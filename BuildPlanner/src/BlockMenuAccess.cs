using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Input;
using Keen.Game2.Client.UI.TerminalScreen.GScreen;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.DCS.Definitions;
using Keen.VRage.DCS.ObjectBuilders;

namespace BuildPlanner;

/// <summary>
/// Queueing from the build menu (G), the way SE1's Build Planner lets you add a block you have not
/// placed yet.
///
/// **Why this hooks the UI and not the input system.**
/// While the build menu is open, right-click is already consumed by vanilla's <c>CursorButton2</c>
/// action in the UI cursor layer — confirmed in the engine's input trace:
///
/// <code>
/// [Input][#4028]: Control Keyboard::G : Build Menu activated with state Start.
/// [Input][#4061]: Consuming input Mouse::Right in layer #26:&lt;Uninitialized&gt;
/// [Input][#4061]: Control Mouse::Right : CursorButton2 activated with state Start.
/// </code>
///
/// An input is consumed by exactly one context per frame, and a layer-less context is dispatched
/// *before* the UI's — so claiming right-click here would have taken it away from the menu itself.
/// That matters: right-clicking a toolbar tile clears the slot (verified in game), and stealing the
/// button would have broken it.
///
/// Hooking the game's own Avalonia handlers avoids the contest entirely. Vanilla still gets its
/// click; we read the same event afterwards. The toolbar is untouched, because its presses go
/// through <c>GScreen.OnToolbarTilePointerPressed</c>, a different path from the catalogue's
/// <c>TilePressed</c>.
/// </summary>
internal static class BlockMenuAccess
{
    /// <summary>
    /// A catalogue tile was pressed. Right-click queues it; anything else is left alone.
    /// </summary>
    internal static void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsRightClick(e)) return;
        if (AlreadyHandled(e, sender)) return;

        var tile = TileOf(sender);
        if (tile == null)
        {
            Log.Debug("  debug: menu right-click with no TileModel on the sender; ignoring");
            return;
        }

        QueueTile(tile);
    }

    /// <summary>
    /// A size tile in the right-hand detail panel was pressed.
    ///
    /// This is the precise-control path: a block that exists in several grid sizes shows one tile per
    /// size there, so right-clicking one queues exactly that size rather than a variant we picked.
    /// </summary>
    internal static void OnSizeTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsRightClick(e)) return;
        if (AlreadyHandled(e, sender)) return;

        if (DataContextOf(sender) is not BlockSizeModel size)
        {
            Log.Debug("  debug: size-tile right-click with no BlockSizeModel; ignoring");
            return;
        }

        // BlockSizeModel.Block is the concrete BlockTileModel for that size.
        var block = ReadMember(size, "Block") as BlockTileModel;
        if (block == null)
        {
            Log.Write("  WARNING: BlockSizeModel.Block was not a BlockTileModel; nothing queued");
            return;
        }

        QueueTile(block);
    }

    /// <summary>A sub-tile (a block kind inside a group) in the detail panel was pressed.</summary>
    internal static void OnSubTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsRightClick(e)) return;
        if (AlreadyHandled(e, sender)) return;

        var tile = TileOf(sender);
        if (tile == null)
        {
            Log.Debug("  debug: sub-tile right-click with no TileModel on the sender; ignoring");
            return;
        }

        QueueTile(tile);
    }

    /// <summary>
    /// Turn whatever tile was right-clicked into a queued block, or say why it could not be.
    ///
    /// The tile hierarchy is deliberately reported at every step: the menu mixes concrete blocks,
    /// block *kinds* (several grid sizes or shapes behind one icon) and groups, and which of those
    /// the grid actually shows is not something to guess at.
    /// </summary>
    private static void QueueTile(TileModel tile)
    {
        var kind = tile.GetType().Name;
        var name = SafeName(tile);

        switch (tile)
        {
            case BlockTileModel block:
                Log.Debug($"  debug: menu right-click on {kind} '{name}'");
                QueueBlockTile(block);
                return;

            case BlockKindTileModel kindTile:
            {
                // One icon, several concrete blocks behind it — the grid sizes. Queue the first
                // unlocked one and NAME it, so the player sees which variant they got.
                var blocks = kindTile.Blocks;
                Log.Debug($"  debug: menu right-click on {kind} '{name}' with {blocks.Length} variant(s):");
                foreach (var variant in blocks)
                    Log.Debug($"      variant: '{SafeName(variant)}' unlocked={variant.Unlocked}");

                if (blocks.IsDefaultOrEmpty)
                {
                    Notify().Warning("Build Planner: that tile has no block behind it");
                    return;
                }

                QueueBlockTile(FirstUnlocked(blocks));
                return;
            }

            case BlockGroupTileModel groupTile:
            {
                // What the main grid is actually made of: the '+' tiles that open into kinds, each
                // kind holding its grid sizes. Verified in game — right-clicking 'Drill' in the grid
                // arrives here, and its kind holds 'Drill 3.5 m' and 'Drill 5.25 m'.
                var kinds = groupTile.BlockKinds;
                Log.Debug($"  debug: menu right-click on {kind} '{name}' with {kinds.Length} kind(s):");
                foreach (var member in kinds)
                    Log.Debug($"      kind: '{SafeName(member)}' variants={member.Blocks.Length}");

                if (kinds.IsDefaultOrEmpty)
                {
                    Notify().Warning("Build Planner: that tile has no block behind it");
                    return;
                }

                var chosen = FirstQueueable(kinds);
                if (chosen == null)
                {
                    Notify().Warning($"Build Planner: nothing unlocked under {name} yet");
                    return;
                }

                QueueBlockTile(chosen);
                return;
            }

            default:
                // Tools, consumables, voxel hands and group tiles all land here. Never silent: a
                // right-click that does nothing is indistinguishable from a broken binding.
                Log.Debug($"  debug: menu right-click on {kind} '{name}' — not a block");
                Notify().Warning("Build Planner: only blocks can be queued from the build menu");
                return;
        }
    }

    private static BlockTileModel FirstUnlocked(IReadOnlyList<BlockTileModel> blocks)
    {
        foreach (var block in blocks)
            if (block.Unlocked) return block;

        return blocks[0];
    }

    /// <summary>The first block under a group that the player can actually build, or null.</summary>
    private static BlockTileModel? FirstQueueable(IReadOnlyList<BlockKindTileModel> kinds)
    {
        foreach (var kind in kinds)
        {
            if (kind.Blocks.IsDefaultOrEmpty) continue;

            foreach (var block in kind.Blocks)
                if (block.Unlocked) return block;
        }

        return null;
    }

    private static void QueueBlockTile(BlockTileModel tile)
    {
        if (!tile.Unlocked)
        {
            Notify().Warning($"Build Planner: {SafeName(tile)} is not unlocked yet");
            return;
        }

        var definition = DefinitionOf(tile);
        if (definition == null)
        {
            Log.Write($"  WARNING: no CubeBlockDefinition behind menu tile '{SafeName(tile)}'");
            Notify().Warning("Build Planner: could not read that block's recipe");
            return;
        }

        // Report the TILE's name, not the definition's.
        //
        // Every grid size of a block shares one UIData.Name - both drills are "Drill" - so using the
        // definition's name made the message unable to say which variant had been queued, exactly
        // the ambiguity that naming it was supposed to remove. The tile carries
        // LocalizableBlockTypeDisplayName, which is per size: "Drill 3.5 m" / "Drill 5.25 m".
        //
        // built: null - nothing is placed yet, so the whole recipe is outstanding. That is the same
        // path a projection takes (BlockRequirements.Remaining).
        BuildPlannerBinding.Controller?.QueueBlock(definition, built: null, displayName: SafeName(tile));
    }

    /// <summary>
    /// The block definition behind a menu tile.
    ///
    /// <c>BlockTileModel.Block</c> is an <see cref="EntityCompositeDefinition"/> and is `internal`,
    /// so it is read by reflection; from there the engine's own accessor pulls out the component
    /// definition, exactly as BlockTileModel's constructor does.
    /// </summary>
    private static CubeBlockDefinition? DefinitionOf(BlockTileModel tile)
    {
        try
        {
            if (ReadMember(tile, "Block") is not EntityCompositeDefinition composite)
            {
                Log.Write("  WARNING: BlockTileModel.Block was not an EntityCompositeDefinition");
                return null;
            }

            return composite.TryGetDefinition<CubeBlockComponent, CubeBlockDefinition>();
        }
        catch (Exception ex)
        {
            Log.Error("reading the block definition from a menu tile failed", ex);
            return null;
        }
    }

    private static Notifier Notify() => BuildPlannerBinding.Notifier;

    private static string SafeName(TileModel tile)
    {
        try
        {
            return tile.Name.ToString() ?? "block";
        }
        catch
        {
            return "block";
        }
    }

    /// <summary>Read a public-or-internal property by name.</summary>
    private static object? ReadMember(object instance, string name)
    {
        var property = instance.GetType().GetProperty(
            name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return property?.GetValue(instance);
    }

    private static bool IsRightClick(PointerPressedEventArgs e)
    {
        try
        {
            return e.GetCurrentPoint(null).Properties.IsRightButtonPressed;
        }
        catch (Exception ex)
        {
            Log.Error("reading the pressed mouse button failed", ex);
            return false;
        }
    }

    private static object? DataContextOf(object? sender)
    {
        return (sender as IDataContextProvider)?.DataContext;
    }

    private static TileModel? TileOf(object? sender) => DataContextOf(sender) as TileModel;

    private static ulong _lastTimestamp;
    private static object? _lastSender;

    /// <summary>
    /// One press can reach the handler twice.
    ///
    /// TilesPanel attaches the handler to the tile control *and* adds it again to each prepared
    /// container (<c>OnContainerPrepared</c>), so a press that bubbles hits both. Vanilla does not
    /// notice — its handler only records a drag origin, which is idempotent — but queueing twice per
    /// click would be very noticeable. The pointer event's timestamp identifies the press.
    /// </summary>
    private static bool AlreadyHandled(PointerPressedEventArgs e, object? sender)
    {
        try
        {
            var timestamp = e.Timestamp;
            if (timestamp == _lastTimestamp && ReferenceEquals(sender, _lastSender))
            {
                Log.Debug("  debug: ignoring a duplicate press event for the same click");
                return true;
            }

            _lastTimestamp = timestamp;
            _lastSender = sender;
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("de-duplicating the press event failed", ex);
            return false;
        }
    }
}
