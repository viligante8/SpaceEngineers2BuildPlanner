# Releasing

## Cutting a release

```
git tag v1.0.0
git push origin v1.0.0
```

That triggers `.github/workflows/release.yml`, which runs the tests, publishes, packages
`dist/BuildPlanner-1.0.0.zip`, and attaches it to a GitHub release.

To rehearse without announcing anything, run the workflow manually from the Actions tab with a
version — a manual run publishes a **draft**.

To build a zip locally, without any of GitHub involved:

```powershell
.\scripts\package.ps1 -Version 1.0.0
```

It works with the game running: everything is built through a temporary directory, so nothing
touches `BuildPlanner\bin\`, the folder players point `-plugins:` at and the game holds open.

## Why the runner has to be self-hosted

**GitHub-hosted runners cannot build this project.** The plugin compiles against the shipped
Space Engineers 2 assemblies — `Game2.Client.dll`, `Game2.Simulation.dll`, `VRage.*.dll` and the rest
— referenced out of `$(GameDir)`. Those are Keen's proprietary binaries. They are not in this
repository and they must not be: redistributing them is not ours to do. No `windows-latest` runner
has Space Engineers 2 installed, so `dotnet build` there fails on every game reference before it
compiles a line.

So the release job runs on a machine that has the game.

The alternative — committing stripped reference assemblies generated from Keen's DLLs — would let a
hosted runner build, but they remain a derivative of proprietary binaries and there is no evidence
Keen permits redistributing them in any form. Not worth the risk for a hobby plugin.

### Setting up the runner

On a Windows machine with Space Engineers 2 installed:

1. Repository → Settings → Actions → Runners → **New self-hosted runner**, and follow the given
   steps.
2. Give it the labels the workflow asks for: `self-hosted`, `windows`, `se2`.
3. Install the [.NET 9 SDK](https://dotnet.microsoft.com/download) if it is not already there.
4. If the game is **not** at
   `F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2`, set a repository variable
   `SE2_GAME_DIR` (Settings → Secrets and variables → Actions → Variables) to the correct `Game2`
   directory.

The runner only needs to be online when a tag is pushed.

## What ships

`scripts/package.ps1` produces a zip holding the plugin, its MonoMod and Mono.Cecil dependencies,
the PDB (so a user's stack trace is readable), and `INSTALL.txt`.

**No game assemblies.** Every game reference in the csproj is `<Private>false</Private>`, so they are
compile-time only. The script checks the published output for anything named `Game2.*`, `VRage.*` or
`Avalonia.*` and refuses to package if one appears — that guard exists so a future edit dropping
`<Private>false</Private>` fails here rather than in a published release.

## Versioning

The tag is the version. `v1.2.0` builds `1.2.0`, stamps it into the assembly, and the plugin reports
it on the first line of its log:

```
BuildPlanner 1.2.0 initializing...
```

That line is the first thing to ask for in a bug report. This plugin binds to method signatures and
private field names, so knowing the build decides whether a failure is one already fixed or
something new.

A local build with no `-Version` reports `0.0.0-dev`.

## Before tagging

The unit tests are pure logic and the release runs them, but they cannot see the failures that
actually matter here — every serious bug in this project was a fact about the live engine. Load the
plugin and work through the test steps in `NEXT-SESSION.md` first, particularly after a game update.
