// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickAccessDock;

/// <summary>
/// Renders WinForms context menus to match the Windows 11 system menu: a rounded,
/// theme-aware surface with inset rounded item highlights. Pair with
/// <see cref="Apply"/>, which also requests the DWM rounded-corner treatment and,
/// in dark mode, an acrylic blur-behind material.
/// </summary>
internal sealed class DockMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly DockMenuRenderer Shared = new();
    private static readonly Font MenuFont = DockFonts.Body(9f);
    private const int MenuWidth = 226;
    private const int MinMenuWidth = 80;
    private const int MenuPadding = 4;
    private const int ItemHeight = 34;
    private const int SeparatorHeight = 9;
    private const int IconLeft = 11;
    private const int IconSize = 16;
    private const int TextLeft = 38;

    public DockMenuRenderer()
        : base(new DockMenuColorTable())
    {
        RoundedEdges = true;
    }

    /// <summary>
    /// Themes <paramref name="menu"/> and applies the DWM rounded-corner + acrylic
    /// blur-behind material each time it opens (the handle is realized on open).
    /// </summary>
    public static void Apply(ContextMenuStrip menu) => Apply(menu, MenuWidth);

    /// <summary>
    /// Themes <paramref name="menu"/> at a specific <paramref name="width"/>, used by
    /// the Fluent dropdown so its popup is sized to the dropdown rather than the wider
    /// default dock command menu.
    /// </summary>
    public static void Apply(ContextMenuStrip menu, int width)
    {
        DockPalette.Refresh();

        var menuWidth = Math.Max(width, MinMenuWidth);
        menu.Renderer = Shared;
        menu.ShowImageMargin = true;
        menu.ImageScalingSize = new Size(IconSize, IconSize);
        menu.BackColor = DockPalette.MenuSurface;
        menu.ForeColor = DockPalette.TextPrimary;
        menu.Font = MenuFont;
        menu.Margin = Padding.Empty;
        menu.Padding = new Padding(MenuPadding, 5, MenuPadding, 5);
        menu.MinimumSize = new Size(menuWidth, 0);

        foreach (ToolStripItem item in menu.Items)
        {
            ApplyItemSizing(item, menuWidth);
        }

        menu.Opened += (_, _) =>
        {
            DockNativeMethods.SetImmersiveDarkMode(menu.Handle, !DockPalette.IsLight);
            DockNativeMethods.SetRoundedCorners(menu.Handle, small: true);
            // Acrylic only in dark mode: in light mode the DWM compositor surface bleeds
            // through the GDI-painted background, making menu content hard to read.
            if (!DockPalette.IsLight)
            {
                DockNativeMethods.EnableAcrylic(menu.Handle, Color.FromArgb(225, DockPalette.MenuSurface));
            }
        };
    }

    private static void ApplyItemSizing(ToolStripItem item, int width)
    {
        item.Margin = Padding.Empty;
        item.Padding = Padding.Empty;

        if (item is ToolStripSeparator separator)
        {
            separator.AutoSize = false;
            separator.Size = new Size(width - (MenuPadding * 2), SeparatorHeight);
            return;
        }

        item.AutoSize = false;
        item.Size = new Size(width - (MenuPadding * 2), ItemHeight);

        if (item is ToolStripMenuItem menuItem)
        {
            menuItem.ImageScaling = ToolStripItemImageScaling.None;
        }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        // Solid themed surface; the DWM acrylic provides the blur-behind material and
        // the DWM rounded-corner preference clips this fill to the menu's rounded shape.
        e.Graphics.Clear(SystemInformation.HighContrast ? SystemColors.Menu : DockPalette.MenuSurface);
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
        var rect = new Rectangle(2, 2, e.Item.Width - 4, e.Item.Height - 4);
        var fill = SystemInformation.HighContrast
            ? SystemColors.Highlight
            : e.Item.Pressed ? DockPalette.MenuPressed : DockPalette.MenuHover;
        using var brush = new SolidBrush(fill);
        using var path = DockDrawing.CreateRoundedRectanglePath(rect, 4);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is null)
        {
            return;
        }

        var y = (e.Item.Height - IconSize) / 2;
        var imageBounds = new Rectangle(IconLeft, y, IconSize, IconSize);

        if (e.Item.Enabled)
        {
            e.Graphics.DrawImage(e.Image, imageBounds);
            return;
        }

        ControlPaint.DrawImageDisabled(e.Graphics, e.Image, imageBounds.X, imageBounds.Y, DockPalette.MenuSurface);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var textColor = SystemInformation.HighContrast
            ? (e.Item.Enabled ? SystemColors.MenuText : SystemColors.GrayText)
            : e.Item.Enabled ? DockPalette.TextPrimary : DockPalette.TextSecondary;

        var textBounds = new Rectangle(TextLeft, 0, e.Item.Width - TextLeft - 10, e.Item.Height);
        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            textBounds,
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = e.Item.Bounds;
        var y = bounds.Height / 2;
        using var pen = new Pen(SystemInformation.HighContrast ? SystemColors.MenuText : DockPalette.MenuSeparator);
        e.Graphics.DrawLine(pen, 8, y, bounds.Width - 8, y);
    }
}

/// <summary>Maps the professional menu color slots onto the dock's theme palette.</summary>
internal sealed class DockMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => DockPalette.MenuSurface;

    public override Color ImageMarginGradientBegin => DockPalette.MenuSurface;

    public override Color ImageMarginGradientMiddle => DockPalette.MenuSurface;

    public override Color ImageMarginGradientEnd => DockPalette.MenuSurface;

    public override Color MenuBorder => DockPalette.MenuBorder;

    public override Color MenuItemBorder => DockPalette.MenuHover;

    public override Color MenuItemSelected => DockPalette.MenuHover;

    public override Color MenuItemSelectedGradientBegin => DockPalette.MenuHover;

    public override Color MenuItemSelectedGradientEnd => DockPalette.MenuHover;

    public override Color SeparatorDark => DockPalette.MenuSeparator;

    public override Color SeparatorLight => DockPalette.MenuSeparator;
}

