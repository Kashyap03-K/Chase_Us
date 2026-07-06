using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Temporary immediate-mode debug panel for the Sprint 1 Relay connection test.
/// Host button + join-code field only; delete once real menu UI exists.
/// </summary>
public class NetworkDebugUI : MonoBehaviour
{
    [Tooltip("Max Relay connections for the host, excluding the host itself.")]
    [SerializeField] private int maxConnections = 3;

    private string joinCodeInput = "";
    private string status = "Waiting for Unity Services sign-in...";

    /// <summary>
    /// True while the panel needs mouse interaction (before a host/client
    /// session is running). OrbitCamera checks this before grabbing the
    /// cursor; remove that guard when this debug UI is deleted.
    /// </summary>
    public static bool WantsCursor =>
        NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 320, 240), GUI.skin.box);
        GUILayout.Label("<b>Chase Us — Network Debug</b>", new GUIStyle(GUI.skin.label) { richText = true });

        ServicesBootstrap services = ServicesBootstrap.Instance;
        NetworkBootstrap network = NetworkBootstrap.Instance;

        if (services == null || network == null)
        {
            GUILayout.Label("ServicesBootstrap/NetworkBootstrap missing from scene!");
        }
        else if (!services.IsSignedIn)
        {
            GUILayout.Label(status);
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager nm = NetworkManager.Singleton;
            GUILayout.Label(nm.IsHost ? "Role: HOST" : "Role: CLIENT");

            if (nm.IsHost)
            {
                GUILayout.Label($"Join code: {network.JoinCode}");
                GUILayout.Label($"Connected clients: {nm.ConnectedClients.Count}");
                GUILayout.TextField(network.JoinCode ?? "");
            }
            else
            {
                GUILayout.Label(nm.IsConnectedClient
                    ? $"Connected. LocalClientId: {nm.LocalClientId}"
                    : "Connecting to host...");
            }
        }
        else
        {
            GUILayout.Label($"Signed in: {services.PlayerId}");

            GUI.enabled = !network.IsBusy;
            if (GUILayout.Button("Host (create Relay + join code)"))
            {
                HostClicked();
            }

            GUILayout.BeginHorizontal();
            joinCodeInput = GUILayout.TextField(joinCodeInput, GUILayout.Width(180));
            if (GUILayout.Button("Join"))
            {
                JoinClicked();
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.Label(status);
        }

        GUILayout.EndArea();
    }

    private async void HostClicked()
    {
        Debug.Log("[NetworkDebugUI] Host button clicked — calling StartHostAsync...");
        status = "Creating Relay allocation...";
        string code = await NetworkBootstrap.Instance.StartHostAsync(maxConnections);
        status = code != null ? $"Hosting. Join code: {code}" : "Host FAILED — see Console.";
        Debug.Log($"[NetworkDebugUI] StartHostAsync returned: {(code ?? "null")}");
    }

    private async void JoinClicked()
    {
        status = "Joining Relay allocation...";
        bool started = await NetworkBootstrap.Instance.StartClientAsync(joinCodeInput);
        status = started ? "Client started, connecting..." : "Join FAILED — see Console.";
    }
}
