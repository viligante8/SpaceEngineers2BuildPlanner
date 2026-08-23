# Changelog

## 1.0.2 — 2026-08-23

The first build with the 1.0.1 fixes actually exercised in game: plugin load and all ten hooks,
deposit spilling from a full container into the next one on the same conveyor network, tools staying
on the player, and the queue/withdraw/produce loop with the terminal panel.

### Fixed

- **A deposit now traces each container it fills.** The multi-container path added in 1.0.1 logged
  nothing per transfer, so a spill from a full container into the next one left no evidence — the
  only other output is the final item-type count, which reads identically whether one container took
  everything or four shared it. That made the 1.0.1 fix impossible to verify from a log.

## 1.0.1 — 2026-08-23

Maintenance release. **Use this instead of 1.0.0, which has been withdrawn.**

### Fixed

- **Release artifacts no longer contain absolute build paths.** The 1.0.0 DLL and PDB embedded the
  paths of the machine that built them, including its Windows user name. Debug symbols are no longer
  shipped at all, and packaging now fails outright if a build path reappears.
- **A failed hook no longer disables unrelated features.** Installation returned early as soon as
  any one method lookup failed, so a single renamed engine method could silently take the welder
  hook, the build menu and the terminal panel down together, while the log mentioned only key
  bindings. Each group now installs and reports independently — which matters, because a game patch
  is the expected way this plugin breaks.
- **Deposit no longer strands items.** It stopped at the first container that accepted anything, so
  a container filling up partway through left the remainder on the player with nothing said about
  it. Every reachable container is now offered the item.
- **Deposit reports item types rather than "stacks"**, which is what it was actually counting.

### Added

- MIT `LICENSE`. The repository previously had none, which left it all-rights-reserved.
- Third-party notices now reproduce the real copyright lines and credit **iced** as its own project
  rather than as part of MonoMod.

## 1.0.0 — 2026-08-22

First release. Reproduces the SE1 Build Planner / Easy Inventory workflow.

### Features

- Queue of planned blocks, merged component totals, ×10 multiplier
- **Exact shortfall** via the engine's own `InventoryComponent.FindMissingItems`
- Withdrawal from the aimed container plus the grid's conveyor network
- Deposit-all, which leaves your tools alone
- **Production** at a conveyor-reachable assembler, with the engine cascading sub-components
- Queue visible in the terminal, reusing the panel Keen shipped hidden
- Outcome reported on every path — withdrew, partial, nothing found, nothing queued, no target
- Nine separately rebindable actions in the controls menu, chords included

### Verified in game

Everything in the control table, plus: components for partly-built blocks (the outstanding amount,
not a full recipe), projections, the placement-mode guard, the pause guard, deposit keeping your
tools, and HUD notifications.

**The terminal panel, end to end.** Revealed and bound on every terminal open; the list tracks
queueing live; the ten-item cap engages; **Produce** reaches converters across the conveyor network
and leaves the queue intact; left-click produces one block; right-click removes one, including
consecutive removes that re-index correctly; **Clear** empties it. A complete withdrawal empties the
panel, a partial one deliberately does not.

### Notable fixes during development

- **Queue read a proportion, not a count.** `CubeBlockRecipeDefinition.CriticalItems` is documented
  as *proportions* used "to generate the final recipe based on mass, efficiency and rounding". A
  2.5 m armour cube therefore asked for 1 Steel Plate instead of 30 — and since withdrawal is exact,
  the player got exactly one. The correct field is `CubeBlockDefinition.Items`.
- **Reach ignored conveyors entirely** — see [notes/conveyor-reach.md](notes/conveyor-reach.md).
- Full engine archaeology in [notes/build-planner-api.md](notes/build-planner-api.md).
