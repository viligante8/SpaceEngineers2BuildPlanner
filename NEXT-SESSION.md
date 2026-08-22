# Start here

## What this is

An SE2 **plugin** (not a data mod) that reproduces SE1's Build Planner: queue blocks, press a key at
a container, receive exactly the components you are missing.

**Withdrawal, queueing and production all work and are verified in-game.** Read
`BuildPlanner/README.md` for the control table and the remaining gaps.

## State

**Every control is a separately rebindable action, verified in game (2026-08-22).**

The nine chords used to be one action plus live modifier sampling, so only one line appeared in
Options → Controls — and rebinding it did nothing, because a plugin-made `InputActionDefinition` has
`Guid.Empty` and the customisation is persisted *by GUID*. Each action now has a fixed GUID and is
registered with the `DefinitionManager`; the mechanism and the evidence are in
`notes/build-planner-api.md` ("Custom input actions are keyed by GUID").

Confirmed in one session: all nine actions dispatched on their own bindings, chords resolved against
each other at 1-4 inputs, the orphaned `Guid.Empty` entry was purged, and **all eight keyboard
actions were rebound onto `Mouse::Middle` + chords and survived a restart**. The SE1 middle-click
scheme is therefore reachable by rebinding — see the README's "Middle-click is available after all"
for the one side effect (vanilla's three plain-middle-click actions are suppressed).

**New and verified: queueing from the build menu (G).** Right-click a block tile to queue it.
Confirmed in game 2026-08-22: grid ('+') tiles resolve group -> kind -> block, kind tiles queue their
first unlocked size, the message names the exact size (`queued Battery 0.5 m`), non-block tiles refuse
out loud (`ToolTileModel 'Drill Mk 3'`), and **right-click still clears a toolbar slot** — the
regression the UI-hook design exists to avoid. Only the right-hand panel's size tile is unexercised.

**Everything in the control table is working, and the conveyor fix is confirmed (2026-08-22).**

Produce enqueues at the assembler, the engine's sub-component cascade fires on its own, and reach is
now conveyor-scoped for both withdrawal and production.

What has *not* been separately exercised — all sharing verified code paths, so gaps in testing rather
than known defects:

- **Produce ×10** (`SHIFT+CTRL+N`) — same path as produce, only the multiplier differs
- **`SHIFT+ALT+N`** (clear queue) on its *new* chord — the action itself was verified before the
  chord moved; `SHIFT+ALT+CTRL+N` was exercised, since the dump is what confirmed the cascade

**The full loop is confirmed (2026-08-22):** queue a block, `SHIFT+N` to produce, wait for the
assembler (and the sub-recipes it delegated) to finish, then `N` to withdraw exactly what was made.

## Test steps (for re-running after an engine update)

These are the checks that established the feature works. Re-run them after an SE2 update, since the
plugin binds to method signatures and private field names.

**Test 1 — basic produce.** Stand at a base with an assembler and ore/ingots available. Queue a block (right-click it
with a welder), aim at the assembler or any conveyor-connected block, press **SHIFT + N**.

Expect in the log and on the HUD:

```
  requires 30 x Steel Plate
  producing 30 x Steel Plate (30 run(s)) at 'Assembler500'
notify: Build Planner: producing 30x Steel Plate
```

Then open the assembler's production screen and confirm the recipe is really queued there.

**Test 2 — the cascade.** Enqueue a component whose ingredients the assembler does *not* have, and
confirm the engine raises the sub-recipes itself (ingots at a refinery, ore pulled over the conveyor)
without the mod asking. The dump's `requestedBy=` field is the evidence: a child recipe names the
assembler that asked for it. **Confirmed working 2026-08-22**; the mechanism is recorded in
`notes/build-planner-api.md`.

**Test 3 — reach and failure paths**, all of which report rather than going silent: no assembler on the grid
(`no assembler or refinery connected`), a component nothing can make (`cannot make X`), and a full
assembler queue (`would not accept ...`, logged, then the next converter is tried).

If it looks like nothing happened, press **SHIFT+ALT+CTRL+N** — the dump now lists every converter
it can reach, whether each is crafting, and how deep its queue is.

**Test 4 — rebinding.** Options → Controls → Building lists nine `Build Planner: …` actions. Rebind
one (say Withdraw to `M`), close the menu, and use it. Expect the new key to work and the old one to
do nothing. Restart and check it survived; the startup log prints the binding each action actually
has:

```
  bound BuildPlannerWithdraw to Keyboard::M
```

The log should also show, once, on the first launch after this change:

```
  removed an orphaned control customisation (no action GUID)
```

— that is the `"Action": "00000000-0000-0000-0000-000000000000"` entry the old bug wrote into
`%APPDATA%\SpaceEngineers2\AppData\EngineOptions\CustomizedControlsOptionsPart`.

**Test 5 — the build menu.** Open G and right-click: a grid tile (the '+' kind), a size tile in the
right-hand panel, and a tool tile. Expect `queued <name with size> (N total)` for the first two and
`only blocks can be queued from the build menu` for the third. Then **right-click a toolbar slot and
confirm it still clears** — the mod hooks the menu's UI handlers precisely so it never takes that
button, and that is the regression to watch for.

If one right-click ever queues two blocks, the press de-duplication in `BlockMenuAccess` has failed;
the panel attaches its handler twice (see `notes/build-planner-api.md`).

**Test 6 — the terminal panel (NEW, never yet run).** Queue two or three blocks, then walk up to an
assembler and open its terminal. Expect a "Build Planner" box **bottom-right**, listing one icon per
queued block.

```
  hook installed on TerminalScreen.InitializeComponent
  hook installed on TerminalScreenViewModel.BuildPlannerBlock_ScheduleAll
  terminal[attach]: revealed the shipped build planner panel
  terminal[datacontext]: bound the planner panel to BuildPlannerData (3 block(s) queued)
```

Then exercise the four buttons — each logs a `panel:` line naming what it did:

- **Produce** → same result as `SHIFT+N`, and **the queue must still be there afterwards** (this mod
  deliberately does not clear on produce; Keen's version cleared unconditionally)
- a block's **produce** → only that block's components enqueued
- a block's **remove** → that block gone from the panel *and* from the next withdrawal
- **Clear** → empty panel, and `N` afterwards reports nothing queued

The panel shows at most **ten** blocks by design; with more queued the log says
`showing 10 of N queued block(s)`.

Two things could not be settled by reading and the log will disambiguate: which pass finds the panel
(`init`, `attach` or `datacontext`), and whether bottom-right anchoring lands sensibly at this
resolution. If it renders badly, the Grid's alignment and margin are a one-line change.

## Read before touching plugin code

1. `notes/client-server-split.md` — **the** critical VRAGE3 fact. Two sessions run in-process, both
   have a character entity, both report the same debug name, and only one has the inventory. This
   cost ~15 game restarts to find.
2. `CLAUDE.md`, sections "Debugging Runtime Code" and "SE2 Code Mods Via Plugins".
3. `notes/build-planner-api.md` — verified engine API surface.

## Deliberate scope, not oversights

Queueing (real blocks and projections), withdrawal, keep-queue, x10, deposit, clear, production, the
placement-mode and pause guards, and HUD notifications have all been exercised in a real world.

Nothing is known broken. What is left is deliberate:

1. ~~**No queue UI.**~~ **Superseded 2026-08-22 — and the old claim here was wrong.** This said the
   engine's build planner screen "cannot be driven" because `BuildPlannerData` "never replicates to
   the client". Both halves were mistaken, and the real answer is better: Keen's terminal panel is
   **shipped complete** and merely switched off. `TerminalPlannerPanel` reveals it, feeds it the
   mirrored queue, and detours its four buttons onto `BuildPlannerController` — the shipped verbs
   behind them are half-built (their produce path needs a production screen open, uses each block's
   full recipe, and returns a success flag that cannot be false).

   **Confirmed rendering in game 2026-08-22:** a vertical icon list on the right of the terminal,
   growing upward. Plain, and it overlaps the scroll bar — Keen's `(WIP Pos)` label was honest.
   Queueing, live refresh, the ten-item cap, and Clear all confirmed working. Remove is
   **right-click on a block icon** (one button, dispatched by mouse button — matches SE1).

   Three defects that run found are **fixed and re-verified in game 2026-08-22**:

   - *Produce reported "no assembler connected" at a working assembler* — a client-vs-server entity
     mistake, reverted. Now: `3 item converter(s) conveyor-reachable from
     'CargoContainer750_ServerComposition'` followed by real recipes at `Smelter250_ServerComposition`.
     The `_ServerComposition` suffix is the evidence the right half of the split is being used.
   - *Withdrawal left the panel showing blocks it had already cleared* — `BuildPlannerQueue.Changed`
     now drives the mirror from the mutators. Confirmed by a complete withdrawal followed by
     `bound the planner panel to BuildPlannerData (0 block(s) queued)`, which previously read 5.
   - *Each queued block triggered ~2N+1 panel rebuilds* — both layers batch; the cascade is gone
     from the log.

   Right-click-to-remove confirmed: `panel: removed 'Hinge' (queue index 0, 0 left)`.
2. **Multiplayer is unverified.** Transfers run against the server session in-process.
3. **Queue range is the welder's reach** - a deliberate decision with the reasoning in the README.

## If something breaks after a game update

This is a plugin bound to method signatures and private field names. The startup log names every
hook it installs; a missing one is the first thing to check. The private members relied on are
`IntegrityToolUIComponent._interactedEntityProvider`, `._model`, `._screen`, `._playerData`,
`InventoryNotificationsSessionComponent._ui`, and `TerminalScreenViewModel._buildPlannerData`.

The terminal panel additionally depends on two things Keen could change without touching any
signature: the `LayoutTimer` label `"Terminal.BuildPlanner"` (the only handle on a Grid with no
`x:Name`), and that Grid still being hidden by a literal `IsVisible = false`. If Keen ever finishes
the panel themselves, the reveal becomes a no-op and the log says `planner panel already visible` —
which is the good outcome, not a failure.

## Debugging tools

**SHIFT+ALT+CTRL+N** dumps live state to the log: queue, **reachable item converters** (with
crafting state and queue depth), captured tool, per-player-data chain. The chord moved when
SHIFT/SHIFT+CTRL were given to produce.

`%APPDATA%\SpaceEngineers2\BuildPlanner\queries.txt` drives that dump - dotted paths from the roots
`tool`, `planner`, `clientsession`, with `[index]` and a `!N` depth suffix. It is re-read every dump,
so asking a new question costs no rebuild and no restart. That matters because the game holds the DLL
open: a code change forces a relaunch and a world load, roughly five minutes.

## Logging

Verbose per-click tracing is **on by default**; outcomes, warnings, errors and binding results
always log regardless.

Silence it by creating an empty file — no rebuild, no launch-option change:

```
%APPDATA%\SpaceEngineers2\BuildPlanner\quiet
```

Read once per run, so create it before launching. (Earlier revisions of this file described a
`debug` file that enabled tracing; no such flag exists — `Log.ProbeDebugFlag` probes for `quiet` and
defaults to verbose.)

Log: `%APPDATA%\SpaceEngineers2\BuildPlanner\BuildPlanner.log` (deliberately not under `Temp\Logs`,
which the game clears on startup).

## Build and test loop

```
cd BuildPlanner
dotnet build -p:GameDir="F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"

cd BuildPlanner.Tests
dotnet test          # 43 tests, pure logic only
```

**The game must be closed to build** — it holds the DLL open (MSB3021 otherwise). The workflow that
worked: ask the user to close the game, build, ask them to reopen, have them perform the action, then
read the log.

Tailing that log with a Monitor is worthwhile — results arrive without asking the user to copy
anything.

## Things that wasted time — do not repeat

- **Silent `return` in a handler.** A keypress that logs nothing is indistinguishable from a keypress
  that never arrived. Several restarts went into an input-routing bug that did not exist.
- **Fixing a lookup before checking the object.** Three consecutive wrong theories (tag slots, wrong
  entity, hierarchy) because the target was never dumped first.
- **Truncated diagnostics.** A filtered component list hid the answer twice. Chunk the full list.
- **Marker items.** Asking the user to carry `9 x Titanium` made entity ownership unambiguous in one
  run after many rounds of inference. Use this trick early.
- **Writing C# via shell heredocs.** Apostrophes break the heredoc and non-ASCII gets mangled to
  `0x97`. Two files needed `iconv` repair. Use the Write/Edit tools.
- **Guessing at a private engine field.** Reading `Game2.Client.dll` metadata directly with
  `System.Reflection.Metadata` settled the `InGameUI` question in one shot. The XML docs do not list
  private fields, so absence there is not evidence of absence.
