// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class FloatingDockProperties
    {
        public FloatingDockProperties()
        {
            StartExpanded = new BoolProperty(true);
            ShowLabels = new BoolProperty(false);
            SnapThreshold = new IntProperty(32);
        }

        [JsonPropertyName("StartExpanded")]
        public BoolProperty StartExpanded { get; set; }

        [JsonPropertyName("ShowLabels")]
        public BoolProperty ShowLabels { get; set; }

        [JsonPropertyName("SnapThreshold")]
        public IntProperty SnapThreshold { get; set; }

        public override string ToString() => JsonSerializer.Serialize(this, SettingsSerializationContext.Default.FloatingDockProperties);
    }
}
