// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.PowerToys.FloatingDock.UnitTests;

[TestClass]
public sealed class DockSnapTests
{
    [TestMethod]
    public void Snap_WhenNearRightEdge_ReturnsRightEdgeAndClampsToWorkingArea()
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var bounds = new Rectangle(1805, 100, 100, 60);

        var result = DockSnap.Snap(bounds, area, 32);

        Assert.AreEqual(DockSnap.RightEdge, result.Edge);
        Assert.AreEqual(1820, result.Bounds.Left);
        Assert.AreEqual(100, result.Bounds.Top);
    }

    [TestMethod]
    public void Snap_WhenNearBottomEdge_ReturnsBottomEdge()
    {
        var area = new Rectangle(10, 20, 500, 400);
        var bounds = new Rectangle(100, 350, 90, 60);

        var result = DockSnap.Snap(bounds, area, 16);

        Assert.AreEqual(DockSnap.BottomEdge, result.Edge);
        Assert.AreEqual(360, result.Bounds.Top);
    }

    [TestMethod]
    public void Snap_WhenOutsideWorkingArea_ClampsWithoutSnapping()
    {
        var area = new Rectangle(100, 100, 600, 400);
        var bounds = new Rectangle(20, 50, 90, 60);

        var result = DockSnap.Snap(bounds, area, 16);

        Assert.AreEqual(DockSnap.NoEdge, result.Edge);
        Assert.AreEqual(100, result.Bounds.Left);
        Assert.AreEqual(100, result.Bounds.Top);
    }

    [TestMethod]
    public void DefaultLocation_IsVisibleInsideWorkingArea()
    {
        var area = new Rectangle(0, 0, 1280, 720);
        var size = new Size(90, 52);

        var location = DockSnap.DefaultLocation(size, area);

        Assert.IsTrue(area.Contains(new Rectangle(location, size)));
        Assert.AreEqual(1166, location.X);
        Assert.AreEqual(96, location.Y);
    }

    [TestMethod]
    public void PlaceAgainstEdge_RightEdge_SeatsFlushKeepingVerticalPosition()
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var size = new Size(54, 320);

        var result = DockSnap.PlaceAgainstEdge(size, new Point(900, 200), area, DockSnap.RightEdge);

        Assert.AreEqual(1920 - 54, result.Left);
        Assert.AreEqual(200, result.Top);
        Assert.AreEqual(size.Width, result.Width);
        Assert.AreEqual(size.Height, result.Height);
    }

    [TestMethod]
    public void PlaceAgainstEdge_LeftEdge_SeatsFlushAtWorkingAreaLeft()
    {
        var area = new Rectangle(100, 50, 1000, 800);
        var size = new Size(54, 300);

        var result = DockSnap.PlaceAgainstEdge(size, new Point(500, 700), area, DockSnap.LeftEdge);

        Assert.AreEqual(100, result.Left);

        // The perpendicular position is clamped so the dock stays fully on-screen.
        Assert.AreEqual(550, result.Top);
    }

    [TestMethod]
    public void DetermineEdge_WhenCursorPinnedToLeft_SnapsLeftEvenIfDockEdgeFarFromScreen()
    {
        // The dock's left edge is 80px from the screen edge (outside the 32px threshold),
        // because the grab point is well inside the dock. The cursor is pinned to the
        // screen's left edge, so the snap must still resolve to the left edge.
        var area = new Rectangle(0, 0, 1920, 1080);
        var bounds = new Rectangle(80, 400, 160, 54);
        var cursor = new Point(1, 430);

        var edge = DockSnap.DetermineEdge(bounds, area, 32, cursor);

        Assert.AreEqual(DockSnap.LeftEdge, edge);
    }

    [TestMethod]
    public void DetermineEdge_WhenCursorAndDockAwayFromEdges_ReturnsNoEdge()
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var bounds = new Rectangle(800, 400, 160, 54);
        var cursor = new Point(880, 430);

        var edge = DockSnap.DetermineEdge(bounds, area, 32, cursor);

        Assert.AreEqual(DockSnap.NoEdge, edge);
    }

    [TestMethod]
    public void DetermineEdge_WhenCursorMidScreen_FallsBackToWindowEdge()
    {
        // Cursor is mid-screen, so the dock's own edge proximity is the fallback signal:
        // right edge is within the threshold (1790 + 160 = 1950, 30px from 1920) -> Right.
        var area = new Rectangle(0, 0, 1920, 1080);
        var bounds = new Rectangle(1790, 400, 160, 54);
        var cursor = new Point(960, 430);

        var edge = DockSnap.DetermineEdge(bounds, area, 32, cursor);

        Assert.AreEqual(DockSnap.RightEdge, edge);
    }

    [TestMethod]
    public void DetermineEdge_WhenCursorNearTop_SnapsTopEvenIfDockOverlapsRightEdge()
    {
        // The wide dock still overlaps the right edge (1700 + 200 = 1900, 20px from 1920),
        // but the cursor is pushed to the top, so the snap must resolve to the top edge.
        var area = new Rectangle(0, 0, 1920, 1080);
        var bounds = new Rectangle(1700, 45, 200, 54);
        var cursor = new Point(1750, 10);

        var edge = DockSnap.DetermineEdge(bounds, area, 32, cursor);

        Assert.AreEqual(DockSnap.TopEdge, edge);
    }

    [TestMethod]
    public void DetermineEdge_WhenCursorNearBottom_SnapsBottom()
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var bounds = new Rectangle(800, 980, 200, 54);
        var cursor = new Point(900, 1075);

        var edge = DockSnap.DetermineEdge(bounds, area, 32, cursor);

        Assert.AreEqual(DockSnap.BottomEdge, edge);
    }

    [TestMethod]
    public void PlaceAgainstEdge_NoEdge_ClampsWithoutSeating()
    {
        var area = new Rectangle(0, 0, 800, 600);
        var size = new Size(160, 54);

        var result = DockSnap.PlaceAgainstEdge(size, new Point(900, 300), area, DockSnap.NoEdge);

        Assert.AreEqual(640, result.Left);
        Assert.AreEqual(300, result.Top);
    }
}
