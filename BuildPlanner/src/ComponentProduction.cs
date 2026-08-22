using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks.Production.ItemConverters;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Mathematics;

namespace BuildPlanner;

/// <summary>Outcome of a produce request, so the caller can raise the right HUD notification.</summary>
internal enum ProductionOutcome
{
    /// <summary>Every missing component was enqueued somewhere.</summary>
    Complete,

    /// <summary>Some components were enqueued; others could not be.</summary>
    Partial,

    /// <summary>Nothing was enqueued.</summary>
    Nothing,

    /// <summary>The player already had everything queued; nothing needed making.</summary>
    AlreadySatisfied,

    /// <summary>Nothing within reach can convert items at all.</summary>
    NoConverter
}

/// <summary>One component successfully enqueued, and where.</summary>
internal readonly struct ProductionOrder
{
    internal readonly ItemDefinition Item;

    /// <summary>How many of <see cref="Item"/> the enqueued runs will yield — not the run count.</summary>
    internal readonly FixedPoint Amount;

    /// <summary>Display name of the converter the recipe was enqueued at.</summary>
    internal readonly string Converter;

    internal ProductionOrder(ItemDefinition item, FixedPoint amount, string converter)
    {
        Item = item;
        Amount = amount;
        Converter = converter;
    }
}

internal readonly struct ProductionResult
{
    internal readonly ProductionOutcome Outcome;
    internal readonly List<ProductionOrder> Enqueued;

    /// <summary>Components nothing in reach could make, or that no converter had queue space for.</summary>
    internal readonly List<ItemAmount> Unproducible;

    internal ProductionResult(
        ProductionOutcome outcome, List<ProductionOrder> enqueued, List<ItemAmount> unproducible)
    {
        Outcome = outcome;
        Enqueued = enqueued;
        Unproducible = unproducible;
    }
}

/// <summary>
/// Queues the components the player is short of at a reachable assembler — SE1's SHIFT-produce.
///
/// **This is deliberately thin, and that is the important design fact.** The obvious implementation
/// walks the recipe tree itself: to make a Steel Plate you need an Iron Ingot, to make that you need
/// Iron Ore, so enqueue all three in dependency order across the right blocks. None of that is
/// necessary, because <see cref="ItemConverterComponent"/> already does it.
///
/// Verified in <c>Game2.Simulation.dll</c>: <c>TryEnqueueRecipe</c> takes a write pointer on
/// <c>ConversionQueueData</c>, which fires the job
/// <c>OnRecipeCompletedOrEnqueued</c> (<c>[OnChanged(typeof(ConversionQueueData))]</c>), which calls
/// <c>MarkChildRequestsDirty()</c>. That schedules <c>UpdateRequestsWhileEnabled</c>, which:
///
/// 1. <c>AccumulateIngredientsForFullQueue</c> — totals the inputs the whole queue needs
/// 2. <c>RemoveItemsAlreadyInInventory</c> — subtracts what the block already holds
/// 3. <c>UpdatePersistentRequests</c> — raises conveyor pull requests, so it feeds itself
/// 4. <c>UpdateChildRequests</c> — for anything still missing, finds converters on the same
///    **conveyor group** that list the item in a recipe's <c>Outputs</c>, and enqueues the child
///    recipe on them with itself as the <c>requester</c>
///
/// Step 4 recurses: the child's own queue change marks *it* dirty, so a request for a component
/// cascades down through ingots to ore without this mod knowing any of those recipes exist. It also
/// spreads one item's demand across every capable converter
/// (<c>UpdateRequestsInAvailableConverters</c> divides by the candidate count), and withdraws the
/// request again if the parent's queue changes (<c>ClearChildRequestsOfPreviousChildren</c>).
///
/// So the correct thing for this mod to do is enqueue the *top-level* component recipe and stop.
/// Reimplementing the cascade would duplicate engine behaviour that already handles load-spreading
/// and cancellation, and would drift from it on the next patch.
/// </summary>
internal static class ComponentProduction
{
    /// <summary>
    /// Enqueue production for everything in <paramref name="required"/> that
    /// <paramref name="destination"/> is short of.
    /// </summary>
    /// <param name="destination">The player's inventory — what they already carry is not remade.</param>
    /// <param name="converters">
    /// Candidate converters, in preference order. The caller puts the block the player is aiming at
    /// first, so an explicitly-targeted assembler wins over an arbitrary one on the same grid.
    /// </param>
    /// <param name="required">Total components wanted, already merged per item type and scaled.</param>
    internal static ProductionResult Produce(
        InventoryComponent destination,
        IReadOnlyList<ItemConverterComponent> converters,
        IReadOnlyList<ItemAmount> required)
    {
        var enqueued = new List<ProductionOrder>();
        var unproducible = new List<ItemAmount>();

        if (destination == null || required == null || required.Count == 0)
            return new ProductionResult(ProductionOutcome.AlreadySatisfied, enqueued, unproducible);

        if (converters == null || converters.Count == 0)
            return new ProductionResult(ProductionOutcome.NoConverter, enqueued, unproducible);

        // Same shortfall the withdrawal uses, and for the same reason: producing what the player is
        // already carrying wastes ore and assembler time, and the engine's own subtraction is the
        // one that matches what the block panel shows.
        var missing = InventoryShortfall.Find(destination, required);

        // Always logged, not debug-gated: which converters were in play is the first question asked
        // of every failed produce, and a run that did not record it has to be repeated.
        var names = new List<string>(converters.Count);
        foreach (var converter in converters) names.Add(Describe(converter!));
        Log.Write($"  production shortfall is {missing.Count} item type(s);" +
                  $" converters in reach: {(names.Count == 0 ? "none" : string.Join(", ", names))}");

        if (missing.Count == 0)
            return new ProductionResult(ProductionOutcome.AlreadySatisfied, enqueued, unproducible);

        foreach (var want in missing)
        {
            if (want.Item == null) continue;

            var placed = false;

            foreach (var converter in converters)
            {
                if (converter == null) continue;

                ItemRecipeDefinition? recipe;
                FixedPoint perRun;
                try
                {
                    recipe = FindRecipeFor(converter, want.Item, out perRun);
                }
                catch (Exception ex)
                {
                    Log.Error($"reading recipes off '{Describe(converter)}' failed", ex);
                    continue;
                }

                if (recipe == null)
                {
                    // Logged per converter, not just once at the end. "Nothing can make X" does not
                    // say whether five assemblers were asked and all declined or none was found at
                    // all, and those have different fixes.
                    Log.Debug($"  debug: '{Describe(converter)}' has no recipe for {want.Item.DisplayName}");
                    continue;
                }

                var runs = RunsNeeded(want.Amount, perRun);
                if (runs <= 0)
                {
                    // Defensive: a recipe that yields the item but zero of it would otherwise loop.
                    Log.Write($"  WARNING: '{Describe(converter)}' has a recipe for" +
                              $" {want.Item.DisplayName} that yields nothing; skipping");
                    continue;
                }

                bool accepted;
                try
                {
                    accepted = converter.TryEnqueueRecipe(recipe, runs);
                }
                catch (Exception ex)
                {
                    Log.Error($"enqueueing {want.Item.DisplayName} at '{Describe(converter)}' failed", ex);
                    continue;
                }

                if (!accepted)
                {
                    // The only documented refusal is a full queue: TryEnqueueRecipe returns false
                    // when the queue is already at Definition.MaxQueueSize and the recipe cannot be
                    // merged into the last entry. Try the next converter rather than giving up.
                    Log.Write($"  '{Describe(converter)}' would not accept {want.Item.DisplayName}" +
                              $" (queue full at {converter.Definition?.MaxQueueSize ?? 0}?)");
                    continue;
                }

                var yield = perRun * runs;
                enqueued.Add(new ProductionOrder(want.Item, yield, Describe(converter)));
                Log.Write($"  producing {(int)yield} x {want.Item.DisplayName}" +
                          $" ({runs} run(s)) at '{Describe(converter)}'");
                placed = true;
                break;
            }

            if (!placed)
            {
                // Reported, never silent. "Nothing happened" and "nothing here can make this" look
                // identical to the player otherwise, and only one of them is worth walking away from.
                Log.Write($"  nothing in reach can produce {want.Item.DisplayName}");
                unproducible.Add(want);
            }
        }

        return new ProductionResult(Classify(enqueued.Count, unproducible.Count), enqueued, unproducible);
    }

    /// <summary>
    /// Decide the outcome from what was placed and what was not.
    /// </summary>
    /// <remarks>
    /// Split out so it can be unit-tested: everything else here needs a live converter, which cannot
    /// be constructed outside a loaded game. Note the asymmetry with the withdrawal's classifier —
    /// "nothing left to do" is <see cref="ProductionOutcome.AlreadySatisfied"/> and is decided by the
    /// caller before any of this runs, so zero-and-zero here means an empty request, not success.
    /// </remarks>
    internal static ProductionOutcome Classify(int enqueuedCount, int unproducibleCount)
    {
        if (enqueuedCount == 0) return ProductionOutcome.Nothing;
        return unproducibleCount == 0 ? ProductionOutcome.Complete : ProductionOutcome.Partial;
    }

    /// <summary>
    /// How many runs of a recipe yielding <paramref name="perRun"/> are needed to cover
    /// <paramref name="wanted"/>.
    /// </summary>
    /// <remarks>
    /// Rounds up, matching the engine's own child-request arithmetic in
    /// <c>UpdateRequestsInAvailableConverters</c>: <c>(int)FixedPoint.Ceiling(amount / perRun)</c>.
    /// Rounding down would leave the player one component short of the block they queued — the exact
    /// "took 10, needed 11" failure this whole feature exists to remove, reintroduced one layer down.
    /// </remarks>
    internal static int RunsNeeded(FixedPoint wanted, FixedPoint perRun)
    {
        if (wanted <= 0 || perRun <= 0) return 0;
        return (int)FixedPoint.Ceiling(wanted / perRun);
    }

    /// <summary>
    /// A recipe on <paramref name="converter"/> that yields <paramref name="item"/>, and how much of
    /// it one run produces.
    /// </summary>
    /// <remarks>
    /// This walk mirrors the engine's own private <c>ItemConverterComponent.CanProduceItem</c>, which
    /// cannot be called from here. It uses the same public collections the terminal's
    /// <c>StreamedProductionInfoSessionComponent.TryEnqueueAsync</c> walks to validate a recipe
    /// before enqueueing it, so a recipe found here is one the terminal would also accept:
    /// <c>Definition.RecipeDefinitions</c> (category → recipe lists) → <c>Recipes</c> → <c>Outputs</c>.
    ///
    /// The <c>Amount &gt; 0</c> test is the engine's, not an embellishment — a recipe may list an
    /// output it does not actually yield.
    /// </remarks>
    private static ItemRecipeDefinition? FindRecipeFor(
        ItemConverterComponent converter, ItemDefinition item, out FixedPoint perRun)
    {
        perRun = FixedPoint.Zero;

        var definition = converter.Definition;
        if (definition == null)
        {
            Log.Debug($"  debug: '{Describe(converter)}' has no ItemConverterDefinition");
            return null;
        }

        // RecipeDefinitions is a ListDictionaryReader and its values are ListReaders — both value
        // types, so neither can be null-checked. Verified by the compiler, not assumed: the
        // decompiled iteration reads as KeyValuePair<category, ListReader<ItemRecipesDefinition>>,
        // which looks reference-like and is not.
        foreach (var category in definition.RecipeDefinitions)
        {
            foreach (var recipes in category.Value)
            {
                if (recipes == null || recipes.Recipes.IsDefaultOrEmpty) continue;

                foreach (var recipe in recipes.Recipes)
                {
                    if (recipe == null || recipe.Outputs.IsDefaultOrEmpty) continue;

                    foreach (var output in recipe.Outputs)
                    {
                        if (output.Item != item || output.Amount <= 0) continue;

                        perRun = output.Amount;
                        return recipe;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>A converter's name for the log and the HUD.</summary>
    internal static string Describe(ItemConverterComponent converter)
    {
        try
        {
            return converter?.Entity?.DebugName ?? "assembler";
        }
        catch (Exception ex)
        {
            Log.Error("naming a converter failed", ex);
            return "assembler";
        }
    }
}
