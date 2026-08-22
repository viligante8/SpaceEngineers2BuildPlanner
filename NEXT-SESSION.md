# Start here

## What this is

An SE2 **plugin** (not a data mod) that reproduces SE1's Build Planner: queue blocks, press a key at
a container, receive exactly the components you are missing.

**The withdrawal engine works and is verified in-game.** **Queueing was broken** and is fixed but
untested — see "Immediate next step". Read `BuildPlanner/README.md` for controls and gaps.

## Read before touching plugin code

1. `notes/client-server-split.md` — **the** critical VRAGE3 fact. Two sessions run in-process, both
   have a character entity, both report the same debug name, and only one has the inventory. This
   cost ~15 game restarts to find.
2. `CLAUDE.md`, sections "Debugging Runtime Code" and "SE2 Code Mods Via Plugins".
3. `notes/build-planner-api.md` — verified engine API surface.

## State: working and verified in game

Every interaction has been exercised in a real world and confirmed: queueing (real blocks and
projections), withdrawal, keep-queue, x10, deposit, clear, the placement-mode and pause guards, and
HUD notifications. See the control table in `BuildPlanner/README.md`.

Nothing is known broken. What is left is deliberate scope, recorded in the README:

1. **SHIFT-produce** (SE1 queues components at an assembler) is not implemented. It needs production
   pipeline integration that was never investigated.
2. **No queue UI.** The engine's own build planner screen cannot be driven -
   `BuildPlannerData` never replicates to the client, so the terminal cannot display it
   (`notes/build-planner-api.md`). Building our own G-menu affordance was considered and deferred.
3. **Multiplayer is unverified.** Transfers run against the server session in-process.
4. **Queue range is the welder's reach** - a deliberate decision with the reasoning in the README.

## If something breaks after a game update

This is a plugin bound to method signatures and private field names. The startup log names every
hook it installs; a missing one is the first thing to check. The private members relied on are
`IntegrityToolUIComponent._interactedEntityProvider`, `._model`, `._screen`, `._playerData`, and
`InventoryNotificationsSessionComponent._ui`.

## Debugging tools

**SHIFT+CTRL+N** dumps live state to the log: queue, captured tool, per-player-data chain.

`%APPDATA%\SpaceEngineers2\BuildPlanner\queries.txt` drives that dump - dotted paths from the roots
`tool`, `planner`, `clientsession`, with `[index]` and a `!N` depth suffix. It is re-read every dump,
so asking a new question costs no rebuild and no restart. That matters because the game holds the DLL
open: a code change forces a relaunch and a world load, roughly five minutes.

## Logging

Verbose per-click tracing is now **off by default** (it buried the outcome lines under a dozen
entity dumps per right-click). Outcomes, warnings, errors and binding results always log.

Re-enable by creating an empty file — no rebuild, no launch-option change:

```
%APPDATA%\SpaceEngineers2\BuildPlanner\debug
```

Read once per run, so create it before launching.

Log: `%APPDATA%\SpaceEngineers2\BuildPlanner\BuildPlanner.log` (deliberately not under `Temp\Logs`,
which the game clears on startup).

## Build and test loop

```
cd BuildPlanner
dotnet build -p:GameDir="F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"

cd BuildPlanner.Tests
dotnet test          # 24 tests, pure logic only
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
