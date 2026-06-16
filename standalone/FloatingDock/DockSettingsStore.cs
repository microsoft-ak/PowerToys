// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FloatingDock;

internal sealed class DockSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public DockSettingsStore()
        : this(null)
    {
    }

    public DockSettingsStore(string? appFolder)
    {
        AppFolder = string.IsNullOrWhiteSpace(appFolder) ?
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FloatingDock") :
            appFolder;
    }

    public string AppFolder { get; }

    public string SettingsFilePath => Path.Combine(AppFolder, "settings.json");

    private string DockFilePath => Path.Combine(AppFolder, "dock.json");

    public bool HasSavedState => File.Exists(DockFilePath);

    public DockSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var settings = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(SettingsFilePath), SerializerOptions);
                if (settings is not null)
                {
                    return Normalize(settings);
                }
            }
        }
        catch
        {
        }

        return new DockSettings();
    }

    public void SaveSettings(DockSettings settings)
    {
        Directory.CreateDirectory(AppFolder);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(Normalize(settings), SerializerOptions));
    }

    public DockState LoadState()
    {
        try
        {
            if (File.Exists(DockFilePath))
            {
                var state = JsonSerializer.Deserialize<DockState>(File.ReadAllText(DockFilePath), SerializerOptions);
                if (state is not null)
                {
                    return state;
                }
            }
        }
        catch
        {
        }

        return new DockState();
    }

    public void SaveState(DockState state)
    {
        Directory.CreateDirectory(AppFolder);
        File.WriteAllText(DockFilePath, JsonSerializer.Serialize(state, SerializerOptions));
    }

    private static DockSettings Normalize(DockSettings settings)
    {
        settings.SnapThreshold = Math.Clamp(settings.SnapThreshold, 4, 160);
        settings.AutoHideDelayMs = Math.Clamp(settings.AutoHideDelayMs, 200, 10000);
        return settings;
    }
}
