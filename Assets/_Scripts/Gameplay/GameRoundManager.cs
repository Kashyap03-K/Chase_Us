using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// F4 — catch bookkeeping, win condition, Hunter selection and disconnect
/// handling. Server-authoritative: every field below the NetworkVariables is
/// server-only state; clients learn the round phase and Hunter through the
/// NetworkVariables and the round result through <see cref="RoundResultRpc"/>.
///
/// Win condition is event-driven: <see cref="uncaughtRunners"/> is the single
/// source of truth, and only two things can end a round — a catch event, or
/// the endgame timer (armed exclusively when exactly one runner remains).
///
/// Physical chain work (joints) is delegated through <see cref="IChainService"/>
/// (assumed F3 surface — see that file). Until F3 lands, catches still update
/// game state and every skipped physical step is logged loudly.
///
/// Lives on an in-scene NetworkObject. <see cref="StartRound"/> is the entry
/// point (server-only); wiring it into the Start Game flow is KAS-10 scope.
/// </summary>
public class GameRoundManager : NetworkBehaviour
{
    /// <summary>Idle doubles as the spec's WaitingForPlayers state.</summary>
    public enum RoundPhase : byte { Idle, Active, Endgame, Ended }
    public enum RoundWinner : byte { None, Chain, LastRunner, NoContest, Runners }

    public static GameRoundManager Instance { get; private set; }

    [Header("Round timers (F4 revision — supersedes D2's 'no timer while >1 runner')")]
    [Tooltip("Overall round countdown, started the moment the round begins. Expiry while 2+ runners are still uncaught = Runners win outright.")]
    [SerializeField] private float roundSeconds = 300f;
    [Tooltip("Once exactly one runner remains this REPLACES the round timer (remaining round time is discarded); expiry means the last runner wins.")]
    [SerializeField] private float endgameSeconds = 60f;

    /// <summary>Current phase — server-written, readable everywhere (UI hooks read this).</summary>
    public NetworkVariable<RoundPhase> Phase = new NetworkVariable<RoundPhase>(RoundPhase.Idle);

    /// <summary>Current Hunter's clientId — server-written. Only meaningful while Phase != Idle.</summary>
    public NetworkVariable<ulong> HunterClientId = new NetworkVariable<ulong>(ulong.MaxValue);

    /// <summary>
    /// NGO ServerTime (seconds) at which the current phase's countdown ends; 0
    /// when no timer is running. Written once per phase change — clients derive
    /// the live mm:ss display locally from the synced clock, so there is no
    /// per-frame replication traffic.
    /// </summary>
    public NetworkVariable<double> PhaseEndsAtServerTime = new NetworkVariable<double>(0d);

    /// <summary>
    /// Outcome of the last finished round. NOTE: do NOT read these from a
    /// Phase.OnValueChanged handler on a client — NetworkVariable deltas apply
    /// in field-declaration order, so Phase updates before these do. UI should
    /// consume the RPC-driven events below instead; these exist for late binds.
    /// </summary>
    public NetworkVariable<RoundWinner> LastWinner = new NetworkVariable<RoundWinner>(RoundWinner.None);
    public NetworkVariable<ulong> LastWinnerClientId = new NetworkVariable<ulong>(ulong.MaxValue);

    /// <summary>
    /// Raised on EVERY peer when a round starts. Payload: hunter clientId,
    /// round number. RPC-payload-driven so there is no NetworkVariable
    /// delta-ordering dependence (see LastWinner note).
    /// </summary>
    public static event System.Action<ulong, int> RoundStarted;

    /// <summary>Raised on EVERY peer with the round outcome. Same payload rationale as <see cref="RoundStarted"/>.</summary>
    public static event System.Action<RoundWinner, ulong> RoundResultReceived;

    // ---------- Server-only state ----------

    /// <summary>Single source of truth for the win condition (spec §2).</summary>
    private readonly HashSet<ulong> uncaughtRunners = new HashSet<ulong>();

    /// <summary>Caught players in catch order (earliest first). Excludes the Hunter.</summary>
    private readonly List<ulong> chainMembers = new List<ulong>();

    /// <summary>Players who disconnected mid-round (frozen, still part of the game).</summary>
    private readonly HashSet<ulong> disconnectedPlayers = new HashSet<ulong>();

    /// <summary>
    /// clientId → player object, captured at round start. Needed because a
    /// disconnected player's object survives (DontDestroyWithOwner) but its
    /// ownership reverts to the server, so NGO's own per-client lookups stop
    /// resolving it.
    /// </summary>
    private readonly Dictionary<ulong, NetworkObject> playerObjects = new Dictionary<ulong, NetworkObject>();

    private int roundNumber;                 // 1-based once the first round starts
    private ulong nextRoundHunter;           // first player caught this round (spec §3)
    private bool hasNextRoundHunter;
    private Coroutine roundTimer;            // 5-minute overall round countdown
    private Coroutine endgameTimer;          // 60-second last-runner countdown
    private IChainService chainService;
    private bool warnedNoChainService;

    /// <summary>Chain order, earliest catch first (read-only view for F3/UI).</summary>
    public IReadOnlyList<ulong> ChainMembers => chainMembers;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
        ResetRoundState();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        base.OnDestroy(); // NGO's NetworkBehaviour does its own cleanup here
    }

    // ---------- Round lifecycle ----------

    /// <summary>
    /// Starts a round: picks the Hunter (round 1 random; afterwards the first
    /// player the previous round's Hunter caught — spec §3), fills
    /// uncaughtRunners with everyone else, arms nothing else. Server-only.
    /// </summary>
    [ContextMenu("Start Round (server only)")]
    public void StartRound()
    {
        if (!IsSpawned || !IsServer)
        {
            Debug.LogError("[GameRoundManager] StartRound is server-only and requires a running session.");
            return;
        }
        if (Phase.Value == RoundPhase.Active || Phase.Value == RoundPhase.Endgame)
        {
            Debug.LogError("[GameRoundManager] StartRound ignored — a round is already running.");
            return;
        }

        IReadOnlyList<ulong> clients = NetworkManager.ConnectedClientsIds;
        if (clients.Count < 2)
        {
            Debug.LogError($"[GameRoundManager] StartRound requires ≥ 2 connected players; have {clients.Count}.");
            return;
        }

        ResetRoundState();
        roundNumber++;
        ResolveChainService();

        // Capture player objects up front — after a disconnect these are no
        // longer reachable through per-client lookups (ownership reverts).
        foreach (ulong clientId in clients)
        {
            NetworkObject playerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObject == null)
            {
                Debug.LogError($"[GameRoundManager] Client {clientId} has no player object — is the Player Prefab set?");
                return;
            }
            playerObjects[clientId] = playerObject;
        }

        // --- Hunter selection (spec §3) ---
        ulong hunter;
        if (roundNumber > 1 && hasNextRoundHunter && playerObjects.ContainsKey(nextRoundHunter))
        {
            hunter = nextRoundHunter;
        }
        else
        {
            if (roundNumber > 1)
            {
                // Designated next Hunter left between rounds (open question in F4 — fallback: random).
                Debug.LogWarning("[GameRoundManager] Previous round's first-caught player is not in this round — picking a random Hunter instead.");
            }
            hunter = clients[Random.Range(0, clients.Count)];
        }
        hasNextRoundHunter = false;

        HunterClientId.Value = hunter;
        foreach (ulong clientId in clients)
        {
            if (clientId != hunter)
            {
                uncaughtRunners.Add(clientId);
            }
        }

        // Fresh, separated positions every round — otherwise Play Again starts
        // with the just-caught runner still inside the Hunter's catch radius
        // and the round ends on the first physics tick.
        PlayerSpawnManager spawner = FindFirstObjectByType<PlayerSpawnManager>();
        if (spawner != null)
        {
            spawner.PlaceAllPlayersRandom();
        }
        else
        {
            Debug.LogWarning("[GameRoundManager] No PlayerSpawnManager found — players keep their current positions this round.");
        }

        Phase.Value = RoundPhase.Active;

        // F4 revision (supersedes D2's untimed RoundActive): the whole round is
        // on a 5-minute clock from the first moment.
        roundTimer = StartCoroutine(RoundCountdown());
        PhaseEndsAtServerTime.Value = NetworkManager.ServerTime.Time + roundSeconds;

        Debug.Log($"[GameRoundManager] ROUND {roundNumber} STARTED. Hunter: client {hunter}. " +
                  $"Runners: {uncaughtRunners.Count}. Round timer: {roundSeconds:0}s.");
        RoundStartedRpc(hunter, roundNumber);

        // A 2-player round begins with exactly one runner — that IS the endgame
        // (the round timer above is immediately replaced by the 60s one).
        EvaluateWinCondition();
    }

    private void ResetRoundState()
    {
        // Disconnected players' frozen bodies must not haunt the next round.
        if (IsSpawned && IsServer)
        {
            foreach (ulong clientId in disconnectedPlayers)
            {
                if (playerObjects.TryGetValue(clientId, out NetworkObject body) && body != null && body.IsSpawned)
                {
                    body.Despawn(destroy: true);
                }
            }
        }

        uncaughtRunners.Clear();
        chainMembers.Clear();
        disconnectedPlayers.Clear();
        playerObjects.Clear();
        StopRoundTimer();
        StopEndgameTimer();
        if (IsSpawned && IsServer)
        {
            Phase.Value = RoundPhase.Idle;
            PhaseEndsAtServerTime.Value = 0d;
        }
    }

    private void ResolveChainService()
    {
        if (chainService != null)
        {
            return;
        }
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour is IChainService service)
            {
                chainService = service;
                return;
            }
        }
        if (!warnedNoChainService)
        {
            warnedNoChainService = true;
            Debug.LogWarning("[GameRoundManager] No IChainService in the scene (F3 not present). " +
                             "Catches will update game state but skip physical chain attachment.");
        }
    }

    // ---------- Catch pipeline (spec §1) ----------

    /// <summary>
    /// Raw contact report from a CatchDetector. Direction-agnostic on purpose:
    /// Rigidbody chain members are jittery (D1 risk), so the detector reports
    /// every player-player contact and this method decides whether one side is
    /// chain and the other an uncaught runner.
    /// </summary>
    public void ReportContact(ulong playerA, ulong playerB)
    {
        if (!IsServer || (Phase.Value != RoundPhase.Active && Phase.Value != RoundPhase.Endgame))
        {
            return;
        }

        if (IsChainSide(playerA) && uncaughtRunners.Contains(playerB))
        {
            OnCatch(catcherId: playerA, caughtId: playerB);
        }
        else if (IsChainSide(playerB) && uncaughtRunners.Contains(playerA))
        {
            OnCatch(catcherId: playerB, caughtId: playerA);
        }
    }

    private bool IsChainSide(ulong clientId)
    {
        return clientId == HunterClientId.Value || chainMembers.Contains(clientId);
    }

    /// <summary>Server-confirmed catch (spec §1): state, chain append, physical attach, win check.</summary>
    private void OnCatch(ulong catcherId, ulong caughtId)
    {
        // 1. Single source of truth first — also the dedupe guard: two detectors
        //    reporting the same contact in one physics step no-op here.
        if (!uncaughtRunners.Remove(caughtId))
        {
            return;
        }

        // First catch of the round is by the Hunter by construction (chain of
        // one) — that player is next round's Hunter (spec §3).
        if (chainMembers.Count == 0)
        {
            nextRoundHunter = caughtId;
            hasNextRoundHunter = true;
        }

        // 2. Append to the chain list.
        chainMembers.Add(caughtId);

        // 3. Physical attachment — F3's job, via the assumed interface.
        if (chainService != null && playerObjects.TryGetValue(caughtId, out NetworkObject caughtObject))
        {
            chainService.AttachToTail(caughtObject);
        }
        else
        {
            Debug.LogWarning($"[GameRoundManager] CATCH of client {caughtId}: physical chain attach skipped " +
                             (chainService == null ? "(no IChainService — F3 pending)." : "(player object missing!)."));
        }

        Debug.Log($"[GameRoundManager] CATCH: client {catcherId} caught client {caughtId}. " +
                  $"Chain length (excl. Hunter): {chainMembers.Count}. Runners left: {uncaughtRunners.Count}.");

        // 4. Broadcast the tag/hit reaction anims so every peer sees the catch
        //    play out visually. The RPCs are declared on PlayerMovement — cheap
        //    lookup because we already have the NetworkObjects cached.
        if (playerObjects.TryGetValue(catcherId, out NetworkObject catcherObj) && catcherObj != null)
        {
            PlayerMovement catcherMovement = catcherObj.GetComponent<PlayerMovement>();
            if (catcherMovement != null) catcherMovement.PlayPunchAnimEveryoneRpc();
        }
        if (playerObjects.TryGetValue(caughtId, out NetworkObject caughtObj) && caughtObj != null)
        {
            PlayerMovement caughtMovement = caughtObj.GetComponent<PlayerMovement>();
            if (caughtMovement != null) caughtMovement.PlayHitAnimEveryoneRpc();
        }

        EvaluateWinCondition();
    }

    // ---------- Win condition (spec §2) ----------

    /// <summary>Called only from catch events (and round start). Never polled.</summary>
    private void EvaluateWinCondition()
    {
        if (uncaughtRunners.Count == 0)
        {
            EndRound(RoundWinner.Chain, HunterClientId.Value);
        }
        else if (uncaughtRunners.Count == 1 && Phase.Value == RoundPhase.Active)
        {
            // The 5-minute round timer is REPLACED (not resumed later) by a
            // fresh 60s endgame countdown — remaining round time is discarded
            // by design. Because this runs synchronously inside the catch that
            // dropped the count to 1, a round-timer expiry scheduled for the
            // same tick can no longer fire: the endgame takes precedence.
            StopRoundTimer();
            Phase.Value = RoundPhase.Endgame;
            PhaseEndsAtServerTime.Value = NetworkManager.ServerTime.Time + endgameSeconds;
            endgameTimer = StartCoroutine(EndgameCountdown());
            Debug.Log($"[GameRoundManager] ENDGAME: one runner remains — round timer replaced; chain has {endgameSeconds:0}s to catch them.");
        }
    }

    private IEnumerator RoundCountdown()
    {
        yield return new WaitForSeconds(roundSeconds);

        // Tie-break (F4 revision, open question 3): if a catch in this same
        // tick had already reduced the field to one runner, it stopped this
        // coroutine synchronously and we never get here. Reaching this line
        // means the count was still > 1 at expiry — Runners win outright.
        if (uncaughtRunners.Count > 1)
        {
            Debug.Log($"[GameRoundManager] ROUND TIMER EXPIRED with {uncaughtRunners.Count} runners still uncaught — Runners win, Hunter loses.");
            EndRound(RoundWinner.Runners, ulong.MaxValue);
        }
        else
        {
            // Unreachable by design (count == 1 stops this coroutine) — fail loud.
            Debug.LogError($"[GameRoundManager] Round timer expired in an unexpected state (runners={uncaughtRunners.Count}) — ending as no contest.");
            EndRound(RoundWinner.NoContest, ulong.MaxValue);
        }
    }

    private IEnumerator EndgameCountdown()
    {
        yield return new WaitForSeconds(endgameSeconds);

        // Timer expired before a catch ended the round — the survivor wins.
        ulong survivor = ulong.MaxValue;
        foreach (ulong runner in uncaughtRunners)
        {
            survivor = runner;
        }
        EndRound(RoundWinner.LastRunner, survivor);
    }

    private void StopRoundTimer()
    {
        if (roundTimer != null)
        {
            StopCoroutine(roundTimer);
            roundTimer = null;
        }
    }

    private void StopEndgameTimer()
    {
        if (endgameTimer != null)
        {
            StopCoroutine(endgameTimer);
            endgameTimer = null;
        }
    }

    private void EndRound(RoundWinner winner, ulong winnerClientId)
    {
        StopRoundTimer();
        StopEndgameTimer();

        // Result facts land before the phase flip so Phase.OnValueChanged
        // handlers (ResultScreenUI) read a consistent outcome.
        LastWinner.Value = winner;
        LastWinnerClientId.Value = winnerClientId;
        PhaseEndsAtServerTime.Value = 0d;
        Phase.Value = RoundPhase.Ended;

        Debug.Log($"[GameRoundManager] ROUND {roundNumber} OVER: {winner} (client {winnerClientId}).");
        RoundResultRpc(winner, winnerClientId);
    }

    /// <summary>Round-start broadcast — carries the Hunter id as payload for the role banner.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void RoundStartedRpc(ulong hunterClientId, int round)
    {
        RoundStarted?.Invoke(hunterClientId, round);
    }

    /// <summary>Round outcome broadcast — ResultScreenUI consumes the payload event.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void RoundResultRpc(RoundWinner winner, ulong winnerClientId)
    {
        Debug.Log($"[GameRoundManager] Round result received: {winner} wins (client {winnerClientId}).");
        RoundResultReceived?.Invoke(winner, winnerClientId);
    }

    // ---------- Disconnect handling (spec §4) ----------

    private void HandleClientDisconnected(ulong clientId)
    {
        if (Phase.Value != RoundPhase.Active && Phase.Value != RoundPhase.Endgame)
        {
            return;
        }
        if (!playerObjects.ContainsKey(clientId))
        {
            return; // not part of this round
        }

        disconnectedPlayers.Add(clientId);

        if (uncaughtRunners.Contains(clientId))
        {
            // Uncaught runner: freeze in place, stays in uncaughtRunners, stays
            // catchable. Deliberately no win-condition re-check — a disconnect
            // never changes the count (spec §4).
            FreezePlayerObject(clientId);
            Debug.Log($"[GameRoundManager] Runner {clientId} disconnected — frozen, still catchable. Win count unchanged.");
        }
        else if (chainMembers.Contains(clientId))
        {
            // Chain member: frozen, gap persists until the Hunter removes them.
            chainService?.FreezeMember(clientId);
            FreezePlayerObject(clientId);
            Debug.Log($"[GameRoundManager] Chain member {clientId} disconnected — frozen in chain; Hunter may remove them.");
        }
        else if (clientId == HunterClientId.Value)
        {
            PromoteEarliestCaughtToHunter(oldHunter: clientId);
        }
    }

    /// <summary>Hunter disconnected: role passes to the earliest-caught chain member (spec §4).</summary>
    private void PromoteEarliestCaughtToHunter(ulong oldHunter)
    {
        FreezePlayerObject(oldHunter);

        // Earliest-caught first; skip members who are themselves disconnected —
        // a frozen player cannot hunt (open question in F4; skipping logged).
        ulong newHunter = ulong.MaxValue;
        foreach (ulong member in chainMembers)
        {
            if (!disconnectedPlayers.Contains(member))
            {
                newHunter = member;
                break;
            }
            Debug.LogWarning($"[GameRoundManager] Skipping disconnected chain member {member} for Hunter promotion.");
        }

        if (newHunter == ulong.MaxValue)
        {
            // Hunter gone and no connected chain member to promote. F4 spec does
            // not define this; ending as no-contest rather than leaving a
            // hunterless round running.
            Debug.LogError("[GameRoundManager] Hunter disconnected with no connected chain member to promote — round ends (no contest).");
            EndRound(RoundWinner.NoContest, ulong.MaxValue);
            return;
        }

        chainMembers.Remove(newHunter);
        // The dead Hunter's body stays in the chain as a frozen, removable
        // member at the head-most position (its physical handling is F3's call).
        chainMembers.Insert(0, oldHunter);
        HunterClientId.Value = newHunter;
        chainService?.PromoteToHead(newHunter);

        Debug.Log($"[GameRoundManager] Hunter {oldHunter} disconnected — client {newHunter} (earliest-caught) is the new Hunter.");
    }

    /// <summary>
    /// Current Hunter asks the server to remove a DISCONNECTED chain member and
    /// close the gap. Invokable by any client; the server validates the sender
    /// is the current Hunter — a mid-round promoted Hunter passes this check
    /// automatically because HunterClientId is updated on promotion.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestRemoveChainMemberRpc(ulong targetClientId, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (Phase.Value != RoundPhase.Active && Phase.Value != RoundPhase.Endgame)
        {
            Debug.LogWarning($"[GameRoundManager] Remove request from {sender} ignored — no round running.");
            return;
        }
        if (sender != HunterClientId.Value)
        {
            Debug.LogWarning($"[GameRoundManager] Remove request DENIED: client {sender} is not the current Hunter.");
            return;
        }
        if (!chainMembers.Contains(targetClientId))
        {
            Debug.LogWarning($"[GameRoundManager] Remove request DENIED: client {targetClientId} is not a chain member.");
            return;
        }
        if (!disconnectedPlayers.Contains(targetClientId))
        {
            Debug.LogWarning($"[GameRoundManager] Remove request DENIED: client {targetClientId} is still connected — only disconnected members are removable.");
            return;
        }

        chainService?.RemoveMember(targetClientId);
        chainMembers.Remove(targetClientId);

        // Interpretation (flagged in F4 notes): "remove" despawns the abandoned
        // body once F3 has re-seamed the joints around it.
        if (playerObjects.TryGetValue(targetClientId, out NetworkObject body) && body != null && body.IsSpawned)
        {
            body.Despawn(destroy: true);
        }
        playerObjects.Remove(targetClientId);

        Debug.Log($"[GameRoundManager] Hunter {sender} removed disconnected chain member {targetClientId}; gap closed.");
    }

    /// <summary>
    /// Host-only Play Again (spec §6): resets all round state and returns
    /// everyone to WaitingForPlayers (Phase Idle) — no mode-select detour.
    /// The next round starts through the normal StartRound entry point, which
    /// re-applies the Hunter selection rule, repopulates uncaughtRunners from
    /// the connected players and arms a fresh 5:00 round timer.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPlayAgainRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (sender != NetworkManager.ServerClientId)
        {
            Debug.LogWarning($"[GameRoundManager] Play Again DENIED: client {sender} is not the host.");
            return;
        }
        if (Phase.Value != RoundPhase.Ended)
        {
            Debug.LogWarning("[GameRoundManager] Play Again ignored — no finished round to reset.");
            return;
        }

        ResetRoundState();
        LastWinner.Value = RoundWinner.None;
        LastWinnerClientId.Value = ulong.MaxValue;

        // Roll straight into the next round: StartRound re-enumerates every
        // CONNECTED client (uncaughtRunners repopulated, Hunter reassigned per
        // the selection rule, fresh 5:00) and the replicated Phase change moves
        // every client's UI — nothing here is host-local. Without this call
        // nobody could trigger the next round once the lobby is gone.
        Debug.Log("[GameRoundManager] PLAY AGAIN: round state reset — starting the next round.");
        StartRound();
    }

    /// <summary>
    /// Freezes a disconnected player's object: disabling PlayerMovement stops
    /// the server simulating it (no input arrives anyway), leaving the body —
    /// and its colliders — exactly where it stood. Requires DontDestroyWithOwner
    /// on the player prefab's NetworkObject, otherwise NGO destroys the object
    /// before this runs.
    /// </summary>
    private void FreezePlayerObject(ulong clientId)
    {
        if (!playerObjects.TryGetValue(clientId, out NetworkObject playerObject) || playerObject == null)
        {
            Debug.LogError($"[GameRoundManager] Cannot freeze player {clientId} — object missing. " +
                           "Is DontDestroyWithOwner ticked on the player prefab's NetworkObject?");
            return;
        }

        PlayerMovement movement = playerObject.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false;
        }
    }
}
