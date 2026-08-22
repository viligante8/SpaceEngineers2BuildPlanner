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

## Immediate next step: one session verifies everything outstanding

The mod's core loop is **confirmed working in-game**: right-click an unfinished block with a welder,
press N at a container, receive exactly the missing components in the correct amounts.

Four things are built, compiling and deployed but **never observed running**. All four can be
checked in a single session:

1. **Projections** — right-click a projected (holographic) block. Expect `queued <block> (N total)`.
2. **HUD notifications** — watch the screen, not the log. Messages should appear on the HUD.
3. **Terminal queue visibility** — queue a few blocks, open the terminal, look for them. The queue is
   mirrored into the engine's own `BuildPlannerData`, which the terminal screen already binds to.
4. **Placement-mode guard** — enter block placement mode and right-click. Nothing should queue
   (RMB belongs to the game there). Switch back to the welder and confirm queueing still works.

If anything misbehaves, create `%APPDATA%\SpaceEngineers2\BuildPlanner\debug` before launching and
repeat — every branch reports which path it took.

## Then

1. **Update the README** — move whatever passed out of "Awaiting in-game verification".
2. Remaining gaps are genuinely unstarted: SHIFT/produce variants, the `G` queue screen,
   multiplayer. See `BuildPlanner/README.md`.

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
