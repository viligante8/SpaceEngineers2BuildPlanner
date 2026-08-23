<#
.SYNOPSIS
    Build and package a Build Planner release.

.DESCRIPTION
    Produces a versioned zip containing the plugin and its MonoMod/Cecil dependencies, and
    optionally creates the GitHub release with it attached.

    Releases are cut from a developer's machine, not CI. The plugin compiles against the shipped
    Space Engineers 2 assemblies, which are Keen's and are not in this repository, so a
    GitHub-hosted runner cannot build the project at all - see RELEASING.md.

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
    Skip the unit tests. Do not use this for a release.

.NOTES
    The published output contains no debug symbols and no absolute build paths. Both are checked
    after publishing and the script refuses to package if either reappears.

.PARAMETER Publish
    After packaging, create the GitHub release and attach the zip, using the gh CLI.
    Creates a draft unless -Final is also passed, so there is always a chance to look first.

.PARAMETER Final
    With -Publish, publish the release immediately instead of leaving it as a draft.

.EXAMPLE
    .\packaging\package.ps1 -Version 1.0.0
    Build a zip into dist\ and stop. Nothing leaves your machine.

.EXAMPLE
    .\packaging\package.ps1 -Version 1.0.0 -Publish
    Build, then create a DRAFT GitHub release with the zip attached.
#>
[CmdletBinding()]
param(
    [string]$Version = "0.0.0-dev",
    [string]$GameDir,
    [string]$OutputDir,
    [switch]$SkipTests,
    [switch]$Publish,
    [switch]$Final
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

    # Debug symbols are NOT shipped.
    #
    # A .pdb embeds the machine's absolute source paths and a SourceLink document map, so shipping
    # one publishes the developer's Windows user name to every downloader. The symbols are of no use
    # to a player either - they cannot rebuild the project without Keen's assemblies. Anyone who
    # needs a mapped stack trace can build the same tagged commit and get a matching pdb locally.
    Get-ChildItem $publishDir -Filter *.pdb | Remove-Item -Force

    Copy-Item (Join-Path $PSScriptRoot "INSTALL.txt") -Destination $publishDir
    # MIT requires the notice to travel with the binaries we bundle.
    Copy-Item (Join-Path $PSScriptRoot "THIRD-PARTY-NOTICES.txt") -Destination $publishDir

    # Guard against absolute build paths coming back by any route.
    #
    # Runs AFTER the payload files are copied, so everything that actually ships is scanned - an
    # earlier revision sat above the Copy-Item calls and never looked at INSTALL.txt or
    # THIRD-PARTY-NOTICES.txt at all.
    #
    # Matches a path SHAPE, not the bare user name. Both text files legitimately contain the
    # GitHub URL github.com/viligante8/..., which contains this developer's user name as a
    # substring - a `.Contains($env:USERNAME)` test fails every release on those two files while
    # reporting a <PathMap> problem that does not exist.
    #
    # Reads bytes as Latin-1 so every byte maps to a character: ASCII decoding turns bytes >= 0x80
    # into '?', which would hide a non-ASCII user name. UTF-16 literals are scanned by stripping
    # interleaved nulls.
    # [char]92 is a backslash. Written this way so the pattern cannot be mangled by a tool
    # that rewrites escape sequences on its way into this file.
    $bs = [string][char]92
    $pathPattern = '[A-Za-z]:' + [regex]::Escape($bs) + '(Users|home)' + [regex]::Escape($bs)
    $latin1 = [Text.Encoding]::GetEncoding(28591)

    $leakedPaths = Get-ChildItem $publishDir -File -Recurse | Where-Object {
        $text = $latin1.GetString([IO.File]::ReadAllBytes($_.FullName))
        ($text -match $pathPattern) -or (($text -replace "`0", '') -match $pathPattern)
    }

    if ($leakedPaths) {
        throw "Refusing to package: absolute build paths are embedded in $($leakedPaths.Name -join ', '). Check <PathMap> in BuildPlanner.csproj."
    }

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    $zipPath = Join-Path $OutputDir "BuildPlanner-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

    $size = [math]::Round((Get-Item $zipPath).Length / 1KB)
    Write-Host "`n== packaged =="
    Get-ChildItem $publishDir | ForEach-Object { Write-Host ("  " + $_.Name) }
    Write-Host "`n$zipPath ($size KB)"

    if ($Publish) {
        Write-Host "`n== publish to GitHub =="

        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            throw "The gh CLI is not on PATH. Install GitHub CLI, or attach $zipPath to a release by hand."
        }

        $tag = "v$Version"

        # Reuse the tag if it already exists; gh creates it from HEAD otherwise. Checked up front so
        # a mistyped version does not silently tag the wrong commit.
        $existingTag = (& git tag --list $tag) -join ''
        if ($existingTag) {
            Write-Host "  using existing tag $tag"
        }
        else {
            Write-Host "  $tag does not exist yet; it will be created from the current commit"
        }

        $ghArgs = @(
            "release", "create", $tag, $zipPath,
            "--title", "Build Planner $Version",
            "--generate-notes"
        )

        # Draft by default. Publishing a game plugin is hard to take back once people have it, so
        # the default leaves it reviewable and -Final is an explicit choice.
        if (-not $Final) { $ghArgs += "--draft" }

        & gh @ghArgs
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }

        if ($Final) { Write-Host "`nPublished $tag." }
        else { Write-Host "`nDrafted $tag. Review it on GitHub, then publish when you are happy." }
    }
    else {
        Write-Host "`nNot published. Attach it with:"
        Write-Host "  gh release create v$Version `"$zipPath`" --title `"Build Planner $Version`" --generate-notes --draft"
    }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue }
}
