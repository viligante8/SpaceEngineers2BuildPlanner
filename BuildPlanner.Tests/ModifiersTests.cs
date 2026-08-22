using BuildPlanner;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// The modifier-to-action mapping (notes/build-planner-ux-spec.md).
///
/// This is regression-proofing for refactors, not bug-hunting: the mapping is the one part of the
/// input path that is decidable without the game running. Whether the key actually *arrives* is an
/// integration fact and is covered by the in-game log, not here.
/// </summary>
public class ModifiersTests
{
    [Fact]
    public void NoModifiers_Withdraws()
    {
        Assert.Equal(PlannerAction.Withdraw, Modifiers.Resolve(ctrl: false, alt: false));
    }

    [Fact]
    public void Ctrl_KeepsTheQueue()
    {
        Assert.Equal(PlannerAction.WithdrawKeepQueue, Modifiers.Resolve(ctrl: true, alt: false));
    }

    [Fact]
    public void Alt_Deposits()
    {
        Assert.Equal(PlannerAction.Deposit, Modifiers.Resolve(ctrl: false, alt: true));
    }

    [Fact]
    public void AltCtrl_WithdrawsTenfoldAndKeepsTheQueue()
    {
        Assert.Equal(PlannerAction.WithdrawTenKeepQueue, Modifiers.Resolve(ctrl: true, alt: true));
    }

    /// <summary>
    /// ALT+CTRL must not degrade into plain Deposit or WithdrawKeepQueue. The combined case is
    /// checked before either single-modifier case, and reordering those branches would silently
    /// change the meaning of the most destructive combination (x10).
    /// </summary>
    [Fact]
    public void CombinedModifiers_TakePrecedenceOverEitherAlone()
    {
        var both = Modifiers.Resolve(ctrl: true, alt: true);

        Assert.NotEqual(Modifiers.Resolve(ctrl: true, alt: false), both);
        Assert.NotEqual(Modifiers.Resolve(ctrl: false, alt: true), both);
    }

    /// <summary>Every modifier combination maps to exactly one action - no combination is unhandled.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EveryCombination_IsADefinedAction(bool ctrl, bool alt)
    {
        Assert.True(System.Enum.IsDefined(typeof(PlannerAction), Modifiers.Resolve(ctrl, alt)));
    }
}
