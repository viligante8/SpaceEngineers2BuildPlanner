using System;
using System.Collections.Generic;
using System.Reflection;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Client.WorldObjects.Tools;
using Keen.Game2.Simulation.GameSystems.BuildPlanners;
using Keen.Game2.Simulation.GameSystems.Player;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.Core.Game.Systems;

namespace BuildPlanner;

/// <summary>
/// Mirrors the mod's queue into the engine's own <see cref="BuildPlannerData"/>.
///
/// **Why.** SE2 ships a build planner data model that nothing populates:
/// <c>BuildPlannerData : PerPlayerData</c> with a <c>PlannedBlocks</c> list. The terminal screen is
/// already wired to it — <c>TerminalScreenViewModel</c> holds a <c>_buildPlannerData</c> field, an
/// <c>AvaloniaList&lt;BuildPlannerBlockModel&gt; BuildPlannerBlocks</c> bound to the UI, and
/// <c>UpdateBuildPlannerBlocks</c>, <c>BuildPlannerBlock_ClearAll</c> and
/// <c>BuildPlannerBlock_ScheduleAll</c> methods. Keen built the screen and left the data unfilled.
///
/// Filling it is how the queue becomes visible in the game's own UI instead of only in a log line.
///
/// **Strictly best-effort.** The withdrawal path does not read any of this — <see
/// cref="BuildPlannerQueue"/> remains the source of truth — so every failure here is logged and
/// swallowed. Mirroring must never be able to break queueing or withdrawal.
///
/// **Unverified:** whether the terminal refreshes while already open. <c>UpdateBuildPlannerBlocks</c>
/// is a <c>PropertyChangedEventArgs</c> handler and a plain <c>List.Add</c> raises no such event, so
/// the screen may only pick the queue up when it is next opened. Also unverified is the meaning of
/// the second parameter of <c>BuildPlannerData.AddPlannedBlock(CubeBlockDefinition, int)</c> — count
/// or index — which is why this writes to the <c>PlannedBlocks</c> list directly, where the types
/// leave no room for a wrong guess.
/// </summary>
internal static class EngineQueueMirror
{
    /// <summary>
    /// Replace the engine's planned-block list with the mod's queue.
    ///
    /// Called after every queue change. Rebuilding the whole list rather than tracking deltas keeps
    /// this a pure projection of our queue — there is no second piece of state to drift.
    /// </summary>
    internal static void Sync(IReadOnlyList<CubeBlockDefinition> queue, Session? clientSession)
    {
        try
        {
            var data = Resolve(clientSession);
            if (data == null) return; // Resolve has already said why.

            var planned = data.PlannedBlocks;
            if (planned == null)
            {
                Log.Debug("  debug: BuildPlannerData.PlannedBlocks is null; not mirroring");
                return;
            }

            planned.Clear();
            foreach (var block in queue)
                if (block != null) planned.Add(block);

            Log.Debug($"  debug: mirrored {planned.Count} block(s) into the engine build planner");
        }
        catch (Exception ex)
        {
            // Never fatal: the queue the player actually uses is unaffected.
            Log.Error("mirroring the queue into BuildPlannerData failed", ex);
        }
    }

    /// <summary>
    /// The local player's <see cref="BuildPlannerData"/>.
    ///
    /// <c>IPerPlayerData.GetPerPlayerData&lt;T&gt;(IdentityId)</c> is the intended accessor. The
    /// service instance is borrowed from the captured integrity tool component (its
    /// <c>_playerData</c> field) rather than resolved independently, so it is the same one the game
    /// is using; the identity comes from <c>ClientPlayersSessionComponent.LocalPlayerIdentity</c>,
    /// a public property.
    /// </summary>
    private static BuildPlannerData? Resolve(Session? clientSession)
    {
        var tool = IntegrityToolAccess.Captured;
        if (tool == null)
        {
            Log.Debug("  debug: no integrity tool captured; cannot reach per-player data");
            return null;
        }

        if (clientSession == null)
        {
            Log.Debug("  debug: no client session; cannot resolve the local player identity");
            return null;
        }

        _playerDataField ??= typeof(IntegrityToolUIComponent).GetField(
            "_playerData", BindingFlags.Instance | BindingFlags.NonPublic);

        if (_playerDataField?.GetValue(tool) is not IPerPlayerData perPlayerData)
        {
            Log.Debug("  debug: IntegrityToolUIComponent._playerData unavailable");
            return null;
        }

        var players = clientSession.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
        if (players == null)
        {
            Log.Debug("  debug: no ClientPlayersSessionComponent; cannot resolve identity");
            return null;
        }

        return perPlayerData.GetPerPlayerData<BuildPlannerData>(players.LocalPlayerIdentity);
    }

    private static FieldInfo? _playerDataField;
}
