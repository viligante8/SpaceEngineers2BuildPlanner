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
  registered 9 input actions with the DefinitionManager
  bound BuildPlannerWithdraw to Keyboard::N
  bound BuildPlannerWithdrawKeep to Keyboard::N+Keyboard::Control
  ...
  bound BuildPlannerQueue to Mouse::Right, active only with a welder out
```

Each `bound …` line reports the binding that is **actually in the mapping**, so a rebound key shows
up there rather than the shipped default.

## Controls

Every row is a separate input action and is separately rebindable — see "Rebinding" below. The
chords are only the defaults.

| Default | Action | Verified in game |
|---|---|---|
| **Right-click** an unfinished block (welder equipped) | Queue the components it still needs | yes |
| **Right-click** a block in the build menu (G) | Queue that block's full component list | yes — groups, kinds, refusals |
| **N** | Withdraw queued components, clear the queue | yes |
| **CTRL + N** | Withdraw, **keep** the queue (repeat building) | yes |
| **ALT + CTRL + N** | Withdraw **×10**, keep the queue | yes |
| **ALT + N** | Deposit ore, materials and components into the target (keeps tools) | yes |
| **SHIFT + N** | Produce the missing components at a connected assembler | yes — end to end |
| **SHIFT + CTRL + N** | Produce **×10** | not separately — same path, multiplier only |
| **SHIFT + ALT + N** | Clear the queue without withdrawing | action yes; new chord not re-exercised |
| **SHIFT + ALT + CTRL + N** | Dump runtime state to the log (developer tool) | yes — used to confirm the cascade |

Build planner input is ignored while the game is paused.

A chord beats the plain key because `DisambiguatingControlActivationFilter` discards any candidate
control whose inputs also belong to a control with *more* inputs ("Discard candidate control …, too
few inputs"). Vanilla depends on the same rule for F5 / SHIFT + F5, so SHIFT + N produces rather than
withdrawing.

**The area welder queues its whole selection.** One right-click takes every unfinished block the
area covers, projections included, and reports once: `queued 12 blocks (12 total)`. Blocks already
finished are skipped. Verified in game 2026-08-22 — `tooltip lists 4 block(s), 2 unfinished`.

This needs two things that were each wrong at first. The tool must be *detected*: a plain welder
refreshes through `IntegrityToolUIComponent.UpdateUI`, but an area welder goes through
`AreaDetectionChanged` -> `UpdateAreaUI`, and with only the former hooked the area welder produced no
log line at all, because it was never recognised as an active tool. And the *selection* must be read
from the panel's model rather than the tool's interacted-entity provider, which carries exactly one
entity — preferring it silently reduced an area to the block under the crosshair.

Right-click is only *bound* while a welder or area welder is showing its block panel. The rest of the
time the mod does not hold the button at all, so the game keeps it for dropping items in the
inventory screen, removing projections, and placement mode. This has to be done by activating and
deactivating the input context, not by ignoring the click: an input is consumed by exactly one
context per frame, so merely declining to act on it still takes it away from the game.

Queueing takes the block's **outstanding** components, not a full recipe: a part-welded block that
needs 29 more plates queues 29, not 30.

Withdrawal pulls from the container you are aiming at, widening to every inventory that can reach it
across the **conveyor network**. An unattached container gives you only its own contents.

## Queueing from the build menu

Right-click any block tile in the build menu (G) to queue it, the way SE1 lets you plan a block you
have not placed yet. The full recipe is queued, since nothing is built.

**The menu's tiles are three deep**, which matters because right-clicking each behaves differently:

| Tile | What it is | Right-click |
|---|---|---|
| `BlockGroupTileModel` | the grid tiles, the ones with a '+' | queues the first unlocked block under the group |
| `BlockKindTileModel` | opens from a group; holds the grid sizes | queues the first unlocked size |
| `BlockSizeModel` | a size in the right-hand panel | queues **exactly** that size |
| tools, consumables, voxel hands | — | refused, with a message |

Since a group or kind tile has to *choose* a size, the notification names the one it picked
(`queued Battery 0.5 m`) rather than the generic block name — every grid size shares one
`UIData.Name`, so "Battery" alone could not tell you which you got. For exact control, open the tile
and right-click the size you want in the right-hand panel.

**This is hooked at the UI, not the input system**, and that is a deliberate constraint rather than
an implementation detail. While the menu is open, right-click has already been consumed by vanilla's
`CursorButton2` in the UI cursor layer:

```
[Input][#4028]: Control Keyboard::G : Build Menu activated with state Start.
[Input][#4061]: Consuming input Mouse::Right in layer #26:<Uninitialized>
[Input][#4061]: Control Mouse::Right : CursorButton2 activated with state Start.
```

A layer-less context is dispatched *before* the UI's, so binding our queue action here would have
taken the button away from the menu — and right-clicking a toolbar slot clears it (verified in game).
Hooking `GScreen.TilePressed` / `SizeTilePressed` / `SubTilePressed` sidesteps the contest entirely:
vanilla still gets its click, we read the same event afterwards, and the toolbar is untouched because
its presses go through `OnToolbarTilePointerPressed`, a different path.

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

## Queue visibility in the terminal

Open the terminal and the queue is listed bottom-right, in **Keen's own build planner panel** —
"Build / Planner", a **Produce** button, one icon per queued block with per-block produce and remove
buttons, and a **Clear** button.

None of that is drawn by this mod. The whole panel is in the shipped `TerminalScreen.axaml`, wired to
`TerminalScreenViewModel`'s four build-planner verbs, and left switched off mid-development — the
`(WIP Pos)` label Keen put beside it says as much. Three separate omissions keep it dark, and
`TerminalPlannerPanel` supplies all three:

| What Keen left undone | Evidence | What the mod does |
|---|---|---|
| The panel's Grid is hidden | `IsVisible = false` as a literal in `InitializeComponent`, not a binding | Finds it by `LayoutTimer.Label == "Terminal.BuildPlanner"` and sets `IsVisible = true` |
| `_buildPlannerData` is never assigned | `initonly` field, 6 `ldfld`s and **0 `stfld`s** in all of `Game2.Client.dll` | Sets it via `[UnsafeAccessor]` to the same instance `EngineQueueMirror` writes |
| `UpdateBuildPlannerBlocks` is never subscribed | no `ldftn` to it anywhere | Fills `BuildPlannerBlocks` on `PropertyChanged`, using public API only |

Without the second of those, the panel would still *list* blocks but every button would throw a
`NullReferenceException` on the first press.

The panel shows at most **ten** blocks — Keen's own cap, and a layout constraint rather than a
preference: the icon list is a vertical `StackPanel` in a bottom-anchored Grid with no scroll viewer,
so an uncapped list climbs off the top of the screen. The log says when more are queued than shown.

### The buttons are routed at the mod, not at Keen's half-built verbs

All four are detoured and **replaced** — the originals never run:

| Control | Runs | Notes |
|---|---|---|
| **Produce** button | `ProduceQueueFromTerminal` | Same path as `SHIFT+N` |
| **left-click** a block icon | `ProduceOneFromTerminal` | Only that block's outstanding components |
| **right-click** a block icon | `RemoveQueuedFromTerminal` | Removes from the mod's queue; the mirror re-syncs |
| **Clear** button | `ClearQueueFromTerminal` → `ClearQueue` | Same path as the clear-queue keybind |

**There is no separate remove button, and this is not a missing feature.**
`BuildPlannerIconControl` wraps each icon in a single `Button` whose `OnButtonPressed` dispatches on
which mouse button was used — left to `ProduceCommand`, right to `RemoveCommand`. That matches SE1,
where the wiki's build-planner page says "right-click a block inside the Build Planner removes it
from the queue" (`../notes/build-planner-ux-spec.md`). It is simply undiscoverable: nothing on screen
says so.

That means one queue, not two: everything mutates `BuildPlannerQueue`, and `EngineQueueMirror`
rebuilds the engine's `PlannedBlocks` from it afterwards, so the panel cannot drift from what a
withdrawal will actually pull.

**Why replace rather than wrap** — Keen's `TryScheduleBlockForProduction` does nothing unless a
production screen is already open, targets only that one converter, queues each block's *full* recipe
rather than what is missing, and returns a success flag seeded `true` and OR-ed, so it can never be
false. `ProduceBuildPlannerBlock` uses that flag to decide whether to drop the block, so the shipped
button clears the block whether or not anything was scheduled. Running it alongside ours would
enqueue twice and still lose the queue.

**Two deliberate departures from Keen's behaviour:**

- **Produce does not clear the queue.** Keen's `ScheduleAll` clears unconditionally. Production only
  *starts* the components — the player still has to come back and withdraw them, and unlike a
  withdrawal there is nothing in their inventory afterwards to reconstruct the queue from. This
  matches `SHIFT+N`, which has always behaved this way for that reason.
- **Reach is resolved exactly as the keybind resolves it**, from the interaction provider on the
  **server** character. An earlier revision had the panel pass `TerminalScreenViewModel.Interacted`
  instead, reasoning that a player with a terminal open is not aiming at anything. That produced
  "no assembler or refinery connected" while standing at a working assembler, because the terminal
  view model is `Game2.Client` and its entity is the **client** copy, while `ItemConverterComponent`
  and `InventoryComponent` are `Game2.Simulation` and exist only on the **server** copy — the trap
  `../notes/client-server-split.md` exists to warn about. Observed in game 2026-08-22 and reverted.

Full derivation, including the two timing traps (logical vs visual tree, and when `DataContext`
arrives) in `../notes/build-planner-api.md`, "The terminal's build planner panel is complete but
switched off".

## Rebinding

All nine actions appear in **Options → Controls → Building**, named `Build Planner: …`, and each can
be rebound on its own — to a plain key or to a chord, since the rebinding dialog composes modifiers
(`InputControlComposer.KeyboardDefault`).

**Verified in game 2026-08-22.** All eight keyboard actions were rebound onto `Mouse::Middle` and
its chords, used for a full session, and survived a restart — the startup log read them back from the
options file:

```
  bound BuildPlannerWithdraw to Mouse::Middle
  bound BuildPlannerWithdrawKeep to Mouse::Middle+Keyboard::Control
  bound BuildPlannerDiagnose to Mouse::Middle+Keyboard::Control+Keyboard::Shift+Keyboard::Alt
```

**This did not work before that date and the reason is worth keeping.** A customised binding is
persisted *by the action's GUID*:

```json
{ "Action": "00000000-0000-0000-0000-000000000000",
  "PrimaryControl": { "$Type": "VRage:Keen.VRage.Input.DigitalControl", "Input": "Mouse::Middle" } }
```

That is a real line from `%APPDATA%\SpaceEngineers2\AppData\EngineOptions\CustomizedControlsOptionsPart`
after rebinding the old action. The action had been built with `new InputActionDefinition(...)`,
which leaves `Guid` empty and never registers it with the `DefinitionManager` — so
`ActionControlEntry.Action` could not resolve it on the way back in, returned a placeholder, and
`ControlCustomizationEngineComponent.UpdateMappings` dropped the entry:

```csharp
if (builder.RemoveAction(action))   // false for the placeholder -> the rebinding is discarded
```

The binding menu therefore accepted the new key and the game kept using N.

The fix is the missing half of the setup, not a workaround: each action is now built from an
`InputActionDefinitionObjectBuilder` carrying a fixed GUID (`RuntimeDefinitionHelper.Create(...,
keepBuilderGuid: true)`) and inserted into the definition set that already owns vanilla's input
actions, so `TryGetDefinition` resolves it. Orphaned `00000000-…` entries left over from the old
behaviour are removed from the options file on the next launch.

### Middle-click is available after all

Modelled on the SE1 scheme in `../notes/build-planner-ux-spec.md`. The defaults use `N` because
vanilla binds plain `Mouse::Middle` to three actions — `ba689cc1` (ToolTertiary), `6a759ebb` and
`9ad853aa` — and an input is consumed by exactly one context per frame.

**That does not make middle-click unusable, only impolite by default.** Verified in game 2026-08-22:
rebinding all eight keyboard actions onto `Mouse::Middle` and its chords works, restoring the SE1
scheme exactly. Our withdraw context is layer-less and permanently active, and `DispatchActions`
walks `_activeContexts` backwards, so it claims the button before vanilla's contexts see it.

The cost is that vanilla's three middle-click actions are suppressed while *plain* middle-click is
bound to a Build Planner action. The chorded variants cost nothing — no vanilla action uses
CTRL/ALT/SHIFT + middle-click.

## Working

- Queue of planned blocks, merged component totals, ×10 multiplier
- **Exact shortfall** via the engine's own `InventoryComponent.FindMissingItems`
- Withdrawal from the aimed container plus the grid's conveyor network
- Deposit-all
- **Production** at a conveyor-reachable assembler, with the engine cascading sub-components
- Outcome reported on every path (withdrew / partial / nothing found / nothing queued / no target)
- Nine separately rebindable actions in the controls menu, chords included

## Verified in game

Everything in the control table above, plus: components for partly-built blocks (the outstanding
amount, not a full recipe), projections, the placement-mode guard, the pause guard, deposit keeping
your tools, and HUD notifications.

**The terminal panel, end to end (2026-08-22).** Revealed and bound on every terminal open; the list
tracks queueing live; the ten-item cap engages; **Produce** reaches converters across the conveyor
network (`3 item converter(s) conveyor-reachable from 'CargoContainer750_ServerComposition'` →
recipes enqueued at `Smelter250_ServerComposition`) and leaves the queue intact; left-click produces
one block; right-click removes one, including consecutive removes that re-index correctly; **Clear**
empties it. A complete withdrawal empties the panel, a partial one deliberately does not.

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
3. **Terminal queue visibility.** The queue is mirrored into `BuildPlannerData`, and
   `TerminalPlannerPanel` reveals the panel Keen shipped hidden and feeds it that data.
   *Test:* queue a few blocks, then open the terminal (F) and look bottom-right for a "Build Planner"
   box listing them.
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
2. **Multiplayer is unverified.** Transfers run against the server session in-process; untested
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

43 tests, covering the logic that is decidable without the game running:

| Unit | Why it is tested |
|---|---|
| `BuildPlannerActions.All` | a duplicate GUID, name or chord silently merges two controls |
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
| `TerminalPlannerPanel.cs` | Reveals Keen's hidden terminal panel and feeds it that data |
| `PlayerAccess.cs` | Character and inventory lookup (both sessions) + diagnostics |
| `Modifiers.cs` | Live CTRL/ALT/SHIFT state → action |
| `Notifier.cs` | Outcome messages |
| `Log.cs` | File logging; verbose by default, silenced by the `quiet` flag file |

Tests live in `../BuildPlanner.Tests`.

## Building

```
cd BuildPlanner
dotnet build -p:GameDir="F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"
```

**The game must be closed** — it holds `bin\BuildPlanner.dll` open, and the build fails at the copy
step with MSB3021 while it runs.

## Releasing

```powershell
.\scripts\package.ps1 -Version 1.0.0 -Publish
```

Tests, packages, and creates a **draft** GitHub release with the zip attached. Drop `-Publish` to
just build the zip locally.

Works with the game open — it builds through a temporary directory rather than `bin\`.

There is no CI: the plugin compiles against Keen's shipped assemblies, which are not in this repo,
so no GitHub-hosted runner can build it. `../RELEASING.md` covers that and what the artifact does
and does not contain.

## Background

- `../notes/client-server-split.md` — the two-session architecture. **Read this first.**
- `../notes/build-planner-api.md` — verified engine API surface
- `../notes/build-planner-ux-spec.md` — the SE1 behaviour being reproduced
- `../CLAUDE.md` — project rules, including hard-won plugin and debugging lessons

Being a plugin bound to method signatures, an SE2 update can break it. If it stops loading after a
patch, read the log first.
