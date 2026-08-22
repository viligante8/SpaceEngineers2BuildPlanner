using System.Collections.Generic;
using BuildPlanner;
using Keen.VRage.Library.Mathematics;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// Merging of component requirements across queued blocks.
///
/// Keyed on string here rather than ItemDefinition, which cannot be constructed outside a loaded
/// game — the arithmetic is identical, and it is the arithmetic that can regress.
///
/// **What these tests cannot do:** catch the wrong *source* being read. The longest-lived bug in this
/// feature was summing `CubeBlockRecipeDefinition.CriticalItems` (a proportion shared between block
/// sizes) instead of `CubeBlockDefinition.Items` (the computed per-block recipe), which made a 2.5m
/// armour cube ask for 1 Steel Plate instead of 30. That was a fact about the engine, settled by the
/// XML docs; a test over fabricated data would only have encoded the same wrong assumption.
/// </summary>
public class ComponentTotalsTests
{
    private static FixedPoint Amount(int value) => value;

    [Fact]
    public void SingleRequirement_IsCarriedThrough()
    {
        var totals = new Dictionary<string, FixedPoint>();

        BuildPlannerQueue.Accumulate(totals, "SteelPlate", Amount(30), multiplier: 1);

        Assert.Equal(30, (int)totals["SteelPlate"]);
    }

    [Fact]
    public void RepeatedItem_Sums()
    {
        var totals = new Dictionary<string, FixedPoint>();

        BuildPlannerQueue.Accumulate(totals, "SteelPlate", Amount(30), multiplier: 1);
        BuildPlannerQueue.Accumulate(totals, "SteelPlate", Amount(12), multiplier: 1);

        Assert.Equal(42, (int)totals["SteelPlate"]);
    }

    [Fact]
    public void DistinctItems_AreKeptApart()
    {
        var totals = new Dictionary<string, FixedPoint>();

        BuildPlannerQueue.Accumulate(totals, "SteelPlate", Amount(30), multiplier: 1);
        BuildPlannerQueue.Accumulate(totals, "MetalGrid", Amount(4), multiplier: 1);

        Assert.Equal(2, totals.Count);
        Assert.Equal(30, (int)totals["SteelPlate"]);
        Assert.Equal(4, (int)totals["MetalGrid"]);
    }

    [Fact]
    public void Multiplier_ScalesTheAmount()
    {
        var totals = new Dictionary<string, FixedPoint>();

        BuildPlannerQueue.Accumulate(totals, "SteelPlate", Amount(30), multiplier: 10);

        Assert.Equal(300, (int)totals["SteelPlate"]);
    }

    /// <summary>
    /// The x10 case that would actually be noticed: two of the same block queued. The multiplier has
    /// to apply to the second occurrence too, not just the one that created the entry — otherwise a
    /// x10 withdrawal of two identical blocks arrives short and the player only finds out mid-build.
    /// </summary>
    [Fact]
    public void Multiplier_AppliesToEveryOccurrence_NotJustTheFirst()
    {
        var totals = new Dictionary<string, FixedPoint>();

        BuildPlannerQueue.Accumulate(totals, "SteelPlate", Amount(30), multiplier: 10);
        BuildPlannerQueue.Accumulate(totals, "SteelPlate", Amount(30), multiplier: 10);

        Assert.Equal(600, (int)totals["SteelPlate"]);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(10, 300)]
    public void MultiplierIsLinear(int multiplier, int expected)
    {
        var totals = new Dictionary<string, FixedPoint>();

        BuildPlannerQueue.Accumulate(totals, "SteelPlate", Amount(30), multiplier);

        Assert.Equal(expected, (int)totals["SteelPlate"]);
    }
}
