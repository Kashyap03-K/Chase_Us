using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen loading / splash screen. Renders at the highest sorting order so
/// it covers the arena scene and all other UIs from frame 1. Stays visible until
/// <see cref="ServicesBootstrap"/> reports a successful anonymous sign-in, then
/// fades out and disables itself.
///
/// Shows the Chase Us wordmark and an animated "SIGNING IN…" indicator so the
/// player has something branded to look at while UGS init + auth run (~1–2s
/// typically, more if the connection is slow).
/// </summary>
[DefaultExecutionOrder(-500)]
public class LoadingScreenUI : MonoBehaviour
{
    [Header("Background")]
    [SerializeField, Tooltip("Optional. If null, uses a solid Ground-color background.")]
    private Sprite backgroundSprite;
    [SerializeField, Range(0f, 1f), Tooltip("Dark overlay opacity on top of the background image.")]
    private float backgroundOverlayAlpha = 0.25f;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 200;
    [SerializeField, Range(0.1f, 2f)] private float fadeOutDuration = 0.6f;
    [SerializeField, Tooltip("Safety net — hide the loading screen after this many seconds even if sign-in never completes. Prevents a permanent block if UGS silently fails.")]
    private float maxWaitSeconds = 20f;

    private CanvasGroup canvasGroup;
    private GameObject canvasGO;
    private TMP_Text loadingText;
    private float dotsTimer;
    private float ageSeconds;
    private bool fading;

    private void Start()
    {
        BuildCanvas();

        if (ServicesBootstrap.Instance != null && ServicesBootstrap.Instance.IsSignedIn)
        {
            // Already signed in (e.g. domain reload with cached auth) — fade out immediately.
            StartCoroutine(FadeOutRoutine());
            return;
        }

        if (ServicesBootstrap.Instance != null)
        {
            ServicesBootstrap.Instance.SignedIn += OnSignedIn;
        }
    }

    private void OnDestroy()
    {
        if (ServicesBootstrap.Instance != null)
        {
            ServicesBootstrap.Instance.SignedIn -= OnSignedIn;
        }
    }

    private void Update()
    {
        if (fading || loadingText == null) return;

        // Age-based safety hide — logs it so we know the golden path failed.
        ageSeconds += Time.unscaledDeltaTime;
        if (ageSeconds >= maxWaitSeconds)
        {
            Debug.LogWarning($"[LoadingScreenUI] Sign-in did not complete within {maxWaitSeconds:F0}s — hiding loading screen anyway.");
            StartCoroutine(FadeOutRoutine());
            return;
        }

        // Animate the trailing dots so the screen doesn't look frozen.
        dotsTimer += Time.unscaledDeltaTime;
        int dots = ((int)(dotsTimer * 2f)) % 4;
        loadingText.text = "SIGNING IN" + new string('.', dots);
    }

    private void OnSignedIn()
    {
        if (fading) return;
        StartCoroutine(FadeOutRoutine());
    }

    private IEnumerator FadeOutRoutine()
    {
        fading = true;
        if (loadingText != null) loadingText.text = "READY";

        float t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.unscaledDeltaTime;
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / fadeOutDuration);
            yield return null;
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
        // Disable just this screen's canvas — NOT the whole GameObject. Every UI
        // script on _Bootstrap shares that GameObject, and turning it off here
        // used to disable Mode Select, Lobby, etc. along with the splash.
        if (canvasGO != null) canvasGO.SetActive(false);
    }

    // ---------- Canvas construction ----------

    private void BuildCanvas()
    {
        canvasGO = new GameObject("LoadingScreenCanvas", typeof(RectTransform));
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
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = false;   // no clickable elements, but block interaction with layers below

        BuildBackground(canvasGO.transform);
        BuildContent(canvasGO.transform);
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

    private void BuildContent(Transform parent)
    {
        GameObject column = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
        column.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)column.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1200, 500);
        rt.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup vlg = column.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 24;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        CreateText(column.transform, "Kicker",   "HIDE  ·  SEEK  ·  MVP", 18, UITheme.TextMuted, letterSpacing: 12);
        CreateText(column.transform, "Wordmark", "CHASE US",             140, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(column.transform, "Tagline",  "DUCK  ·  DODGE  ·  TAG", 18, UITheme.TextMuted, letterSpacing: 16);

        loadingText = CreateText(column.transform, "Loading", "SIGNING IN", 14, UITheme.Accent2, style: FontStyles.Bold, letterSpacing: 8, wrap: false);
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
