using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Mathematics;

namespace BuildPlanner;

/// <summary>
/// The queue of blocks the player intends to build, and the component totals they imply.
///
/// SE2 ships <c>Keen.Game2.Simulation.GameSystems.BuildPlanners.BuildPlannerData</c> with a
/// <c>PlannedBlocks</c> list and Add/Remove methods, but nothing in the shipping game populates it —
/// there is no keybind and no localized string for it anywhere in the data. (It *is* read: the
/// terminal's build planner panel binds to it, which is what <see cref="EngineQueueMirror"/> and
/// <see cref="TerminalPlannerPanel"/> exploit.) This class keeps its own queue so the feature works
/// without depending on half-wired engine state — it also holds the per-block *outstanding*
/// requirements, which <c>BuildPlannerData</c> has nowhere to put. Migrating onto BuildPlannerData
/// later is a contained change (see notes/build-planner-api.md).
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

    private int _batchDepth;

    /// <summary>
    /// Raised after the queue changes, once per logical change.
    ///
    /// **This exists because forgetting to call the mirror was a real, shipped bug.** Every mutation
    /// site used to be responsible for calling <see cref="EngineQueueMirror.Sync"/> itself, and the
    /// withdrawal path — the one place that clears the queue as a side effect rather than as the
    /// point of the operation — didn't. Nothing looked wrong until the terminal panel started
    /// displaying the engine's list: withdrawing emptied the queue while the panel went on showing
    /// every block, because the two had silently diverged.
    ///
    /// Making the queue announce its own changes moves that from "remember to call this" to
    /// "impossible to miss".
    /// </summary>
    internal Action? Changed;

    internal int Count => _entries.Count;

    /// <summary>
    /// Coalesce a run of mutations into one <see cref="Changed"/>.
    ///
    /// Queueing an area welder's selection adds dozens of blocks; without this each one would
    /// rebuild the engine's list and, through it, the terminal panel.
    /// </summary>
    internal void BeginBatch() => _batchDepth++;

    /// <inheritdoc cref="BeginBatch"/>
    internal void EndBatch()
    {
        if (_batchDepth > 0) _batchDepth--;
        if (_batchDepth == 0) OnChanged();
    }

    private void OnChanged()
    {
        if (_batchDepth > 0) return;
        Changed?.Invoke();
    }

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
        OnChanged();
    }

    internal void Clear()
    {
        _entries.Clear();
        _blocks.Clear();
        OnChanged();
    }

    /// <summary>
    /// Drop one queued block by position.
    ///
    /// Added for the terminal panel's per-block remove button, which identifies a block by its index
    /// in the displayed list. Both lists are kept strictly parallel — <see cref="Add"/> appends to
    /// each — so one index addresses both.
    /// </summary>
    /// <returns>False when the index is out of range, so the caller can report rather than guess.</returns>
    internal bool RemoveAt(int index)
    {
        if (index < 0 || index >= _entries.Count) return false;

        _entries.RemoveAt(index);
        _blocks.RemoveAt(index);
        OnChanged();
        return true;
    }

    /// <summary>The queued block at a position, or null when the index is out of range.</summary>
    internal CubeBlockDefinition? BlockAt(int index)
        => index >= 0 && index < _blocks.Count ? _blocks[index] : null;

    /// <summary>
    /// What one queued block still needs, for the terminal panel's per-block produce button.
    /// </summary>
    internal List<ItemAmount> GetRequiredComponentsAt(int index, int multiplier = 1)
    {
        if (index < 0 || index >= _entries.Count)
        {
            Log.Write($"  queue: no block at index {index}; nothing required");
            return new List<ItemAmount>();
        }

        return Total(new[] { _entries[index] }, multiplier);
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
    internal List<ItemAmount> GetRequiredComponents(int multiplier = 1) => Total(_entries, multiplier);

    /// <summary>
    /// Merge a set of queue entries into per-item totals. The whole queue and a single entry go
    /// through the same code so the panel's per-block button cannot drift from the keybind.
    /// </summary>
    private List<ItemAmount> Total(IEnumerable<Entry> entries, int multiplier)
    {
        var totals = new Dictionary<ItemDefinition, FixedPoint>();
        var counted = 0;

        foreach (var entry in entries)
        {
            counted++;
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

        // Counts the entries actually totalled, not the whole queue: the panel's per-block button
        // passes one entry, and "12 queued blocks need nothing further" would be a lie about it.
        if (result.Count == 0)
            Log.Write($"  {counted} queued block(s) need nothing further");

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
