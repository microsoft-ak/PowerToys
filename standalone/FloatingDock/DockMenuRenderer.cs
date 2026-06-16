// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FloatingDock;

/// <summary>
/// Renders WinForms context menus to match the Windows 11 system menu: a rounded,
/// theme-aware acrylic glass surface with inset rounded item highlights. Pair with
/// <see cref="Apply"/>, which also requests the DWM rounded-corner and acrylic
/// blur-behind material (the same glassmorphic treatment used by the dock window and
/// the Add/Rename dialog).
/// </summary>
internal sealed class DockMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly DockMenuRenderer Shared = new();
    private static readonly Font MenuFont = new("Segoe UI", 9f);

    public DockMenuRenderer()
        : base(new DockMenuColorTable())
    {
        RoundedEdges = true;
    }

    /// <summary>
    /// Themes <paramref name="menu"/> and applies the DWM rounded-corner + acrylic
    /// blur-behind material each time it opens (the handle is realized on open).
    /// </summary>
    public static void Apply(ContextMenuStrip menu)
    {
        menu.Renderer = Shared;
        menu.ShowImageMargin = false;
        menu.BackColor = DockPalette.Surface;
        menu.ForeColor = DockPalette.TextPrimary;
        menu.Font = MenuFont;
        menu.Padding = new Padding(2, 4, 2, 4);

        menu.Opened += (_, _) =>
        {
            DockNativeMethods.SetImmersiveDarkMode(menu.Handle, !DockPalette.IsLight);
            DockNativeMethods.SetRoundedCorners(menu.Handle, small: true);
            DockNativeMethods.EnableAcrylic(menu.Handle, Color.FromArgb(DockPalette.IsLight ? 205 : 195, DockPalette.Surface));
        };
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        // Solid themed surface; the DWM acrylic provides the blur-behind material and
        // the DWM rounded-corner preference clips this fill to the menu's rounded shape.
        e.Graphics.Clear(DockPalette.Surface);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // The edge is defined by the DWM rounded corners and the system drop shadow, so
        // skip the classic square 1px border (it would poke out past the rounded corners).
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var brush = new SolidBrush(e.Item.Pressed ? DockPalette.TilePressed : DockPalette.TileHover);
        using var path = DockDrawing.CreateRoundedRectanglePath(rect, 5);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? DockPalette.TextPrimary : DockPalette.TextSecondary;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = e.Item.Bounds;
        var y = bounds.Top + (bounds.Height / 2);
        using var pen = new Pen(DockPalette.Separator);
        e.Graphics.DrawLine(pen, bounds.Left + 10, y, bounds.Right - 10, y);
    }
}

/// <summary>Maps the professional menu color slots onto the dock's theme palette.</summary>
internal sealed class DockMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => DockPalette.Surface;

    public override Color ImageMarginGradientBegin => DockPalette.Surface;

    public override Color ImageMarginGradientMiddle => DockPalette.Surface;

    public override Color ImageMarginGradientEnd => DockPalette.Surface;

    public override Color MenuBorder => DockPalette.Border;

    public override Color MenuItemBorder => DockPalette.TileHover;

    public override Color MenuItemSelected => DockPalette.TileHover;

    public override Color MenuItemSelectedGradientBegin => DockPalette.TileHover;

    public override Color MenuItemSelectedGradientEnd => DockPalette.TileHover;

    public override Color SeparatorDark => DockPalette.Separator;

    public override Color SeparatorLight => DockPalette.Separator;
}
