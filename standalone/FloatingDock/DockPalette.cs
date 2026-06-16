// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;

namespace FloatingDock;

/// <summary>
/// Theme-aware color palette for the dock. Colors follow the current Windows
/// app theme (light/dark) and are refreshed whenever the system theme changes.
/// </summary>
internal static class DockPalette
{
    static DockPalette()
    {
        Refresh();
    }

    /// <summary>Gets a value indicating whether the resolved theme is the light theme.</summary>
    public static bool IsLight { get; private set; }

    /// <summary>Gets the dock body / surface color. Acts as the glass tint when acrylic is enabled.</summary>
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

    /// <summary>Gets the separator line color.</summary>
    public static Color Separator { get; private set; }

    /// <summary>Gets the color of the edge notch shown when the dock is snapped.</summary>
    public static Color Notch { get; private set; }

    /// <summary>
    /// Re-reads the Windows app theme and recomputes every palette color.
    /// </summary>
    public static void Refresh()
    {
        IsLight = DockNativeMethods.IsLightTheme();

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
        }
    }
}
