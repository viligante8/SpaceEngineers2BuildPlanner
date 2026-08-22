using BuildPlanner;
using Keen.VRage.Library.Mathematics;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// The decidable parts of SHIFT-produce: how many recipe runs a shortfall implies, and which
/// outcome the player is told about.
///
/// Everything else in <see cref="ComponentProduction"/> needs a live
/// <c>ItemConverterComponent</c>, which cannot be constructed outside a loaded game — and the part
/// that actually matters (that enqueueing one component recipe makes the engine cascade requests for
/// its sub-components) is a fact about the engine's job system that no test here can observe. That
/// is verified in game and recorded in notes/build-planner-api.md.
/// </summary>
public class ProductionTests
{
    /// <summary>
    /// The rounding direction is the whole point. A block needing 30 plates from a recipe that
    /// yields 4 needs 8 runs, not 7: rounding down leaves the player one component short of the
    /// block they queued, which is the "took 10, needed 11" failure this feature exists to remove,
    /// reintroduced one layer down. Matches the engine's own
    /// <c>(int)FixedPoint.Ceiling(amount / perRun)</c>.
    /// </summary>
    [Theory]
    [InlineData(30, 4, 8)]
    [InlineData(30, 1, 30)]
    [InlineData(1, 4, 1)]
    [InlineData(8, 4, 2)]
    [InlineData(9, 4, 3)]
    public void RunsNeeded_RoundsUp(int wanted, int perRun, int expected)
    {
        Assert.Equal(expected, ComponentProduction.RunsNeeded(wanted, perRun));
    }

    /// <summary>
    /// A recipe yielding nothing must not produce a run count. Without this the caller would enqueue
    /// zero-or-negative runs against a real assembler, or spin trying to satisfy a shortfall that
    /// the recipe can never reduce.
    /// </summary>
    [Theory]
    [InlineData(30, 0)]
    [InlineData(0, 4)]
    [InlineData(-5, 4)]
    [InlineData(30, -1)]
    public void RunsNeeded_RefusesDegenerateInput(int wanted, int perRun)
    {
        Assert.Equal(0, ComponentProduction.RunsNeeded(wanted, perRun));
    }

    /// <summary>
    /// Fractional shortfalls still round up to a whole run. FixedPoint has no fractional factory —
    /// the explicit float cast is the conversion the type actually offers.
    /// </summary>
    [Fact]
    public void RunsNeeded_RoundsFractionalShortfallUp()
    {
        Assert.Equal(1, ComponentProduction.RunsNeeded((FixedPoint)0.5f, 1));
    }

    [Fact]
    public void Classify_AllPlaced_IsComplete()
    {
        Assert.Equal(ProductionOutcome.Complete, ComponentProduction.Classify(3, 0));
    }

    [Fact]
    public void Classify_SomePlaced_IsPartial()
    {
        Assert.Equal(ProductionOutcome.Partial, ComponentProduction.Classify(2, 1));
    }

    [Fact]
    public void Classify_NonePlaced_IsNothing()
    {
        Assert.Equal(ProductionOutcome.Nothing, ComponentProduction.Classify(0, 3));
    }

    /// <summary>
    /// Zero placed and zero unproducible means an empty request, not success. "AlreadySatisfied" is
    /// decided by the caller before any converter is consulted, so it must never be inferred here —
    /// telling the player "producing nothing" as though it worked would hide a real failure.
    /// </summary>
    [Fact]
    public void Classify_NothingAtAll_IsNotReportedAsComplete()
    {
        var outcome = ComponentProduction.Classify(0, 0);

        Assert.NotEqual(ProductionOutcome.Complete, outcome);
        Assert.Equal(ProductionOutcome.Nothing, outcome);
    }

    /// <summary>
    /// Partial must never be reported as Complete. Complete suppresses the "cannot make X" half of
    /// the message, so a player missing a component they have no recipe for would walk away thinking
    /// production was under way for all of it.
    /// </summary>
    [Fact]
    public void Classify_AnyUnproducible_IsNeverComplete()
    {
        for (var placed = 1; placed <= 3; placed++)
            Assert.NotEqual(ProductionOutcome.Complete, ComponentProduction.Classify(placed, 1));
    }
}
