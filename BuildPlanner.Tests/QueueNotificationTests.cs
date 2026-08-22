using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BuildPlanner;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.Items;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// <see cref="BuildPlannerQueue.Changed"/> — the notification that keeps the engine's
/// <c>BuildPlannerData</c>, and therefore the terminal panel, in step with the queue.
///
/// **This tests a bug that actually shipped.** Every mutation site used to call
/// <c>EngineQueueMirror.Sync</c> itself, and the withdrawal path — which clears the queue as a side
/// effect rather than as the point of the operation — did not. The divergence was invisible until
/// the terminal panel started displaying the engine's copy, at which point withdrawing emptied the
/// queue while the panel went on listing every block. Reported from a live session 2026-08-22.
///
/// A missed notification cannot be caught by any test that only checks queue contents, so these
/// assert on the signal itself.
/// </summary>
public class QueueNotificationTests
{
    private static CubeBlockDefinition Block()
        => (CubeBlockDefinition)RuntimeHelpers.GetUninitializedObject(typeof(CubeBlockDefinition));

    private static List<ItemAmount> NoRequirements() => new List<ItemAmount>();

    /// <summary>A queue that counts how many times it announced a change.</summary>
    private static BuildPlannerQueue Counting(out List<int> notifications)
    {
        var queue = new BuildPlannerQueue();
        var seen = new List<int>();
        queue.Changed = () => seen.Add(queue.Count);
        notifications = seen;
        return queue;
    }

    [Fact]
    public void Add_Announces()
    {
        var queue = Counting(out var notifications);

        queue.Add(Block(), NoRequirements());

        Assert.Equal(new[] { 1 }, notifications);
    }

    [Fact]
    public void Clear_Announces()
    {
        var queue = Counting(out var notifications);
        queue.Add(Block(), NoRequirements());
        notifications.Clear();

        queue.Clear();

        // The regression this file exists for: a clear that does not announce leaves the panel
        // showing blocks that are no longer queued.
        Assert.Equal(new[] { 0 }, notifications);
    }

    [Fact]
    public void RemoveAt_Announces()
    {
        var queue = Counting(out var notifications);
        queue.Add(Block(), NoRequirements());
        queue.Add(Block(), NoRequirements());
        notifications.Clear();

        queue.RemoveAt(0);

        Assert.Equal(new[] { 1 }, notifications);
    }

    [Fact]
    public void RemoveAt_OutOfRange_SaysNothing()
    {
        var queue = Counting(out var notifications);
        queue.Add(Block(), NoRequirements());
        notifications.Clear();

        queue.RemoveAt(9);

        // Nothing changed, so nothing should be rebuilt.
        Assert.Empty(notifications);
    }

    [Fact]
    public void Add_OfNull_SaysNothing()
    {
        var queue = Counting(out var notifications);

        queue.Add(null!, NoRequirements());

        Assert.Empty(notifications);
    }

    [Fact]
    public void Batch_CoalescesToASingleAnnouncement()
    {
        var queue = Counting(out var notifications);

        queue.BeginBatch();
        for (var i = 0; i < 5; i++) queue.Add(Block(), NoRequirements());
        queue.EndBatch();

        // One notification carrying the final state, not five. Queueing an area-welder selection
        // otherwise rebuilds the engine list — and the panel — once per block.
        Assert.Equal(new[] { 5 }, notifications);
    }

    [Fact]
    public void NestedBatches_AnnounceOnlyOnTheOutermostEnd()
    {
        var queue = Counting(out var notifications);

        queue.BeginBatch();
        queue.Add(Block(), NoRequirements());
        queue.BeginBatch();
        queue.Add(Block(), NoRequirements());
        queue.EndBatch();

        Assert.Empty(notifications);

        queue.EndBatch();

        Assert.Equal(new[] { 2 }, notifications);
    }

    [Fact]
    public void UnbalancedEndBatch_DoesNotWedgeTheQueueSilent()
    {
        var queue = Counting(out var notifications);

        // A stray EndBatch must not drive the depth negative; if it did, every later mutation would
        // be suppressed for the rest of the session and the panel would freeze.
        queue.EndBatch();
        notifications.Clear();

        queue.Add(Block(), NoRequirements());

        Assert.Equal(new[] { 1 }, notifications);
    }
}
