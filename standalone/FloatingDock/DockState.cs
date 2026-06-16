// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace FloatingDock;

internal sealed class DockState
{
    public int Left { get; set; } = 80;

    public int Top { get; set; } = 120;

    public string? MonitorDeviceName { get; set; }

    public string SnapEdge { get; set; } = "None";

    public List<ShortcutItem> Shortcuts { get; set; } = new();
}
