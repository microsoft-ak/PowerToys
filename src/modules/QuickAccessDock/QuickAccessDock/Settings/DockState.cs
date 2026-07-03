// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System.Collections.Generic;

namespace QuickAccessDock;

internal sealed class DockState
{
    public int Left { get; set; } = 80;

    public int Top { get; set; } = 120;

    public string? MonitorDeviceName { get; set; }

    public string SnapEdge { get; set; } = "None";

    public List<ShortcutItem> Shortcuts { get; set; } = new();
}

