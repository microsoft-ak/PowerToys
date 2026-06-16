# Floating Dock Standalone

This is a standalone WinForms build of Floating Dock. It does not load through
PowerToys Runner, does not use the PowerToys module interface, and stores its
own settings under:

```text
%LOCALAPPDATA%\FloatingDock
```

## Build

```powershell
dotnet build .\FloatingDock.Standalone.slnx -c Release
```

## Publish

```powershell
dotnet publish .\FloatingDock.Standalone.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

The published executable is written to:

```text
bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\FloatingDock.exe
```

## Store Packaging Next

The app is now separated from PowerToys at the process and settings level. The
next packaging step is to finish Partner Center identity, listing assets, and
certification.

An unsigned local MSIX can be produced with:

```powershell
.\Packaging\Build-Msix.ps1
```

The package is written to:

```text
artifacts\FloatingDock\FloatingDock-1.0.0.0-win-x64.msix
```
