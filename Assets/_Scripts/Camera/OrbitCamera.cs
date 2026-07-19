using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The transform the camera orbits around (usually the player).")]
    [SerializeField] private Transform target;

    [Tooltip("Height offset above the target's pivot to look at (e.g. shoulders/head, not feet).")]
    [SerializeField] private float targetHeightOffset = 1.5f;

    [Header("Distance")]
    [SerializeField] private float distance = 6f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivityX = 3f;
    [SerializeField] private float mouseSensitivityY = 2f;

    [Tooltip("How high the camera can pitch upward (looking down at the character from above).")]
    [SerializeField] private float maxPitch = 60f;

    [Tooltip("How low the camera can pitch downward (looking up at character from below).")]
    [SerializeField] private float minPitch = -20f;

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.08f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnStart = true;

    // Current camera orbit angles
    private float yaw = 0f;
    private float pitch = 20f;

    private Vector3 currentVelocity;

    // Public read-only accessor so PlayerMovement can align to camera's yaw
    public float Yaw => yaw;

    /// <summary>
    /// Re-points the camera at a new target — the locally-owned player when a
    /// network session spawns one (KAS-28), the offline player when it ends.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    // Menus own the cursor: pre-session screens (Mode Select, Host/Join Choice,
    // Map Select, Join, LAN) via their own IsVisible flags, the post-session
    // lobby via LobbyRoomUI, and the F4 round-result screen (Play Again).
    private static bool MenuWantsCursor =>
        ModeSelectUI.IsVisible || HostJoinChoiceUI.IsVisible || MapSelectUI.IsVisible ||
        JoinScreenUI.IsVisible || LanConnectUI.IsVisible || LobbyRoomUI.IsVisible ||
        ResultScreenUI.IsVisible || CharacterSelectUI.IsVisible;

    private void Start()
    {
        if (lockCursorOnStart && !MenuWantsCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        // Yield the cursor while any menu screen needs it, otherwise its
        // buttons can never be clicked (the click itself re-locks the cursor).
        if (MenuWantsCursor)
        {
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }

        // Allow the user to unlock cursor with Escape (handy for stopping Play mode too)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        // Clicking back into the game re-locks it
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // --- Read mouse delta and update orbit angles ---
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivityX;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivityY;

        yaw += mouseX;
        pitch -= mouseY; // invert Y so mouse up = look up (feels natural)
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // --- Compute desired camera position from orbit angles ---
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = target.position + Vector3.up * targetHeightOffset;
        Vector3 desiredPosition = focusPoint - rotation * Vector3.forward * distance;

        // --- Smoothly move camera into position ---
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentVelocity,
            positionSmoothTime
        );

        // --- Always look at the focus point ---
        transform.LookAt(focusPoint);
    }
}
