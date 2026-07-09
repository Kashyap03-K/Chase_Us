using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Chase Us map picker — host-only. Sits between Mode Select and the
/// Host Lobby. KAS-19 v4 design reference: lobby-flow.html Screen 02.
///
/// Ships with one real map (Sakri Arena) plus two visible "Coming Soon"
/// placeholders so the pipeline for more maps reads at a glance. Locked
/// tiles are non-interactable; the first non-locked map is preselected.
///
/// Routing:
///   * Confirm → hides itself, re-enables the temporary NetworkDebugUI
///     as the next step (swap-out point once Jaivik's E2 Host Lobby lands).
///     The chosen map ID is stored in SelectedMapId + broadcast via
///     the Confirmed event so downstream code can act on it.
///   * Back    → hides itself, calls Show() on ModeSelectUI so the user
///     returns to the entry menu without losing state.
/// </summary>
public class MapSelectUI : MonoBehaviour
{
    [Serializable]
    public class MapDefinition
    {
        public string id = "map_id";
        public string displayName = "MAP NAME";
        public string metaLine = "N–N PLAYERS";
        [TextArea(2, 4)] public string description = "";
        public bool locked = false;
        [Tooltip("Preview tint shown in the tile's upper area — swap for a real screenshot sprite once the arena is closer to final.")]
        public Color previewTint = new Color(0.17f, 0.38f, 0.24f, 1f);
    }

    [Header("Wiring (auto-found if left null)")]
    [SerializeField] private HostJoinChoiceUI hostJoinChoiceUI;
    [SerializeField] private ModeSelectUI modeSelectUI;
    [SerializeField, Tooltip("Temporary. Replaced by Jaivik's E2 (Host Lobby) when it lands.")]
    private NetworkDebugUI networkDebugUI;

    [Header("Maps")]
    [SerializeField]
    private List<MapDefinition> maps = new List<MapDefinition>
    {
        new MapDefinition
        {
            id = "sakri",
            displayName = "SAKRI ARENA",
            metaLine = "4–8 PLAYERS · OPEN GROUND",
            description = "Grass field with crates, trees, and a truck. Balanced sightlines, mid-range chase distances.",
            locked = false,
            previewTint = new Color(0.10f, 0.29f, 0.17f, 1f)
        },
        new MapDefinition
        {
            id = "locked_1",
            displayName = "???",
            metaLine = "LOCKED",
            description = "A tighter, more vertical map — pitched for Sprint 3.",
            locked = true,
            previewTint = new Color(0.09f, 0.06f, 0.15f, 1f)
        },
        new MapDefinition
        {
            id = "locked_2",
            displayName = "???",
            metaLine = "LOCKED",
            description = "Something new — team pitches next planning cycle.",
            locked = true,
            previewTint = new Color(0.09f, 0.06f, 0.15f, 1f)
        }
    };

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;

    /// <summary>The map ID from the last confirmed selection. Null until Confirm is pressed.</summary>
    public string SelectedMapId { get; private set; }

    /// <summary>Fires when the host confirms their selection. Payload is the map id.</summary>
    public event Action<string> Confirmed;

    private CanvasGroup canvasGroup;
    private readonly List<Image> tileBorders = new List<Image>();
    private readonly List<GameObject> tileSelectedBadges = new List<GameObject>();
    private string highlightedMapId;

    private void Awake()
    {
        if (hostJoinChoiceUI == null)
        {
            hostJoinChoiceUI = FindFirstObjectByType<HostJoinChoiceUI>();
        }
        if (modeSelectUI == null)
        {
            modeSelectUI = FindFirstObjectByType<ModeSelectUI>();
        }
        if (networkDebugUI == null)
        {
            networkDebugUI = FindFirstObjectByType<NetworkDebugUI>();
        }

        foreach (MapDefinition m in maps)
        {
            if (!m.locked)
            {
                highlightedMapId = m.id;
                break;
            }
        }
    }

    private void Start()
    {
        BuildCanvas();
        UpdateTileVisuals();
        Hide();
    }

    public void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
    }

    public void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    // ---------- Canvas construction ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("MapSelectCanvas", typeof(RectTransform));
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
        RectTransform root = BuildRoot(canvasGO.transform);
        BuildHeader(root);
        BuildMapGrid(root);
        BuildActions(root);
    }

    private void BuildBackground(Transform parent)
    {
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)bg.transform;
        Stretch(rt);
        Image img = bg.GetComponent<Image>();
        img.color = UITheme.Ground;
        img.raycastTarget = true;
    }

    private RectTransform BuildRoot(Transform parent)
    {
        GameObject root = new GameObject("Root", typeof(RectTransform), typeof(VerticalLayoutGroup));
        root.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)root.transform;
        rt.anchorMin = new Vector2(0.08f, 0.08f);
        rt.anchorMax = new Vector2(0.92f, 0.92f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        VerticalLayoutGroup vlg = root.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 28;
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
        vlg.spacing = 6;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        CreateText(header.transform, "Eyebrow", "HOST · ROOM SETUP", 14, UITheme.TextMuted, letterSpacing: 12);
        CreateText(header.transform, "Title",   "PICK YOUR ARENA",   48, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(header.transform, "Blurb",   "Only the host chooses — everyone plays the same map. Clients skip this screen.", 15, UITheme.TextMuted);
    }

    private void BuildMapGrid(Transform parent)
    {
        GameObject row = new GameObject("MapRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 16;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 460;

        tileBorders.Clear();
        tileSelectedBadges.Clear();

        for (int i = 0; i < maps.Count; i++)
        {
            BuildMapTile(row.transform, maps[i]);
        }
    }

    private void BuildMapTile(Transform parent, MapDefinition map)
    {
        GameObject card = new GameObject($"Tile_{map.id}", typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(parent, false);

        Image border = card.GetComponent<Image>();
        border.color = UITheme.StrokeHi;
        border.raycastTarget = true;
        tileBorders.Add(border);

        const int borderThickness = 2;
        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        inner.transform.SetParent(card.transform, false);
        RectTransform innerRT = (RectTransform)inner.transform;
        innerRT.anchorMin = Vector2.zero;
        innerRT.anchorMax = Vector2.one;
        innerRT.offsetMin = new Vector2(borderThickness, borderThickness);
        innerRT.offsetMax = new Vector2(-borderThickness, -borderThickness);

        Image innerFill = inner.GetComponent<Image>();
        innerFill.color = UITheme.SurfaceHi;
        innerFill.raycastTarget = false;

        VerticalLayoutGroup innerVlg = inner.GetComponent<VerticalLayoutGroup>();
        innerVlg.childAlignment = TextAnchor.UpperCenter;
        innerVlg.spacing = 0;
        innerVlg.padding = new RectOffset(0, 0, 0, 0);
        innerVlg.childControlWidth = true;
        innerVlg.childControlHeight = true;
        innerVlg.childForceExpandWidth = true;
        innerVlg.childForceExpandHeight = false;

        BuildTilePreview(inner.transform, map);
        BuildTileInfo(inner.transform, map);
        BuildTileSelectedBadge(card.transform);

        Button button = card.GetComponent<Button>();
        button.targetGraphic = innerFill;
        ColorBlock cb = button.colors;
        cb.normalColor = UITheme.SurfaceHi;
        cb.highlightedColor = UITheme.Ground2;
        cb.pressedColor = UITheme.Surface;
        cb.selectedColor = UITheme.Ground2;
        cb.disabledColor = UITheme.Surface;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.12f;
        button.colors = cb;

        if (map.locked)
        {
            button.interactable = false;
            CanvasGroup dim = card.AddComponent<CanvasGroup>();
            dim.alpha = 0.55f;
            dim.interactable = false;
            dim.blocksRaycasts = true;
        }
        else
        {
            string capturedId = map.id;
            button.onClick.AddListener(() => OnTileClicked(capturedId));
        }
    }

    private void BuildTilePreview(Transform parent, MapDefinition map)
    {
        GameObject preview = new GameObject("Preview", typeof(RectTransform), typeof(Image));
        preview.transform.SetParent(parent, false);

        Image previewImg = preview.GetComponent<Image>();
        previewImg.color = map.previewTint;
        previewImg.raycastTarget = false;

        LayoutElement le = preview.AddComponent<LayoutElement>();
        le.preferredHeight = 240;

        if (map.locked)
        {
            GameObject overlay = new GameObject("LockCover", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(preview.transform, false);
            RectTransform ovRT = (RectTransform)overlay.transform;
            Stretch(ovRT);
            Image ovImg = overlay.GetComponent<Image>();
            ovImg.color = new Color(0.06f, 0.04f, 0.12f, 0.55f);
            ovImg.raycastTarget = false;

            TMP_Text lockText = CreateText(overlay.transform, "LockLabel", "COMING SOON", 14, UITheme.TextDim, letterSpacing: 8, wrap: false);
            Stretch(lockText.rectTransform);
            lockText.alignment = TextAlignmentOptions.Center;
        }
    }

    private void BuildTileInfo(Transform parent, MapDefinition map)
    {
        GameObject info = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup));
        info.transform.SetParent(parent, false);

        VerticalLayoutGroup vlg = info.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = 6;
        vlg.padding = new RectOffset(16, 16, 14, 14);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TMP_Text name = CreateText(info.transform, "Name", map.displayName, 20, UITheme.Text, style: FontStyles.Bold, letterSpacing: 3, wrap: false);
        name.alignment = TextAlignmentOptions.Left;

        Color metaColor = map.locked ? (Color)UITheme.TextDim : (Color)UITheme.Accent2;
        TMP_Text meta = CreateText(info.transform, "Meta", map.metaLine, 11, metaColor, letterSpacing: 4);
        meta.alignment = TextAlignmentOptions.Left;

        TMP_Text desc = CreateText(info.transform, "Desc", map.description, 12, UITheme.TextMuted);
        desc.alignment = TextAlignmentOptions.Left;
    }

    private void BuildTileSelectedBadge(Transform cardTransform)
    {
        GameObject badge = new GameObject("SelectedBadge", typeof(RectTransform), typeof(Image));
        badge.transform.SetParent(cardTransform, false);

        RectTransform badgeRT = (RectTransform)badge.transform;
        badgeRT.anchorMin = new Vector2(1, 1);
        badgeRT.anchorMax = new Vector2(1, 1);
        badgeRT.pivot = new Vector2(1, 1);
        badgeRT.anchoredPosition = new Vector2(-10, -10);
        badgeRT.sizeDelta = new Vector2(96, 22);

        Image bg = badge.GetComponent<Image>();
        bg.color = UITheme.Accent;
        bg.raycastTarget = false;

        TMP_Text txt = CreateText(badge.transform, "BadgeText", "SELECTED", 10, UITheme.White, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;

        badge.SetActive(false);
        tileSelectedBadges.Add(badge);
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
        rowLE.preferredHeight = 64;

        BuildActionButton(row.transform, "BACK", ghost: true, onClick: OnBackClicked);
        BuildActionButton(row.transform, "CONFIRM & CONTINUE", ghost: false, onClick: OnConfirmClicked);
    }

    private void BuildActionButton(Transform parent, string label, bool ghost, Action onClick)
    {
        GameObject btn = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);

        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredWidth = ghost ? 160 : 300;
        le.preferredHeight = 56;

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
            vlg.padding = new RectOffset(20, 20, 12, 12);

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

            CreateText(inner.transform, "Label", label, 15, UITheme.TextMuted, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
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
            vlg.padding = new RectOffset(20, 20, 12, 12);

            button.targetGraphic = bg;
            ColorBlock cb = button.colors;
            cb.normalColor = UITheme.Accent;
            cb.highlightedColor = UITheme.AccentHi;
            cb.pressedColor = UITheme.Accent;
            cb.selectedColor = UITheme.AccentHi;
            cb.disabledColor = UITheme.Accent;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.12f;
            button.colors = cb;

            CreateText(btn.transform, "Label", label, 15, UITheme.White, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
        }
    }

    // ---------- Interactions ----------

    private void OnTileClicked(string mapId)
    {
        highlightedMapId = mapId;
        UpdateTileVisuals();
    }

    private void UpdateTileVisuals()
    {
        for (int i = 0; i < maps.Count; i++)
        {
            bool isSelected = maps[i].id == highlightedMapId;
            if (i < tileBorders.Count)
            {
                tileBorders[i].color = isSelected ? (Color)UITheme.Accent : (Color)UITheme.StrokeHi;
            }
            if (i < tileSelectedBadges.Count)
            {
                tileSelectedBadges[i].SetActive(isSelected);
            }
        }
    }

    private void OnBackClicked()
    {
        Hide();
        // Prefer routing back to the Host/Join sub-choice — that's where the user came from
        // when they picked HOST. Fall back to Mode Select if the sub-choice isn't in the scene.
        if (hostJoinChoiceUI != null)
        {
            hostJoinChoiceUI.Show();
        }
        else if (modeSelectUI != null)
        {
            modeSelectUI.Show();
        }
    }

    private async void OnConfirmClicked()
    {
        if (string.IsNullOrEmpty(highlightedMapId))
        {
            Debug.LogWarning("[MapSelectUI] Confirm clicked with no map selected — ignoring.");
            return;
        }

        SelectedMapId = highlightedMapId;
        Debug.Log($"[MapSelectUI] Map confirmed: {SelectedMapId} — starting host.");
        Hide();

        Confirmed?.Invoke(SelectedMapId);

        // Host directly. LobbyRoomUI observes OnServerStarted and takes over the screen.
        // Fallback to the legacy NetworkDebugUI panel only if NetworkBootstrap is missing.
        if (NetworkBootstrap.Instance != null)
        {
            string code = await NetworkBootstrap.Instance.StartHostAsync(maxConnections: 3);
            if (code == null)
            {
                Debug.LogError("[MapSelectUI] StartHostAsync returned null — host did not start. Returning to Map Select.");
                Show();
            }
            return;
        }

        Debug.LogWarning("[MapSelectUI] NetworkBootstrap.Instance is null — using legacy NetworkDebugUI as fallback.");
        if (networkDebugUI != null) networkDebugUI.enabled = true;
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
