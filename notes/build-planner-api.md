# Build Planner / Easy Inventory — verified API surface

All entries below were read from the shipping binaries (ilspycmd) or the paired XML docs.
Game build at time of research: **Game2 / VRage 2.4.0.86**.

## Feature status in SE2

Build Planner ships **partially built**: data model and pull machinery are real, the user-facing
surface is absent.

| Layer | Status |
|---|---|
| `GameSystems.BuildPlanners.BuildPlannerData` — "Per-player data keeping track of planned blocks" | present |
| `BuildPlannerData.PlannedBlocks` : `List<CubeBlockDefinition>`, `AddPlannedBlock`, `RemovePlannedBlock` | present |
| Client UI verbs `BuildPlannerBlock_ScheduleAll`, `BuildPlannerBlock_ClearAll`, `ProduceBuildPlannerBlock`, `RemoveBuildPlannerBlock` | present |
| `/UI/TerminalScreen/BuildPlanners/BuildPlannerIconControl.axaml` | present |
| Keybind | **absent** — no input def, nothing in `ActionControlMapping.def` |
| Localization | **absent** — 0 of 271 labels in `ControlsTexts.loc-texts`; no "Planner" string in any `Texts/` file |

Contrast the F12 debug menu, which *is* fully wired (bound key + localized text + `WorldClient.def`).

## Core APIs (exact signatures, from decompiled `Game2.Simulation.dll`)

`Keen.Game2.Simulation.WorldObjects.Items.InventoryComponent`:

```csharp
void       FindMissingItems(ReadOnlySpan<ItemAmount> items, BufferReference<ItemAmount> missingItems)
FixedPoint TransferByDef(InventoryComponent to, ItemDefinition itemDef, int? itemIdxTarget,
                         FixedPoint? amount, bool allowPartial)
FixedPoint AddItemByDef(ItemDefinition itemDef, int? itemIdx, FixedPoint amount, bool allowPartial = true)
ItemStack  RemoveItemByDef(ItemDefinition itemDef, FixedPoint? amount)
FixedPoint CountItem(ItemDefinition itemDef)
bool       HasItem(ItemDefinition itemDef)
```

`FindMissingItems` computes the per-item shortfall — this is the "I needed 11, took 10" problem,
already solved in engine code.

Recipe → required components:
`CubeBlockDefinition.Recipe` → `CubeBlockRecipeDefinition`
  - `MergeableList<ItemAmount> CriticalItems`
  - `MergeableList<ItemAmount>? OptionalItems`
  - `FixedPoint ComputeTotalItemAmount()`

Current build selection (client):
`Game2.Client.GameSystems.BlockPlacement.BlockPlacer.ActiveBundleData.Definition`

## Pulling across a conveyor network

`CubeGrids.ResourceDistribution.Inventories.InventorySystemComponent`:
```csharp
PullAsync(InventoryComponent, ItemDefinition, FixedPoint?)
PullAllAsync(InventoryComponent [, FixedPoint])
TransferByDefAsync(InventoryComponent from, InventoryComponent to, ItemDefinition, int?, FixedPoint?, bool)
IsConnectedTo(InventorySystemComponent)
AreConnectedAsync(InventoryComponent, InventoryComponent, ItemDefinition)
```

`CubeGrids.ResourceDistribution.Conveyors.ConveyorSystemComponent`:
```csharp
IterateReachableInventories(InventoryComponent, ItemDefinition, bool, bool)
```

`WorldObjects.Tools.IItemRequester` / `Items.ItemRequesterComponent` — batched pull:
`AddItemToRequestBatch(ItemAmount)` → `CommitRequestBatch()` →
`PersistentRequestProcessorComponent.CreatePullExactRequest(inventory, amount, item)`.

## Constraint that shapes the design

`PersistentRequestProcessorComponent` is **grid/conveyor scoped** — it walks
`_conveyors.GetConnectedSystems()`, and `ItemRequesterComponent.GetProcessor()` resolves it via
`GetClosestParentOrSelfWith<PersistentRequestProcessorComponent>()`.

The **character has neither** `ItemRequesterComponent` nor `PersistentRequestProcessorComponent`.
`CompositeCharacterServer.def` (guid `4cc16aab-0c8b-4196-9a2d-0d8bc33fe89e`) has `InventoryComponent`
under tag slot `"Inventory"` only.

`ItemRequesterComponent` binds its inventory by tag: `[Component("InventoryRequester")]`.

=> On-grid (connected to conveyors): use Keen's requester/`PullAsync` path.
=> Off-grid (standing near a loose container): direct `TransferByDef` between `InventoryComponent`s,
   Easy-Inventory style.

## Loading a code mod into SE2 (confirmed working on this machine)

```
SpaceEngineers2.exe -loadScripts -plugins:<abs-or-rel-path>\Plugin.dll
```
Log confirms: `Running application with arguments: -loadScripts -plugins:...` then
`[Process Tag Added] : PluginLoaded`; `PBPatch.dll` + `MonoMod.RuntimeDetour.dll` appear in
"Loaded modules".

- Entry point: `Keen.VRage.Core.Plugins.IPlugin`; ctor `(PluginHost host)` is preferred
  (`Activator.CreateInstance(pluginType, this)`).
- `PluginHost.OnBeforeEngineInstantiated` fires after engine setup, before scripts compile.
- Hooking: MonoMod `RuntimeDetour` 25.3.3 — `new Hook(methodInfo, replacementDelegate)`.
- csproj: `net9.0`, `<EnableDynamicLoading>true`, `<Reference>` each `Game2\*.dll` with
  `<Private>false</Private>`.

Reference implementation: Workshop 3679814146 (stored under SE1 appid `1133870`),
`PBPatch_source/PBPatchPlugin.cs`.

## Attaching engine components via data

`VRage:Keen.VRage.DCS.Definitions.EntityCompositeDefinitionObjectBuilder` — `TagSlots` (name → GUID)
plus `Components` (GUID → `{Definition, Type}`). The PB mod uses this to bolt
`InGameScriptingComponent` onto a block. Same mechanism can attach `ItemRequesterComponent`.

## Gotcha: contentcache validation

A mod's `content/contentcache.json` records expected file lengths. If a shipped `.def` doesn't match,
the engine logs `[Content Validation]: Failed to validate ... Differing file length (expected N, got M)`
and shows **"Loading Failed. Corrupted files have been found."**, and those defs do not mount.
Observed with the PB mod's `programmableblock25_server.def` / `_servercomposition.def`.

## Input interception — no detour required

`Keen.VRage.Input.EngineComponents.ActionInputProcessorBaseComponent` (VRage.Input.dll) exposes a
**public event**:

```csharp
public event Action<ListReader<(ActionMapping Mapping, ControlActivation Activation)>>? BeforeControlsDispatched;
```

Subscribing gives every action just before dispatch. `ActionMapping` is a readonly struct with
public fields `InputControl Control` and `InputActionDefinition Action`, so the fired action is
identified directly — no MonoMod hook needed for *reading* input.

Also available on the same component: `PressOnce(InputActionDefinition)`, `Hold(...)`,
`Release(...)`, `Mapping`, and `SetMapping(ActionControlMapping)`.

`ControlActivationFlags` documents activation states (`Start` = "Control became active", plus
`ValueChange`, …).

## HUD notifications (required by the UX spec's warnings)

Pattern copied from `Game2.Client.WorldObjects.Items.InventoryNotificationsSessionComponent`:

```csharp
_ui.ShowNotification(HudNotification.CreateTextNotification(
        icon, text, NotificationPriority.Normal, NotificationType.Error));

_ui.ShowNotification(HudNotification.CreateMaterialNotification(
        icon, item.DisplayName, (int)transferredAmount, (int)totalAmount));
```

`CreateMaterialNotification` already renders "component + amount / total", which is exactly the
withdrawal feedback Build Planner needs. `DisplayFull()` in that class is the shipped
"inventory full" warning.

## Session and character access (verified in game, 2026-08-22)

See `client-server-split.md` for the full account. Summary:

- `GameCoreScene` exposes `GameClient` and `GameServer`; both run in-process in single player and
  each owns a `Session` via `Get<WorldSessionComponent>().OwnedSession`.
- Inventories live on the **server** session's character; the block placer and UI live on the
  **client** session's. Both characters report the debug name `CompositeCharacterServer`.
- `Session.GetEntitiesOfType<T>()` is public; `QueryAllEntities()` is internal.
- `entity.FirstOrDefault<T>()` resolves interfaces; `TryGet<T>(StringId tag)` needs the tag.
- Use `SessionComponents.TryGet<T>()`, never `Get<T>()` — the server session has
  `PlayersSessionComponent` where the client has `ClientPlayersSessionComponent`, and `Get` throws.

## Confirmed working end to end

`InventoryComponent.FindMissingItems` + `TransferByDef` perform the withdrawal exactly as intended.
Observed: one queued Light Armor Cube resolved to `1 x Steel Plate`, shortfall computed as 1, one
plate moved from a `CargoContainer750` into the player's inventory across a 25-inventory conveyor
sweep.
