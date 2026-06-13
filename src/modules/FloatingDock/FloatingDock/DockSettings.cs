// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class DockSettings
{
    public bool StartExpanded { get; set; } = true;

    public bool ShowLabels { get; set; } = true;

    public int SnapThreshold { get; set; } = 32;
}
