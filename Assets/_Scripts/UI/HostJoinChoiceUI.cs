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

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;

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
        RectTransform column = BuildColumn(canvasGO.transform);
        BuildHeader(column);
        BuildChoiceButtons(column);
        BuildBackButton(column);
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
        rt.sizeDelta = new Vector2(760, 520);
        rt.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 32;
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

        CreateText(header.transform, "Eyebrow", "PLAY ONLINE", 14, UITheme.TextMuted, letterSpacing: 12);
        CreateText(header.transform, "Title",   "HOST OR JOIN?", 60, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(header.transform, "Blurb",   "Start a new room and share the code, or drop into someone else's room with a code they've shared.", 15, UITheme.TextMuted);
    }

    private void BuildChoiceButtons(Transform parent)
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
        rowLE.preferredHeight = 180;

        BuildChoiceButton(row.transform, "HOST A ROOM", "CREATE A NEW GAME · SHARE THE CODE",   UITheme.Accent,  OnHostClicked);
        BuildChoiceButton(row.transform, "JOIN A ROOM", "ENTER A CODE · DROP INTO A GAME",       UITheme.Accent2, OnJoinClicked);
    }

    private void BuildChoiceButton(Transform parent, string label, string hint, Color32 accent, Action onClick)
    {
        GameObject card = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(parent, false);

        Image border = card.GetComponent<Image>();
        border.color = accent;
        border.raycastTarget = true;

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
        vlg.spacing = 10;
        vlg.padding = new RectOffset(24, 24, 24, 24);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        Button button = card.GetComponent<Button>();
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

        CreateText(inner.transform, "Label", label, 36, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(inner.transform, "Hint",  hint,  14, UITheme.TextMuted, letterSpacing: 4);
    }

    private void BuildBackButton(Transform parent)
    {
        GameObject btn = new GameObject("Back", typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);
        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredHeight = 44;
        le.preferredWidth = 160;

        Image bg = btn.GetComponent<Image>();
        bg.color = UITheme.StrokeHi;
        bg.raycastTarget = true;

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

        Button button = btn.GetComponent<Button>();
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
        button.onClick.AddListener(OnBackClicked);

        CreateText(inner.transform, "Label", "BACK", 13, UITheme.TextMuted, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
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
