# Build Planner — target UX

Source: https://spaceengineers.wiki.gg/wiki/Build_Planner (SE1 official wiki), fetched 2026-08-21.
This is the behaviour the SE2 mod should reproduce. Cross-checked against Easy Inventory's
`Core.cs` (Workshop 646796262), whose modifier scheme is nearly identical — unsurprising, since
Keen absorbed that mod into Build Planner.

## Queueing blocks (never automatic)

Blocks are added to the queue **only** by explicit action:

- **Right-click an unwelded block** while holding a welder → its missing components are queued
- **Middle-click a block's ghost preview** after selecting it from the toolbar

There is no implicit "select a block and it queues itself."

## Withdrawing from a cargo port (engineer on foot)

| Input | Effect |
|---|---|
| `Middle-click` | Withdraw the queued components (clears queue) |
| `CTRL + Middle-click` | Withdraw, **keep** the queue (for repeat building) |
| `ALT + CTRL + Middle-click` | Withdraw **tenfold**, keep the queue |
| `SHIFT + Middle-click` | Start **producing** queued components |
| `SHIFT + CTRL + Middle-click` | Produce **tenfold** amounts |
| `ALT + Middle-click` | **Deposit** all inventory contents |

## Welder-ship workflow (docked)

Open inventory (`I`), use the middle-column buttons:
- gear → produce queued components
- right-arrow → deposit ship items to base
- left-arrow → withdraw produced components to ship

## Queue inspection

- `G` → toolbar config screen shows queued blocks
- Hover a queued block → shows its missing components
- Right-click a block inside the Build Planner → removes it from the queue

## Failure feedback

"If the engineer's inventory is full, or the components are not yet produced, you'll get a warning.
Then try again a bit later."

=> The mod must surface a visible warning on: inventory full, components unavailable, or
   nothing reachable to pull from. Silent failure is not acceptable.

## Easy Inventory comparison (SE1 mod, for reference)

`Core.cs:353` — `IsNewRightMouseReleased()` gated on Shift/Alt/Ctrl:
`PullOne` (plain), `Queue` (shift), `Push` (alt), `PullTen`/`QueueTen` (ctrl variants).
Same modifier vocabulary, bound to right-click instead of middle-click.
