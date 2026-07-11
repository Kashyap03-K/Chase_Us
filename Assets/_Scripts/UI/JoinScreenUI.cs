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
        // Container that holds the visible field.
        GameObject wrap = new GameObject("InputWrap", typeof(RectTransform), typeof(Image));
        wrap.transform.SetParent(parent, false);
        LayoutElement wrapLE = wrap.AddComponent<LayoutElement>();
        wrapLE.preferredHeight = 100;

        Image border = wrap.GetComponent<Image>();
        border.color = UITheme.StrokeHi;
        border.raycastTarget = true;

        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(wrap.transform, false);
        RectTransform iRT = (RectTransform)inner.transform;
        iRT.anchorMin = Vector2.zero;
        iRT.anchorMax = Vector2.one;
        iRT.offsetMin = new Vector2(2, 2);
        iRT.offsetMax = new Vector2(-2, -2);
        Image innerImg = inner.GetComponent<Image>();
        innerImg.color = UITheme.SurfaceHi;
        innerImg.raycastTarget = true;

        // Text component that TMP_InputField renders characters into.
        GameObject textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inner.transform, false);
        RectTransform taRT = (RectTransform)textArea.transform;
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(20, 8);
        taRT.offsetMax = new Vector2(-20, -8);

        TMP_Text visible = CreateText(textArea.transform, "Text", "", 54, UITheme.Accent2, style: FontStyles.Bold, letterSpacing: 18, wrap: false);
        RectTransform visibleRT = visible.rectTransform;
        visibleRT.anchorMin = Vector2.zero;
        visibleRT.anchorMax = Vector2.one;
        visibleRT.offsetMin = Vector2.zero;
        visibleRT.offsetMax = Vector2.zero;
        visible.alignment = TextAlignmentOptions.Center;

        // Placeholder shows when empty.
        TMP_Text placeholder = CreateText(textArea.transform, "Placeholder", "XXXXXX", 54, UITheme.TextDim, style: FontStyles.Bold, letterSpacing: 18, wrap: false);
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
        input.caretColor = UITheme.Accent2;
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

        BuildActionButton(row.transform, "BACK", ghost: true, wide: false, onClick: OnBackClicked, out _, out _);
        var joinGO = BuildActionButton(row.transform, "JOIN ROOM", ghost: false, wide: false, onClick: OnJoinClicked, out joinButton, out joinButtonLabel);
        // Slight width bump so Join Room label doesn't feel cramped.
        LayoutElement joinLE = joinGO.GetComponent<LayoutElement>();
        joinLE.preferredWidth = 240;
        joinButton.interactable = false;
    }

    private GameObject BuildActionButton(Transform parent, string label, bool ghost, bool wide, Action onClick,
                                          out Button outButton, out TMP_Text outLabel)
    {
        GameObject btn = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);
        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredWidth = wide ? 300 : 160;
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
