using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
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

    private CharacterController controller;
    private Vector3 velocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (orbitCamera == null)
        {
            orbitCamera = FindObjectOfType<OrbitCamera>();
        }
    }

    private void Update()
    {
        // --- Read directional input ---
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 rawInput = new Vector3(horizontal, 0f, vertical);
        rawInput = Vector3.ClampMagnitude(rawInput, 1f);

        // --- Camera-relative direction ---
        Vector3 moveDirection = rawInput;
        if (orbitCamera != null && rawInput.sqrMagnitude > 0.001f)
        {
            Quaternion cameraYaw = Quaternion.Euler(0f, orbitCamera.Yaw, 0f);
            moveDirection = cameraYaw * rawInput;
        }

        // --- Sprint ---
        bool isSprinting = Input.GetKey(sprintKey);
        float currentMoveSpeed = isSprinting ? runSpeed : walkSpeed;

        Vector3 move = moveDirection * currentMoveSpeed;

        // --- Face movement direction ---
        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // --- Grounded check & gravity reset ---
        if (controller.isGrounded && velocity.y < 0f)
        {
            velocity.y = -2f;
        }

        // --- Jump ---
        // Only allow jumping when actually grounded to prevent air-jumps.
        // Formula: v = sqrt(-2 * g * h) gives the exact initial velocity
        // needed to reach 'jumpHeight' under the current gravity.
        if (controller.isGrounded && Input.GetKeyDown(jumpKey))
        {
            velocity.y = Mathf.Sqrt(-2f * gravity * jumpHeight);
        }

        // --- Apply gravity every frame ---
        velocity.y += gravity * Time.deltaTime;

        // --- Combine horizontal move + vertical velocity, apply ---
        Vector3 finalMove = (move + Vector3.up * velocity.y) * Time.deltaTime;
        controller.Move(finalMove);

        // --- Animator ---
        if (animator != null)
        {
            float animatorSpeed = 0f;
            if (rawInput.sqrMagnitude > 0.001f)
            {
                animatorSpeed = isSprinting ? 2f : 1f;
            }
            animator.SetFloat(speedParameterName, animatorSpeed);
        }
    }
}
