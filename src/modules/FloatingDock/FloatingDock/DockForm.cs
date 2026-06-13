// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockForm : Form
{
    private readonly DockSettingsStore store;
    private readonly FlowLayoutPanel strip;
    private readonly Button toggleButton;
    private readonly Button addButton;
    private readonly Timer settingsRefreshTimer;
    private DockSettings settings;
    private DockState state;
    private DateTime settingsLastWrite;
    private bool draggingWindow;
    private Point dragOffset;

    public DockForm(DockSettingsStore store)
    {
        this.store = store;
        settings = store.LoadSettings();
        state = store.LoadState(settings);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        AllowDrop = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(28, 31, 36);
        Padding = new Padding(6);
        StartPosition = FormStartPosition.Manual;

        strip = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };

        toggleButton = CreateChromeButton(">");
        toggleButton.Click += (_, _) => ToggleExpanded();

        addButton = CreateChromeButton("+");
        addButton.AllowDrop = true;
        addButton.Click += (_, _) => AddCustomShortcut();

        Controls.Add(strip);
        ApplySavedLocation();
        BuildStrip();

        MouseDown += BeginWindowDrag;
        MouseMove += ContinueWindowDrag;
        MouseUp += EndWindowDrag;
        DragEnter += OnExternalDragEnter;
        DragDrop += OnExternalDragDrop;
        strip.MouseDown += BeginWindowDrag;
        strip.MouseMove += ContinueWindowDrag;
        strip.MouseUp += EndWindowDrag;
        strip.DragEnter += OnExternalDragEnter;
        strip.DragDrop += OnExternalDragDrop;
        toggleButton.MouseDown += BeginWindowDrag;
        toggleButton.MouseMove += ContinueWindowDrag;
        toggleButton.MouseUp += EndWindowDrag;
        addButton.DragEnter += OnExternalDragEnter;
        addButton.DragDrop += OnExternalDragDrop;

        ContextMenuStrip = CreateDockMenu();

        settingsRefreshTimer = new Timer
        {
            Interval = 1000,
        };
        settingsRefreshTimer.Tick += (_, _) => RefreshSettingsIfChanged();
        settingsRefreshTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        PersistState();
        settingsRefreshTimer.Stop();
        base.OnFormClosing(e);
    }

    private static Button CreateChromeButton(string text)
    {
        return new Button
        {
            Text = text,
            Width = 36,
            Height = 40,
            Margin = new Padding(3),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(48, 53, 61),
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
        };
    }

    private void BuildStrip()
    {
        strip.SuspendLayout();
        strip.Controls.Clear();

        toggleButton.Text = state.IsExpanded ? "<" : "Dock";
        toggleButton.Width = state.IsExpanded ? 36 : 74;
        strip.Controls.Add(toggleButton);

        if (state.IsExpanded)
        {
            for (var index = 0; index < state.Shortcuts.Count; index++)
            {
                strip.Controls.Add(CreateTile(state.Shortcuts[index], index));
            }

            strip.Controls.Add(addButton);
        }

        strip.ResumeLayout();
        ResizeToContent();
        PersistState();
    }

    private ShortcutTile CreateTile(ShortcutItem item, int index)
    {
        var tile = new ShortcutTile(item, index, settings.ShowLabels)
        {
            ContextMenuStrip = CreateShortcutMenu(index),
        };

        var mouseDownPoint = Point.Empty;
        tile.MouseDown += (_, args) => mouseDownPoint = args.Location;
        tile.MouseMove += (_, args) =>
        {
            if (args.Button == MouseButtons.Left &&
                (Math.Abs(args.X - mouseDownPoint.X) > 4 || Math.Abs(args.Y - mouseDownPoint.Y) > 4))
            {
                tile.DoDragDrop(index, DragDropEffects.Move);
            }
        };
        tile.Click += (_, _) => ShortcutResolver.Launch(item);
        tile.DragEnter += OnShortcutDragEnter;
        tile.DragDrop += (_, args) => ReorderShortcutFromDrop(args, index);

        return tile;
    }

    private ContextMenuStrip CreateDockMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Add shortcut...", null, (_, _) => AddCustomShortcut());
        menu.Items.Add(state.IsExpanded ? "Collapse" : "Expand", null, (_, _) => ToggleExpanded());
        menu.Items.Add(settings.ShowLabels ? "Hide labels" : "Show labels", null, (_, _) => ToggleLabels());
        menu.Items.Add("Reset position", null, (_, _) => ResetPosition());
        return menu;
    }

    private ContextMenuStrip CreateShortcutMenu(int index)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShortcutResolver.Launch(state.Shortcuts[index]));
        menu.Items.Add("Rename...", null, (_, _) => RenameShortcut(index));
        menu.Items.Add("Move left", null, (_, _) => MoveShortcut(index, -1));
        menu.Items.Add("Move right", null, (_, _) => MoveShortcut(index, 1));
        menu.Items.Add("Remove", null, (_, _) => RemoveShortcut(index));
        return menu;
    }

    private void ResizeToContent()
    {
        var tileWidth = settings.ShowLabels ? 84 : 50;
        var desiredWidth = state.IsExpanded ? 48 + 48 + (state.Shortcuts.Count * tileWidth) : 90;
        var workingArea = Screen.FromPoint(Location).WorkingArea;
        Width = Math.Min(Math.Max(desiredWidth, 90), Math.Max(workingArea.Width - 24, 90));
        Height = state.IsExpanded ? (settings.ShowLabels ? 76 : 56) : 52;
        strip.Location = new Point(Padding.Left, Padding.Top);
    }

    private void ToggleExpanded()
    {
        state.IsExpanded = !state.IsExpanded;
        BuildStrip();
    }

    private void ToggleLabels()
    {
        settings.ShowLabels = !settings.ShowLabels;
        store.SaveSettings(settings);
        ContextMenuStrip = CreateDockMenu();
        BuildStrip();
    }

    private void AddCustomShortcut()
    {
        var target = InputDialog.ShowDialog(this, "Add shortcut", "File, app, URL, shell target, or command", string.Empty);
        if (target is null)
        {
            return;
        }

        AddShortcut(ShortcutResolver.FromText(target));
    }

    private void RenameShortcut(int index)
    {
        var current = state.Shortcuts[index];
        var newName = InputDialog.ShowDialog(this, "Rename shortcut", "Display name", current.Name);
        if (newName is null)
        {
            return;
        }

        current.Name = newName;
        BuildStrip();
    }

    private void MoveShortcut(int index, int direction)
    {
        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= state.Shortcuts.Count)
        {
            return;
        }

        var item = state.Shortcuts[index];
        state.Shortcuts.RemoveAt(index);
        state.Shortcuts.Insert(newIndex, item);
        BuildStrip();
    }

    private void RemoveShortcut(int index)
    {
        state.Shortcuts.RemoveAt(index);
        BuildStrip();
    }

    private void ReorderShortcutFromDrop(DragEventArgs args, int targetIndex)
    {
        if (args.Data?.GetDataPresent(typeof(int)) != true)
        {
            return;
        }

        var sourceIndex = (int)args.Data.GetData(typeof(int))!;
        if (sourceIndex == targetIndex || sourceIndex < 0 || sourceIndex >= state.Shortcuts.Count)
        {
            return;
        }

        var item = state.Shortcuts[sourceIndex];
        state.Shortcuts.RemoveAt(sourceIndex);
        if (targetIndex > sourceIndex)
        {
            targetIndex--;
        }

        state.Shortcuts.Insert(targetIndex, item);
        BuildStrip();
    }

    private void AddShortcut(ShortcutItem item)
    {
        if (state.Shortcuts.Any(existing => string.Equals(existing.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        state.Shortcuts.Add(item);
        state.IsExpanded = true;
        BuildStrip();
    }

    private void OnExternalDragEnter(object? sender, DragEventArgs args)
    {
        args.Effect = HasShortcutData(args) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnExternalDragDrop(object? sender, DragEventArgs args)
    {
        if (args.Data?.GetDataPresent(DataFormats.FileDrop) == true &&
            args.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                AddShortcut(ShortcutResolver.FromPath(path));
            }
        }
        else if (args.Data?.GetDataPresent(DataFormats.Text) == true &&
                 args.Data.GetData(DataFormats.Text) is string text &&
                 !string.IsNullOrWhiteSpace(text))
        {
            AddShortcut(ShortcutResolver.FromText(text));
        }
    }

    private void OnShortcutDragEnter(object? sender, DragEventArgs args)
    {
        args.Effect = args.Data?.GetDataPresent(typeof(int)) == true ? DragDropEffects.Move : DragDropEffects.None;
    }

    private static bool HasShortcutData(DragEventArgs args)
    {
        return args.Data?.GetDataPresent(DataFormats.FileDrop) == true ||
               args.Data?.GetDataPresent(DataFormats.Text) == true;
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left)
        {
            return;
        }

        draggingWindow = true;
        dragOffset = args.Location;
    }

    private void ContinueWindowDrag(object? sender, MouseEventArgs args)
    {
        if (!draggingWindow)
        {
            return;
        }

        var cursor = Cursor.Position;
        Location = new Point(cursor.X - dragOffset.X, cursor.Y - dragOffset.Y);
    }

    private void EndWindowDrag(object? sender, MouseEventArgs args)
    {
        if (!draggingWindow)
        {
            return;
        }

        draggingWindow = false;
        SnapToNearestEdge();
    }

    private void SnapToNearestEdge()
    {
        var screen = Screen.FromRectangle(Bounds);
        var area = screen.WorkingArea;
        var threshold = Math.Max(4, settings.SnapThreshold);
        var newLeft = Left;
        var newTop = Top;
        var edge = "None";

        if (Math.Abs(Left - area.Left) <= threshold)
        {
            newLeft = area.Left;
            edge = "Left";
        }
        else if (Math.Abs(Right - area.Right) <= threshold)
        {
            newLeft = area.Right - Width;
            edge = "Right";
        }

        if (Math.Abs(Top - area.Top) <= threshold)
        {
            newTop = area.Top;
            edge = "Top";
        }
        else if (Math.Abs(Bottom - area.Bottom) <= threshold)
        {
            newTop = area.Bottom - Height;
            edge = "Bottom";
        }

        Location = new Point(
            Math.Clamp(newLeft, area.Left, area.Right - Width),
            Math.Clamp(newTop, area.Top, area.Bottom - Height));

        state.MonitorDeviceName = screen.DeviceName;
        state.SnapEdge = edge;
        PersistState();
    }

    private void ApplySavedLocation()
    {
        var screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == state.MonitorDeviceName) ?? Screen.PrimaryScreen!;
        var area = screen.WorkingArea;
        Location = new Point(
            Math.Clamp(state.Left, area.Left, area.Right - Math.Max(Width, 90)),
            Math.Clamp(state.Top, area.Top, area.Bottom - Math.Max(Height, 52)));
    }

    private void ResetPosition()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Top + 96);
        SnapToNearestEdge();
    }

    private void PersistState()
    {
        state.Left = Left;
        state.Top = Top;
        state.MonitorDeviceName = Screen.FromPoint(Location).DeviceName;
        store.SaveState(state);
    }

    private void RefreshSettingsIfChanged()
    {
        try
        {
            var settingsPath = Path.Combine(store.ModuleFolder, "settings.json");
            var lastWrite = File.Exists(settingsPath) ? File.GetLastWriteTimeUtc(settingsPath) : DateTime.MinValue;
            if (lastWrite == settingsLastWrite)
            {
                return;
            }

            settingsLastWrite = lastWrite;
            settings = store.LoadSettings();
            ContextMenuStrip = CreateDockMenu();
            BuildStrip();
        }
        catch
        {
        }
    }
}
