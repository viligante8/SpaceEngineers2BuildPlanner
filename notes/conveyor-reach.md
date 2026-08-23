# Reach ignored conveyors entirely

Found while testing produce; fixed and confirmed in game 2026-08-22. It had been present in
the withdrawal from the beginning.

**Symptom, from a test run:** an empty container welded onto a ship but *plumbed to nothing* still
withdrew from the whole ship and still queued work at its assemblers. A standalone container on its
own grid correctly reported nothing available.

**Root cause.** Both sweeps used `InventorySystemComponent.Inventories`, which is not the conveyor
network despite the name. It is filled by `OnBlocksChanged` -> `AddInventories(block)` for every
block on the grid, so conveyor plumbing never enters into it. The separate-grid case worked only
because a different grid has a different `InventorySystemComponent` — the right answer for the wrong
reason, which is why it looked correct.

The engine never uses that set to move anything. `PullAsync`, `PushAllAsync` and
`TransferByDefAsync` all iterate the conveyor graph instead:

```csharp
// InventorySystemComponent.PullAsync
foreach (var inv in ConveyorSystemComponent.IterateReachableInventories(
             invTo, itemDef, followEdgeDirection: false, mustContainTheItem: true))
```

**Fix.** `InventorySources.Reachable` now walks
`ConveyorSystemComponent.IterateReachableInventories(start, null, followEdgeDirection: false)` —
`followEdgeDirection: false` being documented as "search inventories that **can reach** start", the
correct direction for a withdrawal and one that honours one-way topology. `filterItem: null` ignores
per-item filters, so the walk runs once per action rather than once per item.

**Both** sweeps were fixed, not just the reported one: withdrawal and the assembler search shared the
mistake, so an unattached container could also dispatch production. The converter search now also
matches how the engine scopes its own delegation (`conveyorGroup.Blocks`).

Transfers still use `TransferByDef`, which is unchanged and already verified — only the choice of
which inventories are eligible moved.
