using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Mathematics;

namespace BuildPlanner;

/// <summary>Outcome of a withdrawal, so the caller can raise the right HUD notification.</summary>
internal enum WithdrawalOutcome
{
    /// <summary>Everything requested was transferred.</summary>
    Complete,

    /// <summary>Some items moved, but not all — storage ran out or the player's inventory filled.</summary>
    Partial,

    /// <summary>Nothing moved.</summary>
    Nothing,

    /// <summary>The player already had everything queued; no transfer was needed.</summary>
    AlreadySatisfied
}

internal readonly struct WithdrawalResult
{
    internal readonly WithdrawalOutcome Outcome;
    internal readonly List<ItemAmount> Transferred;
    internal readonly List<ItemAmount> StillMissing;

    internal WithdrawalResult(WithdrawalOutcome outcome, List<ItemAmount> transferred, List<ItemAmount> stillMissing)
    {
        Outcome = outcome;
        Transferred = transferred;
        StillMissing = stillMissing;
    }
}

/// <summary>
/// Moves exactly the missing components from source inventories into the player's inventory.
///
/// This is the core of the feature: the player asks for what a set of blocks needs, and gets the
/// difference between that and what they already carry — never a whole stack, never a guessed
/// round number.
/// </summary>
internal static class ComponentWithdrawal
{
    /// <summary>
    /// Pull <paramref name="required"/> into <paramref name="destination"/> from the given sources.
    /// </summary>
    /// <param name="destination">Normally the player's character inventory.</param>
    /// <param name="sources">
    /// Candidate source inventories, tried in order. Callers pass conveyor-reachable inventories when
    /// the player is on a grid, or the aimed-at container when they are not.
    /// </param>
    /// <param name="required">Total components wanted, already merged per item type.</param>
    internal static WithdrawalResult Withdraw(
        InventoryComponent destination,
        IReadOnlyList<InventoryComponent> sources,
        IReadOnlyList<ItemAmount> required)
    {
        var transferred = new List<ItemAmount>();

        if (destination == null || required == null || required.Count == 0)
            return new WithdrawalResult(WithdrawalOutcome.AlreadySatisfied, transferred, new List<ItemAmount>());

        // Ask the engine what is actually short, rather than recomputing it here. FindMissingItems
        // subtracts what the destination already holds, which is precisely the "I needed 11, took 10"
        // arithmetic the player would otherwise do in their head.
        Log.Debug($"  debug: withdrawal wants {required.Count} item type(s) from {sources.Count} source(s)");

        var missing = FindMissing(destination, required);
        Log.Debug($"  debug: shortfall is {missing.Count} item type(s)");

        if (missing.Count == 0)
            return new WithdrawalResult(WithdrawalOutcome.AlreadySatisfied, transferred, missing);

        var anyMoved = false;

        foreach (var want in missing)
        {
            if (want.Item == null) continue;

            var outstanding = want.Amount;

            foreach (var source in sources)
            {
                if (outstanding <= 0) break;
                if (source == null || ReferenceEquals(source, destination)) continue;

                FixedPoint moved;
                try
                {
                    // allowPartial: take what this container has and continue to the next one, rather
                    // than refusing the whole transfer because one container is short.
                    moved = source.TransferByDef(destination, want.Item, null, outstanding, true);
                }
                catch (Exception ex)
                {
                    Log.Error($"transfer of {want.Item.DisplayName} failed", ex);
                    continue;
                }

                if (moved <= 0) continue;

                Log.Debug($"  debug: moved {(int)moved} x {want.Item.DisplayName}");
                outstanding -= moved;
                anyMoved = true;
                transferred.Add(new ItemAmount(want.Item, moved));
            }
        }

        // Re-ask the engine rather than trusting our own subtraction: the destination may have hit a
        // mass limit partway through, so what we moved is not necessarily what we asked for.
        var stillMissing = FindMissing(destination, required);

        var outcome = Classify(stillMissing.Count, anyMoved);

        return new WithdrawalResult(outcome, transferred, stillMissing);
    }

    /// <summary>
    /// Decide the outcome from the post-transfer shortfall and whether anything moved.
    ///
    /// Split out from <see cref="Withdraw"/> purely so it can be unit-tested: everything else in this
    /// class needs a live InventoryComponent, which cannot be constructed outside a loaded game.
    /// The shortfall is re-measured by the engine before this is called, so "nothing still missing"
    /// means complete even if this run moved nothing (the player already had it).
    /// </summary>
    internal static WithdrawalOutcome Classify(int stillMissingCount, bool anyMoved)
    {
        if (stillMissingCount == 0) return WithdrawalOutcome.Complete;
        return anyMoved ? WithdrawalOutcome.Partial : WithdrawalOutcome.Nothing;
    }

    private static List<ItemAmount> FindMissing(InventoryComponent inventory, IReadOnlyList<ItemAmount> required)
    {
        var wanted = new ItemAmount[required.Count];
        for (var i = 0; i < required.Count; i++) wanted[i] = required[i];

        var missing = new List<ItemAmount>();

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
