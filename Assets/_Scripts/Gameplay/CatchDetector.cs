using Unity.Netcode;
using UnityEngine;

/// <summary>
/// F4 catch sensing — one per player prefab. Server-only trigger volume that
/// reports every player-player contact to <see cref="GameRoundManager"/>,
/// which decides whether it constitutes a catch (chain side vs uncaught
/// runner). Reporting is direction-agnostic and fires on Stay as well as
/// Enter because Rigidbody chain members (F3) move less deterministically
/// than the Hunter's CharacterController — missing a single Enter must not
/// mean missing the catch (D1 risk, flagged for playtesting).
///
/// Physics note: two CharacterControllers generate no trigger events between
/// them, so the server adds a kinematic Rigidbody (events only — movement
/// stays CharacterController/NetworkTransform-driven) plus a trigger sphere.
/// Kinematic-trigger vs kinematic-trigger pairs DO raise OnTrigger messages.
/// When F3 converts chain members to real Rigidbodies, the kinematic add is
/// skipped (a Rigidbody is already present) and only the trigger is added —
/// radius/height will need re-tuning then.
/// </summary>
public class CatchDetector : NetworkBehaviour
{
    [Header("Catch volume (server-side; playtest item)")]
    [Tooltip("Radius of the catch trigger sphere around the player.")]
    [SerializeField] private float catchRadius = 0.9f;
    [Tooltip("Height of the trigger sphere's centre above the player pivot (pivot is at the feet).")]
    [SerializeField] private float catchCenterHeight = 1f;

    /// <summary>
    /// The clientId this player object belongs to, captured at spawn. Stable
    /// through disconnects — OwnerClientId is NOT (ownership reverts to the
    /// server when the owner leaves), and frozen players must stay identifiable
    /// to remain catchable.
    /// </summary>
    public ulong PlayerClientId { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        PlayerClientId = OwnerClientId;

        if (IsServer)
        {
            EnsureServerPhysicsSetup();
        }
    }

    private void EnsureServerPhysicsSetup()
    {
        if (GetComponent<Rigidbody>() == null)
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        SphereCollider trigger = gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = catchRadius;
        trigger.center = new Vector3(0f, catchCenterHeight, 0f);
    }

    private void OnTriggerEnter(Collider other) => Report(other);

    private void OnTriggerStay(Collider other) => Report(other);

    private void Report(Collider other)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        GameRoundManager manager = GameRoundManager.Instance;
        if (manager == null)
        {
            return;
        }

        CatchDetector otherDetector = other.GetComponentInParent<CatchDetector>();
        if (otherDetector == null || otherDetector == this)
        {
            return;
        }

        // Direction is resolved by the manager against its authoritative
        // chain/runner sets — this component only states "these two touched".
        manager.ReportContact(PlayerClientId, otherDetector.PlayerClientId);
    }
}
