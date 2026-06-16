// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;

namespace Microsoft.PowerToys.FloatingDock;

internal static class DockSnap
{
    public const string NoEdge = "None";
    public const string LeftEdge = "Left";
    public const string RightEdge = "Right";
    public const string TopEdge = "Top";
    public const string BottomEdge = "Bottom";

    public static SnapResult Snap(Rectangle bounds, Rectangle workingArea, int threshold)
    {
        threshold = Math.Max(4, threshold);
        var newLeft = bounds.Left;
        var newTop = bounds.Top;
        var edge = NoEdge;

        if (Math.Abs(bounds.Left - workingArea.Left) <= threshold)
        {
            newLeft = workingArea.Left;
            edge = LeftEdge;
        }
        else if (Math.Abs(bounds.Right - workingArea.Right) <= threshold)
        {
            newLeft = workingArea.Right - bounds.Width;
            edge = RightEdge;
        }

        if (Math.Abs(bounds.Top - workingArea.Top) <= threshold)
        {
            newTop = workingArea.Top;
            edge = TopEdge;
        }
        else if (Math.Abs(bounds.Bottom - workingArea.Bottom) <= threshold)
        {
            newTop = workingArea.Bottom - bounds.Height;
            edge = BottomEdge;
        }

        return new SnapResult(
            new Rectangle(
                Clamp(newLeft, workingArea.Left, workingArea.Right - bounds.Width),
                Clamp(newTop, workingArea.Top, workingArea.Bottom - bounds.Height),
                bounds.Width,
                bounds.Height),
            edge);
    }

    /// <summary>
    /// Picks the edge to snap to, combining window-edge proximity with the cursor's
    /// proximity to a working-area edge.
    /// </summary>
    /// <remarks>
    /// The window-edge check alone is unreliable for the leading edges (left/top):
    /// the cursor stops at the screen edge before the dock's leading edge can reach the
    /// threshold, because the drag grab point is usually well inside the dock. Falling
    /// back to the cursor position lets "drag to the edge" snap consistently on all sides.
    /// </remarks>
    public static string DetermineEdge(Rectangle bounds, Rectangle workingArea, int threshold, Point cursor)
    {
        threshold = Math.Max(4, threshold);

        // Primary signal: the working-area edge the cursor is closest to. The cursor is
        // what the user aims while dragging, so this snaps to whichever edge they pushed
        // toward — including top/bottom even when a wide dock still overlaps a side edge.
        // Distances are signed; negative means the cursor is past that edge.
        (string Edge, int Distance)[] cursorDistances =
        {
            (LeftEdge, cursor.X - workingArea.Left),
            (RightEdge, workingArea.Right - cursor.X),
            (TopEdge, cursor.Y - workingArea.Top),
            (BottomEdge, workingArea.Bottom - cursor.Y),
        };

        var nearest = cursorDistances[0];
        foreach (var candidate in cursorDistances)
        {
            if (candidate.Distance < nearest.Distance)
            {
                nearest = candidate;
            }
        }

        if (nearest.Distance <= threshold)
        {
            return nearest.Edge;
        }

        // Fallback: the dock's own edge is flush against (or past) a working-area edge,
        // e.g. when it was flung past the edge while the cursor stayed mid-screen.
        return Snap(bounds, workingArea, threshold).Edge;
    }

    public static Point ClampLocation(Size size, Point desiredLocation, Rectangle workingArea)
    {
        return new Point(
            Clamp(desiredLocation.X, workingArea.Left, workingArea.Right - size.Width),
            Clamp(desiredLocation.Y, workingArea.Top, workingArea.Bottom - size.Height));
    }

    /// <summary>
    /// Places a window of <paramref name="size"/> flush against <paramref name="edge"/>,
    /// preserving the perpendicular position from <paramref name="desiredLocation"/>.
    /// Used after an orientation change so the dock re-seats against the same edge
    /// even though its width/height changed.
    /// </summary>
    public static Rectangle PlaceAgainstEdge(Size size, Point desiredLocation, Rectangle workingArea, string edge)
    {
        var clamped = ClampLocation(size, desiredLocation, workingArea);

        return edge switch
        {
            LeftEdge => new Rectangle(workingArea.Left, clamped.Y, size.Width, size.Height),
            RightEdge => new Rectangle(workingArea.Right - size.Width, clamped.Y, size.Width, size.Height),
            TopEdge => new Rectangle(clamped.X, workingArea.Top, size.Width, size.Height),
            BottomEdge => new Rectangle(clamped.X, workingArea.Bottom - size.Height, size.Width, size.Height),
            _ => new Rectangle(clamped, size),
        };
    }

    public static Point DefaultLocation(Size size, Rectangle workingArea)
    {
        return ClampLocation(
            size,
            new Point(workingArea.Right - size.Width - 24, workingArea.Top + 96),
            workingArea);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Clamp(value, min, max);
    }
}

internal readonly record struct SnapResult(Rectangle Bounds, string Edge);
