using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Chase Us lobby room — one screen serves both host and client views,
/// matching KAS-19 v4 Screens 03 (host) and 05 (client). Auto-shows when
/// NGO is listening (host started OR local client connected) and auto-hides
/// on disconnect. Renders the KAS-25 live player list — the widget reads
/// <see cref="NetworkManager.ConnectedClientsIds"/> so it's connection-mode
/// agnostic (works over LAN or Relay identically).
///
/// Host view: room code hero, Copy + New Room actions, Leave + Start Game.
/// Client view: compact code + status line ("Waiting for host to start…"),
/// single Leave Room action.
///
/// Player naming for the MVP: <c>Player &lt;clientId&gt;</c>. Real display
/// names are out of scope for KAS-25 — the acceptance test is just an
/// accurate, live-updating list. Add a name-sync NetworkVariable later.
/// </summary>
public class LobbyRoomUI : MonoBehaviour
{
    [Header("Wiring (auto-found if left null)")]
    [SerializeField] private ModeSelectUI modeSelectUI;

    [Header("Custom button art (optional — falls back to candy pills if null)")]
    [SerializeField] private Sprite startGameSprite;
    [SerializeField] private Sprite leaveRoomSprite;
    [SerializeField] private Sprite copySprite;

    [Header("Button sizes (used when the sprite variant is active)")]
    [SerializeField] private Vector2 startGameButtonSize = new Vector2(400, 100);
    [SerializeField] private Vector2 leaveButtonSize     = new Vector2(300, 100);
    [SerializeField] private Vector2 copyButtonSize      = new Vector2(160, 60);

    [Header("Background")]
    [SerializeField, Tooltip("Optional. If null, uses a solid Ground-color background.")]
    private Sprite backgroundSprite;
    [SerializeField, Range(0f, 1f), Tooltip("Dark overlay opacity on top of the background image — keeps foreground UI legible.")]
    private float backgroundOverlayAlpha = 0.25f;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 90;   // below Map/Mode Select so those cover it if both show
    [SerializeField] private int maxPlayers = 4;

    private CanvasGroup canvasGroup;
    private RectTransform leftColumn;
    private RectTransform playersListRoot;
    private TMP_Text playersCountText;
    private TMP_Text roleEyebrowText;
    private TMP_Text titleText;
    private TMP_Text blurbText;
    private RectTransform codeRow;
    private GameObject codeLabelGO;
    private GameObject codeActionsGO;
    private readonly List<TMP_Text> codeCharTexts = new List<TMP_Text>();
    private Button startGameButton;
    private TMP_Text startGameLabel;
    private GameObject clientStatusLine;
    private bool built;
    private bool hostView;

    // Minimal "drop into the map" start signal — hides the lobby on every peer so
    // the already-spawned players (KAS-27/28) become visible and playable. The
    // real round state machine (countdown, role assignment) remains KAS-10.
    private const string StartGameMessageName = "ChaseUs.StartGame";
    private static readonly Color32[] AvatarPalette =
    {
        new Color32(0xFF, 0x3D, 0x7A, 0xFF), // pink
        new Color32(0x4E, 0xEB, 0xD9, 0xFF), // cyan
        new Color32(0xFF, 0xB8, 0x4E, 0xFF), // amber
        new Color32(0xB4, 0x7C, 0xFF, 0xFF), // chain purple
        new Color32(0x8B, 0x5C, 0xFF, 0xFF), // violet
        new Color32(0x14, 0xA6, 0x97, 0xFF)  // deep teal
    };

    private void Awake()
    {
        if (modeSelectUI == null)
        {
            modeSelectUI = FindFirstObjectByType<ModeSelectUI>();
        }
    }

    private void Start()
    {
        BuildCanvas();
        SubscribeToNetworkEvents();

        // If Play started with NGO already connected (unlikely but defensive), reflect state.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            ShowForCurrentRole();
        }
        else
        {
            Hide();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkEvents();
        IsVisible = false;
    }

    /// <summary>
    /// True while the lobby screen is visible/interactable. OrbitCamera leaves
    /// the cursor unlocked while this is set, so the lobby buttons stay
    /// clickable after the host/client session has started.
    /// </summary>
    public static bool IsVisible { get; private set; }

    // ---------- NGO event wiring ----------

    private void SubscribeToNetworkEvents()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
    }

    private void HandleServerStarted()
    {
        RegisterStartGameMessage();
        ShowForCurrentRole();
        RebuildPlayerList();
    }

    private void HandleClientConnected(ulong clientId)
    {
        // Show the lobby when this client's OWN connection finalizes; also refresh on
        // any subsequent connect (someone else joined) to keep the list live.
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            RegisterStartGameMessage();
            ShowForCurrentRole();
        }
        RebuildPlayerList();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        // Keep list fresh — someone else dropped.
        RebuildPlayerList();
        // If it's *us* dropping, tear down our lobby view.
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            HideAndReturnToMenu();
        }
    }

    private void HandleClientStopped(bool wasHost)
    {
        UnregisterStartGameMessage();
        HideAndReturnToMenu();
    }

    // ---------- Start-game signal (minimal, pre-KAS-10) ----------

    private void RegisterStartGameMessage()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        nm.CustomMessagingManager.RegisterNamedMessageHandler(StartGameMessageName, OnStartGameMessage);
    }

    private void UnregisterStartGameMessage()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        nm.CustomMessagingManager.UnregisterNamedMessageHandler(StartGameMessageName);
    }

    private void OnStartGameMessage(ulong senderClientId, FastBufferReader payload)
    {
        // Only the server may start the game — ignore a spoofing client.
        if (senderClientId != NetworkManager.ServerClientId) return;
        HideForGameplay();
    }

    /// <summary>
    /// Drops this peer out of the lobby and into the map: the networked players
    /// spawned on connect (KAS-27/28) are already standing on their spawn points
    /// behind this screen. Clicking into the game re-locks the cursor (OrbitCamera).
    /// </summary>
    private void HideForGameplay()
    {
        Debug.Log("[LobbyRoomUI] Game started — hiding lobby, dropping into the map.");
        Hide();
    }

    // ---------- Show/hide orchestration ----------

    private void ShowForCurrentRole()
    {
        if (!built) return;

        NetworkManager nm = NetworkManager.Singleton;
        hostView = nm != null && nm.IsHost;

        ApplyRoleView();
        Show();

        // Ensure entry menus are down too — Confirm on Map Select left Mode/Map hidden already,
        // but be defensive if state got out of sync.
        if (modeSelectUI != null) modeSelectUI.Hide();
    }

    private void HideAndReturnToMenu()
    {
        Hide();
        // Route the local player back to Mode Select so they don't stare at a dead
        // scene — unless the LAN screen is up handling its own join failure, in
        // which case popping Mode Select would fight it for the screen.
        if (modeSelectUI != null && !LanConnectUI.IsVisible) modeSelectUI.Show();
    }

    private void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        IsVisible = true;
    }

    private void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        IsVisible = false;
    }

    // ---------- Canvas construction ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("LobbyRoomCanvas", typeof(RectTransform));
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
        RectTransform grid = BuildTwoColumnGrid(canvasGO.transform);
        BuildLeftColumn(grid);
        BuildRightColumn(grid);

        built = true;
    }

    private void BuildBackground(Transform parent)
    {
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)bg.transform;
        Stretch(rt);
        Image img = bg.GetComponent<Image>();
        img.raycastTarget = true;

        if (backgroundSprite != null)
        {
            img.sprite = backgroundSprite;
            img.color = Color.white;
            img.preserveAspect = false;
        }
        else
        {
            img.color = UITheme.Ground;
        }

        if (backgroundSprite != null && backgroundOverlayAlpha > 0f)
        {
            GameObject overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(parent, false);
            RectTransform overlayRT = (RectTransform)overlay.transform;
            Stretch(overlayRT);
            Image overlayImg = overlay.GetComponent<Image>();
            Color32 ground = UITheme.Ground;
            overlayImg.color = new Color(ground.r / 255f, ground.g / 255f, ground.b / 255f, backgroundOverlayAlpha);
            overlayImg.raycastTarget = false;
        }
    }

    private RectTransform BuildTwoColumnGrid(Transform parent)
    {
        GameObject grid = new GameObject("Grid", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        grid.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)grid.transform;
        rt.anchorMin = new Vector2(0.08f, 0.1f);
        rt.anchorMax = new Vector2(0.92f, 0.9f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        HorizontalLayoutGroup hlg = grid.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.UpperLeft;
        hlg.spacing = 40;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        // Force expand so the two columns divide the grid width by their LayoutElement
        // flexibleWidth ratios (1.15 : 1). Without this they collapse to preferred width.
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        return rt;
    }

    private void BuildLeftColumn(Transform parent)
    {
        GameObject col = new GameObject("LeftColumn", typeof(RectTransform), typeof(VerticalLayoutGroup));
        col.transform.SetParent(parent, false);
        LayoutElement le = col.AddComponent<LayoutElement>();
        le.flexibleWidth = 1.15f;

        VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.spacing = 18;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        leftColumn = (RectTransform)col.transform;

        roleEyebrowText = CreateText(col.transform, "Eyebrow", "ROOM LIVE · RELAY ALLOCATION ACTIVE", 12, UITheme.TextMuted, letterSpacing: 8);
        roleEyebrowText.alignment = TextAlignmentOptions.Left;

        titleText = CreateText(col.transform, "Title", "YOUR ROOM IS OPEN.", 44, UITheme.Text, style: FontStyles.Bold, letterSpacing: 3, wrap: false);
        titleText.alignment = TextAlignmentOptions.Left;

        blurbText = CreateText(col.transform, "Blurb", "Share this code with your friends. They pick Play Online, enter the code, and drop in.", 13, UITheme.TextMuted);
        blurbText.alignment = TextAlignmentOptions.Left;

        TMP_Text codeLabel = CreateText(col.transform, "CodeLabel", "ROOM CODE", 11, UITheme.TextDim, letterSpacing: 8);
        codeLabelGO = codeLabel.gameObject;

        BuildCodeRow(col.transform);
        BuildCodeActions(col.transform);
        BuildClientStatusLine(col.transform);
    }

    private void BuildCodeRow(Transform parent)
    {
        GameObject row = new GameObject("CodeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = 8;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 80;

        codeRow = (RectTransform)row.transform;
        codeCharTexts.Clear();
        for (int i = 0; i < 6; i++)
        {
            codeCharTexts.Add(BuildCodeTile(row.transform, "-"));
        }
    }

    private TMP_Text BuildCodeTile(Transform parent, string ch)
    {
        // Rounded rune slot: warm gold border (AccentDk) around a dark plum interior,
        // character rendered in aged-gold serif. Empty slots (ch == "-") show a faint
        // dim character; filled slots pop with the bright accent gold.
        GameObject tile = new GameObject("CodeTile", typeof(RectTransform), typeof(Image));
        tile.transform.SetParent(parent, false);
        LayoutElement le = tile.AddComponent<LayoutElement>();
        le.preferredWidth = 72;
        le.preferredHeight = 88;

        Image bg = tile.GetComponent<Image>();
        bg.sprite = UITheme.PillSprite;
        bg.type = Image.Type.Sliced;
        bg.color = UITheme.AccentDk;
        bg.raycastTarget = false;

        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(tile.transform, false);
        RectTransform iRT = (RectTransform)inner.transform;
        iRT.anchorMin = Vector2.zero;
        iRT.anchorMax = Vector2.one;
        iRT.offsetMin = new Vector2(3, 3);
        iRT.offsetMax = new Vector2(-3, -3);
        Image iImg = inner.GetComponent<Image>();
        iImg.sprite = UITheme.PillSprite;
        iImg.type = Image.Type.Sliced;
        iImg.color = UITheme.Surface;
        iImg.raycastTarget = false;

        TMP_Text t = CreateText(inner.transform, "Char", ch, 44, UITheme.Accent, style: FontStyles.Bold, letterSpacing: 0, wrap: false);
        t.outlineColor = UITheme.AccentDk;
        t.outlineWidth = 0.18f;
        Stretch(t.rectTransform);
        t.alignment = TextAlignmentOptions.Center;
        return t;
    }

    private void BuildCodeActions(Transform parent)
    {
        GameObject row = new GameObject("CodeActions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        codeActionsGO = row;
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = 8;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 40;

        if (copySprite != null)
            BuildActionImageButton(row.transform, "COPY", copySprite, OnCopyClicked, flexWidth: 0f, preferredWidth: (int)copyButtonSize.x, preferredHeight: (int)copyButtonSize.y);
        else
            BuildSmallGhostButton(row.transform, "COPY", OnCopyClicked, width: 92);
    }

    private void BuildClientStatusLine(Transform parent)
    {
        clientStatusLine = new GameObject("ClientStatus", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        clientStatusLine.transform.SetParent(parent, false);
        Image bg = clientStatusLine.GetComponent<Image>();
        bg.color = UITheme.SurfaceHi;
        bg.raycastTarget = false;
        HorizontalLayoutGroup hlg = clientStatusLine.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(14, 14, 12, 12);
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        LayoutElement le = clientStatusLine.AddComponent<LayoutElement>();
        le.preferredHeight = 48;

        // Simple pulsing dot as a lightweight "waiting" cue (no spinner mesh required).
        GameObject dot = new GameObject("PulseDot", typeof(RectTransform), typeof(Image));
        dot.transform.SetParent(clientStatusLine.transform, false);
        LayoutElement dLe = dot.AddComponent<LayoutElement>();
        dLe.preferredWidth = 10;
        dLe.preferredHeight = 10;
        Image dImg = dot.GetComponent<Image>();
        dImg.color = UITheme.Accent2;
        dImg.raycastTarget = false;

        TMP_Text txt = CreateText(clientStatusLine.transform, "Text", "Waiting for the host to start the game…", 13, UITheme.TextMuted);
        txt.alignment = TextAlignmentOptions.Left;

        clientStatusLine.SetActive(false);
    }

    private void BuildRightColumn(Transform parent)
    {
        GameObject col = new GameObject("RightColumn", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        col.transform.SetParent(parent, false);
        LayoutElement le = col.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;

        Image bg = col.GetComponent<Image>();
        bg.color = UITheme.Surface;
        bg.raycastTarget = true;

        VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 12;
        vlg.padding = new RectOffset(24, 24, 20, 20);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        BuildPlayersHeader(col.transform);
        BuildPlayersListContainer(col.transform);
        BuildRoomActions(col.transform);
    }

    private void BuildPlayersHeader(Transform parent)
    {
        GameObject row = new GameObject("PlayersHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(0, 0, 0, 8);
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 40;

        TMP_Text title = CreateText(row.transform, "Title", "PLAYERS", 18, UITheme.Text, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
        title.alignment = TextAlignmentOptions.Left;

        playersCountText = CreateText(row.transform, "Count", "0 / 0", 13, UITheme.TextMuted, wrap: false);
        playersCountText.alignment = TextAlignmentOptions.Right;
    }

    private void BuildPlayersListContainer(Transform parent)
    {
        GameObject list = new GameObject("PlayersList", typeof(RectTransform), typeof(VerticalLayoutGroup));
        list.transform.SetParent(parent, false);
        VerticalLayoutGroup vlg = list.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 8;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        LayoutElement le = list.AddComponent<LayoutElement>();
        le.flexibleHeight = 1f;

        playersListRoot = (RectTransform)list.transform;
    }

    private void BuildRoomActions(Transform parent)
    {
        GameObject row = new GameObject("RoomActions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(0, 0, 12, 0);
        hlg.spacing = 14;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;   // respect each button's preferredWidth
        hlg.childForceExpandHeight = false;  // respect each button's preferredHeight
        LayoutElement le = row.AddComponent<LayoutElement>();
        // Illustrated plaques are ~3.5:1; row height sized so the buttons render at
        // their intended aspect without the layout crushing or stretching them.
        le.preferredHeight = 110;

        if (leaveRoomSprite != null)
            BuildActionImageButton(row.transform, "LEAVE", leaveRoomSprite, OnLeaveClicked, flexWidth: 0f, preferredWidth: (int)leaveButtonSize.x, preferredHeight: (int)leaveButtonSize.y);
        else
            BuildLobbyActionButton(row.transform, "LEAVE", ghost: true, wide: false, onClick: OnLeaveClicked);

        if (startGameSprite != null)
        {
            startGameButton = BuildActionImageButton(row.transform, "START GAME", startGameSprite, OnStartGameClicked, flexWidth: 0f, preferredWidth: (int)startGameButtonSize.x, preferredHeight: (int)startGameButtonSize.y);
            startGameLabel = null; // no separate label — sprite bakes the text in
        }
        else
        {
            GameObject startGO = BuildLobbyActionButton(row.transform, "START GAME", ghost: false, wide: true, onClick: OnStartGameClicked, outLabel: out startGameLabel);
            startGameButton = startGO.GetComponent<Button>();
        }
    }

    // Sprite-based row-action button that fits into the same HorizontalLayoutGroup
    // as the ghost/candy variants — LayoutElement carries the flex/preferred width.
    private Button BuildActionImageButton(Transform parent, string name, Sprite sprite, Action onClick, float flexWidth, int preferredWidth, int preferredHeight = 56)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = flexWidth;
        le.preferredWidth = preferredWidth;
        le.preferredHeight = preferredHeight;

        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = true;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor     = new Color(0.88f, 0.88f, 0.88f, 1f);
        cb.selectedColor    = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.disabledColor    = new Color(0.55f, 0.55f, 0.55f, 1f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.12f;
        btn.colors = cb;
        btn.onClick.AddListener(() => onClick());
        return btn;
    }

    private void BuildSmallGhostButton(Transform parent, string label, Action onClick, int width)
    {
        GameObject btn = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);
        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 32;

        Image bg = btn.GetComponent<Image>();
        bg.color = UITheme.StrokeHi;
        bg.raycastTarget = true;

        Button button = btn.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());

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
        vlg.padding = new RectOffset(8, 8, 4, 4);

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

        CreateText(inner.transform, "Label", label, 11, UITheme.TextMuted, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
    }

    private GameObject BuildLobbyActionButton(Transform parent, string label, bool ghost, bool wide, Action onClick)
    {
        return BuildLobbyActionButton(parent, label, ghost, wide, onClick, out _);
    }

    private GameObject BuildLobbyActionButton(Transform parent, string label, bool ghost, bool wide, Action onClick, out TMP_Text outLabel)
    {
        GameObject btn = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);
        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.flexibleWidth = wide ? 1f : 0f;
        le.preferredWidth = wide ? -1 : 140;
        le.preferredHeight = 52;

        Image bg = btn.GetComponent<Image>();
        bg.raycastTarget = true;
        Button button = btn.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());

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

            outLabel = CreateText(inner.transform, "Label", label, 13, UITheme.TextMuted, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
        }
        else
        {
            // Two-layer: outer AccentDk acts as a warm-gold stroke echoing the
            // CHASE US wordmark's brown outline; inner Accent is the fill.
            bg.color = UITheme.AccentDk;

            GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            inner.transform.SetParent(btn.transform, false);
            RectTransform iRT = (RectTransform)inner.transform;
            iRT.anchorMin = Vector2.zero;
            iRT.anchorMax = Vector2.one;
            iRT.offsetMin = new Vector2(3f, 3f);
            iRT.offsetMax = new Vector2(-3f, -3f);
            Image iImg = inner.GetComponent<Image>();
            iImg.color = UITheme.Accent;
            iImg.raycastTarget = false;

            VerticalLayoutGroup vlg = inner.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = true;
            vlg.padding = new RectOffset(16, 16, 10, 10);

            button.targetGraphic = iImg;
            ColorBlock cb = button.colors;
            cb.normalColor = UITheme.Accent;
            cb.highlightedColor = UITheme.AccentHi;
            cb.pressedColor = UITheme.Accent;
            cb.selectedColor = UITheme.AccentHi;
            cb.disabledColor = UITheme.Surface;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.12f;
            button.colors = cb;

            outLabel = CreateText(inner.transform, "Label", label, 13, UITheme.White, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
        }
        return btn;
    }

    // ---------- Role-specific rendering ----------

    private void ApplyRoleView()
    {
        // LAN sessions (KAS-23) have no join code — discovery finds the host —
        // so the code section is Relay-only chrome.
        bool lan = NetworkBootstrap.Instance != null &&
                   NetworkBootstrap.Instance.CurrentMode == NetworkBootstrap.ConnectionMode.Lan;

        if (hostView)
        {
            roleEyebrowText.text = lan ? "ROOM LIVE · LAN · SAME NETWORK" : "ROOM LIVE · RELAY ALLOCATION ACTIVE";
            titleText.text = "YOUR ROOM IS OPEN.";
            blurbText.text = lan
                ? "Friends on your network pick Play LAN — your game shows up on their list automatically."
                : "Share this code with your friends. They pick Play Online, enter the code, and drop in.";
            if (clientStatusLine != null) clientStatusLine.SetActive(false);
            if (startGameButton != null) startGameButton.gameObject.SetActive(true);
        }
        else
        {
            roleEyebrowText.text = lan ? "CONNECTED TO HOST · LAN" : "CONNECTED TO HOST · RELAY";
            titleText.text = "YOU'RE IN.";
            blurbText.text = "Sit tight — the host will start the round when everyone's ready.";
            if (clientStatusLine != null) clientStatusLine.SetActive(true);
            if (startGameButton != null) startGameButton.gameObject.SetActive(false);
        }

        if (codeLabelGO != null) codeLabelGO.SetActive(!lan);
        if (codeRow != null) codeRow.gameObject.SetActive(!lan);
        if (codeActionsGO != null) codeActionsGO.SetActive(!lan);

        UpdateRoomCode();
    }

    private void UpdateRoomCode()
    {
        string code = NetworkBootstrap.Instance != null ? (NetworkBootstrap.Instance.JoinCode ?? "") : "";
        for (int i = 0; i < codeCharTexts.Count; i++)
        {
            bool filled = i < code.Length;
            TMP_Text t = codeCharTexts[i];
            t.text = filled ? code[i].ToString() : "•";
            // Filled tiles pop with bright accent gold; empty tiles show a dim dot.
            t.color = filled ? (Color)UITheme.Accent : (Color)UITheme.TextDim;
        }
    }

    // ---------- Player list ----------

    private void RebuildPlayerList()
    {
        if (!built || playersListRoot == null) return;

        NetworkManager nm = NetworkManager.Singleton;

        // Clear existing slots. Destroy immediate is fine here — the layout will refresh next frame.
        for (int i = playersListRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(playersListRoot.GetChild(i).gameObject);
        }

        int connected = 0;
        if (nm != null && nm.IsListening)
        {
            // NGO 2.x populates ConnectedClientsIds on both server and client, so hosts and
            // joiners see the same list without a custom NetworkList broadcast. If a future
            // NGO version regresses this to server-only, clients will render an incomplete list
            // and we'd add a `NetworkList<ulong>` mirrored from the server.
            foreach (ulong clientId in nm.ConnectedClientsIds)
            {
                BuildPlayerSlot(clientId, isHostSlot: clientId == NetworkManager.ServerClientId, isLocalSlot: clientId == nm.LocalClientId);
                connected++;
            }
        }

        int emptyToShow = Math.Max(0, maxPlayers - connected);
        for (int i = 0; i < emptyToShow; i++)
        {
            BuildEmptySlot();
        }

        playersCountText.text = $"{connected} / {maxPlayers}";

        UpdateStartButtonInteractable(connected);
    }

    private void UpdateStartButtonInteractable(int connectedCount)
    {
        if (startGameButton == null) return;
        bool enable = hostView && connectedCount >= 2;
        startGameButton.interactable = enable;
        if (startGameLabel != null)
        {
            startGameLabel.color = enable ? (Color)UITheme.White : (Color)UITheme.TextDim;
        }
    }

    private void BuildPlayerSlot(ulong clientId, bool isHostSlot, bool isLocalSlot)
    {
        GameObject slot = new GameObject($"Slot_{clientId}", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        slot.transform.SetParent(playersListRoot, false);
        LayoutElement le = slot.AddComponent<LayoutElement>();
        le.preferredHeight = 56;

        Image bg = slot.GetComponent<Image>();
        bg.color = UITheme.SurfaceHi;
        bg.raycastTarget = false;

        HorizontalLayoutGroup hlg = slot.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(14, 14, 10, 10);
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        BuildAvatar(slot.transform, clientId);
        BuildPlayerInfoBlock(slot.transform, clientId, isLocalSlot);
        if (isHostSlot) BuildBadge(slot.transform, "HOST", solid: true);
        else if (isLocalSlot) BuildBadge(slot.transform, "YOU", solid: false);
    }

    private void BuildEmptySlot()
    {
        GameObject slot = new GameObject("EmptySlot", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        slot.transform.SetParent(playersListRoot, false);
        LayoutElement le = slot.AddComponent<LayoutElement>();
        le.preferredHeight = 48;

        Image bg = slot.GetComponent<Image>();
        bg.color = new Color(0, 0, 0, 0);
        bg.raycastTarget = false;

        // Dashed border using an inset Image with transparent fill and a colored outer would be
        // ideal, but Unity Image without a sprite draws a solid rectangle. For MVP: solid muted
        // border (Stroke color) as the visual — same idea, single draw.
        Image border = new GameObject("Border", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        border.gameObject.transform.SetParent(slot.transform, false);
        RectTransform bRT = (RectTransform)border.transform;
        Stretch(bRT);
        border.color = UITheme.Stroke;
        border.raycastTarget = false;
        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(border.transform, false);
        RectTransform iRT = (RectTransform)inner.transform;
        iRT.anchorMin = Vector2.zero;
        iRT.anchorMax = Vector2.one;
        iRT.offsetMin = new Vector2(1.5f, 1.5f);
        iRT.offsetMax = new Vector2(-1.5f, -1.5f);
        Image iImg = inner.GetComponent<Image>();
        iImg.color = UITheme.Ground;
        iImg.raycastTarget = false;

        HorizontalLayoutGroup hlg = slot.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(14, 14, 10, 10);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        TMP_Text txt = CreateText(inner.transform, "Label", "WAITING FOR PLAYER…", 10, UITheme.TextDim, letterSpacing: 8, wrap: false);
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;
    }

    private void BuildAvatar(Transform parent, ulong clientId)
    {
        GameObject av = new GameObject("Avatar", typeof(RectTransform), typeof(Image));
        av.transform.SetParent(parent, false);
        LayoutElement le = av.AddComponent<LayoutElement>();
        le.preferredWidth = 36;
        le.preferredHeight = 36;
        Image bg = av.GetComponent<Image>();
        bg.color = AvatarPalette[(int)(clientId % (ulong)AvatarPalette.Length)];
        bg.raycastTarget = false;

        TMP_Text initial = CreateText(av.transform, "Initial", InitialFor(clientId), 15, UITheme.White, style: FontStyles.Bold, wrap: false);
        Stretch(initial.rectTransform);
        initial.alignment = TextAlignmentOptions.Center;
    }

    private void BuildPlayerInfoBlock(Transform parent, ulong clientId, bool isLocal)
    {
        GameObject info = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup));
        info.transform.SetParent(parent, false);
        LayoutElement le = info.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredWidth = 100;

        VerticalLayoutGroup vlg = info.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.spacing = 2;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        string displayName = isLocal ? $"You (Player {clientId})" : $"Player {clientId}";
        TMP_Text nameT = CreateText(info.transform, "Name", displayName, 14, UITheme.Text, style: FontStyles.Bold, wrap: false);
        nameT.alignment = TextAlignmentOptions.Left;

        TMP_Text status = CreateText(info.transform, "Status", "READY", 10, UITheme.Accent2, letterSpacing: 4, wrap: false);
        status.alignment = TextAlignmentOptions.Left;
    }

    private void BuildBadge(Transform parent, string label, bool solid)
    {
        GameObject badge = new GameObject("Badge", typeof(RectTransform), typeof(Image));
        badge.transform.SetParent(parent, false);
        LayoutElement le = badge.AddComponent<LayoutElement>();
        le.preferredWidth = 46;
        le.preferredHeight = 20;

        Image bg = badge.GetComponent<Image>();
        bg.raycastTarget = false;

        if (solid)
        {
            bg.color = UITheme.Accent;
            TMP_Text t = CreateText(badge.transform, "Label", label, 9, UITheme.White, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            bg.color = UITheme.Accent2;

            GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
            inner.transform.SetParent(badge.transform, false);
            RectTransform iRT = (RectTransform)inner.transform;
            iRT.anchorMin = Vector2.zero;
            iRT.anchorMax = Vector2.one;
            iRT.offsetMin = new Vector2(1f, 1f);
            iRT.offsetMax = new Vector2(-1f, -1f);
            Image iImg = inner.GetComponent<Image>();
            iImg.color = UITheme.SurfaceHi;
            iImg.raycastTarget = false;

            TMP_Text t = CreateText(inner.transform, "Label", label, 9, UITheme.Accent2, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
        }
    }

    private string InitialFor(ulong clientId)
    {
        // "Player 0" → "P", but pick a deterministic distinct letter per id for MVP flair.
        // Cycle A..Z.
        char c = (char)('A' + (int)(clientId % 26));
        return c.ToString();
    }

    // ---------- Interactions ----------

    private void OnCopyClicked()
    {
        string code = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.JoinCode : null;
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("[LobbyRoomUI] Copy clicked with no join code available.");
            return;
        }
        GUIUtility.systemCopyBuffer = code;
        Debug.Log($"[LobbyRoomUI] Copied join code to clipboard: {code}");
    }

    private void OnLeaveClicked()
    {
        Debug.Log("[LobbyRoomUI] Leave clicked — shutting down NetworkManager.");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        HideAndReturnToMenu();
    }

    private void OnStartGameClicked()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost)
        {
            Debug.LogWarning("[LobbyRoomUI] Start Game clicked but this client is not the host — ignoring.");
            return;
        }
        int connected = nm.ConnectedClientsIds.Count;
        if (connected < 2)
        {
            Debug.LogWarning($"[LobbyRoomUI] Start Game requires ≥ 2 players; only {connected} connected.");
            return;
        }

        // Minimal start (pre-KAS-10): tell every peer to drop out of the lobby and
        // into the map, where the networked players already spawned on connect.
        // TODO(Sprint 3 · KAS-10): replace with the round state machine — advance to
        // WaitingForPlayers (in-map countdown), then RoleAssignment.
        Debug.Log($"[LobbyRoomUI] START GAME · {connected} players. Broadcasting lobby dismissal.");
        using (FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp))
        {
            nm.CustomMessagingManager.SendNamedMessageToAll(StartGameMessageName, writer);
        }
        // Named messages don't loop back to the host — dismiss locally too.
        HideForGameplay();
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
