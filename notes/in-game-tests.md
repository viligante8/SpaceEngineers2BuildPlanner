# In-game test steps

There is no automated coverage for anything that touches the engine — every serious bug in this
project was a fact about the live game, and all of them would have passed a green unit-test suite
(see `../CLAUDE.md`, "Testing Policy"). These are the manual checks that established the feature
works.

**Re-run them after a Space Engineers 2 update.** The plugin binds to method signatures and private
field names, so a patch is the expected way it breaks. The startup log names every hook it installs;
a missing one is the first thing to check.

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

**Test 6 — the terminal panel.** Queue two or three blocks, then walk up to an
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

Confirmed in game 2026-08-22: the panel renders as a vertical icon list on the right of the
terminal, growing upward, and overlaps the scroll bar — Keen's own `(WIP Pos)` label was honest. If
it ever renders badly, the Grid's alignment and margin are a one-line change.
