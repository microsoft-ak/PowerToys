param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0.0"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $projectRoot)
$projectFile = Join-Path $projectRoot "FloatingDock.Standalone.csproj"
$publishRoot = Join-Path $projectRoot "bin\$Configuration\net10.0-windows10.0.19041.0\$Runtime\publish"
$artifactsRoot = Join-Path $repoRoot "artifacts\FloatingDock"
$layoutRoot = Join-Path $artifactsRoot "msix-layout"
$assetsRoot = Join-Path $PSScriptRoot "Assets"
$manifestTemplate = Join-Path $PSScriptRoot "Package.appxmanifest"
$manifestOut = Join-Path $layoutRoot "AppxManifest.xml"
$packageOut = Join-Path $artifactsRoot "FloatingDock-$Version-$Runtime.msix"
$makeAppx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"

if (-not (Test-Path $makeAppx)) {
    throw "makeappx.exe was not found at '$makeAppx'. Install the Windows 10/11 SDK packaging tools."
}

dotnet publish $projectFile -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

if (Test-Path $layoutRoot) {
    Remove-Item -LiteralPath $layoutRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $layoutRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $layoutRoot "Assets") | Out-Null

Copy-Item -LiteralPath (Join-Path $publishRoot "FloatingDock.exe") -Destination (Join-Path $layoutRoot "FloatingDock.exe") -Force
Copy-Item -Path (Join-Path $assetsRoot "*") -Destination (Join-Path $layoutRoot "Assets") -Force

$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifest = $manifest.Replace('Version="1.0.0.0"', "Version=`"$Version`"")
[System.IO.File]::WriteAllText($manifestOut, $manifest, [System.Text.UTF8Encoding]::new($false))

if (Test-Path $packageOut) {
    Remove-Item -LiteralPath $packageOut -Force
}

& $makeAppx pack /d $layoutRoot /p $packageOut /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx failed with exit code $LASTEXITCODE."
}

Write-Host $packageOut
