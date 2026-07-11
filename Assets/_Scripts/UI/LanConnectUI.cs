using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Chase Us LAN screen — KAS-23 (E2). Reached from Mode Select's "Play LAN".
/// One screen for both roles: a HOST button at the top, and a live list of
/// games discovered on the local network below it (via <see cref="LanDiscovery"/>),
/// each with its own JOIN button. No IP typing, no internet, no Unity Services.
///
/// Failure handling is explicit: host-start failure (usually the game port
/// already bound by another instance on this machine), no sessions found,
/// and join timeout all surface as on-screen messages rather than silence.
/// </summary>
public class LanConnectUI : MonoBehaviour
{
    [Header("Wiring (auto-found if left null)")]
    [SerializeField] private ModeSelectUI modeSelectUI;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;

    [Header("Join")]
    [Tooltip("Seconds to wait for a LAN connection before giving up. UnityTransport's own default is a full minute — far too long to sit on a dead join.")]
    [SerializeField] private float joinTimeoutSeconds = 10f;

    private CanvasGroup canvasGroup;
    private RectTransform sessionsListRoot;
    private TMP_Text statusText;
    private TMP_Text errorText;
    private Button hostButton;
    private TMP_Text hostButtonLabel;

    private bool joining;
    private float joinDeadline;
    private string joiningTargetName;

    /// <summary>True while this screen is shown — lets OrbitCamera yield the cursor.</summary>
    public static bool IsVisible { get; private set; }

    private void Awake()
    {
        if (modeSelectUI == null) modeSelectUI = FindAnyObjectByType<ModeSelectUI>();
    }

    private void Start()
    {
        BuildCanvas();
        SubscribeToNetworkEvents();
        Hide();
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkEvents();
        if (LanDiscovery.Instance != null)
        {
            LanDiscovery.Instance.SessionsChanged -= RebuildSessionList;
        }
    }

    public void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        IsVisible = true;

        joining = false;
        SetHostButtonState(idle: true);
        if (errorText != null) errorText.gameObject.SetActive(false);

        LanDiscovery discovery = LanDiscovery.GetOrCreate();
        discovery.SessionsChanged -= RebuildSessionList; // defensive: never double-subscribe
        discovery.SessionsChanged += RebuildSessionList;
        if (!discovery.StartListening())
        {
            ShowError("Couldn't open the discovery port — close other Chase Us instances scanning for games.");
        }
        RebuildSessionList();
    }

    public void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        IsVisible = false;
        joining = false;

        if (LanDiscovery.Instance != null)
        {
            LanDiscovery.Instance.SessionsChanged -= RebuildSessionList;
            LanDiscovery.Instance.StopListening();
        }
    }

    private void Update()
    {
        if (!joining) return;
        if (Time.realtimeSinceStartup < joinDeadline) return;

        // Watchdog: connection didn't complete in time. Tear the client down and
        // put the screen back into a usable state with a clear message.
        joining = false;
        Debug.LogWarning($"[LanConnectUI] Join of '{joiningTargetName}' timed out after {joinTimeoutSeconds}s — shutting client down.");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        SetHostButtonState(idle: true);
        RebuildSessionList();
        ShowError($"Couldn't reach '{joiningTargetName}' — the host may have closed, or a firewall is blocking it.");
    }

    // ---------- NGO event wiring (join outcome) ----------

    private void SubscribeToNetworkEvents()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!joining || NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        // Connected — LobbyRoomUI shows itself off the same callback; we just leave.
        joining = false;
        Hide();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!joining || NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        joining = false;
        string reason = NetworkManager.Singleton.DisconnectReason;
        Debug.LogWarning($"[LanConnectUI] LAN join failed (reason: {(string.IsNullOrEmpty(reason) ? "none given" : reason)}).");
        SetHostButtonState(idle: true);
        RebuildSessionList();
        ShowError($"Couldn't join '{joiningTargetName}' — the host may have closed.");
    }

    // ---------- Canvas construction ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("LanConnectCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        canvasGroup = canvasGO.AddComponent<CanvasGroup>();

        BuildBackground(canvasGO.transform);
        RectTransform col = BuildColumn(canvasGO.transform);
        BuildHeader(col);
        BuildHostSection(col);
        BuildListHeader(col);
        BuildSessionsList(col);
        BuildStatusLine(col);
        BuildErrorLine(col);
        BuildActions(col);
    }

    private void BuildBackground(Transform parent)
    {
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        Stretch((RectTransform)bg.transform);
        Image img = bg.GetComponent<Image>();
        img.color = UITheme.Ground;
        img.raycastTarget = true;
    }

    private RectTransform BuildColumn(Transform parent)
    {
        GameObject col = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
        col.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)col.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(760, 820);
        rt.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 18;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return rt;
    }

    private void BuildHeader(Transform parent)
    {
        GameObject header = new GameObject("Header", typeof(RectTransform), typeof(VerticalLayoutGroup));
        header.transform.SetParent(parent, false);
        VerticalLayoutGroup vlg = header.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 10;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        CreateText(header.transform, "Eyebrow", "SAME NETWORK · NO INTERNET NEEDED", 14, UITheme.TextMuted, letterSpacing: 12);
        CreateText(header.transform, "Title", "PLAY ON YOUR LAN", 48, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(header.transform, "Blurb", "Host a game, or join one that appears below. Everyone must be on the same Wi-Fi or network.", 14, UITheme.TextMuted);
    }

    private void BuildHostSection(Transform parent)
    {
        GameObject row = new GameObject("HostRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 64;

        GameObject hostGO = BuildActionButton(row.transform, "HOST LAN GAME", ghost: false, onClick: OnHostClicked, out hostButton, out hostButtonLabel);
        hostGO.GetComponent<LayoutElement>().preferredWidth = 320;
    }

    private void BuildListHeader(Transform parent)
    {
        GameObject wrap = new GameObject("ListHeader", typeof(RectTransform));
        wrap.transform.SetParent(parent, false);
        LayoutElement le = wrap.AddComponent<LayoutElement>();
        le.preferredHeight = 26;

        TMP_Text t = CreateText(wrap.transform, "Text", "GAMES ON YOUR NETWORK", 12, UITheme.TextDim, letterSpacing: 10);
        Stretch(t.rectTransform);
        t.alignment = TextAlignmentOptions.Center;
    }

    private void BuildSessionsList(Transform parent)
    {
        GameObject list = new GameObject("SessionsList", typeof(RectTransform), typeof(VerticalLayoutGroup));
        list.transform.SetParent(parent, false);
        VerticalLayoutGroup vlg = list.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 8;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        LayoutElement le = list.AddComponent<LayoutElement>();
        le.preferredHeight = 260;
        le.flexibleHeight = 0f;

        sessionsListRoot = (RectTransform)list.transform;
    }

    private void BuildStatusLine(Transform parent)
    {
        GameObject wrap = new GameObject("Status", typeof(RectTransform));
        wrap.transform.SetParent(parent, false);
        LayoutElement le = wrap.AddComponent<LayoutElement>();
        le.preferredHeight = 20;

        statusText = CreateText(wrap.transform, "Text", "SCANNING YOUR NETWORK…", 12, UITheme.TextDim, letterSpacing: 6);
        Stretch(statusText.rectTransform);
        statusText.alignment = TextAlignmentOptions.Center;
    }

    private void BuildErrorLine(Transform parent)
    {
        GameObject wrap = new GameObject("Error", typeof(RectTransform));
        wrap.transform.SetParent(parent, false);
        LayoutElement le = wrap.AddComponent<LayoutElement>();
        le.preferredHeight = 20;

        errorText = CreateText(wrap.transform, "Text", "", 12, UITheme.Danger, letterSpacing: 4);
        Stretch(errorText.rectTransform);
        errorText.alignment = TextAlignmentOptions.Center;
        errorText.gameObject.SetActive(false);
    }

    private void BuildActions(Transform parent)
    {
        GameObject row = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 12;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 60;

        BuildActionButton(row.transform, "BACK", ghost: true, onClick: OnBackClicked, out _, out _);

        GameObject hint = new GameObject("FirewallHint", typeof(RectTransform));
        hint.transform.SetParent(parent, false);
        LayoutElement hintLE = hint.AddComponent<LayoutElement>();
        hintLE.preferredHeight = 18;
        TMP_Text hintText = CreateText(hint.transform, "Text", "Nothing showing up? Allow Unity through Windows Firewall for private networks.", 11, UITheme.TextDim);
        Stretch(hintText.rectTransform);
        hintText.alignment = TextAlignmentOptions.Center;
    }

    private GameObject BuildActionButton(Transform parent, string label, bool ghost, Action onClick,
                                          out Button outButton, out TMP_Text outLabel)
    {
        GameObject btn = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);
        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredWidth = 160;
        le.preferredHeight = 56;

        Image bg = btn.GetComponent<Image>();
        bg.raycastTarget = true;
        Button button = btn.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());
        outButton = button;

        if (ghost)
        {
            bg.color = UITheme.StrokeHi;

            GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            inner.transform.SetParent(btn.transform, false);
            RectTransform iRT = (RectTransform)inner.transform;
            iRT.anchorMin = Vector2.zero;
            iRT.anchorMax = Vector2.one;
            iRT.offsetMin = new Vector2(1.5f, 1.5f);
            iRT.offsetMax = new Vector2(-1.5f, -1.5f);
            Image iImg = inner.GetComponent<Image>();
            iImg.color = UITheme.Ground;
            iImg.raycastTarget = false;

            VerticalLayoutGroup vlg = inner.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = true;
            vlg.padding = new RectOffset(16, 16, 8, 8);

            button.targetGraphic = iImg;
            ColorBlock cb = button.colors;
            cb.normalColor = UITheme.Ground;
            cb.highlightedColor = UITheme.Ground2;
            cb.pressedColor = UITheme.Surface;
            cb.selectedColor = UITheme.Ground2;
            cb.disabledColor = UITheme.Surface;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.12f;
            button.colors = cb;

            outLabel = CreateText(inner.transform, "Label", label, 14, UITheme.TextMuted, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
        }
        else
        {
            bg.color = UITheme.Accent;

            VerticalLayoutGroup vlg = btn.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = true;
            vlg.padding = new RectOffset(16, 16, 8, 8);

            button.targetGraphic = bg;
            ColorBlock cb = button.colors;
            cb.normalColor = UITheme.Accent;
            cb.highlightedColor = UITheme.AccentHi;
            cb.pressedColor = UITheme.Accent;
            cb.selectedColor = UITheme.AccentHi;
            cb.disabledColor = UITheme.Surface;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.12f;
            button.colors = cb;

            outLabel = CreateText(btn.transform, "Label", label, 14, UITheme.White, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
        }
        return btn;
    }

    // ---------- Session list rendering ----------

    private void RebuildSessionList()
    {
        if (sessionsListRoot == null) return;

        for (int i = sessionsListRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(sessionsListRoot.GetChild(i).gameObject);
        }

        IReadOnlyList<LanDiscovery.LanSession> sessions =
            LanDiscovery.Instance != null ? LanDiscovery.Instance.Sessions : (IReadOnlyList<LanDiscovery.LanSession>)Array.Empty<LanDiscovery.LanSession>();

        foreach (LanDiscovery.LanSession session in sessions)
        {
            BuildSessionRow(session);
        }

        if (statusText != null)
        {
            statusText.text = joining
                ? $"JOINING '{joiningTargetName?.ToUpperInvariant()}'…"
                : sessions.Count == 0 ? "SCANNING YOUR NETWORK…" : $"{sessions.Count} GAME{(sessions.Count == 1 ? "" : "S")} FOUND";
        }
    }

    private void BuildSessionRow(LanDiscovery.LanSession session)
    {
        GameObject rowGO = new GameObject($"Session_{session.Key}", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        rowGO.transform.SetParent(sessionsListRoot, false);
        LayoutElement le = rowGO.AddComponent<LayoutElement>();
        le.preferredHeight = 60;

        Image bg = rowGO.GetComponent<Image>();
        bg.color = UITheme.SurfaceHi;
        bg.raycastTarget = false;

        HorizontalLayoutGroup hlg = rowGO.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 12, 8, 8);
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        GameObject info = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup));
        info.transform.SetParent(rowGO.transform, false);
        LayoutElement infoLE = info.AddComponent<LayoutElement>();
        infoLE.flexibleWidth = 1f;
        VerticalLayoutGroup vlg = info.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.spacing = 2;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TMP_Text nameT = CreateText(info.transform, "Name", session.Name, 16, UITheme.Text, style: FontStyles.Bold, wrap: false);
        nameT.alignment = TextAlignmentOptions.Left;
        TMP_Text addrT = CreateText(info.transform, "Addr", $"{session.Address}:{session.GamePort}", 11, UITheme.TextMuted, letterSpacing: 2, wrap: false);
        addrT.alignment = TextAlignmentOptions.Left;

        GameObject joinGO = BuildActionButton(rowGO.transform, "JOIN", ghost: false, onClick: () => OnJoinClicked(session), out Button joinBtn, out _);
        LayoutElement joinLE = joinGO.GetComponent<LayoutElement>();
        joinLE.preferredWidth = 120;
        joinLE.preferredHeight = 44;
        joinBtn.interactable = !joining;
    }

    // ---------- Interactions ----------

    private void OnHostClicked()
    {
        if (joining) return;
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (NetworkBootstrap.Instance == null)
        {
            ShowError("Network not ready — try again.");
            return;
        }

        SetHostButtonState(idle: false);
        bool started = NetworkBootstrap.Instance.StartLanHost();
        if (!started)
        {
            SetHostButtonState(idle: true);
            ShowError($"Couldn't start hosting — port {NetworkBootstrap.DefaultLanPort} may be in use by another instance.");
            return;
        }

        // Success — LobbyRoomUI shows itself off OnServerStarted; this screen retires.
        Hide();
    }

    private void OnJoinClicked(LanDiscovery.LanSession session)
    {
        if (joining) return;
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (NetworkBootstrap.Instance == null)
        {
            ShowError("Network not ready — try again.");
            return;
        }

        joiningTargetName = session.Name;
        bool started = NetworkBootstrap.Instance.StartLanClient(session.Address, session.GamePort);
        if (!started)
        {
            ShowError("Couldn't start joining — see the console for details.");
            return;
        }

        joining = true;
        joinDeadline = Time.realtimeSinceStartup + joinTimeoutSeconds;
        SetHostButtonState(idle: true);
        hostButton.interactable = false;
        RebuildSessionList(); // repaints rows with JOIN disabled + status line
    }

    private void SetHostButtonState(bool idle)
    {
        if (hostButton != null) hostButton.interactable = idle && !joining;
        if (hostButtonLabel != null) hostButtonLabel.text = idle ? "HOST LAN GAME" : "STARTING…";
    }

    private void ShowError(string message)
    {
        if (errorText == null) return;
        errorText.text = message.ToUpperInvariant();
        errorText.gameObject.SetActive(true);
    }

    private void OnBackClicked()
    {
        if (joining && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            // Abandon the in-flight join attempt cleanly.
            NetworkManager.Singleton.Shutdown();
        }
        Hide();
        if (modeSelectUI != null) modeSelectUI.Show();
    }

    // ---------- Shared helpers ----------

    private TMP_Text CreateText(Transform parent, string name, string content, int fontSize, Color color,
                                FontStyles style = FontStyles.Normal, float letterSpacing = 0f, bool wrap = true)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = fontSize;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.characterSpacing = letterSpacing;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = wrap;
#pragma warning restore CS0618
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
