# Changelog

## 1.0.3 — 2026-08-22

An adversarial re-review of the 1.0.2 fixes found several of them wrong. This is the corrected set.

### Fixed

- **A missing `CloseHUD` no longer half-installs queueing.** `IntegrityToolAccess` clears its
  captured tool in exactly one place, reached only from the `CloseHUD` detour. 1.0.2 made a missing
  `CloseHUD` non-fatal, which installed two capture paths with nothing to release them - the capture
  goes sticky and the next right-click queues a stale block outside welder mode, which is the exact
  bug that hook was added to fix. Queueing now declines to install rather than installing broken.
- **A missing `InputGameComponent.Init` no longer publishes dead controls or dead buttons.** Nine
  rebindable actions wired to nothing, and four terminal buttons replaced by no-ops, are both worse
  than not installing at all.
- **Deposit reports partial results.** Placing 100 of 500 plates and saying only "deposited 1 item
  type(s)" is success-shaped; the player walks away carrying 400 with no idea why. It now names what
  is still carried, without asserting a cause it did not establish.
- **Deposit tracing no longer stalls the game thread.** 1.0.2 logged one line per accepting
  container, each a separate open/append/close under a lock, across every inventory on a conveyor
  network. Totalled once per item type instead.
- **`CountItem` and `HasItem` are both guarded.** 1.0.2 guarded one and left the other bare on the
  same object, on the same path - so the guard bought nothing.
- **Right-clicking a build-menu tile is never a silent no-op.**
- **Engine-wide input tracing is opt-in.** It writes to the *game's* log for every session; it is
  now behind a `trace-input` flag file.
- **The release path-leak guard actually runs.** It sat above the file copy, so it never scanned the
  two shipped text files, and it matched a bare user-name substring that both of those files contain
  inside the project's own GitHub URL. It now runs after the copy, tests both a path shape and the
  user name, and is scoped to files this project builds - the bundled MonoMod assemblies carry their
  upstream maintainer's build paths, which are not ours to strip.

### Added

- `SECURITY.md` - what the plugin touches, and commands to verify each claim rather than trust it.
- A `What it touches` section in the README, and an antivirus note. The safety claim previously
  shipped only inside the zip, so it could not be read before taking the risk.

### Documentation

Five engine claims that did not survive a check against the shipped assemblies were corrected,
including one in the "read this first" note that had `"Type": null` in an entity composite exactly
backwards.

## 1.0.2 — 2026-08-22

Superseded by 1.0.3. Added per-container deposit tracing so the 1.0.1 multi-container fix could be
verified from a log; 1.0.3 replaced it with a batched line after measuring what it cost the game
thread.

## 1.0.1 — 2026-08-22

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
