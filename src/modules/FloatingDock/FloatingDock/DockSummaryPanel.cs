// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockSummaryPanel : Control
{
    private bool isHovering;

    public DockSummaryPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(86, 40);
        Margin = new Padding(0, 0, 8, 0);
        Cursor = Cursors.SizeAll;
        AllowDrop = true;
        AccessibleName = "Dock shortcut summary";
        AccessibleRole = AccessibleRole.StaticText;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PrimaryText { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SecondaryText { get; set; } = string.Empty;

    protected override void OnMouseEnter(EventArgs e)
    {
        isHovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        isHovering = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? DockPalette.Surface);

        if (isHovering && !SystemInformation.HighContrast)
        {
            using var brush = new SolidBrush(DockPalette.HoverOverlay);
            using var path = DockDrawing.CreateRoundedRectanglePath(new Rectangle(0, 4, Width - 2, Height - 8), 6);
            e.Graphics.FillPath(brush, path);
        }

        var primary = SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.TextPrimary;
        var secondary = SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.TextSecondary;
        using var primaryFont = new Font(Font.FontFamily, 8.5f, FontStyle.Bold);
        using var secondaryFont = new Font(Font.FontFamily, 8.0f, FontStyle.Regular);

        TextRenderer.DrawText(
            e.Graphics,
            PrimaryText,
            primaryFont,
            new Rectangle(0, 5, Width, 16),
            primary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(
            e.Graphics,
            SecondaryText,
            secondaryFont,
            new Rectangle(0, 22, Width, 15),
            secondary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
