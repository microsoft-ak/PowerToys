// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class FloatingDockSettings : BasePTModuleSettings, ISettingsConfig
    {
        public const string ModuleName = "FloatingDock";
        public const string ModuleVersion = "1.0";

        public FloatingDockSettings()
        {
            Name = ModuleName;
            Version = ModuleVersion;
            Properties = new FloatingDockProperties();
        }

        [JsonPropertyName("properties")]
        public FloatingDockProperties Properties { get; set; }

        public string GetModuleName()
        {
            return Name;
        }

        public bool UpgradeSettingsConfiguration()
        {
            return false;
        }
    }
}
