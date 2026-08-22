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

    [Fact]
    public void Shift_Produces()
    {
        Assert.Equal(PlannerAction.Produce, Modifiers.Resolve(ctrl: false, alt: false, shift: true));
    }

    [Fact]
    public void ShiftCtrl_ProducesTenfold()
    {
        Assert.Equal(PlannerAction.ProduceTen, Modifiers.Resolve(ctrl: true, alt: false, shift: true));
    }

    [Fact]
    public void ShiftAlt_ClearsTheQueue()
    {
        Assert.Equal(PlannerAction.ClearQueue, Modifiers.Resolve(ctrl: false, alt: true, shift: true));
    }

    [Fact]
    public void ShiftAltCtrl_Diagnoses()
    {
        Assert.Equal(PlannerAction.Diagnose, Modifiers.Resolve(ctrl: true, alt: true, shift: true));
    }

    /// <summary>
    /// The developer chord must not start production. SHIFT+ALT+CTRL differs from SHIFT+CTRL
    /// (ProduceTen) by ALT alone, so if the SHIFT branch ever tested CTRL before ALT, reaching for
    /// the diagnostic dump would instead enqueue tenfold work at a real assembler - consuming the
    /// player's ore. The ordering is load-bearing, so it is pinned here.
    /// </summary>
    [Fact]
    public void DiagnoseChord_DoesNotProduce()
    {
        var diagnose = Modifiers.Resolve(ctrl: true, alt: true, shift: true);

        Assert.NotEqual(PlannerAction.Produce, diagnose);
        Assert.NotEqual(PlannerAction.ProduceTen, diagnose);
    }

    /// <summary>
    /// Production must never be reachable without SHIFT. The plain and CTRL chords move real items
    /// into the player's hands; silently enqueueing assembler work instead would be the same class
    /// of silent wrong behaviour that made SHIFT+N perform a withdraw before SHIFT was handled.
    /// </summary>
    [Fact]
    public void WithoutShift_NeverProduces()
    {
        foreach (var (ctrl, alt) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var action = Modifiers.Resolve(ctrl, alt, shift: false);
            Assert.NotEqual(PlannerAction.Produce, action);
            Assert.NotEqual(PlannerAction.ProduceTen, action);
        }
    }

    /// <summary>
    /// No SHIFT action may fall through to a withdrawal or a deposit. Before SHIFT was handled,
    /// Resolve ignored it entirely and SHIFT+N silently performed a plain withdraw - the exact class
    /// of silent wrong behaviour this feature cannot afford, since withdrawals move real items.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Shift_NeverWithdrawsOrDeposits(bool ctrl, bool alt)
    {
        var action = Modifiers.Resolve(ctrl, alt, shift: true);

        Assert.NotEqual(PlannerAction.Withdraw, action);
        Assert.NotEqual(PlannerAction.WithdrawKeepQueue, action);
        Assert.NotEqual(PlannerAction.WithdrawTenKeepQueue, action);
        Assert.NotEqual(PlannerAction.Deposit, action);
    }

    [Fact]
    public void WithoutShift_NeverClearsOrDiagnoses()
    {
        foreach (var (ctrl, alt) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var action = Modifiers.Resolve(ctrl, alt, shift: false);
            Assert.NotEqual(PlannerAction.Diagnose, action);
            Assert.NotEqual(PlannerAction.ClearQueue, action);
        }
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
