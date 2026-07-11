using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Starts an NGO host or client over Unity Relay using a join code, or —
/// KAS-23 (E2) — directly over the local network with UnityTransport and no
/// Unity Services at all. Relay paths require ServicesBootstrap to have
/// completed anonymous sign-in first; LAN paths deliberately do not.
/// The host is the sole network authority; clients contribute nothing but a
/// join code / LAN address, so no client-supplied data is trusted here or later.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    /// <summary>How the current (or last-started) session connects.</summary>
    public enum ConnectionMode { None, Relay, Lan }

    public static NetworkBootstrap Instance { get; private set; }

    // DTLS = encrypted UDP, the recommended Relay connection type for desktop/editor.
    private const string ConnectionType = "dtls";

    /// <summary>Default UDP port for direct LAN sessions.</summary>
    public const ushort DefaultLanPort = 7777;

    /// <summary>Join code of the session we are hosting. Null when not hosting (always null in LAN mode).</summary>
    public string JoinCode { get; private set; }

    /// <summary>Mode of the session currently starting/running. UI reads this to label the lobby.</summary>
    public ConnectionMode CurrentMode { get; private set; } = ConnectionMode.None;

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
            // read these properties at that moment, so they MUST be populated before the call.
            JoinCode = joinCode;
            CurrentMode = ConnectionMode.Relay;

            if (!NetworkManager.Singleton.StartHost())
            {
                JoinCode = null;
                CurrentMode = ConnectionMode.None;
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
            CurrentMode = ConnectionMode.Relay;

            if (!NetworkManager.Singleton.StartClient())
            {
                CurrentMode = ConnectionMode.None;
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

    // ---------- LAN (KAS-23 / E2) — direct UnityTransport, no Unity Services ----------

    /// <summary>
    /// Starts an NGO host directly on the local network: UnityTransport binds
    /// 0.0.0.0:<paramref name="port"/> and LanDiscovery announces the session so
    /// same-network clients can find it without typing an IP. No Relay, no UGS
    /// sign-in required. Returns false on any failure (already logged) — the
    /// most common one is the port already being in use by another host on
    /// this machine.
    /// </summary>
    public bool StartLanHost(ushort port = DefaultLanPort)
    {
        if (!LanPreflightChecksPass(nameof(StartLanHost)))
        {
            return false;
        }

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        string localIp = LanDiscovery.GetLocalIPv4() ?? "127.0.0.1";
        // Address is informational for a host; the listen address is what binds.
        transport.SetConnectionData(localIp, port, "0.0.0.0");

        SubscribeConnectionCallbacks();

        // Populated before StartHost() — OnServerStarted handlers read these synchronously.
        JoinCode = null;
        CurrentMode = ConnectionMode.Lan;

        if (!NetworkManager.Singleton.StartHost())
        {
            CurrentMode = ConnectionMode.None;
            Debug.LogError($"[NetworkBootstrap] StartLanHost FAILED: NetworkManager.StartHost() returned false. " +
                           $"Port {port} may already be in use (another host running on this machine?).");
            return false;
        }

        Debug.Log($"[NetworkBootstrap] LAN HOST STARTED on {localIp}:{port}. Broadcasting for discovery.");

        // Discovery failing is non-fatal — host still reachable by IP.
        LanDiscovery.GetOrCreate().StartHostBroadcast(port, SystemInfo.deviceName);
        NetworkManager.Singleton.OnServerStopped += HandleServerStoppedStopLanBroadcast;
        return true;
    }

    /// <summary>
    /// Joins a LAN host at <paramref name="address"/>:<paramref name="port"/>
    /// (normally taken from a LanDiscovery result). Returns true when the NGO
    /// client STARTED — the connection itself completes (or fails) async and is
    /// reported via the NetworkManager connect/disconnect callbacks.
    /// </summary>
    public bool StartLanClient(string address, ushort port = DefaultLanPort)
    {
        if (!LanPreflightChecksPass(nameof(StartLanClient)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(address) || !System.Net.IPAddress.TryParse(address.Trim(), out _))
        {
            Debug.LogError($"[NetworkBootstrap] StartLanClient FAILED: '{address}' is not a valid IP address.");
            return false;
        }

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(address.Trim(), port);

        SubscribeConnectionCallbacks();
        JoinCode = null;
        CurrentMode = ConnectionMode.Lan;

        if (!NetworkManager.Singleton.StartClient())
        {
            CurrentMode = ConnectionMode.None;
            Debug.LogError("[NetworkBootstrap] StartLanClient FAILED: NetworkManager.StartClient() returned false.");
            return false;
        }

        Debug.Log($"[NetworkBootstrap] LAN CLIENT STARTED, connecting to {address.Trim()}:{port}. Waiting for connection callback...");
        return true;
    }

    private void HandleServerStoppedStopLanBroadcast(bool wasHost)
    {
        if (LanDiscovery.Instance != null)
        {
            LanDiscovery.Instance.StopHostBroadcast();
        }
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStopped -= HandleServerStoppedStopLanBroadcast;
        }
    }

    // ---------- Preflight ----------

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

        return TransportPreflightChecksPass(caller);
    }

    /// <summary>Same checks as the Relay preflight minus the UGS sign-in — LAN must work fully offline.</summary>
    private bool LanPreflightChecksPass(string caller)
    {
        if (IsBusy)
        {
            Debug.LogWarning($"[NetworkBootstrap] {caller} ignored: another host/join attempt is already in flight.");
            return false;
        }

        return TransportPreflightChecksPass(caller);
    }

    private bool TransportPreflightChecksPass(string caller)
    {
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
