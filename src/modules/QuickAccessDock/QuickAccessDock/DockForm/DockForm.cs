// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickAccessDock;

internal sealed partial class DockForm : Form
{
    // The "breadth" is the dock's thickness: its height when horizontal, its width when vertical.
    private const int DockBreadth = 54;

    // Minimums: just wide/tall enough for the handle + menu button with padding.
    private const int MinHorizontalWidth = 44;
    private const int MinVerticalHeight = 44;
    private const int CornerRadius = DockDrawing.CornerRadius;

    // Spacing between horizontal shortcut tiles (kept tight).
    private const int ItemSpacing = 2;

    // The overflow button's size along the dock axis (its width when horizontal, its
    // height when vertical). Kept small so the ellipsis carries no extra padding.
    private const int EllipsisThickness = 16;
    private const int HandleThickness = 14;

    // Trailing inset for the ellipsis in a horizontal dock, so the dots aren't flush
    // against the right border.
    private const int EllipsisEdgeGap = 6;

    // Height of a tile in the vertical column. Sized close to the icon so stacked tiles
    // sit right against each other with no gap.
    private const int VerticalTileHeight = 38;

    // Sliver left on-screen when the dock auto-hides against an edge; also the notch grip.
    private const int RevealStripPx = 6;
    private const int NotchThickness = 4;
    private const int NotchLength = 34;

    private readonly DockSettingsStore store;
    private readonly DockStripPanel strip;
    private readonly DockDragHandle dragHandle;
    private readonly DockActionButton menuButton;
    private readonly DockActionButton addButton;
    private readonly DockEmptyHint emptyHint;
    private FileSystemWatcher? settingsWatcher;
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
    private bool firstShown;

    // How the dock body is currently rendered for the Acrylic style: as transparent glass
    // (the acrylic blur-behind material shows through), or as an opaque frosted surface
    // where blur-behind is unavailable. Off for every other style. Resolved by ApplyTheme.
    private GlassRenderMode glassMode = GlassRenderMode.Off;
    private bool acrylicBackdropFailed;

    private enum GlassRenderMode
    {
        /// <summary>Not the acrylic style: paint the normal solid/neomorphic surface.</summary>
        Off,

        /// <summary>Clear the body to the transparency key so tint-controlled acrylic blur shows through.</summary>
        Glass,

        /// <summary>Acrylic style on a build without blur-behind: opaque frosted fallback.</summary>
        Solid,
    }

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
        Text = "Quick Access Dock";
        AccessibleName = "Quick Access Dock";
        AccessibleDescription = "QuickAccess Floating always-on-top shortcut dock";
        AccessibleRole = AccessibleRole.ToolBar;
        Padding = new Padding(6, 6, 6, 6);
        StartPosition = FormStartPosition.Manual;
        BackColor = DockPalette.Surface;

        strip = new DockStripPanel
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

        dragHandle = new DockDragHandle
        {
            AllowDrop = true,
        };

        menuButton = new DockActionButton
        {
            Glyph = DockActionGlyph.Ellipsis,
            AllowDrop = true,
        };
        menuButton.Click += (_, _) => ShowDockMenu();

        addButton = new DockActionButton
        {
            Glyph = DockActionGlyph.Add,
            AllowDrop = true,
            AccessibleName = "Add shortcut",
        };
        addButton.Click += (_, _) => AddCustomShortcut();

        emptyHint = new DockEmptyHint
        {
            AllowDrop = true,
        };
        emptyHint.Click += (_, _) => AddCustomShortcut();

        toolTip.SetToolTip(dragHandle, "Move dock");
        toolTip.SetToolTip(menuButton, "Dock options");
        toolTip.SetToolTip(addButton, "Add a shortcut");
        toolTip.SetToolTip(emptyHint, "Add a shortcut");

        Controls.Add(strip);
        ApplyTheme();
        BuildStrip(persist: false);
        InitializePlacement();
        PersistState();

        AttachWindowDrag(this);
        AttachWindowDrag(strip);
        AttachWindowDrag(dragHandle);

        KeyDown += OnDockKeyDown;
        DragEnter += OnExternalDragEnter;
        DragDrop += OnExternalDragDrop;
        strip.DragEnter += OnExternalDragEnter;
        strip.DragDrop += OnExternalDragDrop;
        dragHandle.DragEnter += OnExternalDragEnter;
        dragHandle.DragDrop += OnExternalDragDrop;
        menuButton.DragEnter += OnExternalDragEnter;
        menuButton.DragDrop += OnExternalDragDrop;
        addButton.DragEnter += OnExternalDragEnter;
        addButton.DragDrop += OnExternalDragDrop;
        emptyHint.DragEnter += OnExternalDragEnter;
        emptyHint.DragDrop += OnExternalDragDrop;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        SetDockContextMenu(CreateDockMenu());

        try
        {
            Directory.CreateDirectory(store.AppFolder);
            settingsWatcher = new FileSystemWatcher(store.AppFolder, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            settingsWatcher.Changed += (_, _) =>
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(RefreshSettingsIfChanged));
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickAccessDock] Failed to start settings watcher: {ex}");
        }

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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (firstShown)
        {
            return;
        }

        firstShown = true;
        isHidden = false;
        TopMost = true;
        ShowInTaskbar = false;
        BringToFront();
        Activate();

        // The constructor's first layout runs before the window is realized and before
        // per-monitor DPI scaling settles, which can leave the dock slightly wider than
        // its content (a trailing gap past the ellipsis). Re-run the layout now — before
        // revealing — so the first-shown dock hugs its items exactly like every later
        // rebuild does, and the reveal slide targets the corrected bounds.
        BuildStrip(persist: false);
        ReseatAgainstEdge();

        Reveal();
        MarkInteraction();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        PersistState();
        settingsWatcher?.Dispose();
        autoHideTimer.Stop();
        slideTimer.Stop();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        KeyDown -= OnDockKeyDown;
        DragEnter -= OnExternalDragEnter;
        DragDrop -= OnExternalDragDrop;
        strip.DragEnter -= OnExternalDragEnter;
        strip.DragDrop -= OnExternalDragDrop;
        dragHandle.DragEnter -= OnExternalDragEnter;
        dragHandle.DragDrop -= OnExternalDragDrop;
        menuButton.DragEnter -= OnExternalDragEnter;
        menuButton.DragDrop -= OnExternalDragDrop;
        addButton.DragEnter -= OnExternalDragEnter;
        addButton.DragDrop -= OnExternalDragDrop;
        emptyHint.DragEnter -= OnExternalDragEnter;
        emptyHint.DragDrop -= OnExternalDragDrop;
        toolTip.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        if (SystemInformation.HighContrast)
        {
            PaintSolidSurface(e.Graphics, bounds, SystemColors.Window, SystemColors.ControlText);
        }
        else if (glassMode == GlassRenderMode.Glass)
        {
            PaintGlassSurface(e.Graphics, bounds);
        }
        else if (DockPalette.IsNeomorphism)
        {
            PaintNeomorphicSurface(e.Graphics, bounds);
        }
        else
        {
            // Covers the Default style and the Acrylic style's opaque frosted fallback.
            PaintSolidSurface(e.Graphics, bounds, DockPalette.Surface, DockPalette.Border);
        }

        DrawNotch(e.Graphics);
    }

    // Acrylic: clear the body to true transparent (alpha-0) pixels so the tint-controlled
    // acrylic blur-behind material shows through, then trace a soft edge on top so the glass
    // bar still reads against any wallpaper. Every child control clears the same way, so
    // the frosted material is uninterrupted.
    private void PaintGlassSurface(Graphics graphics, Rectangle bounds)
    {
        DockDrawing.ClearSurface(graphics, DockPalette.GlassKey);

        using var path = DockDrawing.CreateRoundedRectanglePath(bounds, CornerRadius);
        using var border = new Pen(DockPalette.Border, 1.0f);
        graphics.DrawPath(border, path);
    }

    private void PaintSolidSurface(Graphics graphics, Rectangle bounds, Color fillColor, Color borderColor)
    {
        using var path = DockDrawing.CreateRoundedRectanglePath(bounds, CornerRadius);
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(borderColor, 1.0f);

        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
    }

    private void PaintNeomorphicSurface(Graphics graphics, Rectangle bounds)
    {
        // The dock body is itself a softly extruded slab: a matte fill with a gentle
        // light edge top-left and dark edge bottom-right. Kept subtle (the tiles inside
        // carry the stronger relief) so the bar stays calm.
        DockDrawing.PaintNeomorphicSurface(
            graphics,
            bounds,
            CornerRadius,
            DockPalette.Surface,
            DockPalette.ShadowLight,
            DockPalette.ShadowDark,
            inset: false,
            intensity: 0.55f);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateWindowRegion();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            // Rebuild the strip so context menu icons regenerate with the new palette colors.
            BuildStrip(persist: false);
            ApplyGlass();
            ReseatAgainstEdge();
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

    private void PersistState()
    {
        state.Left = isHidden ? shownBounds.Left : Left;
        state.Top = isHidden ? shownBounds.Top : Top;
        state.MonitorDeviceName = Screen.FromPoint(shownBounds.Location).DeviceName;
        state.SnapEdge = snapEdge;
        store.SaveState(state);
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
            ApplyGlass();
            ReseatAgainstEdge();

            if (!settings.AutoHide && isHidden)
            {
                Reveal();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickAccessDock] RefreshSettingsIfChanged failed: {ex}");
        }
    }
}
