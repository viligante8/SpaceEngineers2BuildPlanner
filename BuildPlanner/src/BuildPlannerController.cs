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
/// Drives the Build Planner: turns middle-click (+ modifiers) into queue and withdrawal operations.
///
/// Reproduces the SE1 Build Planner control scheme documented in notes/build-planner-ux-spec.md:
///   middle-click             withdraw queued components, clear queue
///   CTRL + middle-click      withdraw, keep queue
///   ALT + CTRL + middle-click withdraw x10, keep queue
///   ALT + middle-click       deposit inventory
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

    /// <summary>Add a block's requirements to the queue. Bound to the queueing input.</summary>
    internal void QueueBlock(CubeBlockDefinition? block)
    {
        if (block == null) return;

        _queue.Add(block);
        _notifier.QueuedBlock(block.UIData?.Name.ToString() ?? "block", _queue.Count);
    }

    internal void ClearQueue()
    {
        _queue.Clear();
        _notifier.QueueCleared();
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
    internal void OnSecondaryAction()
    {
        try
        {
            BuildPlannerBinding.LogContextState("on right-click:");

            // Primary source: the blocks the game's own tooltip is showing. This is the same data
            // that drives the "you need N x Steel Plate" panel, so what gets queued is by definition
            // what the player is looking at. See IntegrityToolAccess for why the two earlier routes
            // (block placer, interacted entity) were both wrong.
            var targeted = IntegrityToolAccess.GetTargetedBlocks();
            if (targeted.Count > 0)
            {
                foreach (var definition in targeted) QueueBlock(definition);
                return;
            }

            var session = _session();
            if (session == null)
            {
                _notifier.Warning("Build Planner: no active session");
                return;
            }

            var character = PlayerAccess.GetLocalCharacter(session);

            // The block placer is client-side (Game2.Client.GameSystems.BlockPlacement), so it lives
            // on the CLIENT session's character - not the server character that owns the inventory.
            // Both exist in-process and are confusingly both named "CompositeCharacterServer".
            var clientCharacter = PlayerAccess.GetClientCharacter(_clientSession());
            if (clientCharacter == null)
            {
                // Falling back to the server character is near-certain to fail — the block placer is
                // a client-only component and the server character does not carry one — but it is
                // kept so a layout we have not seen still gets a chance. Say which half is in use so
                // a later "no placer" line is attributable.
                Log.Debug("  debug: no client character; falling back to the server character" +
                          " (the block placer is client-side, so this will likely find nothing)");
            }

            var placementTarget = PlayerAccess.GetAlignedBlockTarget(clientCharacter ?? character, _clientSession());
            if (placementTarget != null)
            {
                if (placementTarget.BuildProgress >= 1f)
                {
                    _notifier.AlreadyComplete();
                    return;
                }

                QueueBlock(placementTarget.Definition);
                return;
            }

            // NO interaction-based fallback.
            //
            // There used to be one here: GetAimedEntity -> IInteractedEntityProvider ("what entity is
            // being interacted with", i.e. the press-F target), then TryGet<CubeBlockComponent>.
            //
            // That is NOT the block under the crosshair, and it produced a silent wrong answer.
            // Observed in game: the player right-clicked a heavy armor block and the mod queued
            // "Light Armor Cube", then withdrew its 1 x Steel Plate instead of the ~50 the intended
            // block needed. The player reasonably read that as a withdrawal bug; it was a queueing
            // bug, and the fallback hid it by always producing *something* plausible.
            //
            // Queueing the wrong block is worse than queueing nothing: the withdrawal is exact, so a
            // wrong queue silently yields a confidently wrong amount. If the placer cannot resolve a
            // target, say so and queue nothing.
            _notifier.NothingToQueue();
        }
        catch (Exception ex)
        {
            Log.Error("secondary action failed", ex);
        }
    }

    /// <summary>Handle a middle-click, dispatching on the modifiers held right now.</summary>
    internal void OnTertiaryAction()
    {
        try
        {
            switch (Modifiers.Resolve())
            {
                case PlannerAction.Withdraw:
                    Withdraw(multiplier: 1, keepQueue: false);
                    break;
                case PlannerAction.WithdrawKeepQueue:
                    Withdraw(multiplier: 1, keepQueue: true);
                    break;
                case PlannerAction.WithdrawTenKeepQueue:
                    Withdraw(multiplier: 10, keepQueue: true);
                    break;
                case PlannerAction.Deposit:
                    Deposit();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("tertiary action failed", ex);
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
                if (stack.Definition != null && !toMove.Contains(stack.Definition))
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
