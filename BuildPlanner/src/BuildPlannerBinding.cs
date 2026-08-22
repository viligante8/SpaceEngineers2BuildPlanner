using System;
using System.Reflection;
using Keen.Game2.Client.Input;
using Keen.Game2.Client.UI.HUD.Notification;
using Keen.Game2.Client.UI.InGame;
using Keen.Game2.Game.SessionComponents;
using Keen.Game2.Simulation.RuntimeSystems.CoreScenes;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.Core.Input;
using Keen.VRage.Input;
using Keen.VRage.Input.Definitions;
using Keen.VRage.Input.EngineComponents;
using Keen.VRage.Library.Localization;
using Keen.VRage.Input.Extensions;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Library.Utils;

namespace BuildPlanner;

/// <summary>
/// Binds Build Planner actions onto vanilla's input system, from inside InputGameComponent.Init.
///
/// Everything needed is already present on that component â€” the input processor and the services it
/// was constructed with â€” so this reads them off the instance by reflection rather than trying to
/// inject them into a component type the engine's metadata does not know about
/// (see BuildPlannerInstaller for why that route fails).
/// </summary>
internal static class BuildPlannerBinding
{
    private static InputContext? _context;
    private static BuildPlannerController? _controller;
    private static IInputProcessor? _processor;

    /// <summary>
    /// Re-activate our context if something deactivated it.
    ///
    /// GameInputProcessorComponent.ActivateContext replaces the occupant of a named layer, and
    /// contexts are deactivated as the player moves between modes (building, terminal, cockpit).
    /// Our context is layer-less, but it can still be dropped from _activeContexts, after which our
    /// key silently stops working — observed in game as N responding in one state and doing nothing
    /// in another. Checking on a cheap timer keeps the binding alive without hooking more methods.
    /// </summary>
    internal static void EnsureContextActive()
    {
        try
        {
            if (_context == null) return;
            if (_context.IsActive) return;

            Log.Debug("  debug: context was inactive; reactivating");

            // InputContext.Activate() resolves the processor itself through the engine singleton,
            // so it works regardless of which processor instance we captured at bind time.
            _context.Activate();
        }
        catch (Exception ex)
        {
            Log.Error("reactivating context failed", ex);
        }
    }

    /// <summary>
    /// Turn on the engine's own input tracing.
    ///
    /// ActionProcessorDebugObject.DetailedInputLog makes GameInputProcessorComponent log every
    /// control it discards and why ("Discard candidate control …, input already consumed",
    /// "Allowing aliased control …"), written to the game log. That answers directly which context
    /// is claiming a key, instead of inferring it from our own silence.
    /// </summary>
    internal static void EnableEngineInputLogging(ActionInputProcessorBaseComponent processor)
    {
        try
        {
            // DebugObject is redeclared on GameInputProcessorComponent (covariant return), so a
            // plain GetProperty matches both the base and derived declarations and throws
            // AmbiguousMatchException. Walk the hierarchy and take the most-derived one.
            object? debugObject = null;
            for (var type = processor.GetType(); type != null && debugObject == null; type = type.BaseType)
            {
                var property = type.GetProperty(
                    "DebugObject",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);

                if (property != null) debugObject = property.GetValue(processor);
            }

            if (debugObject == null)
            {
                Log.Write("  WARNING: could not reach input DebugObject; engine input tracing off");
                return;
            }

            FieldInfo? field = null;
            for (var type = debugObject.GetType(); type != null && field == null; type = type.BaseType)
            {
                field = type.GetField(
                    "DetailedInputLog",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            }

            if (field == null)
            {
                Log.Write("  WARNING: DetailedInputLog field not found; engine input tracing off");
                return;
            }

            field.SetValue(debugObject, true);
            Log.Write("  engine input tracing enabled (see the game log for discarded controls)");
        }
        catch (Exception ex)
        {
            Log.Error("enabling engine input tracing failed", ex);
        }
    }

    /// <summary>Diagnostic: report whether our context is still active and holding its handler.</summary>
    internal static void LogContextState(string when)
    {
        try
        {
            Log.Debug($"  debug: {when} contextActive={_context?.IsActive} layerIndex={_context?.LayerIndex}");
        }
        catch
        {
            // diagnostics only
        }
    }

    internal static void Attach(InputGameComponent inputComponent)
    {
        if (_context != null)
        {
            Log.Write("  already attached; skipping");
            return;
        }

        var processor = GetPrivateField<IInputProcessor>(inputComponent, "_inputProcessor");
        if (processor == null)
        {
            Log.Write("  ERROR: could not read _inputProcessor; input not bound");
            return;
        }

        // Mapping/SetMapping live on the concrete component, not on IInputProcessor.
        var processorComponent = processor as ActionInputProcessorBaseComponent;
        if (processorComponent == null)
        {
            Log.Write("  ERROR: input processor is not an ActionInputProcessorBaseComponent; input not bound");
            return;
        }

        var definitions = Singleton<DefinitionManager>.Instance;
        if (definitions == null)
        {
            Log.Write("  ERROR: DefinitionManager unavailable; input not bound");
            return;
        }

        if (!definitions.TryGetDefinition<InputActionDefinition>(
                BuildPlannerInstaller.ToolTertiaryActionGuid, out var tertiary))
        {
            Log.Write("  ERROR: ToolTertiaryAction not found; input not bound");
            return;
        }

        definitions.TryGetDefinition<InputActionDefinition>(
            BuildPlannerInstaller.ToolSecondaryActionGuid, out var secondary);

        // Withdraw/deposit gets its own key rather than sharing Mouse::Middle.
        //
        // DisambiguatingControlActivationFilter.FilterOnControl consumes an input once per frame
        // ("Discard candidate control â€¦, input already consumed"), and ProcessActionsPerContext
        // assigns each control to exactly ONE context. Contexts compete for an input rather than
        // observing it in parallel. Mouse::Middle is already claimed in vanilla data by three
        // actions (ToolTertiary, PaintBlock, ToggleGridFollowing); whichever context is active wins
        // and everyone else is cancelled â€” so our handler never ran while a tool was equipped.
        // Verified in game: right-click queueing logged fine while middle-click produced nothing.
        //
        // N is unbound in ActionControlMapping.def (checked against every InputId in that file).
        var withdrawAction = GetOrCreateWithdrawAction(definitions);

        // Force a republish so the action lands in the mapping even if SetMapping already ran
        // before this component initialised.
        processorComponent.SetMapping(InjectActions(processorComponent.Mapping));

        // Build our own layer-less context rather than reusing vanilla's ToolContext.
        //
        // GameInputProcessorComponent.ActivateContext keeps exactly ONE context per named layer: it
        // deactivates whatever currently occupies the layer and takes its place. ToolContext sits on
        // the "BlockPlacer" layer, so activating a second context built from that definition made us
        // and the game evict each other â€” which is why the first in-game test produced no callbacks
        // at all despite binding cleanly.
        //
        // With Layer == null, ActivateContext takes its else branch and appends to _activeContexts,
        // and DispatchActions walks every active context. So we observe the same actions alongside
        // the game instead of fighting it for a slot.
        // TWO contexts, deliberately.
        //
        // Right-click is shared with the game: dropping an item in the inventory screen and removing
        // a projection are both Mouse::Right. An input is consumed by exactly ONE context per frame
        // (DisambiguatingControlActivationFilter), so an always-active context holding right-click
        // steals it from the game everywhere - observed as items refusing to drop and projections
        // refusing to be removed.
        //
        // So the withdraw key lives in a permanently active context, while right-click lives in its
        // own context that is only active while a welder's block panel is showing. The rest of the
        // time the game owns right-click completely, because our context is not merely ignoring the
        // input - it is not there to compete for it.
        var contextDefinition = new InputContextDefinition(new[] { withdrawAction });

        var hostEntity = inputComponent.Entity;
        _hostEntity = hostEntity;

        _controller = new BuildPlannerController(
            new Notifier(ShowNotification),
            () => GetSession(hostEntity),
            () => GetClientSession(hostEntity));

        _context = new InputContext(contextDefinition);
        _processor = processor;
        processor.ActivateContext(_context);

        EnableEngineInputLogging(processorComponent);

        _context.SetTrigger(withdrawAction, () => _controller!.OnTertiaryAction());
        Log.Write("  bound withdraw/deposit to BuildPlannerWithdraw (Keyboard::N)");

        if (secondary != null)
        {
            // Created but NOT activated: IntegrityToolAccess turns it on when a welder starts showing
            // its block panel and off again when it stops.
            _queueContext = new InputContext(new InputContextDefinition(new[] { secondary }));
            _queueContext.SetTrigger(secondary, () => _controller!.OnSecondaryAction());
            Log.Write("  bound queue to ToolSecondaryAction (Mouse::Right), active only with a welder out");
        }
        else
        {
            Log.Write("  WARNING: ToolSecondaryAction not found; queueing not bound");
        }
    }

    /// <summary>Right-click context. Only active while a welder is showing its block panel.</summary>
    private static InputContext? _queueContext;

    /// <summary>
    /// Claim right-click, because a welder is out and aiming at something.
    ///
    /// Idempotent: called from the tool's UpdateUI, which runs constantly while aiming.
    /// </summary>
    internal static void EnableQueueInput()
    {
        try
        {
            if (_queueContext == null || _queueContext.IsActive) return;

            _queueContext.Activate();
            Log.Debug("  debug: right-click claimed (welder active)");
        }
        catch (Exception ex)
        {
            Log.Error("activating the queue input context failed", ex);
        }
    }

    /// <summary>
    /// Release right-click back to the game.
    ///
    /// Without this the mod holds Mouse::Right everywhere - the inventory screen cannot drop items
    /// and projections cannot be removed, because our context consumed the input first.
    /// </summary>
    internal static void DisableQueueInput()
    {
        try
        {
            if (_queueContext == null || !_queueContext.IsActive) return;

            _queueContext.Deactivate();
            Log.Debug("  debug: right-click released back to the game");
        }
        catch (Exception ex)
        {
            Log.Error("deactivating the queue input context failed", ex);
        }
    }

    /// <summary>
    /// The active world session, needed to locate the local player.
    ///
    /// InputGameComponent lives on the *engine* entity, whose scene is the app-level GameCoreScene â€”
    /// not a world session. Entity.GetSession() therefore threw
    /// "Unable to cast GameCoreScene to Session" in game.
    ///
    /// The route below is the one McpServerComponent uses to reach a session from outside any
    /// session: the scene's UserObject is the GameCoreScene, and its GameClient owns the session.
    /// Resolved per call because it is null at the main menu and changes with each world load.
    /// </summary>
    /// <summary>
    /// The CLIENT session. Client-only systems live here - the block placer
    /// (Game2.Client.GameSystems.BlockPlacement) and the in-game UI - while the server session owns
    /// simulation state such as inventories. Both run in-process in single player.
    /// </summary>
    internal static Session? GetClientSession(Keen.VRage.DCS.Components.Entity hostEntity)
    {
        if (hostEntity == null) return null;

        try
        {
            var scene = hostEntity.Scene?.UserObject as GameCoreScene;
            return scene?.GameClient?.Get<WorldSessionComponent>()?.OwnedSession;
        }
        catch (Exception ex)
        {
            Log.Error("client session lookup failed", ex);
            return null;
        }
    }

    private static Session? GetSession(Keen.VRage.DCS.Components.Entity hostEntity)
    {
        if (hostEntity == null) return null;

        try
        {
            var scene = hostEntity.Scene?.UserObject as GameCoreScene;
            if (scene == null) return null;

            // GameCoreScene exposes BOTH halves. In single player they run in-process, and the
            // client session's character (CompositeCharacterServer) carries no InventoryComponent -
            // verified exhaustively in game. Prefer whichever session actually has one.
            var clientSession = scene.GameClient?.Get<WorldSessionComponent>()?.OwnedSession;
            var serverSession = scene.GameServer?.Get<WorldSessionComponent>()?.OwnedSession;

            return serverSession ?? clientSession;
        }
        catch (Exception ex)
        {
            Log.Error("session lookup failed", ex);
            return null;
        }
    }

    /// <summary>The Build Planner's withdraw action, created once and reused for every mapping.</summary>
    private static InputActionDefinition? _withdrawAction;

    /// <summary>Default key: N is unbound in vanilla's ActionControlMapping.def.</summary>
    private const string WithdrawActionName = "BuildPlannerWithdraw";

    internal static InputActionDefinition GetOrCreateWithdrawAction(DefinitionManager? definitions = null)
    {
        if (_withdrawAction != null)
        {
            // The category may not have been resolvable when the action was first created (the
            // controls menu is populated during startup, before definitions are all loaded), so
            // fill it in as soon as a DefinitionManager is available.
            if (!_categoryAssigned && definitions != null) TryAssignCategory(_withdrawAction, definitions);
            return _withdrawAction;
        }

        var action = new InputActionDefinition(LocKey.FromString(WithdrawActionName), InputType.Digital);

        // Name drives sorting and lookup in the controls UI.
        TrySetPrivate(action, "<Name>k__BackingField", StringId.Get(WithdrawActionName));

        definitions ??= Singleton<DefinitionManager>.Instance;
        if (definitions != null) TryAssignCategory(action, definitions);

        _withdrawAction = action;
        return action;
    }

    private static bool _categoryAssigned;

    /// <summary>
    /// ControlCustomizationViewModel drops any action whose Category is null or the hidden category,
    /// and orders groups by OrderedControlCategories — so the action needs vanilla's
    /// "BuildingControls" category (index 8 in that list) to appear in Options -> Controls.
    /// </summary>
    private static void TryAssignCategory(InputActionDefinition action, DefinitionManager definitions)
    {
        try
        {
            if (definitions.TryGetDefinition<ActionCategoryDefinition>(
                    BuildPlannerInstaller.BuildingCategoryGuid, out var category))
            {
                TrySetPrivate(action, "<Category>k__BackingField", category);
                _categoryAssigned = true;
                Log.Write("  action category set to BuildingControls");
            }
        }
        catch (Exception ex)
        {
            Log.Error("assigning action category failed", ex);
        }
    }

    /// <summary>
    /// Add the Build Planner's actions to a mapping that is about to be published.
    ///
    /// Called from the ControlCustomizationEngineComponent.SetMapping hook so the binding survives
    /// every rebuild — the component reconstructs the processor mapping from its own _baseMappings
    /// whenever custom binds change, which silently discarded a direct addition to the processor.
    /// </summary>
    internal static ActionControlMapping InjectActions(ActionControlMapping mapping)
    {
        // Create on demand: ControlCustomizationEngineComponent.SetMapping runs during startup,
        // before InputGameComponent.Init, and the controls menu is populated from *that* mapping.
        // Waiting for Attach() meant the menu was built without our action and never rebuilt.
        var action = GetOrCreateWithdrawAction();

        var builder = mapping.ToBuilder();

        // Respect an existing binding: if the player has rebound this action in the controls menu,
        // leave their choice alone.
        if (builder.ContainsAction(action)) return mapping;

        builder.AddControl(action, new DigitalControl(new DigitalInput(KeyboardInputs.N)));
        return builder.MoveToMapping();
    }

    /// <summary>
    /// Set an auto-property's backing field. InputActionDefinition's properties are all
    /// `private set` because definitions are normally built by the content pipeline from .def files;
    /// a plugin has no such file, so the instance is completed here.
    /// </summary>
    private static void TrySetPrivate(object instance, string backingFieldName, object value)
    {
        try
        {
            var field = instance.GetType().GetField(
                backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                Log.Write($"  WARNING: field {backingFieldName} not found on {instance.GetType().Name}");
                return;
            }

            field.SetValue(instance, value);
        }
        catch (Exception ex)
        {
            Log.Error($"setting {backingFieldName} failed", ex);
        }
    }

    /// <summary>
    /// The host entity, kept so the notification sink can reach the client session on demand.
    /// Resolved per call rather than cached: InGameUI is null at the main menu and is replaced on
    /// every world load.
    /// </summary>
    private static Keen.VRage.DCS.Components.Entity? _hostEntity;

    /// <summary>
    /// The client session's InGameUI, or null when there is no world loaded.
    ///
    /// There is no public accessor for it. The route used here is the one vanilla's own notification
    /// code uses from the inside: InventoryNotificationsSessionComponent is a SessionComponent on the
    /// CLIENT session and holds the UI in a private readonly field <c>_ui</c> of type InGameUI
    /// (confirmed by reading Game2.Client.dll metadata, not by assumption). Borrowing that reference
    /// gives exactly the handle DisplayItem calls ShowNotification on.
    ///
    /// The component lives on the client session specifically — InGameUI is Game2.Client, and the
    /// server session has no UI at all. See notes/client-server-split.md.
    /// </summary>
    private static InGameUI? ResolveInGameUI()
    {
        try
        {
            if (_hostEntity == null)
            {
                Log.Write("  notify: no host entity; cannot reach the HUD");
                return null;
            }

            var clientSession = GetClientSession(_hostEntity);
            if (clientSession == null)
            {
                Log.Write("  notify: no client session (main menu?); HUD unavailable");
                return null;
            }

            // TryGet, never Get: asking a session for a component it does not own throws.
            var notifications = clientSession.SessionComponents
                ?.TryGet<Keen.Game2.Client.WorldObjects.Items.InventoryNotificationsSessionComponent>();
            if (notifications == null)
            {
                Log.Write("  notify: InventoryNotificationsSessionComponent not on the client session");
                return null;
            }

            var ui = GetPrivateField<InGameUI>(notifications, "_ui");
            if (ui == null)
            {
                Log.Write("  notify: InventoryNotificationsSessionComponent._ui was null");
                return null;
            }

            return ui;
        }
        catch (Exception ex)
        {
            Log.Error("resolving InGameUI failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Push a notification to the HUD, and always mirror it to the log.
    ///
    /// The log line is kept even once the HUD works: it is the only record available after the fact,
    /// and it is how every branch of the withdrawal stays observable (CLAUDE.md, "A silent code path
    /// is a broken code path"). If the HUD cannot be reached the message still reaches the log, so
    /// the feature degrades rather than disappearing.
    /// </summary>
    private static void ShowNotification(HudNotification notification)
    {
        var text = notification.Content?.ToString() ?? notification.Name.ToString();
        Log.Write($"  notify: {text}");

        var ui = ResolveInGameUI();
        if (ui == null) return; // ResolveInGameUI has already reported why.

        try
        {
            ui.ShowNotification(notification);
        }
        catch (Exception ex)
        {
            Log.Error("ShowNotification failed", ex);
        }
    }

    private static T? GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        try
        {
            var field = instance.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(instance) as T;
        }
        catch (Exception ex)
        {
            Log.Error($"reading {fieldName} failed", ex);
            return null;
        }
    }
}
