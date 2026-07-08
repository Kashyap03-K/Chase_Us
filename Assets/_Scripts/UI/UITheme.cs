using UnityEngine;

/// <summary>
/// Palette + type constants for the Chase Us dark UI direction.
/// Locked 2026-07-08 during KAS-19; see lobby-flow.html reference for full context.
/// Mirror any changes here in the mockup, or vice versa — the two must stay in sync.
/// </summary>
public static class UITheme
{
    public static readonly Color32 Ground    = new Color32(0x0F, 0x0B, 0x1F, 0xFF);
    public static readonly Color32 Ground2   = new Color32(0x14, 0x10, 0x28, 0xFF);
    public static readonly Color32 Surface   = new Color32(0x1A, 0x15, 0x30, 0xFF);
    public static readonly Color32 SurfaceHi = new Color32(0x24, 0x1B, 0x3D, 0xFF);
    public static readonly Color32 Stroke    = new Color32(0x2E, 0x24, 0x50, 0xFF);
    public static readonly Color32 StrokeHi  = new Color32(0x45, 0x37, 0x70, 0xFF);
    public static readonly Color32 Text      = new Color32(0xF5, 0xF1, 0xE8, 0xFF);
    public static readonly Color32 TextMuted = new Color32(0x9A, 0x8E, 0xB8, 0xFF);
    public static readonly Color32 TextDim   = new Color32(0x6A, 0x5F, 0x85, 0xFF);
    public static readonly Color32 Accent    = new Color32(0xFF, 0x3D, 0x7A, 0xFF); // hot pink — LAN + primary CTA
    public static readonly Color32 AccentHi  = new Color32(0xFF, 0x60, 0x96, 0xFF);
    public static readonly Color32 Accent2   = new Color32(0x4E, 0xEB, 0xD9, 0xFF); // electric cyan — Online + ready states
    public static readonly Color32 Accent2Hi = new Color32(0x7F, 0xF3, 0xE4, 0xFF);
    public static readonly Color32 White     = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
}
