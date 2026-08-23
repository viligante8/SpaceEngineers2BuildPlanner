using System;
using System.Collections.Generic;
using System.Reflection;
using Keen.Game2.Client.UI.HUD.Blocks;
using Keen.Game2.Client.WorldObjects.Tools;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.Core.Game.Systems;

namespace BuildPlanner;

/// <summary>
/// The blocks the game itself says you are looking at.
///
/// **Why this exists.** Earlier attempts resolved the build target through
/// <c>BlockPlacerEntityComponent</c> (never found - it is not on the character, nor reachable by
/// enumerating the client session) and then through <c>IInteractedEntityProvider</c> (the press-F
/// interaction target, which is not the crosshair block and queued the wrong definition).
///
/// The reliable source is the one that already feeds the block tooltip - the panel that shows the
/// block name and "you need N x Steel Plate" while you aim at it.
/// <see cref="IntegrityToolUIComponent"/> drives that panel and holds a
/// <see cref="BlockIntegrityScreenModel"/> whose <c>Blocks</c> field is
/// <c>PooledList&lt;(CubeBlockComponent, CubeBlockDefinition)&gt;</c> - resolved engine data, not UI
/// text. If the tooltip is showing a block, this is that block, by construction.
///
/// It also covers the two cases the placer route was supposed to fix: the component holds a
/// <c>GridProjectionsSessionComponent</c> and an <c>AreaIntegrityToolEntityDetectorComponent</c>, so
/// projections and area-welder multi-block targets flow through the same list.
/// </summary>
internal static class IntegrityToolAccess
{
    /// <summary>
    /// The live component, captured from a detour on its own UpdateUI.
    ///
    /// Not resolved by lookup: the placer experience showed that guessing which entity owns a
    /// client-side tool component is unreliable. Hooking the method that runs when the tooltip
    /// updates hands us the exact instance the game is using.
    /// </summary>
    private static IntegrityToolUIComponent? _current;

    /// <summary>The captured component, for callers that need its services (see EngineQueueMirror).</summary>
    internal static IntegrityToolUIComponent? Captured => _current;

    /// <summary>
    /// Remember the component the game just updated the tooltip on.
    /// </summary>
    /// <param name="source">
    /// Which update path called us. A plain welder refreshes through <c>UpdateUI</c>, an area welder
    /// through <c>AreaDetectionChanged</c> — and only the first was hooked at first, which is why the
    /// area welder did nothing at all: without a capture there is no tool, so right-click was never
    /// claimed and not one line reached the log.
    /// </param>
    internal static void Capture(IntegrityToolUIComponent? component, string source = "UpdateUI")
    {
        if (component == null) return;

        if (!ReferenceEquals(_current, component))
            Log.Trace($"  trace: captured IntegrityToolUIComponent from {source}");

        _current = component;

        // Take right-click only while the welder is actually showing its panel. Outside that the
        // game needs the button for dropping items and removing projections.
        if (IsPanelOpen(component)) BuildPlannerBinding.EnableQueueInput();
        else BuildPlannerBinding.DisableQueueInput();
    }

    /// <summary>Forget the component when its scene goes away, so a stale one is never read.</summary>
    internal static void Release(IntegrityToolUIComponent? component)
    {
        if (component != null && ReferenceEquals(_current, component))
        {
            _current = null;
            BuildPlannerBinding.DisableQueueInput();
            Log.Trace("  trace: released IntegrityToolUIComponent (removed from scene)");
        }
    }

    /// <summary>
    /// The block definitions currently shown in the tooltip, newest detection first.
    ///
    /// Returns an empty list rather than null: callers report "nothing to queue" and must not have to
    /// distinguish "no tool" from "no target" to stay safe.
    /// </summary>
    internal static List<(CubeBlockComponent? Block, CubeBlockDefinition Definition)> GetTargetedBlocks()
    {
        var result = new List<(CubeBlockComponent?, CubeBlockDefinition)>();

        var component = _current;
        if (component == null)
        {
            Log.Write("  Build Planner: no build tool active (equip a welder and aim at a block)");
            return result;
        }

        // Only queue while the welder's block panel is actually open.
        //
        // Without this the capture is sticky: once the welder has set _current, switching to block
        // placement mode leaves it set, so right-click kept queueing there - and in placement mode
        // right-click already means something else to the game. _screen holds the open block-integrity
        // screen and is cleared by the tool's own CloseHUD, so it is the component's own statement
        // that it is no longer the thing the player is aiming with.
        if (!IsPanelOpen(component))
        {
            Log.Debug("  debug: build tool panel is not open (placement mode?); not queueing");
            return result;
        }

        try
        {
            // The panel's own block list first, because it is the whole selection.
            //
            // The interacted-entity provider carries exactly ONE entity, so preferring it (as this
            // did originally) silently reduced an area welder to the block under the crosshair: the
            // provider answered, we returned, and the other blocks in the area were never seen.
            // IntegrityToolUIComponent.UpdateAreaUI builds its model from every entry in
            // _areaDetector.AreaBlocks *and* AreaProjections, so the model is the selection, and for
            // a plain welder it simply holds one entry.
            //
            // It is also the more honest source generally: what the panel lists is exactly what the
            // player is being shown, which is what "queue what I am looking at" should mean.
            var model = GetModel(component);
            if (model == null) return FromProvider(component, result);

            var blocks = model.Blocks;
            if (blocks.Count == 0) return FromProvider(component, result);

            for (var i = 0; i < blocks.Count; i++)
            {
                var (block, definition) = blocks[i];
                if (definition == null) continue;

                // A projection has no built CubeBlockComponent yet, so block may be null - that case
                // is exactly what we want to queue and must not be filtered out. Only skip blocks
                // that are demonstrably already finished.
                if (block != null && block.EffectiveBuildProgress >= 1f)
                {
                    Log.Debug($"  debug: skipping '{definition.UIData?.Name}' - already complete");
                    continue;
                }

                result.Add((block, definition));
            }

            Log.Debug($"  debug: tooltip lists {blocks.Count} block(s), {result.Count} unfinished");
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("reading the block panel failed", ex);
            return result;
        }
    }

    /// <summary>
    /// Last resort when the panel model is missing: the tool's interacted-entity provider.
    ///
    /// It carries exactly one entity, so it can express neither an area selection nor a projection —
    /// which is precisely why it is no longer consulted first. It stays as a fallback because losing
    /// single-block queueing to a momentarily absent model would be worse than a partial answer, and
    /// it reports which path it took either way: a silent fallback is how the original ordering
    /// problem stayed invisible.
    /// </summary>
    private static List<(CubeBlockComponent?, CubeBlockDefinition)> FromProvider(
        IntegrityToolUIComponent component,
        List<(CubeBlockComponent?, CubeBlockDefinition)> result)
    {
        Log.Debug("  debug: no block panel model; falling back to the interacted-entity provider");

        var fromProvider = GetBlockFromProvider(component);
        if (fromProvider != null)
        {
            result.Add((fromProvider, fromProvider.Definition));
            return result;
        }

        Log.Write("  Build Planner: block panel is empty (not aiming at a block?)");
        return result;
    }

    /// <summary>
    /// Whether the tool's block-integrity panel is currently open.
    ///
    /// <c>_screen</c> is the live screen handle; the tool clears it in CloseHUD. If the field ever
    /// stops existing this returns true rather than false — failing open keeps queueing working (the
    /// behaviour the player relies on) instead of silently disabling the feature, and the warning
    /// says why.
    /// </summary>
    private static bool IsPanelOpen(IntegrityToolUIComponent component)
    {
        try
        {
            _screenField ??= typeof(IntegrityToolUIComponent).GetField(
                "_screen", BindingFlags.Instance | BindingFlags.NonPublic);

            if (_screenField == null)
            {
                Log.Write("  WARNING: IntegrityToolUIComponent._screen not found;" +
                          " cannot tell build mode from welder mode");
                return true;
            }

            return _screenField.GetValue(component) != null;
        }
        catch (Exception ex)
        {
            Log.Error("checking whether the build tool panel is open failed", ex);
            return true;
        }
    }

    private static FieldInfo? _screenField;

    /// <summary>
    /// The block the tool's interacted-entity provider is pointed at.
    ///
    /// Only the field holding the provider is private; once we have it, <c>InteractedEntity</c> and
    /// <c>CubeBlockComponent.Definition</c> are both public - this uses the components as intended
    /// rather than reading anything the UI derived.
    /// </summary>
    private static CubeBlockComponent? GetBlockFromProvider(IntegrityToolUIComponent component)
    {
        _providerField ??= typeof(IntegrityToolUIComponent).GetField(
            "_interactedEntityProvider", BindingFlags.Instance | BindingFlags.NonPublic);

        if (_providerField == null)
        {
            Log.Debug("  debug: IntegrityToolUIComponent._interactedEntityProvider not found");
            return null;
        }

        var provider = _providerField.GetValue(component) as InteractedEntityProviderComponent;
        if (provider == null)
        {
            Log.Debug("  debug: the build tool has no interacted-entity provider");
            return null;
        }

        var entity = provider.InteractedEntity;
        if (entity == null)
        {
            Log.Debug("  debug: build tool is not pointed at an entity");
            return null;
        }

        var block = entity.TryGet<CubeBlockComponent>();
        if (block == null)
        {
            Log.Debug($"  debug: '{entity.DebugName}' is not a cube block");
            return null;
        }

        if (block.EffectiveBuildProgress >= 1f)
        {
            Log.Debug($"  debug: '{entity.DebugName}' is already complete");
            return null;
        }

        return block;
    }

    private static FieldInfo? _providerField;

    /// <summary>
    /// <c>IntegrityToolUIComponent._model</c>. Private, so read by reflection; the field is cached
    /// because this runs on every right-click.
    /// </summary>
    private static BlockIntegrityScreenModel? GetModel(IntegrityToolUIComponent component)
    {
        _modelField ??= typeof(IntegrityToolUIComponent).GetField(
            "_model", BindingFlags.Instance | BindingFlags.NonPublic);

        if (_modelField == null)
        {
            Log.Write("  WARNING: IntegrityToolUIComponent._model not found; cannot read the block panel");
            return null;
        }

        return _modelField.GetValue(component) as BlockIntegrityScreenModel;
    }

    private static FieldInfo? _modelField;
}
