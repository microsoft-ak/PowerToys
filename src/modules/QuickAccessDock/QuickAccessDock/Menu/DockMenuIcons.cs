// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace QuickAccessDock;

internal enum DockMenuIcon
{
    Add,
    AutoHide,
    AutoHideOff,
    Reset,
    Settings,
    Open,
    Edit,
    Rename,
    MoveLeft,
    MoveRight,
    Remove,
    Quit,
}

/// <summary>
/// Renders context-menu icons using the standard Windows system icon font — "Segoe
/// Fluent Icons" on Windows 11, falling back to "Segoe MDL2 Assets" on Windows 10.
/// Both expose the same well-known glyphs Windows itself uses in its menus and shell,
/// so the dock's menu matches the rest of the OS.
/// </summary>
internal static class DockMenuIcons
{
    private const int IconSize = 16;

    private static readonly string IconFont = ResolveIconFont();

    public static Image Create(DockMenuIcon icon)
    {
        var bitmap = new Bitmap(IconSize, IconSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Grayscale anti-aliasing (not ClearType) so glyph edges stay clean over the
        // transparent bitmap without colored sub-pixel fringing.
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var color = SystemInformation.HighContrast
            ? SystemColors.MenuText
            : icon == DockMenuIcon.Remove
                ? (DockPalette.IsLight ? Color.FromArgb(196, 43, 28) : Color.FromArgb(255, 153, 143))
                : DockPalette.TextPrimary;

        using var font = new Font(IconFont, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        graphics.DrawString(GlyphFor(icon), font, brush, new RectangleF(0, 0, IconSize, IconSize), format);
        return bitmap;
    }

    // Standard Segoe Fluent Icons / Segoe MDL2 Assets code points.
    private static string GlyphFor(DockMenuIcon icon) => icon switch
    {
        DockMenuIcon.Add => "",         // Add
        DockMenuIcon.AutoHide => "",    // Unpin — enabling auto-hide unpins the dock
        DockMenuIcon.AutoHideOff => "", // Pin — disabling auto-hide keeps it pinned
        DockMenuIcon.Reset => "",       // Refresh
        DockMenuIcon.Settings => "",    // Settings
        DockMenuIcon.Open => "",        // OpenFile
        DockMenuIcon.Edit => "",        // Edit
        DockMenuIcon.Rename => "",      // Rename
        DockMenuIcon.MoveLeft => "",    // ChevronLeft
        DockMenuIcon.MoveRight => "",   // ChevronRight
        DockMenuIcon.Remove => "",      // Delete
        DockMenuIcon.Quit => "",        // PowerButton
        _ => "",
    };

    private static string ResolveIconFont()
    {
        try
        {
            using var installed = new InstalledFontCollection();
            if (installed.Families.Any(family => string.Equals(family.Name, "Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase)))
            {
                return "Segoe Fluent Icons";
            }
        }
        catch
        {
            // Fall through to the Windows 10 icon font.
        }

        return "Segoe MDL2 Assets";
    }
}

