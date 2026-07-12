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
///   * Confirm → hides itself and starts hosting via NetworkBootstrap.
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

    [Header("Custom button art (optional — falls back to candy pills if null)")]
    [SerializeField] private Sprite confirmSprite;
    [SerializeField] private Sprite backSprite;

    [Header("Button sizes (used when the sprite variant is active)")]
    [SerializeField] private Vector2 confirmButtonSize = new Vector2(400, 100);
    [SerializeField] private Vector2 backButtonSize    = new Vector2(300, 100);

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

    [Header("Background")]
    [SerializeField, Tooltip("Optional. If null, uses a solid Ground-color background.")]
    private Sprite backgroundSprite;
    [SerializeField, Range(0f, 1f), Tooltip("Dark overlay opacity on top of the background image.")]
    private float backgroundOverlayAlpha = 0.25f;

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

    /// <summary>True while this screen is shown — lets OrbitCamera yield the cursor.</summary>
    public static bool IsVisible { get; private set; }

    public void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        IsVisible = true;
    }

    public void Hide()
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
            Stretch((RectTransform)overlay.transform);
            Image overlayImg = overlay.GetComponent<Image>();
            Color32 ground = UITheme.Ground;
            overlayImg.color = new Color(ground.r / 255f, ground.g / 255f, ground.b / 255f, backgroundOverlayAlpha);
            overlayImg.raycastTarget = false;
        }
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
        rowLE.preferredHeight = 110;

        if (backSprite != null)
            BuildActionImageButton(row.transform, "BACK", backSprite, OnBackClicked, flexWidth: 0f, preferredWidth: (int)backButtonSize.x, preferredHeight: (int)backButtonSize.y);
        else
            BuildActionButton(row.transform, "BACK", ghost: true, onClick: OnBackClicked);

        if (confirmSprite != null)
            BuildActionImageButton(row.transform, "CONFIRM & CONTINUE", confirmSprite, OnConfirmClicked, flexWidth: 0f, preferredWidth: (int)confirmButtonSize.x, preferredHeight: (int)confirmButtonSize.y);
        else
            BuildActionButton(row.transform, "CONFIRM & CONTINUE", ghost: false, onClick: OnConfirmClicked);
    }

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

    private void BuildActionButton(Transform parent, string label, bool ghost, Action onClick)
    {
        GameObject btn = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);

        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredWidth = ghost ? 180 : 320;
        le.preferredHeight = 56;

        Image strokeImg = btn.GetComponent<Image>();
        strokeImg.sprite = UITheme.PillSprite;
        strokeImg.type = Image.Type.Sliced;
        strokeImg.color = UITheme.ButtonBlueStroke;
        strokeImg.raycastTarget = true;

        Button button = btn.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());

        Color32 fillColor  = ghost ? (Color32)UITheme.Ground   : UITheme.ButtonGreen;
        Color32 hoverColor = ghost ? (Color32)UITheme.Ground2  : UITheme.ButtonGreenHi;

        const int strokeThickness = 4;
        GameObject fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(btn.transform, false);
        RectTransform fillRT = (RectTransform)fillGO.transform;
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(strokeThickness, strokeThickness);
        fillRT.offsetMax = new Vector2(-strokeThickness, -strokeThickness);
        Image fill = fillGO.GetComponent<Image>();
        fill.sprite = UITheme.PillSprite;
        fill.type = Image.Type.Sliced;
        fill.color = fillColor;
        fill.raycastTarget = false;

        button.targetGraphic = fill;
        ColorBlock cb = button.colors;
        cb.normalColor = fillColor;
        cb.highlightedColor = hoverColor;
        cb.pressedColor = fillColor;
        cb.selectedColor = hoverColor;
        cb.disabledColor = UITheme.Surface;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.12f;
        button.colors = cb;

        TMP_Text t = CreateText(fillGO.transform, "Label", label, 20, UITheme.White, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        RectTransform tRT = t.rectTransform;
        tRT.anchorMin = Vector2.zero;
        tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero;
        tRT.offsetMax = Vector2.zero;
        t.alignment = TextAlignmentOptions.Center;
        t.outlineColor = UITheme.ButtonBlueStroke;
        t.outlineWidth = 0.15f;
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
        if (NetworkBootstrap.Instance == null)
        {
            Debug.LogError("[MapSelectUI] NetworkBootstrap.Instance is null — cannot host. Returning to Map Select.");
            Show();
            return;
        }

        string code = await NetworkBootstrap.Instance.StartHostAsync(maxConnections: 3);
        if (code == null)
        {
            Debug.LogError("[MapSelectUI] StartHostAsync returned null — host did not start. Returning to Map Select.");
            Show();
        }
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
