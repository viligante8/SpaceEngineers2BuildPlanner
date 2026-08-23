using System.Collections.Generic;
using BuildPlanner;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// The deposit remainder message.
///
/// This exists because of a reported failure: a deposit left both Cobalt and Silicon behind and the
/// HUD showed only one of them. The log proved both notifications had been raised, so the loss was
/// the HUD's stack cap (MaterialNotificationConfiguration.MaxStackCount is 2, and it is the only
/// notification configuration the game ships). The remainder is now one notification, which no
/// stack cap can truncate - and the truncation rule that keeps it inside one non-wrapping HUD line
/// is the part that can silently regress, so it is pinned here.
/// </summary>
public class RemainderMessageTests
{
    private static IReadOnlyList<(string Name, int Amount)> Items(params (string, int)[] items) => items;

    [Fact]
    public void SingleItemIsNamedInFull()
    {
        Assert.Equal(
            "still carrying 3140x Silicon",
            Notifier.DescribeRemainder(Items(("Silicon", 3140))));
    }

    [Fact]
    public void TwoItemsBothSurvive()
    {
        // The reported case. Both must appear; one row, so the HUD cannot drop either.
        Assert.Equal(
            "still carrying 1348x Cobalt, 3140x Silicon",
            Notifier.DescribeRemainder(Items(("Cobalt", 1348), ("Silicon", 3140))));
    }

    [Fact]
    public void OverflowIsCountedRatherThanNamed()
    {
        Assert.Equal(
            "still carrying 1348x Cobalt, 3140x Silicon +2 more",
            Notifier.DescribeRemainder(Items(
                ("Cobalt", 1348), ("Silicon", 3140), ("Iron", 10), ("Nickel", 20))));
    }

    [Fact]
    public void TwoItemsNeverSayMore()
    {
        // Off-by-one guard: "+0 more" would be worse than saying nothing.
        var message = Notifier.DescribeRemainder(Items(("Cobalt", 1), ("Silicon", 2)));
        Assert.DoesNotContain("more", message);
    }

    [Fact]
    public void StaysWithinOneHudLine()
    {
        // The HUD notification does not wrap and is at most 480px wide - roughly sixty characters.
        // Long names are the realistic worst case.
        var message = Notifier.DescribeRemainder(Items(
            ("Heavy-Duty Plate", 999999), ("Construction Component", 999999),
            ("Motor", 1), ("Steel Tube", 2), ("Silicon", 3)));

        Assert.True(message.Length <= 80, $"remainder message too long for the HUD: {message}");
    }

    [Fact]
    public void EmptyRemainderDoesNotClaimOne()
    {
        Assert.Equal(
            "Build Planner: nothing was deposited",
            Notifier.DescribeRemainder(Items()));
    }
}
