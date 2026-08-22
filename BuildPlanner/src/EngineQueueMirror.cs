using System;
using System.Collections.Generic;
using System.Reflection;
using Keen.Game2.Simulation.GameSystems.Ownership;
using Keen.Game2.Simulation.GameSystems.Player;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Simulation.GameSystems.BuildPlanners;
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
    internal static void Sync(IReadOnlyList<CubeBlockDefinition> queue, Session? clientSession, Session? serverSession)
    {
        try
        {
            var data = Resolve(clientSession, serverSession);
            if (data == null) return; // Resolve has already said why.

            var planned = data.PlannedBlocks;
            if (planned == null)
            {
                Log.Write("  mirror: BuildPlannerData.PlannedBlocks is null; nothing written");
                return;
            }

            // Use the engine's own mutators, NOT the list directly.
            //
            // Decompiled, both AddPlannedBlock and RemovePlannedBlock end with
            // OnPropertyChanged("PlannedBlocks"), and BuildPlannerData is marked [Replicate]. That
            // notification is what pushes the change to the client copy and what
            // TerminalScreenViewModel.UpdateBuildPlannerBlocks listens for.
            //
            // Writing to the List directly (as this did at first) mutates the server object silently:
            // the log said "wrote 2 block(s)" while the client-side instance still reported 0, because
            // no notification ever fired. AddPlannedBlock's second parameter is a count, defaulting
            // to 1 - confirmed from source, not guessed.
            for (var i = planned.Count - 1; i >= 0; i--) data.RemovePlannedBlock(i);
            foreach (var block in queue)
                if (block != null) data.AddPlannedBlock(block);

            // Always logged, not debug-gated. This line is the whole point of the mirror being
            // diagnosable: if it says N blocks were written and no screen shows them, the write
            // works and the UI is the problem. If it never appears, the write is the problem.
            // Those need completely different fixes, and without this line they are indistinguishable.
            Log.Write($"  mirror: wrote {planned.Count} block(s) into BuildPlannerData" +
                      " (should now be visible wherever the game surfaces the build planner)");
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
    internal static BuildPlannerData? Resolve(Session? clientSession, Session? serverSession)
    {
        if (clientSession == null)
        {
            Log.Write("  mirror: no client session; cannot resolve the local player identity");
            return null;
        }

        var players = clientSession.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
        if (players == null)
        {
            Log.Write("  mirror: no ClientPlayersSessionComponent; cannot resolve identity");
            return null;
        }

        var identity = players.LocalPlayerIdentity;

        // The SERVER store, not the client one.
        //
        // Verified in game: asking InProcessPerPlayerDataClientSessionComponent (which is what the
        // integrity tool holds) for BuildPlannerData returned null for every identity. That component
        // only exposes data the server has replicated down. The server component
        // InProcessPerPlayerDataSessionComponent is the one that owns and creates per-player data -
        // it is the only one with GetOrCreateData / ObserveAndReplicateData.
        var store = serverSession?.SessionComponents?.TryGet<InProcessPerPlayerDataSessionComponent>();
        if (store == null)
        {
            Log.Write("  mirror: no InProcessPerPlayerDataSessionComponent on the server session");
            return null;
        }

        var existing = store.GetPerPlayerData<BuildPlannerData>(identity);
        if (existing != null)
        {
            Log.Write($"  mirror: found existing BuildPlannerData for identity {identity}");
            return existing;
        }

        // Nothing in the shipping game ever creates a BuildPlannerData, so on a fresh world there is
        // simply no instance to write into. Create one the same way the engine would.
        return CreateData(store, identity);
    }

    /// <summary>
    /// Create the player's BuildPlannerData through the engine's own creation path.
    ///
    /// <c>GetOrCreateData&lt;T&gt;(IdentityId, List&lt;object&gt;)</c> takes the identity's data
    /// collection, which <c>GetOrCreateCollectionForIdentity</c> supplies. Both are reached by
    /// reflection because they are not part of the public IPerPlayerData surface.
    /// </summary>
    private static BuildPlannerData? CreateData(InProcessPerPlayerDataSessionComponent store, IdentityId identity)
    {
        try
        {
            var type = store.GetType();

            var getCollection = type.GetMethod(
                "GetOrCreateCollectionForIdentity",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var getOrCreate = type.GetMethod(
                "GetOrCreateData",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (getCollection == null || getOrCreate == null)
            {
                Log.Write($"  mirror: creation API missing (GetOrCreateCollectionForIdentity={getCollection != null}," +
                          $" GetOrCreateData={getOrCreate != null}); cannot create BuildPlannerData");
                return null;
            }

            var collection = getCollection.Invoke(store, new object[] { identity });
            if (collection == null)
            {
                Log.Write($"  mirror: no data collection for identity {identity}");
                return null;
            }

            var created = getOrCreate
                .MakeGenericMethod(typeof(BuildPlannerData))
                .Invoke(store, new[] { (object)identity, collection }) as BuildPlannerData;

            Log.Write($"  mirror: created BuildPlannerData for identity {identity}: {created != null}");
            return created;
        }
        catch (Exception ex)
        {
            // Non-fatal: queueing and withdrawal never touch this.
            Log.Error("creating BuildPlannerData failed", ex);
            return null;
        }
    }

}
