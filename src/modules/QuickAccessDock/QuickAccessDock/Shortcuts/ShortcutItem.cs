// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;

namespace QuickAccessDock;

internal sealed class ShortcutItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    public string IconPath { get; set; } = string.Empty;

    public ShortcutKind Kind { get; set; } = ShortcutKind.File;
}

