using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Starts an NGO host or client over Unity Relay using a join code.
/// Requires ServicesBootstrap to have completed anonymous sign-in first.
/// The host is the sole network authority; clients contribute nothing but a
/// join code, so no client-supplied data is trusted here or later.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    public static NetworkBootstrap Instance { get; private set; }

    // DTLS = encrypted UDP, the recommended Relay connection type for desktop/editor.
    private const string ConnectionType = "dtls";

    /// <summary>Join code of the session we are hosting. Null when not hosting.</summary>
    public string JoinCode { get; private set; }

    /// <summary>True while an async host/join attempt is in flight.</summary>
    public bool IsBusy { get; private set; }

    private bool callbacksSubscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// Creates a Relay allocation, configures UnityTransport with the host
    /// allocation data and starts the NGO host.
    /// Returns the join code to share with clients, or null on any failure.
    /// </summary>
    public async Task<string> StartHostAsync(int maxConnections)
    {
        if (!PreflightChecksPass(nameof(StartHostAsync)))
        {
            return null;
        }

        IsBusy = true;
        try
        {
            Debug.Log($"[NetworkBootstrap] Creating Relay allocation (maxConnections={maxConnections})...");
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            Debug.Log($"[NetworkBootstrap] Allocation created (region: {allocation.Region}). Requesting join code...");
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(allocation.ToRelayServerData(ConnectionType));

            SubscribeConnectionCallbacks();

            // StartHost() fires OnServerStarted synchronously — handlers (LobbyRoomUI etc.)
            // read this property at that moment, so it MUST be populated before the call.
            JoinCode = joinCode;

            if (!NetworkManager.Singleton.StartHost())
            {
                JoinCode = null;
                Debug.LogError("[NetworkBootstrap] NetworkManager.StartHost() returned false — host did not start.");
                return null;
            }

            Debug.Log($"[NetworkBootstrap] HOST STARTED. Relay join code: {joinCode} — share this with the joining client.");
            return joinCode;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[NetworkBootstrap] StartHost FAILED (Relay error {e.Reason}): {e.Message}");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkBootstrap] StartHost FAILED (unexpected): {e}");
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Joins an existing Relay allocation via join code, configures
    /// UnityTransport with the client allocation data and starts the NGO client.
    /// Returns true if the client started (transport-level connection then
    /// completes asynchronously and is reported by the connection callbacks).
    /// </summary>
    public async Task<bool> StartClientAsync(string joinCode)
    {
        if (!PreflightChecksPass(nameof(StartClientAsync)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogError("[NetworkBootstrap] StartClient FAILED: join code is empty.");
            return false;
        }

        IsBusy = true;
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim());

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(joinAllocation.ToRelayServerData(ConnectionType));

            SubscribeConnectionCallbacks();

            if (!NetworkManager.Singleton.StartClient())
            {
                Debug.LogError("[NetworkBootstrap] NetworkManager.StartClient() returned false — client did not start.");
                return false;
            }

            // Persist so lobby UIs can render the code the client joined with.
            JoinCode = joinCode.Trim();
            Debug.Log($"[NetworkBootstrap] CLIENT STARTED with join code {JoinCode}. Waiting for connection callback...");
            return true;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[NetworkBootstrap] StartClient FAILED (Relay error {e.Reason}): {e.Message} — check the join code is correct and not expired.");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkBootstrap] StartClient FAILED (unexpected): {e}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool PreflightChecksPass(string caller)
    {
        if (IsBusy)
        {
            Debug.LogWarning($"[NetworkBootstrap] {caller} ignored: another host/join attempt is already in flight.");
            return false;
        }

        if (ServicesBootstrap.Instance == null || !ServicesBootstrap.Instance.IsSignedIn)
        {
            Debug.LogError($"[NetworkBootstrap] {caller} FAILED: not signed in to Unity Services yet. " +
                           "Wait for ServicesBootstrap to log a successful sign-in first.");
            return false;
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError($"[NetworkBootstrap] {caller} FAILED: no NetworkManager in the scene. " +
                           "Create a GameObject with NetworkManager + UnityTransport components.");
            return false;
        }

        if (NetworkManager.Singleton.GetComponent<UnityTransport>() == null)
        {
            Debug.LogError($"[NetworkBootstrap] {caller} FAILED: NetworkManager has no UnityTransport component. " +
                           "Select 'UnityTransport' in the NetworkManager inspector's transport dropdown.");
            return false;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogError($"[NetworkBootstrap] {caller} FAILED: NetworkManager is already running as host or client.");
            return false;
        }

        return true;
    }

    private void SubscribeConnectionCallbacks()
    {
        if (callbacksSubscribed)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        callbacksSubscribed = true;
    }

    private void OnClientConnected(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.IsServer)
        {
            // ConnectedClients is server-only in NGO, hence the guard.
            Debug.Log($"[NetworkBootstrap] CLIENT CONNECTED: clientId={clientId}. ConnectedClients.Count={nm.ConnectedClients.Count}");
        }
        else
        {
            Debug.Log($"[NetworkBootstrap] CONNECTED TO HOST: clientId={clientId} (LocalClientId={nm.LocalClientId})");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        string reason = string.IsNullOrEmpty(nm.DisconnectReason) ? "none given" : nm.DisconnectReason;
        if (nm.IsServer)
        {
            Debug.Log($"[NetworkBootstrap] CLIENT DISCONNECTED: clientId={clientId} (reason: {reason}). ConnectedClients.Count={nm.ConnectedClients.Count}");
        }
        else
        {
            Debug.Log($"[NetworkBootstrap] DISCONNECTED FROM HOST: clientId={clientId} (reason: {reason})");
        }
    }

    private void OnDestroy()
    {
        if (callbacksSubscribed && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
