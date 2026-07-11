using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Chase Us player movement — KAS-27 (F1): server-authoritative NetworkBehaviour.
///
/// Input-reading and movement-applying are separate stages:
///   * <see cref="GatherInput"/> — local-only. Reads axes/keys plus the orbit
///     camera's yaw (movement is camera-relative, so the server needs the
///     owner's view direction to interpret their stick input).
///   * <see cref="ApplyMovement"/> — the only place the character actually
///     moves. When networked, ONLY the server runs it; position/rotation reach
///     everyone through the NetworkTransform on the same object.
///
/// Wire protocol: the owning client sends its <see cref="MoveInput"/> every
/// frame via <see cref="SubmitInputRpc"/> (reliable, owner-only). The server
/// keeps the latest input per player and re-applies it each server frame,
/// consuming the jump edge one-shot so a single press can't fire twice. Move
/// vectors are re-clamped server-side — client-supplied data is not trusted,
/// consistent with the host-authority stance in NetworkBootstrap.
///
/// Offline: an instance with no NetworkObject (the scene's Player_Character,
/// or any non-networked test scene) never spawns, and runs gather+apply
/// locally every frame — behavior identical to the pre-KAS-27 MonoBehaviour.
///
/// Animation: the server computes the blend speed and replicates it through a
/// NetworkVariable; every instance (including non-owners) feeds it to its own
/// Animator. Offline instances set the Animator directly.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement Speeds")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float gravity = -20f;

    [Header("Jump")]
    [Tooltip("How high the character jumps. Higher = bigger jump arc.")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;

    [Header("Input")]
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Camera-Relative Movement")]
    [SerializeField] private OrbitCamera orbitCamera;

    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParameterName = "Speed";

    /// <summary>One frame of player intent, sent owner → server.</summary>
    public struct MoveInput : INetworkSerializable
    {
        public Vector2 Move;        // horizontal input, magnitude ≤ 1
        public bool Sprint;
        public bool JumpPressed;    // edge, consumed one-shot by the server
        public float CameraYaw;     // owner's camera yaw for camera-relative movement

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Move);
            serializer.SerializeValue(ref Sprint);
            serializer.SerializeValue(ref JumpPressed);
            serializer.SerializeValue(ref CameraYaw);
        }
    }

    private CharacterController controller;
    private NetworkObject cachedNetworkObject;
    private Vector3 velocity;          // vertical velocity state — server-only when networked
    private MoveInput pendingInput;    // latest owner intent — server-only when networked

    // Server-written; every instance drives its Animator from this when networked.
    private readonly NetworkVariable<float> netAnimSpeed = new NetworkVariable<float>();

    private bool IsNetworkedAndSpawned => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        cachedNetworkObject = GetComponentInParent<NetworkObject>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (orbitCamera == null)
        {
            orbitCamera = FindAnyObjectByType<OrbitCamera>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // The server simulates with the CharacterController; on everyone else the
        // NetworkTransform writes the transform and a live CC would fight it.
        if (!IsServer && controller != null)
        {
            controller.enabled = false;
        }

        // KAS-28 owner guard: ONLY the owning client captures the scene camera rig.
        // Remote players' instances leave the camera alone, so each client sees the
        // world exclusively through their own player.
        if (IsOwner)
        {
            OrbitCamera sceneCamera = FindSceneOrbitCamera();
            if (sceneCamera != null)
            {
                orbitCamera = sceneCamera;
                sceneCamera.SetTarget(transform);
            }
        }
    }

    /// <summary>
    /// The OrbitCamera that actually sits on a Camera — the scene also has an
    /// inert OrbitCamera component on the offline player object, which must
    /// never be picked for the live rig.
    /// </summary>
    private static OrbitCamera FindSceneOrbitCamera()
    {
        foreach (OrbitCamera candidate in FindObjectsByType<OrbitCamera>(FindObjectsInactive.Exclude))
        {
            if (candidate.GetComponent<Camera>() != null)
            {
                return candidate;
            }
        }
        return null;
    }

    public override void OnNetworkDespawn()
    {
        if (controller != null)
        {
            controller.enabled = true;
        }
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // ---- Offline / non-networked: exactly the pre-KAS-27 behavior. ----
        if (!IsNetworkedAndSpawned)
        {
            float animSpeed = ApplyMovement(GatherInput(), Time.deltaTime);
            if (animator != null)
            {
                animator.SetFloat(speedParameterName, animSpeed);
            }
            return;
        }

        // ---- Networked. ----
        if (IsOwner)
        {
            MoveInput input = GatherInput();
            if (IsServer)
            {
                // Host shortcut: no RPC to ourselves. Preserve an unconsumed jump edge.
                input.JumpPressed |= pendingInput.JumpPressed;
                pendingInput = input;
            }
            else
            {
                SubmitInputRpc(input);
            }
        }

        if (IsServer)
        {
            netAnimSpeed.Value = ApplyMovement(pendingInput, Time.deltaTime);
            pendingInput.JumpPressed = false; // jump is an edge — never re-apply it
        }

        if (animator != null)
        {
            animator.SetFloat(speedParameterName, netAnimSpeed.Value);
        }
    }

    // ---------- Input stage (local machine only) ----------

    private MoveInput GatherInput()
    {
        Vector2 move = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        move = Vector2.ClampMagnitude(move, 1f);

        return new MoveInput
        {
            Move = move,
            Sprint = Input.GetKey(sprintKey),
            JumpPressed = Input.GetKeyDown(jumpKey),
            CameraYaw = orbitCamera != null ? orbitCamera.Yaw : 0f
        };
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitInputRpc(MoveInput input)
    {
        // Latest-wins, except an unconsumed jump edge survives being overwritten
        // by a later frame's input that arrived before the server ticked.
        input.JumpPressed |= pendingInput.JumpPressed;
        pendingInput = input;
    }

    // ---------- Movement stage (server when networked, local when offline) ----------

    /// <summary>
    /// Applies one frame of movement to the CharacterController and returns the
    /// animator blend speed (0 idle / 1 walk / 2 run) for the caller to route.
    /// </summary>
    private float ApplyMovement(MoveInput input, float deltaTime)
    {
        if (controller == null || !controller.enabled)
        {
            return 0f;
        }

        // Never trust a remote magnitude — clamp again on the authority.
        Vector2 clamped = Vector2.ClampMagnitude(input.Move, 1f);
        Vector3 rawInput = new Vector3(clamped.x, 0f, clamped.y);

        // --- Camera-relative direction (yaw supplied by the owning client) ---
        Vector3 moveDirection = rawInput;
        if (rawInput.sqrMagnitude > 0.001f)
        {
            Quaternion cameraYaw = Quaternion.Euler(0f, input.CameraYaw, 0f);
            moveDirection = cameraYaw * rawInput;
        }

        // --- Sprint ---
        float currentMoveSpeed = input.Sprint ? runSpeed : walkSpeed;
        Vector3 move = moveDirection * currentMoveSpeed;

        // --- Face movement direction ---
        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * deltaTime);
        }

        // --- Grounded check & gravity reset ---
        if (controller.isGrounded && velocity.y < 0f)
        {
            velocity.y = -2f;
        }

        // --- Jump ---
        // Only when actually grounded, to prevent air-jumps.
        // v = sqrt(-2 * g * h) is the exact initial velocity for height h.
        if (controller.isGrounded && input.JumpPressed)
        {
            velocity.y = Mathf.Sqrt(-2f * gravity * jumpHeight);
        }

        // --- Apply gravity every frame ---
        velocity.y += gravity * deltaTime;

        // --- Combine horizontal move + vertical velocity, apply ---
        Vector3 finalMove = (move + Vector3.up * velocity.y) * deltaTime;
        controller.Move(finalMove);

        // --- Animator blend speed ---
        if (rawInput.sqrMagnitude > 0.001f)
        {
            return input.Sprint ? 2f : 1f;
        }
        return 0f;
    }
}
