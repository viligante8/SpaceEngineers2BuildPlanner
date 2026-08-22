# Start here

## What this is

An SE2 **plugin** (not a data mod) that reproduces SE1's Build Planner: queue blocks, press a key at
a container, receive exactly the components you are missing.

**The core feature works and is verified in-game.** Read `BuildPlanner/README.md` for controls,
status, and the gap list.

## Read before touching plugin code

1. `notes/client-server-split.md` — **the** critical VRAGE3 fact. Two sessions run in-process, both
   have a character entity, both report the same debug name, and only one has the inventory. This
   cost ~15 game restarts to find.
2. `CLAUDE.md`, sections "Debugging Runtime Code" and "SE2 Code Mods Via Plugins".
3. `notes/build-planner-api.md` — verified engine API surface.

## Immediate next step

**Test the projections fix** (deployed 2026-08-22 00:26, never run).

Right-clicking a *projection* should queue it. Previously failed with
`no BlockPlacerEntityComponent on character` because the block placer is client-side while the
character lookup had moved to the server session. The fix resolves each from its own session.

Test: launch, right-click a projected (holographic) block, check the log for
`queued <block> (N total)`.

## Then, in order

1. **HUD notifications** — messages currently go to the log only. Pattern to copy is in
   `BuildPlanner/README.md` gap #2; resolve `InGameUI` from the **client** session.
2. **Strip debug logging** back to outcomes and errors — but keep every branch reporting something.
3. **Tests** — user asked for regression tests once it works. Unit-test the pure logic only; every
   bug so far was an integration fact no unit test would catch. See CLAUDE.md "Testing Policy".

## Build and test loop

```
cd BuildPlanner
dotnet build -p:GameDir="F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"
```

**The game must be closed to build** — it holds the DLL open (MSB3021 otherwise). The workflow that
worked: ask the user to close the game, build, ask them to reopen, have them perform the action, then
read `%APPDATA%\SpaceEngineers2\BuildPlanner\BuildPlanner.log`.

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
