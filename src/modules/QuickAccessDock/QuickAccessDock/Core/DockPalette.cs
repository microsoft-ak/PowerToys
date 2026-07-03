// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System.Drawing;
using System.Windows.Forms;

namespace QuickAccessDock;

/// <summary>
/// Theme-aware color palette for the dock. The look is defined by two axes: a visual
/// <see cref="DockThemeStyle"/> (Default, Neomorphism, Acrylic) and a light/dark
/// <see cref="DockTheme"/> mode. Every color is recomputed when either axis changes or
/// when the system theme changes.
/// </summary>
internal static class DockPalette
{
    static DockPalette()
    {
        Refresh();
    }

    /// <summary>Gets a value indicating whether the resolved theme is the light theme.</summary>
    public static bool IsLight { get; private set; }

    /// <summary>Gets the requested app theme. System follows the current Windows app theme.</summary>
    public static DockTheme Theme { get; private set; } = DockTheme.System;

    /// <summary>Gets the active visual style.</summary>
    public static DockThemeStyle Style { get; private set; } = DockThemeStyle.Default;

    /// <summary>Gets a value indicating whether the active style is the soft-UI neomorphism style.</summary>
    public static bool IsNeomorphism => Style == DockThemeStyle.Neomorphism;

    /// <summary>Gets a value indicating whether the active style is the blurred-glass acrylic style.</summary>
    public static bool IsAcrylic => Style == DockThemeStyle.Acrylic;

    /// <summary>
    /// A sentinel BackColor that marks "render as acrylic glass": the dock body and its
    /// child controls carry it, and their paint code (via <see cref="DockDrawing.ClearSurface"/>)
    /// then writes true alpha-0 pixels so the window's acrylic blur-behind material shows
    /// through. It is never used as a window transparency key — that would defeat the backdrop.
    /// </summary>
    public static readonly Color GlassKey = Color.FromArgb(1, 2, 3);

    /// <summary>
    /// The tint applied to the acrylic blur-behind material in the glass style. Its alpha
    /// sets how strongly the light/dark frost veils the blurred background — a low alpha
    /// keeps the dock clearly see-through rather than a flat grey/white slab. Only
    /// meaningful for the acrylic style.
    /// </summary>
    public static Color GlassTint { get; private set; }

    /// <summary>Gets the dock body / surface color.</summary>
    public static Color Surface { get; private set; }

    /// <summary>Gets the dock border color.</summary>
    public static Color Border { get; private set; }

    /// <summary>Gets the resting fill of a shortcut tile / action chip.</summary>
    public static Color TileFill { get; private set; }

    /// <summary>Gets the hovered fill of a shortcut tile / action chip.</summary>
    public static Color TileHover { get; private set; }

    /// <summary>Gets the pressed fill of a shortcut tile / action chip.</summary>
    public static Color TilePressed { get; private set; }

    /// <summary>Gets the primary (high emphasis) text/glyph color.</summary>
    public static Color TextPrimary { get; private set; }

    /// <summary>Gets the secondary (low emphasis) text color.</summary>
    public static Color TextSecondary { get; private set; }

    /// <summary>Gets the accent color used by primary buttons.</summary>
    public static Color Accent { get; private set; }

    /// <summary>Gets the brighter accent used to highlight the reveal notch when hidden.</summary>
    public static Color AccentSoft { get; private set; }

    /// <summary>
    /// Gets the user-chosen accent color override, if any, applied on top of the active
    /// style/theme's default accent. Null means "use the style's default accent".
    /// </summary>
    public static Color? AccentOverride { get; private set; }

    /// <summary>Gets the separator line color.</summary>
    public static Color Separator { get; private set; }

    /// <summary>Gets the color of the edge notch shown when the dock is snapped.</summary>
    public static Color Notch { get; private set; }

    /// <summary>Gets the Fluent context menu surface color.</summary>
    public static Color MenuSurface { get; private set; }

    /// <summary>Gets the Fluent context menu hovered item fill.</summary>
    public static Color MenuHover { get; private set; }

    /// <summary>Gets the Fluent context menu pressed item fill.</summary>
    public static Color MenuPressed { get; private set; }

    /// <summary>Gets the Fluent context menu border color.</summary>
    public static Color MenuBorder { get; private set; }

    /// <summary>Gets the Fluent context menu separator color.</summary>
    public static Color MenuSeparator { get; private set; }

    /// <summary>
    /// Gets the light (top-left) shadow used by the neomorphism style to make surfaces
    /// look extruded. Unused by the other styles.
    /// </summary>
    public static Color ShadowLight { get; private set; }

    /// <summary>
    /// Gets the dark (bottom-right) shadow used by the neomorphism style to make surfaces
    /// look extruded. Unused by the other styles.
    /// </summary>
    public static Color ShadowDark { get; private set; }

    /// <summary>
    /// Re-reads the selected theme/style and recomputes every palette color.
    /// </summary>
    public static void Refresh()
    {
        Refresh(Theme, Style, AccentOverride);
    }

    /// <summary>
    /// Applies <paramref name="theme"/> (keeping the current style) and recomputes colors.
    /// </summary>
    public static void Refresh(DockTheme theme)
    {
        Refresh(theme, Style, AccentOverride);
    }

    /// <summary>
    /// Applies <paramref name="theme"/> and <paramref name="style"/> (keeping the current
    /// accent override) and recomputes every palette color.
    /// </summary>
    public static void Refresh(DockTheme theme, DockThemeStyle style)
    {
        Refresh(theme, style, AccentOverride);
    }

    /// <summary>
    /// Applies <paramref name="theme"/>, <paramref name="style"/>, and an optional
    /// <paramref name="accentOverride"/> (null to use the style's default accent) and
    /// recomputes every palette color.
    /// </summary>
    public static void Refresh(DockTheme theme, DockThemeStyle style, Color? accentOverride)
    {
        Theme = theme;
        Style = style;
        AccentOverride = accentOverride;
        IsLight = theme switch
        {
            DockTheme.Light => true,
            DockTheme.Dark => false,
            _ => DockNativeMethods.IsLightTheme(),
        };

        // Defaults shared by every style: no neomorphic shadows, no glass tint.
        ShadowLight = Color.Transparent;
        ShadowDark = Color.Transparent;
        GlassTint = Color.Transparent;

        switch (style)
        {
            case DockThemeStyle.Neomorphism:
                ApplyNeomorphism();
                break;
            case DockThemeStyle.Acrylic:
                ApplyAcrylic();
                break;
            default:
                ApplyDefault();
                break;
        }

        if (accentOverride is Color custom)
        {
            Accent = custom;
            AccentSoft = IsLight ? ControlPaint.Dark(custom, 0.08f) : ControlPaint.Light(custom, 0.3f);
        }
        else if (DockNativeMethods.GetSystemAccentColor() is Color system)
        {
            // No user override: follow the Windows accent color (Personalization ► Colors),
            // as Fluent apps are expected to. The style's hardcoded accent above stays as the
            // fallback used when the system accent can't be read.
            ApplySystemAccent(system);
        }
    }

    // Adapts the raw Windows accent color to the active light/dark mode the way WinUI does:
    // a lighter variant on dark backgrounds and a slightly deeper one on light backgrounds,
    // so the accent keeps enough contrast against the dock surface either way.
    private static void ApplySystemAccent(Color system)
    {
        if (IsLight)
        {
            // Deepen only very light accents so they still read on the near-white surface.
            Accent = system.GetBrightness() > 0.72f ? ControlPaint.Dark(system, 0.05f) : system;
            AccentSoft = ControlPaint.Dark(Accent, 0.08f);
        }
        else
        {
            // Lift dark accents toward WinUI's "light 2" shade for contrast on the dark surface.
            Accent = system.GetBrightness() < 0.45f ? ControlPaint.Light(system, 0.3f) : system;
            AccentSoft = ControlPaint.Light(Accent, 0.3f);
        }
    }

    /// <summary>Parses a "#RRGGBB" hex string into a color, or null if it isn't valid.</summary>
    public static Color? ParseAccentColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var value = hex.TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return null;
        }

        return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    /// <summary>Formats a color as a "#RRGGBB" hex string for persistence.</summary>
    public static string FormatAccentColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void ApplyDefault()
    {
        if (IsLight)
        {
            Surface = Color.FromArgb(243, 244, 247);
            Border = Color.FromArgb(214, 218, 224);
            TileFill = Color.FromArgb(255, 255, 255);
            TileHover = Color.FromArgb(236, 239, 244);
            TilePressed = Color.FromArgb(224, 228, 235);
            TextPrimary = Color.FromArgb(28, 31, 36);
            TextSecondary = Color.FromArgb(96, 104, 116);
            Accent = Color.FromArgb(76, 99, 230);
            AccentSoft = Color.FromArgb(0, 120, 212);
            Separator = Color.FromArgb(206, 211, 219);
            Notch = Color.FromArgb(150, 158, 170);
            MenuSurface = Color.FromArgb(249, 249, 249);
            MenuHover = Color.FromArgb(240, 240, 240);
            MenuPressed = Color.FromArgb(232, 232, 232);
            MenuBorder = Color.FromArgb(225, 225, 225);
            MenuSeparator = Color.FromArgb(226, 226, 226);
        }
        else
        {
            Surface = Color.FromArgb(31, 33, 36);
            Border = Color.FromArgb(78, 82, 90);
            TileFill = Color.FromArgb(39, 43, 50);
            TileHover = Color.FromArgb(47, 52, 60);
            TilePressed = Color.FromArgb(55, 61, 70);
            TextPrimary = Color.FromArgb(236, 239, 244);
            TextSecondary = Color.FromArgb(178, 186, 196);
            Accent = Color.FromArgb(105, 118, 255);
            AccentSoft = Color.FromArgb(65, 182, 255);
            Separator = Color.FromArgb(80, 86, 96);
            Notch = Color.FromArgb(120, 128, 140);
            MenuSurface = Color.FromArgb(44, 44, 44);
            MenuHover = Color.FromArgb(59, 59, 59);
            MenuPressed = Color.FromArgb(67, 67, 67);
            MenuBorder = Color.FromArgb(80, 80, 80);
            MenuSeparator = Color.FromArgb(68, 68, 68);
        }
    }

    // Soft UI: surface and tiles share one matte color; depth comes entirely from a
    // light (top-left) highlight and a dark (bottom-right) shadow. Contrast is kept low.
    private static void ApplyNeomorphism()
    {
        if (IsLight)
        {
            Surface = Color.FromArgb(224, 229, 236);   // #E0E5EC, the classic soft-UI base
            Border = Color.FromArgb(213, 219, 228);     // barely-there edge
            TileFill = Color.FromArgb(224, 229, 236);   // tiles match the surface exactly
            TileHover = Color.FromArgb(230, 235, 242);
            TilePressed = Color.FromArgb(214, 220, 229);
            TextPrimary = Color.FromArgb(52, 64, 84);
            TextSecondary = Color.FromArgb(120, 132, 152);
            Accent = Color.FromArgb(94, 108, 217);
            AccentSoft = Color.FromArgb(94, 108, 217);
            Separator = Color.FromArgb(202, 209, 219);
            Notch = Color.FromArgb(160, 172, 190);
            MenuSurface = Color.FromArgb(224, 229, 236);
            MenuHover = Color.FromArgb(214, 220, 229);
            MenuPressed = Color.FromArgb(206, 212, 222);
            MenuBorder = Color.FromArgb(208, 214, 224);
            MenuSeparator = Color.FromArgb(205, 211, 221);
            ShadowLight = Color.FromArgb(255, 255, 255);
            ShadowDark = Color.FromArgb(163, 177, 198);  // #A3B1C6
        }
        else
        {
            Surface = Color.FromArgb(43, 47, 54);       // #2B2F36 soft charcoal
            Border = Color.FromArgb(43, 47, 54);
            TileFill = Color.FromArgb(43, 47, 54);
            TileHover = Color.FromArgb(49, 54, 62);
            TilePressed = Color.FromArgb(37, 41, 48);
            TextPrimary = Color.FromArgb(226, 231, 240);
            TextSecondary = Color.FromArgb(150, 160, 176);
            Accent = Color.FromArgb(130, 143, 255);
            AccentSoft = Color.FromArgb(130, 143, 255);
            Separator = Color.FromArgb(58, 63, 72);
            Notch = Color.FromArgb(110, 118, 132);
            MenuSurface = Color.FromArgb(43, 47, 54);
            MenuHover = Color.FromArgb(54, 60, 69);
            MenuPressed = Color.FromArgb(37, 41, 48);
            MenuBorder = Color.FromArgb(58, 63, 72);
            MenuSeparator = Color.FromArgb(54, 59, 68);
            ShadowLight = Color.FromArgb(58, 64, 74);    // raised highlight
            ShadowDark = Color.FromArgb(24, 26, 31);     // recessed shadow
        }
    }

    // Blurred glass: the dock body renders as true-transparent pixels so the acrylic
    // blur-behind material shows the wallpaper/windows behind it, frosted by GlassTint.
    // The tint alpha is deliberately low so the dock stays clearly see-through (not a flat
    // grey/white slab). Surface is only the solid frosted fallback used where acrylic is
    // unavailable. Every other color paints opaquely on top of the glass.
    private static void ApplyAcrylic()
    {
        if (IsLight)
        {
            GlassTint = Color.FromArgb(90, 250, 252, 255);
            Surface = Color.FromArgb(232, 238, 246);    // solid fallback only
            Border = Color.FromArgb(178, 188, 202);     // soft edge so the bar reads on any wallpaper
            TileFill = Surface;                         // tiles have no resting chip on glass
            TileHover = Color.FromArgb(64, 255, 255, 255);
            TilePressed = Color.FromArgb(92, 238, 244, 252);
            TextPrimary = Color.FromArgb(28, 31, 36);
            TextSecondary = Color.FromArgb(70, 78, 90);
            Accent = Color.FromArgb(76, 99, 230);
            AccentSoft = Color.FromArgb(0, 120, 212);
            Separator = Color.FromArgb(190, 198, 210);
            Notch = Color.FromArgb(138, 146, 160);
            MenuSurface = Color.FromArgb(249, 249, 249);
            MenuHover = Color.FromArgb(240, 240, 240);
            MenuPressed = Color.FromArgb(232, 232, 232);
            MenuBorder = Color.FromArgb(225, 225, 225);
            MenuSeparator = Color.FromArgb(226, 226, 226);
        }
        else
        {
            GlassTint = Color.FromArgb(110, 18, 20, 26);
            Surface = Color.FromArgb(24, 26, 30);        // solid fallback only
            Border = Color.FromArgb(78, 84, 96);         // a touch lighter than the frost, so the edge reads
            TileFill = Surface;
            TileHover = Color.FromArgb(54, 255, 255, 255);
            TilePressed = Color.FromArgb(72, 255, 255, 255);
            TextPrimary = Color.FromArgb(236, 239, 244);
            TextSecondary = Color.FromArgb(176, 184, 196);
            Accent = Color.FromArgb(105, 118, 255);
            AccentSoft = Color.FromArgb(65, 182, 255);
            Separator = Color.FromArgb(66, 72, 84);
            Notch = Color.FromArgb(120, 128, 140);
            MenuSurface = Color.FromArgb(44, 44, 44);
            MenuHover = Color.FromArgb(59, 59, 59);
            MenuPressed = Color.FromArgb(67, 67, 67);
            MenuBorder = Color.FromArgb(80, 80, 80);
            MenuSeparator = Color.FromArgb(68, 68, 68);
        }
    }

}
