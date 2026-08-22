# Start here

## What this is

An SE2 **plugin** (not a data mod) that reproduces SE1's Build Planner: queue blocks, press a key at
a container, receive exactly the components you are missing.

**Withdrawal, queueing and production all work and are verified in-game.** Read
`BuildPlanner/README.md` for the control table and the remaining gaps.

## State

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

1. **No queue UI.** The engine's own build planner screen cannot be driven -
   `BuildPlannerData` never replicates to the client, so the terminal cannot display it
   (`notes/build-planner-api.md`). Building our own G-menu affordance was considered and deferred.
2. **Multiplayer is unverified.** Transfers run against the server session in-process.
3. **Queue range is the welder's reach** - a deliberate decision with the reasoning in the README.

## If something breaks after a game update

This is a plugin bound to method signatures and private field names. The startup log names every
hook it installs; a missing one is the first thing to check. The private members relied on are
`IntegrityToolUIComponent._interactedEntityProvider`, `._model`, `._screen`, `._playerData`, and
`InventoryNotificationsSessionComponent._ui`.

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
dotnet test          # 56 tests, pure logic only
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
