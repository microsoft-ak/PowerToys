// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using global::PowerToys.GPOWrapper;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public partial class QuickAccessDockViewModel : PageViewModelBase
    {
        protected override string ModuleName => QuickAccessDockSettings.ModuleName;

        private SettingsUtils SettingsUtils { get; set; }

        private GeneralSettings GeneralSettingsConfig { get; set; }

        private QuickAccessDockSettings Settings { get; set; }

        private Func<string, int> SendConfigMSG { get; }

        public QuickAccessDockViewModel(SettingsUtils settingsUtils, ISettingsRepository<GeneralSettings> settingsRepository, ISettingsRepository<QuickAccessDockSettings> moduleSettingsRepository, Func<string, int> ipcMSGCallBackFunc)
        {
            ArgumentNullException.ThrowIfNull(settingsUtils);
            SettingsUtils = settingsUtils;

            ArgumentNullException.ThrowIfNull(settingsRepository);
            GeneralSettingsConfig = settingsRepository.SettingsConfig;

            InitializeEnabledValue();

            ArgumentNullException.ThrowIfNull(moduleSettingsRepository);
            Settings = moduleSettingsRepository.SettingsConfig;

            _theme = Settings.Properties.Theme.Value;
            _style = Settings.Properties.Style.Value;
            _snapThreshold = Settings.Properties.SnapThreshold.Value;
            _autoHide = Settings.Properties.AutoHide.Value;
            _autoHideDelayMs = Settings.Properties.AutoHideDelayMs.Value;
            _syncWebsiteIcons = Settings.Properties.SyncWebsiteIcons.Value;

            SendConfigMSG = ipcMSGCallBackFunc;
        }

        private void InitializeEnabledValue()
        {
            _enabledGpoRuleConfiguration = GPOWrapper.GetConfiguredQuickAccessDockEnabledValue();
            if (_enabledGpoRuleConfiguration == GpoRuleConfigured.Disabled || _enabledGpoRuleConfiguration == GpoRuleConfigured.Enabled)
            {
                // Get the enabled state from GPO.
                _enabledStateIsGPOConfigured = true;
                _isEnabled = _enabledGpoRuleConfiguration == GpoRuleConfigured.Enabled;
            }
            else
            {
                _isEnabled = GeneralSettingsConfig.Enabled.QuickAccessDock;
            }
        }

        public override Dictionary<string, HotkeySettings[]> GetAllHotkeySettings() => new();

        public bool IsEnabled
        {
            get => _isEnabled;

            set
            {
                if (_enabledStateIsGPOConfigured)
                {
                    // If it's GPO configured, shouldn't be able to change this state.
                    return;
                }

                if (value != _isEnabled)
                {
                    _isEnabled = value;

                    GeneralSettingsConfig.Enabled.QuickAccessDock = value;
                    OutGoingGeneralSettings snd = new OutGoingGeneralSettings(GeneralSettingsConfig);

                    SendConfigMSG(snd.ToString());
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        public bool IsEnabledGpoConfigured => _enabledStateIsGPOConfigured;

        // 0 = Follow system, 1 = Light, 2 = Dark.
        public int Theme
        {
            get => _theme;

            set
            {
                if (value != _theme)
                {
                    _theme = value;
                    Settings.Properties.Theme.Value = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // 0 = Default (Fluent), 1 = Neomorphism, 2 = Acrylic.
        public int Style
        {
            get => _style;

            set
            {
                if (value != _style)
                {
                    _style = value;
                    Settings.Properties.Style.Value = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public int SnapThreshold
        {
            get => _snapThreshold;

            set
            {
                value = Math.Clamp(value, 4, 160);
                if (value != _snapThreshold)
                {
                    _snapThreshold = value;
                    Settings.Properties.SnapThreshold.Value = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool AutoHide
        {
            get => _autoHide;

            set
            {
                if (value != _autoHide)
                {
                    _autoHide = value;
                    Settings.Properties.AutoHide.Value = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public int AutoHideDelayMs
        {
            get => _autoHideDelayMs;

            set
            {
                value = Math.Clamp(value, 200, 10000);
                if (value != _autoHideDelayMs)
                {
                    _autoHideDelayMs = value;
                    Settings.Properties.AutoHideDelayMs.Value = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool SyncWebsiteIcons
        {
            get => _syncWebsiteIcons;

            set
            {
                if (value != _syncWebsiteIcons)
                {
                    _syncWebsiteIcons = value;
                    Settings.Properties.SyncWebsiteIcons.Value = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            OnPropertyChanged(propertyName);
            SettingsUtils.SaveSettings(Settings.ToJsonString(), QuickAccessDockSettings.ModuleName);
        }

        public void RefreshEnabledState()
        {
            InitializeEnabledValue();
            OnPropertyChanged(nameof(IsEnabled));
        }

        private GpoRuleConfigured _enabledGpoRuleConfiguration;
        private bool _enabledStateIsGPOConfigured;
        private bool _isEnabled;
        private int _theme;
        private int _style;
        private int _snapThreshold;
        private bool _autoHide;
        private int _autoHideDelayMs;
        private bool _syncWebsiteIcons;
    }
}
