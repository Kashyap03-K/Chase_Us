using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Player placement (F2, reworked in G2) + offline-player swap.
///
/// G2 placement model:
///   * The Hunter starts every round on <see cref="hunterSpawnPoint"/> — a
///     hand-placed marker at the map centre (NEEDS Editor placement; origin +
///     warning until then).
///   * Runners get random points inside the play area, derived at runtime from
///     the four "_Boundary" wall transforms (their min/max positions inset by
///     wall half-thickness + margin). Candidates are rejected if they overlap
///     anything on the Obstacle layer (assigned to every "_Obstacles" child at
///     startup by this component) or sit within <see cref="minPlayerSeparation"/>
///     of any already-placed player. Retries are capped so a bad map config
///     degrades to a warning, never a hang.
///   * Connect-time placement (pre-round lobby) deliberately uses the same
///     random-valid-point path with NO hunter special-case — there is no
///     hunter before a round starts. (Explicit design choice, see PR.)
///
/// Offline handling: the scene's non-networked Player_Character stays playable
/// exactly as before when no session is running; swapped out during sessions.
/// </summary>
public class PlayerSpawnManager : MonoBehaviour
{
    [Header("G2 — Hunter spawn")]
    [SerializeField, Tooltip("Marker for the Hunter's round-start position. NEEDS MANUAL PLACEMENT at the map centre in the Editor — until then world origin is used and a warning logged each round.")]
    private Transform hunterSpawnPoint;

    [Header("G2 — Map bounds")]
    [SerializeField, Tooltip("Root whose children are the boundary walls ('_Boundary'). Auto-found by name if null; bounds derive from the walls' min/max positions.")]
    private Transform boundaryRoot;
    [SerializeField, Tooltip("Inset from the derived wall positions: covers wall half-thickness plus a safety margin.")]
    private float boundsMargin = 2f;
    [SerializeField, Tooltip("Fallback play area (x = min X, y = min Z) if _Boundary can't be found. Matches the current Sakri walls.")]
    private Rect fallbackBounds = new Rect(-24.3f, -24f, 49f, 49.4f);

    [Header("G2 — Runner placement")]
    [SerializeField, Tooltip("Layer for spawn-blocking geometry. Assigned to every '_Obstacles' child at startup by this component (no per-prefab layer setup existed).")]
    private string obstacleLayerName = "Obstacle";
    [SerializeField, Tooltip("Minimum distance between any two spawned players. Catch CONTACT distance is trigger radius (0.5) + capsule radius (0.5) = 1.0m — '2x catch radius' would equal exactly that, so 3x contact distance is used to rule out instant catches. Tunable.")]
    private float minPlayerSeparation = 3f;
    [SerializeField, Tooltip("Candidate re-samples per player before giving up with a warning.")]
    private int maxPlacementAttempts = 20;
    [SerializeField, Tooltip("Spawn drop height; gravity settles players onto the ground.")]
    private float spawnHeight = 0.2f;

    [Header("Offline scene player (auto-found if left null)")]
    [SerializeField] private GameObject offlinePlayer;

    [Header("Camera hand-back (auto-found if left null)")]
    [SerializeField] private OrbitCamera orbitCamera;

    private bool subscribed;
    private bool boundsDerived;
    private Rect playBounds;
    private int obstacleMask;
    private float playerRadius = 0.5f;   // read from the player prefab's CC when available
    private float playerHeight = 2f;

    private void Awake()
    {
        if (offlinePlayer == null)
        {
            // The offline player is the PlayerMovement instance with no NetworkObject.
            foreach (PlayerMovement pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include))
            {
                if (pm.GetComponentInParent<NetworkObject>() == null)
                {
                    offlinePlayer = pm.gameObject;
                    break;
                }
            }
        }

        if (orbitCamera == null)
        {
            foreach (OrbitCamera candidate in FindObjectsByType<OrbitCamera>(FindObjectsInactive.Exclude))
            {
                if (candidate.GetComponent<Camera>() != null)
                {
                    orbitCamera = candidate;
                    break;
                }
            }
        }
    }

    private void Start()
    {
        AssignObstacleLayer();

        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[PlayerSpawnManager] No NetworkManager in scene — spawn placement disabled.");
            return;
        }

        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
        NetworkManager.Singleton.OnClientStarted += HandleClientStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnServerStopped += HandleSessionStopped;
        NetworkManager.Singleton.OnClientStopped += HandleSessionStopped;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (!subscribed || NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
        NetworkManager.Singleton.OnClientStarted -= HandleClientStarted;
        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnServerStopped -= HandleSessionStopped;
        NetworkManager.Singleton.OnClientStopped -= HandleSessionStopped;
    }

    // ---------- G2 setup ----------

    /// <summary>
    /// No dedicated physics layer existed for map geometry (everything shipped
    /// on Default, which also carries the ground and player capsules — an
    /// overlap test against Default would false-positive constantly). The
    /// "Obstacle" layer was added to TagManager; this stamps it onto every
    /// child under "_Obstacles" at startup so the map team doesn't have to
    /// re-layer hundreds of prefab instances by hand. Replace with Editor-side
    /// assignment whenever convenient — this stays correct either way.
    /// </summary>
    private void AssignObstacleLayer()
    {
        int layer = LayerMask.NameToLayer(obstacleLayerName);
        if (layer < 0)
        {
            Debug.LogError($"[PlayerSpawnManager] Layer '{obstacleLayerName}' does not exist in TagManager — obstacle-aware spawning disabled (candidates will only be separation-checked).");
            obstacleMask = 0;
            return;
        }
        obstacleMask = 1 << layer;

        GameObject obstaclesRoot = GameObject.Find("_Obstacles");
        if (obstaclesRoot == null)
        {
            Debug.LogWarning("[PlayerSpawnManager] No '_Obstacles' object in the scene — nothing to stamp with the Obstacle layer.");
            return;
        }

        SetLayerRecursively(obstaclesRoot.transform, layer);
        Debug.Log($"[PlayerSpawnManager] Stamped layer '{obstacleLayerName}' ({layer}) onto '_Obstacles' and all children.");
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
        }
    }

    /// <summary>
    /// Play-area rect (x/y = min X/Z), derived once from the "_Boundary" wall
    /// children: their min/max positions inset by <see cref="boundsMargin"/>
    /// (covers the walls' half-thickness). Falls back to the serialized rect
    /// if the object is missing.
    /// </summary>
    private Rect GetPlayBounds()
    {
        if (boundsDerived) return playBounds;

        if (boundaryRoot == null)
        {
            GameObject found = GameObject.Find("_Boundary");
            if (found != null) boundaryRoot = found.transform;
        }

        if (boundaryRoot != null && boundaryRoot.childCount >= 2)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < boundaryRoot.childCount; i++)
            {
                Vector3 p = boundaryRoot.GetChild(i).position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }
            playBounds = Rect.MinMaxRect(minX + boundsMargin, minZ + boundsMargin,
                                         maxX - boundsMargin, maxZ - boundsMargin);
            Debug.Log($"[PlayerSpawnManager] Play bounds derived from '_Boundary': x [{playBounds.xMin:0.#}, {playBounds.xMax:0.#}], z [{playBounds.yMin:0.#}, {playBounds.yMax:0.#}].");
        }
        else
        {
            playBounds = fallbackBounds;
            Debug.LogWarning("[PlayerSpawnManager] '_Boundary' not found — using serialized fallback bounds.");
        }

        boundsDerived = true;
        return playBounds;
    }

    private void CachePlayerCapsule()
    {
        NetworkManager nm = NetworkManager.Singleton;
        GameObject prefab = nm != null && nm.NetworkConfig != null ? nm.NetworkConfig.PlayerPrefab : null;
        CharacterController cc = prefab != null ? prefab.GetComponent<CharacterController>() : null;
        if (cc != null)
        {
            playerRadius = cc.radius;
            playerHeight = cc.height;
        }
    }

    /// <summary>
    /// Samples a random in-bounds point that (a) doesn't overlap the Obstacle
    /// layer with the player's own capsule and (b) keeps
    /// <see cref="minPlayerSeparation"/> from every position in
    /// <paramref name="occupied"/>. Falls back to the last candidate (with a
    /// warning) after <see cref="maxPlacementAttempts"/> — never hangs.
    /// </summary>
    private Vector3 FindValidRandomPoint(List<Vector3> occupied)
    {
        Rect bounds = GetPlayBounds();
        Vector3 candidate = Vector3.zero;
        float sepSqr = minPlayerSeparation * minPlayerSeparation;

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            candidate = new Vector3(
                Random.Range(bounds.xMin, bounds.xMax),
                spawnHeight,
                Random.Range(bounds.yMin, bounds.yMax));

            bool tooClose = false;
            foreach (Vector3 taken in occupied)
            {
                Vector3 flat = candidate - taken;
                flat.y = 0f;
                if (flat.sqrMagnitude < sepSqr) { tooClose = true; break; }
            }
            if (tooClose) continue;

            if (obstacleMask != 0)
            {
                Vector3 capsuleBottom = candidate + Vector3.up * playerRadius;
                Vector3 capsuleTop = candidate + Vector3.up * Mathf.Max(playerRadius, playerHeight - playerRadius);
                if (Physics.CheckCapsule(capsuleBottom, capsuleTop, playerRadius, obstacleMask))
                {
                    continue; // inside a tree/crate/prop — re-sample
                }
            }

            return candidate;
        }

        Debug.LogWarning($"[PlayerSpawnManager] No valid spawn found in {maxPlacementAttempts} attempts — using last candidate {candidate}. Check map bounds / obstacle density.");
        return candidate;
    }

    // ---------- Session lifecycle ----------

    private void HandleServerStarted()
    {
        SetOfflinePlayerActive(false);
    }

    private void HandleClientStarted()
    {
        // Fires on pure clients AND on the host's client half — idempotent.
        SetOfflinePlayerActive(false);
    }

    private void HandleSessionStopped(bool wasHost)
    {
        SetOfflinePlayerActive(true);

        // Hand the camera back to the offline player; the networked one is despawning.
        if (orbitCamera != null && offlinePlayer != null)
        {
            orbitCamera.SetTarget(offlinePlayer.transform);
        }
    }

    // ---------- Server-side placement ----------

    private void HandleClientConnected(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
        {
            Debug.LogWarning($"[PlayerSpawnManager] Client {clientId} connected but has no player object to place. " +
                             "Is the Player Prefab set on the NetworkManager?");
            return;
        }

        CachePlayerCapsule();

        // G2 design choice: connect-time (pre-round lobby) has no meaningful
        // hunter/runner split — everyone gets a plain map-bounded random valid
        // point. Round-start roles are handled by PlaceAllPlayersRandom.
        List<Vector3> occupied = new List<Vector3>();
        foreach (ulong otherId in nm.ConnectedClientsIds)
        {
            if (otherId == clientId) continue;
            if (nm.ConnectedClients.TryGetValue(otherId, out NetworkClient other) && other.PlayerObject != null)
            {
                occupied.Add(other.PlayerObject.transform.position);
            }
        }

        Vector3 point = FindValidRandomPoint(occupied);
        PlacePlayer(client.PlayerObject, point);
        Debug.Log($"[PlayerSpawnManager] Placed player of client {clientId} at {point}.");
    }

    /// <summary>
    /// G2: round-start placement. The Hunter lands on the hand-placed centre
    /// marker; every runner gets a random valid point — in-bounds, off the
    /// Obstacle layer, and at least <see cref="minPlayerSeparation"/> from the
    /// Hunter and every runner placed before them. Called by GameRoundManager
    /// at each round start including Play Again. Server-only.
    /// </summary>
    public void PlaceAllPlayersRandom(ulong hunterClientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer)
        {
            Debug.LogError("[PlayerSpawnManager] PlaceAllPlayersRandom is server-only.");
            return;
        }

        CachePlayerCapsule();

        Vector3 hunterPos;
        if (hunterSpawnPoint != null)
        {
            hunterPos = hunterSpawnPoint.position;
        }
        else
        {
            hunterPos = new Vector3(0f, spawnHeight, 0f);
            Debug.LogWarning("[PlayerSpawnManager] hunterSpawnPoint not assigned — Hunter placed at world origin. " +
                             "Place the marker at the map centre in the Editor and wire it on PlayerSpawnManager.");
        }

        List<Vector3> occupied = new List<Vector3> { hunterPos };
        int placed = 0;

        foreach (ulong clientId in nm.ConnectedClientsIds)
        {
            if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
            {
                continue;
            }

            if (clientId == hunterClientId)
            {
                PlacePlayer(client.PlayerObject, hunterPos);
            }
            else
            {
                Vector3 point = FindValidRandomPoint(occupied);
                occupied.Add(point);
                PlacePlayer(client.PlayerObject, point);
            }
            placed++;
        }

        Debug.Log($"[PlayerSpawnManager] Round placement: hunter (client {hunterClientId}) at {hunterPos}, " +
                  $"{placed - 1} runner(s) on random valid points.");
    }

    private static void PlacePlayer(NetworkObject playerObject, Vector3 position)
    {
        // The CharacterController caches its position — disable it around the
        // teleport so it can't snap the player back.
        CharacterController cc = playerObject.GetComponent<CharacterController>();
        bool ccWasEnabled = cc != null && cc.enabled;
        if (ccWasEnabled) cc.enabled = false;

        NetworkTransform netTransform = playerObject.GetComponent<NetworkTransform>();
        if (netTransform != null)
        {
            netTransform.Teleport(position, playerObject.transform.rotation, playerObject.transform.localScale);
        }
        else
        {
            playerObject.transform.position = position;
        }

        if (ccWasEnabled) cc.enabled = true;
    }

    private void SetOfflinePlayerActive(bool active)
    {
        if (offlinePlayer == null || offlinePlayer.activeSelf == active) return;
        offlinePlayer.SetActive(active);
        Debug.Log($"[PlayerSpawnManager] Offline scene player {(active ? "reactivated" : "deactivated")} " +
                  $"({(active ? "session ended" : "network session running")}).");
    }
}
