using System;
using System.Reflection;
using Avalonia.Input;
using Keen.Game2.Client.Input;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Input;
using Keen.VRage.Input.EngineComponents;
using MonoMod.RuntimeDetour;

// The build-menu screen's type and its namespace are both called "GScreen", so the type needs an
// alias to be nameable here.
using GScreenView = Keen.Game2.Client.UI.TerminalScreen.GScreen.GScreen;

namespace BuildPlanner;

/// <summary>
/// Installs the Build Planner into a running game.
///
/// **Why a detour rather than a custom engine component.**
/// The obvious approach — <c>EngineBuilder.Add&lt;MyComponent&gt;()</c> from
/// <c>OnBeforeEngineInstantiated</c> — does not work for a plugin. <c>EngineBuilder.Add</c> calls
/// <c>RuntimeComponentInfo.For(type)</c>, which resolves through
/// <c>MetadataManager.GetActiveContext()</c>. That context is built once from the entry assembly
/// (see <c>MetadataManager.InitializeWithEntryAssembly</c>), and a dynamically loaded plugin
/// assembly is not in it — so the lookup returns null and Add throws NullReferenceException.
/// Verified in game: "failed to register engine component: System.NullReferenceException at
/// EngineBuilder.CreateIfNeeded".
///
/// So instead of introducing a new component type, we attach to one the engine already knows:
/// <c>InputGameComponent.Init()</c>. That component is a natural fit — it exists precisely to
/// register global input, and by the time its Init runs the input processor and definition manager
/// are live. The detour runs the original first, then adds our own InputContext beside its.
/// </summary>
internal sealed class BuildPlannerInstaller
{
    /// <summary>
    /// GUID of vanilla's "Tool" input context (Assets/MainMenuData/Input/Inputs/ToolContext.def).
    /// It declares ToolPrimary/Secondary/Tertiary, and ActionControlMapping.def binds Tertiary to
    /// Mouse::Middle — the button the SE1 Build Planner uses.
    /// </summary>
    internal static readonly Guid ToolContextGuid = new Guid("c9a80415-b6aa-4b05-8daf-8403c2a26ac0");

    /// <summary>ToolTertiaryAction — Mouse::Middle. Withdraw / deposit.</summary>
    internal static readonly Guid ToolTertiaryActionGuid = new Guid("ba689cc1-83f3-4473-aef6-882607dd3467");

    /// <summary>ToolSecondaryAction — Mouse::Right. Queue the block being looked at.</summary>
    internal static readonly Guid ToolSecondaryActionGuid = new Guid("f1789abd-986b-464a-9c2a-7ebd13c53c25");

    /// <summary>Vanilla "BuildingControls" action category, so our action appears in Options -> Controls.</summary>
    internal static readonly Guid BuildingCategoryGuid = new Guid("480bde0d-9a98-48fb-bffb-40cc0e156c30");

    private static Hook? _initHook;
    private static Hook? _setMappingHook;
    private static Hook? _integrityToolHook;
    private static Hook? _integrityToolCloseHook;
    private static Hook? _tilePressedHook;
    private static Hook? _sizeTilePressedHook;
    private static Hook? _subTilePressedHook;

    internal void Install(PluginHost host)
    {
        var init = typeof(InputGameComponent).GetMethod(
            "Init", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (init == null)
        {
            Log.Write("  ERROR: InputGameComponent.Init not found; input not bound");
            return;
        }

        _initHook = new Hook(init, HookedInit);
        Log.Write("  hook installed on InputGameComponent.Init");

        // ControlCustomizationEngineComponent owns the mapping: it keeps _baseMappings and rebuilds
        // the processor's mapping from it whenever custom binds change. Adding our action straight
        // to the processor was therefore undone the next time it rebuilt — observed in the game log
        // as "228 Mapping added" (ours) immediately followed by "227 Mapping added" (the reset),
        // which is why the N key did nothing and no entry appeared in Options -> Controls.
        //
        // Hooking its SetMapping lets us inject our action into the mapping it is about to publish,
        // so the binding survives every rebuild and ControlsViewModel sees it.
        var setMapping = typeof(ControlCustomizationEngineComponent).GetMethod(
            "SetMapping", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (setMapping == null)
        {
            Log.Write("  WARNING: ControlCustomization.SetMapping not found; key binding may be reset");
            return;
        }

        _setMappingHook = new Hook(setMapping, HookedSetMapping);
        Log.Write("  hook installed on ControlCustomizationEngineComponent.SetMapping");

        InstallIntegrityToolHook();
        InstallBuildMenuHooks();
    }

    /// <summary>
    /// Queueing from the build menu (G).
    ///
    /// These are Avalonia pointer handlers on the menu's own tiles, not input actions. That is
    /// deliberate — while the menu is open the input system has already given right-click to
    /// vanilla's CursorButton2, and taking it back would break right-click-to-clear on the toolbar.
    /// See BlockMenuAccess for the trace that showed this.
    /// </summary>
    private void InstallBuildMenuHooks()
    {
        _tilePressedHook = HookGScreen("TilePressed", HookedTilePressed);
        _sizeTilePressedHook = HookGScreen("SizeTilePressed", HookedSizeTilePressed);
        _subTilePressedHook = HookGScreen("SubTilePressed", HookedSubTilePressed);
    }

    private static Hook? HookGScreen(string methodName, Delegate replacement)
    {
        var method = typeof(GScreenView).GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method == null)
        {
            Log.Write($"  WARNING: GScreen.{methodName} not found; that build-menu path will not queue");
            return null;
        }

        var hook = new Hook(method, replacement);
        Log.Write($"  hook installed on GScreen.{methodName}");
        return hook;
    }

    private delegate void OriginalTilePressed(
        GScreenView self, object? sender, PointerPressedEventArgs e);

    private static void HookedTilePressed(
        OriginalTilePressed original, GScreenView self, object? sender, PointerPressedEventArgs e)
    {
        // Original first: it records the drag origin, and a right-click must not change how dragging
        // behaves.
        original(self, sender, e);

        try
        {
            BlockMenuAccess.OnTilePressed(sender, e);
        }
        catch (Exception ex)
        {
            Log.Error("queueing from a build-menu tile failed", ex);
        }
    }

    private static void HookedSizeTilePressed(
        OriginalTilePressed original, GScreenView self, object? sender, PointerPressedEventArgs e)
    {
        original(self, sender, e);

        try
        {
            BlockMenuAccess.OnSizeTilePressed(sender, e);
        }
        catch (Exception ex)
        {
            Log.Error("queueing from a build-menu size tile failed", ex);
        }
    }

    private static void HookedSubTilePressed(
        OriginalTilePressed original, GScreenView self, object? sender, PointerPressedEventArgs e)
    {
        original(self, sender, e);

        try
        {
            BlockMenuAccess.OnSubTilePressed(sender, e);
        }
        catch (Exception ex)
        {
            Log.Error("queueing from a build-menu sub-tile failed", ex);
        }
    }

    /// <summary>
    /// Capture the component that feeds the block tooltip.
    ///
    /// This is how the Build Planner learns what block the player is aiming at. Two earlier routes
    /// failed in game: BlockPlacerEntityComponent could not be located at all, and the interacted-
    /// entity provider returned the press-F target rather than the crosshair block. UpdateUI runs on
    /// the component that populates the "you need N x Steel Plate" panel, so hooking it hands us the
    /// exact instance the game is already using - no lookup, no guessing where it lives.
    /// </summary>
    private void InstallIntegrityToolHook()
    {
        var updateUI = typeof(Keen.Game2.Client.WorldObjects.Tools.IntegrityToolUIComponent).GetMethod(
            "UpdateUI", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (updateUI == null)
        {
            Log.Write("  ERROR: IntegrityToolUIComponent.UpdateUI not found; queueing will not work");
            return;
        }

        _integrityToolHook = new Hook(updateUI, HookedUpdateUI);
        Log.Write("  hook installed on IntegrityToolUIComponent.UpdateUI");

        // CloseHUD is the tool saying "I am no longer showing a block". Without this the captured
        // component stays live after the player switches to block placement mode, where right-click
        // already means something else - queueing there was the reported bug.
        var closeHud = typeof(Keen.Game2.Client.WorldObjects.Tools.IntegrityToolUIComponent).GetMethod(
            "CloseHUD", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (closeHud == null)
        {
            Log.Write("  WARNING: IntegrityToolUIComponent.CloseHUD not found;" +
                      " queueing may fire outside welder mode");
            return;
        }

        _integrityToolCloseHook = new Hook(closeHud, HookedCloseHUD);
        Log.Write("  hook installed on IntegrityToolUIComponent.CloseHUD");
    }

    private delegate void OriginalCloseHUD(
        Keen.Game2.Client.WorldObjects.Tools.IntegrityToolUIComponent self);

    private static void HookedCloseHUD(
        OriginalCloseHUD original,
        Keen.Game2.Client.WorldObjects.Tools.IntegrityToolUIComponent self)
    {
        original(self);

        try
        {
            IntegrityToolAccess.Release(self);
        }
        catch (Exception ex)
        {
            Log.Error("releasing the integrity tool component failed", ex);
        }
    }

    private delegate void OriginalUpdateUI(
        Keen.Game2.Client.WorldObjects.Tools.IntegrityToolUIComponent self,
        Keen.VRage.DCS.Components.Entity target);

    private static void HookedUpdateUI(
        OriginalUpdateUI original,
        Keen.Game2.Client.WorldObjects.Tools.IntegrityToolUIComponent self,
        Keen.VRage.DCS.Components.Entity target)
    {
        // Original first: it is what populates the model we are about to rely on.
        original(self, target);

        try
        {
            IntegrityToolAccess.Capture(self);
        }
        catch (Exception ex)
        {
            Log.Error("capturing the integrity tool component failed", ex);
        }
    }

    private delegate void OriginalSetMapping(ControlCustomizationEngineComponent self, ActionControlMapping mapping);

    private static void HookedSetMapping(
        OriginalSetMapping original,
        ControlCustomizationEngineComponent self,
        ActionControlMapping mapping)
    {
        try
        {
            BuildPlannerBinding.NoteCustomizationComponent(self);
            mapping = BuildPlannerBinding.InjectActions(mapping);
            BuildPlannerBinding.EnsureContextActive();
        }
        catch (Exception ex)
        {
            Log.Error("injecting Build Planner actions into mapping failed", ex);
        }

        original(self, mapping);

        // After, never before: the purge removes entries from an ObservableList, and the resulting
        // CollectionChanged makes the component rebuild its mapping from _baseMappings - which is
        // still null until the original SetMapping has run.
        if (!_purgedOrphans)
        {
            _purgedOrphans = true;
            BuildPlannerBinding.PurgeOrphanedCustomizations(self);
        }
    }

    /// <summary>Orphaned customisations are cleaned once per run; see PurgeOrphanedCustomizations.</summary>
    private static bool _purgedOrphans;

    private delegate void OriginalInit(InputGameComponent self);

    private static void HookedInit(OriginalInit original, InputGameComponent self)
    {
        original(self);

        try
        {
            BuildPlannerBinding.Attach(self);
        }
        catch (Exception ex)
        {
            Log.Error("attaching Build Planner failed", ex);
        }
    }
}
