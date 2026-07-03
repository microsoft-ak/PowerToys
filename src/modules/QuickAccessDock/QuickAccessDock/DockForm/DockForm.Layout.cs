// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace QuickAccessDock;

internal sealed partial class DockForm : Form
{
    private static bool IsVerticalEdge(string edge) => edge is DockSnap.LeftEdge or DockSnap.RightEdge;

    private void DrawNotch(Graphics graphics)
    {
        if (snapEdge == DockSnap.NoEdge)
        {
            return;
        }

        // The notch sits on the inward-facing side of the dock (the sliver that stays
        // on-screen when auto-hidden), so it doubles as the reveal grip.
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

    private void BuildStrip(bool persist = true)
    {
        strip.SuspendLayout();
        strip.Controls.Clear();

        // Order: the move handle leads, then the shortcuts, with the overflow (...) menu
        // on the trailing edge. When there are no shortcuts, an add affordance stands in
        // for them: the "Add new" text hint when horizontal, or a compact "+" button when
        // vertical (where rotated text reads poorly).
        strip.Controls.Add(dragHandle);

        if (state.Shortcuts.Count > 0)
        {
            for (var index = 0; index < state.Shortcuts.Count; index++)
            {
                strip.Controls.Add(CreateTile(state.Shortcuts[index], index));
            }
        }
        else if (orientation == DockOrientation.Vertical)
        {
            strip.Controls.Add(addButton);
        }
        else
        {
            strip.Controls.Add(emptyHint);
        }

        strip.Controls.Add(menuButton);

        ApplyOrientationLayout();
        strip.ResumeLayout();
        ResizeToContent();
        ApplyTheme();
        SetDockContextMenu(CreateDockMenu());

        if (persist)
        {
            PersistState();
        }
    }

    private void ApplyOrientationLayout()
    {
        var innerBreadth = DockBreadth - Padding.Horizontal;
        var lastIndex = strip.Controls.Count - 1;

        dragHandle.DockOrientation = orientation;
        emptyHint.DockOrientation = orientation;

        if (orientation == DockOrientation.Vertical)
        {
            strip.FlowDirection = FlowDirection.TopDown;

            // Thin leading handle + ellipsis and short tiles, with no margin between any
            // items, so the column stacks tightly with no gaps.
            dragHandle.Size = new Size(38, HandleThickness);
            menuButton.Size = new Size(38, EllipsisThickness);

            // The empty-state "+" stands in for a shortcut, so give it a shortcut tile's
            // footprint rather than the compact ellipsis height — otherwise it looks cramped.
            addButton.Size = new Size(38, VerticalTileHeight);

            // The hint's text is rotated 90° in a vertical dock, so its column height is
            // the horizontal text extent.
            emptyHint.Size = new Size(38, emptyHint.PreferredHorizontalWidth);

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
            dragHandle.Size = new Size(HandleThickness, 40);
            menuButton.Size = new Size(EllipsisThickness, 40);
            addButton.Size = new Size(EllipsisThickness, 40);
            emptyHint.Size = new Size(emptyHint.PreferredHorizontalWidth, 40);

            for (var i = 0; i < strip.Controls.Count; i++)
            {
                var control = strip.Controls[i];

                // The handle, action buttons, and trailing item sit flush; shortcut tiles
                // and the empty hint keep a small gap so the dock wraps content snugly.
                var right = i == lastIndex || control is DockActionButton or DockDragHandle ? 0 : ItemSpacing;
                control.Margin = new Padding(0, 0, right, 0);
            }

            // Inset the trailing ellipsis from the right border so its dots aren't flush
            // against the dock edge.
            menuButton.Margin = new Padding(0, 0, EllipsisEdgeGap, 0);
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
                tile.DoDragDrop(item.Id, DragDropEffects.Move);
            }
        };
        tile.Click += (_, _) => LaunchShortcut(item);
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
        DockPalette.Refresh(settings.Theme, settings.Style, DockPalette.ParseAccentColor(settings.AccentColor));
        glassMode = ResolveGlassMode();

        // On the glass path the form, strip, and every tile carry the glass sentinel as
        // their BackColor; their paint code (via DockDrawing.ClearSurface) then writes
        // true alpha-0 pixels so the DWM system-backdrop acrylic shows through the whole
        // bar. No TransparencyKey: a chroma key would punch real pass-through holes and
        // defeat the backdrop. Otherwise the surface (or high-contrast color) is opaque.
        var backColor = SystemInformation.HighContrast ? SystemColors.Window
            : glassMode == GlassRenderMode.Glass ? DockPalette.GlassKey
            : DockPalette.Surface;

        BackColor = backColor;
        TransparencyKey = Color.Empty;
        ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : DockPalette.TextPrimary;
        strip.BackColor = backColor;

        foreach (Control control in strip.Controls)
        {
            control.BackColor = backColor;
            control.ForeColor = ForeColor;
            control.Invalidate();
        }

        Invalidate();
    }

    private GlassRenderMode ResolveGlassMode()
    {
        if (!DockPalette.IsAcrylic || SystemInformation.HighContrast)
        {
            return GlassRenderMode.Off;
        }

        // Where the acrylic blur-behind material is unavailable (pre-Win10 1803), the
        // acrylic style falls back to an opaque frosted surface.
        if (acrylicBackdropFailed || !DockNativeMethods.SupportsAcrylicBlur())
        {
            return GlassRenderMode.Solid;
        }

        return GlassRenderMode.Glass;
    }

    private void ApplyGlass()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        // Windows 11 immersive chrome and rounded corners for every style.
        DockNativeMethods.SetImmersiveDarkMode(Handle, !DockPalette.IsLight);
        DockNativeMethods.SetRoundedCorners(Handle);

        if (glassMode == GlassRenderMode.Glass)
        {
            if (DockNativeMethods.TryEnableTintedAcrylic(Handle, !DockPalette.IsLight, DockPalette.GlassTint))
            {
                return;
            }

            acrylicBackdropFailed = true;
            DockNativeMethods.DisableTintedAcrylic(Handle);
            ApplyTheme();
            return;
        }

        // Default / Neomorphism / solid-fallback acrylic: ensure no backdrop lingers.
        DockNativeMethods.DisableTintedAcrylic(Handle);
    }

    private void UpdateWindowRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = DockDrawing.CreateRoundedRectanglePath(new Rectangle(0, 0, Width, Height), CornerRadius);
        var region = new Region(path);

        if (isHidden && snapEdge != DockSnap.NoEdge)
        {
            // While hidden, the window's bounds extend past its own monitor's working
            // area — into the taskbar's band, or, when two monitors sit edge-to-edge,
            // into the neighboring monitor — so only a thin reveal notch should actually
            // be visible. Relying on z-order against the taskbar (or whatever sits on
            // the next monitor) is unreliable across monitors, so clip the window's own
            // region to its snapped monitor's working area instead: that guarantees only
            // the notch ever paints, no matter what's behind it. The clip is anchored to
            // the stable pre-hide shownBounds rather than the live (already-slid) Bounds,
            // so Screen.FromRectangle resolves the correct, original monitor.
            var workingArea = Screen.FromRectangle(shownBounds).WorkingArea;
            var localClip = new Rectangle(
                workingArea.Left - Left,
                workingArea.Top - Top,
                workingArea.Width,
                workingArea.Height);
            region.Intersect(localClip);
        }

        var previousRegion = Region;
        Region = region;
        previousRegion?.Dispose();
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
        EnsureVisibleOnStartup(area);
    }

    private void EnsureVisibleOnStartup(Rectangle workingArea)
    {
        // Saved state can become stale after monitor, DPI, or taskbar changes. If the
        // dock is no longer visible, pull it back into the current working area.
        var visibleLocation = DockSnap.ClampLocation(Size, Location, workingArea);
        var visibleBounds = new Rectangle(visibleLocation, Size);

        if (!workingArea.IntersectsWith(visibleBounds))
        {
            visibleLocation = DockSnap.DefaultLocation(Size, workingArea);
            visibleBounds = new Rectangle(visibleLocation, Size);
        }

        if (Location != visibleLocation)
        {
            isHidden = false;
            snapEdge = DockSnap.NoEdge;
            Location = visibleLocation;
            Bounds = visibleBounds;
            shownBounds = visibleBounds;
            state.Left = visibleLocation.X;
            state.Top = visibleLocation.Y;
            state.MonitorDeviceName = Screen.FromPoint(visibleLocation).DeviceName;
            state.SnapEdge = DockSnap.NoEdge;
            PersistState();
        }
    }
}

