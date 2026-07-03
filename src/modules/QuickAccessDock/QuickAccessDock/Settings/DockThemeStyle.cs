// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

namespace QuickAccessDock;

/// <summary>
/// The visual treatment applied to the dock surface, tiles and chrome. Each style
/// has its own light and dark palette (the light/dark choice lives in
/// <see cref="DockTheme"/>); together they form the two axes of the dock's look.
/// </summary>
internal enum DockThemeStyle
{
    /// <summary>The clean, solid Windows 11 / Fluent surface (the original look).</summary>
    Default,

    /// <summary>Soft UI: a monochrome surface with paired light/dark shadows so tiles look extruded.</summary>
    Neomorphism,

    /// <summary>
    /// Blurred glass: a tint-controlled acrylic blur-behind material, frosting whatever sits
    /// behind the dock with a low-alpha light or dark tint so it stays clearly see-through.
    /// The body renders as true-transparent pixels so the blur shows through; tiles sit
    /// directly on the glass with only a highlight on hover (no resting chip). Falls back to
    /// an opaque frosted surface on builds without acrylic support (pre-Win10 1803).
    /// </summary>
    Acrylic,
}
