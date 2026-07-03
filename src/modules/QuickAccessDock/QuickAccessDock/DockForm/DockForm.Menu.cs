// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QuickAccessDock;

internal sealed partial class DockForm : Form
{
    private static ToolStripMenuItem CreateMenuItem(string text, DockMenuIcon icon, EventHandler onClick)
    {
        return new ToolStripMenuItem(text, DockMenuIcons.Create(icon), onClick)
        {
            ImageScaling = ToolStripItemImageScaling.None,
        };
    }

    private ContextMenuStrip CreateDockMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateMenuItem("Add new", DockMenuIcon.Add, (_, _) => AddCustomShortcut()));
        menu.Items.Add(CreateMenuItem(
            settings.AutoHide ? "Disable auto-hide" : "Enable auto-hide",
            settings.AutoHide ? DockMenuIcon.AutoHideOff : DockMenuIcon.AutoHide,
            (_, _) => ToggleAutoHide()));
        menu.Items.Add(CreateMenuItem("Reset position", DockMenuIcon.Reset, (_, _) => ResetPosition()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("Quit", DockMenuIcon.Quit, (_, _) => Quit()));
        DockMenuRenderer.Apply(menu);
        return menu;
    }

    private ContextMenuStrip CreateShortcutMenu(int index)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateMenuItem("Open", DockMenuIcon.Open, (_, _) => LaunchShortcut(state.Shortcuts[index])));
        menu.Items.Add(CreateMenuItem("Edit...", DockMenuIcon.Edit, (_, _) => EditShortcut(index)));
        menu.Items.Add(CreateMenuItem("Rename...", DockMenuIcon.Rename, (_, _) => RenameShortcut(index)));
        menu.Items.Add(CreateMenuItem("Move left", DockMenuIcon.MoveLeft, (_, _) => MoveShortcut(index, -1)));
        menu.Items.Add(CreateMenuItem("Move right", DockMenuIcon.MoveRight, (_, _) => MoveShortcut(index, 1)));
        menu.Items.Add(CreateMenuItem("Remove", DockMenuIcon.Remove, (_, _) => RemoveShortcut(index)));
        DockMenuRenderer.Apply(menu);
        return menu;
    }

    private void SetDockContextMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        strip.ContextMenuStrip = menu;
        dragHandle.ContextMenuStrip = menu;
        menuButton.ContextMenuStrip = menu;
    }

    private void ShowDockMenu()
    {
        var menu = CreateDockMenu();
        SetDockContextMenu(menu);
        menu.Show(menuButton, new Point(0, menuButton.Height + 2));
    }

    private void ToggleAutoHide()
    {
        settings.AutoHide = !settings.AutoHide;
        store.SaveSettings(settings);
        SetDockContextMenu(CreateDockMenu());
        if (!settings.AutoHide && isHidden)
        {
            Reveal();
        }

        MarkInteraction();
    }

    private void Quit()
    {
        Close();
    }

    private async void LaunchShortcut(ShortcutItem item)
    {
        ShortcutResolver.Launch(item);

        if (!settings.SyncWebsiteIconsAfterOpen || !ShortcutIconSync.CanSync(item))
        {
            return;
        }

        var iconChanged = await ShortcutIconSync.TrySyncWebsiteIconAsync(item, store.IconCacheFolder);
        if (!iconChanged || IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                BuildStrip();
                PersistState();
            }));
            return;
        }

        BuildStrip();
        PersistState();
    }

    private void AddCustomShortcut()
    {
        modalOpen = true;
        try
        {
            var result = InputDialog.ShowAddDialog(this, "Add new", "File, app, URL, shell target, or command", "Title (optional)");
            if (result is null)
            {
                return;
            }

            var (title, target) = result.Value;
            var shortcut = ShortcutResolver.FromText(target);
            if (!string.IsNullOrWhiteSpace(title))
            {
                shortcut.Name = title;
            }

            AddShortcut(shortcut);
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
}

