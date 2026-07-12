using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sub-choice screen shown after Mode Select's "Play Online" — asks the user
/// whether they want to host a new room or join an existing one. Routes to
/// MapSelectUI (host path) or JoinScreenUI (join path).
///
/// Not in the KAS-19 v4 mockup as a distinct screen; the design assumed the
/// disambiguation would happen implicitly. Adding it here to unblock joiners
/// without inflating Mode Select with a third button.
/// </summary>
public class HostJoinChoiceUI : MonoBehaviour
{
    [Header("Wiring (auto-found if left null)")]
    [SerializeField] private ModeSelectUI modeSelectUI;
    [SerializeField] private MapSelectUI mapSelectUI;
    [SerializeField] private JoinScreenUI joinScreenUI;

    [Header("Background")]
    [SerializeField, Tooltip("Optional. If null, uses a solid Ground-color background.")]
    private Sprite backgroundSprite;
    [SerializeField, Range(0f, 1f), Tooltip("Dark overlay opacity on top of the background image.")]
    private float backgroundOverlayAlpha = 0.25f;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;

    [Header("Buttons (position over the background art)")]
    [SerializeField] private Vector2 hostButtonAnchor = new Vector2(0.32f, 0.30f);
    [SerializeField] private Vector2 joinButtonAnchor = new Vector2(0.68f, 0.30f);
    [SerializeField] private Vector2 buttonSize = new Vector2(500, 320);
    [SerializeField] private Vector2 backButtonAnchor = new Vector2(0.5f, 0.06f);
    [SerializeField] private Vector2 backButtonSize = new Vector2(240, 72);

    [Header("Custom button art (optional — falls back to candy pills if null)")]
    [Tooltip("Illustrated plaque sprite for the Host button. Whole plaque IS the button — labels/icons baked in.")]
    [SerializeField] private Sprite hostButtonSprite;
    [Tooltip("Illustrated plaque sprite for the Join button.")]
    [SerializeField] private Sprite joinButtonSprite;
    [Tooltip("Illustrated sprite for the Back button.")]
    [SerializeField] private Sprite backButtonSprite;

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        if (modeSelectUI == null)  modeSelectUI  = FindFirstObjectByType<ModeSelectUI>();
        if (mapSelectUI == null)   mapSelectUI   = FindFirstObjectByType<MapSelectUI>();
        if (joinScreenUI == null)  joinScreenUI  = FindFirstObjectByType<JoinScreenUI>();
    }

    private void Start()
    {
        BuildCanvas();
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
        GameObject canvasGO = new GameObject("HostJoinChoiceCanvas", typeof(RectTransform));
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

        // Host + Join: use the illustrated plaque sprites if the user dropped them
        // into the Inspector; otherwise fall back to procedural candy pills.
        if (hostButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Host a Room", hostButtonAnchor, buttonSize, hostButtonSprite, OnHostClicked);
        else
            UIButton.BuildCandyPill (canvasGO.transform, "Host a Room", hostButtonAnchor, buttonSize, UITheme.ButtonGreen, UITheme.ButtonGreenHi, OnHostClicked);

        if (joinButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Join a Room", joinButtonAnchor, buttonSize, joinButtonSprite, OnJoinClicked);
        else
            UIButton.BuildCandyPill (canvasGO.transform, "Join a Room", joinButtonAnchor, buttonSize, UITheme.ButtonBlue,  UITheme.ButtonBlueHi,  OnJoinClicked);

        if (backButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Back", backButtonAnchor, backButtonSize, backButtonSprite, OnBackClicked);
        else
            UIButton.BuildCandyPill(canvasGO.transform, "Back", backButtonAnchor, backButtonSize, UITheme.Ground, UITheme.Ground2, OnBackClicked, fontSize: 24);
    }

    private void BuildBackground(Transform parent)
    {
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)bg.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            Image overlayImg = overlay.GetComponent<Image>();
            Color32 ground = UITheme.Ground;
            overlayImg.color = new Color(ground.r / 255f, ground.g / 255f, ground.b / 255f, backgroundOverlayAlpha);
            overlayImg.raycastTarget = false;
        }
    }

    // ---------- Interactions ----------

    private void OnHostClicked()
    {
        Debug.Log("[HostJoinChoiceUI] HOST → showing Map Select.");
        Hide();
        if (mapSelectUI != null) mapSelectUI.Show();
        else Debug.LogWarning("[HostJoinChoiceUI] MapSelectUI not found in scene — host flow can't proceed.");
    }

    private void OnJoinClicked()
    {
        Debug.Log("[HostJoinChoiceUI] JOIN → showing Join Screen.");
        Hide();
        if (joinScreenUI != null) joinScreenUI.Show();
        else Debug.LogWarning("[HostJoinChoiceUI] JoinScreenUI not found in scene — join flow can't proceed.");
    }

    private void OnBackClicked()
    {
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
}
