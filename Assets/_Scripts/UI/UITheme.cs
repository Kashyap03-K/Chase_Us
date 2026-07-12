using UnityEngine;

/// <summary>
/// Palette + type constants for the Chase Us UI direction.
/// Originally locked 2026-07-08 during KAS-19 as a midnight-indigo dark theme.
/// Revised 2026-07-12 after final character/background art landed — warm sunset
/// plum ground + toy-gold CTA to match the CHASE US wordmark and the Pixar-style
/// forest-at-dusk backgrounds. Mirror any changes here in the mockups.
/// </summary>
public static class UITheme
{
    // Backgrounds / panels — warm plum tuned to the sunset sky's dark end.
    public static readonly Color32 Ground    = new Color32(0x2A, 0x1B, 0x3D, 0xFF);
    public static readonly Color32 Ground2   = new Color32(0x34, 0x22, 0x4A, 0xFF);
    public static readonly Color32 Surface   = new Color32(0x3D, 0x2A, 0x52, 0xFF);
    public static readonly Color32 SurfaceHi = new Color32(0x4E, 0x38, 0x68, 0xFF);
    public static readonly Color32 Stroke    = new Color32(0x5A, 0x42, 0x78, 0xFF);
    public static readonly Color32 StrokeHi  = new Color32(0x74, 0x58, 0x96, 0xFF);

    // Text — warm off-white reads well over both the plum panels and the sunset art.
    public static readonly Color32 Text      = new Color32(0xF5, 0xF1, 0xE8, 0xFF);
    public static readonly Color32 TextMuted = new Color32(0xC7, 0xB8, 0xDC, 0xFF);
    public static readonly Color32 TextDim   = new Color32(0x8E, 0x7C, 0xA5, 0xFF);

    // Primary CTA — toy gold pulled from the CHASE US wordmark. This is the biggest
    // shift from the original hot-pink direction: the wordmark is the game's visual
    // anchor and CTAs should echo it.
    public static readonly Color32 Accent    = new Color32(0xF5, 0xB9, 0x42, 0xFF);
    public static readonly Color32 AccentHi  = new Color32(0xFF, 0xCF, 0x6E, 0xFF);
    public static readonly Color32 AccentDk  = new Color32(0xB8, 0x7D, 0x1A, 0xFF); // wordmark-style darker outline

    // Secondary accent — softer teal from Mira's sweater. Ready/go states, "you" chips.
    public static readonly Color32 Accent2   = new Color32(0x5F, 0xD4, 0xB8, 0xFF);
    public static readonly Color32 Accent2Hi = new Color32(0x86, 0xE3, 0xCC, 0xFF);

    // Role colors — kept from the original palette. Hunter reuses the wordmark gold's
    // opposite to stay distinct; In-Chain purple already lives in Nova's outfit.
    public static readonly Color32 Hunter    = new Color32(0xFF, 0x3D, 0x7A, 0xFF); // hot pink — Hunter role only
    public static readonly Color32 Chain     = new Color32(0xB4, 0x7C, 0xFF, 0xFF); // chain purple — In-Chain role (KAS-20)

    public static readonly Color32 Warn      = new Color32(0xE8, 0x8A, 0x3D, 0xFF); // deeper amber — connecting / caution (keeps distance from CTA gold)
    public static readonly Color32 Danger    = new Color32(0xFF, 0x6B, 0x6B, 0xFF); // soft red — error / disconnect
    public static readonly Color32 White     = new Color32(0xFF, 0xFF, 0xFF, 0xFF);

    // Toy-pill button palette — matches the mockup reference: bright fills, navy
    // stroke, cartoon rounded corners. Used by the pre-game screens that render
    // on top of the sunset background art. Stroke is shared across variants so
    // buttons on the same screen read as siblings.
    public static readonly Color32 ButtonBlue        = new Color32(0x6B, 0xA5, 0xE8, 0xFF);
    public static readonly Color32 ButtonBlueHi      = new Color32(0x8C, 0xBC, 0xF0, 0xFF);
    public static readonly Color32 ButtonGreen       = new Color32(0x5E, 0xBC, 0x7A, 0xFF);
    public static readonly Color32 ButtonGreenHi     = new Color32(0x82, 0xCF, 0x98, 0xFF);
    public static readonly Color32 ButtonBlueStroke  = new Color32(0x1E, 0x3A, 0x5F, 0xFF);

    // ---------- Procedural pill sprite ----------
    // Runtime-generated rounded-rect sprite. Avoids importing a 9-slice asset —
    // the texture is 64×64 with a 30-px corner radius, sliced so any target size
    // renders as a proper pill. Cached; generated on first access.
    private static Sprite _pillSprite;
    public static Sprite PillSprite
    {
        get
        {
            if (_pillSprite == null) _pillSprite = GeneratePillSprite();
            return _pillSprite;
        }
    }

    private static Sprite GeneratePillSprite()
    {
        const int size = 64;
        const float radius = 30f;
        const float half = size / 2f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius - d + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        Vector4 border = new Vector4(radius, radius, radius, radius);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}
