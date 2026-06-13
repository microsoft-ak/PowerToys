// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Text.Json;

using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;
using Microsoft.PowerToys.Settings.UI.SerializationContext;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public partial class FloatingDockViewModel : Observable
    {
        private const string ModuleName = FloatingDockSettings.ModuleName;
        private readonly Func<string, int> sendConfigMessage;
        private readonly GeneralSettings generalSettingsConfig;
        private FloatingDockSettings settings;
        private bool isEnabled;
        private bool startExpanded;
        private bool showLabels;
        private double snapThreshold;

        public FloatingDockViewModel(SettingsUtils settingsUtils, ISettingsRepository<GeneralSettings> settingsRepository, Func<string, int> ipcMSGCallBackFunc)
        {
            ArgumentNullException.ThrowIfNull(settingsUtils);
            ArgumentNullException.ThrowIfNull(settingsRepository);

            generalSettingsConfig = settingsRepository.SettingsConfig;
            sendConfigMessage = ipcMSGCallBackFunc;
            settings = LoadSettings(settingsUtils);

            InitializeEnabledValue();
            startExpanded = settings.Properties.StartExpanded.Value;
            showLabels = settings.Properties.ShowLabels.Value;
            snapThreshold = settings.Properties.SnapThreshold.Value;
        }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (isEnabled != value)
                {
                    isEnabled = value;
                    generalSettingsConfig.Enabled.FloatingDock = value;
                    OnPropertyChanged(nameof(IsEnabled));

                    OutGoingGeneralSettings outgoingMessage = new OutGoingGeneralSettings(generalSettingsConfig);
                    sendConfigMessage(outgoingMessage.ToString());
                }
            }
        }

        public bool IsEnabledGpoConfigured => false;

        public bool StartExpanded
        {
            get => startExpanded;
            set
            {
                if (startExpanded != value)
                {
                    startExpanded = value;
                    settings.Properties.StartExpanded.Value = value;
                    OnPropertyChanged(nameof(StartExpanded));
                    NotifySettingsChanged();
                }
            }
        }

        public bool ShowLabels
        {
            get => showLabels;
            set
            {
                if (showLabels != value)
                {
                    showLabels = value;
                    settings.Properties.ShowLabels.Value = value;
                    OnPropertyChanged(nameof(ShowLabels));
                    NotifySettingsChanged();
                }
            }
        }

        public double SnapThreshold
        {
            get => snapThreshold;
            set
            {
                var normalizedValue = Math.Clamp((int)Math.Round(value), 4, 96);
                if (Math.Abs(snapThreshold - normalizedValue) > 0.1)
                {
                    snapThreshold = normalizedValue;
                    settings.Properties.SnapThreshold.Value = normalizedValue;
                    OnPropertyChanged(nameof(SnapThreshold));
                    NotifySettingsChanged();
                }
            }
        }

        public void RefreshEnabledState()
        {
            InitializeEnabledValue();
            OnPropertyChanged(nameof(IsEnabled));
        }

        private static FloatingDockSettings LoadSettings(SettingsUtils settingsUtils)
        {
            try
            {
                return settingsUtils.GetSettingsOrDefault<FloatingDockSettings>(FloatingDockSettings.ModuleName);
            }
            catch (Exception e)
            {
                Logger.LogError($"Exception encountered while reading {FloatingDockSettings.ModuleName} settings.", e);
            }

            return new FloatingDockSettings();
        }

        private void InitializeEnabledValue()
        {
            isEnabled = generalSettingsConfig.Enabled.FloatingDock;
        }

        private void NotifySettingsChanged()
        {
            sendConfigMessage(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{{ \"powertoys\": {{ \"{0}\": {1} }} }}",
                    ModuleName,
                    JsonSerializer.Serialize(settings, SourceGenerationContextContext.Default.FloatingDockSettings)));
        }
    }
}
