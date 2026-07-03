// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace QuickAccessDock;

/// <summary>
/// Loads and saves the dock's user settings and runtime state.
///
/// As a PowerToys module the user-facing settings (theme, auto-hide, snap threshold, …)
/// are owned by the PowerToys Settings UI, which writes them to
/// <c>%LOCALAPPDATA%\Microsoft\PowerToys\QuickAccessDock\settings.json</c> using the
/// standard PowerToys envelope (<c>name</c> / <c>version</c> / <c>properties</c>, with each
/// property wrapped as <c>{ "value": … }</c>). This store reads and writes that same file so
/// the dock stays in sync with the Settings UI in both directions.
///
/// The runtime state (window placement, snap edge, and the shortcut list) is private to the
/// dock and is persisted next to it as <c>dock.json</c>.
/// </summary>
internal sealed class DockSettingsStore
{
    // Must match Settings.UI.Library QuickAccessDockSettings.ModuleName and the module key.
    private const string ModuleName = "QuickAccessDock";

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
        AppFolder = string.IsNullOrWhiteSpace(appFolder) ? GetDefaultModuleFolder() : appFolder;
    }

    public string AppFolder { get; }

    public string SettingsFilePath => Path.Combine(AppFolder, "settings.json");

    public string IconCacheFolder => Path.Combine(AppFolder, "icons");

    private string DockFilePath => Path.Combine(AppFolder, "dock.json");

    public bool HasSavedState => File.Exists(DockFilePath);

    /// <summary>The PowerToys per-module save folder: <c>%LOCALAPPDATA%\Microsoft\PowerToys\QuickAccessDock</c>.</summary>
    public static string GetDefaultModuleFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "PowerToys",
            ModuleName);

    public DockSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var root = JsonNode.Parse(File.ReadAllText(SettingsFilePath));
                var properties = root?["properties"];
                if (properties is not null)
                {
                    return Normalize(FromProperties(properties));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickAccessDock] LoadSettings failed: {ex}");
        }

        return new DockSettings();
    }

    public void SaveSettings(DockSettings settings)
    {
        Directory.CreateDirectory(AppFolder);

        var envelope = new JsonObject
        {
            ["name"] = ModuleName,
            ["version"] = "1.0",
            ["properties"] = ToProperties(Normalize(settings)),
        };

        File.WriteAllText(SettingsFilePath, envelope.ToJsonString(SerializerOptions));
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickAccessDock] LoadState failed: {ex}");
        }

        return new DockState();
    }

    public void SaveState(DockState state)
    {
        Directory.CreateDirectory(AppFolder);
        File.WriteAllText(DockFilePath, JsonSerializer.Serialize(state, SerializerOptions));
    }

    // The Settings UI stores each property as a { "value": … } object (PowerToys BoolProperty /
    // IntProperty / StringProperty). These helpers read/write those wrappers so the JSON contract
    // stays identical to Settings.UI.Library QuickAccessDockProperties.
    private static DockSettings FromProperties(JsonNode properties) => new()
    {
        Theme = (DockTheme)ReadInt(properties, "theme", (int)DockTheme.System),
        Style = (DockThemeStyle)ReadInt(properties, "style", (int)DockThemeStyle.Default),
        SnapThreshold = ReadInt(properties, "snap-threshold", 32),
        AutoHide = ReadBool(properties, "auto-hide", true),
        AutoHideDelayMs = ReadInt(properties, "auto-hide-delay-ms", 1000),
        SyncWebsiteIconsAfterOpen = ReadBool(properties, "sync-website-icons", true),
        AccentColor = NullIfEmpty(ReadString(properties, "accent-color")),
    };

    private static JsonObject ToProperties(DockSettings s) => new()
    {
        ["theme"] = Value((int)s.Theme),
        ["style"] = Value((int)s.Style),
        ["snap-threshold"] = Value(s.SnapThreshold),
        ["auto-hide"] = Value(s.AutoHide),
        ["auto-hide-delay-ms"] = Value(s.AutoHideDelayMs),
        ["sync-website-icons"] = Value(s.SyncWebsiteIconsAfterOpen),
        ["accent-color"] = Value(s.AccentColor ?? string.Empty),
    };

    private static JsonObject Value(int v) => new() { ["value"] = v };

    private static JsonObject Value(bool v) => new() { ["value"] = v };

    private static JsonObject Value(string v) => new() { ["value"] = v };

    private static int ReadInt(JsonNode properties, string name, int fallback)
    {
        var value = properties[name]?["value"];
        return value is null ? fallback : value.GetValue<int>();
    }

    private static bool ReadBool(JsonNode properties, string name, bool fallback)
    {
        var value = properties[name]?["value"];
        return value is null ? fallback : value.GetValue<bool>();
    }

    private static string ReadString(JsonNode properties, string name)
    {
        var value = properties[name]?["value"];
        return value is null ? string.Empty : value.GetValue<string>();
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DockSettings Normalize(DockSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme))
        {
            settings.Theme = DockTheme.System;
        }

        if (!Enum.IsDefined(settings.Style))
        {
            settings.Style = DockThemeStyle.Default;
        }

        settings.SnapThreshold = Math.Clamp(settings.SnapThreshold, 4, 160);
        settings.AutoHideDelayMs = Math.Clamp(settings.AutoHideDelayMs, 200, 10000);

        if (DockPalette.ParseAccentColor(settings.AccentColor) is null)
        {
            settings.AccentColor = null;
        }

        return settings;
    }
}
