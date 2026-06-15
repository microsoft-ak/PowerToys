// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockSeparator : Control
{
    private DockOrientation orientation = DockOrientation.Horizontal;

    public DockSeparator()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(10, 32);
        Margin = new Padding(0, 4, 4, 4);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DockOrientation Orientation
    {
        get => orientation;
        set
        {
            if (orientation == value)
            {
                return;
            }

            orientation = value;
            Size = orientation == DockOrientation.Vertical ? new Size(36, 10) : new Size(10, 32);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? DockPalette.Surface);
        using var pen = new Pen(SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.Separator, 1.0f);

        if (orientation == DockOrientation.Vertical)
        {
            e.Graphics.DrawLine(pen, 4, Height / 2, Width - 4, Height / 2);
        }
        else
        {
            e.Graphics.DrawLine(pen, Width / 2, 4, Width / 2, Height - 4);
        }
    }
}
