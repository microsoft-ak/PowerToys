// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockHubButton : Button
{
    private bool isHovering;

    public DockHubButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Size = new Size(42, 40);
        Margin = new Padding(0, 0, 8, 0);
        UseVisualStyleBackColor = false;
        TabStop = true;
        AccessibleName = "Expand or collapse dock";
        AccessibleRole = AccessibleRole.PushButton;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ShortcutCount { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsExpanded { get; set; }

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

        var highContrast = SystemInformation.HighContrast;
        var rect = new Rectangle(4, 2, 36, 36);
        using var outerBrush = new SolidBrush(highContrast ? SystemColors.ButtonFace : Color.FromArgb(37, 42, 57));
        using var ringPen = new Pen(highContrast ? SystemColors.Highlight : Color.FromArgb(105, 118, 255), 3.0f);
        using var softPen = new Pen(highContrast ? SystemColors.ControlText : Color.FromArgb(65, 182, 255), 1.3f);
        using var hoverBrush = new SolidBrush(Color.FromArgb(28, Color.White));

        if (isHovering)
        {
            e.Graphics.FillEllipse(hoverBrush, rect);
        }

        e.Graphics.FillEllipse(outerBrush, rect);
        e.Graphics.DrawEllipse(ringPen, rect);

        var inner = Rectangle.Inflate(rect, -7, -7);
        e.Graphics.DrawArc(softPen, inner, 220, IsExpanded ? 260 : 160);

        var text = ShortcutCount > 99 ? "99+" : ShortcutCount.ToString(CultureInfo.InvariantCulture);
        using var font = new Font(Font.FontFamily, ShortcutCount > 99 ? 7.0f : 9.0f, FontStyle.Bold);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            font,
            rect,
            highContrast ? SystemColors.ControlText : Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
