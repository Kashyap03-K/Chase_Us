using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// F3 — chain formation mechanics (physics-based, Path A). Server-only logic;
/// lives on _Bootstrap next to PlayerSpawnManager.
///
/// The chain is a list of players: index 0 is the Hunter (head), every later
/// entry is jointed to the one before it. Catching is proximity-based and
/// checked for EVERY chain member, not just the Hunter — any link touching an
/// uncaught runner appends that runner to the chain END (new catches always
/// attach at the tail, per spec).
///
/// Architecture switch on catch: the runner's CharacterController turns off,
/// its (until now kinematic) Rigidbody turns dynamic, its CapsuleCollider
/// turns on, and a ConfigurableJoint ties it to the previous tail. The Hunter
/// keeps its F1/F2 CharacterController pipeline untouched — its kinematic
/// Rigidbody merely serves as a moving joint anchor for the first caught link.
///
/// Turning is fully emergent from the joint spring/damper physics — there is
/// deliberately no scripted follow/turn logic here.
///
/// All physics/penalty numbers are inspector-tunable placeholders per the
/// design doc — expect playtest iteration.
///
/// Pre-KAS-10 shims (both flagged in the F3 report):
///   * Hunter = the host's player, assigned when the host connects. Role
///     assignment replaces this in KAS-10.
///   * Catching is gated on the host's lobby being dismissed (Start Game),
///     since there is no round state machine yet.
/// </summary>
public class ChainManager : MonoBehaviour
{
    [Header("Catch")]
    [SerializeField, Tooltip("A chain member catches an uncaught runner when their pivots come within this distance (server-side check).")]
    private float catchRadius = 1.2f;

    [Header("Joint (design-doc placeholders — tune in playtests)")]
    [SerializeField, Tooltip("Max stretch before the link goes taut (m).")]
    private float linearLimit = 1.5f;
    [SerializeField] private float positionSpring = 200f;
    [SerializeField] private float positionDamper = 20f;
    [SerializeField, Tooltip("± swing around the two perpendicular axes (deg).")]
    private float angularSwingLimit = 45f;
    [SerializeField, Tooltip("± twist around the joint's primary axis (deg).")]
    private float angularTwistLimit = 30f;
    [SerializeField, Tooltip("Body-local anchor for both joint ends — roughly hand height.")]
    private Vector3 jointAnchor = new Vector3(0f, 1f, 0f);
    [SerializeField, Tooltip("Let chained bodies collide with each other instead of ghosting through.")]
    private bool jointEnableCollision = true;

    [Header("Chained body (uniform per link — length penalty handles the rest)")]
    [SerializeField] private float linkMass = 1f;
    [SerializeField] private float linkLinearDrag = 1f;
    [SerializeField] private float linkAngularDrag = 5f;

    [Header("Hunter speed penalty")]
    [SerializeField, Tooltip("Speed lost per chained link: speed *= 1 - penalty * links.")]
    private float linkPenalty = 0.05f;
    [SerializeField, Tooltip("Floor as a fraction of base speed — the Hunter never gets slower than this.")]
    private float minSpeedFraction = 0.4f;

    /// <summary>[0] = Hunter. Server-only.</summary>
    private readonly List<ChainLink> chain = new List<ChainLink>();
    private readonly List<ChainLink> uncaughtRunners = new List<ChainLink>();
    private bool subscribed;

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[ChainManager] No NetworkManager in scene — chain mechanics disabled.");
            return;
        }

        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        NetworkManager.Singleton.OnServerStopped += HandleServerStopped;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (!subscribed || NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
    }

    // ---------- Roster ----------

    private void HandleServerStarted()
    {
        chain.Clear();
        uncaughtRunners.Clear();
    }

    private void HandleClientConnected(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
        {
            return; // PlayerSpawnManager already warns about missing player objects
        }

        ChainLink link = client.PlayerObject.GetComponent<ChainLink>();
        if (link == null)
        {
            Debug.LogWarning($"[ChainManager] Player of client {clientId} has no ChainLink component — excluded from chain mechanics.");
            return;
        }

        // TODO(KAS-10): replace with real role assignment. Until the round state
        // machine exists, the host's player is the Hunter / chain head.
        if (clientId == NetworkManager.ServerClientId)
        {
            chain.Insert(0, link);
            Debug.Log("[ChainManager] Hunter (chain head) registered: host player (pre-KAS-10 placeholder role).");
        }
        else
        {
            uncaughtRunners.Add(link);
            Debug.Log($"[ChainManager] Runner registered (client {clientId}). Uncaught runners: {uncaughtRunners.Count}");
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // Their player object is about to despawn — drop it from our lists and
        // splice the chain around the hole if they were mid-chain.
        uncaughtRunners.RemoveAll(l => l == null || l.OwnerClientId == clientId);

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i] != null && chain[i].OwnerClientId != clientId) continue;

            ChainLink leaving = chain[i];
            chain.RemoveAt(i);

            // Re-joint the member that followed the leaver (if any) to the
            // member ahead of the leaver, keeping the chain contiguous.
            if (i > 0 && i < chain.Count && chain[i] != null && chain[i - 1] != null)
            {
                ChainLink follower = chain[i];
                if (follower.JointToAhead != null) Destroy(follower.JointToAhead);
                follower.JointToAhead = CreateJoint(follower, chain[i - 1]);
                follower.Ahead.Value = chain[i - 1].NetworkObject;
                Debug.Log($"[ChainManager] Spliced chain around departed client {clientId}.");
            }

            if (leaving != null && leaving.JointToAhead != null) Destroy(leaving.JointToAhead);
        }

        UpdateHunterSpeedMultiplier();
    }

    private void HandleServerStopped(bool wasHost)
    {
        chain.Clear();
        uncaughtRunners.Clear();
    }

    // ---------- Catch loop (server) ----------

    private void FixedUpdate()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !nm.IsListening) return;
        if (chain.Count == 0 || uncaughtRunners.Count == 0) return;

        // Pre-KAS-10 gate: no catching while the (host-local) lobby is still up.
        if (LobbyRoomUI.IsVisible) return;

        float catchSqr = catchRadius * catchRadius;

        // Every chain member — head AND links — can catch independently.
        for (int m = 0; m < chain.Count; m++)
        {
            ChainLink member = chain[m];
            if (member == null) continue;

            for (int r = uncaughtRunners.Count - 1; r >= 0; r--)
            {
                ChainLink runner = uncaughtRunners[r];
                if (runner == null)
                {
                    uncaughtRunners.RemoveAt(r);
                    continue;
                }

                if ((member.transform.position - runner.transform.position).sqrMagnitude <= catchSqr)
                {
                    Catch(runner, caughtBy: member);
                }
            }
        }
    }

    /// <summary>
    /// Appends <paramref name="runner"/> to the END of the chain (regardless of
    /// which member touched them) and performs the CC → Rigidbody+joint switch.
    /// </summary>
    private void Catch(ChainLink runner, ChainLink caughtBy)
    {
        uncaughtRunners.Remove(runner);

        ChainLink tail = chain[chain.Count - 1];
        chain.Add(runner);

        // --- Architecture switch: CharacterController-driven → Rigidbody+joint-driven ---
        CharacterController cc = runner.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        if (runner.ChainCollider != null) runner.ChainCollider.enabled = true;

        Rigidbody body = runner.Body;
        if (body != null)
        {
            body.isKinematic = false;
            body.mass = linkMass;
            body.linearDamping = linkLinearDrag;
            body.angularDamping = linkAngularDrag;
        }

        runner.JointToAhead = CreateJoint(runner, tail);

        // --- Replicate: caught flag + who they hang off (drives the rope visual) ---
        runner.IsCaught.Value = true;
        runner.Ahead.Value = tail.NetworkObject;

        UpdateHunterSpeedMultiplier();

        Debug.Log($"[ChainManager] CAUGHT: client {runner.OwnerClientId} (touched by client {caughtBy.OwnerClientId}) " +
                  $"attached behind client {tail.OwnerClientId}. Chain length: {chain.Count - 1} link(s). " +
                  $"Uncaught runners left: {uncaughtRunners.Count}");
    }

    private ConfigurableJoint CreateJoint(ChainLink from, ChainLink to)
    {
        ConfigurableJoint joint = from.gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = to.Body; // hunter's body is kinematic: a moving anchor, unaffected by chain forces
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = jointAnchor;
        joint.connectedAnchor = jointAnchor;
        joint.enableCollision = jointEnableCollision;

        joint.xMotion = ConfigurableJointMotion.Limited;
        joint.yMotion = ConfigurableJointMotion.Limited;
        joint.zMotion = ConfigurableJointMotion.Limited;
        joint.linearLimit = new SoftJointLimit { limit = linearLimit };
        joint.linearLimitSpring = new SoftJointLimitSpring { spring = positionSpring, damper = positionDamper };

        joint.angularXMotion = ConfigurableJointMotion.Limited; // twist axis
        joint.angularYMotion = ConfigurableJointMotion.Limited; // swing
        joint.angularZMotion = ConfigurableJointMotion.Limited; // swing
        joint.lowAngularXLimit = new SoftJointLimit { limit = -angularTwistLimit };
        joint.highAngularXLimit = new SoftJointLimit { limit = angularTwistLimit };
        joint.angularYLimit = new SoftJointLimit { limit = angularSwingLimit };
        joint.angularZLimit = new SoftJointLimit { limit = angularSwingLimit };

        return joint;
    }

    private void UpdateHunterSpeedMultiplier()
    {
        if (chain.Count == 0 || chain[0] == null) return;
        int links = chain.Count - 1;
        chain[0].SpeedMultiplier.Value = Mathf.Max(minSpeedFraction, 1f - linkPenalty * links);
    }
}
