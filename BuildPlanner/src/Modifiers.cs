using System;
using Keen.VRage.Core.Input;
using Keen.VRage.Input;
using Keen.VRage.Library.Utils;

namespace BuildPlanner;

/// <summary>
/// Which action a middle-click means, decided by the modifier keys held at the moment of the click.
/// Mirrors the SE1 Build Planner (see notes/build-planner-ux-spec.md).
/// </summary>
internal enum PlannerAction
{
    /// <summary>Plain: withdraw queued components and clear the queue.</summary>
    Withdraw,

    /// <summary>CTRL: withdraw but keep the queue, for building the same thing repeatedly.</summary>
    WithdrawKeepQueue,

    /// <summary>ALT+CTRL: withdraw tenfold, keep the queue.</summary>
    WithdrawTenKeepQueue,

    /// <summary>ALT: deposit the player's inventory into the target.</summary>
    Deposit,

    /// <summary>
    /// SHIFT: dump runtime state to the log.
    ///
    /// The nearest thing to a breakpoint available here - a plugin cannot attach a debugger, and a
    /// game restart plus world load costs about five minutes, so being able to snapshot live state
    /// at the exact moment something looks wrong is worth a keybind. SHIFT was already unmapped.
    /// </summary>
    Diagnose
}

/// <summary>
/// Reads live modifier-key state.
///
/// The input action itself (ToolTertiaryAction = Mouse::Middle) carries no modifier information, so
/// the keyboard is sampled directly when the click arrives. That matches how the SE1 scheme works:
/// one button, meaning selected by whatever is held.
/// </summary>
internal static class Modifiers
{
    internal static bool Ctrl => IsDown(KeyboardInputs.Control);
    internal static bool Alt => IsDown(KeyboardInputs.Alt);
    internal static bool Shift => IsDown(KeyboardInputs.Shift);

    /// <summary>
    /// Map the currently-held modifiers onto an action.
    /// </summary>
    /// <remarks>
    /// SHIFT variants in SE1 trigger *production* (queue components at an assembler). Production is
    /// not implemented in this build, so SHIFT is deliberately not mapped here rather than silently
    /// behaving like a plain withdraw — doing the wrong thing quietly is worse than doing nothing.
    /// </remarks>
    internal static PlannerAction Resolve() => Resolve(Ctrl, Alt, Shift);

    /// <summary>
    /// The modifier-to-action mapping itself, with the live input read lifted out so it can be
    /// unit-tested. <see cref="Resolve()"/> samples the keyboard and delegates here.
    /// </summary>
    internal static PlannerAction Resolve(bool ctrl, bool alt, bool shift = false)
    {
        // Checked first: a diagnostic dump must never be mistaken for a withdrawal, and previously
        // SHIFT was ignored entirely, so SHIFT+N silently performed a plain withdraw.
        if (shift) return PlannerAction.Diagnose;

        if (alt && ctrl) return PlannerAction.WithdrawTenKeepQueue;
        if (alt) return PlannerAction.Deposit;
        if (ctrl) return PlannerAction.WithdrawKeepQueue;
        return PlannerAction.Withdraw;
    }

    private static bool IsDown(InputId input)
    {
        try
        {
            if (!ManualSingleton<InputDeviceManager>.HasValue) return false;

            var keyboard = Singleton<InputDeviceManager>.Instance.Keyboard;
            if (keyboard == null) return false;

            return new DigitalInput(input).IsActive(keyboard);
        }
        catch (Exception ex)
        {
            Log.Error("modifier read failed", ex);
            return false;
        }
    }
}
