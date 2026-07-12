using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// F3 — the PHYSICAL side of the chain (Path A), driven entirely by F4's
/// GameRoundManager through <see cref="IChainService"/>.
///
/// Division of labour after the F3+F4 integration:
///   * F4 owns every catch DECISION (CatchDetector → ReportContact → OnCatch)
///     and all game state: uncaughtRunners, chainMembers, Hunter selection,
///     win condition, disconnect policy. F3's original standalone catch loop,
///     roster tracking, host-as-Hunter placeholder and disconnect auto-splice
///     were removed here — they would have raced F4's single source of truth.
///   * This class only builds and reshapes joints when told to:
///       - round start (Phase → Active): converts F4's chosen Hunter to the
///         Rigidbody-driven head (bidirectional tug-of-war, F3 Change 1);
///       - AttachToTail: CC → Rigidbody switch + ConfigurableJoint to the tail;
///       - FreezeMember: kinematic pin — the chain stays tethered to the
///         frozen body, so the gap persists exactly as F4's D2 rule wants;
///       - RemoveMember: destroy the member's joints and re-seam neighbours;
///       - PromoteToHead: re-root the chain on the promoted member (an
///         RB→RB role swap — the Change-1 Hunter is already Rigidbody-driven,
///         so no CharacterController is involved);
///       - round reset (Phase → Idle): destroy all joints, restore everyone
///         to free CharacterController-driven bodies for the next round.
///
/// Turning remains fully emergent from the joint spring/damper physics.
/// All joint/penalty numbers are inspector-tunable placeholders.
/// </summary>
public class ChainManager : MonoBehaviour, IChainService
{
    [Header("Joint (design-doc placeholders — tune in playtests)")]
    [SerializeField, Tooltip("Taut distance between chain members (m). 0 = auto: derive character width from the capsule collider (radius × 2). Note: below the combined capsule radii, jointEnableCollision must be OFF or the colliders fight the joint.")]
    private float restDistanceOverride = 0.8f;
    [SerializeField] private float positionSpring = 600f;
    [SerializeField] private float positionDamper = 30f;
    [SerializeField, Tooltip("± swing around the two perpendicular axes (deg).")]
    private float angularSwingLimit = 45f;
    [SerializeField, Tooltip("± twist around the joint's primary axis (deg).")]
    private float angularTwistLimit = 30f;
    [SerializeField, Tooltip("Body-local anchor for both joint ends — roughly hand height.")]
    private Vector3 jointAnchor = new Vector3(0f, 1f, 0f);
    [SerializeField, Tooltip("Collision between the two JOINTED bodies only (non-adjacent members always collide). Must stay OFF while the rest distance is below the combined capsule radii (~1.0m), or the contact solver fights the joint.")]
    private bool jointEnableCollision = false;
    [SerializeField, Tooltip("Auto rest distance = capsule width × this. Slightly above 1 keeps taut members from resting in permanent collider contact (contact solver fighting the joint = jitter).")]
    private float restWidthFactor = 1.15f;

    [Header("Chained body (uniform per link — length penalty handles the rest)")]
    [SerializeField] private float linkMass = 1f;
    [SerializeField] private float linkLinearDrag = 1f;
    [SerializeField] private float linkAngularDrag = 5f;

    [Header("Hunter speed penalty")]
    [SerializeField, Tooltip("Speed lost per chained link: speed *= 1 - penalty * links.")]
    private float linkPenalty = 0.05f;
    [SerializeField, Tooltip("Floor as a fraction of base speed — the Hunter never gets slower than this.")]
    private float minSpeedFraction = 0.4f;

    /// <summary>Physical chain order, [0] = head/Hunter. Server-only. Mirrors F4's Hunter + chainMembers.</summary>
    private readonly List<ChainLink> chain = new List<ChainLink>();

    private GameRoundManager boundRoundManager;
    private bool serverStopHooked;
    private float resolvedRestDistance = -1f;

    private void Start()
    {
        BindRoundManager();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStopped += HandleServerStopped;
            serverStopHooked = true;
        }
    }

    private void OnDestroy()
    {
        if (boundRoundManager != null)
        {
            boundRoundManager.Phase.OnValueChanged -= HandlePhaseChanged;
        }
        if (serverStopHooked && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
        }
    }

    private void BindRoundManager()
    {
        if (boundRoundManager != null) return;
        boundRoundManager = GameRoundManager.Instance;
        if (boundRoundManager == null)
        {
            Debug.LogWarning("[ChainManager] No GameRoundManager in the scene — chain physics will never be driven.");
            return;
        }
        boundRoundManager.Phase.OnValueChanged += HandlePhaseChanged;
    }

    // ---------- Round lifecycle hooks (server-side reactions to F4 state) ----------

    private void HandlePhaseChanged(GameRoundManager.RoundPhase previous, GameRoundManager.RoundPhase current)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        switch (current)
        {
            // Play Again / round reset: F4 fires this BEFORE the next round's
            // StartRound teleports players, so joints are gone before any
            // teleport can yank them.
            case GameRoundManager.RoundPhase.Idle:
                ResetChainPhysics();
                break;

            // Round started: F4 has already picked the Hunter and re-placed
            // everyone; root the (currently empty) chain on the Hunter.
            case GameRoundManager.RoundPhase.Active:
                InitializeHead(boundRoundManager.HunterClientId.Value);
                break;
        }
    }

    private void HandleServerStopped(bool wasHost)
    {
        chain.Clear(); // player objects are being destroyed with the session
    }

    private void InitializeHead(ulong hunterClientId)
    {
        // Mid-round Endgame→Active never happens; Active only follows Idle, so
        // the chain is empty here (reset either never populated it or cleared it).
        chain.Clear();

        ChainLink head = FindLink(hunterClientId);
        if (head == null)
        {
            Debug.LogError($"[ChainManager] Round started but Hunter (client {hunterClientId}) has no ChainLink — chain physics disabled this round.");
            return;
        }

        // F3 Change 1: the Hunter is Rigidbody-driven from the start — same
        // physics family as chain members, so joint tension genuinely pulls
        // back on it (bidirectional tug-of-war).
        ConvertToPhysicsBody(head);
        head.SpeedMultiplier.Value = 1f;
        chain.Add(head);
        Debug.Log($"[ChainManager] Chain rooted on Hunter (client {hunterClientId}), Rigidbody-driven.");
    }

    /// <summary>Destroys all joints and restores every player to a free CC-driven body.</summary>
    private void ResetChainPhysics()
    {
        foreach (ChainLink link in ChainLink.All)
        {
            if (link == null) continue;

            if (link.JointToAhead != null)
            {
                Destroy(link.JointToAhead);
                link.JointToAhead = null;
            }
            RestoreToFreeBody(link);
        }
        chain.Clear();
        Debug.Log("[ChainManager] Chain physics reset — all joints destroyed, players restored to free bodies.");
    }

    // ---------- IChainService (called by F4's GameRoundManager, server-only) ----------

    public void AttachToTail(NetworkObject caughtPlayer)
    {
        if (!ServerGuard(nameof(AttachToTail))) return;

        ChainLink link = caughtPlayer != null ? caughtPlayer.GetComponent<ChainLink>() : null;
        if (link == null)
        {
            Debug.LogError("[ChainManager] AttachToTail: caught player has no ChainLink component.");
            return;
        }
        if (chain.Count == 0)
        {
            Debug.LogError("[ChainManager] AttachToTail called with no chain head — was the round started?");
            return;
        }
        if (chain.Contains(link)) return; // defensive dedupe; F4's OnCatch already guards

        ChainLink tail = chain[chain.Count - 1];

        ConvertToPhysicsBody(link);
        link.JointToAhead = CreateJoint(link, tail);
        link.IsCaught.Value = true;
        link.Ahead.Value = tail.NetworkObject;
        chain.Add(link);

        UpdateHunterSpeedMultiplier();
        Debug.Log($"[ChainManager] ATTACHED client {link.PlayerClientId} behind client {tail.PlayerClientId}. " +
                  $"Chain length: {chain.Count - 1} link(s).");
    }

    public void FreezeMember(ulong playerClientId)
    {
        if (!ServerGuard(nameof(FreezeMember))) return;

        ChainLink link = FindLink(playerClientId);
        if (link == null)
        {
            Debug.LogWarning($"[ChainManager] FreezeMember: no ChainLink for client {playerClientId}.");
            return;
        }

        FreezeBody(link);
        // Joints to/from a kinematic body stay valid — it becomes a fixed
        // anchor. The member behind remains tethered to it, so the chain is
        // pinned at the frozen body until the Hunter removes it (gap persists
        // by design, no dead-joint instability).
        Debug.Log($"[ChainManager] FROZE chain member (client {playerClientId}) in place.");
    }

    public void RemoveMember(ulong playerClientId)
    {
        if (!ServerGuard(nameof(RemoveMember))) return;

        ChainLink link = FindLink(playerClientId);
        int index = link != null ? chain.IndexOf(link) : -1;
        if (index < 0)
        {
            Debug.LogWarning($"[ChainManager] RemoveMember: client {playerClientId} is not in the physical chain.");
            return;
        }
        if (index == 0)
        {
            Debug.LogError("[ChainManager] RemoveMember: refusing to remove the chain head — promote a new head first.");
            return;
        }

        ChainLink ahead = chain[index - 1];
        ChainLink behind = index + 1 < chain.Count ? chain[index + 1] : null;

        if (link.JointToAhead != null)
        {
            Destroy(link.JointToAhead);
            link.JointToAhead = null;
        }

        // Re-seam: the member behind the hole is jointed directly to the member
        // ahead of it (which may be the Hunter — that IS the head-adjacent case).
        if (behind != null)
        {
            if (behind.JointToAhead != null)
            {
                Destroy(behind.JointToAhead);
            }
            behind.JointToAhead = CreateJoint(behind, ahead);
            behind.Ahead.Value = ahead.NetworkObject; // re-point the rope visual
        }

        link.IsCaught.Value = false; // F4 despawns the body right after this returns
        link.Ahead.Value = default;
        chain.RemoveAt(index);

        UpdateHunterSpeedMultiplier();
        Debug.Log($"[ChainManager] REMOVED client {playerClientId} from the chain " +
                  (behind != null
                      ? $"and re-seamed client {behind.PlayerClientId} onto client {ahead.PlayerClientId}."
                      : "(was the tail — no re-seam needed)."));
    }

    public void PromoteToHead(ulong playerClientId)
    {
        if (!ServerGuard(nameof(PromoteToHead))) return;

        ChainLink newHead = FindLink(playerClientId);
        int index = newHead != null ? chain.IndexOf(newHead) : -1;
        if (index < 0)
        {
            Debug.LogError($"[ChainManager] PromoteToHead: client {playerClientId} is not in the physical chain.");
            return;
        }
        if (index == 0)
        {
            return; // already the head — nothing physical to do
        }

        ChainLink oldHead = chain[0];

        // 1. Cut the promoted member loose from whoever it hung off. Because it
        //    stays Rigidbody-driven (the Change-1 Hunter is RB too — confirmed:
        //    this is an RB→RB role swap, no CharacterController re-enable),
        //    flipping IsCaught off is all PlayerMovement needs to start routing
        //    the owner's input through the Hunter locomotion path.
        if (newHead.JointToAhead != null)
        {
            Destroy(newHead.JointToAhead);
            newHead.JointToAhead = null;
        }
        newHead.IsCaught.Value = false;
        newHead.Ahead.Value = default;

        // 2. The old (disconnected) Hunter's body re-seams into the chain at the
        //    head-most member position: frozen, hanging off the new head,
        //    removable later via RemoveMember — mirroring F4's chainMembers list.
        FreezeBody(oldHead);
        oldHead.JointToAhead = CreateJoint(oldHead, newHead);
        oldHead.IsCaught.Value = true;
        oldHead.Ahead.Value = newHead.NetworkObject;
        oldHead.SpeedMultiplier.Value = 1f;

        // 3. Reorder to mirror F4: [newHead, oldHead, ...everyone else in catch order].
        //    Members physically jointed to newHead's old position keep their
        //    joints — the one directly behind it now simply hangs off the head.
        //    Members BETWEEN the old head and newHead (only possible when all of
        //    them are disconnected/frozen — promotion picks the earliest-caught
        //    CONNECTED member) keep hanging off the old head as a frozen strand.
        chain.RemoveAt(index);
        chain.RemoveAt(0);
        chain.Insert(0, oldHead);
        chain.Insert(0, newHead);

        UpdateHunterSpeedMultiplier();
        Debug.Log($"[ChainManager] PROMOTED client {playerClientId} to chain head (RB→RB swap); " +
                  $"old Hunter (client {oldHead.PlayerClientId}) re-seamed as a frozen link behind it.");
    }

    // ---------- Body state switches ----------

    /// <summary>
    /// CharacterController-driven → Rigidbody-driven (server only). Uprights the
    /// character first (yaw kept, pitch/roll zeroed) and relies on the frozen
    /// X/Z rotation constraints so joint torque can't topple it (F3 Change 2).
    /// </summary>
    private void ConvertToPhysicsBody(ChainLink link)
    {
        CharacterController cc = link.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        if (link.ChainCollider != null) link.ChainCollider.enabled = true;

        link.transform.rotation = Quaternion.Euler(0f, link.transform.eulerAngles.y, 0f);

        Rigidbody body = link.Body;
        if (body == null) return;

        body.isKinematic = false;
        // Dynamic bodies step at the fixed timestep (50Hz) — without render
        // interpolation the camera sees stepped motion as visible jitter.
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.mass = linkMass;
        body.linearDamping = linkLinearDrag;
        body.angularDamping = linkAngularDrag;
    }

    /// <summary>Back to a free, CharacterController-driven runner (round reset).</summary>
    private void RestoreToFreeBody(ChainLink link)
    {
        FreezeBody(link); // kinematic + zeroed velocities

        if (link.ChainCollider != null) link.ChainCollider.enabled = false;

        CharacterController cc = link.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;

        link.transform.rotation = Quaternion.Euler(0f, link.transform.eulerAngles.y, 0f);

        if (link.IsSpawned)
        {
            link.IsCaught.Value = false;
            link.Ahead.Value = default;
            link.SpeedMultiplier.Value = 1f;
        }
    }

    private static void FreezeBody(ChainLink link)
    {
        Rigidbody body = link.Body;
        if (body == null) return;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        // Kinematic bodies are positioned directly (teleports, NetworkTransform);
        // interpolation would lag them a frame behind those writes.
        body.interpolation = RigidbodyInterpolation.None;
    }

    // ---------- Joint construction ----------

    /// <summary>
    /// Taut distance between chain members: the override if set, otherwise the
    /// character's width read from its capsule collider (radius × 2) — logged
    /// once so playtests know the actual number in use.
    /// </summary>
    private float GetRestDistance(ChainLink sample)
    {
        if (restDistanceOverride > 0f) return restDistanceOverride;
        if (resolvedRestDistance <= 0f)
        {
            float width = sample != null && sample.ChainCollider != null
                ? sample.ChainCollider.radius * 2f
                : 1f;
            // At exactly capsule width the two capsules rest in permanent
            // contact when taut — the small factor gives the contact solver
            // clearance while staying "holding hands" close.
            resolvedRestDistance = width * restWidthFactor;
            Debug.Log($"[ChainManager] Chain rest distance: capsule width {width:0.##} m × {restWidthFactor:0.##} = {resolvedRestDistance:0.##} m.");
        }
        return resolvedRestDistance;
    }

    private ConfigurableJoint CreateJoint(ChainLink from, ChainLink to)
    {
        ConfigurableJoint joint = from.gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = to.Body;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = jointAnchor;
        joint.connectedAnchor = jointAnchor;
        joint.enableCollision = jointEnableCollision;

        joint.xMotion = ConfigurableJointMotion.Limited;
        joint.yMotion = ConfigurableJointMotion.Limited;
        joint.zMotion = ConfigurableJointMotion.Limited;
        joint.linearLimit = new SoftJointLimit { limit = GetRestDistance(from) };
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

    // ---------- Helpers ----------

    private static ChainLink FindLink(ulong playerClientId)
    {
        foreach (ChainLink link in ChainLink.All)
        {
            if (link != null && link.PlayerClientId == playerClientId)
            {
                return link;
            }
        }
        return null;
    }

    private bool ServerGuard(string caller)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer && nm.IsListening)
        {
            BindRoundManager(); // defensive: bind late if Start-order missed it
            return true;
        }
        Debug.LogError($"[ChainManager] {caller} is server-only — refused.");
        return false;
    }

    private void UpdateHunterSpeedMultiplier()
    {
        if (chain.Count == 0 || chain[0] == null) return;
        int links = chain.Count - 1;
        chain[0].SpeedMultiplier.Value = Mathf.Max(minSpeedFraction, 1f - linkPenalty * links);
    }
}
