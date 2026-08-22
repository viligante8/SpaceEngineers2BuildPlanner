using System;
using System.Collections.Generic;
using Keen.Game2.Client.UI.HUD.Notification;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.Game2.Simulation.WorldObjects.Tools;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;

namespace BuildPlanner;

/// <summary>
/// Drives the Build Planner: turns input actions into queue, withdrawal and production operations.
///
/// Reproduces the SE1 Build Planner control scheme documented in notes/build-planner-ux-spec.md,
/// with the SE1 chords kept as the default bindings (see <see cref="BuildPlannerActions.All"/>):
///   N                 withdraw queued components, clear queue
///   CTRL + N          withdraw, keep queue
///   ALT + CTRL + N    withdraw x10, keep queue
///   ALT + N           deposit inventory
///   SHIFT + N         produce queued components at a connected assembler
///   SHIFT + CTRL + N  produce x10
///
/// Every one of those is a separate action, so the player can rebind any of them individually.
///
/// Queueing stays explicit, as in SE1: blocks never enter the queue on their own.
/// </summary>
internal sealed class BuildPlannerController
{
    private readonly BuildPlannerQueue _queue = new BuildPlannerQueue();
    private readonly Notifier _notifier;
    private readonly Func<Session?> _session;
    private readonly Func<Session?> _clientSession;

    internal BuildPlannerController(
        Notifier notifier,
        Func<Session?> session,
        Func<Session?> clientSession)
    {
        _notifier = notifier;
        _session = session;
        _clientSession = clientSession;
    }

    internal BuildPlannerQueue Queue => _queue;

    /// <summary>
    /// Add a block's requirements to the queue. Bound to the queueing input, and used by the build
    /// menu.
    /// </summary>
    /// <param name="displayName">
    /// What to call the block in the notification. Every grid size of a block shares one
    /// <c>UIData.Name</c> — both drills are "Drill" — so callers that know the precise variant (the
    /// build menu does) pass its name, and the player can see which size was queued.
    /// </param>
    internal void QueueBlock(
        CubeBlockDefinition? block, CubeBlockComponent? built = null, string? displayName = null)
    {
        if (block == null) return;

        // Queue what is still OUTSTANDING, not the whole recipe. A partly welded block needs the
        // remainder; asking for a full block's worth hands the player components they already put in.
        _queue.Add(block, BlockRequirements.Remaining(built, block));
        _notifier.QueuedBlock(displayName ?? block.UIData?.Name.ToString() ?? "block", _queue.Count);
        EngineQueueMirror.Sync(_queue.Blocks, _clientSession(), _session());
    }

    /// <summary>
    /// Queue everything the build tool is showing, reporting once.
    ///
    /// An area welder's panel lists every block its area covers — dozens, potentially — and each one
    /// announcing itself would bury the HUD. A single block still gets its own name, since that is
    /// the common case and the name is the useful part.
    /// </summary>
    internal void QueueBlocks(IReadOnlyList<(CubeBlockComponent? Built, CubeBlockDefinition Definition)> blocks)
    {
        if (blocks.Count == 0) return;

        if (blocks.Count == 1)
        {
            QueueBlock(blocks[0].Definition, blocks[0].Built);
            return;
        }

        var added = 0;
        foreach (var (built, definition) in blocks)
        {
            if (definition == null) continue;

            _queue.Add(definition, BlockRequirements.Remaining(built, definition));
            added++;
        }

        if (added == 0) return;

        _notifier.QueuedBlocks(added, _queue.Count);
        EngineQueueMirror.Sync(_queue.Blocks, _clientSession(), _session());
    }

    internal void ClearQueue()
    {
        // Report the no-op case distinctly: "queue cleared" when nothing was queued reads as though
        // something was discarded, and the player cannot see the queue to know otherwise.
        if (_queue.Count == 0)
        {
            _notifier.NothingQueued();
            return;
        }

        var cleared = _queue.Count;
        _queue.Clear();
        _notifier.QueueCleared(cleared);
        EngineQueueMirror.Sync(_queue.Blocks, _clientSession(), _session());
    }

    /// <summary>
    /// Right-click: queue the block being looked at, matching SE1's "right-click unwelded blocks
    /// while holding a welder".
    ///
    /// Handles both real unwelded blocks and *projections*. Projections are the important case for
    /// building from a blueprint, and they are not entities — ProjectionBlockPlacementTarget is
    /// documented as a "Non-real block", so TryGet&lt;CubeBlockComponent&gt; on the aimed entity finds
    /// nothing and the earlier implementation silently did nothing when aiming at one.
    ///
    /// Both cases are covered by the block placer's own resolved target: BlockPlacementAlignment
    /// sets AlignedBlock to a CubeBlockPlacementTarget for a real block and a
    /// ProjectionBlockPlacementTarget for a projection, and both expose Definition and BuildProgress.
    /// </summary>
    private void QueueAimedBlock()
    {
        BuildPlannerBinding.LogContextState("on the queue key:");

        // Primary source: the blocks the game's own tooltip is showing. This is the same data
        // that drives the "you need N x Steel Plate" panel, so what gets queued is by definition
        // what the player is looking at. See IntegrityToolAccess for why the two earlier routes
        // (block placer, interacted entity) were both wrong.
        var targeted = IntegrityToolAccess.GetTargetedBlocks();
        if (targeted.Count > 0)
        {
            QueueBlocks(targeted);
            return;
        }

        // No fallback. The integrity tool is the ONLY source.
        //
        // A block-placer fallback used to sit here. It never resolved in welder mode - every
        // right-click logged "no BlockPlacerEntityComponent on character" - but it resolves
        // perfectly in block PLACEMENT mode, because that is when a block placer exists and has
        // an aligned block. So the one situation it worked in was the one situation we must not
        // queue in, and it defeated the placement-mode guard by running after it:
        //
        //   Build Planner: no build tool active (equip a welder and aim at a block)   <- guard OK
        //   notify: Build Planner: queued Gearforge (1 total)                          <- fallback
        //
        // Observed in game 2026-08-22. Queueing requires an active welder, full stop.
        _notifier.NothingToQueue();
    }

    /// <summary>
    /// Run one Build Planner operation. Every input action lands here, carrying what it means.
    ///
    /// The operation is decided by which action fired, not by reading the keyboard: each action is
    /// separately bound and separately rebindable, so the chords in the defaults are just defaults.
    /// </summary>
    internal void Perform(PlannerAction action)
    {
        try
        {
            if (BuildPlannerBinding.IsGamePaused())
            {
                // No notification: the HUD is behind the menu and the player did not mean to act.
                Log.Debug($"  debug: game is paused; ignoring {action}");
                return;
            }

            switch (action)
            {
                case PlannerAction.Queue:
                    QueueAimedBlock();
                    break;
                case PlannerAction.Withdraw:
                    Withdraw(multiplier: 1, keepQueue: false);
                    break;
                case PlannerAction.WithdrawKeepQueue:
                    Withdraw(multiplier: 1, keepQueue: true);
                    break;
                case PlannerAction.WithdrawTenKeepQueue:
                    Withdraw(multiplier: 10, keepQueue: true);
                    break;
                case PlannerAction.Produce:
                    Produce(multiplier: 1);
                    break;
                case PlannerAction.ProduceTen:
                    Produce(multiplier: 10);
                    break;
                case PlannerAction.Deposit:
                    Deposit();
                    break;
                case PlannerAction.ClearQueue:
                    ClearQueue();
                    break;
                case PlannerAction.Diagnose:
                    Diagnostics.DumpAll(_queue, _clientSession(), _session());
                    _notifier.Info("Build Planner: state written to the log");
                    break;
                default:
                    // Never silent: an unhandled action would otherwise look like a dead keybind.
                    Log.Write($"  WARNING: no handler for {action}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"{action} failed", ex);
        }
    }

    private void Withdraw(int multiplier, bool keepQueue)
    {
        if (_queue.Count == 0)
        {
            _notifier.NothingQueued();
            return;
        }

        var session = _session();
        if (session == null)
        {
            // Every early exit reports itself: a silent return here made the withdrawal look like a
            // dead keybind for several test rounds, when the engine trace proved the key was
            // dispatching correctly all along.
            _notifier.Warning("Build Planner: no active session");
            return;
        }

        var character = PlayerAccess.GetLocalCharacter(session);
        if (character == null)
        {
            _notifier.Warning("Build Planner: could not find your character");
            return;
        }

        var destination = PlayerAccess.GetCharacterInventory(character, session);
        if (destination == null)
        {
            _notifier.Warning("Build Planner: could not find your inventory");
            return;
        }

        var target = GetAimedEntity(character);
        if (target == null)
        {
            _notifier.NoTarget();
            return;
        }

        var sources = InventorySources.CollectFrom(target);
        if (sources.Count == 0)
        {
            _notifier.NoTarget();
            return;
        }

        var required = _queue.GetRequiredComponents(multiplier);
        var result = ComponentWithdrawal.Withdraw(destination, sources, required);

        switch (result.Outcome)
        {
            case WithdrawalOutcome.AlreadySatisfied:
                _notifier.AlreadyHaveEverything();
                if (!keepQueue) _queue.Clear();
                break;

            case WithdrawalOutcome.Complete:
                _notifier.Withdrew(result.Transferred);
                if (!keepQueue) _queue.Clear();
                break;

            case WithdrawalOutcome.Partial:
                // Keep the queue regardless: the player still needs the remainder, and clearing it
                // would silently discard what they asked for.
                _notifier.WithdrewPartial(result.Transferred, result.StillMissing);
                break;

            case WithdrawalOutcome.Nothing:
                _notifier.NothingAvailable(result.StillMissing);
                break;
        }
    }

    /// <summary>
    /// SHIFT: queue the components the player is short of at a reachable assembler.
    ///
    /// **The queue is never cleared here, on any path.** Production only *starts* the components; the
    /// player still has to come back and withdraw them once they exist. Clearing on produce would
    /// leave them with an assembler full of parts and no record of what they were for — and unlike a
    /// withdrawal, there is nothing in their inventory afterwards to reconstruct it from.
    ///
    /// Sub-components are not this method's problem. Enqueueing a Steel Plate recipe at an assembler
    /// that has no iron makes the engine request ingots from any connected converter that can make
    /// them, which in turn requests ore — see <see cref="ComponentProduction"/> for the verified
    /// chain. This asks only for the top-level components the queued blocks need.
    /// </summary>
    private void Produce(int multiplier)
    {
        if (_queue.Count == 0)
        {
            _notifier.NothingQueued();
            return;
        }

        var session = _session();
        if (session == null)
        {
            _notifier.Warning("Build Planner: no active session");
            return;
        }

        var character = PlayerAccess.GetLocalCharacter(session);
        if (character == null)
        {
            _notifier.Warning("Build Planner: could not find your character");
            return;
        }

        // The player's inventory is the yardstick, not a destination: production is measured against
        // what they already carry so the same components are not made twice. Nothing is moved here.
        var carried = PlayerAccess.GetCharacterInventory(character, session);
        if (carried == null)
        {
            _notifier.Warning("Build Planner: could not find your inventory");
            return;
        }

        var target = GetAimedEntity(character);
        if (target == null)
        {
            _notifier.NoTarget();
            return;
        }

        var converters = InventorySources.CollectConvertersFrom(target);
        if (converters.Count == 0)
        {
            _notifier.NoConverter();
            return;
        }

        var required = _queue.GetRequiredComponents(multiplier);
        var result = ComponentProduction.Produce(carried, converters, required);

        switch (result.Outcome)
        {
            case ProductionOutcome.AlreadySatisfied:
                _notifier.AlreadyHaveEverythingToProduce();
                break;

            case ProductionOutcome.Complete:
                _notifier.Producing(result.Enqueued);
                break;

            case ProductionOutcome.Partial:
                _notifier.ProducingPartial(result.Enqueued, result.Unproducible);
                break;

            case ProductionOutcome.Nothing:
                _notifier.CannotProduce(result.Unproducible);
                break;

            case ProductionOutcome.NoConverter:
                // Reachable even though the count was checked above: Produce re-checks its own
                // arguments, and a converter list that emptied in between must still report itself.
                _notifier.NoConverter();
                break;
        }
    }

    /// <summary>ALT + middle-click: push the player's inventory into the target container.</summary>
    private void Deposit()
    {
        var session = _session();
        if (session == null)
        {
            _notifier.Warning("Build Planner: no active session");
            return;
        }

        var character = PlayerAccess.GetLocalCharacter(session);
        var source = PlayerAccess.GetCharacterInventory(character, session);
        if (source == null)
        {
            _notifier.Warning("Build Planner: could not find your inventory");
            return;
        }

        var target = GetAimedEntity(character);
        var destinations = InventorySources.CollectFrom(target);
        if (destinations.Count == 0)
        {
            _notifier.NoTarget();
            return;
        }

        var moved = 0;

        // Snapshot what to move first: transferring mutates the inventory being iterated.
        var toMove = new List<ItemDefinition>();
        try
        {
            // IterateItemsReverse yields (ItemStack Stack, int Index) pairs.
            foreach (var (stack, _) in source.IterateItemsReverse())
            {
                if (stack.Definition == null || toMove.Contains(stack.Definition)) continue;

                if (!IsBuildMaterial(stack.Definition))
                {
                    // Deposit used to empty everything, tools included - it cheerfully posted the
                    // player's welder and grinder into a container. Reported in game.
                    Log.Debug($"  debug: keeping '{stack.Definition.DisplayName}' (not a build material)");
                    continue;
                }

                toMove.Add(stack.Definition);
            }
        }
        catch (Exception ex)
        {
            Log.Error("enumerating inventory for deposit failed", ex);
            return;
        }

        foreach (var itemDef in toMove)
        {
            foreach (var destination in destinations)
            {
                if (destination == null || ReferenceEquals(destination, source)) continue;

                try
                {
                    if (source.TransferByDef(destination, itemDef, null, null, true) > 0)
                    {
                        moved++;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"deposit of {itemDef.DisplayName} failed", ex);
                }
            }
        }

        _notifier.Deposited(moved);
    }

    /// <summary>
    /// Whether an item is something a builder wants to hand over to storage.
    /// </summary>
    /// <remarks>
    /// <c>ItemDefinition.Type</c> is documented as "the type(s) this item belongs to" and its enum
    /// <c>ItemTypes</c> is Ore, Material, Component, Item, Consumable, Datapad (plus PresetNone and
    /// PresetAll). Deposit takes the three that are build inputs and leaves the rest, so tools,
    /// consumables and datapads stay on the player.
    ///
    /// An allow-list, not a deny-list: a new item type should default to staying in the player's
    /// inventory. Wrongly depositing a tool is far worse than wrongly keeping some ore.
    /// </remarks>
    private static bool IsBuildMaterial(ItemDefinition item)
    {
        const ItemTypes BuildInputs = ItemTypes.Ore | ItemTypes.Material | ItemTypes.Component;
        return (item.Type & BuildInputs) != 0;
    }

    /// <summary>
    /// The entity the player is interacting with, via the character's interacted-entity provider —
    /// the same mechanism the game uses for "press F to use this block".
    /// </summary>
    private static Entity? GetAimedEntity(Entity? character)
    {
        if (character == null) return null;

        try
        {
            // Same lookup FirstInteractedEntityProviderAdapterComponent performs: the provider may sit
            // on the character or anywhere up its hierarchy, and it is an interface rather than a
            // Component subclass, so TryGet<T> cannot be used.
            var provider = character.FirstOrDefault<IInteractedEntityProvider>();
            return provider?.InteractedEntity;
        }
        catch (Exception ex)
        {
            Log.Error("resolving aimed entity failed", ex);
            return null;
        }
    }
}
