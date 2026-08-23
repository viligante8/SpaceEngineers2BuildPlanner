# The client/server entity split — the single most important VRAGE3 fact for plugin code

This cost roughly fifteen game restarts to find. Read this before writing any plugin code that
touches the player, their inventory, or client-side systems.

## The shape of it

`GameCoreScene` (reachable from any entity via `entity.Scene?.UserObject as GameCoreScene`) exposes
**two** halves, and **both run in-process**:

```csharp
public Entity? GameServer { get; private set; }
public Entity? GameClient { get; private set; }
```

Each owns its own `Session`:

```csharp
scene.GameServer?.Get<WorldSessionComponent>()?.OwnedSession   // simulation state
scene.GameClient?.Get<WorldSessionComponent>()?.OwnedSession   // rendering, input, UI
```

## The trap

**Both sessions contain a character entity, and both report the debug name
`CompositeCharacterServer`.** They are different objects with different components:

| | client session's character | server session's character |
|---|---|---|
| `DebugName` | `CompositeCharacterServer` | `CompositeCharacterServer` |
| Component count | **58** | **52** |
| `InventoryComponent` | **absent** | **present — the player's actual items** |
| `BlockPlacerEntityComponent` | present (client-only type) | absent |
| `CharacterComponent` | present | present |

The names being identical is what made this so expensive to diagnose. Component count is currently
the only quick discriminator; prefer testing for the component you actually need.

Verified in game by planting marker items: the server character's inventory reported
`9 x Titanium, 1 x Welder Mk 2, 1 x Area Welder Mk1, 1 x Grinder Mk 3, 1 x Drill Mk 2`, matching
exactly what the player was carrying.

## Which half owns what

Assembly location is the reliable tell:

- **`Game2.Simulation.*`** → server session. `InventoryComponent`, `CubeBlockComponent`,
  `InventorySystemComponent`, recipes, items.
- **`Game2.Client.*`** → client session. `BlockPlacerEntityComponent`, `InGameUI`,
  `ClientPlayersSessionComponent`, terminal/HUD models.

A plugin that spans both (as the Build Planner does — inventory transfers *and* aiming/UI) must hold
**both sessions** and resolve each lookup against the correct one. Collapsing to a single session
breaks the other half silently.

## Finding the character in each session

They need different code, because the session components differ:

```csharp
// CLIENT session — has a local player controller
var players = clientSession.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
var controller = players?.LocalPlayerController;
// walk controller.ControlledEntities newest-first; follow SeatComponent.Pilot if seated

// SERVER session — no local controller; enumerate instead
foreach (var entity in serverSession.GetEntitiesOfType<CharacterComponent>())
    if (entity.FirstOrDefault<InventoryComponent>() != null) return entity;
```

**Use `TryGet`, never `Get`,** for session components. The server session holds
`PlayersSessionComponent` while the client holds `ClientPlayersSessionComponent`; asking a server
session for the client type throws:

```
InvalidCastException: Unable to cast 'PlayersSessionComponent' to 'ClientPlayersSessionComponent'
```

## Component lookup rules learned along the way

- **`entity.FirstOrDefault<T>()`** (from `EntityFunctions`, predicate optional) resolves interfaces
  and is what shipping code uses. `TryGet<T>(StringId tag = default)` matches only concrete
  `Component` types and needs the tag when a composite defines one.
- **`Session.GetEntitiesOfType<T>()`** is public and is how mission code
  (`ItemsInPlayerInventoryProgressTrackerComponent`) finds players. `Session.QueryAllEntities()` is
  private — not usable from a plugin.
- **`HierarchyComponent.Children`** is public, so descendants are walkable;
  `entity.Data.TryGet<ParentData>(out var d)` then `d.GetEntity(entity.Scene)` walks up. Neither
  helped here — the inventory was on the other *session*, not elsewhere in the graph — but the
  traversal code is in `PlayerAccess` if needed.
- **`"Type": null` in a composite does NOT mean "no component".** It means the component type is
  resolved from the `Definition` the slot names. This note previously claimed the opposite, and the
  claim was wrong.

  Traced in full: `CompositeCharacterServer.def`'s `"Inventory"` tag slot maps to component GUID
  `a6f8013a-3c5d-4b45-f3f5-ce59dbd5e17e`, whose entry is
  `{"Definition": "fe9df47f-5673-4253-b797-54635d4f3f42", "Type": null}` -
  and `fe9df47f` is `Assets/Characters/System/CharacterInventory.def`, an
  `InventoryDefinitionObjectBuilder` with `MaxMass: 8400` and
  `InventoryName: "CharacterInventoryName"`. `PrefabCharacterServer.def` then fills that same slot
  with an `InventoryObjectBuilder` carrying the starting items.

  So the slot called empty here is the character's **main inventory**. `CharacterComponent` uses the
  identical `Type: null` pattern, which is the tell that should have caught this: a composite whose
  character has no character component is not a plausible reading.

  The real lesson stands, just for a different reason - do not infer component presence from the
  composite alone. Resolve the `Definition`, and confirm against a live entity's component list.

## Diagnostic technique that actually worked

Definition files and decompilation both failed to settle this — the composite declares the tag with
a null type and the real wiring comes from partialdefs. What settled it in one run:

1. Log the entity's **full** component list, chunked (a truncated dump hid the answer twice).
2. Log the whole ancestry and child tree with `hasInventory` at each node.
3. **Log inventory contents**, and have the player plant a distinctive marker item first.

The marker made ownership unambiguous instead of inferred. When an entity-identity question gets
hard, ask the player to make their entity identifiable.
