// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuickAccessDock;

internal sealed partial class DockForm : Form
{
    private void MarkInteraction()
    {
        lastInteractionUtc = DateTime.UtcNow;
    }

    private void OnAutoHidePoll()
    {
        if (modalOpen || draggingWindow || (ContextMenuStrip?.Visible ?? false))
        {
            MarkInteraction();
            if (isHidden)
            {
                Reveal();
            }

            return;
        }

        if (ActiveZoneContains(Cursor.Position))
        {
            MarkInteraction();
            if (isHidden)
            {
                Reveal();
            }

            return;
        }

        var snapped = snapEdge != DockSnap.NoEdge;
        if (settings.AutoHide && snapped && !isHidden &&
            (DateTime.UtcNow - lastInteractionUtc).TotalMilliseconds >= Math.Max(200, settings.AutoHideDelayMs))
        {
            HideToEdge();
        }
    }

    private bool ActiveZoneContains(Point point)
    {
        if (isHidden)
        {
            return HiddenRevealZone().Contains(point);
        }

        var zone = shownBounds;
        zone.Inflate(2, 2);
        return zone.Contains(point);
    }

    private Rectangle HiddenRevealZone()
    {
        var area = Screen.FromRectangle(shownBounds).WorkingArea;
        var hot = RevealStripPx + 4;

        return snapEdge switch
        {
            DockSnap.LeftEdge => new Rectangle(area.Left, shownBounds.Top, hot, shownBounds.Height),
            DockSnap.RightEdge => new Rectangle(area.Right - hot, shownBounds.Top, hot, shownBounds.Height),
            DockSnap.TopEdge => new Rectangle(shownBounds.Left, area.Top, shownBounds.Width, hot),
            DockSnap.BottomEdge => new Rectangle(shownBounds.Left, area.Bottom - hot, shownBounds.Width, hot),
            _ => Rectangle.Empty,
        };
    }

    private Point HiddenLocation()
    {
        return snapEdge switch
        {
            DockSnap.LeftEdge => new Point(shownBounds.Left - (Width - RevealStripPx), shownBounds.Top),
            DockSnap.RightEdge => new Point(shownBounds.Left + (Width - RevealStripPx), shownBounds.Top),
            DockSnap.TopEdge => new Point(shownBounds.Left, shownBounds.Top - (Height - RevealStripPx)),
            DockSnap.BottomEdge => new Point(shownBounds.Left, shownBounds.Top + (Height - RevealStripPx)),
            _ => shownBounds.Location,
        };
    }

    private void HideToEdge()
    {
        if (snapEdge == DockSnap.NoEdge)
        {
            return;
        }

        isHidden = true;
        Invalidate();
        StartSlide(HiddenLocation());

        // The slide moves the window's bounds past its own monitor's working area (into
        // the taskbar's band, or into a neighboring monitor placed edge-to-edge); clip the
        // region immediately to the final hidden geometry so the oversized bounds never
        // paint anything outside the reveal notch, even on the first frame.
        UpdateWindowRegion();
    }

    private void Reveal()
    {
        isHidden = false;
        Invalidate();
        StartSlide(shownBounds.Location);
        UpdateWindowRegion();
        MarkInteraction();
    }

    private void StartSlide(Point target)
    {
        slideTarget = target;
        if (!slideTimer.Enabled)
        {
            slideTimer.Start();
        }
    }

    private void OnSlideTick()
    {
        var current = Location;
        var dx = slideTarget.X - current.X;
        var dy = slideTarget.Y - current.Y;

        if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2)
        {
            Location = slideTarget;
            slideTimer.Stop();
            UpdateWindowRegion();
            return;
        }

        Location = new Point(current.X + Step(dx), current.Y + Step(dy));
        UpdateWindowRegion();
    }

    private static int Step(int delta)
    {
        var step = (int)(delta * 0.30);
        if (step == 0 && delta != 0)
        {
            step = Math.Sign(delta);
        }

        return step;
    }
}

