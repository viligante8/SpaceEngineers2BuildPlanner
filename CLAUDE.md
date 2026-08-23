# Build Planner — project guide

A Space Engineers 2 **plugin** (`-plugins:`), written in C# against the shipped VRAGE3 assemblies
and loaded through Keen's plugin host. It reproduces SE1's Build Planner: queue blocks, withdraw
exactly the components you are missing, produce what you cannot find.

**This repository contains no data-mod files** — no `.def`, no `.partialdef`, no GUIDs of our own.
It is code, and the failure modes are runtime ones.

General SE2 modding guidance — the XML doc corpus, the vanilla definition corpus, the VRAGE3 data
model, and the evidence hierarchy — lives one directory up, in `../CLAUDE.md`, outside this
repository. Read it for anything touching game *data*. What follows is what governs work *here*.

## The short version

- The shipped assemblies and the vanilla corpus outrank every prose source, this file included.
- Never invent a method name, a field name, or a private member. Check it against
  `$GAME/Game2/*.xml` or the assembly metadata first — they are on disk, and checking costs seconds.
- A tool failing to answer is not evidence about the answer.
- Integration failures dominate. A green test suite is not a working plugin; see "Testing Policy".

---

# Repo Conventions

This project is a git repository published at
`https://github.com/viligante8/SpaceEngineers2BuildPlanner`, MIT licensed (`LICENSE`).

It holds one deliverable: the **Build Planner** plugin (`BuildPlanner/`), with unit tests in
`BuildPlanner.Tests/`, engine findings in `notes/`, manual test steps in `notes/in-game-tests.md`,
and release instructions in `RELEASING.md`. `README.md` at the root is the public front page.

**Never put an absolute path containing a user name into a tracked file or a shipped binary.** A
released `.pdb` and an un-mapped DLL both published this machine's user name to every downloader
once already; `<PathMap>` in the csproj and a guard in `packaging/package.ps1` now prevent it, and the
docs use `%USERPROFILE%` or a generic `C:\SE2Mods\` example.

**Space Engineers 2 has no multiplayer.** Do not write "unverified in multiplayer", "on a dedicated
server", or "in single player" — the first two describe something that does not exist, and the third
implies a contrast with it. The client/server split inside the engine is real and important
(`notes/client-server-split.md`), but both halves run in-process, always.

Do not commit, publish to the Workshop, or otherwise push anything outward without being asked.

**No AI attribution, ever, anywhere in this repo.** No `Co-Authored-By: Claude`, no "Generated with
Claude Code", no mention of AI in commit messages, code comments, or docs. This overrides the default
Claude Code commit footer. Write commits as the author would.

---

# Debugging Runtime Code: Log Every Branch, Verify The Object First

These rules were paid for during the Build Planner plugin. Each cost multiple game restarts.

## A silent code path is a broken code path

**Every early `return` in a handler must say why it returned** — not just error paths, the
"nothing to do" paths too.

The failure mode is specific and expensive: a keypress produced *no log line at all*, which looks
identical to "the key never reached my code". Several restarts went into hunting an input-routing
bug (context eviction, layer conflicts, key contention) before enabling the engine's own input trace
proved the key had been dispatching correctly the entire time. The handler was running, hitting
`if (destination == null) return;`, and vanishing without a word.

```csharp
// WRONG - indistinguishable from "never called"
if (destination == null) return;

// RIGHT
if (destination == null) { _notifier.Warning("could not find your inventory"); return; }
```

Silence is the most misleading signal in this project because it is consistent with *every*
hypothesis. Make it impossible.

## Verify the object before fixing what you do on it

When a lookup returns null, **dump what you are actually holding before theorising about why the
lookup failed.**

Concretely: `TryGet<InventoryComponent>()` on the "character" returned null. The plausible cause —
correct in general — is that a character carries three `InventoryComponent`s bound to tag slots
(`Inventory`, `ConsumableInventory`, `DatapadInventory`), so an untagged lookup cannot disambiguate.
That fix was implemented, deployed, and changed nothing, because the real problem was that the entity
was not the character at all.

The tell was already in the log and was not acted on: `no BlockPlacerEntityComponent on character`
had been printing on *every* right-click. Two components that must exist on a player character, both
absent, is one bug — the wrong entity — not two independent lookup bugs.

Before fixing a failed lookup, log the target's identity and component list:

```csharp
Log.Write($"  debug: no InventoryComponent on '{DescribeEntity(entity)}'");
```

`Entity.DebugName` and `Entity.Components` (an `ImmutableArray<Component>`) are both public.

**Two failed lookups on the same object are evidence about the object, not about the lookups.**

## Use the engine's own diagnostics before building a theory

`ActionProcessorDebugObject.DetailedInputLog` makes `GameInputProcessorComponent` log every control
it consumes or discards, and why, into the game log:

```
[Input][#1721]: Consuming input Keyboard::N in layer #36
[Input][#1721]: Control Keyboard::N : BuildPlannerWithdraw activated with state Start.
[Input][#2244]: Discard candidate control Keyboard::Escape, input Keyboard::Escape already consumed.
```

That output settled in one test what several rounds of decompiling and inference had got wrong.
Reach for engine-side tracing early — the engine knows what it did.

## Do not put a plugin log where the game prunes it

`%APPDATA%\SpaceEngineers2\Temp\Logs\` is **cleared on startup**. A log written there is destroyed by
the next launch, including the log of the run being diagnosed. Use a sibling directory
(`%APPDATA%\SpaceEngineers2\BuildPlanner\`) so history survives, which also lets the user run several
tests before anything is read.

## Reflection lookups: expect ambiguity

`GetMethod`/`GetProperty` by name throws `AmbiguousMatchException` when a member is overloaded or
redeclared in a derived class. Both bit this project:

- `GameEntityExtensions.GetSession` — non-generic `GetSession(Entity)` plus generic
  `GetSession<T>(Entity)`. Filter with `!IsGenericMethodDefinition` and an exact parameter match.
- `GameInputProcessorComponent.DebugObject` — redeclared with a covariant return type. Walk the type
  hierarchy with `BindingFlags.DeclaredOnly` and take the most-derived.

Prefer a direct compiled call whenever the type is public — the compiler checks it and it cannot
drift silently.

---

# SE2 Code Mods Via Plugins — Confirmed Working

**This is the mechanism this project runs on.** In-game scripting (the programmable-block sandbox)
is still unfinished, but **arbitrary C# runs today via the plugin system**, verified end to end on
this machine. Public prose still says code mods are unsupported; it is out of date.

```
SpaceEngineers2.exe -plugins:C:\path\to\YourPlugin.dll
```

- Entry point: implement `Keen.VRage.Core.Plugins.IPlugin`. `PluginHost` instantiates via
  `Activator.CreateInstance(pluginType, this)`, falling back to a parameterless constructor — provide
  both.
- Lifecycle: `PluginHost.OnBeforeEngineInstantiated(EngineBuilder)` and `OnBeforeProjectsLoaded`.
- Patching: MonoMod `RuntimeDetour` — `new Hook(methodInfo, replacementDelegate)`.
- csproj: `net9.0`, `<EnableDynamicLoading>true`, `<Reference>` each `Game2\*.dll` with
  `<Private>false</Private>`.
- `-loadScripts` is **not** required for a plugin; that flag is for in-game scripting.

## You cannot register a new Component type from a plugin

`EngineBuilder.Add<MyComponent>()` calls `RuntimeComponentInfo.For(type)`, resolving through
`MetadataManager.GetActiveContext()`. That context is built once from the **entry assembly**
(`MetadataManager.InitializeWithEntryAssembly`); a dynamically loaded plugin assembly is not in it,
so the lookup returns null and `Add` throws `NullReferenceException` inside `CreateIfNeeded`.

Attach to a component the engine already knows: detour a suitable existing method (for input work,
`InputGameComponent.Init`) and hang the behaviour off that.

## Input system facts

- **One context per named layer.** `GameInputProcessorComponent.ActivateContext` deactivates whatever
  occupies a named layer and takes its place. Reusing a vanilla `InputContextDefinition` (which
  carries a `Layer`) makes mod and game evict each other — the symptom is a handler that binds
  without error and never fires. Construct a **layer-less** context instead:
  `new InputContextDefinition(actions)` appends to `_activeContexts` and coexists.
- **An input is consumed once per frame.** `DisambiguatingControlActivationFilter` logs
  "Discard candidate control ..., input already consumed"; `ProcessActionsPerContext` assigns each
  control to exactly one context. Sharing a key with a vanilla action is a race, not coexistence —
  pick an unbound key. (`Mouse::Middle` is bound in vanilla to `ToolTertiary`, `PaintBlock` and
  `ToggleGridFollowing`; `Keyboard::N` has zero references.)
- **`ControlCustomizationEngineComponent` owns the mapping.** It keeps `_baseMappings` and rebuilds
  the processor mapping from it whenever custom binds change, silently discarding anything added
  straight to the processor. The game log shows this as `228 Mapping added` followed by
  `227 Mapping added`. Hook its `SetMapping` and inject into the mapping it is about to publish.
- **Appearing in Options -> Controls:** `ControlCustomizationViewModel` builds from
  `mapping.ControlsPerAction`, drops actions whose `Category` is null or the hidden category, and
  orders groups by `ActionCategoryConfiguration.OrderedControlCategories`. Give the action a `Name`
  (`StringId`) and a real `Category` — vanilla "BuildingControls" is
  `480bde0d-9a98-48fb-bffb-40cc0e156c30`. Both are `private set`; set the backing fields
  (`<Name>k__BackingField`, `<Category>k__BackingField`) by reflection.
- **Timing matters:** the controls menu is populated during startup, *before*
  `InputGameComponent.Init`. An action created at attach time is too late to appear — create it on
  demand inside the `SetMapping` hook.

## Reaching the world session from outside a session

A plugin component lives on the **engine** entity, whose scene is the app-level `GameCoreScene`, not
a `Session`. `Entity.GetSession()` there throws `InvalidCastException: Unable to cast GameCoreScene
to Session`. Use the route `McpServerComponent` uses:

```csharp
var scene = hostEntity.Scene?.UserObject as GameCoreScene;
var session = scene?.GameClient?.Get<WorldSessionComponent>()?.OwnedSession;
```

Resolve per call — it is null at the main menu and changes with each world load.

## Mod content-cache validation

A mod's `content/contentcache.json` records each file's expected length. If a shipped `.def` does not
match, the engine logs `[Content Validation]: Failed to validate ... Differing file length (expected
N, got M)`, shows **"Loading Failed. Corrupted files have been found."**, and refuses to mount those
defs. The rest of the mod still loads, so the symptom is a block that never appears. Seen in a
published Workshop mod whose author edited defs after generating the cache.

## Entity component lookup

`Entity.TryGet<T>(StringId tag = default)` takes an optional tag. Entities carrying several
components of the same type bind them to **tag slots** in their `EntityCompositeDefinition` — the
character has `Inventory`, `ConsumableInventory` and `DatapadInventory`. Pass the tag when the
composite defines one.

---

# Testing Policy

**Integration failures dominate here, and unit tests cannot see them.** Every bug in the Build
Planner plugin — wrong entity, input-context eviction, mapping reset, tag slots, silent returns —
was a fact about the live engine. All would have passed a green test suite.

So the split is:

- **Pure logic gets unit tests** — component-total merging, multipliers, outcome classification,
  modifier resolution. These are regression-proofing for refactors, and they run without the game.
- **Everything touching the engine gets a logged, reproducible in-game check.** The log *is* the test
  output. Keep every branch reported (see "A silent code path is a broken code path") so a run can be
  read after the fact instead of re-run under observation.

Do not let a passing suite stand in for loading the mod. "Definition of Done" still requires
observing the change in-game.
