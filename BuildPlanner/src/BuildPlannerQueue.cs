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
    private readonly List<CubeBlockDefinition> _blocks = new List<CubeBlockDefinition>();

    internal int Count => _blocks.Count;

    internal IReadOnlyList<CubeBlockDefinition> Blocks => _blocks;

    internal void Add(CubeBlockDefinition block)
    {
        if (block != null) _blocks.Add(block);
    }

    internal bool Remove(CubeBlockDefinition block) => _blocks.Remove(block);

    internal void Clear() => _blocks.Clear();

    /// <summary>
    /// Total components required by everything queued, merged per item type.
    /// </summary>
    /// <param name="multiplier">
    /// Scales every amount. The SE1 Build Planner's CTRL variants withdraw tenfold; pass 10 for those.
    /// </param>
    /// <remarks>
    /// Only <see cref="CubeBlockRecipeDefinition.CriticalItems"/> is summed. OptionalItems are exactly
    /// that — optional — and pulling them would take more from storage than the player asked for.
    /// </remarks>
    internal List<ItemAmount> GetRequiredComponents(int multiplier = 1)
    {
        var totals = new Dictionary<ItemDefinition, FixedPoint>();

        foreach (var block in _blocks)
        {
            var recipe = block?.Recipe;
            if (recipe?.CriticalItems == null) continue;

            foreach (var required in recipe.CriticalItems)
            {
                if (required.Item == null) continue;

                var amount = required.Amount * multiplier;
                totals[required.Item] = totals.TryGetValue(required.Item, out var running)
                    ? running + amount
                    : amount;
            }
        }

        var result = new List<ItemAmount>(totals.Count);
        foreach (var entry in totals)
        {
            result.Add(new ItemAmount(entry.Key, entry.Value));
            Log.Write($"  debug: requires {(int)entry.Value} x {entry.Key.DisplayName}");
        }

        if (result.Count == 0)
            Log.Write($"  debug: {_blocks.Count} queued block(s) produced NO component requirements");

        return result;
    }
}
