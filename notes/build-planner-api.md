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

**Unverified when this was written**, both since settled: whether an already-open terminal refreshes
(`UpdateBuildPlannerBlocks` is a `PropertyChanged` handler and `List.Add` raises no event), and what
the second parameter of `AddPlannedBlock(CubeBlockDefinition, int)` means — count or index.

*Answers:* the second parameter is a **count**, defaulting to 1
(`AddPlannedBlock(CubeBlockDefinition block, int count = 1)`), and both mutators end in
`OnPropertyChanged("PlannedBlocks")`. An open terminal **does** refresh live — but only because this
mod subscribes to that notification itself; nothing in the shipping game ever did. Confirmed in game
2026-08-22, with the panel's list tracking each queued block as it was added.

## The terminal's build planner panel is complete but switched off (2026-08-22)

**The UI is already built.** `TerminalScreen.axaml` contains the whole panel; Keen did not leave it
to be written. Decompiled from `Game2.Client.dll` (`ilspycmd -t
Keen.Game2.Client.UI.TerminalScreen.TerminalScreen`), the tree is:

```
LayoutTimer  Label="Terminal.BuildPlanner"   Grid.Row=1
└── Grid   VerticalAlignment=Bottom  HorizontalAlignment=Right
           Margin=20,0,20,0  Background=#00FFFFFF  IsVisible=FALSE
    ├── Border            Classes="Static TerminalOpacity"
    └── StackPanel        Margin=10  Spacing=8
        ├── TextBlock     "Build"
        ├── TextBlock     "Planner"
        ├── TextBlock     "(WIP Pos)"
        ├── Button        "Produce"  Command={Binding BuildPlannerBlock_ScheduleAll}
        ├── ItemsControl  ItemsSource={CompiledBinding BuildPlannerBlocks}
        │   └── ItemTemplate → BuildPlannerIconControl
        │           Icon          ={Binding Icon}
        │           ProduceCommand={Binding ProduceBuildPlannerBlock}
        │           RemoveCommand ={Binding RemoveBuildPlannerBlock}
        └── Button        "Clear"    Command={Binding BuildPlannerBlock_ClearAll}
```

That is the SE1 build planner's terminal affordance, finished, styled with the terminal's own
resources, and wired to all four view-model verbs. **Exactly three things stop it working**, and all
three are reachable from a plugin:

1. **`IsVisible = false` is a literal, not a binding.** In the decompiled `InitializeComponent`:
   `grid29.IsVisible = false;` — IL `IL_1336: ldc.i4.0` / `IL_1337: callvirt Visual::set_IsVisible`.
   No style, no trigger, no condition. The `(WIP Pos)` label next to it is Keen's own note that the
   panel is parked mid-development.
2. **`TerminalScreenViewModel._buildPlannerData` is never assigned.** The field is
   `.field private initonly`; across the *entire* `Game2.Client.dll` IL there are six `ldfld`s and
   **zero `stfld`s**. It is always null, so `BuildPlannerBlock_ScheduleAll`, `BuildPlannerBlock_ClearAll`,
   `ProduceBuildPlannerBlock` and `RemoveBuildPlannerBlock` would each throw `NullReferenceException`
   on the first button press.
3. **`UpdateBuildPlannerBlocks` is never subscribed.** It has the right shape for
   `BuildPlannerData.PropertyChanged` (`PerPlayerData : ObservableObject : INotifyPropertyChanged`),
   but there is **no `ldftn` reference to it anywhere** — nothing hands it to any event. So
   `BuildPlannerBlocks` would stay empty even with the data present.

**How the mod supplies all three** (`src/TerminalPlannerPanel.cs`):

- hook `TerminalScreen.InitializeComponent(bool)` — public, and the moment the XAML tree exists
- find the panel by `LayoutTimer.Label == "Terminal.BuildPlanner"` (the Grid has no `x:Name`), set
  its `Child.IsVisible = true`, and hide the `(WIP Pos)` label
- set `_buildPlannerData` with `[UnsafeAccessor(UnsafeAccessorKind.Field)]` — it is `initonly`, so
  this is the supported way in on net9.0, and it is type-checked at JIT time
- fill `BuildPlannerBlocks` ourselves on `data.PropertyChanged`, using public API only
  (`BuildPlannerBlocks` has a public getter, `BuildPlannerBlockModel(CubeBlockDefinition)` is public)
  rather than reflecting on Keen's private `UpdateBuildPlannerBlocks`

**Timing traps, both real:**

- `TerminalScreen` is a `UserControl` (`ScreenView : ViewBase : UserControl`). After
  `InitializeComponent` the panel is in the **logical** tree, but content only enters the **visual**
  tree when the template is applied — a visual-tree search at that moment finds nothing. Search
  logical first, and retry on `AttachedToVisualTree`.
- `DataContext` is assigned by `ScreenView` (`base.DataContext = dataContext`) separately from
  construction, so the view model may not be there yet either. Retry on `DataContextChanged`.
  `TerminalScreen` is `IReusableScreen`, so the same control returns with a *new* view model — the
  old `PropertyChanged` subscription has to be released or one dead view model accumulates per
  terminal opened.

### How much of the *functionality* did Keen ship? Almost none.

The panel is finished; the machinery behind it is not. Reading the four verbs settles what this mod
duplicates and what it adds.

**`BuildPlannerBlock_ClearAll` / `RemoveBuildPlannerBlock`** — real, and trivial: `RemovePlannedBlock`
on the list.

**`TryScheduleBlockForProduction(CubeBlockDefinition)`** — the only genuine overlap with this mod, and
it is far narrower than `ComponentProduction`:

```csharp
bool flag = true;                                     // seeded TRUE
foreach (ItemAmount current in block.Items)
    flag |= TryScheduleItem(current.Item, current.Amount);
return flag;                                          // therefore ALWAYS true
```

1. **It only works with the production screen open.** `TryScheduleItem` starts
   `if (ProductionScreen?.StreamedModel.Definition == null) return false;` — so the player must be at
   a converter's terminal, on the production tab. There is no "aim at any conveyor-connected block".
2. **One converter only** — whatever `StreamedModel` that screen is showing. No reachability search,
   no next-converter fallback when a queue is full.
3. **Full recipe, not the remainder.** It walks `block.Items`, so a half-welded block re-queues
   everything already in it. `BlockRequirements.Remaining` exists in this mod for exactly that.
4. **The return value is broken.** `flag` is seeded `true` and combined with `|=`, so it cannot ever
   be false. `ProduceBuildPlannerBlock` uses it to decide whether to drop the block from the queue —
   so the block is removed whether or not anything was actually scheduled.

**Nothing populates the queue.** `AddPlannedBlock` has **zero callers in `Game2.Client.dll`**
(IL-verified), no input action, and no localized string. The queueing half is absent, not hidden.

**Withdrawal — the actual point of the Build Planner — does not exist anywhere in the engine.**
No shipped code pulls a block's missing components into the player's inventory. Nor does deposit,
×10, keep-queue, or any HUD feedback.

So the mod duplicates one method, and that method is both more limited and buggier than its
replacement. Everything else here is net-new.

**Consequence for the panel — and what was done about it.** Left alone, its **Produce** button would
run Keen's path: silently doing nothing unless a production screen is open, then clearing the queue
regardless. All four verbs are therefore detoured and **replaced** (originals never called) with
`BuildPlannerController.ProduceQueueFromTerminal`, `ProduceOneFromTerminal`,
`RemoveQueuedFromTerminal` and `ClearQueueFromTerminal`, so the buttons run the same code as the
keybinds and mutate one queue rather than two.

Two departures from Keen's semantics, both deliberate: produce does **not** clear the queue (matching
`SHIFT+N` — production only starts the components, and the player still has to withdraw them), and
reachability is rooted at `TerminalScreenViewModel.Interacted` (a public property) rather than the
interaction provider, since with a terminal open the player is looking at a screen rather than aiming
at anything.

**`UpdateBuildPlannerBlocks` caps the display at ten:**
`for (i = 0; i < Math.Min(10, plannedBlocks.Count); i++)`. That is a layout constraint, not a
preference — the ItemsPanel is a **vertical** `StackPanel` (no `Orientation` set) in a bottom-anchored
Grid with no scroll viewer, so an uncapped list grows off the top of the screen.
`TerminalPlannerPanel` matches the ten and logs when it is showing fewer blocks than are queued.

### What a live session found (2026-08-22)

The panel renders: a vertical list of block icons on the right, growing upward, in the terminal
shell. It is plain, and it overlaps the terminal's scroll bar — consistent with `(WIP Pos)`, and
evidence that this is not the finished layout Keen intends. Serviceable as-is.

Three things the first live run settled that reading could not:

1. **There is no separate remove button.** `BuildPlannerIconControl` puts one `Button` around each
   icon and dispatches on mouse button inside `OnButtonPressed`:
   `IsLeftButtonPressed → ProduceCommand`, `IsRightButtonPressed → RemoveCommand`. This matches SE1
   (see `build-planner-ux-spec.md`), and is invisible unless you already know.
2. **`TerminalScreenViewModel.Interacted` is a CLIENT entity — do not feed it to simulation
   lookups.** Passing it as the reachability root made produce report "no assembler or refinery
   connected" at a working assembler, because `ItemConverterComponent` and `InventoryComponent` are
   `Game2.Simulation` types that live only on the server copy of the entity. See
   `client-server-split.md`; the terminal view model is `Game2.Client`, so everything it hands you is
   the client half. Reach must come from the server character's interaction provider, exactly as the
   keybind does.
3. **`EngineQueueMirror.Sync` rebuilds by remove-all/add-all, and every step notifies.** Each
   `RemovePlannedBlock`/`AddPlannedBlock` raises `OnPropertyChanged("PlannedBlocks")`, so a naive
   per-notification refresh ran ~2N+1 full rebuilds of the bound list per queued block — visible in
   the log as counts ticking 12…0 then 0…13 on one keypress. Both the mirror and the queue now batch.

**And one pre-existing bug the panel exposed.** `Withdraw` clears the queue on success but never
called the mirror, unlike every other mutation site — so a withdrawal emptied the mod's queue while
`BuildPlannerData` kept every block, and the panel went on displaying them. This had been latent
since the mirror was written; nothing surfaced it until something finally *read* the engine's copy.
`BuildPlannerQueue.Changed` now fires from the mutators themselves, so no call site can forget.

**Method of discovery, worth keeping.** The XAML is compiled to IL, not stored as text, so grepping
the DLL for `.axaml` content finds only path tables. What gave it away was Avalonia's compiled-binding
metadata, which *is* plain text in the binary:

```
!Property.Keen.Game2.Client.UI.TerminalScreen.TerminalScreenViewModel,Game2.Client.BuildPlannerBlocks
```

A record like that only exists if some XAML actually compiled a binding to that property — which
proved a real view referenced it before anything was decompiled. Grepping a binary with
`grep -a -o '.\{0,300\}NEEDLE.\{0,300\}'` is pathological on a 40 MB DLL (it ran past two minutes);
`grep -a -b -o NEEDLE` for byte offsets plus `dd` is instant.

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

## The area welder is a separate refresh path (2026-08-22)

`IntegrityToolUIComponent` serves both welders, but they reach it differently:

| Tool | Trigger | Builds the model in |
|---|---|---|
| welder | `OnEntityChanged` / `OnNewDetectionArrived` | `UpdateUI(Entity?)` |
| area welder | `AreaDetectionChanged(Entity)` | `UpdateAreaUI()` |

Hooking only `UpdateUI` meant the area welder was **never captured**, so the queue input context was
never activated and right-click stayed with the game. The symptom was total silence — no log line, no
notification — which is indistinguishable from a broken keybind and is exactly the failure mode
CLAUDE.md warns about. `AreaDetectionChanged` is the better hook of the two: `UpdateAreaUI` is
`async void`, so a detour on it returns at the first `await` regardless.

`UpdateAreaUI` builds the panel model from the whole selection:

```csharp
foreach (Entity areaBlock in _areaDetector.AreaBlocks)
    pooledList.Add((areaBlock.Get<CubeBlockComponent>(), ...Definition));
foreach (var (_, projections) in _areaDetector.AreaProjections)
    foreach (var item in projections) pooledList.Add((null, item.Item2));
```

so `_model.Blocks` is the area, projections included (with a null component, as projections have no
built block). **Read the model, not `InteractedEntityProviderComponent`** — the provider carries one
entity, so preferring it reduces an area welder to the block under the crosshair. The provider is
only worth keeping as a fallback for when the model is momentarily absent.

`CloseHUD` is not only teardown: `UpdateUI` and `UpdateAreaUI` both call it mid-refresh to dispose the
previous screen before opening the next. Anything hooking it to mean "the tool stopped showing a
block" will therefore fire constantly during normal aiming, and must be paired with a re-capture on
the update path to be self-correcting.


## Finding a block's inventory: scan by type, never by tag (2026-08-22)

Aiming at an assembler and pressing withdraw reported "not looking at a container", although the
target had resolved perfectly:

```
debug: aimed entity 'Assembler500_ServerComposition' components=12
debug: aimed entity has no InventoryComponent
```

`Entity.TryGet<T>(StringId tag)` resolves **by tag first and casts second**:

```csharp
public Component? TryGet(StringId tag) {
    if (CompositionData.TryGetValue(tag, out var index)) return Components[index];
    return null;
}
```

so it returns a component only when the tag was guessed exactly right. Our list
(`Inventory`, `InventoryIn`, `InventoryOut`) covers cargo containers but not converters, and
extending it would only move the failure to the next block type. `CompositionData` is a
`Dictionary<int,int>` of StringId hash to component index — tags are the *only* key it has.

**Scan `Entity.Components` by type instead.** It is a public `ImmutableArray<Component>`, so a plain
`is InventoryComponent` walk finds every inventory whatever its tag. (`Entity.All<TFeature>()` does
the same thing but returns a NoAlloq `SpanEnumerable`, which means referencing that assembly for no
gain. `Entity.Has<T>` asserts it is "reserved for interfaces or with conditional".)

Converters keep **more than one** inventory — vanilla ships
`Assembler500_Client_InventoryInDefinition` and `Assembler500_Client_InventoryOutDefinitionOut` — and
what a player wants to withdraw is in the *output*, so collect them all rather than the first.

The tag pass is still worth keeping ahead of the type scan, but only for ORDER: a deposit wants a
block's main or input inventory as its destination. When no tag is recognised that ordering is a
guess, and the code says so in the log rather than pretending otherwise.

## The HUD notification is one line and does not wrap (2026-08-22)

`InGameUI.ShowNotification(HudNotification)` renders through `TextNotificationViewModel` into
`HUDNotificationView`, whose compiled XAML builds the text block as:

```csharp
textBlock3.TextTrimming = TextTrimming.CharacterEllipsis;
textBlock3.TextWrapping = TextWrapping.NoWrap;
```

inside a grid column of `MinWidth = 317`, `MaxWidth = 480`, with 20px margins either side. There is
no wrapping to enable and no multi-line variant on this path: anything past roughly 440px is cut off
with an ellipsis, unreadable.

**Measured budget.** A screenshot of the live HUD settled what the pixel figure means in characters:
`Build Planner: … 17x Construction Component` (43 chars) rendered in full, while a line of about 58
was cut after roughly 50. The font is proportional, so a character count is only a proxy — the code
uses 44 deliberately under the observed maximum.

Consequences for any message this mod shows:

- **Split long lists across several notifications**; the HUD stacks them.
- **Never join two facts with a dash.** "withdrew A, B — still short C" put the more important half
  where the ellipsis falls. Two notifications, one per fact.
- **A long prefix and a long item name cannot share a line.** `Build Planner: nothing can make` is 31
  characters and `17x Construction Component` is 26. When they do not fit, emit the prefix as a header
  on its own line.
- Continuation lines drop the `Build Planner: ` prefix — on a 44 character budget it was a third of
  the line carrying no information.

`ToastNotificationDefinition` (namespace `UI.HUD.Notifications`, **plural**) is a different system
with a `Title` plus a `Content` "shown beneath the title", and may not share this constraint. It is
definition-driven, so using it from a plugin would mean creating a definition at runtime the way the
input actions do. Unexplored.
