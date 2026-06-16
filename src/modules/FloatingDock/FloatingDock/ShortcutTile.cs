// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class ShortcutTile : Button
{
    private bool isHovering;
    private bool isPressed;

    public ShortcutTile(ShortcutItem item, int index)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        Item = item;
        Index = index;
        AllowDrop = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Margin = new Padding(0, 0, 6, 0);
        Padding = Padding.Empty;
        Size = new Size(38, 40);

        // Shortcut tiles launch a file/folder/app/URL, so show the hand cursor.
        Cursor = Cursors.Hand;
        Image = ShortcutResolver.GetIcon(item, true);
        Tag = index;
        UseVisualStyleBackColor = false;
        AutoEllipsis = true;
        TabStop = true;
        AccessibleName = item.Name;
        AccessibleDescription = item.Target;
        AccessibleRole = AccessibleRole.PushButton;
    }

    public ShortcutItem Item { get; }

    public int Index { get; }

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

        // No resting background: the icon sits directly on the dock surface. Only draw a
        // subtle rounded highlight while hovered/pressed so clicks still read as buttons.
        if (isHovering || isPressed)
        {
            var fill = SystemInformation.HighContrast ? SystemColors.Highlight :
                isPressed ? DockPalette.TilePressed : DockPalette.TileHover;
            using var brush = new SolidBrush(fill);
            using var path = DockDrawing.CreateRoundedRectanglePath(new Rectangle(0, 2, Width - 1, Height - 4), 6);
            e.Graphics.FillPath(brush, path);
        }

        if (Image is not null)
        {
            var imageSize = 20;
            var imageX = (Width - imageSize) / 2;
            var imageY = (Height - imageSize) / 2;
            e.Graphics.DrawImage(Image, new Rectangle(imageX, imageY, imageSize, imageSize));
        }
    }
}
