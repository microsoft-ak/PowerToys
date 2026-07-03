// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickAccessDock;

internal sealed class DockDragHandle : Control
{
    private DockOrientation orientation = DockOrientation.Horizontal;

    public DockDragHandle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(16, 40);
        Margin = Padding.Empty;
        TabStop = false;
        Cursor = Cursors.SizeAll;
        AccessibleName = "Move dock";
        AccessibleDescription = "Drag to move the dock";
        AccessibleRole = AccessibleRole.Grip;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DockOrientation DockOrientation
    {
        get => orientation;
        set
        {
            if (orientation != value)
            {
                orientation = value;
                Invalidate();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        DockDrawing.ClearSurface(e.Graphics, Parent?.BackColor ?? DockPalette.Surface);

        var color = SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(125, DockPalette.TextSecondary);
        using var brush = new SolidBrush(color);

        var columns = orientation == DockOrientation.Vertical ? 3 : 2;
        var rows = orientation == DockOrientation.Vertical ? 2 : 3;
        const int dotSize = 2;
        const int gap = 3;

        var totalWidth = (columns * dotSize) + ((columns - 1) * gap);
        var totalHeight = (rows * dotSize) + ((rows - 1) * gap);
        var startX = (Width - totalWidth) / 2;
        var startY = (Height - totalHeight) / 2;

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                e.Graphics.FillEllipse(
                    brush,
                    startX + (column * (dotSize + gap)),
                    startY + (row * (dotSize + gap)),
                    dotSize,
                    dotSize);
            }
        }
    }
}

