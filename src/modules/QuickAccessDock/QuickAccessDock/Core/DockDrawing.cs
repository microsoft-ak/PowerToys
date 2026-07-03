// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickAccessDock;

internal static class DockDrawing
{
    /// <summary>The dock body's corner radius, shared so child controls can replicate its shape.</summary>
    public const int CornerRadius = 10;

    /// <summary>
    /// Clears <paramref name="graphics"/> to <paramref name="surface"/>, except when
    /// <paramref name="surface"/> is the acrylic glass sentinel (<see cref="DockPalette.GlassKey"/>) —
    /// then it writes true alpha-0 (fully transparent) pixels via <see cref="CompositingMode.SourceCopy"/>
    /// so the window's DWM system-backdrop acrylic shows through. The dock body and every child control
    /// share this so the frosted glass is uninterrupted, with foreground content painted opaque on top.
    /// </summary>
    public static void ClearSurface(Graphics graphics, Color surface)
    {
        if (surface.ToArgb() == DockPalette.GlassKey.ToArgb())
        {
            var previous = graphics.CompositingMode;
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = previous;
            return;
        }

        graphics.Clear(surface);
    }

    public static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>
    /// Paints a neomorphic (soft UI) rounded surface: a matte fill carrying a soft light
    /// highlight on the top-left and a soft dark shadow on the bottom-right, so the shape
    /// reads as extruded from — or, when <paramref name="inset"/> is set, pressed into —
    /// the surrounding surface. The soft shadows are simulated with a short stack of
    /// offset, fading strokes clipped to the rounded shape.
    /// </summary>
    public static void PaintNeomorphicSurface(
        Graphics graphics,
        Rectangle bounds,
        int radius,
        Color baseColor,
        Color light,
        Color dark,
        bool inset,
        float intensity = 1f)
    {
        using (var fillPath = CreateRoundedRectanglePath(bounds, radius))
        using (var fill = new SolidBrush(baseColor))
        {
            graphics.FillPath(fill, fillPath);
        }

        if (light.A == 0 && dark.A == 0)
        {
            return;
        }

        // When pressed the lighting flips: the dark edge moves to the top-left and the
        // highlight to the bottom-right, giving the concave "pushed in" look.
        var topLeft = inset ? dark : light;
        var bottomRight = inset ? light : dark;

        var previousSmoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var previousClip = graphics.Clip;
        using (var clipPath = CreateRoundedRectanglePath(bounds, radius))
        {
            graphics.SetClip(clipPath, CombineMode.Replace);

            const int layers = 4;
            for (var i = 1; i <= layers; i++)
            {
                // Earlier (tighter) layers are the most opaque, so the glow concentrates
                // near the edge and fades outward.
                var alpha = (int)Math.Round(58 * intensity * (layers - i + 1) / (double)layers);
                if (alpha <= 0)
                {
                    continue;
                }

                // A path shifted down-right shows its top-left edges inside the clip
                // (the highlight); shifted up-left shows its bottom-right edges (the shadow).
                var highlightRect = new Rectangle(bounds.X + i, bounds.Y + i, bounds.Width, bounds.Height);
                using (var highlightPath = CreateRoundedRectanglePath(highlightRect, radius))
                using (var highlightPen = new Pen(Color.FromArgb(Math.Min(255, alpha), topLeft), 1.8f))
                {
                    graphics.DrawPath(highlightPen, highlightPath);
                }

                var shadowRect = new Rectangle(bounds.X - i, bounds.Y - i, bounds.Width, bounds.Height);
                using (var shadowPath = CreateRoundedRectanglePath(shadowRect, radius))
                using (var shadowPen = new Pen(Color.FromArgb(Math.Min(255, alpha), bottomRight), 1.8f))
                {
                    graphics.DrawPath(shadowPen, shadowPath);
                }
            }
        }

        graphics.Clip = previousClip;
        previousClip.Dispose();
        graphics.SmoothingMode = previousSmoothing;
    }
}

