# Floating Dock local run guide

Floating Dock is split into two projects:

- `FloatingDockModuleInterface`: native PowerToys module loaded by Runner.
- `FloatingDock`: WinForms helper process that renders the always-on-top dock.

The module interface starts `PowerToys.FloatingDock.exe` and passes the current Runner process id plus an exit event. The helper exits when Runner exits or when the module signals that event.

## Prerequisites

- Visual Studio 2022 17.4+ or Visual Studio 2026 with the PowerToys native and .NET workloads installed.
- Repo submodules initialized:

```powershell
git submodule update --init --recursive
```

- NuGet/project restore completed once:

```powershell
tools\build\build-essentials.cmd
```

If native builds fail with `MSB8040`, install the Visual Studio Spectre-mitigated C++ libraries. For local MVP validation only, the native projects can also be built with `/p:SpectreMitigation=false`.

## Build the MVP pieces

Run each command from the listed folder.

```powershell
cd C:\Git\PowerToys\src\modules\FloatingDock\FloatingDock
C:\Git\PowerToys\tools\build\build.cmd
```

```powershell
cd C:\Git\PowerToys\src\modules\FloatingDock\FloatingDockModuleInterface
C:\Git\PowerToys\tools\build\build.cmd
```

```powershell
cd C:\Git\PowerToys\src\settings-ui\Settings.UI.Library
C:\Git\PowerToys\tools\build\build.cmd
```

```powershell
cd C:\Git\PowerToys\src\settings-ui\Settings.UI
C:\Git\PowerToys\tools\build\build.cmd
```

```powershell
cd C:\Git\PowerToys\src\runner
C:\Git\PowerToys\tools\build\build.cmd
```

If the machine is missing Spectre-mitigated C++ libraries, use this temporary local validation form for native projects:

```powershell
C:\Git\PowerToys\tools\build\build.cmd /p:SpectreMitigation=false
```

Build logs are written next to each project. Check `build.debug.x64.errors.log` first on failure.

## Run through PowerToys

1. Exit any installed PowerToys instance from the tray icon.
2. Build the projects above.
3. Start the debug Runner from the repo output folder:

```powershell
cd C:\Git\PowerToys\x64\Debug
.\PowerToys.exe
```

4. Open PowerToys Settings.
5. Select `Floating Dock` in the left navigation.
6. Turn the module on.

The dock should appear as an always-on-top PC Manager-style toolbox: a compact dark rounded surface with a shortcut count puck, shortcut icons, an add button, and an overflow menu. Drag the dock body to move it, move it near a monitor working-area edge to snap, drop files/folders/shortcuts/URLs onto it to add launch shortcuts, and use right-click menus to rename, reorder, or remove items.

## If the dock does not appear

Check these in order:

1. Confirm you are running the repo build, not an installed PowerToys build:

```powershell
Get-Process PowerToys -ErrorAction SilentlyContinue | Select-Object Id,Path
```

The path should be under `C:\Git\PowerToys\x64\Debug`. If it is under `%LOCALAPPDATA%\PowerToys`, exit that instance before starting the repo build.

2. Confirm both Floating Dock binaries exist:

```powershell
Test-Path C:\Git\PowerToys\x64\Debug\PowerToys.FloatingDock.exe
Test-Path C:\Git\PowerToys\x64\Debug\PowerToys.FloatingDockModuleInterface.dll
```

3. Run the direct helper smoke test below. If the helper appears directly but not through PowerToys, inspect the module interface log under:

```text
%LOCALAPPDATA%\Microsoft\PowerToys\FloatingDock\ModuleInterface\Logs
```

4. Reset a saved off-screen position:

```powershell
Remove-Item "$env:LOCALAPPDATA\Microsoft\PowerToys\FloatingDock\dock.json" -ErrorAction SilentlyContinue
```

On next launch, the dock defaults to a visible top-right position on the primary monitor.

## Run the helper directly

For UI iteration, the helper can be launched without Runner. This does not exercise PowerToys module loading, but it does exercise dock rendering, drag/drop, persistence, snapping, and event-based shutdown.

```powershell
$eventName = "Local\PowerToys_FloatingDock_Dev_$PID"
$created = $false
$exitEvent = [System.Threading.EventWaitHandle]::new(
    $false,
    [System.Threading.EventResetMode]::ManualReset,
    $eventName,
    [ref]$created)

$proc = Start-Process `
    -FilePath "C:\Git\PowerToys\x64\Debug\PowerToys.FloatingDock.exe" `
    -ArgumentList @("--pid", $PID, "--exit-event", $eventName) `
    -PassThru
```

Signal the event to close the helper:

```powershell
$exitEvent.Set()
$proc.WaitForExit(5000)
$exitEvent.Dispose()
```

## Reset local dock data

The MVP stores settings and shortcut state under:

```text
%LOCALAPPDATA%\Microsoft\PowerToys\FloatingDock\settings.json
%LOCALAPPDATA%\Microsoft\PowerToys\FloatingDock\dock.json
```

Delete those files to reset the dock layout, position, and shortcuts.

## Quick smoke test

After building `FloatingDock`, this verifies that the helper starts and exits when its event is signaled:

```powershell
$eventName = "Local\PowerToys_FloatingDock_Smoke_$PID"
$created = $false
$exitEvent = [System.Threading.EventWaitHandle]::new($false, [System.Threading.EventResetMode]::ManualReset, $eventName, [ref]$created)
try {
    $proc = Start-Process `
        -FilePath "C:\Git\PowerToys\x64\Debug\PowerToys.FloatingDock.exe" `
        -ArgumentList @("--pid", $PID, "--exit-event", $eventName) `
        -WindowStyle Hidden `
        -PassThru

    Start-Sleep -Seconds 2
    $started = -not $proc.HasExited
    $exitEvent.Set() | Out-Null
    $exited = $proc.WaitForExit(5000)

    "started=$started exitedAfterSignal=$exited exitCode=$($proc.ExitCode)"
}
finally {
    $exitEvent.Dispose()
}
```

Expected result:

```text
started=True exitedAfterSignal=True exitCode=0
```

## Tests

Build and run the focused unit tests:

```powershell
cd C:\Git\PowerToys\src\modules\FloatingDock\FloatingDock.UnitTests
C:\Git\PowerToys\tools\build\build.cmd
```

```powershell
$vstest = "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
& $vstest "C:\Git\PowerToys\x64\Debug\tests\FloatingDock.UnitTests\net10.0-windows10.0.26100.0\PowerToys.FloatingDock.UnitTests.dll"
```

Build the fuzz target package:

```powershell
cd C:\Git\PowerToys\src\modules\FloatingDock\FloatingDock.FuzzTests
C:\Git\PowerToys\tools\build\build.cmd
```

Settings UI validation for this module lives in:

```text
src\settings-ui\Settings.UI.UnitTests\ViewModelTests\FloatingDock.cs
```
