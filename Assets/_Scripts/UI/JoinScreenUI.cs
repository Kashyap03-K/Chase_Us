using System;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Chase Us join code entry screen — KAS-19 v4 Screen 04.
/// Client's entry point: 6-character alphanumeric code (case-insensitive),
/// submits via <see cref="NetworkBootstrap.StartClientAsync"/>. LobbyRoomUI
/// observes the resulting client-connect event and takes over.
///
/// Simpler than the mockup for MVP: uses a single TMP_InputField with wide
/// letter spacing rather than 6 discrete tiles. Tile-based input needs a
/// hidden input + per-char rendering, which is polish for a later pass.
/// </summary>
public class JoinScreenUI : MonoBehaviour
{
    [Header("Wiring (auto-found if left null)")]
    [SerializeField] private HostJoinChoiceUI hostJoinChoiceUI;
    [SerializeField] private ModeSelectUI modeSelectUI;

    [Header("Custom button art (optional — falls back to candy pills if null)")]
    [SerializeField] private Sprite joinRoomSprite;
    [SerializeField] private Sprite backSprite;

    [Header("Button sizes (used when the sprite variant is active)")]
    [SerializeField] private Vector2 joinRoomButtonSize = new Vector2(260, 100);
    [SerializeField] private Vector2 backButtonSize     = new Vector2(220, 100);

    [Header("Background")]
    [SerializeField, Tooltip("Optional. If null, uses a solid Ground-color background.")]
    private Sprite backgroundSprite;
    [SerializeField, Range(0f, 1f), Tooltip("Dark overlay opacity on top of the background image.")]
    private float backgroundOverlayAlpha = 0.25f;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;

    private const int CodeLength = 6;

    private CanvasGroup canvasGroup;
    private TMP_InputField input;
    private TMP_Text progressText;
    private TMP_Text errorText;
    private Button joinButton;
    private TMP_Text joinButtonLabel;
    private bool joining;

    private void Awake()
    {
        if (hostJoinChoiceUI == null) hostJoinChoiceUI = FindFirstObjectByType<HostJoinChoiceUI>();
        if (modeSelectUI == null)     modeSelectUI     = FindFirstObjectByType<ModeSelectUI>();
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

        if (input != null)
        {
            input.text = "";
            input.Select();
            input.ActivateInputField();
        }
        if (errorText != null) errorText.gameObject.SetActive(false);
        UpdateProgress("");
    }

    public void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        joining = false;
        IsVisible = false;
    }

    // ---------- Canvas construction ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("JoinScreenCanvas", typeof(RectTransform));
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
        BuildInput(col);
        BuildProgressLine(col);
        BuildErrorLine(col);
        BuildActions(col);
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

    private RectTransform BuildColumn(Transform parent)
    {
        GameObject col = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
        col.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)col.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(700, 620);
        rt.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 22;
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

        CreateText(header.transform, "Eyebrow", "JOIN A ROOM", 14, UITheme.TextMuted, letterSpacing: 12);
        CreateText(header.transform, "Title",   "ENTER THE CODE", 48, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(header.transform, "Blurb",   "Six characters, letters and numbers. Not case-sensitive.", 14, UITheme.TextMuted);
    }

    private void BuildInput(Transform parent)
    {
        // Rounded rune-slot container: warm gold border (AccentDk) around a dark
        // plum interior. Text renders in bright accent gold with wide letter
        // spacing so the 6 characters read as separate tiles.
        GameObject wrap = new GameObject("InputWrap", typeof(RectTransform), typeof(Image));
        wrap.transform.SetParent(parent, false);
        LayoutElement wrapLE = wrap.AddComponent<LayoutElement>();
        wrapLE.preferredHeight = 108;

        Image border = wrap.GetComponent<Image>();
        border.sprite = UITheme.PillSprite;
        border.type = Image.Type.Sliced;
        border.color = UITheme.AccentDk;
        border.raycastTarget = true;

        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(wrap.transform, false);
        RectTransform iRT = (RectTransform)inner.transform;
        iRT.anchorMin = Vector2.zero;
        iRT.anchorMax = Vector2.one;
        iRT.offsetMin = new Vector2(4, 4);
        iRT.offsetMax = new Vector2(-4, -4);
        Image innerImg = inner.GetComponent<Image>();
        innerImg.sprite = UITheme.PillSprite;
        innerImg.type = Image.Type.Sliced;
        innerImg.color = UITheme.Surface;
        innerImg.raycastTarget = true;

        GameObject textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inner.transform, false);
        RectTransform taRT = (RectTransform)textArea.transform;
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(24, 10);
        taRT.offsetMax = new Vector2(-24, -10);

        TMP_Text visible = CreateText(textArea.transform, "Text", "", 54, UITheme.Accent, style: FontStyles.Bold, letterSpacing: 22, wrap: false);
        RectTransform visibleRT = visible.rectTransform;
        visibleRT.anchorMin = Vector2.zero;
        visibleRT.anchorMax = Vector2.one;
        visibleRT.offsetMin = Vector2.zero;
        visibleRT.offsetMax = Vector2.zero;
        visible.alignment = TextAlignmentOptions.Center;
        visible.outlineColor = UITheme.AccentDk;
        visible.outlineWidth = 0.18f;

        // Six dots as placeholder so the slots read as tiles before typing.
        TMP_Text placeholder = CreateText(textArea.transform, "Placeholder", "••••••", 54, UITheme.TextDim, style: FontStyles.Bold, letterSpacing: 22, wrap: false);
        RectTransform phRT = placeholder.rectTransform;
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero;
        phRT.offsetMax = Vector2.zero;
        placeholder.alignment = TextAlignmentOptions.Center;
        placeholder.fontStyle = FontStyles.Bold;

        input = inner.AddComponent<TMP_InputField>();
        input.textViewport = taRT;
        input.textComponent = visible;
        input.placeholder = placeholder;
        input.characterLimit = CodeLength;
        input.contentType = TMP_InputField.ContentType.Alphanumeric;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.caretWidth = 3;
        input.caretColor = UITheme.Accent;
        input.customCaretColor = true;
        input.onValueChanged.AddListener(OnValueChanged);
        input.onSubmit.AddListener(_ => OnJoinClicked());
    }

    private void BuildProgressLine(Transform parent)
    {
        GameObject wrap = new GameObject("Progress", typeof(RectTransform));
        wrap.transform.SetParent(parent, false);
        LayoutElement le = wrap.AddComponent<LayoutElement>();
        le.preferredHeight = 20;

        progressText = CreateText(wrap.transform, "Text", "0 / 6 entered", 12, UITheme.TextDim, letterSpacing: 6);
        RectTransform rt = progressText.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        progressText.alignment = TextAlignmentOptions.Center;
    }

    private void BuildErrorLine(Transform parent)
    {
        GameObject wrap = new GameObject("Error", typeof(RectTransform));
        wrap.transform.SetParent(parent, false);
        LayoutElement le = wrap.AddComponent<LayoutElement>();
        le.preferredHeight = 20;

        errorText = CreateText(wrap.transform, "Text", "", 12, UITheme.Danger, letterSpacing: 6, wrap: false);
        RectTransform rt = errorText.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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

        if (backSprite != null)
            BuildActionImageButton(row.transform, "BACK", backSprite, OnBackClicked, preferredWidth: (int)backButtonSize.x, preferredHeight: (int)backButtonSize.y);
        else
            BuildActionButton(row.transform, "BACK", ghost: true, wide: false, onClick: OnBackClicked, out _, out _);

        GameObject joinGO;
        if (joinRoomSprite != null)
        {
            joinButton = BuildActionImageButton(row.transform, "JOIN ROOM", joinRoomSprite, OnJoinClicked, preferredWidth: (int)joinRoomButtonSize.x, preferredHeight: (int)joinRoomButtonSize.y);
            joinGO = joinButton.gameObject;
            joinButtonLabel = null;
        }
        else
        {
            joinGO = BuildActionButton(row.transform, "JOIN ROOM", ghost: false, wide: false, onClick: OnJoinClicked, out joinButton, out joinButtonLabel);
        }
        // Slight width bump so Join Room label doesn't feel cramped.
        LayoutElement joinLE = joinGO.GetComponent<LayoutElement>();
        joinLE.preferredWidth = 240;
        joinButton.interactable = false;
    }

    private Button BuildActionImageButton(Transform parent, string name, Sprite sprite, Action onClick, int preferredWidth, int preferredHeight = 56)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        LayoutElement le = go.AddComponent<LayoutElement>();
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

    private GameObject BuildActionButton(Transform parent, string label, bool ghost, bool wide, Action onClick,
                                          out Button outButton, out TMP_Text outLabel)
    {
        // Toy pill matching the rest of the pre-game flow. Primary = blue fill,
        // ghost = plum fill (subordinate but same shape/stroke so it reads as sibling).
        GameObject btn = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);
        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredWidth = wide ? 300 : 180;
        le.preferredHeight = 56;

        Image strokeImg = btn.GetComponent<Image>();
        strokeImg.sprite = UITheme.PillSprite;
        strokeImg.type = Image.Type.Sliced;
        strokeImg.color = UITheme.ButtonBlueStroke;
        strokeImg.raycastTarget = true;

        Button button = btn.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());
        outButton = button;

        Color32 fillColor = ghost ? (Color32)UITheme.Ground   : UITheme.ButtonBlue;
        Color32 hoverColor = ghost ? (Color32)UITheme.Ground2 : UITheme.ButtonBlueHi;

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

        outLabel = CreateText(fillGO.transform, "Label", label, 20, UITheme.White, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        RectTransform tRT = outLabel.rectTransform;
        tRT.anchorMin = Vector2.zero;
        tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero;
        tRT.offsetMax = Vector2.zero;
        outLabel.alignment = TextAlignmentOptions.Center;
        outLabel.outlineColor = UITheme.ButtonBlueStroke;
        outLabel.outlineWidth = 0.15f;
        return btn;
    }

    // ---------- Interactions ----------

    private static readonly Regex AllowedChars = new Regex("[^A-Z0-9]", RegexOptions.Compiled);

    private void OnValueChanged(string value)
    {
        // Force uppercase + strip disallowed chars (Alphanumeric already blocks most,
        // but keep this explicit so paste of "kk-tt6c" becomes "KKTT6C" cleanly).
        string upper = value.ToUpperInvariant();
        string filtered = AllowedChars.Replace(upper, "");
        if (filtered.Length > CodeLength) filtered = filtered.Substring(0, CodeLength);

        if (filtered != value)
        {
            // Rewrite silently — set without triggering another OnValueChanged storm.
            input.SetTextWithoutNotify(filtered);
        }

        UpdateProgress(filtered);
        if (errorText != null && errorText.gameObject.activeSelf) errorText.gameObject.SetActive(false);
    }

    private void UpdateProgress(string current)
    {
        int n = current?.Length ?? 0;
        if (progressText != null) progressText.text = $"{n} / {CodeLength} entered";
        if (joinButton != null)
        {
            bool ready = n == CodeLength && !joining;
            joinButton.interactable = ready;
            if (joinButtonLabel != null)
            {
                joinButtonLabel.color = ready ? (Color)UITheme.White : (Color)UITheme.TextDim;
            }
        }
    }

    private async void OnJoinClicked()
    {
        if (joining) return;
        string code = input != null ? input.text : "";
        if (string.IsNullOrEmpty(code) || code.Length != CodeLength)
        {
            ShowError("Enter all 6 characters.");
            return;
        }

        if (NetworkBootstrap.Instance == null)
        {
            ShowError("Network not ready — try again.");
            return;
        }

        joining = true;
        UpdateProgress(code);
        if (joinButtonLabel != null) joinButtonLabel.text = "JOINING…";

        Debug.Log($"[JoinScreenUI] Joining with code {code}.");
        bool started = await NetworkBootstrap.Instance.StartClientAsync(code);

        if (!started)
        {
            joining = false;
            if (joinButtonLabel != null) joinButtonLabel.text = "JOIN ROOM";
            ShowError("Couldn't join — check the code and try again.");
            UpdateProgress(input.text);
            return;
        }

        // Success — LobbyRoomUI observes OnClientConnected and shows itself.
        Hide();
    }

    private void ShowError(string message)
    {
        if (errorText == null) return;
        errorText.text = message.ToUpperInvariant();
        errorText.gameObject.SetActive(true);
    }

    private void OnBackClicked()
    {
        Hide();
        if (hostJoinChoiceUI != null) hostJoinChoiceUI.Show();
        else if (modeSelectUI != null) modeSelectUI.Show();
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
