// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockActionButton : Button
{
    public DockActionButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Size = new Size(28, 40);
        Margin = new Padding(1, 0, 1, 0);
        UseVisualStyleBackColor = false;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleName = "Dock menu";
        AccessibleRole = AccessibleRole.PushButton;
    }

    private DockOrientation orientation = DockOrientation.Horizontal;

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
        e.Graphics.Clear(Parent?.BackColor ?? DockPalette.Surface);

        // No hover/pressed background: just the ellipsis glyph on the dock surface.
        var color = SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.TextPrimary;
        using var brushDots = new SolidBrush(color);

        if (orientation == DockOrientation.Vertical)
        {
            // Vertical (left/right-snapped) dock: horizontal ellipsis (dots in a row).
            var centerY = Height / 2;
            for (var i = -1; i <= 1; i++)
            {
                e.Graphics.FillEllipse(brushDots, (Width / 2) + (i * 6) - 2, centerY - 2, 4, 4);
            }
        }
        else
        {
            // Horizontal dock: vertical ellipsis (dots stacked along the Y axis).
            var centerX = Width / 2;
            for (var i = -1; i <= 1; i++)
            {
                e.Graphics.FillEllipse(brushDots, centerX - 2, (Height / 2) + (i * 6) - 2, 4, 4);
            }
        }
    }
}
