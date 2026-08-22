# The shipped MCP/HTTP control server — investigated, NOT usable (2026-08-22)

SE2 ships `Keen.Game2.AutoTests.MCP.McpServerComponent` in `Game2.AutoTests.dll` (v2.4.0.86), an
HTTP control server on `localhost:29150` that Keen built for automated testing. It exposes
`/query` (arbitrary C# against the live session), `/getEntities`, `/screenshot`, `/loadWorld` and a
debug-menu bridge — which would remove almost all of this project's verification cost.

**It could not be activated. Both documented routes were tried in game and both failed.**

## Attempt 1: `-MCP` (and `-loadScripts`)

```
-plugins:...\BuildPlanner.dll -MCP -loadScripts
```

Confirmed passed — the game log's "Running application with arguments" line shows both flags. Port
29150 was polled for five minutes while the game ran: never opened, connection refused throughout.

`McpServerComponent` derives from **`EngineComponent`**, not `SessionComponent`, so it is
constructed at engine startup — loading a world would not start it later. `Game2.AutoTests` is also
absent from `SpaceEngineers2.deps.json`, so the assembly is most likely never loaded at all, and the
flag has nothing to act on.

## Attempt 2: loading the assembly as a plugin

`Game2.AutoTests.dll` does contain a valid entry point — `Keen.Game2.AutoTests.AutoTest : IPlugin`
with `ctor(PluginHost)` — and `PluginHost.LoadPlugins` splits `-plugins:` on **`;`** (decompiled), so
several plugins can be listed:

```
-plugins:...\BuildPlanner.dll;F:\...\Game2\Game2.AutoTests.dll -MCP -loadScripts
```

The arguments arrive intact, but the game **crashes deterministically at startup**:

```
System.IO.DirectoryNotFoundException: Could not locate project TestData
   at Keen.Game2.Game.Helpers.GameContent.GetProjectByName(String projectName)
   at Keen.Game2.AutoTests.AutoTest.<>c.<.ctor>b__9_3(List`1 additionalProjects)
   at Keen.VRage.Core.Plugins.PluginHost.InvokeOnBeforeProjectsLoaded(List`1 pluginsProjects)
   at Keen.Game2.GameApp.CreateEngine(String[] args)
```

`AutoTest`'s constructor registers an `OnBeforeProjectsLoaded` callback requiring a content project
named `TestData`, which the retail install does not ship. The crash happens before the engine is
created, so nothing else in the process gets a chance to run.

## Verdict

Not usable without Keen's internal test content. **Do not retry either route.**

The only untested idea is fabricating a `TestData` content project so `GetProjectByName` succeeds —
speculative, unknown required contents, and it would run Keen's autotest harness against a real save.
Not attempted deliberately.

Useful facts salvaged regardless:

- `-plugins:` accepts several assemblies separated by `;` (from `PluginHost.LoadPlugins`).
- A plugin whose constructor throws takes the whole game down at startup — plugin constructors run
  inside `CreateEngine`, before any crash isolation.
