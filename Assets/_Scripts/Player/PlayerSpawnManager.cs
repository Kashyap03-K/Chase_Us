using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// KAS-28 (F2): places each connecting player's spawned player object on a
/// distinct spawn point (server-authoritative — clients receive the position
/// through NetworkTransform), and swaps the offline scene player out for the
/// duration of a network session.
///
/// Spawn points are a serialized ring of 4 (this sprint's testing ceiling);
/// a 5th+ player wraps around rather than erroring, since enforcing a player
/// cap is lobby scope, not spawn scope.
///
/// Offline handling: the scene's non-networked Player_Character stays playable
/// exactly as before when no session is running. While a session runs it is
/// deactivated (otherwise it would shadow the real networked player and eat
/// the same input), and reactivated — with the camera handed back — when the
/// session ends.
/// </summary>
public class PlayerSpawnManager : MonoBehaviour
{
    [Header("Spawn points (server picks one per connecting player)")]
    [SerializeField]
    private Vector3[] spawnPoints =
    {
        new Vector3(-8f, 0.2f, -7f),
        new Vector3(-4f, 0.2f, -7f),
        new Vector3(-8f, 0.2f, -3f),
        new Vector3(-4f, 0.2f, -3f)
    };

    [Header("Offline scene player (auto-found if left null)")]
    [SerializeField] private GameObject offlinePlayer;

    [Header("Camera hand-back (auto-found if left null)")]
    [SerializeField] private OrbitCamera orbitCamera;

    private int nextSpawnIndex;
    private bool subscribed;

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

    // ---------- Session lifecycle ----------

    private void HandleServerStarted()
    {
        nextSpawnIndex = 0;
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

        Vector3 point = spawnPoints[nextSpawnIndex % spawnPoints.Length];
        nextSpawnIndex++;

        PlacePlayer(client.PlayerObject, point);
        Debug.Log($"[PlayerSpawnManager] Placed player of client {clientId} at spawn point {point}.");
    }

    /// <summary>
    /// F4: re-places every connected player's object on a random, distinct
    /// spawn point (wrapping if there are more players than points). Called by
    /// GameRoundManager at each round start — including Play Again — so nobody
    /// begins a round already inside another player's catch radius. Server-only.
    /// </summary>
    public void PlaceAllPlayersRandom()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer)
        {
            Debug.LogError("[PlayerSpawnManager] PlaceAllPlayersRandom is server-only.");
            return;
        }

        // Fisher–Yates shuffle of the spawn indices.
        int[] order = new int[spawnPoints.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        int placed = 0;
        foreach (ulong clientId in nm.ConnectedClientsIds)
        {
            if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
            {
                continue;
            }
            PlacePlayer(client.PlayerObject, spawnPoints[order[placed % order.Length]]);
            placed++;
        }
        Debug.Log($"[PlayerSpawnManager] Re-placed {placed} player(s) on shuffled spawn points for the new round.");
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
