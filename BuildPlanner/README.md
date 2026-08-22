# SE2 Build Planner

Brings the Space Engineers 1 **Build Planner** / *Easy Inventory* workflow to Space Engineers 2:
queue the blocks you intend to build, then pull **exactly** the components you are missing from a
container with one keypress — no stack-grabbing, no "took 10, needed 11, walk back".

Implemented as a **plugin** (`-plugins:`), not a data mod, so it does not appear in the in-game mod
list. It is always active once the launch option is set.

## Status

**The core loop works, verified in-game.** Queue → aim at a container → press N → exactly the
missing components arrive. Confirmed by the game's own inventory data, not just by the mod claiming
success:

```
requires 1 x Steel Plate
shortfall is 1 item type(s)
moved 1 x Steel Plate
notify: Build Planner: withdrew 1x Steel Plate
```

Known gaps are listed under "Not working yet".

## Running it

Steam → Space Engineers 2 → Properties → Launch Options:

```
-plugins:C:\Users\vilig\RiderProjects\se2\mod1\BuildPlanner\bin\BuildPlanner.dll
```

`-loadScripts` is **not** needed (that flag is for in-game scripting).

Log: `%APPDATA%\SpaceEngineers2\BuildPlanner\BuildPlanner.log` — deliberately **not** under
`Temp\Logs`, which the game clears on startup.

A healthy start looks like:

```
BuildPlanner initializing...
  hook installed on InputGameComponent.Init
  hook installed on ControlCustomizationEngineComponent.SetMapping
BuildPlanner ready.
  action category set to BuildingControls
  bound withdraw/deposit to BuildPlannerWithdraw (Keyboard::N)
  bound queue to ToolSecondaryAction (Mouse::Right)
```

## Controls

| Input | Action |
|---|---|
| **Right-click** an unfinished block | Queue its missing components |
| **N** | Withdraw queued components, clear the queue |
| **CTRL + N** | Withdraw, **keep** the queue (repeat building) |
| **ALT + CTRL + N** | Withdraw **×10**, keep the queue |
| **ALT + N** | Deposit your inventory into the target |

Withdrawal pulls from the container you are aiming at, widening to every conveyor-connected
inventory on that grid (25 were swept in testing).

`BuildPlannerWithdraw` appears in **Options → Controls → Building** and can be rebound; the mod
respects a rebind rather than forcing N.

Modelled on the SE1 scheme in `../notes/build-planner-ux-spec.md`, adapted: SE1 used middle-click,
which is unusable here because vanilla already binds `Mouse::Middle` to three actions and an input is
consumed by exactly one context per frame.

## Working

- Queue of planned blocks, merged component totals, ×10 multiplier
- **Exact shortfall** via the engine's own `InventoryComponent.FindMissingItems`
- Withdrawal from the aimed container plus the grid's conveyor network
- Deposit-all
- Outcome reported on every path (withdrew / partial / nothing found / nothing queued / no target)
- Rebindable key registered in the controls menu

## Not working yet

1. **Projections do not queue.** A fix is deployed but **untested**: `BlockPlacerEntityComponent` is
   client-side, so it must be looked up on the *client* session's character while the inventory comes
   from the *server* session's. See `../notes/client-server-split.md`. Next thing to test.
2. **Notifications go to the log, not the HUD.** `Notifier` formats them correctly; the binding lacks
   a usable `InGameUI` handle. The pattern to copy is
   `InventoryNotificationsSessionComponent.DisplayItem`:
   `_ui.ShowNotification(HudNotification.CreateTextNotification(icon, LocKey, priority, type))`.
   `InGameUI` is `Keen.Game2.Client.UI.InGame.InGameUI`, injected as a `[Service]` — resolve it from
   the **client** session.
3. **SHIFT variants (produce / produce ×10)** are deliberately unmapped rather than silently behaving
   like a plain withdraw.
4. **The `G` queue screen.** `BuildPlannerIconControl.axaml` ships with the game; whether it can be
   surfaced is unverified. No visual queue inspection today.
5. **Verbose debug logging** is still in. Strip it back to outcomes and errors once the remaining
   pieces work — but keep *every branch* reporting something (see CLAUDE.md, "A silent code path is a
   broken code path").
6. **Multiplayer is unverified.** Transfers run against the server session in-process; untested
   against a real server.
7. **No tests yet.** Deferred by agreement until the feature works. When added: unit-test the pure
   logic (`BuildPlannerQueue.GetRequiredComponents`, `ComponentWithdrawal` outcome classification,
   `Modifiers.Resolve`) — every bug found so far was an integration fact no unit test would have
   caught.

## Layout

| File | Role |
|---|---|
| `BuildPlannerPlugin.cs` | `IPlugin` entry point |
| `BuildPlannerInstaller.cs` | MonoMod hooks; vanilla GUIDs |
| `BuildPlannerBinding.cs` | Input context, action registration, session resolution |
| `BuildPlannerController.cs` | Dispatches input to queue / withdraw / deposit |
| `BuildPlannerQueue.cs` | Planned blocks → merged component totals |
| `ComponentWithdrawal.cs` | The transfer and its outcome |
| `InventorySources.cs` | Aimed container → conveyor-reachable inventories |
| `PlayerAccess.cs` | Character and inventory lookup (both sessions) + diagnostics |
| `Modifiers.cs` | Live CTRL/ALT/SHIFT state → action |
| `Notifier.cs` | Outcome messages |
| `Log.cs` | File logging |

## Building

```
cd BuildPlanner
dotnet build -p:GameDir="F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"
```

**The game must be closed** — it holds `bin\BuildPlanner.dll` open, and the build fails at the copy
step with MSB3021 while it runs.

## Background

- `../notes/client-server-split.md` — the two-session architecture. **Read this first.**
- `../notes/build-planner-api.md` — verified engine API surface
- `../notes/build-planner-ux-spec.md` — the SE1 behaviour being reproduced
- `../CLAUDE.md` — project rules, including hard-won plugin and debugging lessons

Being a plugin bound to method signatures, an SE2 update can break it. If it stops loading after a
patch, read the log first.
