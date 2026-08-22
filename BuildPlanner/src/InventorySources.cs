using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
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
/// is a door into the whole grid's storage. That widening is what makes one keypress enough.
/// </summary>
internal static class InventorySources
{
    /// <summary>
    /// Collect source inventories reachable from <paramref name="target"/>, nearest-first:
    /// the target's own inventory, then everything sharing its inventory/conveyor system.
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

            // Then the rest of the grid's storage, reached through the block's grid.
            var block = target.TryGet<CubeBlockComponent>();
            var gridEntity = block?.Grid?.Entity;
            if (gridEntity == null)
            {
                Log.Debug("  debug: aimed entity is not a grid block; no conveyor sweep");
                return sources;
            }

            var inventorySystem = gridEntity.TryGet<InventorySystemComponent>();
            if (inventorySystem == null)
            {
                Log.Debug($"  debug: grid '{gridEntity.DebugName}' has no InventorySystemComponent");
                return sources;
            }

            foreach (var inventory in inventorySystem.Inventories)
            {
                if (inventory == null) continue;
                if (sources.Contains(inventory)) continue;
                sources.Add(inventory);
            }
        }
        catch (Exception ex)
        {
            Log.Error("CollectFrom failed", ex);
        }

        Log.Debug($"  debug: collected {sources.Count} source inventories");
        return sources;
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
