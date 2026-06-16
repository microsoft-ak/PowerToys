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

    public ShortcutTile(ShortcutItem item, int index, bool showLabel)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        Item = item;
        Index = index;
        ShowLabel = showLabel;
        AllowDrop = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Margin = new Padding(0, 0, 6, 0);
        Padding = Padding.Empty;
        Size = showLabel ? new Size(68, 40) : new Size(38, 40);
        Image = ShortcutResolver.GetIcon(item);
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

    private bool ShowLabel { get; }

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
        var fill = highContrast ? SystemColors.ButtonFace :
            isPressed ? DockPalette.TilePressed :
            isHovering ? DockPalette.TileHover :
            DockPalette.TileFill;

        using (var brush = new SolidBrush(fill))
        using (var path = DockDrawing.CreateRoundedRectanglePath(new Rectangle(0, 4, Width - 1, Height - 8), 6))
        {
            e.Graphics.FillPath(brush, path);
        }

        if (Image is not null)
        {
            var imageSize = 20;
            var imageX = ShowLabel ? 8 : (Width - imageSize) / 2;
            var imageY = (Height - imageSize) / 2;
            e.Graphics.DrawImage(Image, new Rectangle(imageX, imageY, imageSize, imageSize));
        }

        if (ShowLabel)
        {
            using var font = new Font(Font.FontFamily, 7.5f, FontStyle.Regular);
            var textColor = highContrast ? SystemColors.ControlText : DockPalette.TextPrimary;
            TextRenderer.DrawText(
                e.Graphics,
                Item.Name,
                font,
                new Rectangle(32, 10, Width - 36, Height - 18),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
