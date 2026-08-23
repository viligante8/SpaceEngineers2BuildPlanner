<#
.SYNOPSIS
    Build the plugin and install it where the game loads it from.

.DESCRIPTION
    The development loop is: change code, get it into the folder that -plugins: points at, relaunch.
    Doing that by hand means packaging, finding the zip, extracting it over the right directory and
    remembering that the game holds the DLL open. This does all of it.

    Deliberately builds through packaging\package.ps1 rather than copying out of BuildPlanner\bin.
    That is the same path a release takes, so what gets tested is what would ship - including the
    guard that refuses to package absolute build paths, and the absence of debug symbols.

.PARAMETER InstallDir
    Where the game loads the plugin from. Defaults to $env:SE2_PLUGIN_DIR, then C:\BuildPlanner.

.PARAMETER Version
    Version to stamp. Defaults to 0.0.0-dev, which is how the log tells a local build from a release.

.PARAMETER SkipTests
    Skip the unit tests. Fine for a quick iteration, not for anything you intend to keep.

.PARAMETER ClearLog
    Move the current log aside first, so the next run's log contains only that run.

.EXAMPLE
    .\packaging\install.ps1
    Build, test, install to C:\BuildPlanner. Then launch the game.

.EXAMPLE
    .\packaging\install.ps1 -SkipTests -ClearLog
    Fastest loop: no tests, and a clean log to read afterwards.
#>
[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$Version = "0.0.0-dev",
    [switch]$SkipTests,
    [switch]$ClearLog
)

$ErrorActionPreference = "Stop"

if (-not $InstallDir) {
    if ($env:SE2_PLUGIN_DIR) { $InstallDir = $env:SE2_PLUGIN_DIR }
    else { $InstallDir = "C:\BuildPlanner" }
}

# Checked up front rather than letting Expand-Archive fail halfway through and leave a half-updated
# folder, which is worse than not starting: some DLLs new, some old, and no obvious sign of it.
if (Get-Process SpaceEngineers2 -ErrorAction SilentlyContinue) {
    throw "Space Engineers 2 is running and holds the plugin open. Close it and run this again."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $env:APPDATA "SpaceEngineers2\BuildPlanner"
$log = Join-Path $logDir "BuildPlanner.log"

Write-Host "Build Planner -> $InstallDir"

$packageArgs = @{ Version = $Version }
if ($SkipTests) { $packageArgs.SkipTests = $true }
& (Join-Path $PSScriptRoot "package.ps1") @packageArgs
if ($LASTEXITCODE -ne 0) { throw "Packaging failed; nothing installed." }

$zip = Join-Path $repoRoot "dist\BuildPlanner-$Version.zip"
if (-not (Test-Path $zip)) { throw "Expected $zip and it is not there." }

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Expand-Archive -Path $zip -DestinationPath $InstallDir -Force

# Confirm on the bytes rather than the version string: a local build and the release it came from
# can carry the same version, so only a hash says whether the new code is actually in place.
$installed = Get-FileHash (Join-Path $InstallDir "BuildPlanner.dll") -Algorithm SHA256
$staging = Join-Path ([IO.Path]::GetTempPath()) ("bp-verify-" + [Guid]::NewGuid().ToString("N"))
Expand-Archive -Path $zip -DestinationPath $staging
$expected = Get-FileHash (Join-Path $staging "BuildPlanner.dll") -Algorithm SHA256
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

if ($installed.Hash -ne $expected.Hash) {
    throw "Installed BuildPlanner.dll does not match the package. Something else is writing to $InstallDir."
}

if ($ClearLog -and (Test-Path $log)) {
    $aside = Join-Path $logDir ("BuildPlanner.log.previous")
    if (Test-Path $aside) { Remove-Item $aside -Force }
    Move-Item $log $aside
    Write-Host "  log moved aside to BuildPlanner.log.previous"
}

Write-Host ""
Write-Host "Installed $Version, verified by hash."
Write-Host "  launch option: -plugins:$InstallDir\BuildPlanner.dll"
Write-Host "  log:           $log"
