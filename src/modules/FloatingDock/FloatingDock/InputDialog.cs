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
    private Rectangle? dockBounds;

    public InputDialog(string title, string label, string initialValue)
    {
        DockPalette.Refresh();

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(420, 126);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = DockPalette.Surface;
        ForeColor = DockPalette.TextPrimary;
        Font = new Font("Segoe UI", 9f);

        var labelControl = new Label
        {
            AutoSize = true,
            Text = label,
            ForeColor = DockPalette.TextSecondary,
            BackColor = Color.Transparent,
            Location = new Point(12, 14),
        };

        input = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(12, 40),
            Size = new Size(396, 24),
            Text = initialValue,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = DockPalette.TileFill,
            ForeColor = DockPalette.TextPrimary,
        };

        var okButton = CreateButton("OK", DialogResult.OK, new Point(252, 88), accent: true);
        var cancelButton = CreateButton("Cancel", DialogResult.Cancel, new Point(333, 88), accent: false);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(new Control[] { labelControl, input, okButton, cancelButton });
    }

    public string Value => input.Text.Trim();

    public static string? ShowDialog(IWin32Window owner, string title, string label, string initialValue)
    {
        using var dialog = new InputDialog(title, label, initialValue)
        {
            dockBounds = (owner as Control)?.Bounds,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Value)
            ? dialog.Value
            : null;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PositionRelativeToDock();
    }

    private void PositionRelativeToDock()
    {
        if (dockBounds is not Rectangle dock)
        {
            // No dock info: center on the screen under the cursor.
            var fallback = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(fallback.Left + ((fallback.Width - Width) / 2), fallback.Top + ((fallback.Height - Height) / 2));
            return;
        }

        var area = Screen.FromRectangle(dock).WorkingArea;
        const int gap = 8;

        // Centered horizontally on the dock, clamped to the screen.
        var x = Math.Max(area.Left, Math.Min(dock.Left + ((dock.Width - Width) / 2), area.Right - Width));

        // Below the dock by default; above it when the dock sits past three-quarters of the
        // screen height (or when there is no room below), so the dialog stays on-screen.
        var threeQuarterLine = area.Top + (area.Height * 3 / 4);
        var below = dock.Bottom + gap;
        var placeAbove = dock.Top >= threeQuarterLine || below + Height > area.Bottom;
        var y = placeAbove ? dock.Top - gap - Height : below;
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - Height));

        Location = new Point(x, y);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Mirror the Command Palette window styling: theme-aware chrome, rounded
        // corners and an acrylic blur-behind material for a glassmorphic look.
        DockNativeMethods.SetImmersiveDarkMode(Handle, !DockPalette.IsLight);
        DockNativeMethods.SetRoundedCorners(Handle);
        DockNativeMethods.EnableAcrylic(Handle, Color.FromArgb(DockPalette.IsLight ? 200 : 190, DockPalette.Surface));
    }

    private Button CreateButton(string text, DialogResult result, Point location, bool accent)
    {
        var button = new Button
        {
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            DialogResult = result,
            Text = text,
            Location = location,
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.Flat,
            ForeColor = accent ? Color.White : DockPalette.TextPrimary,
            BackColor = accent ? DockPalette.Accent : DockPalette.TileFill,
            UseVisualStyleBackColor = false,
        };

        button.FlatAppearance.BorderColor = DockPalette.Border;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }
}
