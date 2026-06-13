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

    public static Point ClampLocation(Size size, Point desiredLocation, Rectangle workingArea)
    {
        return new Point(
            Clamp(desiredLocation.X, workingArea.Left, workingArea.Right - size.Width),
            Clamp(desiredLocation.Y, workingArea.Top, workingArea.Bottom - size.Height));
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
