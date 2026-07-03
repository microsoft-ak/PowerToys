// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuickAccessDock;

internal sealed partial class DockForm : Form
{
    private void AttachWindowDrag(Control control)
    {
        control.MouseDown += BeginWindowDrag;
        control.MouseMove += ContinueWindowDrag;
        control.MouseUp += EndWindowDrag;
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
        args.Effect = args.Data?.GetDataPresent(typeof(string)) == true ? DragDropEffects.Move : DragDropEffects.None;
    }

    private static bool HasShortcutData(DragEventArgs args)
    {
        return args.Data?.GetDataPresent(DataFormats.FileDrop) == true ||
               args.Data?.GetDataPresent(DataFormats.Text) == true;
    }

    private void ReorderShortcutFromDrop(DragEventArgs args, int targetIndex)
    {
        if (args.Data?.GetDataPresent(typeof(string)) != true)
        {
            return;
        }

        var sourceId = (string)args.Data.GetData(typeof(string))!;
        var sourceIndex = state.Shortcuts.FindIndex(s => s.Id == sourceId);
        if (sourceIndex < 0 || sourceIndex == targetIndex)
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
}

