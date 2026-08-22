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

## Reaching the HUD from a plugin (verified in game 2026-08-22)

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

Confirmed in game: notifications appear on screen, including the production and reach messages.

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

## The engine's build planner data IS wired to the terminal (2026-08-22)

An earlier note in this repo said `BuildPlannerData` was dead — "nothing populates or reads it".
That is **half right and the wrong half matters**: nothing *populates* it, but the terminal screen
already *reads* it.

`Keen.Game2.Client.UI.TerminalScreen.TerminalScreenViewModel` holds:

- `_buildPlannerData : BuildPlannerData`
- `BuildPlannerBlocks : AvaloniaList<BuildPlannerBlockModel>` (bound to the UI)
- `UpdateBuildPlannerBlocks(object, PropertyChangedEventArgs)`
- `BuildPlannerBlock_ClearAll()`, `BuildPlannerBlock_ScheduleAll()`,
  `ProduceBuildPlannerBlock(...)`, `RemoveBuildPlannerBlock(...)`

So Keen shipped the screen and left the data empty. Filling
`BuildPlannerData.PlannedBlocks` is how a mod gets queue visibility without building any UI.

**Reaching it:**

```csharp
IPerPlayerData.GetPerPlayerData<BuildPlannerData>(IdentityId)   // Game2.Simulation.GameSystems.Player
```

- the `IPerPlayerData` service is borrowed from `IntegrityToolUIComponent._playerData`, so it is the
  same instance the game uses
- the identity is `ClientPlayersSessionComponent.LocalPlayerIdentity` (public property)

**Method-of-discovery warning.** This was nearly missed twice. A reflection scan for "who references
`BuildPlannerData`" was piped through `head -10`, and ten `BuildPlannerData+Serializer` entries filled
the window — making it look like only serialization touched it. **When scanning for references,
exclude the type's own nested types before truncating**, or the answer hides behind its own
boilerplate.

**Still unverified:** whether an already-open terminal refreshes (`UpdateBuildPlannerBlocks` is a
`PropertyChanged` handler and `List.Add` raises no event), and what the second parameter of
`AddPlannedBlock(CubeBlockDefinition, int)` means — count or index. The mirror writes to the
`PlannedBlocks` list directly for that reason.

## Production: the engine already cascades sub-components (2026-08-22)

**The single most important fact for SHIFT-produce, and it removes most of the work.** A mod does
not need to walk the recipe tree (plate → ingot → ore) and enqueue each tier on the right block.
`ItemConverterComponent` does that itself, and reimplementing it would duplicate engine behaviour
that already handles load-spreading and cancellation.

### The API

```csharp
// Keen.Game2.Simulation.WorldObjects.CubeBlocks.Production.ItemConverters.ItemConverterComponent
public bool TryEnqueueRecipe(ItemRecipeDefinition recipe, int repeatTimes)   // public
public readonly ItemConverterDefinition Definition;
public readonly InventoryComponent InputInventory;
public readonly InventoryComponent OutputInventory;
public bool Crafting { get; }
public bool DequeueRecipe(int index)
public bool DequeueCurrentRecipe()
```

`TryEnqueueRecipe` returns false only when the queue is at `Definition.MaxQueueSize` and the recipe
cannot be merged into the last entry (identical recipe + identical requester merge by bumping
`Times`). Assembler500 ships `MaxQueueSize: 20`.

This is the same call the terminal makes.
`StreamedProductionInfoSessionComponent.TryEnqueueAsync(model, recipe, times)` does `MoveTo.Server`,
validates the recipe is in the converter's own `Definition.RecipeDefinitions`, then calls
`TryEnqueueRecipe`. Running on the server session in-process, a plugin can call it directly.

### The cascade, and what triggers it

`TryEnqueueRecipe` takes `GetWritePtr<ConversionQueueData>()`. That marks the data changed, which
fires the DCS job:

```csharp
[OnAdded(typeof(ConversionQueueData))]
[OnChanged(typeof(ConversionQueueData))]
[OnChanged(typeof(ConversionQueueItem))]
private void OnRecipeCompletedOrEnqueued(...)
{
    TryStartNextRecipe(...);
    MarkChildRequestsDirty();      // <- sets RefreshChildRequestsTag
}
```

`RefreshChildRequestsTag` schedules `UpdateRequestsWhileEnabled`, which in order:

1. `AccumulateIngredientsForFullQueue` — totals the inputs the whole queue needs
2. `PreventItemsFromBeingPulled` / `RemoveItemsAlreadyInInventory` — subtracts what it holds
3. `LimitItemsToRequestByAvailableInventorySpace`
4. `UpdatePersistentRequests` — raises conveyor pull requests, so the block feeds itself
5. `EnsureConnectedConverterCacheIsUpdated` + `UpdateChildRequests` — **delegation**

`UpdateChildRequests` finds converters that can produce each still-missing input and calls the
*private* overload `TryEnqueueRecipe(recipe, count, requester)` with itself as `requester`. The
child's queue change marks the child dirty in turn, so the cascade recurses to ore without the
caller knowing any intermediate recipe exists.

Also handled for free:
- **Load spreading** — `UpdateRequestsInAvailableConverters` divides the demand by the number of
  capable converters and rounds up per converter with `(int)FixedPoint.Ceiling(amount / perRun)`.
- **Retraction** — `ClearChildRequestsOfPreviousChildren` + `RemoveQueuedItemsFromRequester` pull
  the child requests back when the parent's queue changes.
- **Merging** — a repeat request bumps `Times` via `UpdateAlreadyProducingChildRecipeWithCount`
  rather than queueing a duplicate.

**Scope is the conveyor group, not the grid.** `EnsureConnectedConverterCacheIsUpdated` iterates
`_conveyorComponent.ConveyorSystem.TryGetGroup(Entity.DEntity).Blocks`. The mod's own converter
search is grid-wide (it reuses the withdrawal's `InventorySystemComponent.Inventories` sweep), so
the two differ — recorded as a known gap in `BuildPlanner/README.md`.

### Item → recipe

`ItemConverterComponent.CanProduceItem(ItemDefinition)` does exactly this and is **private**. The
same walk over public collections is what `TryEnqueueAsync` uses, so reimplementing it is using the
intended surface, not working around one:

```
Definition.RecipeDefinitions            // ListDictionaryReader<ItemConverterRecipeCategoryDefinition, ItemRecipesDefinition>
  -> category.Value                     // ListReader<ItemRecipesDefinition>
    -> recipes.Recipes                  // ImmutableArray<ItemRecipeDefinition>
      -> recipe.Outputs                 // ImmutableArray<ItemAmount>, match Item and Amount > 0
```

**Both readers are value types.** `RecipeDefinitions` is a `ListDictionaryReader<,>` and its values
are `ListReader<>`; neither can be compared to null. The decompiled iteration reads as
`KeyValuePair<category, ListReader<...>>`, which looks reference-like and is not — the compiler
caught this, nothing else would have.

`ItemRecipeDefinition` exposes `Inputs`, `Outputs`, `TimeToConvert`, `Icon`, `DisplayNameOverride`.
In the `.def` JSON, `Inputs`/`Outputs`/`Recipes` all serialize as Key/Value pairs but are flat
`ImmutableArray`s at runtime.

### The personal crafter is a separate system

`Keen.Game2.Simulation.WorldObjects.Tools.BackpackItemConverterComponent` (`[ServerOnly]`,
`[DefaultTag("IBackpackItemConverter")]`) is the welder's ad-hoc crafting, not the assembler:

```csharp
ItemRecipeDefinition? TryGetRecipe(ReadOnlySpan<ItemAmount> itemsNeededNow, ReadOnlySpan<ItemAmount> allMissingItems)
Task<bool> CraftRecipe(ItemRecipeDefinition recipe)     // one at a time; no-op while IsCrafting
void StopItemConversion()
```

`TryGetRecipe` is tantalisingly close to what Build Planner wants ("the next recipe that should run
based on the components that are needed") but crafts a **single** recipe into the player's own
inventory, has no queue, and signals `ShowCraftablesMissingIngredients` / `ShowUncraftable` on
failure. `RecipeHelper.TryCraft(recipe, inventory, efficiencyMultiplier, craftsCount)` is the
underlying primitive and is public. Not used by SHIFT-produce, which targets assemblers — but it is
the right entry point if an "instant craft from ore in your backpack" variant is ever wanted.

## `InventorySystemComponent.Inventories` is grid-wide, NOT the conveyor network (2026-08-22)

**A name that means the opposite of what it looks like, and it produced a real bug.**

```csharp
// InventorySystemComponent
private HashSet<InventoryComponent> _inventories = new HashSet<InventoryComponent>();
public HashSetReader<InventoryComponent> Inventories { get; private set; }

private void OnBlocksChanged(CubeGridComponent.BlocksChangedArgs blocks)
{
    foreach (var removed in blocks.RemovedBlocks) RemoveInventories(removed);
    foreach (var added   in blocks.AddedBlocks)   AddInventories(added);
}
```

`AddInventories` adds `block.Entity.All<InventoryComponent>()` for **every block on the grid**.
Conveyor plumbing is never consulted. So iterating `Inventories` gives all storage on the grid,
connected or not.

**Observed symptom:** an empty container welded onto a ship and attached to nothing still withdrew
from the entire ship and still queued work at its assemblers. A container on a *separate* grid
behaved correctly — but only because a different grid has a different `InventorySystemComponent`,
so the one case that looked right was right for the wrong reason.

**The conveyor-scoped API instead:**

```csharp
// Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Conveyors.ConveyorSystemComponent
public static ReachableInventoriesEnumerator IterateReachableInventories(
    InventoryComponent invStart, ItemDefinition? filterItem,
    bool followEdgeDirection = true, bool mustContainTheItem = false)

public static ReachableInventoriesEnumerator IterateReachableInventories(
    int startNodeIdx, ConveyorSystemComponent systemFrom, ItemDefinition? filterItem,
    bool followEdgeDirection = true, bool mustContainTheItem = false,
    InventoryComponent? ignoreInventory = null)
```

Documented parameter semantics, straight from the XML:

- `followEdgeDirection` — "direction of the search. If true, search inventories that are **reachable
  from** start. If false, search inventories that **can reach** start." **A withdrawal wants
  `false`.** This is not symmetric: conveyor edges are directional.
- `filterItem` — "item that must be able to pass through, **if null, filters are ignored**". Passing
  null gives topological reachability, so the walk can run once per action instead of once per item.
- `mustContainTheItem` — restricts to inventories holding the item.
- The 4-arg overload passes `invStart` as `ignoreInventory`, so **the start inventory is excluded**
  from the results; callers must add it themselves.
- Returns `ReachableInventoriesEnumerator.Empty` when `invStart.ConveyorSystem` is null — the normal
  case for a lone container, not an error.

Every engine transfer path uses this, which is the tell that it is the intended surface:
`PullAsync` uses `(invTo, itemDef, followEdgeDirection: false, mustContainTheItem: true)`,
`PushAsync`/`PushAllAsync` use the default direction.

`ConveyorSystemComponent.TryGetGroup(DEntity block, int subgraphNodeId = 0)` returns a
`ConveyorGroup` whose `Blocks` is what `ItemConverterComponent` iterates for its child-request
delegation — the same scope, reached a different way.

**Lesson, and it is CLAUDE.md's "Bias Against Confirmation".** `InventorySystemComponent.Inventories`
was found first, was plausible, produced working withdrawals in every test on a normally-plumbed
base, and was wrong. The first file found is rarely the whole story; the check that would have caught
it is "what does the engine itself call to do this?", which points at the conveyor graph every time.

## Custom input actions are keyed by GUID, and must be registered (2026-08-22)

A plugin action that is not in the `DefinitionManager` **cannot be rebound**. The controls menu
accepts the new key, writes it to disk, and the game goes on using the old one. This was the reported
bug: "our N hotkey is in the settings -> controls menu. I tried remapping it and nothing changed."

### Evidence

`%APPDATA%\SpaceEngineers2\AppData\EngineOptions\CustomizedControlsOptionsPart`, after a rebind of
the old plugin action:

```json
{ "Action": "00000000-0000-0000-0000-000000000000",
  "PrimaryControl": { "$Type": "VRage:Keen.VRage.Input.DigitalControl", "Input": "Mouse::Middle" },
  "SecondaryControl": null }
```

### The mechanism

`CustomizedControlsOptionsPart.ActionControlEntry` (VRage.Input.dll) stores the action as a GUID:

```csharp
[Serialize(Name = "Action")] private Guid _actionGuid;
public InputActionDefinition Action {
    get {
        if (Singleton<DefinitionManager>.Instance.TryGetDefinition(_actionGuid, out InputActionDefinition d))
            return d;
        Assert.Fail("Failed to locate definition with Guid {_actionGuid}", ...);
        return _placeholder;                       // a throwaway runtime definition
    }
    private set => _actionGuid = value.Guid;
}
```

and `ControlCustomizationEngineComponent.UpdateMappings` applies customisations only to actions the
base mapping already contains:

```csharp
ActionControlMapping.Builder builder = _baseMappings.ToBuilder();
foreach (var (action, primary, secondary) in _customizedControls.CustomizedControls) {
    if (builder.RemoveAction(action)) {          // placeholder -> false -> entry silently dropped
        if (primary != null) builder.AddControl(action, primary);
        if (secondary != null) builder.AddControl(action, secondary);
    }
}
_actionProcessor.SetMapping(builder.MoveToMapping());
```

`new InputActionDefinition(displayName, type)` leaves `Definition.Guid` as `Guid.Empty` — the
constructor sets only `DisplayName`, `Name`, `ExpectedInputType`, `Reactivate`. So every rebind of a
plugin action was written as `Guid.Empty` and thrown away on the way back.

### The fix

Build the definition from an object builder and keep the GUID:

```csharp
var builder = new InputActionDefinitionObjectBuilder {
    Guid = ourGuid, Name = StringId.Get("BuildPlannerWithdraw"),
    DisplayName = LocKey.FromString("Build Planner: Withdraw"),
    ExpectedInputType = InputType.Digital, Category = buildingControls,
};
var action = RuntimeDefinitionHelper.Create<InputActionDefinition>(builder, context: null, keepBuilderGuid: true);
```

`RuntimeDefinitionHelper.Create` runs the engine's own `Init`/`PostInit` and asserts
`instance.Guid == builder.Guid`. Without `keepBuilderGuid: true` it *clears* the builder's GUID —
which is why the placeholder in `CustomizedControlsOptionsPart` has `Guid.Empty`.

Then make it resolvable. There is no public registration API — `PushDefinitionSetAsync` wants an
`IDefinitionObjectBuilderLocator` and a full async load — but the lookup is a plain dictionary:

```
DefinitionManager.TryGetDefinition -> TryGetAnyDefinitionInternal -> DefinitionSet.TryGetAnyDefinition
DefinitionSet.TryGetAnyDefinition == _definitionsById.TryGetValue(id, out definition)   // verified in IL
```

so the definitions are inserted into `DefinitionSet._definitionsById` of the set that already holds
vanilla's input actions (located by looking up a known action GUID rather than by set name). That
gives them exactly that set's lifetime.

### Chords are real bindings, not modifier sampling

`DigitalCompositeInputControl(mainInput, modifiers)` is how vanilla expresses ALT+F4 and SHIFT+F5 in
`Assets/MainMenuData/Input/ActionControlMapping.def`. `InputControlComposer.KeyboardDefault`
(valid modifiers: Alt, Control, Shift) builds the same thing at runtime —
`Compose(action, mainInput, modifiers)` — and is what the rebinding dialog itself uses
(`InputCompositionDialogViewModel.ProcessInput` → `TryComposeFromActive`), so chord *rebinding* works
out of the box.

Disambiguation is automatic: `DisambiguatingControlActivationFilter.StartFrame` records, per input,
the largest number of inputs any candidate control has, and `FilterOnControl` cancels a control when
`_maxInputs[input] > control.Inputs.Length` ("Discard candidate control …, too few inputs"). So plain
N is cancelled in the frame where SHIFT+N is active.

### Two mapping entries may share an input

`ActionControlMapping.Builder.AddControl` throws only on the *same control instance*: "Each action
must be bound to a unique control instance, event if those instances share the same inputs." Two
distinct `DigitalControl(Mouse::Right)` objects bound to two actions are legal — they then compete
in the filter, and `DispatchActions` walks `_activeContexts` **backwards**, so a layer-less context
(appended past the named layers) is processed before vanilla's and takes the input.

### Where the controls menu comes from

`ControlCustomizationViewModel.SetMapping` groups `mapping.ControlsPerAction` by
`Definition.Category`, dropping null and the hidden category, ordering groups by
`ActionCategoryConfiguration.OrderedControlCategories` and actions by `Definition.Name.String`.
`ControlsViewModel` (Game2.Client) snapshots `ActionsPerCategory` in its constructor. Publishing
through `ControlCustomizationEngineComponent.SetMapping` (rather than straight into the processor)
is therefore what makes a late-arriving action appear in the menu — it rebuilds that view model and
re-applies customisations in one go.

### Confirmed in game (2026-08-22)

One session exercised the whole path. All nine actions dispatched on their own bindings; chords
resolved correctly against each other at one, two, three and four inputs; the orphaned `Guid.Empty`
entry was purged once and never reappeared.

Then all eight keyboard actions were rebound onto `Mouse::Middle` and its chords and **survived a
restart** — the plugin read them back out of the mapping at startup:

```
  bound BuildPlannerWithdraw to Mouse::Middle
  bound BuildPlannerWithdrawKeep to Mouse::Middle+Keyboard::Control
  bound BuildPlannerDiagnose to Mouse::Middle+Keyboard::Control+Keyboard::Shift+Keyboard::Alt
```

The options file holds real GUIDs and `CompositeInputControl\`1<System.Boolean>` payloads for the
chords, which is the round trip that used to fail.

**Consequence worth knowing:** plain `Mouse::Middle` *is* usable, contradicting an earlier note in
the README. Vanilla binds it to three actions (`ba689cc1` ToolTertiary, `6a759ebb`, `9ad853aa`), but
a layer-less context is dispatched before them (`DispatchActions` walks `_activeContexts` backwards),
so the mod wins the button — at the cost of suppressing those three while a Build Planner action sits
on plain middle-click. Chorded middle-click costs nothing, as no vanilla action uses a modifier with
that button. The shipped defaults stay on `N` so the mod does not take the button uninvited.

## The build menu's tile hierarchy, and how to act on a right-click (2026-08-22)

Queueing a block from the build menu (G) cannot be done through the input system, and the tiles are
not what they look like. Both facts were settled in game rather than by reading.

### Right-click is already spoken for while the menu is open

The engine's own trace (`ActionProcessorDebugObject.DetailedInputLog`) during a menu right-click:

```
[Input][#4028]: Control Keyboard::G : Build Menu activated with state Start.
[Input][#4061]: Consuming input Mouse::Right in layer #26:<Uninitialized>
[Input][#4061]: Control Mouse::Right : CursorButton2 activated with state Start.
```

An input is consumed by exactly one context per frame and a layer-less context is dispatched first,
so activating a mod context here **takes right-click away from the menu**. That is not acceptable:
right-clicking a toolbar slot clears it (confirmed in game by the user).

The way through is to hook the menu's own Avalonia handlers on `GScreen`
(`Keen.Game2.Client.UI.TerminalScreen.GScreen.GScreen` — the type and its namespace share a name, so
C# needs an alias):

| Handler | Fires for |
|---|---|
| `TilePressed(object?, PointerPressedEventArgs)` | catalogue tiles in the central panel |
| `SizeTilePressed` | size tiles in the right-hand detail panel |
| `SubTilePressed` | kind sub-tiles in the detail panel |
| `OnToolbarTilePointerPressed` | **the toolbar** — a separate path, so it is untouched |

Run the original first (it records the drag origin), then read `e.GetCurrentPoint(null)
.Properties.IsRightButtonPressed` and `(sender as IDataContextProvider)?.DataContext as TileModel`.

`TilesPanel` attaches its handler to the tile control *and* re-adds it to every prepared container
(`OnContainerPrepared`), so one press can reach the handler twice. Vanilla does not care — its
handler only stores a point — but anything with a side effect must de-duplicate on
`PointerEventArgs.Timestamp`.

### Three tile types, only one of which is a block

Observed live, which corrected a wrong first implementation that refused the grid's own tiles:

```
menu right-click on BlockGroupTileModel 'Battery' — not a block
menu right-click on BlockKindTileModel 'Battery' with 3 variant(s):
    variant: 'Battery 0.5 m' unlocked=True
    variant: 'Battery 1.5 m' unlocked=True
    variant: 'Battery 2.5 m' unlocked=True
```

- `BlockGroupTileModel.BlockKinds` → `ImmutableArray<BlockKindTileModel>` — the grid tiles ('+').
- `BlockKindTileModel.Blocks` → `ImmutableArray<BlockTileModel>` — the grid sizes.
- `BlockTileModel.Block` → `EntityCompositeDefinition` (**internal**, so reflection), and
  `EntityObjectBuilderFunctions.TryGetDefinition<CubeBlockComponent, CubeBlockDefinition>(composite)`
  pulls out the definition, exactly as `BlockTileModel`'s constructor does.
- `BlockSizeModel.Block` → the concrete `BlockTileModel` for one size.

**Naming trap:** every grid size shares one `CubeBlockDefinition.UIData.Name` — all three batteries
are "Battery". The per-size name is on the tile (`LocalizableBlockTypeDisplayName`), so any message
about which variant was queued has to come from the tile, not the definition.
