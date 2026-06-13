// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.PowerToys.FloatingDock.UnitTests;

[TestClass]
public sealed class DockSettingsStoreTests
{
    private string testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        testFolder = Path.Combine(Path.GetTempPath(), "PowerToysFloatingDockTests", Guid.NewGuid().ToString("N"));
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
    public void LoadSettings_WhenNoFile_ReturnsDefaults()
    {
        var store = new DockSettingsStore(testFolder);

        var settings = store.LoadSettings();

        Assert.IsTrue(settings.StartExpanded);
        Assert.IsFalse(settings.ShowLabels);
        Assert.AreEqual(32, settings.SnapThreshold);
    }

    [TestMethod]
    public void SaveAndLoadSettings_RoundTripsValues()
    {
        var store = new DockSettingsStore(testFolder);
        store.SaveSettings(new DockSettings
        {
            StartExpanded = false,
            ShowLabels = false,
            SnapThreshold = 64,
        });

        var settings = store.LoadSettings();

        Assert.IsFalse(settings.StartExpanded);
        Assert.IsFalse(settings.ShowLabels);
        Assert.AreEqual(64, settings.SnapThreshold);
    }

    [TestMethod]
    public void SaveAndLoadState_RoundTripsShortcutsAndPosition()
    {
        var store = new DockSettingsStore(testFolder);
        var state = new DockState
        {
            IsExpanded = false,
            Left = 12,
            Top = 34,
            MonitorDeviceName = "\\\\.\\DISPLAY2",
            SnapEdge = DockSnap.RightEdge,
            Shortcuts =
            {
                new ShortcutItem
                {
                    Name = "Docs",
                    Target = "shell:Personal",
                    Kind = ShortcutKind.Shell,
                },
            },
        };

        store.SaveState(state);
        var loaded = store.LoadState(new DockSettings());

        Assert.IsTrue(store.HasSavedState);
        Assert.IsFalse(loaded.IsExpanded);
        Assert.AreEqual(12, loaded.Left);
        Assert.AreEqual(34, loaded.Top);
        Assert.AreEqual("\\\\.\\DISPLAY2", loaded.MonitorDeviceName);
        Assert.AreEqual(DockSnap.RightEdge, loaded.SnapEdge);
        Assert.AreEqual(1, loaded.Shortcuts.Count);
        Assert.AreEqual("Docs", loaded.Shortcuts[0].Name);
    }

    [TestMethod]
    public void LoadState_WhenShortcutKindIsString_LoadsShortcut()
    {
        Directory.CreateDirectory(testFolder);
        var stateJson =
            """
            {
              "IsExpanded": true,
              "Left": 10,
              "Top": 20,
              "SnapEdge": "None",
              "Shortcuts": [
                {
                  "Name": "Docs",
                  "Target": "shell:Personal",
                  "Kind": "Shell"
                }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(testFolder, "dock.json"), stateJson);
        var store = new DockSettingsStore(testFolder);

        var loaded = store.LoadState(new DockSettings());

        Assert.AreEqual(1, loaded.Shortcuts.Count);
        Assert.AreEqual(ShortcutKind.Shell, loaded.Shortcuts[0].Kind);
        Assert.AreEqual("shell:Personal", loaded.Shortcuts[0].Target);
    }

    [TestMethod]
    public void LoadSettings_WhenJsonIsMalformed_ReturnsDefaults()
    {
        Directory.CreateDirectory(testFolder);
        File.WriteAllText(Path.Combine(testFolder, "settings.json"), "{ not json");
        var store = new DockSettingsStore(testFolder);

        var settings = store.LoadSettings();

        Assert.IsTrue(settings.StartExpanded);
        Assert.IsFalse(settings.ShowLabels);
        Assert.AreEqual(32, settings.SnapThreshold);
    }
}
