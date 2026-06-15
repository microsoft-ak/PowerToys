// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.PowerToys.FloatingDock.UnitTests;

[TestClass]
public sealed class ShortcutResolverTests
{
    private string testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        testFolder = Path.Combine(Path.GetTempPath(), "PowerToysFloatingDockShortcutTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testFolder))
        {
            Directory.Delete(testFolder, recursive: true);
        }
    }

    [TestMethod]
    public void FromPath_ClassifiesFolders()
    {
        var folder = Path.Combine(testFolder, "Folder Target");
        Directory.CreateDirectory(folder);

        var item = ShortcutResolver.FromPath(folder);

        Assert.AreEqual(ShortcutKind.Folder, item.Kind);
        Assert.AreEqual("Folder Target", item.Name);
        Assert.AreEqual(folder, item.Target);
        Assert.AreEqual(folder, item.WorkingDirectory);
    }

    [TestMethod]
    public void FromPath_ClassifiesExecutables()
    {
        var exe = Path.Combine(testFolder, "sample.exe");
        File.WriteAllText(exe, string.Empty);

        var item = ShortcutResolver.FromPath(exe);

        Assert.AreEqual(ShortcutKind.Executable, item.Kind);
        Assert.AreEqual("sample", item.Name);
        Assert.AreEqual(testFolder, item.WorkingDirectory);
    }

    [TestMethod]
    public void FromText_ClassifiesUrls()
    {
        var item = ShortcutResolver.FromText("https://example.com/path");

        Assert.AreEqual(ShortcutKind.Url, item.Kind);
        Assert.AreEqual("example.com", item.Name);
        Assert.AreEqual("https://example.com/path", item.Target);
    }

    [TestMethod]
    public void FromText_ClassifiesShellTargets()
    {
        var item = ShortcutResolver.FromText("shell:Personal");

        Assert.AreEqual(ShortcutKind.Shell, item.Kind);
        Assert.AreEqual("Personal", item.Name);
    }

    [TestMethod]
    public void FromText_ClassifiesStoreApps()
    {
        var item = ShortcutResolver.FromText("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");

        Assert.AreEqual(ShortcutKind.StoreApp, item.Kind);
        Assert.AreEqual("Store app", item.Name);
    }

    [TestMethod]
    public void FromText_FallsBackToCommand()
    {
        var item = ShortcutResolver.FromText("echo hello");

        Assert.AreEqual(ShortcutKind.Command, item.Kind);
        Assert.AreEqual("Command", item.Name);
        Assert.AreEqual("echo hello", item.Target);
    }
}
