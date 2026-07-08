using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The Chase Us main-menu entry screen — two buttons: Play LAN and Play Online.
/// KAS-22 (E1). Matches the dark reference locked in KAS-19 (lobby-flow.html).
///
/// Constructs its own Canvas at runtime so no scene wiring is needed — drop this
/// component onto any GameObject in the scene and it appears on Play.
///
/// Routing:
///   * Play Online → hides this screen, re-enables the temporary NetworkDebugUI
///     (host + join-code panel). Swap-out point when Jaivik's E2/E3 (Host Lobby
///     + Join screen) land — replace SetDebugUIVisible with the new screens.
///   * Play LAN → shows a "not yet implemented" notice. NetworkBootstrap has no
///     LAN transport path yet; when it does, wire this to that flow.
/// </summary>
public class ModeSelectUI : MonoBehaviour
{
    [Header("Wiring (auto-found if left null)")]
    [SerializeField, Tooltip("Temporary. Replaced by Jaivik's E2/E3 screens.")]
    private NetworkDebugUI networkDebugUI;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;

    private CanvasGroup canvasGroup;
    private TMP_Text noticeText;
    private float noticeExpireAt;
    private const float NoticeDurationSeconds = 3f;

    private void Awake()
    {
        if (networkDebugUI == null)
        {
            networkDebugUI = FindFirstObjectByType<NetworkDebugUI>();
        }
    }

    private void Start()
    {
        BuildCanvas();
        SetDebugUIVisible(false);
    }

    private void Update()
    {
        if (noticeText != null && noticeText.gameObject.activeSelf && Time.unscaledTime >= noticeExpireAt)
        {
            noticeText.gameObject.SetActive(false);
        }
    }

    // ---------- Canvas construction ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("ModeSelectCanvas", typeof(RectTransform));
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

        EnsureEventSystem();

        BuildBackground(canvasGO.transform);
        RectTransform column = BuildColumn(canvasGO.transform);
        BuildBrand(column);
        BuildModeButtons(column);
        BuildNotice(column);
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
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

    private RectTransform BuildColumn(Transform parent)
    {
        GameObject col = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
        col.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)col.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(720, 560);
        rt.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 40;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return rt;
    }

    private void BuildBrand(Transform parent)
    {
        GameObject brand = new GameObject("Brand", typeof(RectTransform), typeof(VerticalLayoutGroup));
        brand.transform.SetParent(parent, false);

        VerticalLayoutGroup vlg = brand.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 10;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        CreateText(brand.transform, "Kicker",  "HIDE  ·  SEEK  ·  MVP", 20, UITheme.TextMuted, letterSpacing: 12);
        CreateText(brand.transform, "Logo",    "CHASE US",              128, UITheme.Text,      style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(brand.transform, "Tagline", "DUCK  ·  DODGE  ·  TAG", 20, UITheme.TextMuted, letterSpacing: 16);
    }

    private void BuildModeButtons(Transform parent)
    {
        GameObject row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 20;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 160;

        BuildModeButton(row.transform, "PLAY LAN",    "SAME WI-FI · NO INTERNET",     UITheme.Accent,  OnLanClicked);
        BuildModeButton(row.transform, "PLAY ONLINE", "ANYWHERE · VIA UNITY RELAY",   UITheme.Accent2, OnOnlineClicked);
    }

    private void BuildModeButton(Transform parent, string label, string hint, Color32 accent, Action onClick)
    {
        // Card = outer image (solid accent color, acts as the visible border) + Button component
        GameObject card = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(parent, false);

        Image border = card.GetComponent<Image>();
        border.color = accent;
        border.raycastTarget = true;

        // Inner = interior fill, inset by borderThickness on all sides — reveals the border underneath
        const int borderThickness = 2;
        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        inner.transform.SetParent(card.transform, false);
        RectTransform innerRT = (RectTransform)inner.transform;
        innerRT.anchorMin = Vector2.zero;
        innerRT.anchorMax = Vector2.one;
        innerRT.offsetMin = new Vector2(borderThickness, borderThickness);
        innerRT.offsetMax = new Vector2(-borderThickness, -borderThickness);

        Image fill = inner.GetComponent<Image>();
        fill.color = UITheme.SurfaceHi;
        fill.raycastTarget = false;

        VerticalLayoutGroup vlg = inner.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 8;
        vlg.padding = new RectOffset(24, 24, 24, 24);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        Button button = card.GetComponent<Button>();
        // Target the inner fill so hover/press only tints the interior — the accent border stays solid.
        button.targetGraphic = fill;
        ColorBlock cb = button.colors;
        cb.normalColor = UITheme.SurfaceHi;
        cb.highlightedColor = UITheme.Ground2;
        cb.pressedColor = UITheme.Surface;
        cb.selectedColor = UITheme.Ground2;
        cb.disabledColor = UITheme.Surface;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.12f;
        button.colors = cb;
        button.onClick.AddListener(() => onClick());

        CreateText(inner.transform, "Label", label, 40, UITheme.Text,      style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(inner.transform, "Hint",  hint,  16, UITheme.TextMuted, letterSpacing: 4);
    }

    private void BuildNotice(Transform parent)
    {
        GameObject wrap = new GameObject("Notice", typeof(RectTransform), typeof(LayoutElement));
        wrap.transform.SetParent(parent, false);
        LayoutElement le = wrap.GetComponent<LayoutElement>();
        le.preferredHeight = 24;

        noticeText = CreateText(wrap.transform, "NoticeText", "", 14, UITheme.TextMuted, letterSpacing: 8);
        RectTransform rt = noticeText.rectTransform;
        Stretch(rt);
        noticeText.alignment = TextAlignmentOptions.Center;
        noticeText.gameObject.SetActive(false);
    }

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
        // enableWordWrapping is marked obsolete in TMP 4.x but still functional, and works
        // uniformly across every TMP version shipped with Unity 2020+ — safer than the
        // newer textWrappingMode enum which had renames between preview builds.
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

    // ---------- Routing ----------

    private void OnLanClicked()
    {
        Debug.LogWarning("[ModeSelectUI] Play LAN clicked — LAN flow is not yet implemented in NetworkBootstrap.");
        ShowNotice("LAN mode coming soon — use Play Online for now.");
    }

    private void OnOnlineClicked()
    {
        if (ServicesBootstrap.Instance == null || !ServicesBootstrap.Instance.IsSignedIn)
        {
            Debug.LogWarning("[ModeSelectUI] Play Online clicked before ServicesBootstrap signed in — ignoring.");
            ShowNotice("Signing in… try again in a moment.");
            return;
        }

        Debug.Log("[ModeSelectUI] Play Online → routing to Host/Join panel (temporary NetworkDebugUI target).");
        Hide();
        SetDebugUIVisible(true);
    }

    private void ShowNotice(string message)
    {
        if (noticeText == null) return;
        noticeText.text = message;
        noticeText.gameObject.SetActive(true);
        noticeExpireAt = Time.unscaledTime + NoticeDurationSeconds;
    }

    private void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    private void SetDebugUIVisible(bool visible)
    {
        if (networkDebugUI != null)
        {
            networkDebugUI.enabled = visible;
        }
    }
}
