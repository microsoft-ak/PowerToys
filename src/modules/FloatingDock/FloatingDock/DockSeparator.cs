// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockSeparator : Control
{
    public DockSeparator()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(10, 32);
        Margin = new Padding(0, 4, 4, 4);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? DockPalette.Surface);
        var color = SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(80, 86, 96);
        using var pen = new Pen(color, 1.0f);
        e.Graphics.DrawLine(pen, Width / 2, 4, Width / 2, Height - 4);
    }
}
