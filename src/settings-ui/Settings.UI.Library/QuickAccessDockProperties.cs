// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    // Needs to be kept in sync with the DockSettingsStore property contract in
    // src\modules\QuickAccessDock\QuickAccessDock\Settings\DockSettingsStore.cs
    public class QuickAccessDockProperties
    {
        // Theme index: 0 = System, 1 = Light, 2 = Dark.
        public const int DefaultTheme = 0;

        // Style index: 0 = Default (Fluent), 1 = Neomorphism, 2 = Acrylic.
        public const int DefaultStyle = 0;

        public const int DefaultSnapThreshold = 32;
        public const bool DefaultAutoHide = true;
        public const int DefaultAutoHideDelayMs = 1000;
        public const bool DefaultSyncWebsiteIcons = true;
        public const string DefaultAccentColor = "";

        public QuickAccessDockProperties()
        {
            Theme = new IntProperty(DefaultTheme);
            Style = new IntProperty(DefaultStyle);
            SnapThreshold = new IntProperty(DefaultSnapThreshold);
            AutoHide = new BoolProperty(DefaultAutoHide);
            AutoHideDelayMs = new IntProperty(DefaultAutoHideDelayMs);
            SyncWebsiteIcons = new BoolProperty(DefaultSyncWebsiteIcons);
            AccentColor = new StringProperty(DefaultAccentColor);
        }

        [JsonPropertyName("theme")]
        public IntProperty Theme { get; set; }

        [JsonPropertyName("style")]
        public IntProperty Style { get; set; }

        [JsonPropertyName("snap-threshold")]
        public IntProperty SnapThreshold { get; set; }

        [JsonPropertyName("auto-hide")]
        public BoolProperty AutoHide { get; set; }

        [JsonPropertyName("auto-hide-delay-ms")]
        public IntProperty AutoHideDelayMs { get; set; }

        [JsonPropertyName("sync-website-icons")]
        public BoolProperty SyncWebsiteIcons { get; set; }

        [JsonPropertyName("accent-color")]
        public StringProperty AccentColor { get; set; }
    }
}
