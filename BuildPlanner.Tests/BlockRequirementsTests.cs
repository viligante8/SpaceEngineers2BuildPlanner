using System.Collections.Generic;
using System.Linq;
using BuildPlanner;
using Keen.VRage.Library.Mathematics;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// Working out what a partly-built block still needs.
///
/// Keyed on string rather than ItemDefinition, which cannot be constructed outside a loaded game.
/// The arithmetic is identical, and the arithmetic is where the bug was.
/// </summary>
public class BlockRequirementsTests
{
    private static (string, FixedPoint) Item(string name, int amount) => (name, (FixedPoint)amount);

    private static int AmountOf(
        List<(string Key, FixedPoint Amount)> items, string key) =>
        (int)items.Single(i => i.Key == key).Amount;

    /// <summary>
    /// The bug this class exists for.
    ///
    /// The engine orders a block's items by integrity, so a type used both critically and optionally
    /// appears TWICE. A Gearforge reads:
    ///   16 x Steel Plate, 8 x Motor, 2 x Electronic Parts, 2 x Metal Grid, 12 x Steel Plate
    /// Matching each entry against the first stored entry of its type compared the trailing 12
    /// plates against the 16 already stored, went negative, and dropped them — so a block could only
    /// ever be topped up to its functional threshold, never completed.
    /// </summary>
    [Fact]
    public void DuplicateItemTypes_AreSummed_NotMatchedIndividually()
    {
        var total = new[]
        {
            Item("SteelPlate", 16), Item("Motor", 8), Item("ElectronicParts", 2),
            Item("MetalGrid", 2), Item("SteelPlate", 12)
        };
        var stored = new[]
        {
            Item("SteelPlate", 16), Item("Motor", 8), Item("ElectronicParts", 2), Item("MetalGrid", 2)
        };

        var remaining = BlockRequirements.SubtractAggregated(total, stored);

        Assert.Single(remaining);
        Assert.Equal(12, AmountOf(remaining, "SteelPlate"));
    }

    [Fact]
    public void PartiallyStored_ReturnsOnlyTheShortfall()
    {
        var total = new[] { Item("SteelPlate", 28), Item("Motor", 8) };
        var stored = new[] { Item("SteelPlate", 16), Item("Motor", 5) };

        var remaining = BlockRequirements.SubtractAggregated(total, stored);

        Assert.Equal(12, AmountOf(remaining, "SteelPlate"));
        Assert.Equal(3, AmountOf(remaining, "Motor"));
    }

    [Fact]
    public void NothingStored_ReturnsTheWholeTotal()
    {
        var remaining = BlockRequirements.SubtractAggregated(
            new[] { Item("SteelPlate", 28) }, new (string, FixedPoint)[0]);

        Assert.Equal(28, AmountOf(remaining, "SteelPlate"));
    }

    [Fact]
    public void FullyStored_ReturnsNothing()
    {
        var items = new[] { Item("SteelPlate", 28), Item("Motor", 8) };

        Assert.Empty(BlockRequirements.SubtractAggregated(items, items));
    }

    /// <summary>Over-stocked items are dropped, never reported as a negative requirement.</summary>
    [Fact]
    public void StoredMoreThanNeeded_IsNotNegative()
    {
        var remaining = BlockRequirements.SubtractAggregated(
            new[] { Item("SteelPlate", 10) }, new[] { Item("SteelPlate", 16) });

        Assert.Empty(remaining);
    }

    /// <summary>An item stored but not in the recipe must not invent a requirement.</summary>
    [Fact]
    public void StoredItemNotInTotal_IsIgnored()
    {
        var remaining = BlockRequirements.SubtractAggregated(
            new[] { Item("SteelPlate", 10) }, new[] { Item("Uranium", 5) });

        Assert.Equal(10, AmountOf(remaining, "SteelPlate"));
    }
}
