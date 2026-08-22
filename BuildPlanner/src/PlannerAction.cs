namespace BuildPlanner;

/// <summary>
/// Everything the Build Planner can be asked to do. One input action per member
/// (see <see cref="BuildPlannerActions.All"/>), each separately rebindable.
/// </summary>
/// <remarks>
/// These were once a single key plus live modifier sampling, mirroring the SE1 Build Planner's
/// one-button scheme. The chords survive as the *default* bindings, but they are now real bindings
/// rather than keyboard state read at press time - which is what lets the controls menu rebind each
/// one on its own.
/// </remarks>
internal enum PlannerAction
{
    /// <summary>Queue the block being looked at. Only live while a welder is showing its panel.</summary>
    Queue,

    /// <summary>Withdraw queued components and clear the queue.</summary>
    Withdraw,

    /// <summary>Withdraw but keep the queue, for building the same thing repeatedly.</summary>
    WithdrawKeepQueue,

    /// <summary>Withdraw tenfold, keep the queue.</summary>
    WithdrawTenKeepQueue,

    /// <summary>Deposit the player's inventory into the target.</summary>
    Deposit,

    /// <summary>Queue the missing components for production at a connected assembler.</summary>
    Produce,

    /// <summary>Produce tenfold amounts.</summary>
    ProduceTen,

    /// <summary>
    /// Empty the queue without withdrawing anything.
    ///
    /// Previously there was no way to do this at all - the queue could only be cleared as a side
    /// effect of a successful withdrawal, so a player who queued the wrong block was stuck with it.
    /// </summary>
    ClearQueue,

    /// <summary>
    /// Dump runtime state to the log.
    ///
    /// The nearest thing to a breakpoint available here - a plugin cannot attach a debugger, and a
    /// game restart plus world load costs about five minutes, so snapshotting live state at the exact
    /// moment something looks wrong is worth a binding.
    /// </summary>
    Diagnose
}
