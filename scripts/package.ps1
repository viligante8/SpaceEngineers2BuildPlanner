<#
.SYNOPSIS
    Build and package a Build Planner release.

.DESCRIPTION
    Produces a versioned zip containing the plugin and its MonoMod/Cecil dependencies, ready to
    attach to a GitHub release. Used both by a human and by .github/workflows/release.yml.

    Deliberately publishes to an isolated output directory rather than the project's own bin\.
    Two reasons:

      * bin\ is the directory players point -plugins: at, and the game holds BuildPlanner.dll open
        while it runs (MSB3021). A release build must not fail just because the game is open.
      * bin\ accumulates whatever previous debug builds left there. A release should be built from
        nothing.

    The published output contains NO Space Engineers assemblies: every game reference in the csproj
    is <Private>false</Private>, so Keen's DLLs are compile-time only and are never copied. Do not
    change that - redistributing them is not ours to do.

.PARAMETER Version
    Version for the assembly and the zip name, without a leading "v" (e.g. 1.2.0).
    Defaults to 0.0.0-dev for local builds.

.PARAMETER GameDir
    The game's Game2 directory, holding the assemblies this plugin compiles against.
    Falls back to $env:SE2_GAME_DIR, then to the default Steam location.

.PARAMETER OutputDir
    Where to write the zip. Defaults to dist\ at the repo root.

.PARAMETER SkipTests
    Skip the unit tests. The release workflow does not pass this.

.EXAMPLE
    .\scripts\package.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string]$Version = "0.0.0-dev",
    [string]$GameDir,
    [string]$OutputDir,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginProject = Join-Path $repoRoot "BuildPlanner\BuildPlanner.csproj"
$testProject = Join-Path $repoRoot "BuildPlanner.Tests\BuildPlanner.Tests.csproj"

if (-not $GameDir) {
    if ($env:SE2_GAME_DIR) {
        $GameDir = $env:SE2_GAME_DIR
    }
    else {
        $GameDir = "F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"
    }
}

if (-not (Test-Path $GameDir)) {
    # Named explicitly rather than letting MSBuild fail with a dozen unresolved-reference errors,
    # which do not mention the real cause.
    throw "Game directory not found: $GameDir`nThis project compiles against the shipped Space Engineers 2 assemblies. Set -GameDir or `$env:SE2_GAME_DIR."
}

if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot "dist" }

# Strip a leading "v" so a git tag can be passed through unchanged.
$Version = $Version -replace '^v', ''

Write-Host "Build Planner $Version"
Write-Host "  game:   $GameDir"
Write-Host "  output: $OutputDir"

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("buildplanner-pack-" + [Guid]::NewGuid().ToString("N"))
$publishDir = Join-Path $staging "publish"
$intermediate = Join-Path $staging "obj"

try {
    if (-not $SkipTests) {
        Write-Host "`n== tests =="
        # The tests reference the game assemblies too, so they need the same GameDir.
        #
        # OutputPath is redirected for the same reason as the publish below: the test project
        # references the plugin, so building it writes BuildPlanner\bin\BuildPlanner.dll - the file
        # the running game holds open. Without this the tests spend ten retries losing to MSB3026
        # whenever someone packages with the game up.
        & dotnet test $testProject `
            -c Release `
            -p:GameDir=$GameDir `
            -p:OutputPath="$intermediate\tests\" `
            --nologo
        if ($LASTEXITCODE -ne 0) { throw "Tests failed; not packaging." }
    }
    else {
        Write-Host "`n== tests skipped =="
    }

    Write-Host "`n== publish =="
    & dotnet publish $pluginProject `
        -c Release `
        -o $publishDir `
        -p:GameDir=$GameDir `
        -p:OutputPath="$intermediate\" `
        -p:Version=$Version `
        -p:InformationalVersion=$Version `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    # Guard against ever shipping Keen's assemblies. If a reference loses <Private>false</Private>
    # this catches it here rather than in a published release.
    $leaked = Get-ChildItem $publishDir -Filter *.dll |
        Where-Object { $_.Name -like "Game2.*" -or $_.Name -like "VRage.*" -or $_.Name -like "Avalonia.*" }
    if ($leaked) {
        throw "Refusing to package: game assemblies were copied into the output ($($leaked.Name -join ', ')). Check <Private>false</Private> on the csproj references."
    }

    if (-not (Test-Path (Join-Path $publishDir "BuildPlanner.dll"))) {
        throw "Refusing to package: BuildPlanner.dll is missing from the publish output."
    }

    Copy-Item (Join-Path $PSScriptRoot "INSTALL.txt") -Destination $publishDir

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    $zipPath = Join-Path $OutputDir "BuildPlanner-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

    $size = [math]::Round((Get-Item $zipPath).Length / 1KB)
    Write-Host "`n== packaged =="
    Get-ChildItem $publishDir | ForEach-Object { Write-Host ("  " + $_.Name) }
    Write-Host "`n$zipPath ($size KB)"

    # Consumed by the release workflow.
    if ($env:GITHUB_OUTPUT) {
        "zip=$zipPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "version=$Version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue }
}
