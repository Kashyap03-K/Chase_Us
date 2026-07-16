using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared toy-pill button builder for the pre-game menu screens (ModeSelect,
/// HostJoinChoice, etc.). Builds a candy-3D pill with drop shadow, navy stroke,
/// gradient fill, top-gloss highlight, and TMP-outlined label — all procedurally
/// from <see cref="UITheme.PillSprite"/>, no imported assets required.
///
/// Anchored via a single (anchor, size) pair so screens can expose SerializeField
/// positions on the Inspector and drag buttons around to line up with their art.
/// </summary>
public static class UIButton
{
    private const int StrokeThickness = 7;
    private const int ShadowOffsetY = -10;
    private const float ShadowAlpha = 0.45f;

    /// <summary>
    /// Image-based button — the whole button IS the sprite. Use when the button
    /// visuals live in an illustrated asset (e.g. the wooden plaques from the
    /// HostJoinChoice reference art). Adds a subtle brighten-on-hover tint via
    /// the button's ColorBlock so the click still has feedback.
    /// </summary>
    public static Button BuildImageButton(
        Transform parent, string name, Vector2 anchor, Vector2 size,
        Sprite sprite, Action onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = true;
        img.color = Color.white;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f); // subtle brighten on hover
        cb.pressedColor     = new Color(0.88f, 0.88f, 0.88f, 1f);
        cb.selectedColor    = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.disabledColor    = new Color(0.6f, 0.6f, 0.6f, 1f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.12f;
        btn.colors = cb;
        btn.onClick.AddListener(() =>
        {
            // G3 audio — UI click. Null-conditional so a scene without an
            // AudioManager (e.g. an isolated UI test scene) never throws.
            AudioManager am = AudioManager.Instance;
            if (am != null) am.PlaySfx(am.uiClick);
            onClick();
        });
        return btn;
    }

    public static Button BuildCandyPill(
        Transform parent, string label, Vector2 anchor, Vector2 size,
        Color32 fillColor, Color32 hoverColor, Action onClick,
        int fontSize = 44,
        string iconGlyph = "▶",
        bool showRivets = true)
    {
        // Root sits at the requested anchor; everything else is a child of the
        // root so the drop shadow, stroke, and label move together.
        GameObject root = new GameObject(label, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rootRT = (RectTransform)root.transform;
        rootRT.anchorMin = anchor;
        rootRT.anchorMax = anchor;
        rootRT.pivot = new Vector2(0.5f, 0.5f);
        rootRT.sizeDelta = size;
        rootRT.anchoredPosition = Vector2.zero;

        BuildShadow(root.transform);
        Image fill = BuildBody(root.transform, fillColor);
        BuildBottomShade(fill.transform);
        BuildGloss(fill.transform);
        BuildTopHighlight(fill.transform);
        if (showRivets) BuildRivets(fill.transform);
        string composed = string.IsNullOrEmpty(iconGlyph) ? label : $"{iconGlyph}  {label}";
        BuildLabel(fill.transform, composed, fontSize);

        Button btn = root.AddComponent<Button>();
        btn.targetGraphic = fill;
        ColorBlock cb = btn.colors;
        cb.normalColor = fillColor;
        cb.highlightedColor = hoverColor;
        cb.pressedColor = Darken(fillColor, 0.10f);
        cb.selectedColor = hoverColor;
        cb.disabledColor = UITheme.Surface;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.12f;
        btn.colors = cb;
        btn.onClick.AddListener(() =>
        {
            // G3 audio — UI click. Null-conditional so a scene without an
            // AudioManager (e.g. an isolated UI test scene) never throws.
            AudioManager am = AudioManager.Instance;
            if (am != null) am.PlaySfx(am.uiClick);
            onClick();
        });
        return btn;
    }

    private static void BuildShadow(Transform parent)
    {
        GameObject shadow = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
        shadow.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)shadow.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(0, ShadowOffsetY);
        rt.offsetMax = new Vector2(0, ShadowOffsetY);
        Image img = shadow.GetComponent<Image>();
        img.sprite = UITheme.PillSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0f, 0f, 0f, ShadowAlpha);
        img.raycastTarget = false;
    }

    private static Image BuildBody(Transform parent, Color32 fillColor)
    {
        // Outer = navy stroke; inner = fill. Both use the same procedural pill.
        GameObject stroke = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
        stroke.transform.SetParent(parent, false);
        RectTransform sRT = (RectTransform)stroke.transform;
        sRT.anchorMin = Vector2.zero;
        sRT.anchorMax = Vector2.one;
        sRT.offsetMin = Vector2.zero;
        sRT.offsetMax = Vector2.zero;
        Image sImg = stroke.GetComponent<Image>();
        sImg.sprite = UITheme.PillSprite;
        sImg.type = Image.Type.Sliced;
        sImg.color = UITheme.ButtonBlueStroke;
        sImg.raycastTarget = true;

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(stroke.transform, false);
        RectTransform fRT = (RectTransform)fill.transform;
        fRT.anchorMin = Vector2.zero;
        fRT.anchorMax = Vector2.one;
        fRT.offsetMin = new Vector2(StrokeThickness, StrokeThickness);
        fRT.offsetMax = new Vector2(-StrokeThickness, -StrokeThickness);
        Image fImg = fill.GetComponent<Image>();
        fImg.sprite = UITheme.PillSprite;
        fImg.type = Image.Type.Sliced;
        fImg.color = fillColor;
        fImg.raycastTarget = false;
        return fImg;
    }

    private static void BuildBottomShade(Transform parent)
    {
        // Dark half-pill at the bottom of the fill — fakes a vertical gradient
        // by pulling the lower half toward navy. Sits under the gloss.
        GameObject shade = new GameObject("BottomShade", typeof(RectTransform), typeof(Image));
        shade.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)shade.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0.55f);
        rt.offsetMin = new Vector2(4, 4);
        rt.offsetMax = new Vector2(-4, 0);
        Image img = shade.GetComponent<Image>();
        img.sprite = UITheme.PillSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0f, 0.08f, 0.2f, 0.28f);
        img.raycastTarget = false;
    }

    private static void BuildGloss(Transform parent)
    {
        // Broad soft shine covering the top ~45% of the fill.
        GameObject gloss = new GameObject("Gloss", typeof(RectTransform), typeof(Image));
        gloss.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)gloss.transform;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(8, 0);
        rt.offsetMax = new Vector2(-8, -6);
        Image img = gloss.GetComponent<Image>();
        img.sprite = UITheme.PillSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 1f, 1f, 0.30f);
        img.raycastTarget = false;
    }

    private static void BuildTopHighlight(Transform parent)
    {
        // Concentrated bright rim right at the top edge — the "wet plastic" sheen.
        // Narrow and bright so it reads as a specular highlight, not haze.
        GameObject hi = new GameObject("TopHighlight", typeof(RectTransform), typeof(Image));
        hi.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)hi.transform;
        rt.anchorMin = new Vector2(0f, 0.78f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(14, 0);
        rt.offsetMax = new Vector2(-14, -6);
        Image img = hi.GetComponent<Image>();
        img.sprite = UITheme.PillSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 1f, 1f, 0.55f);
        img.raycastTarget = false;
    }

    private static void BuildRivets(Transform parent)
    {
        // Small dark "iron studs" tucked in at the left and right ends of the fill,
        // vertically centered. Cheap way to sell the "wooden plaque with metal
        // bands" vibe without an illustrated asset.
        AddRivet(parent, new Vector2(0.02f, 0.5f));
        AddRivet(parent, new Vector2(0.98f, 0.5f));
    }

    private static void AddRivet(Transform parent, Vector2 anchor)
    {
        GameObject dot = new GameObject("Rivet", typeof(RectTransform), typeof(Image));
        dot.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)dot.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(16, 16);
        rt.anchoredPosition = Vector2.zero;
        Image img = dot.GetComponent<Image>();
        img.sprite = UITheme.PillSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.11f, 0.18f, 0.32f, 1f); // deep navy stud
        img.raycastTarget = false;
    }

    private static void BuildLabel(Transform parent, string label, int fontSize)
    {
        GameObject labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(parent, false);
        TextMeshProUGUI t = labelGO.AddComponent<TextMeshProUGUI>();
        t.text = label;
        t.fontSize = fontSize;
        t.color = UITheme.White;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.characterSpacing = 4;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.outlineColor = UITheme.ButtonBlueStroke;
        t.outlineWidth = 0.30f;
        t.faceColor = UITheme.White;
        // Slight upward nudge so the label optically centers within the pill —
        // otherwise the drop shadow makes it look bottom-heavy.
        RectTransform rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(0, 6);
        rt.offsetMax = new Vector2(0, -2);
    }

    private static Color32 Darken(Color32 c, float amount)
    {
        return new Color32(
            (byte)Mathf.Clamp(c.r * (1f - amount), 0f, 255f),
            (byte)Mathf.Clamp(c.g * (1f - amount), 0f, 255f),
            (byte)Mathf.Clamp(c.b * (1f - amount), 0f, 255f),
            c.a);
    }
}
