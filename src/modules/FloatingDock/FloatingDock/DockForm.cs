// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockForm : Form
{
    private const int DockHeight = 54;
    private const int MinDockWidth = 154;
    private const int CornerRadius = 7;

    private readonly DockSettingsStore store;
    private readonly FlowLayoutPanel strip;
    private readonly DockHubButton hubButton;
    private readonly DockActionButton addButton;
    private readonly DockActionButton menuButton;
    private readonly DockSummaryPanel summaryPanel;
    private readonly DockSeparator separator;
    private readonly Timer settingsRefreshTimer;
    private readonly ToolTip toolTip = new();
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

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        TopMost = true;
        AllowDrop = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "PowerToys Floating Dock";
        AccessibleName = "PowerToys Floating Dock";
        AccessibleDescription = "Floating always-on-top shortcut dock";
        AccessibleRole = AccessibleRole.ToolBar;
        Padding = new Padding(6, 6, 6, 6);
        StartPosition = FormStartPosition.Manual;
        BackColor = DockPalette.Surface;

        strip = new FlowLayoutPanel
        {
            AutoSize = false,
            BackColor = DockPalette.Surface,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };

        hubButton = new DockHubButton();
        hubButton.Click += (_, _) => ToggleExpanded();

        summaryPanel = new DockSummaryPanel();
        summaryPanel.DoubleClick += (_, _) => AddCustomShortcut();
        summaryPanel.DragEnter += OnExternalDragEnter;
        summaryPanel.DragDrop += OnExternalDragDrop;

        separator = new DockSeparator();

        addButton = new DockActionButton(DockActionKind.Add)
        {
            AllowDrop = true,
        };
        addButton.Click += (_, _) => AddCustomShortcut();
        addButton.DragEnter += OnExternalDragEnter;
        addButton.DragDrop += OnExternalDragDrop;

        menuButton = new DockActionButton(DockActionKind.More);
        menuButton.Click += (_, _) => ShowDockMenu();

        toolTip.SetToolTip(hubButton, "Expand or collapse");
        toolTip.SetToolTip(summaryPanel, "Drag the dock or drop shortcuts here");
        toolTip.SetToolTip(addButton, "Add shortcut");
        toolTip.SetToolTip(menuButton, "More options");

        Controls.Add(strip);
        ApplyTheme();
        BuildStrip(persist: false);
        ApplySavedLocation();
        PersistState();

        AttachWindowDrag(this);
        AttachWindowDrag(strip);
        AttachWindowDrag(hubButton);
        AttachWindowDrag(summaryPanel);

        KeyDown += OnDockKeyDown;
        DragEnter += OnExternalDragEnter;
        DragDrop += OnExternalDragDrop;
        strip.DragEnter += OnExternalDragEnter;
        strip.DragDrop += OnExternalDragDrop;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

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
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        toolTip.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = DockDrawing.CreateRoundedRectanglePath(bounds, CornerRadius);
        using var fill = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Window : DockPalette.Surface);
        using var border = new Pen(SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(78, 82, 90), 1.0f);

        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateWindowRegion();
    }

    private void AttachWindowDrag(Control control)
    {
        control.MouseDown += BeginWindowDrag;
        control.MouseMove += ContinueWindowDrag;
        control.MouseUp += EndWindowDrag;
    }

    private void BuildStrip(bool persist = true)
    {
        strip.SuspendLayout();
        strip.Controls.Clear();

        hubButton.ShortcutCount = state.Shortcuts.Count;
        hubButton.IsExpanded = state.IsExpanded;
        hubButton.AccessibleDescription = state.IsExpanded ? "Collapse dock" : "Expand dock";
        strip.Controls.Add(hubButton);

        if (state.IsExpanded)
        {
            if (state.Shortcuts.Count == 0)
            {
                summaryPanel.Width = 72;
                summaryPanel.PrimaryText = "Drop links";
                summaryPanel.SecondaryText = "or files";
                strip.Controls.Add(summaryPanel);
            }
            else
            {
                for (var index = 0; index < state.Shortcuts.Count; index++)
                {
                    strip.Controls.Add(CreateTile(state.Shortcuts[index], index));
                }
            }
        }
        else
        {
            summaryPanel.Width = 72;
            summaryPanel.PrimaryText = "Shortcuts";
            summaryPanel.SecondaryText = state.Shortcuts.Count == 1 ? "1 pinned" : $"{state.Shortcuts.Count} pinned";
            strip.Controls.Add(summaryPanel);
        }

        strip.Controls.Add(separator);
        strip.Controls.Add(addButton);
        strip.Controls.Add(menuButton);

        strip.ResumeLayout();
        ResizeToContent();
        ApplyTheme();
        ContextMenuStrip = CreateDockMenu();

        if (persist)
        {
            PersistState();
        }
    }

    private ShortcutTile CreateTile(ShortcutItem item, int index)
    {
        var tile = new ShortcutTile(item, index, settings.ShowLabels)
        {
            ContextMenuStrip = CreateShortcutMenu(index),
        };
        toolTip.SetToolTip(tile, $"{item.Name}\n{item.Target}");

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
        tile.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Delete)
            {
                RemoveShortcut(index);
                args.Handled = true;
            }
            else if (args.KeyCode == Keys.F2)
            {
                RenameShortcut(index);
                args.Handled = true;
            }
        };
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

    private void ShowDockMenu()
    {
        ContextMenuStrip = CreateDockMenu();
        ContextMenuStrip.Show(menuButton, new Point(0, menuButton.Height + 2));
    }

    private void ResizeToContent()
    {
        strip.Location = new Point(Padding.Left, Padding.Top);

        var contentWidth = strip.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        var workingArea = Screen.FromPoint(Location).WorkingArea;
        Width = Math.Min(Math.Max(contentWidth + Padding.Horizontal, MinDockWidth), Math.Max(workingArea.Width - 24, MinDockWidth));
        Height = DockHeight;
        strip.Size = new Size(Width - Padding.Horizontal, Height - Padding.Vertical);
        UpdateWindowRegion();
    }

    private void ApplyTheme()
    {
        BackColor = SystemInformation.HighContrast ? SystemColors.Window : DockPalette.Surface;
        ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : Color.White;
        strip.BackColor = BackColor;

        foreach (Control control in strip.Controls)
        {
            control.BackColor = BackColor;
            control.ForeColor = ForeColor;
            control.Invalidate();
        }

        Invalidate();
    }

    private void UpdateWindowRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = DockDrawing.CreateRoundedRectanglePath(new Rectangle(0, 0, Width, Height), CornerRadius);
        var previousRegion = Region;
        Region = new Region(path);
        previousRegion?.Dispose();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            ApplyTheme();
        }
    }

    private void OnDockKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyCode == Keys.Escape && state.IsExpanded)
        {
            ToggleExpanded();
            args.Handled = true;
        }
        else if (args.KeyCode == Keys.Insert || (args.Control && args.KeyCode == Keys.N))
        {
            AddCustomShortcut();
            args.Handled = true;
        }
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
        if (args.Button != MouseButtons.Left || sender is not Control control)
        {
            return;
        }

        draggingWindow = true;
        dragOffset = PointToClient(control.PointToScreen(args.Location));
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
        var result = DockSnap.Snap(Bounds, area, settings.SnapThreshold);
        Bounds = result.Bounds;

        state.MonitorDeviceName = screen.DeviceName;
        state.SnapEdge = result.Edge;
        PersistState();
    }

    private void ApplySavedLocation()
    {
        var screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == state.MonitorDeviceName) ?? Screen.PrimaryScreen!;
        var area = screen.WorkingArea;
        Location = store.HasSavedState ?
            DockSnap.ClampLocation(Size, new Point(state.Left, state.Top), area) :
            DockSnap.DefaultLocation(Size, area);
    }

    private void ResetPosition()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = DockSnap.DefaultLocation(Size, area);
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
