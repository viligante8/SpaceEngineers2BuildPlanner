using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks.Production.ItemConverters;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Conveyors;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Inventories;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Utils;

namespace BuildPlanner;

/// <summary>
/// Finds the inventories a withdrawal may pull from.
///
/// The SE1 Build Planner withdraws by aiming at a cargo port, and that is deliberately what this
/// reproduces: the target is whatever the player is looking at, not everything within some radius.
/// Aiming keeps the result predictable â€” components come from the container the player chose.
///
/// From that container the search widens along the conveyor network, because a cargo port on a base
/// is a door into the storage behind it. That widening is what makes one keypress enough.
/// </summary>
internal static class InventorySources
{
    /// <summary>
    /// Collect source inventories reachable from <paramref name="target"/>, nearest-first:
    /// the target's own inventory, then everything that can reach it over the conveyor network.
    /// </summary>
    internal static List<InventoryComponent> CollectFrom(Entity? target)
    {
        var sources = new List<InventoryComponent>();
        if (target == null) return sources;

        try
        {
            // The aimed-at block itself. Blocks bind their inventory to a tag slot (the template
            // definitions use "Inventory", with In/Out variants on some blocks), and an untagged
            // TryGet cannot disambiguate when several exist — the same trap that made the character
            // inventory lookup fail. Try the common tags, then fall back to untagged.
            Log.Debug($"  debug: aimed entity '{target.DebugName}' components={target.Components.Length}");

            var direct = FindInventory(target);
            if (direct != null)
            {
                sources.Add(direct);
                PlayerAccess.LogInventoryContents(target.DebugName, direct);
            }
            else Log.Debug("  debug: aimed entity has no InventoryComponent");

            // Then everything that can reach it across the conveyor network.
            if (direct == null)
            {
                // Without an inventory on the aimed block there is no node to start the walk from,
                // so there is nothing to widen to. Reported rather than returning an empty list
                // silently: "no target" and "target has no inventory" have different fixes.
                Log.Write("  Build Planner: that block has no inventory to pull through");
                return sources;
            }

            foreach (var inventory in Reachable(direct))
            {
                if (inventory == null || sources.Contains(inventory)) continue;
                sources.Add(inventory);
            }
        }
        catch (Exception ex)
        {
            Log.Error("CollectFrom failed", ex);
        }

        Log.Write($"  {sources.Count} inventory/inventories conveyor-reachable from"
                  + $" '{target.DebugName}'");
        return sources;
    }

    /// <summary>
    /// Every inventory that can feed <paramref name="start"/> across the conveyor network.
    /// </summary>
    /// <remarks>
    /// **This replaced a grid-wide sweep, and the difference is the whole point.**
    /// <c>InventorySystemComponent.Inventories</c> looks like the conveyor network and is not: it is
    /// filled by <c>OnBlocksChanged</c> → <c>AddInventories(block)</c> for every block on the grid,
    /// so conveyors never enter into it. Using it meant an unattached empty container welded anywhere
    /// on a ship acted as a door into the whole ship's storage — reported from a test 2026-08-22.
    ///
    /// The engine never uses that set to move anything. <c>PullAsync</c>, <c>PushAllAsync</c> and
    /// <c>TransferByDefAsync</c> all iterate this enumerator instead.
    ///
    /// <c>followEdgeDirection: false</c> is the engine's own choice in <c>PullAsync</c> and is
    /// documented as "search inventories that **can reach** start" — the correct direction for a
    /// withdrawal, and it honours one-way conveyor topology rather than assuming symmetry.
    ///
    /// <c>filterItem: null</c> means "filters are ignored", giving topological reachability once for
    /// the whole withdrawal rather than a separate walk per item. The engine passes a real item
    /// there because it also wants <c>mustContainTheItem</c>; this mod resolves per-item availability
    /// afterwards, through the transfer itself.
    ///
    /// The start inventory is excluded by the enumerator (it is passed as <c>ignoreInventory</c>), so
    /// callers add it themselves — which is what puts the aimed container first in the list.
    /// </remarks>
    private static IEnumerable<InventoryComponent> Reachable(InventoryComponent start)
    {
        var reachable = new List<InventoryComponent>();

        try
        {
            if (start.ConveyorSystem == null)
            {
                // Normal for a lone container: it simply has no network, so only its own contents
                // are available. Not an error, but worth saying — it is the likeliest explanation
                // for "why did that pull nothing".
                Log.Write("  that block is not on a conveyor network; using only its own inventory");
                return reachable;
            }

            foreach (var inventory in ConveyorSystemComponent.IterateReachableInventories(
                         start, null, followEdgeDirection: false))
            {
                if (inventory != null) reachable.Add(inventory);
            }
        }
        catch (Exception ex)
        {
            Log.Error("walking the conveyor network failed", ex);
        }

        return reachable;
    }

    /// <summary>
    /// Item converters reachable from <paramref name="target"/>, most-preferred first.
    /// </summary>
    /// <remarks>
    /// The aimed-at block comes first when it is itself a converter, so aiming straight at an
    /// assembler sends the work there. Everything else follows in conveyor order.
    ///
    /// Scoped to the conveyor network via <see cref="Reachable"/>, which also matches how the engine
    /// scopes its own converter delegation — <c>EnsureConnectedConverterCacheIsUpdated</c> iterates
    /// <c>conveyorGroup.Blocks</c>. An earlier version swept the grid instead, which let an
    /// unattached container queue work at assemblers it had no connection to.
    ///
    /// Deduplicated by entity: an assembler's input and output inventories would otherwise offer the
    /// same converter twice.
    /// </remarks>
    internal static List<ItemConverterComponent> CollectConvertersFrom(Entity? target)
    {
        var converters = new List<ItemConverterComponent>();
        if (target == null) return converters;

        try
        {
            var seen = new HashSet<Entity>();

            // The aimed block first, so an explicitly targeted assembler outranks the network walk.
            var direct = target.TryGet<ItemConverterComponent>();
            if (direct != null)
            {
                converters.Add(direct);
                seen.Add(target);
                Log.Debug($"  debug: aimed entity '{target.DebugName}' is itself an item converter");
            }

            // Then every converter whose output can reach the aimed block across the conveyor
            // network — the same walk the withdrawal uses, so the two halves of the feature agree
            // about reach. Previously this swept the grid, which let an unattached container
            // dispatch work to assemblers it was not plumbed to.
            var start = FindInventory(target);
            if (start == null)
            {
                Log.Write("  Build Planner: that block has no inventory, so no assembler is reachable through it");
                return converters;
            }

            foreach (var inventory in Reachable(start))
            {
                var owner = inventory?.Entity;
                if (owner == null || !seen.Add(owner)) continue;

                var converter = owner.TryGet<ItemConverterComponent>();
                if (converter == null) continue;

                converters.Add(converter);
            }
        }
        catch (Exception ex)
        {
            Log.Error("CollectConvertersFrom failed", ex);
        }

        Log.Write($"  {converters.Count} item converter(s) conveyor-reachable from '{target.DebugName}'");
        return converters;
    }

    /// <summary>
    /// An entity's inventory, tolerating the tag slot it happens to use.
    /// </summary>
    private static InventoryComponent? FindInventory(Entity entity)
    {
        foreach (var tag in InventoryTags)
        {
            var inventory = entity.TryGet<InventoryComponent>(StringId.Get(tag));
            if (inventory != null) return inventory;
        }

        return entity.TryGet<InventoryComponent>();
    }

    private static readonly string[] InventoryTags = { "Inventory", "InventoryOut", "InventoryIn" };
}
