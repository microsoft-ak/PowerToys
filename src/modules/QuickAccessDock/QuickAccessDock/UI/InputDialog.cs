// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuickAccessDock;

internal sealed class InputDialog : Form
{
    private readonly TextBox input;
    private readonly TextBox? titleInput;
    private Rectangle? dockBounds;

    public InputDialog(string title, string label, string initialValue, string? titleLabel = null, string titleInitialValue = "")
    {
        DockPalette.Refresh();

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = DockPalette.Surface;
        ForeColor = DockPalette.TextPrimary;
        Font = DockFonts.Body(9f);

        // The optional leading Title row (used only by the "Add new" flow) pushes
        // everything below it down by one row's worth of space.
        var offset = 0;
        if (titleLabel is not null)
        {
            var titleLabelControl = new Label
            {
                AutoSize = true,
                Text = titleLabel,
                ForeColor = DockPalette.TextSecondary,
                BackColor = DockPalette.Surface,
                Location = new Point(12, 14),
            };

            titleInput = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(12, 40),
                Size = new Size(396, 24),
                Text = titleInitialValue,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = DockPalette.TileFill,
                ForeColor = DockPalette.TextPrimary,
                MaxLength = 2048,
            };

            Controls.AddRange(new Control[] { titleLabelControl, titleInput });
            offset = 60;
        }

        ClientSize = new Size(420, 126 + offset);

        var labelControl = new Label
        {
            AutoSize = true,
            Text = label,
            ForeColor = DockPalette.TextSecondary,
            BackColor = DockPalette.Surface,
            Location = new Point(12, 14 + offset),
        };

        input = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(12, 40 + offset),
            Size = new Size(396, 24),
            Text = initialValue,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = DockPalette.TileFill,
            ForeColor = DockPalette.TextPrimary,
            MaxLength = 2048,
        };

        var okButton = CreateButton("OK", DialogResult.OK, new Point(252, 88 + offset), accent: true);
        var cancelButton = CreateButton("Cancel", DialogResult.Cancel, new Point(333, 88 + offset), accent: false);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(new Control[] { labelControl, input, okButton, cancelButton });
    }

    public string Value => input.Text.Trim();

    public string Title => titleInput?.Text.Trim() ?? string.Empty;

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

    /// <summary>
    /// Shows a two-field dialog with an optional Title above the required target field,
    /// used by "Add new" so a custom display name can be set up front instead of only
    /// through a follow-up Rename.
    /// </summary>
    public static (string Title, string Target)? ShowAddDialog(IWin32Window owner, string title, string label, string titleLabel)
    {
        using var dialog = new InputDialog(title, label, string.Empty, titleLabel)
        {
            dockBounds = (owner as Control)?.Bounds,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Value)
            ? (dialog.Title, dialog.Value)
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

        // Mirror the Command Palette window styling: theme-aware chrome and rounded
        // corners for every style.
        DockNativeMethods.SetImmersiveDarkMode(Handle, !DockPalette.IsLight);
        DockNativeMethods.SetRoundedCorners(Handle);

        // The automatic dark-mode caption scheme isn't reliably high-contrast on every
        // Windows build, so force white titlebar text explicitly rather than depending on it.
        if (!DockPalette.IsLight)
        {
            DockNativeMethods.SetCaptionColors(Handle, captionColor: null, textColor: Color.White);
        }
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
            Cursor = Cursors.Hand,
        };

        if (accent)
        {
            // Windows accent (default) button: the fill carries it, so no contrasting outline.
            // Hover lightens and press darkens, matching the system button's state feedback.
            button.FlatAppearance.BorderColor = DockPalette.Accent;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(DockPalette.Accent, 0.15f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(DockPalette.Accent, 0.08f);
        }
        else
        {
            // Windows secondary button: a subtle 1px border with hover/press fills.
            button.FlatAppearance.BorderColor = DockPalette.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = DockPalette.TileHover;
            button.FlatAppearance.MouseDownBackColor = DockPalette.TilePressed;
        }

        return button;
    }
}

