// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickAccessDock;

internal sealed class ShortcutTile : Button
{
    private bool isHovering;
    private bool isPressed;

    public ShortcutTile(ShortcutItem item, int index)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);

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

        DockDrawing.ClearSurface(e.Graphics, Parent?.BackColor ?? DockPalette.Surface);

        var tileBounds = new Rectangle(0, 2, Width - 1, Height - 4);

        if (SystemInformation.HighContrast)
        {
            if (isHovering || isPressed)
            {
                using var hcBrush = new SolidBrush(SystemColors.Highlight);
                using var hcPath = DockDrawing.CreateRoundedRectanglePath(tileBounds, 6);
                e.Graphics.FillPath(hcBrush, hcPath);
            }
        }
        else if (DockPalette.IsNeomorphism)
        {
            // Soft UI: every tile is a tactile, extruded chip — raised at rest and on
            // hover (slightly brighter), pressed in (concave) while held.
            var baseColor = isHovering && !isPressed ? DockPalette.TileHover : DockPalette.TileFill;
            DockDrawing.PaintNeomorphicSurface(
                e.Graphics,
                tileBounds,
                8,
                baseColor,
                DockPalette.ShadowLight,
                DockPalette.ShadowDark,
                inset: isPressed);
        }
        else if (isHovering || isPressed)
        {
            // Default: no resting background — the icon sits on the dock surface, with
            // a rounded highlight only while hovered or pressed.
            var fill = isPressed ? DockPalette.TilePressed : DockPalette.TileHover;
            using var brush = new SolidBrush(fill);
            using var path = DockDrawing.CreateRoundedRectanglePath(tileBounds, 6);
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

