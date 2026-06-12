// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using ManagedCommon;
using Microsoft.CmdPal.Ext.Bookmarks;
using Microsoft.CmdPal.UI.Messages;
using Microsoft.CmdPal.UI.ViewModels;
using Microsoft.CmdPal.UI.ViewModels.Dock;
using Microsoft.CmdPal.UI.ViewModels.Messages;
using Microsoft.CmdPal.UI.ViewModels.Settings;
using Microsoft.CommandPalette.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

using RS_ = Microsoft.CmdPal.UI.Helpers.ResourceLoaderInstance;

namespace Microsoft.CmdPal.UI.Dock;

public sealed partial class DockControl : UserControl, IRecipient<CloseContextMenuMessage>, IRecipient<EnterDockEditModeMessage>, IRecipient<ExitDockEditModeMessage>, IRecipient<CrossMonitorBandDropMessage>
{
    private DockViewModel _viewModel;

    internal DockViewModel ViewModel => _viewModel;

    /// <summary>
    /// Gets or sets the HWND of the parent DockWindow that owns this control.
    /// Used to target palette-show messages to the correct DockWindow in multi-monitor setups.
    /// </summary>
    internal IntPtr OwnerHwnd { get; set; }

    public static readonly DependencyProperty ItemsOrientationProperty =
        DependencyProperty.Register(nameof(ItemsOrientation), typeof(Orientation), typeof(DockControl), new PropertyMetadata(Orientation.Horizontal));

    public Orientation ItemsOrientation
    {
        get => (Orientation)GetValue(ItemsOrientationProperty);
        set => SetValue(ItemsOrientationProperty, value);
    }

    public static readonly DependencyProperty DockSideProperty =
        DependencyProperty.Register(nameof(DockSide), typeof(DockSide), typeof(DockControl), new PropertyMetadata(DockSide.Top));

    public DockSide DockSide
    {
        get => (DockSide)GetValue(DockSideProperty);
        set => SetValue(DockSideProperty, value);
    }

    public static readonly DependencyProperty DockSizeProperty =
        DependencyProperty.Register(nameof(DockSize), typeof(DockSize), typeof(DockControl), new PropertyMetadata(DockSize.Default));

    public DockSize DockSize
    {
        get => (DockSize)GetValue(DockSizeProperty);
        set => SetValue(DockSizeProperty, value);
    }

    public static readonly DependencyProperty IsFloatingProperty =
        DependencyProperty.Register(nameof(IsFloating), typeof(bool), typeof(DockControl), new PropertyMetadata(false));

    /// <summary>
    /// Gets or sets a value indicating whether the dock is in fit-to-content
    /// (compact toolbar) mode rather than spanning the full screen edge.
    /// </summary>
    public bool IsFloating
    {
        get => (bool)GetValue(IsFloatingProperty);
        set => SetValue(IsFloatingProperty, value);
    }

    /// <summary>
    /// Raised when the user starts dragging the resize grip (fit-to-content mode).
    /// </summary>
    internal event EventHandler? ResizeDragStarted;

    /// <summary>
    /// Raised on every pointer move while the resize grip is being dragged.
    /// The owning window reads the cursor position itself, so no delta payload.
    /// </summary>
    internal event EventHandler? ResizeDragDelta;

    /// <summary>
    /// Raised when a resize-grip drag finishes (pointer released or capture lost).
    /// </summary>
    internal event EventHandler? ResizeDragCompleted;

    /// <summary>
    /// Raised when the user double-taps the resize grip to return to automatic sizing.
    /// </summary>
    internal event EventHandler? ResizeDragReset;

    /// <summary>
    /// Raised when the dock content's natural size may have changed (bands
    /// resized, edit mode toggled), so a fit-to-content window can re-fit.
    /// </summary>
    internal event EventHandler? ContentLayoutChanged;

    public static readonly DependencyProperty IsEditModeProperty =
        DependencyProperty.Register(nameof(IsEditMode), typeof(bool), typeof(DockControl), new PropertyMetadata(false, OnIsEditModeChanged));

    public bool IsEditMode
    {
        get => (bool)GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

    private static void OnIsEditModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockControl control && e.NewValue is bool isEditMode)
        {
            control.UpdateEditMode(isEditMode);
        }
    }

    internal DockControl(DockViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        Loaded += DockControl_Loaded;
        Unloaded += DockControl_Unloaded;

        // Start with edit mode disabled - normal click behavior
        UpdateEditMode(false);
    }

    private void DockControl_Loaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<CloseContextMenuMessage>(this);
        WeakReferenceMessenger.Default.Register<EnterDockEditModeMessage>(this);
        WeakReferenceMessenger.Default.Register<ExitDockEditModeMessage>(this);
        WeakReferenceMessenger.Default.Register<CrossMonitorBandDropMessage>(this);

        ContextControl.ViewModel.CommandInvoked -= ContextMenu_CommandInvoked;
        ContextControl.ViewModel.CommandInvoked += ContextMenu_CommandInvoked;
        ContextControl.ViewModel.CommandInvoking -= ContextMenu_CommandInvoking;
        ContextControl.ViewModel.CommandInvoking += ContextMenu_CommandInvoking;

        ViewModel.CenterItems.CollectionChanged -= CenterItems_CollectionChanged;
        ViewModel.CenterItems.CollectionChanged += CenterItems_CollectionChanged;

        UpdateEditModeTeachingTip();
    }

    private void DockControl_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        ContextControl.ViewModel.CommandInvoked -= ContextMenu_CommandInvoked;
        ContextControl.ViewModel.CommandInvoking -= ContextMenu_CommandInvoking;

        ViewModel.CenterItems.CollectionChanged -= CenterItems_CollectionChanged;

        if (EditButtonsTeachingTip.IsOpen)
        {
            EditButtonsTeachingTip.IsOpen = false;
        }

        if (ContextMenuFlyout.IsOpen)
        {
            ContextMenuFlyout.Hide();
        }

        if (AddBandFlyout.IsOpen)
        {
            AddBandFlyout.Hide();
        }

        if (EditModeContextMenu.IsOpen)
        {
            EditModeContextMenu.Hide();
        }
    }

    private void CenterItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateCenterVisibility();
    }

    private void UpdateCenterVisibility()
    {
        ContentGrid.IsCenterVisible = IsEditMode || ViewModel.CenterItems.Count > 0;
    }

    public void Receive(EnterDockEditModeMessage message)
    {
        // Message may arrive from a background thread, dispatch to UI thread
        DispatcherQueue.TryEnqueue(() =>
        {
            EnterEditMode();
        });
    }

    public void Receive(ExitDockEditModeMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (message.Discard)
            {
                DiscardEditMode();
            }
            else
            {
                ExitEditMode();
            }
        });
    }

    private void UpdateEditMode(bool isEditMode)
    {
        // Update center visibility based on edit mode and center items
        UpdateCenterVisibility();

        // Enable/disable drag-and-drop based on edit mode
        StartListView.CanDragItems = isEditMode;
        StartListView.CanReorderItems = isEditMode;
        StartListView.AllowDrop = isEditMode;

        CenterListView.CanDragItems = isEditMode;
        CenterListView.CanReorderItems = isEditMode;
        CenterListView.AllowDrop = isEditMode;

        EndListView.CanDragItems = isEditMode;
        EndListView.CanReorderItems = isEditMode;
        EndListView.AllowDrop = isEditMode;

        if (isEditMode)
        {
            EditButtonsTeachingTip.PreferredPlacement = DockSide switch
            {
                DockSide.Left => TeachingTipPlacementMode.Right,
                DockSide.Right => TeachingTipPlacementMode.Left,
                DockSide.Top => TeachingTipPlacementMode.Bottom,
                DockSide.Bottom => TeachingTipPlacementMode.Top,
                _ => TeachingTipPlacementMode.Auto,
            };
        }

        UpdateEditModeTeachingTip();

        // Edit mode shows/hides the add-band buttons, changing the dock's
        // natural length — let a fit-to-content window re-fit.
        ContentLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEditModeTeachingTip()
    {
        if (XamlRoot is null || ContentGrid.XamlRoot is null || EditButtonsTeachingTip.Parent is null)
        {
            return;
        }

        if (!IsEditMode)
        {
            if (EditButtonsTeachingTip.IsOpen)
            {
                EditButtonsTeachingTip.IsOpen = false;
            }

            return;
        }

        if (!EditButtonsTeachingTip.IsOpen)
        {
            EditButtonsTeachingTip.IsOpen = true;
        }
    }

    private static void PreparePopupForShow(FlyoutBase popup, FrameworkElement placementTarget)
    {
        if (placementTarget.XamlRoot is not null && popup.XamlRoot != placementTarget.XamlRoot)
        {
            popup.XamlRoot = placementTarget.XamlRoot;
        }
    }

    internal void EnterEditMode()
    {
        // Snapshot current state so we can restore on discard
        ViewModel.SnapshotBandOrder();
        IsEditMode = true;
    }

    internal void ExitEditMode()
    {
        IsEditMode = false;

        // Save all changes when exiting edit mode
        ViewModel.SaveBandOrder();
    }

    internal void DiscardEditMode()
    {
        IsEditMode = false;

        // Restore the original band order from snapshot
        ViewModel.RestoreBandOrder();
    }

    private void DoneEditingButton_Click(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new ExitDockEditModeMessage(Discard: false));
    }

    private void DiscardEditingButton_Click(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new ExitDockEditModeMessage(Discard: true));
    }

    internal void UpdateSettings(DockSettings settings, DockSide? effectiveSide = null)
    {
        var side = effectiveSide ?? settings.Side;
        DockSide = side;

        // Compact mode is only supported for Top/Bottom positions
        var isHorizontal = side == DockSide.Top || side == DockSide.Bottom;
        var effectiveSize = isHorizontal ? settings.DockSize : DockSize.Default;
        DockSize = effectiveSize;

        ItemsOrientation = isHorizontal ? Orientation.Horizontal : Orientation.Vertical;
        IsFloating = settings.LengthMode == DockLengthMode.FitToContent;
        UpdateResizeGrip(settings, isHorizontal);

        if (settings.Backdrop == DockBackdrop.Transparent)
        {
            RootGrid.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }
    }

    /// <summary>
    /// Places the resize grip on the correct edge for the current orientation and
    /// alignment: the grip sits on the dock's "free" end (the edge that moves when
    /// the dock grows), which for End alignment is the start edge instead.
    /// </summary>
    private void UpdateResizeGrip(DockSettings settings, bool isHorizontal)
    {
        if (!IsFloating)
        {
            ResizeGrip.Visibility = Visibility.Collapsed;
            return;
        }

        ResizeGrip.Visibility = Visibility.Visible;

        var gripAtStart = settings.Alignment == DockAlignment.End;
        if (isHorizontal)
        {
            ResizeGrip.Width = 12;
            ResizeGrip.Height = double.NaN;
            ResizeGrip.HorizontalAlignment = gripAtStart ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            ResizeGrip.VerticalAlignment = VerticalAlignment.Stretch;
            ResizeGripHandle.Width = 3;
            ResizeGripHandle.Height = 20;
        }
        else
        {
            ResizeGrip.Width = double.NaN;
            ResizeGrip.Height = 12;
            ResizeGrip.HorizontalAlignment = HorizontalAlignment.Stretch;
            ResizeGrip.VerticalAlignment = gripAtStart ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            ResizeGripHandle.Width = 20;
            ResizeGripHandle.Height = 3;
        }
    }

    /// <summary>
    /// Measures the natural length (in DIPs) of the dock content along the dock
    /// axis, unconstrained by the current window size. Used by the owning window
    /// in fit-to-content mode to size itself to the content.
    /// </summary>
    internal double MeasureDesiredLength(bool isHorizontal, double thicknessDips)
    {
        var available = isHorizontal
            ? new Windows.Foundation.Size(double.PositiveInfinity, thicknessDips)
            : new Windows.Foundation.Size(thicknessDips, double.PositiveInfinity);

        RootGrid.Measure(available);
        var desired = isHorizontal ? RootGrid.DesiredSize.Width : RootGrid.DesiredSize.Height;

        // Re-run a normal measure pass so the temporary infinite constraint
        // doesn't leave the tree laid out for the wrong size.
        RootGrid.InvalidateMeasure();

        return desired;
    }

    private void BandItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // Ignore clicks when in edit mode - allow drag behavior instead
        if (IsEditMode)
        {
            return;
        }

        if (sender is DockItemControl dockItem && dockItem.DataContext is DockBandViewModel band && dockItem.Tag is DockItemViewModel item)
        {
            // Use the center of the border as the point to open at
            var borderCenter = GetDockItemCenter(dockItem);

            InvokeItem(item, borderCenter);
            e.Handled = true;
        }
    }

    private ContextMenuFilterLocation GetDockContextMenuFilterLocation()
    {
        return DockSide == DockSide.Bottom
            ? ContextMenuFilterLocation.Bottom
            : ContextMenuFilterLocation.Top;
    }

    // Stores the band that was right-clicked for edit mode context menu
    private DockBandViewModel? _editModeContextBand;

    // Position (in window coords) of the dock item whose context menu is currently
    // open, used to anchor the cmdpal palette when a Page command is invoked from
    // the context menu. Null when the open context menu is not anchored to a band.
    private Point? _bandContextMenuPalettePos;

    private void BandItem_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is DockItemControl dockItem && dockItem.DataContext is DockBandViewModel band && dockItem.Tag is DockItemViewModel item)
        {
            // In edit mode, show the edit mode context menu (show/hide labels)
            if (IsEditMode)
            {
                // Find the parent DockBandViewModel for this item
                _editModeContextBand = band;
                if (_editModeContextBand != null)
                {
                    // Update toggle menu item checked state based on current settings
                    ShowTitlesMenuItem.IsChecked = _editModeContextBand.ShowTitles;
                    ShowSubtitlesMenuItem.IsChecked = _editModeContextBand.ShowSubtitles;

                    // Hide subtitle toggle in compact mode — no subtitle in the template
                    ShowSubtitlesMenuItem.Visibility = DockSize == DockSize.Compact
                        ? Visibility.Collapsed
                        : Visibility.Visible;

                    PreparePopupForShow(EditModeContextMenu, dockItem);
                    EditModeContextMenu.ShowAt(
                        dockItem,
                        new FlyoutShowOptions()
                        {
                            ShowMode = FlyoutShowMode.Standard,
                            Placement = FlyoutPlacementMode.TopEdgeAlignedRight,
                        });
                    e.Handled = true;
                }

                return;
            }

            // Normal mode - show the command context menu
            if (item.CanOpenContextMenu)
            {
                // Remember where to anchor the palette if the user picks a Page
                // command from the context menu.
                _bandContextMenuPalettePos = GetDockItemCenter(dockItem);

                ContextControl.ViewModel.SelectedItem = item;
                ContextControl.ShowFilterBox = true;
                ContextControl.PrepareForOpen(GetDockContextMenuFilterLocation());
                PreparePopupForShow(ContextMenuFlyout, dockItem);
                ContextMenuFlyout.ShowAt(
                    dockItem,
                    new FlyoutShowOptions()
                    {
                        ShowMode = FlyoutShowMode.Standard,
                        Placement = FlyoutPlacementMode.TopEdgeAlignedRight,
                    });
                e.Handled = true;
            }
        }
    }

    private void ShowTitlesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_editModeContextBand != null)
        {
            _editModeContextBand.ShowTitles = ShowTitlesMenuItem.IsChecked;
        }
    }

    private void ShowSubtitlesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_editModeContextBand != null)
        {
            _editModeContextBand.ShowSubtitles = ShowSubtitlesMenuItem.IsChecked;
        }
    }

    private void UnpinBandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_editModeContextBand != null)
        {
            ViewModel.UnpinBand(_editModeContextBand);
            _editModeContextBand = null;
        }
    }

    private void InvokeItem(DockItemViewModel item, Point pos)
    {
        var command = item.Command;
        var hwnd = OwnerHwnd;
        try
        {
            PerformCommandMessage m = new(command.Model)
            {
                WithAnimation = false,
                TransientPage = true,

                // If the command is invokable and its result asks for a
                // confirmation dialog, surface the cmdpal window anchored at
                // this dock item before the dialog appears.
                OnBeforeShowConfirmation = () =>
                    WeakReferenceMessenger.Default.Send<RequestShowPaletteAtMessage>(new(pos, hwnd)),
            };
            WeakReferenceMessenger.Default.Send(m);

            if (IsPageCommand(command.Model.Unsafe))
            {
                WeakReferenceMessenger.Default.Send<RequestShowPaletteAtMessage>(new(pos, hwnd));
            }
        }
        catch (COMException e)
        {
            Logger.LogError("Error invoking dock command", e);
        }
    }

    private static bool IsPageCommand(ICommand? command)
    {
        // A Page command is one that's not directly invokable - selecting it
        // navigates into a page rather than performing an action in place.
        return command is not null and not IInvokableCommand;
    }

    private static Point GetDockItemCenter(FrameworkElement dockItem)
    {
        var borderPos = dockItem.TransformToVisual(null).TransformPoint(new Point(0, 0));
        return new Point(
            borderPos.X + (dockItem.ActualWidth / 2),
            borderPos.Y + (dockItem.ActualHeight / 2));
    }

    private void ContextMenu_CommandInvoked(object? sender, CommandItemViewModel command)
    {
        // The context menu just invoked a command. If it came from a dock band
        // (i.e. _bandContextMenuPalettePos is set) and the command is a Page,
        // open the cmdpal palette anchored at the dock item — mirroring what
        // a direct click on the band does.
        var pos = _bandContextMenuPalettePos;
        _bandContextMenuPalettePos = null;

        if (pos is null)
        {
            return;
        }

        if (IsPageCommand(command.Command.Model.Unsafe))
        {
            WeakReferenceMessenger.Default.Send<RequestShowPaletteAtMessage>(new(pos.Value, OwnerHwnd));
        }
    }

    private void ContextMenu_CommandInvoking(object? sender, PerformCommandMessage message)
    {
        // The context menu is about to dispatch a command. If it was opened
        // from a dock band, attach a callback so that an invokable command
        // whose result is a Confirm surfaces the cmdpal window anchored at the
        // dock item before the confirmation dialog appears.
        var pos = _bandContextMenuPalettePos;
        if (pos is null)
        {
            return;
        }

        var hwnd = OwnerHwnd;
        var capturedPos = pos.Value;
        message.OnBeforeShowConfirmation = () =>
            WeakReferenceMessenger.Default.Send<RequestShowPaletteAtMessage>(new(capturedPos, hwnd));
    }

    private void ContextMenuFlyout_Opened(object sender, object e)
    {
        // We need to wait until our flyout is opened to try and toss focus
        // at its search box. The control isn't in the UI tree before that
        ContextControl.FocusSearchBox();
    }

    public void Receive(CloseContextMenuMessage message)
    {
        if (ContextMenuFlyout.IsOpen)
        {
            ContextMenuFlyout.Hide();
        }
    }

    private void RootGrid_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        // Don't show the dock context menu while in edit mode
        if (IsEditMode)
        {
            return;
        }

        // This context menu is for the dock itself (not a band), so the palette
        // should not be opened on invocation.
        _bandContextMenuPalettePos = null;

        var pos = e.GetPosition(null);
        var item = this.ViewModel.GetContextMenuForDock();
        if (item.HasMoreCommands)
        {
            ContextControl.ViewModel.SelectedItem = item;
            ContextControl.ShowFilterBox = false;
            ContextControl.PrepareForOpen(GetDockContextMenuFilterLocation());
            PreparePopupForShow(ContextMenuFlyout, RootGrid);
            ContextMenuFlyout.ShowAt(
            this.RootGrid,
            new FlyoutShowOptions()
            {
                ShowMode = FlyoutShowMode.Standard,
                Placement = FlyoutPlacementMode.TopEdgeAlignedRight,
                Position = pos,
            });
            e.Handled = true;
        }
    }

    private DockBandViewModel? _draggedBand;

    private void BandListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count > 0 && e.Items[0] is DockBandViewModel band)
        {
            _draggedBand = band;
            e.Data.RequestedOperation = DataPackageOperation.Move;

            // Only advertise cross-monitor data when we have a real monitor ID.
            // Without one (single-monitor / global dock) the cross-monitor path
            // cannot safely distinguish source from target.
            if (ViewModel.MonitorDeviceId is not null)
            {
                e.Data.Properties["DockBandId"] = band.Id;
                e.Data.Properties["SourceMonitorDeviceId"] = ViewModel.MonitorDeviceId;
            }
        }
    }

    private void BandListView_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedBand != null || e.DataView.Properties.ContainsKey("DockBandId"))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private void BandListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Reordering within the same list is handled automatically by ListView
        // We just need to sync the ViewModel order without saving
        if (args.DropResult == DataPackageOperation.Move && _draggedBand != null)
        {
            DockPinSide targetSide;
            ObservableCollection<DockBandViewModel> targetCollection;

            if (sender == StartListView)
            {
                targetSide = DockPinSide.Start;
                targetCollection = ViewModel.StartItems;
            }
            else if (sender == CenterListView)
            {
                targetSide = DockPinSide.Center;
                targetCollection = ViewModel.CenterItems;
            }
            else
            {
                targetSide = DockPinSide.End;
                targetCollection = ViewModel.EndItems;
            }

            // Find the new index and sync ViewModel (without saving)
            var newIndex = targetCollection.IndexOf(_draggedBand);
            if (newIndex >= 0)
            {
                ViewModel.SyncBandPosition(_draggedBand, targetSide, newIndex);
            }
        }

        _draggedBand = null;
    }

    private void StartListView_Drop(object sender, DragEventArgs e)
    {
        HandleCrossListDrop(DockPinSide.Start, e);
        ResetListViewState(sender);
    }

    private void CenterListView_Drop(object sender, DragEventArgs e)
    {
        HandleCrossListDrop(DockPinSide.Center, e);
        ResetListViewState(sender);
    }

    private void EndListView_Drop(object sender, DragEventArgs e)
    {
        HandleCrossListDrop(DockPinSide.End, e);
        ResetListViewState(sender);
    }

    private void HandleCrossListDrop(DockPinSide targetSide, DragEventArgs e)
    {
        if (_draggedBand != null)
        {
            HandleLocalCrossListDrop(targetSide, e);
            return;
        }

        // Cross-monitor drag from another DockControl
        if (e.DataView.Properties.TryGetValue("DockBandId", out var bandIdObj) &&
            e.DataView.Properties.TryGetValue("SourceMonitorDeviceId", out var sourceMonitorObj) &&
            bandIdObj is string bandId &&
            sourceMonitorObj is string sourceMonitorDeviceId)
        {
            HandleCrossMonitorDrop(bandId, sourceMonitorDeviceId, targetSide, e);
        }
    }

    private void HandleLocalCrossListDrop(DockPinSide targetSide, DragEventArgs e)
    {
        // Check which list the band is currently in
        var isInStart = ViewModel.StartItems.Contains(_draggedBand!);
        var isInCenter = ViewModel.CenterItems.Contains(_draggedBand!);

        DockPinSide sourceSide;
        if (isInStart)
        {
            sourceSide = DockPinSide.Start;
        }
        else if (isInCenter)
        {
            sourceSide = DockPinSide.Center;
        }
        else
        {
            sourceSide = DockPinSide.End;
        }

        // Only handle cross-list drops here; same-list reorders are handled in DragItemsCompleted
        if (sourceSide != targetSide)
        {
            var targetListView = targetSide switch
            {
                DockPinSide.Start => StartListView,
                DockPinSide.Center => CenterListView,
                _ => EndListView,
            };
            var targetCollection = targetSide switch
            {
                DockPinSide.Start => ViewModel.StartItems,
                DockPinSide.Center => ViewModel.CenterItems,
                _ => ViewModel.EndItems,
            };

            var dropIndex = GetDropIndex(targetListView, e, targetCollection.Count);

            // Move the band to the new side (without saving - save happens on Done)
            ViewModel.MoveBandWithoutSaving(_draggedBand!, targetSide, dropIndex);
            e.Handled = true;
        }
    }

    private void HandleCrossMonitorDrop(string bandId, string sourceMonitorDeviceId, DockPinSide targetSide, DragEventArgs e)
    {
        var targetListView = targetSide switch
        {
            DockPinSide.Start => StartListView,
            DockPinSide.Center => CenterListView,
            _ => EndListView,
        };
        var targetCollection = targetSide switch
        {
            DockPinSide.Start => ViewModel.StartItems,
            DockPinSide.Center => ViewModel.CenterItems,
            _ => ViewModel.EndItems,
        };

        var dropIndex = GetDropIndex(targetListView, e, targetCollection.Count);

        ViewModel.AcceptBandFromMonitor(bandId, targetSide, dropIndex);

        if (!string.IsNullOrEmpty(sourceMonitorDeviceId))
        {
            WeakReferenceMessenger.Default.Send(new CrossMonitorBandDropMessage(bandId, sourceMonitorDeviceId));
        }

        e.Handled = true;
    }

    private int GetDropIndex(ListView listView, DragEventArgs e, int itemCount)
    {
        var position = e.GetPosition(listView);

        // Find the item at the drop position
        for (var i = 0; i < itemCount; i++)
        {
            if (listView.ContainerFromIndex(i) is ListViewItem container)
            {
                var itemBounds = container.TransformToVisual(listView).TransformBounds(
                    new Rect(0, 0, container.ActualWidth, container.ActualHeight));

                if (ItemsOrientation == Orientation.Horizontal)
                {
                    // For horizontal layout, check X position
                    if (position.X < itemBounds.X + (itemBounds.Width / 2))
                    {
                        return i;
                    }
                }
                else
                {
                    // For vertical layout, check Y position
                    if (position.Y < itemBounds.Y + (itemBounds.Height / 2))
                    {
                        return i;
                    }
                }
            }
        }

        // If we're past all items, insert at the end
        return itemCount;
    }

    // Tracks which section (Start/Center/End) the add button was clicked for
    private DockPinSide _addBandTargetSide;

    private void AddBandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sideTag)
        {
            _addBandTargetSide = sideTag switch
            {
                "Start" => DockPinSide.Start,
                "Center" => DockPinSide.Center,
                "End" => DockPinSide.End,
                _ => DockPinSide.Center,
            };

            // Populate the list with available bands (not already in the dock)
            var availableBands = ViewModel.GetAvailableBandsToAdd().ToList();
            AddBandListView.ItemsSource = availableBands;

            // Show/hide empty state text based on whether there are bands to add
            var hasAvailableBands = availableBands.Count > 0;
            NoAvailableBandsText.Visibility = hasAvailableBands ? Visibility.Collapsed : Visibility.Visible;
            AddBandListView.Visibility = hasAvailableBands ? Visibility.Visible : Visibility.Collapsed;

            // Show the flyout
            PreparePopupForShow(AddBandFlyout, button);
            AddBandFlyout.ShowAt(button);
        }
    }

    private void AddBandListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TopLevelViewModel topLevel)
        {
            // Add the band to the target section
            ViewModel.AddBandToSection(topLevel, _addBandTargetSide);

            // Close the flyout
            AddBandFlyout.Hide();
        }
    }

    private void BandListView_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is ListView view && (_draggedBand != null || e.DataView.Properties.ContainsKey("DockBandId")))
        {
            view.Background = Application.Current.Resources["ControlAltFillColorQuarternaryBrush"] as SolidColorBrush;
            e.DragUIOverride.IsGlyphVisible = false;
            e.DragUIOverride.IsCaptionVisible = false;
        }
    }

    private void BandListView_DragLeave(object sender, DragEventArgs e)
    {
        ResetListViewState(sender);
    }

    private void ResetListViewState(object sender)
    {
        if (sender is ListView listView)
        {
            listView.Background = new SolidColorBrush(Colors.Transparent);
        }
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        // Don't intercept internal band drag-drop during edit mode
        if (_draggedBand != null)
        {
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems) ||
            e.DataView.Contains(StandardDataFormats.Uri))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = RS_.GetString("Dock_DropFile_Caption");
            e.DragUIOverride.IsGlyphVisible = true;
            e.DragUIOverride.IsCaptionVisible = true;

            // DON'T mark the event as handled - if you do, we won't get the Drop event.
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        // Don't intercept internal band drag-drop during edit mode
        if (_draggedBand != null)
        {
            Logger.LogDebug("[DockDrop] RootGrid_Drop: ignoring (internal band drag in progress)");
            return;
        }

        var hasStorageItems = e.DataView.Contains(StandardDataFormats.StorageItems);
        var hasUri = e.DataView.Contains(StandardDataFormats.Uri);

        if (!hasStorageItems && !hasUri)
        {
            return;
        }

        e.Handled = true;

        try
        {
            var bookmarksManager = App.Current.Services.GetService<IBookmarksManager>();
            if (bookmarksManager == null)
            {
                Logger.LogWarning("[DockDrop] IBookmarksManager service is not registered; cannot pin dropped item");
                return;
            }

            var foundItem = false;
            if (hasStorageItems)
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    var path = item.Path;
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(path);
                    AddBookmarkAndPinToDock(bookmarksManager, name, path);
                    foundItem = true;
                }
            }

            if (foundItem)
            {
                return;
            }

            if (hasUri)
            {
                var uri = await e.DataView.GetUriAsync();
                var url = uri.AbsoluteUri;
                var name = uri.Host;
                AddBookmarkAndPinToDock(bookmarksManager, name, url);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("[DockDrop] Error handling file drop on dock", ex);
        }
    }

    private static void AddBookmarkAndPinToDock(IBookmarksManager bookmarksManager, string name, string bookmarkValue)
    {
        var bookmark = bookmarksManager.Add(name, bookmarkValue);

        // Make the command ID exactly the same as the ID it would have in the
        // top-level list, so that pinning to the dock from the top-level is seamless.
        var commandId = Ext.Bookmarks.Helpers.CommandIds.GetLaunchBookmarkItemId(bookmark.Id);
        Logger.LogDebug($"[DockDrop] Pinning dropped item '{name}' as bookmark id={bookmark.Id} (commandId='{commandId}')");
        WeakReferenceMessenger.Default.Send(new PinToDockMessage("Bookmarks", commandId, true, WithReload: false));
    }

    private void BandListView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The band lists report their natural content size (they sit inside
        // scrollers), so a change here means the dock's fit-to-content length
        // is stale. No-op for full-edge docks — the window ignores the event.
        ContentLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool _resizeGripDragActive;

    private void ResizeGrip_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement grip && grip.CapturePointer(e.Pointer))
        {
            _resizeGripDragActive = true;
            ResizeDragStarted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void ResizeGrip_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_resizeGripDragActive)
        {
            ResizeDragDelta?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void ResizeGrip_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_resizeGripDragActive && sender is FrameworkElement grip)
        {
            grip.ReleasePointerCapture(e.Pointer);
            CompleteResizeGripDrag();
            e.Handled = true;
        }
    }

    private void ResizeGrip_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        CompleteResizeGripDrag();
    }

    private void CompleteResizeGripDrag()
    {
        if (_resizeGripDragActive)
        {
            _resizeGripDragActive = false;
            ResizeDragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ResizeGrip_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        ResizeDragReset?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void ResizeGrip_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var shape = ItemsOrientation == Orientation.Horizontal
            ? Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast
            : Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth;
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(shape);
    }

    private void ResizeGrip_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_resizeGripDragActive)
        {
            ProtectedCursor = null;
        }
    }

    private void ShortcutTargetTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AddShortcutButton.IsEnabled = !string.IsNullOrWhiteSpace(ShortcutTargetTextBox.Text);
    }

    private async void BrowseShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (XamlRoot?.ContentIslandEnvironment is null)
            {
                return;
            }

            var windowId = XamlRoot.ContentIslandEnvironment.AppWindowId;
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(windowId)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.Desktop,
            };
            picker.FileTypeFilter!.Add("*");

            var file = await picker.PickSingleFileAsync()!;
            if (file is not null && !string.IsNullOrEmpty(file.Path))
            {
                ShortcutTargetTextBox.Text = file.Path;
                if (string.IsNullOrWhiteSpace(ShortcutNameTextBox.Text))
                {
                    ShortcutNameTextBox.Text = Path.GetFileNameWithoutExtension(file.Path);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to pick a file for a dock shortcut", ex);
        }
    }

    private void AddShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        var target = ShortcutTargetTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        var name = ShortcutNameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = DeriveShortcutName(target);
        }

        var bookmarksManager = App.Current.Services.GetService<IBookmarksManager>();
        if (bookmarksManager is null)
        {
            Logger.LogWarning("IBookmarksManager service is not registered; cannot add dock shortcut");
            return;
        }

        var bookmark = bookmarksManager.Add(name, target);

        // Same command ID the bookmark would have in the top-level list, so
        // pinning stays consistent with the drag-and-drop and palette paths.
        var commandId = Ext.Bookmarks.Helpers.CommandIds.GetLaunchBookmarkItemId(bookmark.Id);
        WeakReferenceMessenger.Default.Send(new PinToDockMessage(
            "Bookmarks",
            commandId,
            true,
            WithReload: false,
            Side: _addBandTargetSide,
            MonitorDeviceId: ViewModel.MonitorDeviceId));

        ShortcutNameTextBox.Text = string.Empty;
        ShortcutTargetTextBox.Text = string.Empty;
        AddBandFlyout.Hide();
    }

    private static string DeriveShortcutName(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host;
        }

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(target);
            if (!string.IsNullOrEmpty(fileName))
            {
                return fileName;
            }
        }
        catch (ArgumentException)
        {
            // Not a valid path - fall through to using the raw target as the name.
        }

        return target;
    }

    public void Receive(CrossMonitorBandDropMessage message)
    {
        // Only match if this dock has a real monitor ID that matches the source.
        if (ViewModel.MonitorDeviceId is null)
        {
            return;
        }

        if (!string.Equals(ViewModel.MonitorDeviceId, message.SourceMonitorDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.RemoveBandById(message.BandId);
        });
    }
}
