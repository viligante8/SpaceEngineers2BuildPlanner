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

**SHIFT-produce works, confirmed in-game 2026-08-22**, including the engine's sub-component cascade:
enqueueing a component recipe at an assembler made the engine raise the ingot and ore sub-recipes on
connected converters by itself. See "Producing components".

**The full loop is confirmed end to end:** queue a block, `SHIFT+N` to produce, wait for the
assembler and its delegated sub-recipes to finish, then `N` to withdraw exactly what was made.

Testing produce also exposed a reach bug that had been present in the withdrawal all along — see
"Fixed: reach ignored conveyors entirely".

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
| **SHIFT + N** | Produce the missing components at a connected assembler | yes — end to end |
| **SHIFT + CTRL + N** | Produce **×10** | not separately — same path, multiplier only |
| **SHIFT + ALT + N** | Clear the queue without withdrawing | action yes; new chord not re-exercised |
| **SHIFT + ALT + CTRL + N** | Dump runtime state to the log (developer tool) | yes — used to confirm the cascade |

Build planner input is ignored while the game is paused.

Right-click is only *bound* while a welder or area welder is showing its block panel. The rest of the
time the mod does not hold the button at all, so the game keeps it for dropping items in the
inventory screen, removing projections, and placement mode. This has to be done by activating and
deactivating the input context, not by ignoring the click: an input is consumed by exactly one
context per frame, so merely declining to act on it still takes it away from the game.

Queueing takes the block's **outstanding** components, not a full recipe: a part-welded block that
needs 29 more plates queues 29, not 30.

Withdrawal pulls from the container you are aiming at, widening to every inventory that can reach it
across the **conveyor network**. An unattached container gives you only its own contents.

## Producing components

`SHIFT + N` at a block on a grid with an assembler queues the components you are **short of** —
shortfall against your own inventory, the same subtraction the withdrawal uses — as recipes on that
assembler. `SHIFT + CTRL + N` does it for ten times the queued blocks.

Aim straight at an assembler to send the work there specifically; aim at anything else on the grid
and the first reachable converter that has a recipe for each component is used.

**Produce never clears the queue, on any path.** The components do not exist yet; you still have to
come back and press `N` once they do, and the queue is the only record of what they were for.

### Sub-components are the engine's job, not the mod's

The obvious implementation walks the recipe tree — Steel Plate needs Iron Ingot needs Iron Ore — and
enqueues each tier on the right block. **That work is already done in the engine**, so this mod does
not do it.

`ItemConverterComponent.TryEnqueueRecipe` takes a write pointer on `ConversionQueueData`. That marks
the data changed, which fires `OnRecipeCompletedOrEnqueued` (`[OnChanged(typeof(ConversionQueueData))]`),
which calls `MarkChildRequestsDirty()`, which schedules `UpdateRequestsWhileEnabled`. That job:

1. `AccumulateIngredientsForFullQueue` — totals what the whole queue needs as input
2. `RemoveItemsAlreadyInInventory` — subtracts what the block already holds
3. `UpdatePersistentRequests` — raises conveyor pull requests, so the assembler feeds itself
4. `UpdateChildRequests` — for anything still missing, finds converters **on the same conveyor
   group** that list the item in a recipe's `Outputs` and enqueues the child recipe on them, passing
   itself as the `requester`

Step 4 recurses, because the child's own queue change marks *it* dirty in turn. So one enqueued
Steel Plate recipe cascades down to ore on its own. The engine also spreads one item's demand across
every capable converter (`UpdateRequestsInAvailableConverters` divides by the candidate count) and
retracts the request if the parent's queue changes (`ClearChildRequestsOfPreviousChildren`).

Reimplementing any of that would duplicate engine behaviour and drift from it on the next patch.
The mod enqueues the top-level component recipe and stops.

### Known gaps in produce

- **Progression-locked recipes are not filtered.** The recipe walk mirrors the one
  `StreamedProductionInfoSessionComponent.TryEnqueueAsync` performs before enqueueing, so a recipe
  found here is one the terminal would also accept — but whether the engine refuses a locked recipe
  at a lower layer is unconfirmed.

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
- **Production** at a conveyor-reachable assembler, with the engine cascading sub-components
- Outcome reported on every path (withdrew / partial / nothing found / nothing queued / no target)
- Rebindable key registered in the controls menu

## Verified in game

Everything in the control table above, plus: components for partly-built blocks (the outstanding
amount, not a full recipe), projections, the placement-mode guard, the pause guard, deposit keeping
your tools, and HUD notifications.

## Previously awaiting verification (all now confirmed)

Kept as a record of what was uncertain and how each was settled — not as an outstanding list.

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

## Fixed: reach ignored conveyors entirely (2026-08-22)

Fixed and confirmed in-game 2026-08-22.

**Symptom, from a test run:** an empty container welded onto a ship but *plumbed to nothing* still
withdrew from the whole ship and still queued work at its assemblers. A standalone container on its
own grid correctly reported nothing available.

**Root cause.** Both sweeps used `InventorySystemComponent.Inventories`, which is not the conveyor
network despite the name. It is filled by `OnBlocksChanged` -> `AddInventories(block)` for every
block on the grid, so conveyor plumbing never enters into it. The separate-grid case worked only
because a different grid has a different `InventorySystemComponent` — the right answer for the wrong
reason, which is why it looked correct.

The engine never uses that set to move anything. `PullAsync`, `PushAllAsync` and
`TransferByDefAsync` all iterate the conveyor graph instead:

```csharp
// InventorySystemComponent.PullAsync
foreach (var inv in ConveyorSystemComponent.IterateReachableInventories(
             invTo, itemDef, followEdgeDirection: false, mustContainTheItem: true))
```

**Fix.** `InventorySources.Reachable` now walks
`ConveyorSystemComponent.IterateReachableInventories(start, null, followEdgeDirection: false)` —
`followEdgeDirection: false` being documented as "search inventories that **can reach** start", the
correct direction for a withdrawal and one that honours one-way topology. `filterItem: null` ignores
per-item filters, so the walk runs once per action rather than once per item.

**Both** sweeps were fixed, not just the reported one: withdrawal and the assembler search shared the
mistake, so an unattached container could also dispatch production. The converter search now also
matches how the engine scopes its own delegation (`conveyorGroup.Blocks`).

Transfers still use `TransferByDef`, which is unchanged and already verified — only the choice of
which inventories are eligible moved.

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

1. **Produce ×10 and the moved SHIFT+ALT clear-queue chord have not been separately exercised.** Both
   share their code paths with actions that have been verified, so this is a gap in testing rather
   than a known defect.
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

Outcomes, warnings, errors and binding results always log.

Verbose per-click tracing (entity dumps, component lists, inventory contents) is **on by default** —
a game restart plus world load costs about five minutes, so a run that failed to record something
needed is far more expensive than a large log file.

Silence it by creating an empty file (no rebuild, no launch-option change):

```
%APPDATA%\SpaceEngineers2\BuildPlanner\quiet
```

Read once per run, so create it before launching. Every branch still reports *something* with the
flag set (CLAUDE.md, "A silent code path is a broken code path"); it only drops the supporting
detail.

## Tests

```
cd BuildPlanner.Tests
dotnet test
```

56 tests, covering the logic that is decidable without the game running:

| Unit | Why it is tested |
|---|---|
| `Modifiers.Resolve(ctrl, alt)` | branch order decides what ALT+CTRL means |
| `ComponentWithdrawal.Classify` | picks the message the player acts on |
| `BuildPlannerQueue.Accumulate` | merge + x10 must apply to *every* occurrence |
| `ComponentProduction.RunsNeeded` | rounding down leaves the player one component short |
| `ComponentProduction.Classify` | Partial must never be reported as Complete |

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
| `ComponentProduction.cs` | Enqueues component recipes at reachable converters |
| `InventoryShortfall.cs` | Engine-computed "what am I short of", shared by both |
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
