// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.UnitTests.BackwardsCompatibility;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ViewModelTests
{
    [TestClass]
    public class FloatingDock
    {
        [TestMethod]
        public void IsEnabledShouldSendGeneralSettingsMessage()
        {
            var generalSettingsUtils = new Mock<SettingsUtils>(new FileSystem(), null);
            generalSettingsUtils
                .Setup(x => x.GetSettingsOrDefault<GeneralSettings>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new GeneralSettings());

            var moduleSettingsUtils = new Mock<SettingsUtils>(new FileSystem(), null);
            moduleSettingsUtils
                .Setup(x => x.GetSettingsOrDefault<FloatingDockSettings>(FloatingDockSettings.ModuleName, It.IsAny<string>()))
                .Returns(new FloatingDockSettings());

            var messages = new List<string>();
            var viewModel = new FloatingDockViewModel(
                moduleSettingsUtils.Object,
                new BackCompatTestProperties.MockSettingsRepository<GeneralSettings>(generalSettingsUtils.Object),
                msg =>
                {
                    messages.Add(msg);
                    return 0;
                });

            viewModel.IsEnabled = true;

            Assert.AreEqual(1, messages.Count);
            var outgoing = JsonSerializer.Deserialize<OutGoingGeneralSettings>(messages[0]);
            Assert.IsNotNull(outgoing);
            Assert.IsTrue(outgoing.GeneralSettings.Enabled.FloatingDock);
        }

        [TestMethod]
        public void SnapThresholdShouldSendModuleSettingsMessage()
        {
            var generalSettingsUtils = new Mock<SettingsUtils>(new FileSystem(), null);
            generalSettingsUtils
                .Setup(x => x.GetSettingsOrDefault<GeneralSettings>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new GeneralSettings());

            var moduleSettingsUtils = new Mock<SettingsUtils>(new FileSystem(), null);
            moduleSettingsUtils
                .Setup(x => x.GetSettingsOrDefault<FloatingDockSettings>(FloatingDockSettings.ModuleName, It.IsAny<string>()))
                .Returns(new FloatingDockSettings());

            var messages = new List<string>();
            var viewModel = new FloatingDockViewModel(
                moduleSettingsUtils.Object,
                new BackCompatTestProperties.MockSettingsRepository<GeneralSettings>(generalSettingsUtils.Object),
                msg =>
                {
                    messages.Add(msg);
                    return 0;
                });

            viewModel.SnapThreshold = 128;

            Assert.AreEqual(1, messages.Count);
            using var document = JsonDocument.Parse(messages[0]);
            var threshold = document.RootElement
                .GetProperty("powertoys")
                .GetProperty(FloatingDockSettings.ModuleName)
                .GetProperty("properties")
                .GetProperty("SnapThreshold")
                .GetProperty("value")
                .GetInt32();

            Assert.AreEqual(96, threshold);
        }
    }
}
