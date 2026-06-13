// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using ManagedCommon;
using Microsoft.CmdPal.UI.Helpers;
using Microsoft.CmdPal.UI.ViewModels;
using Microsoft.CmdPal.UI.ViewModels.Dock;
using Microsoft.CmdPal.UI.ViewModels.Messages;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CmdPal.UI.ViewModels.Services;
using Microsoft.CmdPal.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT;
using WinRT.Interop;
using WinUIEx;
using MonitorInfo = Microsoft.CmdPal.UI.ViewModels.Models.MonitorInfo;

namespace Microsoft.CmdPal.UI.Dock;

#pragma warning disable SA1402 // File may only contain a single type

public sealed partial class DockWindow : WindowEx,
    IRecipient<BringToTopMessage>,
    IRecipient<RequestShowPaletteAtMessage>,
    IRecipient<QuitMessage>,
    IDisposable
{
#pragma warning disable SA1306 // Field names should begin with lower-case letter
#pragma warning disable SA1310 // Field names should not contain underscore
    private readonly uint WM_TASKBAR_RESTART;
#pragma warning restore SA1310 // Field names should not contain underscore
#pragma warning restore SA1306 // Field names should begin with lower-case letter

    private readonly IThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IMonitorService _monitorService;
    private readonly DockWindowViewModel _windowViewModel;
    private readonly HiddenOwnerWindowBehavior _hiddenOwnerWindowBehavior = new();

    private HWND _hwnd = HWND.Null;
    private APPBARDATA _appBarData;
    private uint _callbackMessageId;
    private bool _isWindowTopmost;
    private bool _isFullScreenAppOpen;

    private DockSettings _settings;
    private DockViewModel viewModel;
    private DockControl _dock;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _configurationSource;
    private bool _isUpdatingBackdrop;
    private BackdropParameters? _lastAppliedAcrylicBackdrop;
    private DockSize _lastSize;
    private bool _isDisposed;

    // Resize-drag state for fit-to-content mode. Lengths are in physical pixels.
    private System.Drawing.Point _resizeDragStartCursor;
    private double _resizeDragStartLengthPx;
    private bool _resizeDragActive;

    /// <summary>
    /// Minimum length of the dock (in DIPs) when it doesn't span the full screen edge.
    /// </summary>
    private const double MinFloatingLengthDips = 140;

    /// <summary>
    /// Extra room (in DIPs) added to the measured content length in fit-to-content
    /// mode so the resize grip and edge margins don't overlap the content.
    /// </summary>
    private const double FloatingContentPaddingDips = 24;

    /// <summary>
    /// How close (in DIPs) the floating dock's edge must be to a screen edge to snap to it.
    /// </summary>
    private const double SnapThresholdDips = 28;

    /// <summary>
    /// Thickness (in DIPs) of the on-screen strip left visible when a snapped floating
    /// dock is auto-hidden.
    /// </summary>
    private const double AutoHideRevealSliverDips = 4;

    /// <summary>
    /// How close (in DIPs) the pointer must come to the hidden strip to reveal the dock.
    /// </summary>
    private const double AutoHideRevealZoneDips = 6;

    private DispatcherQueueTimer? _autoHideTimer;
    private bool _autoHideHidden;

    /// <summary>
    /// The physical-pixel rectangle a snapped floating dock occupies when fully shown.
    /// Cached by <see cref="PositionFloatingDock"/> so the auto-hide poll doesn't re-measure
    /// content on every tick.
    /// </summary>
    private RECT _floatingShownRect;

    /// <summary>
    /// The monitor this dock window is displayed on. Null means primary monitor (legacy behavior).
    /// </summary>
    private MonitorInfo? _targetMonitor;

    /// <summary>
    /// Per-monitor dock side override. Null means use the global setting.
    /// </summary>
    private DockSide? _sideOverride;

    /// <summary>
    /// Gets the effective dock side for this window, respecting per-monitor overrides.
    /// </summary>
    private DockSide EffectiveSide => _sideOverride ?? _settings.Side;

    /// <summary>
    /// Gets a value indicating whether the dock is a free-floating, draggable toolbar
    /// (not anchored to a screen edge as an app bar).
    /// </summary>
    private bool IsFloatingPlacement => _settings.Placement == DockPlacement.Floating;

    /// <summary>
    /// Gets a value indicating whether the dock is an edge-anchored compact toolbar that
    /// fits its content (and therefore isn't registered as a space-reserving app bar).
    /// </summary>
    private bool IsEdgeFitToContent => _settings.Placement == DockPlacement.Edge && _settings.LengthMode == DockLengthMode.FitToContent;

    /// <summary>
    /// Gets the dock side the inner <see cref="DockControl"/> should lay out for: the snapped
    /// edge when floating-and-snapped, horizontal (Top) when floating-and-free, otherwise the
    /// edge-placement side.
    /// </summary>
    private DockSide ControlSide => IsFloatingPlacement
        ? (_settings.FloatingSnapped ? _settings.Side : DockSide.Top)
        : EffectiveSide;

    // Store the original WndProc
    private WNDPROC? _originalWndProc;
    private WNDPROC? _customWndProc;

    // internal Settings CurrentSettings => _settings;
    public DockWindow()
        : this(App.Current.Services.GetService<DockViewModel>()!)
    {
    }

    public DockWindow(DockViewModel dockViewModel)
        : this(dockViewModel, null, null)
    {
    }

    public DockWindow(DockViewModel dockViewModel, MonitorInfo? targetMonitor, DockSide? sideOverride)
    {
        _targetMonitor = targetMonitor;
        _sideOverride = sideOverride;

        var serviceProvider = App.Current.Services;
        var mainSettings = serviceProvider.GetRequiredService<ISettingsService>().Settings;
        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _settingsService.SettingsChanged += SettingsChangedHandler;
        _monitorService = serviceProvider.GetRequiredService<IMonitorService>();
        _settings = mainSettings.DockSettings;
        _lastSize = EffectiveDockSize(_settings);

        viewModel = dockViewModel;
        _themeService = serviceProvider.GetRequiredService<IThemeService>();
        _themeService.ThemeChanged += ThemeService_ThemeChanged;
        InitializeBackdropSupport();
        _windowViewModel = new DockWindowViewModel(_themeService);
        _dock = new DockControl(viewModel);
        _dock.ResizeDragStarted += Dock_ResizeDragStarted;
        _dock.ResizeDragDelta += Dock_ResizeDragDelta;
        _dock.ResizeDragCompleted += Dock_ResizeDragCompleted;
        _dock.ResizeDragReset += Dock_ResizeDragReset;
        _dock.ContentLayoutChanged += Dock_ContentLayoutChanged;
        _dock.MoveDragRequested += Dock_MoveDragRequested;

        InitializeComponent();
        Root.Children.Add(_dock);
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        _hiddenOwnerWindowBehavior.ShowInTaskbar(this, false);
        if (AppWindow.Presenter is OverlappedPresenter overlappedPresenter)
        {
            overlappedPresenter.SetBorderAndTitleBar(false, false);
            overlappedPresenter.IsResizable = false;
        }

        _hwnd = GetWindowHandle(this);
        _dock.OwnerHwnd = (nint)_hwnd;

        // immediately when we're created: make sure to remove our window frame
        // and shadow. We don't _always_ get an Activated when we're first
        // created.
        UpdateWindowFrame();
        this.Activated += DockWindow_Activated;

        WeakReferenceMessenger.Default.Register<BringToTopMessage>(this);
        WeakReferenceMessenger.Default.Register<RequestShowPaletteAtMessage>(this);
        WeakReferenceMessenger.Default.Register<QuitMessage>(this);

        // Subclass the window to intercept messages
        //
        // Set up custom window procedure to listen for display changes
        // LOAD BEARING: If you don't stick the pointer to HotKeyPrc into a
        // member (and instead like, use a local), then the pointer we marshal
        // into the WindowLongPtr will be useless after we leave this function,
        // and our **WindProc will explode**.
        _customWndProc = CustomWndProc;

        _callbackMessageId = PInvoke.RegisterWindowMessage($"CmdPal_ABM_{_hwnd}");

        // TaskbarCreated is the message that's broadcast when explorer.exe
        // restarts. We need to know when that happens to be able to bring our
        // app bar back
        // And this apparently happens on lock screens / hibernates, too
        WM_TASKBAR_RESTART = PInvoke.RegisterWindowMessage("TaskbarCreated");

        var hotKeyPrcPointer = Marshal.GetFunctionPointerForDelegate(_customWndProc);
        _originalWndProc = Marshal.GetDelegateForFunctionPointer<WNDPROC>(PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, hotKeyPrcPointer));

        // Disable minimize and maximize box
        var style = (WINDOW_STYLE)PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        style &= ~WINDOW_STYLE.WS_MINIMIZEBOX; // Remove WS_MINIMIZEBOX
        style &= ~WINDOW_STYLE.WS_MAXIMIZEBOX; // Remove WS_MAXIMIZEBOX
        _ = PInvoke.SetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, (int)style);

        ShowDesktop.AddHook(this);
        var userNotificationFlags = WindowHelper.GetUserNotificationFlags();
        _isFullScreenAppOpen = userNotificationFlags.IsFullscreenState || userNotificationFlags.IsBusy;
        UpdateSettingsOnUiThread();
    }

    private void SettingsChangedHandler(ISettingsService sender, SettingsModel args)
    {
        if (_isDisposed)
        {
            return;
        }

        _settings = args.DockSettings;
        RefreshSideOverride();
        DispatcherQueue.TryEnqueue(UpdateSettingsOnUiThread);
    }

    private void DockWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        UpdateWindowFrame();
        UpdateTopmostState();
    }

    private void UpdateWindowFrame()
    {
        // These are used for removing the very subtle shadow/border that we get from Windows 11
        HwndExtensions.ToggleWindowStyle(_hwnd, false, WindowStyle.TiledWindow);
        unsafe
        {
            // Square corners when spanning the full edge; rounded corners when the
            // dock is a compact toolbar (edge fit-to-content or free-floating).
            var preference = (IsEdgeFitToContent || IsFloatingPlacement)
                ? DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND
                : DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DEFAULT;
            PInvoke.DwmSetWindowAttribute(_hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));
        }
    }

    private HWND GetWindowHandle(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        return new HWND(hwnd);
    }

    private void UpdateSettingsOnUiThread()
    {
        if (_isDisposed)
        {
            return;
        }

        this.viewModel.UpdateSettings(_settings);
        UpdateBackdrop();

        _dock.UpdateSettings(_settings, ControlSide);
        UpdateWindowFrame();

        if (IsFloatingPlacement)
        {
            // Floating placement: a free, draggable overlay — never an app bar.
            if (_appBarData.hWnd != IntPtr.Zero)
            {
                DestroyAppBar(_hwnd);
            }

            PositionFloatingDock();
            UpdateTopmostState();
            UpdateAutoHideTimer();
            return;
        }

        // Leaving floating placement: make sure the auto-hide timer is stopped
        // and the window is back to its shown position.
        UpdateAutoHideTimer();

        if (IsEdgeFitToContent)
        {
            // Fit-to-content mode: the dock is a compact toolbar, not an app
            // bar, so it must not reserve work-area space.
            if (_appBarData.hWnd != IntPtr.Zero)
            {
                DestroyAppBar(_hwnd);
            }

            UpdateFloatingWindowPosition();
            UpdateTopmostState();
            return;
        }

        var side = DockSettingsToViews.GetAppBarEdge(EffectiveSide);

        if (_appBarData.hWnd != IntPtr.Zero)
        {
            var sameEdge = _appBarData.uEdge == side;
            var sameSize = _lastSize == EffectiveDockSize(_settings);
            if (sameEdge && sameSize)
            {
                UpdateTopmostState();
                return;
            }

            DestroyAppBar(_hwnd);
        }

        CreateAppBar(_hwnd);
        UpdateTopmostState();
    }

    private void InitializeBackdropSupport()
    {
        if (DesktopAcrylicController.IsSupported())
        {
            _configurationSource = new SystemBackdropConfiguration
            {
                IsInputActive = true,
            };
        }
    }

    private void UpdateBackdrop()
    {
        // Prevent re-entrance when backdrop changes trigger theme refresh work.
        if (_isUpdatingBackdrop)
        {
            return;
        }

        _isUpdatingBackdrop = true;

        try
        {
            switch (_settings.Backdrop)
            {
                case DockBackdrop.Transparent:
                    if (SystemBackdrop is not TransparentTintBackdrop)
                    {
                        CleanupBackdropControllers();
                        SetupTransparentBackdrop();
                    }

                    break;

                case DockBackdrop.Acrylic:
                default:
                    SetupDesktopAcrylic(_themeService.CurrentDockTheme.BackdropParameters);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to update dock backdrop", ex);
        }
        finally
        {
            _isUpdatingBackdrop = false;
        }
    }

    private void SetupTransparentBackdrop()
    {
        if (SystemBackdrop is not TransparentTintBackdrop)
        {
            SystemBackdrop = new TransparentTintBackdrop();
        }

        _lastAppliedAcrylicBackdrop = null;
    }

    private void CleanupBackdropControllers()
    {
        if (_acrylicController is not null)
        {
            _acrylicController.RemoveAllSystemBackdropTargets();
            _acrylicController.Dispose();
            _acrylicController = null;
        }

        _lastAppliedAcrylicBackdrop = null;
    }

    private void SetupDesktopAcrylic(BackdropParameters backdrop)
    {
        var needsAcrylicUpdate = _acrylicController is null || _lastAppliedAcrylicBackdrop != backdrop;
        if (!needsAcrylicUpdate)
        {
            return;
        }

        CleanupBackdropControllers();

        // Fall back to the transparent backdrop if acrylic is not supported.
        if (_configurationSource is null || !DesktopAcrylicController.IsSupported())
        {
            SetupTransparentBackdrop();
            return;
        }

        // DesktopAcrylicController and SystemBackdrop can't be active simultaneously.
        SystemBackdrop = null;

        _acrylicController = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin,
            TintColor = backdrop.TintColor,
            TintOpacity = backdrop.EffectiveOpacity,
            FallbackColor = backdrop.FallbackColor,
            LuminosityOpacity = backdrop.EffectiveLuminosityOpacity,
        };

        // Enable the system backdrop.
        // Note: Be sure to have "using WinRT;" to support the Window.As<...>() call.
        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        _lastAppliedAcrylicBackdrop = backdrop;
    }

    private void DisposeAcrylic()
    {
        CleanupBackdropControllers();
        _configurationSource = null;
    }

    private void ThemeService_ThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateBackdrop();

            // ActualTheme / RequestedTheme sync,
            // as pilfered from WindowThemeSynchronizer
            // LOAD BEARING: Changing the RequestedTheme to Dark then Light then target forces
            // a refresh of the theme.
            Root.RequestedTheme = ElementTheme.Dark;
            Root.RequestedTheme = ElementTheme.Light;
            Root.RequestedTheme = _themeService.CurrentDockTheme.Theme;
        });
    }

    private void CreateAppBar(HWND hwnd)
    {
        _appBarData = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = _callbackMessageId,
        };

        // Register this window as an app bar
        PInvoke.SHAppBarMessage(PInvoke.ABM_NEW, ref _appBarData);

        // Stash the last size we created the bar at, so we know when to hot-
        // reload it
        _lastSize = EffectiveDockSize(_settings);

        UpdateWindowPosition();
    }

    private void DestroyAppBar(HWND hwnd)
    {
        PInvoke.SHAppBarMessage(PInvoke.ABM_REMOVE, ref _appBarData);
        _appBarData = default;
    }

    private void UpdateTopmostState(bool bringToFront = false)
    {
        var shouldStayOnTop = _settings.AlwaysOnTop && !_isFullScreenAppOpen;
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;

        if (shouldStayOnTop)
        {
            if (_isWindowTopmost && !bringToFront)
            {
                return;
            }

            PInvoke.SetWindowPos(_hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0, flags);
            _isWindowTopmost = true;
            return;
        }

        if (bringToFront)
        {
            // Win32 trick: briefly set HWND_TOPMOST then immediately clear it
            // with HWND_NOTOPMOST. This brings the window to the foreground
            // without permanently pinning it as topmost.
            PInvoke.SetWindowPos(_hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0, flags);
        }

        if (!_isWindowTopmost && !bringToFront)
        {
            return;
        }

        var zOrder = _isFullScreenAppOpen ? HWND.HWND_BOTTOM : HWND.HWND_NOTOPMOST;
        PInvoke.SetWindowPos(_hwnd, zOrder, 0, 0, 0, 0, flags);
        _isWindowTopmost = false;
    }

    private void UpdateWindowPosition()
    {
        Logger.LogDebug("UpdateWindowPosition");

        if (IsFloatingPlacement)
        {
            PositionFloatingDock();
            return;
        }

        if (IsEdgeFitToContent)
        {
            UpdateFloatingWindowPosition();
            return;
        }

        var dpi = PInvoke.GetDpiForWindow(_hwnd);

        var scaleFactor = dpi / 96.0;
        var effectiveSize = EffectiveDockSize(_settings);
        UpdateAppBarDataForEdge(EffectiveSide, effectiveSize, scaleFactor);

        // Query and set position
        PInvoke.SHAppBarMessage(PInvoke.ABM_QUERYPOS, ref _appBarData);

        // ABM_QUERYPOS adjusts our rect so we don't overlap other app bars,
        // but it may have shifted our anchored edge without updating the
        // opposite edge. We need to re-apply our desired thickness so the
        // bar keeps its correct size. Without this, a second bar docked to
        // the same side would get a zero-height/width rect and fail to
        // reserve work-area space.
        switch (EffectiveSide)
        {
            case DockSide.Top:
                _appBarData.rc.bottom = _appBarData.rc.top + (int)(DockSettingsToViews.HeightForSize(effectiveSize) * scaleFactor);
                break;
            case DockSide.Bottom:
                _appBarData.rc.top = _appBarData.rc.bottom - (int)(DockSettingsToViews.HeightForSize(effectiveSize) * scaleFactor);
                break;
            case DockSide.Left:
                _appBarData.rc.right = _appBarData.rc.left + (int)(DockSettingsToViews.WidthForSize(effectiveSize) * scaleFactor);
                break;
            case DockSide.Right:
                _appBarData.rc.left = _appBarData.rc.right - (int)(DockSettingsToViews.WidthForSize(effectiveSize) * scaleFactor);
                break;
        }

        PInvoke.SHAppBarMessage(PInvoke.ABM_SETPOS, ref _appBarData);

        // TODO: investigate ABS_AUTOHIDE and auto hide bars.
        // I think it's something like this, but I don't totally know
        //   _appBarData.lParam = ABS_ALWAYSONTOP;
        //   _appBarData.lParam = (LPARAM)(int)PInvoke.ABS_AUTOHIDE;
        //   PInvoke.SHAppBarMessage(ABM_SETSTATE, ref _appBarData);
        //   PInvoke.SHAppBarMessage(PInvoke.ABM_SETAUTOHIDEBAR, ref _appBarData);

        // The dock window is borderless (SetBorderAndTitleBar(false, false),
        // IsResizable = false) so no frame compensation is needed — the
        // app bar rect matches the window rect exactly.
        PInvoke.MoveWindow(
            _hwnd,
            _appBarData.rc.left,
            _appBarData.rc.top,
            _appBarData.rc.right - _appBarData.rc.left,
            _appBarData.rc.bottom - _appBarData.rc.top,
            true);
    }

    /// <summary>
    /// Positions the dock as a compact toolbar along the chosen screen edge
    /// (fit-to-content mode). Unlike the app-bar path, the window length is
    /// either the user-set <see cref="DockSettings.CustomLength"/> or the
    /// measured natural length of the dock content, and the window is aligned
    /// along the edge per <see cref="DockSettings.Alignment"/>.
    /// </summary>
    /// <param name="overrideLengthPx">Live length (physical pixels) used while the
    /// user is dragging the resize grip; <c>null</c> uses settings/measured length.</param>
    private void UpdateFloatingWindowPosition(double? overrideLengthPx = null)
    {
        if (_isDisposed)
        {
            return;
        }

        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var scaleFactor = dpi / 96.0;
        var side = EffectiveSide;
        var isHorizontal = side is DockSide.Top or DockSide.Bottom;
        var effectiveSize = EffectiveDockSize(_settings);
        var thicknessDips = isHorizontal
            ? DockSettingsToViews.HeightForSize(effectiveSize)
            : DockSettingsToViews.WidthForSize(effectiveSize);
        var thicknessPx = (int)(thicknessDips * scaleFactor);

        // Use the monitor work area (excludes the taskbar) so the toolbar never
        // overlaps it; fall back to primary screen metrics like the app-bar path.
        int waLeft, waTop, waRight, waBottom;
        if (_targetMonitor is not null)
        {
            waLeft = _targetMonitor.WorkArea.Left;
            waTop = _targetMonitor.WorkArea.Top;
            waRight = _targetMonitor.WorkArea.Right;
            waBottom = _targetMonitor.WorkArea.Bottom;
        }
        else
        {
            var primary = _monitorService.GetPrimaryMonitor();
            if (primary is not null)
            {
                waLeft = primary.WorkArea.Left;
                waTop = primary.WorkArea.Top;
                waRight = primary.WorkArea.Right;
                waBottom = primary.WorkArea.Bottom;
            }
            else
            {
                waLeft = 0;
                waTop = 0;
                waRight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
                waBottom = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);
            }
        }

        var edgeLengthPx = isHorizontal ? waRight - waLeft : waBottom - waTop;

        double lengthPx;
        if (overrideLengthPx is double live)
        {
            lengthPx = live;
        }
        else if (_settings.CustomLength > 0)
        {
            lengthPx = _settings.CustomLength * scaleFactor;
        }
        else
        {
            var contentDips = _dock.MeasureDesiredLength(isHorizontal, thicknessDips);
            lengthPx = (contentDips + FloatingContentPaddingDips) * scaleFactor;
        }

        lengthPx = Math.Clamp(lengthPx, Math.Min(MinFloatingLengthDips * scaleFactor, edgeLengthPx), edgeLengthPx);

        var offsetPx = _settings.Alignment switch
        {
            DockAlignment.Start => 0.0,
            DockAlignment.End => edgeLengthPx - lengthPx,
            _ => (edgeLengthPx - lengthPx) / 2.0,
        };

        int x, y, width, height;
        if (isHorizontal)
        {
            x = waLeft + (int)offsetPx;
            y = side == DockSide.Top ? waTop : waBottom - thicknessPx;
            width = (int)lengthPx;
            height = thicknessPx;
        }
        else
        {
            x = side == DockSide.Left ? waLeft : waRight - thicknessPx;
            y = waTop + (int)offsetPx;
            width = thicknessPx;
            height = (int)lengthPx;
        }

        // Skip the move when nothing changed; SizeChanged-driven refits would
        // otherwise keep poking the window on every layout pass.
        PInvoke.GetWindowRect(_hwnd, out var currentRect);
        if (currentRect.left == x && currentRect.top == y &&
            currentRect.right - currentRect.left == width &&
            currentRect.bottom - currentRect.top == height)
        {
            return;
        }

        PInvoke.MoveWindow(_hwnd, x, y, width, height, true);
    }

    private void Dock_ResizeDragStarted(object? sender, EventArgs e)
    {
        if (_isDisposed || !IsEdgeFitToContent)
        {
            return;
        }

        PInvoke.GetCursorPos(out var cursor);
        _resizeDragStartCursor = cursor;
        PInvoke.GetWindowRect(_hwnd, out var rect);
        var isHorizontal = EffectiveSide is DockSide.Top or DockSide.Bottom;
        _resizeDragStartLengthPx = isHorizontal ? rect.right - rect.left : rect.bottom - rect.top;
        _resizeDragActive = true;
    }

    private void Dock_ResizeDragDelta(object? sender, EventArgs e)
    {
        if (_isDisposed || !IsEdgeFitToContent || !_resizeDragActive)
        {
            return;
        }

        PInvoke.GetCursorPos(out var cursor);
        var isHorizontal = EffectiveSide is DockSide.Top or DockSide.Bottom;
        double delta = isHorizontal
            ? cursor.X - _resizeDragStartCursor.X
            : cursor.Y - _resizeDragStartCursor.Y;

        // The grip sits on the end edge of the dock, except for End alignment
        // where the end edge is pinned to the screen corner and the grip sits
        // on the start edge instead (dragging toward the start grows the dock).
        // For Center alignment the dock grows symmetrically, so double the
        // delta to keep the grip tracking the cursor.
        var sign = _settings.Alignment == DockAlignment.End ? -1.0 : 1.0;
        var factor = _settings.Alignment == DockAlignment.Center ? 2.0 : 1.0;

        var newLengthPx = _resizeDragStartLengthPx + (delta * sign * factor);
        UpdateFloatingWindowPosition(newLengthPx);
    }

    private void Dock_ResizeDragCompleted(object? sender, EventArgs e)
    {
        if (_isDisposed || !IsEdgeFitToContent || !_resizeDragActive)
        {
            return;
        }

        _resizeDragActive = false;

        // Persist the final (clamped) length the window actually ended up at.
        PInvoke.GetWindowRect(_hwnd, out var rect);
        var isHorizontal = EffectiveSide is DockSide.Top or DockSide.Bottom;
        var lengthPx = isHorizontal ? rect.right - rect.left : rect.bottom - rect.top;

        // A click on the grip without movement shouldn't turn automatic sizing
        // into a fixed length.
        if (Math.Abs(lengthPx - _resizeDragStartLengthPx) < 1)
        {
            return;
        }

        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var lengthDips = Math.Round(lengthPx * 96.0 / dpi);

        _settingsService.UpdateSettings(s => s with
        {
            DockSettings = s.DockSettings with { CustomLength = lengthDips },
        });
    }

    private void Dock_ResizeDragReset(object? sender, EventArgs e)
    {
        if (_isDisposed || !IsEdgeFitToContent)
        {
            return;
        }

        _resizeDragActive = false;

        // Back to automatic fit-to-content sizing.
        _settingsService.UpdateSettings(s => s with
        {
            DockSettings = s.DockSettings with { CustomLength = 0 },
        });
    }

    private void Dock_ContentLayoutChanged(object? sender, EventArgs e)
    {
        // The dock's natural content length changed (band added/removed, items
        // loaded, edit mode toggled). In automatic fit-to-content mode, follow it.
        if (_isDisposed || !IsEdgeFitToContent || _settings.CustomLength > 0 || _resizeDragActive)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_isDisposed && IsEdgeFitToContent && _settings.CustomLength == 0 && !_resizeDragActive)
            {
                UpdateFloatingWindowPosition();
            }
            else if (!_isDisposed && IsFloatingPlacement && !_autoHideHidden)
            {
                // Free-floating dock follows its content size too.
                PositionFloatingDock();
            }
        });
    }

    /// <summary>
    /// Positions a free-floating dock. When snapped, it sits flush against the
    /// <see cref="DockSettings.Side"/> edge (content-sized, auto-hide capable);
    /// otherwise it sits at the saved free position, sized to its content in both
    /// dimensions. Skips repositioning while the dock is auto-hidden so the hide
    /// offset isn't clobbered.
    /// </summary>
    private void PositionFloatingDock()
    {
        if (_isDisposed || !IsFloatingPlacement || _autoHideHidden)
        {
            return;
        }

        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var scaleFactor = dpi / 96.0;
        var snappedSide = _settings.Side;
        var isHorizontal = !_settings.FloatingSnapped || snappedSide is DockSide.Top or DockSide.Bottom;

        // Thickness (cross-axis) comes from the dock size; length (main axis)
        // from the measured content.
        var effectiveSize = EffectiveDockSize(_settings);
        var thicknessDips = isHorizontal
            ? DockSettingsToViews.HeightForSize(effectiveSize)
            : DockSettingsToViews.WidthForSize(effectiveSize);
        var thicknessPx = (int)(thicknessDips * scaleFactor);

        var contentDips = _dock.MeasureDesiredLength(isHorizontal, thicknessDips);
        var lengthPx = (int)((contentDips + FloatingContentPaddingDips) * scaleFactor);
        lengthPx = Math.Max(lengthPx, (int)(MinFloatingLengthDips * scaleFactor));

        var wa = GetWorkAreaUnderWindow();

        int x, y, width, height;
        if (_settings.FloatingSnapped)
        {
            switch (snappedSide)
            {
                case DockSide.Top:
                    width = lengthPx;
                    height = thicknessPx;
                    x = Clamp(wa.left + (((wa.right - wa.left) - width) / 2), wa.left, wa.right - width);
                    y = wa.top;
                    break;
                case DockSide.Bottom:
                    width = lengthPx;
                    height = thicknessPx;
                    x = Clamp(wa.left + (((wa.right - wa.left) - width) / 2), wa.left, wa.right - width);
                    y = wa.bottom - height;
                    break;
                case DockSide.Left:
                    width = thicknessPx;
                    height = lengthPx;
                    x = wa.left;
                    y = Clamp(wa.top + (((wa.bottom - wa.top) - height) / 2), wa.top, wa.bottom - height);
                    break;
                default: // Right
                    width = thicknessPx;
                    height = lengthPx;
                    x = wa.right - width;
                    y = Clamp(wa.top + (((wa.bottom - wa.top) - height) / 2), wa.top, wa.bottom - height);
                    break;
            }

            _floatingShownRect = new RECT { left = x, top = y, right = x + width, bottom = y + height };
        }
        else
        {
            width = lengthPx;
            height = thicknessPx;
            x = (int)(_settings.FloatingX * scaleFactor);
            y = (int)(_settings.FloatingY * scaleFactor);

            // Keep the dock on-screen even if the saved position is stale.
            x = Clamp(x, wa.left, Math.Max(wa.left, wa.right - width));
            y = Clamp(y, wa.top, Math.Max(wa.top, wa.bottom - height));
        }

        PInvoke.MoveWindow(_hwnd, x, y, width, height, true);
    }

    private void Dock_MoveDragRequested(object? sender, EventArgs e)
    {
        if (_isDisposed || !IsFloatingPlacement)
        {
            return;
        }

        // Hand the drag off to the system's modal move loop. It moves the window
        // with the cursor and, on release, sends WM_EXITSIZEMOVE — which is where
        // we run snap detection. This keeps band buttons clickable (only the grab
        // handle starts a move) without a hand-rolled move loop.
        _ = PInvoke.ReleaseCapture();
        _ = PInvoke.SendMessage(_hwnd, PInvoke.WM_NCLBUTTONDOWN, new WPARAM(PInvoke.HTCAPTION), default);
    }

    /// <summary>
    /// Runs after the user finishes dragging a floating dock: snaps to the nearest
    /// screen edge when close enough (transforming orientation), otherwise records
    /// the new free position. Persists the result so it survives restarts.
    /// </summary>
    private void OnFloatingMoveFinished()
    {
        if (_isDisposed || !IsFloatingPlacement)
        {
            return;
        }

        PInvoke.GetWindowRect(_hwnd, out var rect);
        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var scaleFactor = dpi / 96.0;
        var wa = GetWorkAreaUnderWindow();
        var thresholdPx = SnapThresholdDips * scaleFactor;

        var distLeft = rect.left - wa.left;
        var distTop = rect.top - wa.top;
        var distRight = wa.right - rect.right;
        var distBottom = wa.bottom - rect.bottom;

        var minDist = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));

        DockSide? snapTo = null;
        if (minDist <= thresholdPx)
        {
            if (minDist == distTop)
            {
                snapTo = DockSide.Top;
            }
            else if (minDist == distBottom)
            {
                snapTo = DockSide.Bottom;
            }
            else if (minDist == distLeft)
            {
                snapTo = DockSide.Left;
            }
            else
            {
                snapTo = DockSide.Right;
            }
        }

        if (snapTo is DockSide side)
        {
            _settingsService.UpdateSettings(s => s with
            {
                DockSettings = s.DockSettings with
                {
                    Side = side,
                    FloatingSnapped = true,
                },
            });
        }
        else
        {
            // Free placement: persist the dropped top-left in DIPs.
            _settingsService.UpdateSettings(s => s with
            {
                DockSettings = s.DockSettings with
                {
                    FloatingSnapped = false,
                    FloatingX = Math.Round(rect.left / scaleFactor),
                    FloatingY = Math.Round(rect.top / scaleFactor),
                },
            });
        }
    }

    /// <summary>
    /// Gets the work area (excludes the taskbar) of the monitor currently under the
    /// dock window, in physical pixels. Falls back to the target/primary monitor.
    /// </summary>
    private unsafe RECT GetWorkAreaUnderWindow()
    {
        // MONITOR_DEFAULTTONEAREST always yields a valid monitor for a real window.
        var hmon = PInvoke.MonitorFromWindow(_hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var mi = default(MONITORINFO);
        mi.cbSize = (uint)sizeof(MONITORINFO);
        if (PInvoke.GetMonitorInfo(hmon, &mi))
        {
            return mi.rcWork;
        }

        // Fallback: target monitor work area, else full primary screen.
        if (_targetMonitor is not null)
        {
            return new RECT
            {
                left = _targetMonitor.WorkArea.Left,
                top = _targetMonitor.WorkArea.Top,
                right = _targetMonitor.WorkArea.Right,
                bottom = _targetMonitor.WorkArea.Bottom,
            };
        }

        return new RECT
        {
            left = 0,
            top = 0,
            right = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN),
            bottom = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN),
        };
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

    /// <summary>
    /// Starts or stops the auto-hide polling timer based on the current settings. When
    /// auto-hide turns off (or the dock leaves floating/snapped state) the dock is
    /// returned to its fully-shown position.
    /// </summary>
    private void UpdateAutoHideTimer()
    {
        var wantAutoHide = IsFloatingPlacement && _settings.FloatingSnapped && _settings.AutoHide;

        if (wantAutoHide)
        {
            _autoHideTimer ??= CreateAutoHideTimer();
            if (!_autoHideTimer.IsRunning)
            {
                _autoHideTimer.Start();
            }

            return;
        }

        if (_autoHideTimer is not null && _autoHideTimer.IsRunning)
        {
            _autoHideTimer.Stop();
        }

        // Make sure we don't leave the dock stuck off-screen.
        if (_autoHideHidden)
        {
            _autoHideHidden = false;
            if (IsFloatingPlacement)
            {
                PositionFloatingDock();
            }
        }
    }

    private DispatcherQueueTimer CreateAutoHideTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(150);
        timer.Tick += AutoHide_Tick;
        return timer;
    }

    private void AutoHide_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_isDisposed || !IsFloatingPlacement || !_settings.FloatingSnapped || !_settings.AutoHide)
        {
            return;
        }

        // Use the shown rect cached the last time we positioned the dock, so the
        // poll stays cheap (no content re-measure on every tick).
        if (_floatingShownRect.right <= _floatingShownRect.left)
        {
            _floatingShownRect = GetSnappedShownRect();
        }

        PInvoke.GetCursorPos(out var cursor);
        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var scaleFactor = dpi / 96.0;
        var revealZonePx = AutoHideRevealZoneDips * scaleFactor;

        // The shown rectangle is where the dock sits when revealed.
        var shown = _floatingShownRect;
        var side = _settings.Side;

        // Reveal when the cursor is over the shown rectangle, or within the reveal
        // zone of the snapped edge along the dock's span.
        var overShown = cursor.X >= shown.left && cursor.X <= shown.right &&
                        cursor.Y >= shown.top && cursor.Y <= shown.bottom;

        bool nearEdge = side switch
        {
            DockSide.Top => cursor.Y <= shown.top + revealZonePx && cursor.X >= shown.left && cursor.X <= shown.right,
            DockSide.Bottom => cursor.Y >= shown.bottom - revealZonePx && cursor.X >= shown.left && cursor.X <= shown.right,
            DockSide.Left => cursor.X <= shown.left + revealZonePx && cursor.Y >= shown.top && cursor.Y <= shown.bottom,
            _ => cursor.X >= shown.right - revealZonePx && cursor.Y >= shown.top && cursor.Y <= shown.bottom,
        };

        var wantShown = overShown || nearEdge;

        if (wantShown && _autoHideHidden)
        {
            _autoHideHidden = false;
            PInvoke.MoveWindow(_hwnd, shown.left, shown.top, shown.right - shown.left, shown.bottom - shown.top, true);
        }
        else if (!wantShown && !_autoHideHidden)
        {
            _autoHideHidden = true;
            var sliverPx = (int)(AutoHideRevealSliverDips * scaleFactor);
            var (hx, hy) = side switch
            {
                DockSide.Top => (shown.left, shown.top - ((shown.bottom - shown.top) - sliverPx)),
                DockSide.Bottom => (shown.left, shown.bottom - sliverPx),
                DockSide.Left => (shown.left - ((shown.right - shown.left) - sliverPx), shown.top),
                _ => (shown.right - sliverPx, shown.top),
            };
            PInvoke.MoveWindow(_hwnd, hx, hy, shown.right - shown.left, shown.bottom - shown.top, true);
        }
    }

    /// <summary>
    /// Computes the physical-pixel rectangle a snapped floating dock occupies when
    /// fully shown (mirrors <see cref="PositionFloatingDock"/>'s snapped branch).
    /// </summary>
    private RECT GetSnappedShownRect()
    {
        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var scaleFactor = dpi / 96.0;
        var side = _settings.Side;
        var isHorizontal = side is DockSide.Top or DockSide.Bottom;

        var effectiveSize = EffectiveDockSize(_settings);
        var thicknessDips = isHorizontal
            ? DockSettingsToViews.HeightForSize(effectiveSize)
            : DockSettingsToViews.WidthForSize(effectiveSize);
        var thicknessPx = (int)(thicknessDips * scaleFactor);

        var contentDips = _dock.MeasureDesiredLength(isHorizontal, thicknessDips);
        var lengthPx = (int)((contentDips + FloatingContentPaddingDips) * scaleFactor);
        lengthPx = Math.Max(lengthPx, (int)(MinFloatingLengthDips * scaleFactor));

        var wa = GetWorkAreaUnderWindow();

        int x, y, width, height;
        switch (side)
        {
            case DockSide.Top:
                width = lengthPx;
                height = thicknessPx;
                x = Clamp(wa.left + (((wa.right - wa.left) - width) / 2), wa.left, wa.right - width);
                y = wa.top;
                break;
            case DockSide.Bottom:
                width = lengthPx;
                height = thicknessPx;
                x = Clamp(wa.left + (((wa.right - wa.left) - width) / 2), wa.left, wa.right - width);
                y = wa.bottom - height;
                break;
            case DockSide.Left:
                width = thicknessPx;
                height = lengthPx;
                x = wa.left;
                y = Clamp(wa.top + (((wa.bottom - wa.top) - height) / 2), wa.top, wa.bottom - height);
                break;
            default: // Right
                width = thicknessPx;
                height = lengthPx;
                x = wa.right - width;
                y = Clamp(wa.top + (((wa.bottom - wa.top) - height) / 2), wa.top, wa.bottom - height);
                break;
        }

        return new RECT { left = x, top = y, right = x + width, bottom = y + height };
    }

    /// <summary>
    /// Re-resolves <see cref="_targetMonitor"/> against the current monitor list.
    /// <see cref="MonitorInfo"/> is an immutable record, so the instance captured
    /// at construction time becomes stale whenever the display topography changes.
    /// If the monitor is no longer connected we keep the stale reference; the
    /// <see cref="DockWindowManager"/> will close this window shortly.
    /// </summary>
    private void RefreshTargetMonitor()
    {
        if (_targetMonitor is null)
        {
            return;
        }

        var refreshed = _monitorService.GetMonitorByStableId(_targetMonitor.StableId);
        if (refreshed is not null)
        {
            _targetMonitor = refreshed;
        }
    }

    private void RefreshSideOverride()
    {
        if (_targetMonitor is null)
        {
            _sideOverride = null;
            return;
        }

        _sideOverride = _settings.GetSideForMonitor(_targetMonitor.StableId);
    }

    /// <summary>
    /// Compact mode is only supported for Top/Bottom dock positions.
    /// For Left/Right, always use Default size.
    /// </summary>
    private static DockSize EffectiveDockSize(DockSettings settings)
    {
        var isHorizontal = settings.Side == DockSide.Top || settings.Side == DockSide.Bottom;
        return isHorizontal ? settings.DockSize : DockSize.Default;
    }

    private void UpdateAppBarDataForEdge(DockSide side, DockSize size, double scaleFactor)
    {
        Logger.LogDebug("UpdateAppBarDataForEdge");
        var horizontalHeightDips = DockSettingsToViews.HeightForSize(size);
        var verticalWidthDips = DockSettingsToViews.WidthForSize(size);

        // Use monitor-specific bounds when available; fall back to primary screen metrics
        int monLeft, monTop, monRight, monBottom;
        if (_targetMonitor is not null)
        {
            monLeft = _targetMonitor.Bounds.Left;
            monTop = _targetMonitor.Bounds.Top;
            monRight = _targetMonitor.Bounds.Right;
            monBottom = _targetMonitor.Bounds.Bottom;
        }
        else
        {
            monLeft = 0;
            monTop = 0;
            monRight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
            monBottom = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);
        }

        if (side == DockSide.Top)
        {
            _appBarData.uEdge = PInvoke.ABE_TOP;
            _appBarData.rc.left = monLeft;
            _appBarData.rc.top = monTop;
            _appBarData.rc.right = monRight;
            _appBarData.rc.bottom = monTop + (int)(horizontalHeightDips * scaleFactor);
        }
        else if (side == DockSide.Bottom)
        {
            var heightPixels = (int)(horizontalHeightDips * scaleFactor);

            _appBarData.uEdge = PInvoke.ABE_BOTTOM;
            _appBarData.rc.left = monLeft;
            _appBarData.rc.top = monBottom - heightPixels;
            _appBarData.rc.right = monRight;
            _appBarData.rc.bottom = monBottom;
        }
        else if (side == DockSide.Left)
        {
            var widthPixels = (int)(verticalWidthDips * scaleFactor);

            _appBarData.uEdge = PInvoke.ABE_LEFT;
            _appBarData.rc.left = monLeft;
            _appBarData.rc.top = monTop;
            _appBarData.rc.right = monLeft + widthPixels;
            _appBarData.rc.bottom = monBottom;
        }
        else if (side == DockSide.Right)
        {
            var widthPixels = (int)(verticalWidthDips * scaleFactor);

            _appBarData.uEdge = PInvoke.ABE_RIGHT;
            _appBarData.rc.left = monRight - widthPixels;
            _appBarData.rc.top = monTop;
            _appBarData.rc.right = monRight;
            _appBarData.rc.bottom = monBottom;
        }
        else
        {
            return;
        }
    }

    private LRESULT CustomWndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        // check settings changed
        if (msg == PInvoke.WM_SETTINGCHANGE)
        {
            if (wParam == (uint)SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA)
            {
                Logger.LogDebug($"WM_SETTINGCHANGE(SPI_SETWORKAREA)");

                // Use debounced call to throttle rapid successive calls
                DispatcherQueue.TryEnqueue(() => UpdateWindowPosition());
            }
        }
        else if (msg == PInvoke.WM_DISPLAYCHANGE)
        {
            Logger.LogDebug("WM_DISPLAYCHANGE");

            // Invalidate the monitor cache so DockWindowManager can reconcile
            _monitorService.NotifyMonitorsChanged();

            // Use dispatcher to ensure we're on the UI thread.
            // Refresh _targetMonitor before re-positioning: the MonitorInfo
            // captured at construction is an immutable record, so its Bounds
            // are stale after a topology change (e.g. an external display was
            // disconnected, shifting our monitor's virtual-screen origin).
            // Without this, UpdateAppBarDataForEdge would compute the AppBar
            // rect against the old coordinates and produce a wildly incorrect
            // size/position.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed)
                {
                    return;
                }

                RefreshTargetMonitor();
                UpdateWindowPosition();
            });
        }

        // Intercept WM_SYSCOMMAND to prevent minimize and maximize
        else if (msg == PInvoke.WM_SYSCOMMAND)
        {
            var command = (int)(wParam.Value & 0xFFF0);
            if (command == PInvoke.SC_MINIMIZE || command == PInvoke.SC_MAXIMIZE)
            {
                // Block minimize and maximize commands
                return new LRESULT(0);
            }
        }

        // Stop min/max on WM_WINDOWPOSCHANGING too
        else if (msg == PInvoke.WM_WINDOWPOSCHANGING)
        {
            unsafe
            {
                var pWindowPos = (WINDOWPOS*)lParam.Value;

                // Check if the window is being hidden (minimized) or if flags suggest minimize/maximize
                if ((pWindowPos->flags & SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW) != 0)
                {
                    // Prevent hiding the window (minimize)
                    pWindowPos->flags &= ~SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW;
                    pWindowPos->flags |= SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
                }

                // Additional check: if the window position suggests it's being minimized or maximized
                // by checking for dramatic size changes
                if (pWindowPos->cx <= 0 || pWindowPos->cy <= 0)
                {
                    // Prevent zero or negative size changes (minimize)
                    pWindowPos->flags |= SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
                }
            }
        }

        // Handle WM_SIZE to prevent minimize/maximize state changes
        else if (msg == PInvoke.WM_SIZE)
        {
            var sizeType = (int)wParam.Value;
            if (sizeType == PInvoke.SIZE_MINIMIZED || sizeType == PInvoke.SIZE_MAXIMIZED)
            {
                // Block the size change by not calling the original window procedure
                return new LRESULT(0);
            }
        }

        // Handle WM_SHOWWINDOW to prevent hiding (minimize)
        else if (msg == PInvoke.WM_SHOWWINDOW)
        {
            var isBeingShown = wParam.Value != 0;
            if (!isBeingShown)
            {
                // Prevent hiding the window
                return new LRESULT(0);
            }
        }

        // Handle double-click on title bar (non-client area)
        else if (msg == PInvoke.WM_NCLBUTTONDBLCLK)
        {
            var hitTest = (int)wParam.Value;
            if (hitTest == PInvoke.HTCAPTION)
            {
                // Block double-click on title bar to prevent maximize
                return new LRESULT(0);
            }
        }

        // Handle WM_GETMINMAXINFO to allow the dock to be smaller than
        // the default minimum window size (SM_CYMINTRACK ~36px).
        else if (msg == PInvoke.WM_GETMINMAXINFO)
        {
            // Call the original WndProc first so it fills default values,
            // then override the minimum tracking size.
            var result = PInvoke.CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
            unsafe
            {
                var minMaxInfo = (MINMAXINFO*)lParam.Value;
                minMaxInfo->ptMinTrackSize.X = 1;
                minMaxInfo->ptMinTrackSize.Y = 1;
            }

            return result;
        }

        // Handle the AppBarMessage message
        // This is needed to update the position when the work area changes.
        // (notably, when the user toggles auto-hide taskbars)
        else if (msg == _callbackMessageId)
        {
            if (wParam.Value == PInvoke.ABN_POSCHANGED)
            {
                UpdateWindowPosition();
            }
            else if (wParam.Value == PInvoke.ABN_FULLSCREENAPP)
            {
                _isFullScreenAppOpen = lParam != 0;
                UpdateTopmostState();
            }
        }
        // The system modal move loop (started when the user drags the floating
        // dock's grab handle) finished — decide whether to snap to an edge.
        else if (msg == PInvoke.WM_EXITSIZEMOVE)
        {
            if (IsFloatingPlacement)
            {
                DispatcherQueue.TryEnqueue(OnFloatingMoveFinished);
            }
        }
        else if (msg == WM_TASKBAR_RESTART)
        {
            Logger.LogDebug("WM_TASKBAR_RESTART");

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed)
                {
                    return;
                }

                // Floating and fit-to-content docks aren't app bars; just re-anchor them.
                if (IsFloatingPlacement)
                {
                    PositionFloatingDock();
                }
                else if (IsEdgeFitToContent)
                {
                    UpdateFloatingWindowPosition();
                }
                else
                {
                    CreateAppBar(_hwnd);
                }
            });

            WeakReferenceMessenger.Default.Send<BringToTopMessage>(new(false));
        }

        // Call the original window procedure for all other messages
        return PInvoke.CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
    }

    void IRecipient<BringToTopMessage>.Receive(BringToTopMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTopmostState(message.BringToFront);
        });
    }

    public void Receive(QuitMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_appBarData.hWnd != IntPtr.Zero)
            {
                DestroyAppBar(_hwnd);
            }

            this.Close();
        });
    }

    void IRecipient<RequestShowPaletteAtMessage>.Receive(RequestShowPaletteAtMessage message)
    {
        if (_isDisposed || message.OwnerHwnd != (nint)_hwnd)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => RequestShowPaletteOnUiThread(message.PosDips));
    }

    private void RequestShowPaletteOnUiThread(Point posDips)
    {
        // pos is relative to our root. We need to convert to absolute
        // virtual-screen coords.
        //
        // TransformToVisual(null) yields a point in the XamlRoot's coordinate
        // space (i.e. the window's client area in DIPs), NOT in screen space.
        // To get true screen coordinates we must offset by the window's
        // screen-space origin (GetWindowRect, which is in pixels). Without
        // this offset, X (for Top/Bottom docks) or Y (for Left/Right docks)
        // stays in window-local pixels and the palette ends up on the primary
        // monitor when the dock lives on a secondary monitor.
        var rootPosDips = Root.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var screenPosDips = new Point(rootPosDips.X + posDips.X, rootPosDips.Y + posDips.Y);

        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var scaleFactor = dpi / 96.0;
        PInvoke.GetWindowRect(_hwnd, out var ourRect);

        var screenPosPixels = new Point(
            ourRect.left + (screenPosDips.X * scaleFactor),
            ourRect.top + (screenPosDips.Y * scaleFactor));

        // Use monitor-specific bounds when available
        // Note: we compute the quadrant in monitor-local coordinates, but
        // keep screenPosPixels in absolute virtual-screen coordinates. Mixing
        // the two below (when only one axis is overridden from ourRect, which
        // is in virtual-screen coords) produced an off-screen final position
        // on secondary monitors.
        int screenWidth, screenHeight;
        double localX, localY;
        if (_targetMonitor is not null)
        {
            screenWidth = _targetMonitor.Bounds.Width;
            screenHeight = _targetMonitor.Bounds.Height;
            localX = screenPosPixels.X - _targetMonitor.Bounds.Left;
            localY = screenPosPixels.Y - _targetMonitor.Bounds.Top;
        }
        else
        {
            screenWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
            screenHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);
            localX = screenPosPixels.X;
            localY = screenPosPixels.Y;
        }

        // Now we're going to find the best position for the palette.

        // We want to anchor the palette on the dock side.
        // on the top:
        //   - anchor to the top, left if we're on the left half of the screen
        //   - anchor to the top, right if we're on the right half of the screen
        // On the left:
        //   - anchor to the top, left if we're on the top half of the screen
        //   - anchor to the bottom, left if we're on the bottom half of the screen
        // On the right:
        //   - anchor to the top, right if we're on the top half of the screen
        //   - anchor to the bottom, right if we're on the bottom half of the screen
        // On the bottom:
        //   - anchor to the bottom, left if we're on the left half of the screen
        //   - anchor to the bottom, right if we're on the right half of the screen
        var onTopHalf = localY < screenHeight / 2;
        var onLeftHalf = localX < screenWidth / 2;
        var onRightHalf = !onLeftHalf;
        var onBottomHalf = !onTopHalf;

        var anchorPoint = EffectiveSide switch
        {
            DockSide.Top => onLeftHalf ? AnchorPoint.TopLeft : AnchorPoint.TopRight,
            DockSide.Bottom => onLeftHalf ? AnchorPoint.BottomLeft : AnchorPoint.BottomRight,
            DockSide.Left => onTopHalf ? AnchorPoint.TopLeft : AnchorPoint.BottomLeft,
            DockSide.Right => onTopHalf ? AnchorPoint.TopRight : AnchorPoint.BottomRight,
            _ => AnchorPoint.TopLeft,
        };

        // we also need to slide the anchor point a bit away from the dock
        var paddingDips = 8;
        var paddingPixels = paddingDips * scaleFactor;

        // Depending on the side we're on, we need to offset differently
        switch (EffectiveSide)
        {
            case DockSide.Top:
                screenPosPixels.Y = ourRect.bottom + paddingPixels;
                break;
            case DockSide.Bottom:
                screenPosPixels.Y = ourRect.top - paddingPixels;
                break;
            case DockSide.Left:
                screenPosPixels.X = ourRect.right + paddingPixels;
                break;
            case DockSide.Right:
                screenPosPixels.X = ourRect.left - paddingPixels;
                break;
        }

        // Now that we know the anchor corner, and where to attempt to place it, we can
        // ask the palette to show itself there.
        WeakReferenceMessenger.Default.Send<ShowPaletteAtMessage>(new(screenPosPixels, anchorPoint));
    }

    public DockWindowViewModel WindowViewModel => _windowViewModel;

    public string? MonitorDeviceId => viewModel.MonitorDeviceId;

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private void DockWindow_Closed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    private void Cleanup()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _settingsService?.SettingsChanged -= SettingsChangedHandler;

        _dock.ResizeDragStarted -= Dock_ResizeDragStarted;
        _dock.ResizeDragDelta -= Dock_ResizeDragDelta;
        _dock.ResizeDragCompleted -= Dock_ResizeDragCompleted;
        _dock.ResizeDragReset -= Dock_ResizeDragReset;
        _dock.ContentLayoutChanged -= Dock_ContentLayoutChanged;
        _dock.MoveDragRequested -= Dock_MoveDragRequested;

        if (_autoHideTimer is not null)
        {
            _autoHideTimer.Stop();
            _autoHideTimer.Tick -= AutoHide_Tick;
            _autoHideTimer = null;
        }

        Activated -= DockWindow_Activated;
        _themeService.ThemeChanged -= ThemeService_ThemeChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);

        DisposeAcrylic();
        _windowViewModel.Dispose();

        // Remove our app bar registration
        if (_appBarData.hWnd != IntPtr.Zero)
        {
            DestroyAppBar(_hwnd);
        }

        // Unhook the window procedure
        ShowDesktop.RemoveHook();
    }
}

// Thank you to https://stackoverflow.com/a/35422795/1481137
internal static class ShowDesktop
{
    private const string WORKERW = "WorkerW";
    private const string PROGMAN = "Progman";

    private static WINEVENTPROC? _hookProc;
    private static IntPtr _hookHandle = IntPtr.Zero;

    public static void AddHook(Window window)
    {
        if (IsHooked)
        {
            return;
        }

        IsHooked = true;

        _hookProc = (WINEVENTPROC)WinEventCallback;
        _hookHandle = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, HMODULE.Null, _hookProc, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
    }

    public static void RemoveHook()
    {
        if (!IsHooked)
        {
            return;
        }

        IsHooked = false;

        PInvoke.UnhookWinEvent((HWINEVENTHOOK)_hookHandle);
        _hookProc = null;
        _hookHandle = IntPtr.Zero;
    }

    private static string GetWindowClass(HWND hwnd)
    {
        unsafe
        {
            fixed (char* c = new char[32])
            {
                _ = PInvoke.GetClassName(hwnd, (PWSTR)c, 32);
                return new string(c);
            }
        }
    }

    internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private static void WinEventCallback(
        HWINEVENTHOOK hWinEventHook,
        uint eventType,
        HWND hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType == PInvoke.EVENT_SYSTEM_FOREGROUND)
        {
            var @class = GetWindowClass(hwnd);
            var bringToFront = string.Equals(@class, WORKERW, StringComparison.Ordinal) || string.Equals(@class, PROGMAN, StringComparison.Ordinal);
            if (bringToFront)
            {
                Logger.LogDebug("ShowDesktop invoked. Bring us back");
            }

            WeakReferenceMessenger.Default.Send<BringToTopMessage>(new(bringToFront));
        }
    }

    public static bool IsHooked { get; private set; }
}

internal sealed record BringToTopMessage(bool BringToFront);

internal sealed record RequestShowPaletteAtMessage(Point PosDips, IntPtr OwnerHwnd);

internal sealed record ShowPaletteAtMessage(Point PosPixels, AnchorPoint Anchor);

internal enum AnchorPoint
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

#pragma warning restore SA1402 // File may only contain a single type
