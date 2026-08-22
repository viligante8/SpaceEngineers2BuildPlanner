# SE2 Build Planner

Brings the Space Engineers 1 **Build Planner** / *Easy Inventory* workflow to Space Engineers 2:
queue the blocks you intend to build, then pull **exactly** the components you are missing from a
container with one keypress — no stack-grabbing, no "took 10, needed 11, walk back".

Implemented as a **plugin** (`-plugins:`), not a data mod, so it does not appear in the in-game mod
list. It is always active once the launch option is set.

## Status

**The core loop works, confirmed in-game 2026-08-22.** Aim at an unfinished block with a welder →
right-click to queue → aim at a container → press N → exactly the missing components arrive, in the
amounts the game's own block panel shows.

```
notify: Build Planner: queued Light Armor Cube (1 total)
requires 30 x Steel Plate
notify: Build Planner: withdrew 30x Steel Plate
```

Getting there took three wrong turns, all recorded in `../notes/build-planner-api.md`. The one worth
knowing: the queue summed `CubeBlockRecipeDefinition.CriticalItems`, which the XML docs define as
*proportions* used "to generate the final recipe based on mass, efficiency and rounding" — not a
component count. A 2.5m armour cube therefore asked for 1 Steel Plate instead of 30, and since the
withdrawal is exact, the player got exactly one. The correct field is
`CubeBlockDefinition.Items` — "computed when definition is post-processed".

Still unconfirmed in-game: projections, HUD notifications, and the placement-mode guard. See
"Awaiting in-game verification".

## Running it

Steam → Space Engineers 2 → Properties → Launch Options:

```
-plugins:C:\Users\vilig\RiderProjects\se2\mod1\BuildPlanner\bin\BuildPlanner.dll
```

`-loadScripts` is **not** needed (that flag is for in-game scripting).

Log: `%APPDATA%\SpaceEngineers2\BuildPlanner\BuildPlanner.log` — deliberately **not** under
`Temp\Logs`, which the game clears on startup.

A healthy start looks like:

```
BuildPlanner initializing...
  hook installed on InputGameComponent.Init
  hook installed on ControlCustomizationEngineComponent.SetMapping
BuildPlanner ready.
  action category set to BuildingControls
  bound withdraw/deposit to BuildPlannerWithdraw (Keyboard::N)
  bound queue to ToolSecondaryAction (Mouse::Right)
```

## Controls

| Input | Action | Verified in game |
|---|---|---|
| **Right-click** an unfinished block (welder equipped) | Queue the components it still needs | yes |
| **N** | Withdraw queued components, clear the queue | yes |
| **CTRL + N** | Withdraw, **keep** the queue (repeat building) | yes |
| **ALT + CTRL + N** | Withdraw **×10**, keep the queue | yes |
| **ALT + N** | Deposit ore, materials and components into the target (keeps tools) | yes |
| **SHIFT + N** | Clear the queue without withdrawing | yes |
| **SHIFT + CTRL + N** | Dump runtime state to the log (developer tool) | yes |

Build planner input is ignored while the game is paused.

Right-click is only *bound* while a welder or area welder is showing its block panel. The rest of the
time the mod does not hold the button at all, so the game keeps it for dropping items in the
inventory screen, removing projections, and placement mode. This has to be done by activating and
deactivating the input context, not by ignoring the click: an input is consumed by exactly one
context per frame, so merely declining to act on it still takes it away from the game.

Queueing takes the block's **outstanding** components, not a full recipe: a part-welded block that
needs 29 more plates queues 29, not 30.

Withdrawal pulls from the container you are aiming at, widening to every conveyor-connected
inventory on that grid (25 were swept in testing).

`BuildPlannerWithdraw` appears in **Options → Controls → Building** and can be rebound; the mod
respects a rebind rather than forcing N.

Modelled on the SE1 scheme in `../notes/build-planner-ux-spec.md`, adapted: SE1 used middle-click,
which is unusable here because vanilla already binds `Mouse::Middle` to three actions and an input is
consumed by exactly one context per frame.

## Working

- Queue of planned blocks, merged component totals, ×10 multiplier
- **Exact shortfall** via the engine's own `InventoryComponent.FindMissingItems`
- Withdrawal from the aimed container plus the grid's conveyor network
- Deposit-all
- Outcome reported on every path (withdrew / partial / nothing found / nothing queued / no target)
- Rebindable key registered in the controls menu

## Verified in game

Everything in the control table above, plus: components for partly-built blocks (the outstanding
amount, not a full recipe), projections, the placement-mode guard, the pause guard, deposit keeping
your tools, and HUD notifications.

## Previously awaiting verification (all now confirmed)

Written, compiling, deployed — **not yet observed working**. Per CLAUDE.md, building is not done.

1. **Projections.** Queueing now reads the welder's own target rather than hunting for the block
   placer, and the tool component that supplies it also owns the projection state, so projections
   should flow through the same path.
   *Test:* right-click a projected (holographic) block; expect `queued <block> (N total)`.
2. **HUD notifications.** `InGameUI` is borrowed from `InventoryNotificationsSessionComponent._ui`
   (a private field, confirmed against `Game2.Client.dll` metadata) on the **client** session, then
   `InGameUI.ShowNotification(HudNotification)` is called.
   *Test:* any action — the message should appear on screen, not only in the log. Every failure to
   reach the HUD logs its own reason (`notify: …`) and still records the message.
3. **Terminal queue visibility.** The queue is mirrored into `BuildPlannerData`; the terminal screen
   is already wired to that data.
   *Test:* queue a few blocks, then open the terminal and look for them.
4. **Placement-mode guard.** Right-click used to queue while in block/projection placement mode,
   where the game already uses RMB. The captured tool component is now released on the tool's own
   `CloseHUD`, and queueing additionally requires its block panel (`_screen`) to be open.
   *Test:* enter placement mode and right-click — nothing should be queued; then switch back to the
   welder and confirm queueing still works.

## Known limitations (decided, not oversights)

**Queue range is the welder's reach.** You must be close enough that the block panel is showing,
which sometimes means crouching for ground blocks. This was raised in testing and deliberately left
alone (2026-08-22).

The range lives in `RaycastEntityDetectorDefinition.MaxLength` on the tool's detector, and
`DetectionArgs` has no per-call length override — so it is a property of a shared definition, not
something a caller can vary. The two ways to extend it both cost something:

- Run our own detection with `MaxLength` temporarily raised and restored. Welding range stays
  correct, but right-click would have to be bound whenever a welder is equipped rather than only
  while the panel shows — widening exactly the input claim that caused the inventory/projection bug.
- Raise `MaxLength` outright. One field, robust, but it extends *welding* reach too, which is a
  gameplay change.

Neither was worth re-opening a freshly fixed input path. If revisited, prefer the first and gate
right-click so it is never claimed while a terminal or inventory screen is open.

## Not working yet

1. **SHIFT variants (produce / produce ×10)** are deliberately unmapped rather than silently behaving
   like a plain withdraw.
2. **The terminal queue screen.** The queue is now mirrored into the engine's own
   `BuildPlannerData.PlannedBlocks`, which `TerminalScreenViewModel` already binds to
   (`BuildPlannerBlocks`, `UpdateBuildPlannerBlocks`, `BuildPlannerBlock_ClearAll`,
   `BuildPlannerBlock_ScheduleAll`). Keen built the screen and left the data unpopulated; filling it
   is what should make the queue visible in-game. **Unverified**, and two things are unknown: whether
   an already-open terminal refreshes (the update handler is driven by `PropertyChanged`, which a
   plain `List.Add` does not raise), and whether removing a block there feeds back to the mod's own
   queue — it currently does not.
3. **Multiplayer is unverified.** Transfers run against the server session in-process; untested
   against a real server.

## Logging

Outcomes, warnings, errors and binding results always log. The verbose per-click tracing (entity
dumps, component lists, inventory contents) is **off by default** — it made a single right-click
write a dozen lines and buried the outcome.

Turn it back on by creating an empty file (no rebuild, no launch-option change):

```
%APPDATA%\SpaceEngineers2\BuildPlanner\debug
```

Read once per run. Every branch still reports *something* without it (CLAUDE.md, "A silent code path
is a broken code path"); the flag only restores the supporting detail.

## Tests

```
cd BuildPlanner.Tests
dotnet test
```

24 tests, covering the logic that is decidable without the game running:

| Unit | Why it is tested |
|---|---|
| `Modifiers.Resolve(ctrl, alt)` | branch order decides what ALT+CTRL means |
| `ComponentWithdrawal.Classify` | picks the message the player acts on |
| `BuildPlannerQueue.Accumulate` | merge + x10 must apply to *every* occurrence |

Each was extracted from an engine-coupled caller specifically to be testable, and each has been
mutation-checked — breaking the logic makes them fail, so they are not vacuous.

**What these tests structurally cannot catch, and the honest record of it.** Every bug this feature
has actually had was a fact about the engine, not an arithmetic slip:

| Bug | Why no unit test would have caught it |
|---|---|
| Summed `Recipe.CriticalItems` (a *proportion*) instead of `CubeBlockDefinition.Items` (the computed recipe) — 1 Steel Plate instead of 30 | The wrong field is real and populated. Fabricated test data would have encoded the same wrong assumption. Settled by the XML docs. |
| Block placer never found on the character | Only observable in a running game. |
| Queued from `IInteractedEntityProvider` on the server character (the press-F target) | Same. |
| Captured tool component stayed live in placement mode | Same. |

So the suite is regression-proofing for refactors, **not** evidence the feature works. Nothing here
substitutes for loading it — see "Awaiting in-game verification".

Note `Private=true` on the engine references in the test csproj (the plugin uses `false`): the test
host has no game assemblies loaded, so anything a test touches at runtime — `FixedPoint`, for
instance — must be copied to the output folder.

## Layout

| File | Role |
|---|---|
| `BuildPlannerPlugin.cs` | `IPlugin` entry point |
| `BuildPlannerInstaller.cs` | MonoMod hooks; vanilla GUIDs |
| `BuildPlannerBinding.cs` | Input context, action registration, session resolution |
| `BuildPlannerController.cs` | Dispatches input to queue / withdraw / deposit |
| `BuildPlannerQueue.cs` | Planned blocks → merged component totals |
| `ComponentWithdrawal.cs` | The transfer and its outcome |
| `InventorySources.cs` | Aimed container → conveyor-reachable inventories |
| `IntegrityToolAccess.cs` | The welder's current target — what to queue |
| `EngineQueueMirror.cs` | Mirrors the queue into the engine's `BuildPlannerData` |
| `PlayerAccess.cs` | Character and inventory lookup (both sessions) + diagnostics |
| `Modifiers.cs` | Live CTRL/ALT/SHIFT state → action |
| `Notifier.cs` | Outcome messages |
| `Log.cs` | File logging; `Log.Debug` is gated by the `debug` flag file |

Tests live in `../BuildPlanner.Tests`.

## Building

```
cd BuildPlanner
dotnet build -p:GameDir="F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"
```

**The game must be closed** — it holds `bin\BuildPlanner.dll` open, and the build fails at the copy
step with MSB3021 while it runs.

## Background

- `../notes/client-server-split.md` — the two-session architecture. **Read this first.**
- `../notes/build-planner-api.md` — verified engine API surface
- `../notes/build-planner-ux-spec.md` — the SE1 behaviour being reproduced
- `../CLAUDE.md` — project rules, including hard-won plugin and debugging lessons

Being a plugin bound to method signatures, an SE2 update can break it. If it stops loading after a
patch, read the log first.
