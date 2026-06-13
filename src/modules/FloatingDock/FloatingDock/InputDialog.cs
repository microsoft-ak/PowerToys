// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class InputDialog : Form
{
    private readonly TextBox input;

    public InputDialog(string title, string label, string initialValue)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 126);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var labelControl = new Label
        {
            AutoSize = true,
            Text = label,
            Location = new Point(12, 14),
        };

        input = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(12, 40),
            Size = new Size(396, 24),
            Text = initialValue,
        };

        var okButton = new Button
        {
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            DialogResult = DialogResult.OK,
            Text = "OK",
            Location = new Point(252, 88),
            Size = new Size(75, 28),
        };

        var cancelButton = new Button
        {
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            DialogResult = DialogResult.Cancel,
            Text = "Cancel",
            Location = new Point(333, 88),
            Size = new Size(75, 28),
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(new Control[] { labelControl, input, okButton, cancelButton });
    }

    public string Value => input.Text.Trim();

    public static string? ShowDialog(IWin32Window owner, string title, string label, string initialValue)
    {
        using var dialog = new InputDialog(title, label, initialValue);
        return dialog.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Value)
            ? dialog.Value
            : null;
    }
}
