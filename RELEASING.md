# Releasing

Releases are cut from a developer's machine. There is no CI.

## Cutting a release

```powershell
.\scripts\package.ps1 -Version 1.0.0 -Publish
```

That runs the unit tests, publishes, packages `dist\BuildPlanner-1.0.0.zip`, and creates a **draft**
GitHub release `v1.0.0` with the zip attached. Look it over on GitHub, then hit publish.

Add `-Final` to publish immediately instead of drafting. The default is a draft on purpose: a game
plugin is hard to take back once people have downloaded it.

To build a zip and nothing else — nothing leaves your machine:

```powershell
.\scripts\package.ps1 -Version 1.0.0
```

It prints the `gh release create` line to run if you change your mind.

Both work **with the game running**. Everything is built through a temporary directory, so nothing
touches `BuildPlanner\bin\` — the folder players point `-plugins:` at, which the game holds open.

### Tags

`-Publish` uses the tag `v<version>`. If it already exists, it is reused; if not, `gh` creates it
from the current commit. Tag deliberately first if you want it on something other than `HEAD`:

```
git tag v1.0.0 <sha> && git push origin v1.0.0
```

## Why there is no CI

**A GitHub-hosted runner cannot build this project.** The plugin compiles against the shipped
Space Engineers 2 assemblies — `Game2.Client.dll`, `Game2.Simulation.dll`, `VRage.*.dll` and the rest
— referenced out of `$(GameDir)`. Those are Keen's proprietary binaries. They are not in this
repository and must not be: redistributing them is not ours to do. No `windows-latest` runner has
Space Engineers 2 installed, so `dotnet build` there fails on every game reference.

The ways around that were all worse than building locally:

- **A self-hosted runner** — works, but it means keeping a machine registered and online as CI for a
  release that happens rarely and takes nine seconds by hand.
- **Committing stripped reference assemblies** generated from Keen's DLLs — would let a hosted
  runner build, but they are still derived from proprietary binaries and there is no evidence Keen
  permits redistributing them in any form.

Releasing is not the slow part of this project. Loading the game and testing is.

## What ships

The zip holds the plugin, its MonoMod and Mono.Cecil dependencies, the PDB (so a user's stack trace
is readable), `INSTALL.txt`, and `THIRD-PARTY-NOTICES.txt`.

**No game assemblies.** Every game reference in the csproj is `<Private>false</Private>`, so they are
compile-time only. The script checks the published output for anything named `Game2.*`, `VRage.*` or
`Avalonia.*` and refuses to package if one appears — that guard exists so a future edit dropping
`<Private>false</Private>` fails here rather than in a published release.

MonoMod and Mono.Cecil are both MIT, which is why the notices file travels with them.

## Versioning

The version is stamped into the assembly and reported on the plugin's first log line:

```
BuildPlanner 1.0.0 initializing...
```

That is the first thing to ask for in a bug report. This plugin binds to method signatures and
private field names, so knowing the build is what separates a failure already fixed from a new one.

A local build with no `-Version` reports `0.0.0-dev`.

## Before releasing

The unit tests are pure logic and the script runs them, but they cannot see the failures that
actually matter here — every serious bug in this project was a fact about the live engine, and all
of them would have passed a green suite. Load the plugin and work through the test steps in
`NEXT-SESSION.md` first, especially after a game update.
