using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BuildPlanner;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.Items;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// Positional access into the queue — <c>RemoveAt</c>, <c>BlockAt</c> and the index bounds.
///
/// **Why this is worth a test.** <see cref="BuildPlannerQueue"/> keeps two lists that must stay
/// index-parallel: <c>_entries</c> (block + its outstanding requirements) and <c>_blocks</c> (the
/// definitions handed to the engine mirror and the terminal panel). The panel's per-block Produce
/// and Remove buttons address a block purely by its position in the displayed list, so a
/// <c>RemoveAt</c> that updated only one list would not throw — it would quietly produce or delete
/// the *wrong block*, and only in the UI path, which no keybind test would reach.
///
/// Definitions are allocated uninitialised. <see cref="CubeBlockDefinition"/> cannot be constructed
/// outside a loaded game, but these tests only need distinct object identities to track positions;
/// no member on them is ever read.
///
/// **What these cannot cover:** that a removed block's *requirements* go with it. That needs real
/// <see cref="ItemDefinition"/>s to tell two requirement lists apart, and those cannot be
/// constructed either — <see cref="ComponentTotalsTests"/> covers the arithmetic itself. The
/// invariant tested here is the one that keeps the two lists aligned in the first place.
/// </summary>
public class QueueRemovalTests
{
    private static CubeBlockDefinition Block()
        => (CubeBlockDefinition)RuntimeHelpers.GetUninitializedObject(typeof(CubeBlockDefinition));

    private static List<ItemAmount> NoRequirements() => new List<ItemAmount>();

    [Fact]
    public void RemoveAt_DropsTheBlockAtThatPosition()
    {
        var queue = new BuildPlannerQueue();
        var first = Block();
        var second = Block();
        var third = Block();

        queue.Add(first, NoRequirements());
        queue.Add(second, NoRequirements());
        queue.Add(third, NoRequirements());

        Assert.True(queue.RemoveAt(1));

        Assert.Equal(2, queue.Count);
        Assert.Same(first, queue.BlockAt(0));
        Assert.Same(third, queue.BlockAt(1));
    }

    [Fact]
    public void RemoveAt_KeepsBlocksListParallelWithEntries()
    {
        var queue = new BuildPlannerQueue();
        var first = Block();
        var second = Block();

        queue.Add(first, NoRequirements());
        queue.Add(second, NoRequirements());

        queue.RemoveAt(0);

        // Count comes from _entries and Blocks from _blocks. If RemoveAt updated only one of them
        // these two would disagree — which is the failure the panel would show as "removed one
        // block, the wrong one vanished".
        Assert.Equal(queue.Count, queue.Blocks.Count);
        Assert.Same(second, queue.Blocks[0]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    public void RemoveAt_OutOfRange_ReportsFailureAndChangesNothing(int index)
    {
        var queue = new BuildPlannerQueue();

        Assert.False(queue.RemoveAt(index));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void RemoveAt_PastTheEnd_LeavesTheQueueIntact()
    {
        var queue = new BuildPlannerQueue();
        var only = Block();
        queue.Add(only, NoRequirements());

        Assert.False(queue.RemoveAt(1));

        Assert.Equal(1, queue.Count);
        Assert.Same(only, queue.BlockAt(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void BlockAt_OutOfRange_IsNullRatherThanThrowing(int index)
    {
        var queue = new BuildPlannerQueue();
        queue.Add(Block(), NoRequirements());

        // The panel resolves an index from a UI list that can lag the queue by a frame, so an
        // out-of-range read must be reportable, not fatal.
        Assert.Null(queue.BlockAt(index));
    }

    [Fact]
    public void GetRequiredComponentsAt_OutOfRange_IsEmptyRatherThanThrowing()
    {
        var queue = new BuildPlannerQueue();
        queue.Add(Block(), NoRequirements());

        Assert.Empty(queue.GetRequiredComponentsAt(7));
    }

    [Fact]
    public void ClearedQueue_HasNoBlocksAtAnyPosition()
    {
        var queue = new BuildPlannerQueue();
        queue.Add(Block(), NoRequirements());
        queue.Add(Block(), NoRequirements());

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.Empty(queue.Blocks);
        Assert.Null(queue.BlockAt(0));
    }
}
