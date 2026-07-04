using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The transform the camera should follow (usually the player).")]
    [SerializeField] private Transform target;

    [Header("Position")]
    [Tooltip("Offset from the target in world space. Default: behind and above.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 6f, -8f);

    [Header("Smoothing")]
    [Tooltip("How smoothly the camera catches up to the target. Higher = snappier, lower = more delayed.")]
    [SerializeField] private float followSmoothTime = 0.15f;

    [Tooltip("Should the camera also rotate to look at the target?")]
    [SerializeField] private bool lookAtTarget = true;

    private Vector3 currentVelocity;

    private void LateUpdate()
    {
        if (target == null) return;

        // Smoothly move the camera toward the target's position + offset
        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentVelocity,
            followSmoothTime
        );

        // Optionally rotate the camera to look at the target
        if (lookAtTarget)
        {
            transform.LookAt(target.position + Vector3.up * 1f); // aim slightly above ground
        }
    }
}
