// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickAccessDock;

/// <summary>The glyph a <see cref="DockActionButton"/> paints.</summary>
internal enum DockActionGlyph
{
    /// <summary>An overflow ellipsis ("...") that opens the dock menu.</summary>
    Ellipsis,

    /// <summary>A plus ("+") that adds a new shortcut.</summary>
    Add,
}

internal sealed class DockActionButton : Button
{
    private DockActionGlyph glyph = DockActionGlyph.Ellipsis;

    public DockActionButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
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

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DockActionGlyph Glyph
    {
        get => glyph;
        set
        {
            if (glyph != value)
            {
                glyph = value;
                Invalidate();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        DockDrawing.ClearSurface(e.Graphics, Parent?.BackColor ?? DockPalette.Surface);

        if (glyph == DockActionGlyph.Add)
        {
            DrawPlus(e.Graphics);
            return;
        }

        // No hover/pressed background: just the ellipsis glyph on the dock surface. The
        // dots always sit in a row (a horizontal ellipsis) in both dock orientations.
        var color = SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.TextPrimary;
        using var brushDots = new SolidBrush(color);

        const float dot = 3f;
        const float spacing = 5f;
        var centerX = Width / 2f;
        var centerY = Height / 2f;
        for (var i = -1; i <= 1; i++)
        {
            e.Graphics.FillEllipse(brushDots, centerX + (i * spacing) - (dot / 2f), centerY - (dot / 2f), dot, dot);
        }
    }

    private void DrawPlus(Graphics graphics)
    {
        // The add affordance uses the accent color so it reads as a distinct "new" action
        // alongside the neutral ellipsis. A plus looks the same in either orientation.
        var color = SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.Accent;
        using var pen = new Pen(color, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        var centerX = Width / 2f;
        var centerY = Height / 2f;
        const float arm = 5f;
        graphics.DrawLine(pen, centerX - arm, centerY, centerX + arm, centerY);
        graphics.DrawLine(pen, centerX, centerY - arm, centerX, centerY + arm);
    }
}

