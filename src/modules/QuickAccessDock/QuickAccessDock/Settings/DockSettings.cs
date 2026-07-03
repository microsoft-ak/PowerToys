// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

namespace QuickAccessDock;

internal sealed class DockSettings
{
    // The visual style (Default, Neomorphism). Each style ships a light
    // and a dark palette; Theme below selects which of the two is used.
    public DockThemeStyle Style { get; set; } = DockThemeStyle.Default;

    // Light/dark mode. System follows the current Windows app theme.
    public DockTheme Theme { get; set; } = DockTheme.System;

    public int SnapThreshold { get; set; } = 32;

    // Slide the dock off the snapped edge when it is idle, leaving only a reveal notch.
    public bool AutoHide { get; set; } = true;

    // How long (in milliseconds) the dock waits after the last interaction before auto-hiding.
    public int AutoHideDelayMs { get; set; } = 1000;

    // Update website shortcut favicons after opening the URL.
    public bool SyncWebsiteIconsAfterOpen { get; set; } = true;

    // Custom accent color override as "#RRGGBB". Null/empty uses the active style's
    // default accent.
    public string? AccentColor { get; set; }
}

