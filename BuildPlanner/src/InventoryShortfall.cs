using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.Items;

namespace BuildPlanner;

/// <summary>
/// What an inventory is short of, asked of the engine rather than worked out here.
///
/// <c>InventoryComponent.FindMissingItems</c> subtracts what the inventory already holds from what
/// was asked for. That subtraction is precisely the "I needed 11, took 10, walked back" arithmetic
/// the Build Planner exists to remove, and the engine's answer is the one that matches the numbers
/// the block panel shows the player.
///
/// Shared by withdrawal and production so the two can never disagree about what "missing" means: a
/// produce that used a different shortfall from the withdraw that follows it would either remake
/// components the player is already carrying or leave them one short.
/// </summary>
internal static class InventoryShortfall
{
    /// <summary>
    /// Per-item shortfall of <paramref name="required"/> against <paramref name="inventory"/>.
    /// Returns an empty list on failure — callers treat that as "nothing missing", which is the safe
    /// direction: it moves and makes nothing.
    /// </summary>
    internal static List<ItemAmount> Find(InventoryComponent inventory, IReadOnlyList<ItemAmount> required)
    {
        var missing = new List<ItemAmount>();
        if (inventory == null || required == null || required.Count == 0) return missing;

        var wanted = new ItemAmount[required.Count];
        for (var i = 0; i < required.Count; i++) wanted[i] = required[i];

        // FindMissingItems writes into a BufferReference, which is a ref struct over a Buffer<T>.
        // BufferReference's constructor is internal, so the buffer must be allocated first and its
        // public GetReference() used. Worst case is every requested type missing in full, so capacity
        // equals the request count. Buffer<T> owns native memory — dispose it.
        var buffer = new Keen.VRage.Library.Memory.Buffer<ItemAmount>(
            required.Count, Keen.VRage.Library.Memory.Allocator.Heap);

        try
        {
            inventory.FindMissingItems(wanted, buffer.GetReference());

            for (var i = 0; i < buffer.Count; i++)
            {
                var entry = buffer[i];
                if (entry.Amount > 0) missing.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Log.Error("FindMissingItems failed", ex);
            return new List<ItemAmount>();
        }
        finally
        {
            buffer.Dispose();
        }

        return missing;
    }
}
