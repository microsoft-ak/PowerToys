// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace FloatingDock;

internal sealed class DockSettings
{
    public int SnapThreshold { get; set; } = 32;

    // Slide the dock off the snapped edge when it is idle, leaving only a reveal notch.
    public bool AutoHide { get; set; } = true;

    // How long (in milliseconds) the dock waits after the last interaction before auto-hiding.
    public int AutoHideDelayMs { get; set; } = 1000;
}
