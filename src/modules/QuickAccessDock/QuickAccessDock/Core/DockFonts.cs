// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System.Drawing;
using System.Linq;

namespace QuickAccessDock;

/// <summary>
/// Resolves the recommended Windows UI typeface. Windows 11 ships "Segoe UI Variable",
/// the current system font; Windows 10 does not, so this falls back to "Segoe UI".
/// Naming a family that isn't installed makes GDI+ substitute an unrelated default
/// (Microsoft Sans Serif) rather than Segoe UI, so every body font is created here to
/// pick the best installed family once and stay correct on both OS versions.
/// </summary>
internal static class DockFonts
{
    /// <summary>The resolved body family: Segoe UI Variable Text on Win11, else Segoe UI.</summary>
    public static readonly string BodyFamily =
        IsInstalled("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    /// <summary>Creates a body-text font in the resolved family.</summary>
    public static Font Body(float size, FontStyle style = FontStyle.Regular) => new(BodyFamily, size, style);

    private static bool IsInstalled(string family)
    {
        try
        {
            using var installed = new System.Drawing.Text.InstalledFontCollection();
            return installed.Families.Any(f => string.Equals(f.Name, family, System.StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
