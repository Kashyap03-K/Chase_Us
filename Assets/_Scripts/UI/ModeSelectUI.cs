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
///   * Play Online → hides this screen, shows the Host/Join choice screen.
///   * Play LAN → hides this screen, shows the LAN host/discover screen (KAS-23).
/// </summary>
public class ModeSelectUI : MonoBehaviour
{
    [Header("Wiring (auto-found if left null)")]
    [SerializeField] private HostJoinChoiceUI hostJoinChoiceUI;
    [SerializeField] private MapSelectUI mapSelectUI;
    [SerializeField] private LanConnectUI lanConnectUI;
    [SerializeField] private CharacterSelectUI characterSelectUI;

    [Header("Background")]
    [SerializeField, Tooltip("Optional. If null, uses a solid Ground-color background.")]
    private Sprite backgroundSprite;
    [SerializeField, Range(0f, 1f), Tooltip("Dark overlay opacity on top of the background image.")]
    private float backgroundOverlayAlpha = 0.25f;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;

    [Header("Buttons (position over the background art)")]
    [Tooltip("Center of the Play LAN button in canvas-anchor coords (0,0 = bottom-left; 1,1 = top-right).")]
    [SerializeField] private Vector2 lanButtonAnchor = new Vector2(0.32f, 0.14f);
    [Tooltip("Center of the Play Online button.")]
    [SerializeField] private Vector2 onlineButtonAnchor = new Vector2(0.68f, 0.14f);
    [Tooltip("Width × height of each button in reference-resolution pixels.")]
    [SerializeField] private Vector2 buttonSize = new Vector2(560, 180);

    [Header("Custom button art (optional — falls back to candy pills if null)")]
    [Tooltip("Illustrated sprite for the Play LAN button. Whole sprite IS the button — labels/icons baked in.")]
    [SerializeField] private Sprite lanButtonSprite;
    [Tooltip("Illustrated sprite for the Play Online button.")]
    [SerializeField] private Sprite onlineButtonSprite;

    private CanvasGroup canvasGroup;
    private TMP_Text noticeText;
    private float noticeExpireAt;
    private const float NoticeDurationSeconds = 3f;

    private void Awake()
    {
        if (hostJoinChoiceUI == null)
        {
            hostJoinChoiceUI = FindFirstObjectByType<HostJoinChoiceUI>();
        }
        if (mapSelectUI == null)
        {
            mapSelectUI = FindFirstObjectByType<MapSelectUI>();
        }
        if (lanConnectUI == null)
        {
            lanConnectUI = FindAnyObjectByType<LanConnectUI>();
        }
        if (characterSelectUI == null)
        {
            characterSelectUI = FindFirstObjectByType<CharacterSelectUI>();
        }
    }

    private void Start()
    {
        BuildCanvas();
        Show();
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

        // Illustrated buttons if the user dropped sprites into the Inspector;
        // otherwise fall back to procedural candy pills.
        if (lanButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Play LAN", lanButtonAnchor, buttonSize, lanButtonSprite, OnLanClicked);
        else
            UIButton.BuildCandyPill (canvasGO.transform, "Play LAN", lanButtonAnchor, buttonSize, UITheme.ButtonBlue, UITheme.ButtonBlueHi, OnLanClicked);

        if (onlineButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Play Online", onlineButtonAnchor, buttonSize, onlineButtonSprite, OnOnlineClicked);
        else
            UIButton.BuildCandyPill (canvasGO.transform, "Play Online", onlineButtonAnchor, buttonSize, UITheme.ButtonBlue, UITheme.ButtonBlueHi, OnOnlineClicked);

        BuildNotice(BuildNoticeAnchor(canvasGO.transform));
    }

    private RectTransform BuildNoticeAnchor(Transform parent)
    {
        GameObject anchor = new GameObject("NoticeAnchor", typeof(RectTransform), typeof(VerticalLayoutGroup));
        anchor.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)anchor.transform;
        rt.anchorMin = new Vector2(0.5f, 0.05f);
        rt.anchorMax = new Vector2(0.5f, 0.05f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1000, 32);
        rt.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup vlg = anchor.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return rt;
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
        if (characterSelectUI != null)
        {
            Debug.Log("[ModeSelectUI] Play LAN → showing Character Select (next=LAN).");
            Hide();
            characterSelectUI.Show(CharacterSelectUI.NextScreen.LanConnect);
            return;
        }

        if (lanConnectUI != null)
        {
            Debug.LogWarning("[ModeSelectUI] Play LAN: no CharacterSelectUI found — skipping character pick and routing straight to LAN.");
            Hide();
            lanConnectUI.Show();
            return;
        }

        Debug.LogWarning("[ModeSelectUI] Play LAN clicked but no LanConnectUI found in the scene.");
        ShowNotice("LAN mode unavailable — LanConnectUI missing from scene.");
    }

    private void OnOnlineClicked()
    {
        if (ServicesBootstrap.Instance == null || !ServicesBootstrap.Instance.IsSignedIn)
        {
            Debug.LogWarning("[ModeSelectUI] Play Online clicked before ServicesBootstrap signed in — ignoring.");
            ShowNotice("Signing in… try again in a moment.");
            return;
        }

        // Preferred: character pick, then sub-choice screen.
        if (characterSelectUI != null)
        {
            Debug.Log("[ModeSelectUI] Play Online → showing Character Select (next=HostJoinChoice).");
            Hide();
            characterSelectUI.Show(CharacterSelectUI.NextScreen.HostJoinChoice);
            return;
        }

        // Fallback: skip character pick, jump straight to Host/Join choice.
        if (hostJoinChoiceUI != null)
        {
            Debug.LogWarning("[ModeSelectUI] Play Online: no CharacterSelectUI found — skipping character pick.");
            Hide();
            hostJoinChoiceUI.Show();
            return;
        }

        // Deep fallback: skip the sub-choice, route straight to Map Select (host-only path).
        if (mapSelectUI != null)
        {
            Debug.LogWarning("[ModeSelectUI] Play Online: no HostJoinChoiceUI found — skipping sub-choice, routing to Map Select (host path only).");
            Hide();
            mapSelectUI.Show();
            return;
        }

        Debug.LogError("[ModeSelectUI] Play Online: neither HostJoinChoiceUI nor MapSelectUI found in the scene — cannot route anywhere.");
    }

    private void ShowNotice(string message)
    {
        if (noticeText == null) return;
        noticeText.text = message;
        noticeText.gameObject.SetActive(true);
        noticeExpireAt = Time.unscaledTime + NoticeDurationSeconds;
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
}
