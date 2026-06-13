// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerToys.FloatingDock;

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

    public DockSettingsStore(string? moduleFolder)
    {
        ModuleFolder = string.IsNullOrWhiteSpace(moduleFolder) ?
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "PowerToys",
                "FloatingDock") :
            moduleFolder;
    }

    public string ModuleFolder { get; }

    private string SettingsFilePath => Path.Combine(ModuleFolder, "settings.json");

    private string DockFilePath => Path.Combine(ModuleFolder, "dock.json");

    public bool HasSavedState => File.Exists(DockFilePath);

    public DockSettings LoadSettings()
    {
        var settings = new DockSettings();

        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return settings;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(SettingsFilePath));
            if (!document.RootElement.TryGetProperty("properties", out var properties))
            {
                return settings;
            }

            settings.StartExpanded = ReadBool(properties, "StartExpanded", settings.StartExpanded);
            settings.ShowLabels = ReadBool(properties, "ShowLabels", settings.ShowLabels);
            settings.SnapThreshold = ReadInt(properties, "SnapThreshold", settings.SnapThreshold);
        }
        catch
        {
            return new DockSettings();
        }

        return settings;
    }

    public void SaveSettings(DockSettings settings)
    {
        Directory.CreateDirectory(ModuleFolder);
        var payload = new
        {
            name = "FloatingDock",
            version = "1.0",
            properties = new
            {
                StartExpanded = new { value = settings.StartExpanded },
                ShowLabels = new { value = settings.ShowLabels },
                SnapThreshold = new { value = settings.SnapThreshold },
            },
        };

        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(payload, SerializerOptions));
    }

    public DockState LoadState(DockSettings settings)
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

        return new DockState
        {
            IsExpanded = settings.StartExpanded,
        };
    }

    public void SaveState(DockState state)
    {
        Directory.CreateDirectory(ModuleFolder);
        File.WriteAllText(DockFilePath, JsonSerializer.Serialize(state, SerializerOptions));
    }

    private static bool ReadBool(JsonElement properties, string key, bool defaultValue)
    {
        return properties.TryGetProperty(key, out var property) &&
               property.TryGetProperty("value", out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;
    }

    private static int ReadInt(JsonElement properties, string key, int defaultValue)
    {
        return properties.TryGetProperty(key, out var property) &&
               property.TryGetProperty("value", out var value) &&
               value.TryGetInt32(out var result)
            ? result
            : defaultValue;
    }
}
