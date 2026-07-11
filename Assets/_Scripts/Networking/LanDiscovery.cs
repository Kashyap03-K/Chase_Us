using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// LAN session discovery for KAS-23 (E2) — lets clients find a host on the same
/// network without typing an IP.
///
/// Host side: broadcasts a small "session available" UDP packet once per second
/// to every reachable broadcast address (global 255.255.255.255 plus each NIC's
/// subnet-directed broadcast, so it works across common router configs).
/// Client side: binds the discovery port with address-reuse (so several
/// ParrelSync editor instances on ONE machine can all listen at once) and
/// collects sessions, aging them out when the pings stop.
///
/// Deliberately single-threaded: all socket work is non-blocking polling from
/// Update(), so there are no background threads touching Unity state.
///
/// Packet format (UTF-8): "CHUS1|&lt;gamePort&gt;|&lt;sessionName&gt;".
/// Anything that doesn't parse is ignored — nothing here is trusted beyond
/// "there is a host at this address"; the actual game traffic is NGO's.
/// </summary>
public class LanDiscovery : MonoBehaviour
{
    /// <summary>UDP port the discovery pings travel on (NOT the game port).</summary>
    public const int DiscoveryPort = 47777;

    private const string Magic = "CHUS1";
    private const float BroadcastIntervalSeconds = 1f;
    private const float SessionStaleSeconds = 3.5f;

    /// <summary>A host found on the local network.</summary>
    public class LanSession
    {
        public string Address;
        public ushort GamePort;
        public string Name;
        public float LastSeenRealtime;
        public string Key => $"{Address}:{GamePort}";
    }

    public static LanDiscovery Instance { get; private set; }

    /// <summary>Sessions currently visible on the network (client/listen mode).</summary>
    public readonly List<LanSession> Sessions = new List<LanSession>();

    /// <summary>Fired whenever <see cref="Sessions"/> gained, lost or refreshed an entry.</summary>
    public event Action SessionsChanged;

    public bool IsBroadcasting => broadcastClient != null;
    public bool IsListening => listenClient != null;

    private UdpClient broadcastClient;
    private byte[] broadcastPayload;
    private float nextBroadcastAt;

    private UdpClient listenClient;

    /// <summary>Finds the existing instance or creates a persistent hidden one.</summary>
    public static LanDiscovery GetOrCreate()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("LanDiscovery");
            DontDestroyOnLoad(go);
            go.AddComponent<LanDiscovery>();
        }
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ---------- Host: broadcast ----------

    /// <summary>
    /// Starts announcing a hosted session. Returns false (with a logged error)
    /// if the socket could not be created — the host itself is unaffected;
    /// clients just won't auto-discover it.
    /// </summary>
    public bool StartHostBroadcast(ushort gamePort, string sessionName)
    {
        StopHostBroadcast();
        try
        {
            broadcastClient = new UdpClient();
            broadcastClient.EnableBroadcast = true;

            string safeName = string.IsNullOrWhiteSpace(sessionName) ? "CHASE US HOST" : sessionName.Replace("|", " ").Trim();
            if (safeName.Length > 24) safeName = safeName.Substring(0, 24);
            broadcastPayload = Encoding.UTF8.GetBytes($"{Magic}|{gamePort}|{safeName}");
            nextBroadcastAt = 0f; // send immediately on next Update
            Debug.Log($"[LanDiscovery] Broadcasting LAN session '{safeName}' (game port {gamePort}) on UDP {DiscoveryPort}.");
            return true;
        }
        catch (SocketException e)
        {
            Debug.LogError($"[LanDiscovery] Could not start session broadcast: {e.Message}. " +
                           "Clients will not auto-discover this host.");
            broadcastClient = null;
            return false;
        }
    }

    public void StopHostBroadcast()
    {
        if (broadcastClient == null) return;
        try { broadcastClient.Close(); } catch { /* closing best-effort */ }
        broadcastClient = null;
        Debug.Log("[LanDiscovery] Stopped session broadcast.");
    }

    // ---------- Client: listen ----------

    /// <summary>
    /// Starts listening for host announcements. Returns false (with a logged
    /// error) if the discovery port could not be bound.
    /// </summary>
    public bool StartListening()
    {
        if (listenClient != null) return true;
        try
        {
            listenClient = new UdpClient();
            // ReuseAddress lets multiple editor instances (ParrelSync clones) on the
            // same machine all bind 47777 — broadcasts are delivered to every one.
            listenClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listenClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            Debug.Log($"[LanDiscovery] Listening for LAN sessions on UDP {DiscoveryPort}.");
            return true;
        }
        catch (SocketException e)
        {
            Debug.LogError($"[LanDiscovery] Could not listen on UDP {DiscoveryPort}: {e.Message}. " +
                           "Another application may have the port bound exclusively.");
            listenClient = null;
            return false;
        }
    }

    public void StopListening()
    {
        if (listenClient == null) return;
        try { listenClient.Close(); } catch { /* closing best-effort */ }
        listenClient = null;
        if (Sessions.Count > 0)
        {
            Sessions.Clear();
            SessionsChanged?.Invoke();
        }
        Debug.Log("[LanDiscovery] Stopped listening for LAN sessions.");
    }

    // ---------- Pump ----------

    private void Update()
    {
        if (broadcastClient != null && Time.realtimeSinceStartup >= nextBroadcastAt)
        {
            nextBroadcastAt = Time.realtimeSinceStartup + BroadcastIntervalSeconds;
            SendBroadcastPing();
        }

        if (listenClient != null)
        {
            DrainIncomingPings();
            PruneStaleSessions();
        }
    }

    private void SendBroadcastPing()
    {
        foreach (IPAddress target in EnumerateBroadcastAddresses())
        {
            try
            {
                broadcastClient.Send(broadcastPayload, broadcastPayload.Length, new IPEndPoint(target, DiscoveryPort));
            }
            catch (SocketException)
            {
                // Some NICs (VPN adapters etc.) reject broadcast sends — not fatal,
                // the other targets still go out. Silence keeps the console usable.
            }
        }
    }

    private void DrainIncomingPings()
    {
        try
        {
            bool changed = false;
            while (listenClient != null && listenClient.Available > 0)
            {
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = listenClient.Receive(ref from);
                changed |= TryIngestPing(data, from);
            }
            if (changed) SessionsChanged?.Invoke();
        }
        catch (SocketException)
        {
            // A remote ICMP "port unreachable" can surface here on Windows; ignore.
        }
        catch (ObjectDisposedException)
        {
            // Closed mid-drain (e.g. Hide() during Update) — fine.
        }
    }

    private bool TryIngestPing(byte[] data, IPEndPoint from)
    {
        string text;
        try { text = Encoding.UTF8.GetString(data); }
        catch { return false; }

        string[] parts = text.Split(new[] { '|' }, 3);
        if (parts.Length != 3 || parts[0] != Magic) return false;
        if (!ushort.TryParse(parts[1], out ushort gamePort)) return false;

        string address = from.Address.ToString();
        string key = $"{address}:{gamePort}";
        foreach (LanSession s in Sessions)
        {
            if (s.Key == key)
            {
                bool renamed = s.Name != parts[2];
                s.Name = parts[2];
                s.LastSeenRealtime = Time.realtimeSinceStartup;
                return renamed;
            }
        }

        Sessions.Add(new LanSession
        {
            Address = address,
            GamePort = gamePort,
            Name = parts[2],
            LastSeenRealtime = Time.realtimeSinceStartup
        });
        Debug.Log($"[LanDiscovery] Found LAN session '{parts[2]}' at {address}:{gamePort}.");
        return true;
    }

    private void PruneStaleSessions()
    {
        bool changed = false;
        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            if (Time.realtimeSinceStartup - Sessions[i].LastSeenRealtime > SessionStaleSeconds)
            {
                Debug.Log($"[LanDiscovery] LAN session '{Sessions[i].Name}' at {Sessions[i].Key} went quiet — removing.");
                Sessions.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) SessionsChanged?.Invoke();
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Global broadcast plus each active NIC's subnet-directed broadcast
    /// (e.g. 192.168.1.255). The global one also loops back to listeners on
    /// this machine, which is what makes same-machine ParrelSync testing work.
    /// </summary>
    private static IEnumerable<IPAddress> EnumerateBroadcastAddresses()
    {
        yield return IPAddress.Broadcast;

        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { yield break; }

        foreach (NetworkInterface nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;

                byte[] addr = ua.Address.GetAddressBytes();
                byte[] mask = ua.IPv4Mask.GetAddressBytes();
                byte[] bcast = new byte[4];
                for (int i = 0; i < 4; i++) bcast[i] = (byte)(addr[i] | ~mask[i]);
                yield return new IPAddress(bcast);
            }
        }
    }

    /// <summary>
    /// Best-guess LAN IPv4 of this machine (first operational non-loopback NIC).
    /// Null when the machine has no usable IPv4 — callers should fall back to 127.0.0.1.
    /// </summary>
    public static string GetLocalIPv4()
    {
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ua.Address.ToString();
                    }
                }
            }
        }
        catch { /* fall through */ }
        return null;
    }

    private void OnDestroy()
    {
        StopHostBroadcast();
        StopListening();
        if (Instance == this) Instance = null;
    }
}
