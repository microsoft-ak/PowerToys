// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockActionButton : Button
{
    private bool isHovering;
    private bool isPressed;

    public DockActionButton(DockActionKind kind)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Kind = kind;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Size = new Size(28, 40);
        Margin = new Padding(1, 0, 1, 0);
        UseVisualStyleBackColor = false;
        TabStop = true;
        AccessibleName = kind == DockActionKind.Add ? "Add shortcut" : "Dock menu";
        AccessibleRole = AccessibleRole.PushButton;
    }

    public DockActionKind Kind { get; }

    protected override void OnMouseEnter(EventArgs e)
    {
        isHovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        isHovering = false;
        isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        isPressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        isPressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? DockPalette.Surface);

        var highContrast = SystemInformation.HighContrast;
        var color = highContrast ? SystemColors.ControlText : Color.White;
        var hoverFill = isPressed ? Color.FromArgb(42, Color.White) : Color.FromArgb(25, Color.White);

        if (isHovering || isPressed)
        {
            using var brush = new SolidBrush(highContrast ? SystemColors.Highlight : hoverFill);
            using var path = DockDrawing.CreateRoundedRectanglePath(new Rectangle(2, 4, Width - 4, Height - 8), 6);
            e.Graphics.FillPath(brush, path);
        }

        using var pen = new Pen(color, 2.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var brushDots = new SolidBrush(color);

        if (Kind == DockActionKind.Add)
        {
            var center = new Point(Width / 2, Height / 2);
            e.Graphics.DrawLine(pen, center.X - 6, center.Y, center.X + 6, center.Y);
            e.Graphics.DrawLine(pen, center.X, center.Y - 6, center.X, center.Y + 6);
        }
        else
        {
            var centerY = Height / 2;
            for (var i = -1; i <= 1; i++)
            {
                e.Graphics.FillEllipse(brushDots, (Width / 2) + (i * 6) - 2, centerY - 2, 4, 4);
            }
        }
    }
}
