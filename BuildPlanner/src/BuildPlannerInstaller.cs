using System;
using System.Reflection;
using Keen.Game2.Client.Input;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Input;
using Keen.VRage.Input.EngineComponents;
using MonoMod.RuntimeDetour;

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
    }

    private delegate void OriginalSetMapping(ControlCustomizationEngineComponent self, ActionControlMapping mapping);

    private static void HookedSetMapping(
        OriginalSetMapping original,
        ControlCustomizationEngineComponent self,
        ActionControlMapping mapping)
    {
        try
        {
            mapping = BuildPlannerBinding.InjectActions(mapping);
            BuildPlannerBinding.EnsureContextActive();
        }
        catch (Exception ex)
        {
            Log.Error("injecting Build Planner actions into mapping failed", ex);
        }

        original(self, mapping);
    }

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
