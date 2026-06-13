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
}
