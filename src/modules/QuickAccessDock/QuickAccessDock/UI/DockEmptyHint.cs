// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickAccessDock;

internal sealed class DockEmptyHint : Control
{
    // Shown when the dock has no shortcuts. Clicking it (or dropping onto it) adds one.
    public const string HintText = "Add new";

    private static readonly Font HintFont = DockFonts.Body(9f);

    public int PreferredHorizontalWidth
    {
        get
        {
            var size = TextRenderer.MeasureText(HintText, HintFont, new Size(int.MaxValue, 0), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            return size.Width + 24;
        }
    }
    private DockOrientation orientation = DockOrientation.Horizontal;

    public DockEmptyHint()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(PreferredHorizontalWidth, 40);
        Margin = Padding.Empty;
        Text = HintText;
        AllowDrop = true;
        TabStop = false;
        Cursor = Cursors.Hand;
        AccessibleName = HintText;
        AccessibleDescription = "Click, or drop a file, app, URL, or command, to add a shortcut";
        AccessibleRole = AccessibleRole.PushButton;
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

        var color = SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.TextSecondary;
        if (orientation == DockOrientation.Vertical)
        {
            DrawVerticalText(e.Graphics, color);
            return;
        }

        if (DockPalette.IsAcrylic)
        {
            // On the transparent glass surface, render with GDI+ (which writes an alpha
            // channel) so the glyphs composite as solid text rather than showing the blur
            // through as transparent holes, the way GDI's TextRenderer would.
            DrawHorizontalText(e.Graphics, color);
            return;
        }

        TextRenderer.DrawText(
            e.Graphics,
            HintText,
            HintFont,
            ClientRectangle,
            color,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    private void DrawHorizontalText(Graphics graphics, Color color)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.DrawString(HintText, HintFont, brush, ClientRectangle, format);
    }

    private void DrawVerticalText(Graphics graphics, Color color)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        var state = graphics.Save();
        graphics.TranslateTransform(Width / 2f, Height / 2f);
        graphics.RotateTransform(90f);
        graphics.DrawString(HintText, HintFont, brush, new RectangleF(-Height / 2f, -Width / 2f, Height, Width), format);
        graphics.Restore(state);
    }
}

