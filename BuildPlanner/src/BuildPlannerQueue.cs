using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Mathematics;

namespace BuildPlanner;

/// <summary>
/// The queue of blocks the player intends to build, and the component totals they imply.
///
/// SE2 ships <c>Keen.Game2.Simulation.GameSystems.BuildPlanners.BuildPlannerData</c> with a
/// <c>PlannedBlocks</c> list and Add/Remove methods, but nothing populates or reads it — there is no
/// keybind and no localized string for it anywhere in the shipping data. This class keeps its own
/// queue so the feature works without depending on half-wired engine state; migrating onto
/// BuildPlannerData later is a contained change (see notes/build-planner-api.md).
/// </summary>
internal sealed class BuildPlannerQueue
{
    /// <summary>
    /// One queued block: what it is, and what it still needed when it was queued.
    ///
    /// The requirement is a snapshot rather than a live read. The player queues "what this block
    /// needs now"; re-deriving it later would silently change the answer if someone welded the block
    /// in between, and the CubeBlockComponent may not even exist by then.
    /// </summary>
    private readonly record struct Entry(CubeBlockDefinition Definition, List<ItemAmount> Required);

    private readonly List<Entry> _entries = new List<Entry>();
    private readonly List<CubeBlockDefinition> _blocks = new List<CubeBlockDefinition>();

    internal int Count => _entries.Count;

    /// <summary>Queued block definitions, for the engine mirror and the UI.</summary>
    internal IReadOnlyList<CubeBlockDefinition> Blocks => _blocks;

    /// <summary>
    /// Queue a block along with the components it still needs.
    /// </summary>
    /// <param name="required">
    /// Outstanding components, from <see cref="BlockRequirements.Remaining"/> — NOT the definition's
    /// full recipe. A half-built block must only pull the remainder.
    /// </param>
    internal void Add(CubeBlockDefinition block, List<ItemAmount> required)
    {
        if (block == null) return;

        _entries.Add(new Entry(block, required ?? new List<ItemAmount>()));
        _blocks.Add(block);
    }

    internal void Clear()
    {
        _entries.Clear();
        _blocks.Clear();
    }

    /// <summary>
    /// Total components required by everything queued, merged per item type.
    /// </summary>
    /// <param name="multiplier">
    /// Scales every amount. The SE1 Build Planner's CTRL variants withdraw tenfold; pass 10 for those.
    /// </param>
    /// <remarks>
    /// Uses <see cref="CubeBlockDefinition.Items"/> — "Collection of items necessary to build the
    /// block. Computed when definition is post-processed."
    ///
    /// **Do not use <c>Recipe.CriticalItems</c> here.** That was this feature's longest-lived bug.
    /// CubeBlockRecipeDefinition is documented as defining "the *proportions* and criticality of
    /// items... used to generate the final recipe based on mass, efficiency and rounding amounts" —
    /// it is a ratio shared across every block that uses that recipe, not a component count. Reading
    /// it made a 2.5m light armor cube ask for 1 Steel Plate instead of 30, and because the
    /// withdrawal is exact, the player got exactly one plate. The same recipe object is referenced by
    /// the 0.5m variant, which is why the wrong answer still looked like a plausible block.
    ///
    /// <c>Items</c> is the post-processed, per-block result — the same figure the block tooltip
    /// shows the player.
    /// </remarks>
    internal List<ItemAmount> GetRequiredComponents(int multiplier = 1)
    {
        var totals = new Dictionary<ItemDefinition, FixedPoint>();

        foreach (var entry in _entries)
        {
            foreach (var required in entry.Required)
            {
                if (required.Item == null) continue;
                Accumulate(totals, required.Item, required.Amount, multiplier);
            }
        }

        var result = new List<ItemAmount>(totals.Count);
        foreach (var entry in totals)
        {
            result.Add(new ItemAmount(entry.Key, entry.Value));
            // Always logged. This line is what makes a wrong queue visible: "1 x Steel Plate" for a
            // heavy armor block is obviously wrong at a glance, and burying it behind the debug flag
            // is how a queueing bug got reported as a withdrawal bug.
            Log.Write($"  requires {(int)entry.Value} x {entry.Key.DisplayName}");
        }

        if (result.Count == 0)
            Log.Write($"  {_entries.Count} queued block(s) need nothing further");

        return result;
    }

    /// <summary>
    /// Add one requirement into the running totals, scaled by the multiplier.
    /// </summary>
    /// <remarks>
    /// Generic over the key only so it can be unit-tested: <see cref="ItemDefinition"/> cannot be
    /// constructed outside a loaded game, but the arithmetic here is pure and is the part that can
    /// silently regress — the multiplier must apply to *every* occurrence of an item, including the
    /// second and later times the same item type is seen, or a x10 withdrawal of two identical
    /// blocks comes out short.
    ///
    /// This does NOT guard against reading the wrong source field; that was a semantic fact about
    /// the engine (Recipe.CriticalItems is a proportion, CubeBlockDefinition.Items is the computed
    /// recipe) and no test over fabricated data could have caught it.
    /// </remarks>
    internal static void Accumulate<TKey>(
        Dictionary<TKey, FixedPoint> totals, TKey key, FixedPoint amount, int multiplier)
        where TKey : notnull
    {
        var scaled = amount * multiplier;
        totals[key] = totals.TryGetValue(key, out var running) ? running + scaled : scaled;
    }
}
