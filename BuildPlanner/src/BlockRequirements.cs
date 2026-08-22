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

            var remaining = Subtract(total, stored);

            // Kept at debug level. This accounting is what exposed the duplicate-item bug, and the
            // numbers are cheap to produce; the outcome line the player needs is "requires N x item",
            // logged separately by the queue.
            Log.Debug($"  remainder for '{definition.UIData?.Name}':" +
                      $" effectiveProgress={block.EffectiveBuildProgress:F3}" +
                      $" buildProgress={block.BuildProgress:F3}" +
                      $" totalItemAmount={(int)definition.TotalItemAmount}" +
                      $" optionalItemAmount={(int)definition.OptionalItemAmount}" +
                      $" minFunctional={definition.MinFunctionalBuildProgress:F3}" +
                      $" definitionItemTypes={(definition.Items.IsDefaultOrEmpty ? 0 : definition.Items.Length)}");

            LogItems("    total ", total);
            LogItems("    stored", stored);
            LogItems("    needs ", remaining);

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

    /// <summary>
    /// Outstanding = total minus stored, aggregated per item type.
    /// </summary>
    /// <remarks>
    /// **The same item type can appear more than once in these lists, and that is the whole reason
    /// this method exists.** The engine's list is ordered by integrity ("Index 0 = Lowest
    /// Integrity"), so a block whose optional portion uses a material it already used critically
    /// lists it twice. A Gearforge reads:
    ///
    ///     16 x Steel Plate, 8 x Motor, 2 x Electronic Parts, 2 x Metal Grid, 12 x Steel Plate
    ///
    /// The earlier implementation matched each total against the *first* stored entry of that type,
    /// so the trailing "12 x Steel Plate" was compared against the 16 already stored, went negative,
    /// and was discarded. The visible symptom was that a block could only ever be topped up to its
    /// functional threshold - the optional remainder was silently cancelled out every time.
    ///
    /// Summing per type on both sides before subtracting is correct regardless of ordering or
    /// duplication.
    /// </remarks>
    internal static List<ItemAmount> Subtract(List<ItemAmount> total, List<ItemAmount> stored)
    {
        var remaining = SubtractAggregated(
            Pairs(total), Pairs(stored));

        var result = new List<ItemAmount>(remaining.Count);
        foreach (var (item, amount) in remaining) result.Add(new ItemAmount(item, amount));
        return result;
    }

    private static IEnumerable<(ItemDefinition, Keen.VRage.Library.Mathematics.FixedPoint)> Pairs(
        List<ItemAmount> items)
    {
        foreach (var item in items)
            if (item.Item != null)
                yield return (item.Item, item.Amount);
    }

    /// <summary>
    /// The aggregation itself, generic purely so it can be unit-tested — ItemDefinition cannot be
    /// constructed outside a loaded game, but this arithmetic is where the bug was.
    /// </summary>
    internal static List<(TKey Key, Keen.VRage.Library.Mathematics.FixedPoint Amount)> SubtractAggregated<TKey>(
        IEnumerable<(TKey Key, Keen.VRage.Library.Mathematics.FixedPoint Amount)> total,
        IEnumerable<(TKey Key, Keen.VRage.Library.Mathematics.FixedPoint Amount)> stored)
        where TKey : notnull
    {
        var wanted = new Dictionary<TKey, Keen.VRage.Library.Mathematics.FixedPoint>();

        // Sum, never overwrite: the same key legitimately appears several times.
        foreach (var (key, amount) in total)
            wanted[key] = wanted.TryGetValue(key, out var w) ? w + amount : amount;

        foreach (var (key, amount) in stored)
            if (wanted.TryGetValue(key, out var w))
                wanted[key] = w - amount;

        var remaining = new List<(TKey, Keen.VRage.Library.Mathematics.FixedPoint)>(wanted.Count);
        foreach (var entry in wanted)
            if (entry.Value > 0)
                remaining.Add((entry.Key, entry.Value));

        return remaining;
    }

    /// <summary>Log an item list compactly, at debug level.</summary>
    private static void LogItems(string label, List<ItemAmount> items)
    {
        if (items.Count == 0)
        {
            Log.Debug($"  {label}: (none)");
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var item in items)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append((int)item.Amount).Append(" x ").Append(item.Item?.DisplayName.ToString() ?? "?");
        }

        Log.Debug($"  {label}: {sb}");
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

}
