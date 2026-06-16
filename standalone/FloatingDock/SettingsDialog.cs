// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace FloatingDock;

internal sealed class SettingsDialog : Form
{
    private readonly CheckBox autoHideCheckBox;
    private readonly NumericUpDown autoHideDelayInput;
    private readonly NumericUpDown snapThresholdInput;

    public SettingsDialog(DockSettings settings)
    {
        Text = "Floating Dock Settings";
        AccessibleName = "Floating Dock Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 174);
        Font = SystemFonts.MessageBoxFont;

        autoHideCheckBox = new CheckBox
        {
            Text = "Auto-hide when snapped",
            Checked = settings.AutoHide,
            AutoSize = true,
            Location = new Point(16, 16),
        };

        var delayLabel = new Label
        {
            Text = "Auto-hide delay (ms)",
            AutoSize = true,
            Location = new Point(16, 54),
        };

        autoHideDelayInput = new NumericUpDown
        {
            Minimum = 200,
            Maximum = 10000,
            Increment = 100,
            Value = Math.Clamp(settings.AutoHideDelayMs, 200, 10000),
            Location = new Point(190, 50),
            Width = 130,
        };

        var snapLabel = new Label
        {
            Text = "Snap threshold (px)",
            AutoSize = true,
            Location = new Point(16, 88),
        };

        snapThresholdInput = new NumericUpDown
        {
            Minimum = 4,
            Maximum = 160,
            Increment = 4,
            Value = Math.Clamp(settings.SnapThreshold, 4, 160),
            Location = new Point(190, 84),
            Width = 130,
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(164, 130),
            Size = new Size(75, 28),
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(245, 130),
            Size = new Size(75, 28),
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(new Control[]
        {
            autoHideCheckBox,
            delayLabel,
            autoHideDelayInput,
            snapLabel,
            snapThresholdInput,
            okButton,
            cancelButton,
        });
    }

    public DockSettings Settings => new()
    {
        AutoHide = autoHideCheckBox.Checked,
        AutoHideDelayMs = (int)autoHideDelayInput.Value,
        SnapThreshold = (int)snapThresholdInput.Value,
    };
}
