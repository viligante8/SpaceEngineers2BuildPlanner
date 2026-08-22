using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Memory;

namespace BuildPlanner;

/// <summary>
/// What a block still needs, as opposed to what a whole block costs.
///
/// A partially welded block does not need a full recipe. Queueing
/// <see cref="CubeBlockDefinition.Items"/> for a block that is already 1/30th built asks for 30 Steel
/// Plates when 29 would finish it, and because the withdrawal is exact the player gets exactly one
/// too many — the same "confidently wrong number" failure as the recipe bug, one layer up.
///
/// The engine already tracks this per block:
/// <c>CubeBlockComponent.GetTotalItems</c> (everything the finished block holds) minus
/// <c>GetStoredItems</c> (what is in it now). Subtracting them is the remainder.
///
/// <c>GetItemsDelta</c> looks like the obvious call and is not: it is documented as calculating
/// "differences of item amounts **when changing build progress ratio**", i.e. for an intended
/// progress change, not for the outstanding remainder.
/// </summary>
internal static class BlockRequirements
{
    /// <summary>
    /// Components still needed to finish <paramref name="block"/>.
    ///
    /// <paramref name="block"/> is null for a projection — nothing is built yet, so the full recipe
    /// from the definition is exactly right there.
    /// </summary>
    internal static List<ItemAmount> Remaining(CubeBlockComponent? block, CubeBlockDefinition definition)
    {
        if (definition == null) return new List<ItemAmount>();

        if (block == null)
        {
            // Projection: no built block exists, so the whole recipe is outstanding.
            Log.Debug($"  debug: '{definition.UIData?.Name}' has no built block (projection); using the full recipe");
            return FromDefinition(definition);
        }

        try
        {
            var capacity = definition.Items.IsDefaultOrEmpty ? 0 : definition.Items.Length;
            if (capacity == 0)
            {
                Log.Write($"  WARNING: '{definition.UIData?.Name}' has no computed Items; cannot size the item buffers");
                return new List<ItemAmount>();
            }

            var total = Read(buffer => block.GetTotalItems(buffer, false), capacity);
            var stored = Read(buffer => block.GetStoredItems(buffer, false), capacity);

            var remaining = new List<ItemAmount>(total.Count);

            foreach (var want in total)
            {
                if (want.Item == null) continue;

                var have = AmountOf(stored, want.Item);
                var outstanding = want.Amount - have;

                // Skip anything already satisfied. A negative can only mean the block holds more of
                // an item than the finished block needs; either way nothing is outstanding.
                if (outstanding <= 0) continue;

                remaining.Add(new ItemAmount(want.Item, outstanding));
            }

            Log.Debug($"  debug: '{definition.UIData?.Name}' at {block.EffectiveBuildProgress:P0}" +
                      $" still needs {remaining.Count} item type(s)");

            return remaining;
        }
        catch (Exception ex)
        {
            // Fall back to the full recipe rather than queueing nothing: too many components is a
            // visible annoyance, none at all looks like the feature is broken.
            Log.Error($"computing the remainder for '{definition.UIData?.Name}' failed; using the full recipe", ex);
            return FromDefinition(definition);
        }
    }

    /// <summary>The block's complete recipe, for projections and for error fallback.</summary>
    private static List<ItemAmount> FromDefinition(CubeBlockDefinition definition)
    {
        var items = new List<ItemAmount>();
        if (definition.Items.IsDefaultOrEmpty) return items;

        foreach (var item in definition.Items)
            if (item.Item != null)
                items.Add(item);

        return items;
    }

    /// <summary>
    /// Run one of the engine's buffer-filling calls and copy the result out.
    ///
    /// The engine writes into a <c>BufferReference</c>, a ref struct over a <c>Buffer&lt;T&gt;</c>
    /// whose constructor is internal — so the buffer is allocated here and its public GetReference()
    /// passed in. Buffer owns native memory and must be disposed.
    /// </summary>
    private static List<ItemAmount> Read(Action<BufferReference<ItemAmount>> fill, int capacity)
    {
        var buffer = new Buffer<ItemAmount>(capacity, Allocator.Heap);

        try
        {
            fill(buffer.GetReference());

            var items = new List<ItemAmount>(buffer.Count);
            for (var i = 0; i < buffer.Count; i++) items.Add(buffer[i]);
            return items;
        }
        finally
        {
            buffer.Dispose();
        }
    }

    private static Keen.VRage.Library.Mathematics.FixedPoint AmountOf(
        List<ItemAmount> items, ItemDefinition item)
    {
        foreach (var candidate in items)
            if (ReferenceEquals(candidate.Item, item))
                return candidate.Amount;

        return 0;
    }
}
