# Quick Access Dock

A lightweight, always-on-top floating shortcut dock. Pin apps, files, folders, URLs, UNC
paths, and shell commands to a compact bar that floats anywhere on screen — or snaps flush to
any screen edge with auto-hide.

## Structure

| Project | Type | Description |
| --- | --- | --- |
| `QuickAccessDock` | .NET (WinForms) exe | The dock itself, shipped as `PowerToys.QuickAccessDock.exe`. Launched and stopped by the runner. |
| `QuickAccessDockModuleInterface` | C++ DLL | Implements `PowertoyModuleIface`; the runner loads it to enable/disable the module and read the GPO policy. |

## Lifecycle

- The runner starts `PowerToys.QuickAccessDock.exe --pid <runnerPid>` when the module is enabled.
- The dock exits when the runner process exits (parent-PID watch) or when the runner signals
  `CommonSharedConstants::QUICK_ACCESS_DOCK_EXIT_EVENT` (module disabled / PowerToys shutting down).
- Only one dock runs at a time (single-instance mutex).

## Settings

Settings are owned by the PowerToys Settings UI (Quick Access Dock page) and persisted to
`%LOCALAPPDATA%\Microsoft\PowerToys\QuickAccessDock\settings.json` in the standard PowerToys
envelope. The dock watches that file and live-reloads changes. The pinned shortcuts, window
placement, and snap edge are private runtime state kept next to it in `dock.json`.

Exposed settings: theme (system/light/dark), style, auto-hide + delay, snap threshold, and
website-icon sync.

## Features

- Shortcuts to apps, files, folders, URLs, UNC paths, and shell commands
- Drag-and-drop to add; drag tiles to reorder
- Edge snapping with auto-hide to a thin reveal notch
- Horizontal and vertical layouts, dynamic width, per-monitor DPI aware
- Windows 11 styling that honors the system accent color
- Favicon sync for URL shortcuts
- Keyboard support: `Insert` / `Ctrl+N` add, `Delete` remove, `F2` rename, tab/arrow navigation

Originally authored by ajaykontham (MIT); integrated into PowerToys as a module.
