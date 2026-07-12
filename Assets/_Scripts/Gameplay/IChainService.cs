using Unity.Netcode;

/// <summary>
/// F4's ASSUMED contract with F3 (Chain Formation, Physics Path A). F3 is not
/// in this repository yet — this interface is the single boundary between
/// F4's game rules and F3's joint physics, so when F3 lands its ChainManager
/// implements this (or a thin adapter does) and nothing in F4 changes.
///
/// Per-member provenance against F3's spec:
///  * <see cref="AttachToTail"/>   — squarely F3's stated scope ("new catches
///    attach at the END of the chain, joint created between new catch and
///    current tail"). Only the signature is assumed.
///  * <see cref="FreezeMember"/>   — small addition (expected ~isKinematic on
///    the member's Rigidbody). Needed by F4 disconnect handling.
///  * <see cref="RemoveMember"/>   — ⚠ NOT in F3's stated scope. Requires
///    destroying the member's joints and re-seaming its two neighbours with a
///    new joint. F3 must grow this capability.
///  * <see cref="PromoteToHead"/>  — ⚠ NOT in F3's stated scope. Requires
///    converting a Rigidbody chain member back into the CharacterController-
///    driven head and re-rooting the remaining joints on it. F3 must grow
///    this capability. How the old (disconnected) head's body is handled
///    physically is F3's decision; F4 keeps it in the chain list as a frozen,
///    removable member.
///
/// All methods are server-only; implementations should fail loud if called on
/// a client. Players are identified by the clientId they had when their
/// player object spawned (see CatchDetector.PlayerClientId — ownership of a
/// disconnected player's object reverts to the server, so OwnerClientId is
/// not a stable key).
/// </summary>
public interface IChainService
{
    /// <summary>Joint the freshly caught player to the current chain tail (F3 Path A).</summary>
    void AttachToTail(NetworkObject caughtPlayer);

    /// <summary>Freeze a chain member in place (disconnected member — gap persists).</summary>
    void FreezeMember(ulong playerClientId);

    /// <summary>
    /// Remove a member from the physical chain and re-seam its neighbours'
    /// joints to close the gap. F4 has already validated that the member is
    /// disconnected and that the requester is the current Hunter.
    /// </summary>
    void RemoveMember(ulong playerClientId);

    /// <summary>
    /// Convert the given chain member into the new chain head (Hunter):
    /// Rigidbody member → CharacterController-driven head, joints re-rooted.
    /// </summary>
    void PromoteToHead(ulong playerClientId);
}
