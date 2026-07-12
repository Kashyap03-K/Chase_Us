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

    [Header("Rigidbody locomotion (F3 — Hunter AND chained players, same drive = equal pull strength)")]
    [Tooltip("Acceleration toward desired velocity for a Rigidbody-driven player. Higher = snappier and less rope influence; lower = floatier, more visible tug-of-war.")]
    [SerializeField] private float physicsMoveAccel = 12f;
    [Tooltip("PLACEHOLDER ground probe for Rigidbody jumps (single ray below the capsule center) until real fall/land animations and proper grounding exist.")]
    [SerializeField] private float groundProbeDistance = 1.15f;

    [Header("Auto-recover placeholder (F3 Change 3) — TODO: replace with real get-up animation once available")]
    [Tooltip("transform.up · world-up below this counts as tipped over.")]
    [SerializeField] private float uprightDotThreshold = 0.5f;
    [Tooltip("Seconds a player must stay tipped before snapping back upright.")]
    [SerializeField] private float uprightRecoverDelay = 1.5f;

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
    private ChainLink chainLink;       // optional — present on the networked player prefab (F3)
    private Vector3 velocity;          // vertical velocity state — server-only when networked
    private MoveInput pendingInput;    // latest owner intent — server-only when networked

    private float tippedSinceTime = -1f;   // Change 3 recovery timer — server-only

    private bool IsCaughtInChain => chainLink != null && chainLink.IsCaught.Value;

    // True once ChainManager has made this player's body dynamic (the Hunter from
    // registration, everyone else from the moment they're caught). Server-side
    // only — client bodies stay kinematic forever.
    private bool IsPhysicsDriven => chainLink != null && chainLink.Body != null && !chainLink.Body.isKinematic;

    // Server-written; every instance drives its Animator from this when networked.
    private readonly NetworkVariable<float> netAnimSpeed = new NetworkVariable<float>();

    private bool IsNetworkedAndSpawned => cachedNetworkObject != null && cachedNetworkObject.IsSpawned;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        cachedNetworkObject = GetComponentInParent<NetworkObject>();
        chainLink = GetComponent<ChainLink>();

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
            if (IsCaughtInChain)
            {
                // F3: chained players are Rigidbody+joint-driven — forces are applied
                // in FixedUpdate (jump edge consumed there too). Anim blend from intent.
                Vector2 chainMove = Vector2.ClampMagnitude(pendingInput.Move, 1f);
                netAnimSpeed.Value = chainMove.sqrMagnitude > 0.001f ? (pendingInput.Sprint ? 2f : 1f) : 0f;
            }
            else if (IsPhysicsDriven)
            {
                // F3 Change 1 (Hunter): motion is applied in FixedUpdate so joint
                // tension resolves in the same solver step; the jump edge is
                // consumed there too. Anim blend from intent, like the CC path.
                Vector2 move = Vector2.ClampMagnitude(pendingInput.Move, 1f);
                netAnimSpeed.Value = move.sqrMagnitude > 0.001f ? (pendingInput.Sprint ? 2f : 1f) : 0f;
            }
            else
            {
                // F1 path, unchanged — except the hunter's chain-length speed penalty
                // factor (1 for everyone who isn't the chain head).
                float speedMultiplier = chainLink != null ? chainLink.SpeedMultiplier.Value : 1f;
                netAnimSpeed.Value = ApplyMovement(pendingInput, Time.deltaTime, speedMultiplier);
                pendingInput.JumpPressed = false; // jump is an edge — never re-apply it
            }
        }

        if (animator != null)
        {
            animator.SetFloat(speedParameterName, netAnimSpeed.Value);
        }
    }

    private void FixedUpdate()
    {
        // All Rigidbody-based movement is server-only, like the CC path.
        if (!IsNetworkedAndSpawned || !IsServer || chainLink == null) return;

        Rigidbody body = chainLink.Body;
        if (body == null || body.isKinematic) return;

        // PLACEHOLDER physics parity: CC-driven players fall under the serialized
        // `gravity` (-20) while PhysX applies Physics.gravity (-9.81) to Rigidbody
        // players. Apply the difference so every player — CC or RB, whoever they
        // are — shares identical fall speed and jump arc. Remove if/when gravity
        // is unified project-wide.
        body.AddForce(Vector3.up * (gravity - Physics.gravity.y), ForceMode.Acceleration);

        if (IsCaughtInChain)
        {
            ApplyCaughtTug(body);
        }
        else
        {
            ApplyPhysicsLocomotion(body);
        }

        AutoRecoverUpright(body);
    }

    /// <summary>
    /// F3: a caught player's own input drives their chained body with the SAME
    /// velocity-seeking model as the Hunter's locomotion — identical strength
    /// on both ends of the rope, so tug-of-war is symmetric (host or not).
    /// The joints alone provide the tension/constraint. Jump works too (same
    /// placeholder ground probe) — the rope simply yanks back mid-air.
    /// </summary>
    private void ApplyCaughtTug(Rigidbody body)
    {
        Vector2 clamped = Vector2.ClampMagnitude(pendingInput.Move, 1f);
        Vector3 rawInput = new Vector3(clamped.x, 0f, clamped.y);

        if (rawInput.sqrMagnitude > 0.001f)
        {
            // Same camera-relative interpretation as the CC path.
            Vector3 moveDirection = Quaternion.Euler(0f, pendingInput.CameraYaw, 0f) * rawInput;

            // Same drive as ApplyPhysicsLocomotion: accelerate toward the
            // desired velocity. Equal walk/run targets + equal accel constant
            // = equal pulling strength against the Hunter's.
            float targetSpeed = pendingInput.Sprint ? runSpeed : walkSpeed;
            Vector3 desiredVelocity = moveDirection * targetSpeed;
            Vector3 velocity = body.linearVelocity;
            Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            body.AddForce((desiredVelocity - horizontalVelocity) * physicsMoveAccel, ForceMode.Acceleration);

            // Face where they're pulling. MoveRotation lets the joint's angular
            // limits push back instead of being overwritten.
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            body.MoveRotation(Quaternion.Slerp(body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }

        // PLACEHOLDER grounding, same as the Hunter's — replace when real
        // fall/land animations exist. Jump velocity uses the same `gravity`
        // as the CC path (the parity force in FixedUpdate makes it real),
        // so chained, Hunter and free players all jump identically.
        if (pendingInput.JumpPressed &&
            Physics.Raycast(body.position + Vector3.up, Vector3.down, groundProbeDistance))
        {
            Vector3 v = body.linearVelocity;
            v.y = Mathf.Sqrt(-2f * gravity * jumpHeight);
            body.linearVelocity = v;
        }
        pendingInput.JumpPressed = false; // consumed here, not in Update, for this path
    }

    /// <summary>
    /// F3 Change 1: Rigidbody locomotion for the physics-driven Hunter. The body
    /// is accelerated TOWARD the desired velocity rather than having its velocity
    /// assigned — that's what makes tug-of-war real: joint tension from caught
    /// runners adds opposing force in the same solver step, so a pulling chain
    /// visibly slows or stalls the Hunter instead of being overwritten.
    /// Camera-relative direction math is identical to the CC path.
    /// </summary>
    private void ApplyPhysicsLocomotion(Rigidbody body)
    {
        Vector2 clamped = Vector2.ClampMagnitude(pendingInput.Move, 1f);
        Vector3 rawInput = new Vector3(clamped.x, 0f, clamped.y);

        Vector3 moveDirection = rawInput;
        if (rawInput.sqrMagnitude > 0.001f)
        {
            moveDirection = Quaternion.Euler(0f, pendingInput.CameraYaw, 0f) * rawInput;
        }

        // Chain-length speed penalty still applies on top of the emergent drag.
        float speedMultiplier = chainLink.SpeedMultiplier.Value;
        float targetSpeed = (pendingInput.Sprint ? runSpeed : walkSpeed) * speedMultiplier;
        Vector3 desiredVelocity = moveDirection * targetSpeed;

        Vector3 velocity = body.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        body.AddForce((desiredVelocity - horizontalVelocity) * physicsMoveAccel, ForceMode.Acceleration);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            body.MoveRotation(Quaternion.Slerp(body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }

        // PLACEHOLDER jump/grounding until real fall/land animations exist: a
        // single ray from the capsule center to just below the feet. Jump
        // velocity uses the same `gravity` as the CC path — the parity force
        // in FixedUpdate makes the arcs identical for every player.
        if (pendingInput.JumpPressed &&
            Physics.Raycast(body.position + Vector3.up, Vector3.down, groundProbeDistance))
        {
            Vector3 v = body.linearVelocity;
            v.y = Mathf.Sqrt(-2f * gravity * jumpHeight);
            body.linearVelocity = v;
        }
        pendingInput.JumpPressed = false; // consumed here, not in Update, for this path
    }

    /// <summary>
    /// F3 Change 3 — PLACEHOLDER auto-recover. TODO: replace with a real get-up
    /// animation once fall/land animations exist. Change 2's freeze-X/Z
    /// constraints should prevent toppling entirely; this is the safety net for
    /// anything that still slips through (collisions, joint snap-back, etc.).
    /// </summary>
    private void AutoRecoverUpright(Rigidbody body)
    {
        if (Vector3.Dot(transform.up, Vector3.up) >= uprightDotThreshold)
        {
            tippedSinceTime = -1f;
            return;
        }

        if (tippedSinceTime < 0f)
        {
            tippedSinceTime = Time.time;
            return;
        }

        if (Time.time - tippedSinceTime < uprightRecoverDelay) return;

        tippedSinceTime = -1f;
        body.rotation = Quaternion.Euler(0f, body.rotation.eulerAngles.y, 0f);
        body.angularVelocity = Vector3.zero;
        Debug.Log($"[PlayerMovement] Auto-recovered client {OwnerClientId}'s player to standing (placeholder — no get-up animation yet).");
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
    /// <paramref name="speedMultiplier"/> is the F3 hunter chain penalty (1 = none).
    /// </summary>
    private float ApplyMovement(MoveInput input, float deltaTime, float speedMultiplier = 1f)
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

        // --- Sprint (scaled by the chain penalty when this player heads a chain) ---
        float currentMoveSpeed = (input.Sprint ? runSpeed : walkSpeed) * speedMultiplier;
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
