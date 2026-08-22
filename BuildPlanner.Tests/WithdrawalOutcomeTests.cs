using BuildPlanner;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// Outcome classification for a withdrawal.
///
/// This decides which message the player sees, and getting it wrong is the difference between
/// "withdrew everything" and "still short 3x Steel Plate" — the whole point of the feature is that
/// the player can trust that answer without opening their inventory.
///
/// The shortfall count fed in is re-measured by the engine (InventoryComponent.FindMissingItems)
/// *after* the transfers, so these cases are about interpreting that measurement, not producing it.
/// The transfer itself needs a live InventoryComponent and is verified in-game instead.
/// </summary>
public class WithdrawalOutcomeTests
{
    [Fact]
    public void NothingStillMissing_IsComplete()
    {
        Assert.Equal(WithdrawalOutcome.Complete, ComponentWithdrawal.Classify(stillMissingCount: 0, anyMoved: true));
    }

    /// <summary>
    /// Nothing moved and nothing missing means the player already had everything. It reports Complete
    /// rather than Nothing: from the player's side the queue is satisfied, which is a success.
    /// (Withdraw itself short-circuits this case earlier as AlreadySatisfied; this guards the path
    /// where the pre-check passed but the post-transfer re-measure comes back clean.)
    /// </summary>
    [Fact]
    public void NothingMovedButNothingMissing_IsStillComplete()
    {
        Assert.Equal(WithdrawalOutcome.Complete, ComponentWithdrawal.Classify(stillMissingCount: 0, anyMoved: false));
    }

    [Fact]
    public void SomethingMovedButStillShort_IsPartial()
    {
        Assert.Equal(WithdrawalOutcome.Partial, ComponentWithdrawal.Classify(stillMissingCount: 2, anyMoved: true));
    }

    /// <summary>
    /// Still short and nothing moved is Nothing, not Partial. Partial tells the player they got
    /// something; saying that when the containers were empty would be a lie they act on.
    /// </summary>
    [Fact]
    public void NothingMovedAndStillShort_IsNothing()
    {
        Assert.Equal(WithdrawalOutcome.Nothing, ComponentWithdrawal.Classify(stillMissingCount: 1, anyMoved: false));
    }

    /// <summary>
    /// A shortfall never reports Complete regardless of how much moved — Complete is driven solely by
    /// the engine's re-measure, which is the check that catches a destination that hit a mass limit
    /// partway through.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(50, true)]
    [InlineData(50, false)]
    public void AnyRemainingShortfall_IsNeverComplete(int stillMissing, bool anyMoved)
    {
        Assert.NotEqual(WithdrawalOutcome.Complete, ComponentWithdrawal.Classify(stillMissing, anyMoved));
    }
}
