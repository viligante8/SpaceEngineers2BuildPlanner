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

## Immediate next step

Rebuilt 2026-08-22 with a **root-cause fix** for two user-reported symptoms that were one bug.
See `notes/build-planner-api.md`, "The block placer is NOT on the character". Untested in game.

The block placer was never found on the character (every right-click logged
`no BlockPlacerEntityComponent on character or its parents`), so queueing always fell through to an
interaction-based fallback that queued the *press-F target* rather than the crosshair block. Result:
projections never queued, and normal blocks queued the wrong definition -> `N` withdrew a
confidently wrong amount (1 Steel Plate instead of ~50).

Test, in one session:

1. **Right-click a normal unfinished block.** Check the log line `requires N x <item>` (now always
   logged) matches that block. This is the regression that was invisible before.
2. **Right-click a projection (holographic block).** Expect `queued <block> (N total)`.
3. **Press N at a container.** Expect the withdrawn amount to match `requires`.
4. **Watch the HUD**, not just the log - notifications should now appear on screen.

Failure is now attributable without the debug flag: the log distinguishes
`no block placer found` from `block placer found, but it has no aligned block`.

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
