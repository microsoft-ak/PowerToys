// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using ManagedCommon;
using Microsoft.CmdPal.UI.ViewModels;
using Microsoft.CmdPal.UI.ViewModels.Dock;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CmdPal.UI.ViewModels.Services;
using Microsoft.CmdPal.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.Windows.Storage.Pickers;

namespace Microsoft.CmdPal.UI.Settings;

public sealed partial class DockSettingsPage : Page
{
    private readonly TaskScheduler _mainTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();

    internal SettingsViewModel ViewModel { get; }

    public List<DockBandSettingsViewModel> AllDockBandItems => GetAllBandSettings();

    public DockSettingsPage()
    {
        this.InitializeComponent();

        var themeService = App.Current.Services.GetService<IThemeService>()!;
        var topLevelCommandManager = App.Current.Services.GetService<TopLevelCommandManager>()!;
        var settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
        var monitorService = App.Current.Services.GetService<IMonitorService>();

        ViewModel = new SettingsViewModel(topLevelCommandManager, _mainTaskScheduler, themeService, settingsService, monitorService);

        // Initialize UI state
        InitializeSettings();
    }

    private void InitializeSettings()
    {
        // Initialize UI controls to match current settings
        DockPlacementComboBox.SelectedIndex = SelectedPlacementIndex;
        DockPositionComboBox.SelectedIndex = SelectedSideIndex;
        DockSizeComboBox.SelectedIndex = SelectedDockSizeIndex;
        DockLengthModeComboBox.SelectedIndex = SelectedLengthModeIndex;
        DockAlignmentComboBox.SelectedIndex = SelectedAlignmentIndex;
        BackdropComboBox.SelectedIndex = SelectedBackdropIndex;
        UpdateDockSizeCardVisibility();
        UpdateDockLengthCardsVisibility();
        UpdatePlacementCardsVisibility();
    }

    private async void PickBackgroundImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (XamlRoot?.ContentIslandEnvironment is null)
            {
                return;
            }

            var windowId = XamlRoot?.ContentIslandEnvironment?.AppWindowId ?? new Microsoft.UI.WindowId(0);

            var picker = new FileOpenPicker(windowId)
            {
                CommitButtonText = ViewModels.Properties.Resources.builtin_settings_appearance_pick_background_image_title!,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail,
            };

            string[] extensions = [".png", ".bmp", ".jpg", ".jpeg", ".jfif", ".gif", ".tiff", ".tif", ".webp", ".jxr"];
            foreach (var ext in extensions)
            {
                picker.FileTypeFilter!.Add(ext);
            }

            var file = await picker.PickSingleFileAsync()!;
            if (file != null)
            {
                ViewModel.DockAppearance.BackgroundImagePath = file.Path ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to pick background image file for dock", ex);
        }
    }

    private void OpenWindowsColorsSettings_Click(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        // LOAD BEARING (or BEAR LOADING?): Process.Start with UseShellExecute inside a XAML input event can trigger WinUI reentrancy
        // and cause FailFast crashes. Task.Run moves the call off the UI thread to prevent hard process termination.
        Task.Run(() =>
        {
            try
            {
                _ = Process.Start(new ProcessStartInfo("ms-settings:colors") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to open Windows Settings", ex);
            }
        });
    }

    // Property bindings for ComboBoxes
    public int SelectedDockSizeIndex
    {
        get => DockSizeToSelectedIndex(ViewModel.Dock_DockSize);
        set => ViewModel.Dock_DockSize = SelectedIndexToDockSize(value);
    }

    public int SelectedSideIndex
    {
        get => SideToSelectedIndex(ViewModel.Dock_Side);
        set
        {
            ViewModel.Dock_Side = SelectedIndexToSide(value);
            UpdateDockSizeCardVisibility();
        }
    }

    public int SelectedBackdropIndex
    {
        get => BackdropToSelectedIndex(ViewModel.Dock_Backdrop);
        set => ViewModel.Dock_Backdrop = SelectedIndexToBackdrop(value);
    }

    public int SelectedPlacementIndex
    {
        get => PlacementToSelectedIndex(ViewModel.Dock_Placement);
        set
        {
            ViewModel.Dock_Placement = SelectedIndexToPlacement(value);
            UpdatePlacementCardsVisibility();
        }
    }

    public int SelectedLengthModeIndex
    {
        get => LengthModeToSelectedIndex(ViewModel.Dock_LengthMode);
        set
        {
            ViewModel.Dock_LengthMode = SelectedIndexToLengthMode(value);
            UpdateDockLengthCardsVisibility();
        }
    }

    public int SelectedAlignmentIndex
    {
        get => AlignmentToSelectedIndex(ViewModel.Dock_Alignment);
        set => ViewModel.Dock_Alignment = SelectedIndexToAlignment(value);
    }

    public bool ShowLabels
    {
        get => ViewModel.Dock_ShowLabels;
        set => ViewModel.Dock_ShowLabels = value;
    }

    // Conversion methods for ComboBox bindings
    private static int DockSizeToSelectedIndex(DockSize size) => size switch
    {
        DockSize.Default => 0,
        DockSize.Compact => 1,
        _ => 0,
    };

    private static DockSize SelectedIndexToDockSize(int index) => index switch
    {
        0 => DockSize.Default,
        1 => DockSize.Compact,
        _ => DockSize.Default,
    };

    private static int SideToSelectedIndex(DockSide side) => side switch
    {
        DockSide.Left => 0,
        DockSide.Top => 1,
        DockSide.Right => 2,
        DockSide.Bottom => 3,
        _ => 1,
    };

    private static DockSide SelectedIndexToSide(int index) => index switch
    {
        0 => DockSide.Left,
        1 => DockSide.Top,
        2 => DockSide.Right,
        3 => DockSide.Bottom,
        _ => DockSide.Top,
    };

    private static int PlacementToSelectedIndex(DockPlacement placement) => placement switch
    {
        DockPlacement.Edge => 0,
        DockPlacement.Floating => 1,
        _ => 0,
    };

    private static DockPlacement SelectedIndexToPlacement(int index) => index switch
    {
        0 => DockPlacement.Edge,
        1 => DockPlacement.Floating,
        _ => DockPlacement.Edge,
    };

    private static int LengthModeToSelectedIndex(DockLengthMode mode) => mode switch
    {
        DockLengthMode.Full => 0,
        DockLengthMode.FitToContent => 1,
        _ => 0,
    };

    private static DockLengthMode SelectedIndexToLengthMode(int index) => index switch
    {
        0 => DockLengthMode.Full,
        1 => DockLengthMode.FitToContent,
        _ => DockLengthMode.Full,
    };

    private static int AlignmentToSelectedIndex(DockAlignment alignment) => alignment switch
    {
        DockAlignment.Start => 0,
        DockAlignment.Center => 1,
        DockAlignment.End => 2,
        _ => 1,
    };

    private static DockAlignment SelectedIndexToAlignment(int index) => index switch
    {
        0 => DockAlignment.Start,
        1 => DockAlignment.Center,
        2 => DockAlignment.End,
        _ => DockAlignment.Center,
    };

    private static int BackdropToSelectedIndex(DockBackdrop backdrop) => backdrop switch
    {
        DockBackdrop.Transparent => 0,
        DockBackdrop.Acrylic => 1,
        _ => 1,
    };

    private static DockBackdrop SelectedIndexToBackdrop(int index) => index switch
    {
        0 => DockBackdrop.Transparent,
        1 => DockBackdrop.Acrylic,
        _ => DockBackdrop.Acrylic,
    };

    private void UpdateDockSizeCardVisibility()
    {
        // Compact/default size is an edge-placement, Top/Bottom-only setting.
        var isEdge = ViewModel.Dock_Placement == DockPlacement.Edge;
        var side = ViewModel.Dock_Side;
        var isTopOrBottom = side == DockSide.Top || side == DockSide.Bottom;
        DockSizeSettingsCard.Visibility = (isEdge && isTopOrBottom) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDockLengthCardsVisibility()
    {
        // Length/alignment/reset only apply to edge placement in fit-to-content mode.
        var isEdge = ViewModel.Dock_Placement == DockPlacement.Edge;
        var isFitToContent = isEdge && ViewModel.Dock_LengthMode == DockLengthMode.FitToContent;
        var visibility = isFitToContent ? Visibility.Visible : Visibility.Collapsed;
        DockAlignmentSettingsCard.Visibility = visibility;
        DockResetLengthSettingsCard.Visibility = visibility;
    }

    private void UpdatePlacementCardsVisibility()
    {
        var isFloating = ViewModel.Dock_Placement == DockPlacement.Floating;

        // Auto-hide is floating-only; the edge-anchored sizing controls are
        // hidden when floating (the floating dock always fits its content).
        DockAutoHideSettingsCard.Visibility = isFloating ? Visibility.Visible : Visibility.Collapsed;

        var edgeOnly = isFloating ? Visibility.Collapsed : Visibility.Visible;
        DockLengthSettingsCard.Visibility = edgeOnly;

        UpdateDockSizeCardVisibility();
        UpdateDockLengthCardsVisibility();
    }

    private void ResetDockLength_Click(object sender, RoutedEventArgs e)
    {
        // Return the fit-to-content dock to automatic sizing.
        ViewModel.Dock_CustomLength = 0;
    }

    private List<TopLevelViewModel> GetAllBands()
    {
        var allBands = new List<TopLevelViewModel>();

        var tlcManager = App.Current.Services.GetService<TopLevelCommandManager>()!;

        foreach (var item in tlcManager.GetDockBandsSnapshot())
        {
            if (item.IsDockBand)
            {
                allBands.Add(item);
            }
        }

        return allBands;
    }

    private List<DockBandSettingsViewModel> GetAllBandSettings()
    {
        var allSettings = new List<DockBandSettingsViewModel>();

        // var allBands = GetAllBands();
        var tlcManager = App.Current.Services.GetService<TopLevelCommandManager>()!;
        var settingsModel = App.Current.Services.GetRequiredService<ISettingsService>().Settings;
        var settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
        var dockViewModel = App.Current.Services.GetService<DockViewModel>()!;
        var allBands = tlcManager.GetDockBandsSnapshot();
        foreach (var band in allBands)
        {
            var setting = band.DockBandSettings;
            if (setting is not null)
            {
                var bandVm = dockViewModel.FindBandByTopLevel(band);
                allSettings.Add(new(
                    dockSettingsModel: setting,
                    topLevelAdapter: band,
                    bandViewModel: bandVm,
                    settingsService: settingsService
                ));
            }
        }

        return allSettings;
    }
}
