// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System.Drawing;
using System.Windows.Forms;

namespace QuickAccessDock;

/// <summary>
/// The dock's content strip. Behaves like a normal <see cref="FlowLayoutPanel"/>, but when
/// its <see cref="Control.BackColor"/> is the acrylic glass sentinel it clears to true
/// transparent (alpha-0) pixels instead of painting an opaque background, so the window's
/// DWM system-backdrop acrylic shows through the strip and the tiles laid out on it.
/// </summary>
internal sealed class DockStripPanel : FlowLayoutPanel
{
    public DockStripPanel()
    {
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint,
            true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (BackColor.ToArgb() == DockPalette.GlassKey.ToArgb())
        {
            DockDrawing.ClearSurface(e.Graphics, DockPalette.GlassKey);
            return;
        }

        base.OnPaintBackground(e);
    }
}
