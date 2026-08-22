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

## Reaching the HUD from a plugin (metadata-verified 2026-08-22, not yet observed in game)

`InGameUI` is the HUD entry point, and `ShowNotification` is documented in the shipped XML:

```
M:Keen.Game2.Client.UI.InGame.InGameUI.ShowNotification(Keen.Game2.Client.UI.HUD.Notification.HudNotification)
```

The problem is obtaining an instance: `InGameUI` is injected as a service and has no public
accessor reachable from plugin code.

**The route that works:** borrow the reference a vanilla session component already holds.
`Keen.Game2.Client.WorldObjects.Items.InventoryNotificationsSessionComponent` extends
`Keen.VRage.Core.Game.Components.SessionComponent` and stores the UI in a private field:

```
FIELD _ui : InGameUI        [Private, InitOnly]
```

Both facts were read directly out of `Game2.Client.dll` metadata (`System.Reflection.Metadata` over
the PE file), not inferred from the XML docs, which do not list private fields.

So:

```csharp
var clientSession = /* GameCoreScene.GameClient's OwnedSession */;
var notifications = clientSession.SessionComponents
    ?.TryGet<InventoryNotificationsSessionComponent>();     // TryGet, never Get
var ui = /* reflect _ui off notifications */;
ui.ShowNotification(notification);
```

**It must be the CLIENT session.** `InGameUI` is `Game2.Client`; the server session has no UI. This
is the same split as the block placer — see `client-server-split.md`.

Resolve it per call, never cache: it is null at the main menu and is replaced on every world load.

`HudNotification` is a **struct** whose `Content` is `Nullable<LocKey>`, so `notification.Content`
needs a null check before `ToString`.

Not yet confirmed in game — the call compiles against the real assemblies but has not been observed
putting text on screen. Until it is, treat this as verified-by-metadata only.

## The block placer is NOT on the character (root cause, 2026-08-22)

Two user-reported symptoms turned out to be one bug:

1. Right-clicking a projection queued nothing.
2. Right-clicking an armor block queued the wrong block, so `N` withdrew 1 x Steel Plate
   instead of the ~50 the intended block needed.

**Evidence:** every right-click in the log, without exception, printed

```
debug: no BlockPlacerEntityComponent on character or its parents
```

So `GetAlignedBlockTarget` never returned a target and `OnSecondaryAction` *always* fell through to
its fallback path. That fallback used
`character.FirstOrDefault<IInteractedEntityProvider>().InteractedEntity`, which the XML docs define
as "What entity is being interacted with" — the press-F interaction target, **not** the block under
the crosshair. It therefore queued a plausible-but-wrong block on every use.

Projections have no entity at all (`ProjectionBlockPlacementTarget` is a "Non-real block"), so the
fallback could never queue one — symptom 1. And because withdrawal is exact, a wrong queue produces a
confidently wrong amount — symptom 2.

**Fixes:**

- `BlockPlacerEntityComponent` is a `GameComponent` (entity-level, per Game2.Client.dll metadata) but
  does not hang off the character or its ancestors. It is now located by enumerating the **client**
  session: `clientSession.GetEntitiesOfType<BlockPlacerEntityComponent>()`, mirroring how the
  character inventory is found by enumerating the server session.
- **The interaction-based fallback was deleted entirely.** Queueing the wrong block is worse than
  queueing nothing, because the exactness of the withdrawal turns it into a silent wrong answer.
  With no target, the mod now says "not looking at an unfinished block" and queues nothing.

**Lesson, and it is the CLAUDE.md one again:** a fallback that always produces *something* converts a
hard failure into a silent wrong answer. The "no placer" line was printing on every click from the
very first test run and was read as a projections-only problem; it was in fact reporting that the
primary path never worked at all. Two failing lookups on the same object were evidence about the
object.

`requires N x <item>` is now logged unconditionally for exactly this reason — it is the line that
makes a wrong queue obvious at a glance.


## Recipes: CriticalItems is a proportion, Items is the recipe (2026-08-22)

**The longest-lived bug in this feature.** The queue summed
`CubeBlockDefinition.Recipe.CriticalItems` on the assumption it was the block's component list. It
is not. `CubeBlockRecipeDefinition` is documented as:

> Defines the **proportions** and criticality of items needed to build a given CubeBlockComponent.
> It is used to generate the final recipe based on mass, efficiency and rounding amounts.

So it is a ratio, shared by every block that references that recipe. Use instead:

> `CubeBlockDefinition.Items` — Collection of items necessary to build the block. **Computed when
> definition is post-processed.** Index 0 = Lowest Integrity

Symptom: a 2.5m light armour cube asked for `1 x Steel Plate` instead of 30. Because the withdrawal
is exact, the player received exactly one plate. It survived three rounds of debugging because "1 x
Steel Plate" is a *plausible* answer — it never looked like a parse failure, and it sent the
investigation after a block-identity bug instead.

Related, on the same definition: `TotalItemAmount`, `OptionalItemAmount`, `RecipeEfficiency`,
`RecipeRoundingConfiguration`. Whether `Items` includes optional components is **not yet confirmed**.

## Finding what the player is aiming at (2026-08-22)

Three routes were tried. Only the third works:

| Route | Result |
|---|---|
| `BlockPlacerEntityComponent` via the character hierarchy | Never found — logged "no BlockPlacerEntityComponent on character or its parents" on every click |
| Same, via `clientSession.GetEntitiesOfType<BlockPlacerEntityComponent>()` | Also empty |
| `IInteractedEntityProvider` on the **server character** | Returns the press-F interaction target, not the crosshair block |
| **`IntegrityToolUIComponent`** (the component behind the block panel) | Works |

`Keen.Game2.Client.WorldObjects.Tools.IntegrityToolUIComponent` is the component that supplies the
"you need N x Steel Plate" panel. It holds:

- `_interactedEntityProvider : InteractedEntityProviderComponent` — the **tool's** provider, whose
  public `InteractedEntity` is the block being aimed at. This is the primary read.
- `_model : BlockIntegrityScreenModel` — whose `Blocks` is
  `PooledList<(CubeBlockComponent, CubeBlockDefinition)>`. Used only for what one entity cannot
  express: area-welder multi-block selection, and projections with no built entity.
- `_screen : IObservableDisposable` — non-null while the block panel is open.
- `_projections : GridProjectionsSessionComponent`, `_areaDetector` — why projections and area
  welding come along for free.

The instance is captured by a MonoMod detour on its private `UpdateUI(Entity)`; there is no lookup,
which is the point after two failed ones. It is released on its own `CloseHUD()`.

**Gate queueing on `_screen`.** Without it the capture is sticky: after switching to block placement
mode the component stays captured and right-click keeps queueing, in a mode where the game already
binds RMB.
