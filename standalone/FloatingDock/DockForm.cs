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

namespace FloatingDock;

internal sealed class DockForm : Form
{
    // The "breadth" is the dock's thickness: its height when horizontal, its width when vertical.
    private const int DockBreadth = 54;

    // Minimums are kept just large enough for the (empty) dock to hold the overflow button,
    // so the dock always hugs its content instead of leaving a gap after the last item.
    private const int MinHorizontalWidth = 44;
    private const int MinVerticalHeight = 44;
    private const int CornerRadius = 10;

    // Spacing between horizontal shortcut tiles (kept tight).
    private const int ItemSpacing = 2;

    // The overflow button's size along the dock axis (its width when horizontal, its
    // height when vertical). Kept small so the ellipsis carries no extra padding.
    private const int EllipsisThickness = 16;

    // Height of a tile in the vertical column. Sized close to the icon so stacked tiles
    // sit right against each other with no gap.
    private const int VerticalTileHeight = 38;

    // Sliver left on-screen when the dock auto-hides against an edge; also the notch grip.
    private const int RevealStripPx = 6;
    private const int NotchThickness = 4;
    private const int NotchLength = 34;

    private readonly DockSettingsStore store;
    private readonly FlowLayoutPanel strip;
    private readonly DockActionButton menuButton;
    private readonly Timer settingsRefreshTimer;
    private readonly Timer autoHideTimer;
    private readonly Timer slideTimer;
    private readonly ToolTip toolTip = new();
    private DockSettings settings;
    private DockState state;
    private DateTime settingsLastWrite;
    private bool draggingWindow;
    private bool dragMoved;
    private Point dragOffset;

    private DockOrientation orientation = DockOrientation.Horizontal;
    private string snapEdge = DockSnap.NoEdge;
    private Rectangle shownBounds;
    private bool isHidden;
    private bool modalOpen;
    private DateTime lastInteractionUtc = DateTime.UtcNow;
    private Point slideTarget;

    public DockForm(DockSettingsStore store)
    {
        this.store = store;
        settings = store.LoadSettings();
        state = store.LoadState();

        snapEdge = state.SnapEdge ?? DockSnap.NoEdge;
        orientation = IsVerticalEdge(snapEdge) ? DockOrientation.Vertical : DockOrientation.Horizontal;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        TopMost = true;
        AllowDrop = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "Floating Dock";
        AccessibleName = "Floating Dock";
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

            // The strip background is the dock's drag handle, but keep the normal pointer
            // here rather than a move cursor.
            Cursor = Cursors.Default,
        };

        menuButton = new DockActionButton();
        menuButton.Click += (_, _) => ShowDockMenu();

        toolTip.SetToolTip(menuButton, "More options");

        Controls.Add(strip);
        ApplyTheme();
        BuildStrip(persist: false);
        InitializePlacement();
        PersistState();

        AttachWindowDrag(this);
        AttachWindowDrag(strip);

        KeyDown += OnDockKeyDown;
        DragEnter += OnExternalDragEnter;
        DragDrop += OnExternalDragDrop;
        strip.DragEnter += OnExternalDragEnter;
        strip.DragDrop += OnExternalDragDrop;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ContextMenuStrip = CreateDockMenu();

        settingsRefreshTimer = new Timer { Interval = 1000 };
        settingsRefreshTimer.Tick += (_, _) => RefreshSettingsIfChanged();
        settingsRefreshTimer.Start();

        autoHideTimer = new Timer { Interval = 120 };
        autoHideTimer.Tick += (_, _) => OnAutoHidePoll();
        autoHideTimer.Start();

        slideTimer = new Timer { Interval = 15 };
        slideTimer.Tick += (_, _) => OnSlideTick();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyGlass();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        PersistState();
        settingsRefreshTimer.Stop();
        autoHideTimer.Stop();
        slideTimer.Stop();
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
        using var border = new Pen(SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.Border, 1.0f);

        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        DrawNotch(e.Graphics);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateWindowRegion();
    }

    private static bool IsVerticalEdge(string edge) => edge is DockSnap.LeftEdge or DockSnap.RightEdge;

    private void DrawNotch(Graphics graphics)
    {
        if (snapEdge == DockSnap.NoEdge)
        {
            return;
        }

        // The notch sits on the inward-facing side of the dock (the sliver that stays
        // on-screen when auto-hidden), so it doubles as the reveal grip (PC Manager style).
        // When hidden it is brightened to stand out as a handle.
        var color = isHidden ? DockPalette.AccentSoft : DockPalette.Notch;
        using var brush = new SolidBrush(color);

        const int gap = 2;
        Rectangle rect = snapEdge switch
        {
            DockSnap.LeftEdge => new Rectangle(Width - gap - NotchThickness, (Height - NotchLength) / 2, NotchThickness, NotchLength),
            DockSnap.RightEdge => new Rectangle(gap, (Height - NotchLength) / 2, NotchThickness, NotchLength),
            DockSnap.TopEdge => new Rectangle((Width - NotchLength) / 2, Height - gap - NotchThickness, NotchLength, NotchThickness),
            DockSnap.BottomEdge => new Rectangle((Width - NotchLength) / 2, gap, NotchLength, NotchThickness),
            _ => Rectangle.Empty,
        };

        if (rect.IsEmpty)
        {
            return;
        }

        var radius = Math.Min(rect.Width, rect.Height) / 2;
        using var notch = DockDrawing.CreateRoundedRectanglePath(rect, Math.Max(1, radius));
        graphics.FillPath(brush, notch);
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

        // The overflow (...) button leads the strip: left edge when horizontal, top edge
        // when vertical. Shortcuts follow after it.
        strip.Controls.Add(menuButton);

        var count = state.Shortcuts.Count;
        for (var index = 0; index < count; index++)
        {
            strip.Controls.Add(CreateTile(state.Shortcuts[index], index));
        }

        ApplyOrientationLayout();
        strip.ResumeLayout();
        ResizeToContent();
        ApplyTheme();
        ContextMenuStrip = CreateDockMenu();

        if (persist)
        {
            PersistState();
        }
    }

    private void ApplyOrientationLayout()
    {
        var innerBreadth = DockBreadth - Padding.Horizontal;
        var lastIndex = strip.Controls.Count - 1;

        menuButton.DockOrientation = orientation;

        if (orientation == DockOrientation.Vertical)
        {
            strip.FlowDirection = FlowDirection.TopDown;

            // Thin leading ellipsis and short tiles, with no margin between any items, so
            // the column stacks tightly with no gaps.
            menuButton.Size = new Size(38, EllipsisThickness);

            foreach (Control control in strip.Controls)
            {
                if (control is ShortcutTile)
                {
                    control.Size = new Size(38, VerticalTileHeight);
                }

                var side = Math.Max(0, (innerBreadth - control.Width) / 2);
                control.Margin = new Padding(side, 0, side, 0);
            }
        }
        else
        {
            strip.FlowDirection = FlowDirection.LeftToRight;
            menuButton.Size = new Size(EllipsisThickness, 40);

            for (var i = 0; i < strip.Controls.Count; i++)
            {
                var control = strip.Controls[i];

                // The ellipsis sits flush against the first item; tiles keep a small gap,
                // and the trailing item hugs the edge so the dock just wraps its content.
                var right = i == lastIndex || control is DockActionButton ? 0 : ItemSpacing;
                control.Margin = new Padding(0, 0, right, 0);
            }
        }
    }

    private ShortcutTile CreateTile(ShortcutItem item, int index)
    {
        var tile = new ShortcutTile(item, index)
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
        menu.Items.Add("Add new", null, (_, _) => AddCustomShortcut());
        menu.Items.Add(settings.AutoHide ? "Disable auto-hide" : "Enable auto-hide", null, (_, _) => ToggleAutoHide());
        menu.Items.Add("Reset position", null, (_, _) => ResetPosition());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        DockMenuRenderer.Apply(menu);
        return menu;
    }

    private ContextMenuStrip CreateShortcutMenu(int index)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShortcutResolver.Launch(state.Shortcuts[index]));
        menu.Items.Add("Edit...", null, (_, _) => EditShortcut(index));
        menu.Items.Add("Rename...", null, (_, _) => RenameShortcut(index));
        menu.Items.Add("Move left", null, (_, _) => MoveShortcut(index, -1));
        menu.Items.Add("Move right", null, (_, _) => MoveShortcut(index, 1));
        menu.Items.Add("Remove", null, (_, _) => RemoveShortcut(index));
        DockMenuRenderer.Apply(menu);
        return menu;
    }

    private void ShowDockMenu()
    {
        ContextMenuStrip = CreateDockMenu();
        ContextMenuStrip.Show(menuButton, new Point(0, menuButton.Height + 2));
    }

    private void ShowSettings()
    {
        modalOpen = true;
        try
        {
            using var dialog = new SettingsDialog(settings);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            settings = dialog.Settings;
            store.SaveSettings(settings);
            settingsLastWrite = File.Exists(store.SettingsFilePath) ? File.GetLastWriteTimeUtc(store.SettingsFilePath) : DateTime.MinValue;
            BuildStrip();
            ReseatAgainstEdge();

            if (!settings.AutoHide && isHidden)
            {
                Reveal();
            }
        }
        finally
        {
            modalOpen = false;
            MarkInteraction();
        }
    }

    private void ResizeToContent()
    {
        strip.Location = new Point(Padding.Left, Padding.Top);
        var workingArea = Screen.FromPoint(Location).WorkingArea;

        if (orientation == DockOrientation.Vertical)
        {
            var contentHeight = strip.Controls.Cast<Control>().Sum(control => control.Height + control.Margin.Vertical);
            Width = DockBreadth;
            Height = Math.Min(Math.Max(contentHeight + Padding.Vertical, MinVerticalHeight), Math.Max(workingArea.Height - 24, MinVerticalHeight));
        }
        else
        {
            var contentWidth = strip.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
            Height = DockBreadth;
            Width = Math.Min(Math.Max(contentWidth + Padding.Horizontal, MinHorizontalWidth), Math.Max(workingArea.Width - 24, MinHorizontalWidth));
        }

        strip.Size = new Size(Width - Padding.Horizontal, Height - Padding.Vertical);
        UpdateWindowRegion();
    }

    private void ApplyTheme()
    {
        DockPalette.Refresh();

        BackColor = SystemInformation.HighContrast ? SystemColors.Window : DockPalette.Surface;
        ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.TextPrimary;
        strip.BackColor = BackColor;

        foreach (Control control in strip.Controls)
        {
            control.BackColor = BackColor;
            control.ForeColor = ForeColor;
            control.Invalidate();
        }

        Invalidate();
    }

    private void ApplyGlass()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        // Clean, theme-aligned surface: a solid dock that is dark in dark mode and light
        // in light mode, with Windows 11 immersive chrome and rounded corners. No
        // translucency, so the dock never bleeds the desktop wallpaper through and stays
        // visually calm.
        DockNativeMethods.SetImmersiveDarkMode(Handle, !DockPalette.IsLight);
        DockNativeMethods.SetRoundedCorners(Handle);
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
            ApplyGlass();
        }
    }

    private void OnDockKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyCode == Keys.Insert || (args.Control && args.KeyCode == Keys.N))
        {
            AddCustomShortcut();
            args.Handled = true;
        }
    }

    private void ToggleAutoHide()
    {
        settings.AutoHide = !settings.AutoHide;
        store.SaveSettings(settings);
        ContextMenuStrip = CreateDockMenu();
        if (!settings.AutoHide && isHidden)
        {
            Reveal();
        }

        MarkInteraction();
    }

    private void AddCustomShortcut()
    {
        modalOpen = true;
        try
        {
            var target = InputDialog.ShowDialog(this, "Add new", "File, app, URL, shell target, or command", string.Empty);
            if (target is null)
            {
                return;
            }

            AddShortcut(ShortcutResolver.FromText(target));
        }
        finally
        {
            modalOpen = false;
            MarkInteraction();
        }
    }

    private void EditShortcut(int index)
    {
        modalOpen = true;
        try
        {
            var current = state.Shortcuts[index];
            var target = InputDialog.ShowDialog(this, "Edit shortcut", "File, app, URL, shell target, or command", current.Target);
            if (target is null)
            {
                return;
            }

            // Re-resolve from the new target so the kind, working directory, and icon update.
            // The display name is left as-is (use Rename to change that).
            var resolved = ShortcutResolver.FromText(target);
            current.Target = resolved.Target;
            current.Kind = resolved.Kind;
            current.WorkingDirectory = resolved.WorkingDirectory;
            BuildStrip();
            ReseatAgainstEdge();
        }
        finally
        {
            modalOpen = false;
            MarkInteraction();
        }
    }

    private void RenameShortcut(int index)
    {
        modalOpen = true;
        try
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
        finally
        {
            modalOpen = false;
            MarkInteraction();
        }
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
        ReseatAgainstEdge();
    }

    private void RemoveShortcut(int index)
    {
        state.Shortcuts.RemoveAt(index);
        BuildStrip();
        ReseatAgainstEdge();
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
        ReseatAgainstEdge();
    }

    private void AddShortcut(ShortcutItem item)
    {
        if (state.Shortcuts.Any(existing => string.Equals(existing.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        state.Shortcuts.Add(item);
        BuildStrip();
        ReseatAgainstEdge();
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

        // Make sure the dock is fully on-screen before a drag starts.
        if (isHidden)
        {
            Reveal();
        }

        slideTimer.Stop();
        draggingWindow = true;
        dragMoved = false;
        MarkInteraction();

        // Moving away from a left/right edge returns the dock to its horizontal layout.
        if (orientation == DockOrientation.Vertical)
        {
            orientation = DockOrientation.Horizontal;
            snapEdge = DockSnap.NoEdge;
            BuildStrip(persist: false);
        }

        dragOffset = PointToClient(control.PointToScreen(args.Location));
        dragOffset = new Point(
            Math.Min(dragOffset.X, Math.Max(0, Width - 10)),
            Math.Min(dragOffset.Y, Math.Max(0, Height - 10)));
    }

    private void ContinueWindowDrag(object? sender, MouseEventArgs args)
    {
        if (!draggingWindow)
        {
            return;
        }

        var cursor = Cursor.Position;
        var newLocation = new Point(cursor.X - dragOffset.X, cursor.Y - dragOffset.Y);
        if (newLocation != Location)
        {
            dragMoved = true;
            Location = newLocation;
        }

        MarkInteraction();
    }

    private void EndWindowDrag(object? sender, MouseEventArgs args)
    {
        if (!draggingWindow)
        {
            return;
        }

        draggingWindow = false;

        // A click that did not move the window (e.g. tapping the summary to expand) must
        // not re-snap the dock; only an actual drag re-evaluates the snap edge.
        if (dragMoved)
        {
            SnapToNearestEdge();
        }
    }

    private void SnapToNearestEdge()
    {
        var screen = Screen.FromRectangle(Bounds);
        var area = screen.WorkingArea;

        var dropLocation = Bounds.Location;
        var edge = DockSnap.DetermineEdge(Bounds, area, settings.SnapThreshold, Cursor.Position);

        var desiredOrientation = IsVerticalEdge(edge) ? DockOrientation.Vertical : DockOrientation.Horizontal;
        if (desiredOrientation != orientation)
        {
            orientation = desiredOrientation;
            snapEdge = edge;
            BuildStrip(persist: false);
        }

        snapEdge = edge;
        var placed = DockSnap.PlaceAgainstEdge(Size, dropLocation, area, edge);
        shownBounds = placed;
        Bounds = placed;

        state.MonitorDeviceName = screen.DeviceName;
        state.SnapEdge = edge;
        PersistState();
        MarkInteraction();
    }

    // Re-seats the dock flush against its current edge after a layout/size change.
    private void ReseatAgainstEdge()
    {
        if (snapEdge == DockSnap.NoEdge)
        {
            shownBounds = Bounds;
            return;
        }

        var area = Screen.FromRectangle(Bounds).WorkingArea;
        var placed = DockSnap.PlaceAgainstEdge(Size, shownBounds.Location, area, snapEdge);
        shownBounds = placed;
        if (!isHidden)
        {
            Bounds = placed;
        }
    }

    private void InitializePlacement()
    {
        var screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == state.MonitorDeviceName) ?? Screen.PrimaryScreen!;
        var area = screen.WorkingArea;

        var location = store.HasSavedState
            ? DockSnap.ClampLocation(Size, new Point(state.Left, state.Top), area)
            : DockSnap.DefaultLocation(Size, area);

        Location = location;

        if (snapEdge != DockSnap.NoEdge)
        {
            Bounds = DockSnap.PlaceAgainstEdge(Size, location, area, snapEdge);
        }

        shownBounds = Bounds;
        MarkInteraction();
    }

    private void ResetPosition()
    {
        orientation = DockOrientation.Horizontal;
        snapEdge = DockSnap.NoEdge;
        BuildStrip(persist: false);

        var area = Screen.PrimaryScreen!.WorkingArea;
        isHidden = false;
        TopMost = true;
        Location = DockSnap.DefaultLocation(Size, area);
        SnapToNearestEdge();
    }

    private void PersistState()
    {
        state.Left = isHidden ? shownBounds.Left : Left;
        state.Top = isHidden ? shownBounds.Top : Top;
        state.MonitorDeviceName = Screen.FromPoint(shownBounds.Location).DeviceName;
        state.SnapEdge = snapEdge;
        store.SaveState(state);
    }

    private void MarkInteraction()
    {
        lastInteractionUtc = DateTime.UtcNow;
    }

    private void OnAutoHidePoll()
    {
        if (modalOpen || draggingWindow || (ContextMenuStrip?.Visible ?? false))
        {
            MarkInteraction();
            if (isHidden)
            {
                Reveal();
            }

            return;
        }

        if (ActiveZoneContains(Cursor.Position))
        {
            MarkInteraction();
            if (isHidden)
            {
                Reveal();
            }

            return;
        }

        var snapped = snapEdge != DockSnap.NoEdge;
        if (settings.AutoHide && snapped && !isHidden &&
            (DateTime.UtcNow - lastInteractionUtc).TotalMilliseconds >= Math.Max(200, settings.AutoHideDelayMs))
        {
            HideToEdge();
        }
    }

    private bool ActiveZoneContains(Point point)
    {
        if (isHidden)
        {
            return HiddenRevealZone().Contains(point);
        }

        var zone = shownBounds;
        zone.Inflate(2, 2);
        return zone.Contains(point);
    }

    private Rectangle HiddenRevealZone()
    {
        var area = Screen.FromRectangle(shownBounds).WorkingArea;
        var hot = RevealStripPx + 4;

        return snapEdge switch
        {
            DockSnap.LeftEdge => new Rectangle(area.Left, shownBounds.Top, hot, shownBounds.Height),
            DockSnap.RightEdge => new Rectangle(area.Right - hot, shownBounds.Top, hot, shownBounds.Height),
            DockSnap.TopEdge => new Rectangle(shownBounds.Left, area.Top, shownBounds.Width, hot),
            DockSnap.BottomEdge => new Rectangle(shownBounds.Left, area.Bottom - hot, shownBounds.Width, hot),
            _ => Rectangle.Empty,
        };
    }

    private Point HiddenLocation()
    {
        return snapEdge switch
        {
            DockSnap.LeftEdge => new Point(shownBounds.Left - (Width - RevealStripPx), shownBounds.Top),
            DockSnap.RightEdge => new Point(shownBounds.Left + (Width - RevealStripPx), shownBounds.Top),
            DockSnap.TopEdge => new Point(shownBounds.Left, shownBounds.Top - (Height - RevealStripPx)),
            DockSnap.BottomEdge => new Point(shownBounds.Left, shownBounds.Top + (Height - RevealStripPx)),
            _ => shownBounds.Location,
        };
    }

    private void HideToEdge()
    {
        if (snapEdge == DockSnap.NoEdge)
        {
            return;
        }

        isHidden = true;

        // When hiding against the bottom edge, the dock body slides down into the taskbar's
        // band. Drop out of the always-on-top z-order so the taskbar (itself a topmost
        // window) renders in front of it — the dock tucks behind the taskbar instead of
        // covering it, leaving only the reveal notch peeking above the taskbar.
        if (snapEdge == DockSnap.BottomEdge)
        {
            TopMost = false;
        }

        Invalidate();
        StartSlide(HiddenLocation());
    }

    private void Reveal()
    {
        isHidden = false;
        TopMost = true;
        Invalidate();
        StartSlide(shownBounds.Location);
        MarkInteraction();
    }

    private void StartSlide(Point target)
    {
        slideTarget = target;
        if (!slideTimer.Enabled)
        {
            slideTimer.Start();
        }
    }

    private void OnSlideTick()
    {
        var current = Location;
        var dx = slideTarget.X - current.X;
        var dy = slideTarget.Y - current.Y;

        if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2)
        {
            Location = slideTarget;
            slideTimer.Stop();
            return;
        }

        Location = new Point(current.X + Step(dx), current.Y + Step(dy));
    }

    private static int Step(int delta)
    {
        var step = (int)(delta * 0.30);
        if (step == 0 && delta != 0)
        {
            step = Math.Sign(delta);
        }

        return step;
    }

    private void RefreshSettingsIfChanged()
    {
        try
        {
            var settingsPath = store.SettingsFilePath;
            var lastWrite = File.Exists(settingsPath) ? File.GetLastWriteTimeUtc(settingsPath) : DateTime.MinValue;
            if (lastWrite == settingsLastWrite)
            {
                return;
            }

            settingsLastWrite = lastWrite;
            settings = store.LoadSettings();
            BuildStrip();
            ReseatAgainstEdge();

            if (!settings.AutoHide && isHidden)
            {
                Reveal();
            }
        }
        catch
        {
        }
    }
}
