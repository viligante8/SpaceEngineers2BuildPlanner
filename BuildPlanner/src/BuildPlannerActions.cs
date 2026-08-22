using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using Keen.VRage.Core.Input;
using Keen.VRage.Input;
using Keen.VRage.Input.Definitions;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Library.Localization;
using Keen.VRage.Library.Utils;

namespace BuildPlanner;

/// <summary>
/// One Build Planner input action: its identity, its default binding, and what it does.
/// </summary>
/// <remarks>
/// Every action is a real <see cref="InputActionDefinition"/> with its own GUID, so the controls
/// menu can rebind each one independently. See <see cref="BuildPlannerActions"/> for why the GUID
/// is the load-bearing part.
/// </remarks>
internal sealed class BuildPlannerAction
{
    internal BuildPlannerAction(
        string guid,
        string name,
        string displayName,
        PlannerAction performs,
        InputId mainInput,
        params InputId[] modifiers)
    {
        Guid = new Guid(guid);
        Name = name;
        DisplayName = displayName;
        Performs = performs;
        MainInput = mainInput;
        Modifiers = modifiers;
    }

    /// <summary>Stable identity. The player's rebinding is stored against this, so it must never change.</summary>
    internal Guid Guid { get; }

    /// <summary>Sorting/lookup key inside the controls menu.</summary>
    internal string Name { get; }

    /// <summary>What the controls menu shows.</summary>
    internal string DisplayName { get; }

    /// <summary>The operation this action performs.</summary>
    internal PlannerAction Performs { get; }

    /// <summary>Default main input (the key or button, without modifiers).</summary>
    internal InputId MainInput { get; }

    /// <summary>Default modifiers held with <see cref="MainInput"/>.</summary>
    internal InputId[] Modifiers { get; }

    /// <summary>The engine definition, created once by <see cref="BuildPlannerActions.EnsureCreated"/>.</summary>
    internal InputActionDefinition? Definition { get; set; }
}

/// <summary>
/// The Build Planner's input actions, and their registration with the engine.
///
/// **Why every action needs a real GUID, registered with the DefinitionManager.**
/// Rebinding in Options -&gt; Controls did nothing for several builds: the key stayed on N no matter
/// what the player chose. The cause is that a customised binding is persisted *by the action's
/// GUID*, not by the action object:
///
/// <code>
/// // CustomizedControlsOptionsPart.ActionControlEntry
/// private Guid _actionGuid;
/// public InputActionDefinition Action {
///     get {
///         if (Singleton&lt;DefinitionManager&gt;.Instance.TryGetDefinition(_actionGuid, out InputActionDefinition d))
///             return d;
///         Assert.Fail(...);
///         return _placeholder;      // <-- a definition nothing is bound to
///     }
///     private set =&gt; _actionGuid = value.Guid;
/// }
/// </code>
///
/// The old action was built with <c>new InputActionDefinition(...)</c>, which leaves
/// <see cref="Definition.Guid"/> as <see cref="Guid.Empty"/> and never enters the DefinitionManager.
/// So the rebinding was written to disk as
/// <c>"Action": "00000000-0000-0000-0000-000000000000"</c> (seen verbatim in
/// <c>%APPDATA%\SpaceEngineers2\AppData\EngineOptions\CustomizedControlsOptionsPart</c>), the
/// lookup on the way back in failed, and
/// <c>ControlCustomizationEngineComponent.UpdateMappings</c> skipped the entry entirely:
///
/// <code>
/// if (builder.RemoveAction(action))   // false for the placeholder -> customisation dropped
/// {
///     builder.AddControl(action, primary);
///     ...
/// }
/// </code>
///
/// The fix is therefore not a workaround but the missing half of the setup: build each action from
/// an <see cref="InputActionDefinitionObjectBuilder"/> carrying our own GUID, and put it in the
/// definition set that already owns vanilla's input actions so the engine can resolve it.
/// </summary>
internal static class BuildPlannerActions
{
    /// <summary>
    /// Every Build Planner action, with the chord it ships with.
    ///
    /// The defaults are the SE1 Build Planner scheme (notes/build-planner-ux-spec.md), now expressed
    /// as real bindings rather than as modifier keys sampled at press time. That is what makes them
    /// rebindable: the controls menu can only offer what the mapping knows about.
    ///
    /// Chords work because <c>DisambiguatingControlActivationFilter.StartFrame</c> drops a candidate
    /// control when any of its inputs also belongs to a control with MORE inputs - so SHIFT+N wins
    /// over N when SHIFT is held. Vanilla relies on the same rule for F5 / SHIFT+F5.
    /// </summary>
    internal static readonly BuildPlannerAction[] All =
    {
        new BuildPlannerAction(
            "5dc55fa1-07c8-4a1a-8fb4-283994f74196", "BuildPlannerQueue",
            "Build Planner: Queue Block", PlannerAction.Queue,
            MouseInputs.Right),

        new BuildPlannerAction(
            "98756ec9-c8b8-4cb6-913a-23c651797d8b", "BuildPlannerWithdraw",
            "Build Planner: Withdraw", PlannerAction.Withdraw,
            KeyboardInputs.N),

        new BuildPlannerAction(
            "a55c30c2-0a5f-4463-8aef-83c340707087", "BuildPlannerWithdrawKeep",
            "Build Planner: Withdraw, Keep Queue", PlannerAction.WithdrawKeepQueue,
            KeyboardInputs.N, KeyboardInputs.Control),

        new BuildPlannerAction(
            "320e920a-4741-4e6f-8ed5-4930b35f8871", "BuildPlannerWithdrawTen",
            "Build Planner: Withdraw x10, Keep Queue", PlannerAction.WithdrawTenKeepQueue,
            KeyboardInputs.N, KeyboardInputs.Control, KeyboardInputs.Alt),

        new BuildPlannerAction(
            "2329bad4-4843-45d8-bb28-7e070d22d831", "BuildPlannerDeposit",
            "Build Planner: Deposit Inventory", PlannerAction.Deposit,
            KeyboardInputs.N, KeyboardInputs.Alt),

        new BuildPlannerAction(
            "d594175f-6582-452b-82ed-8de61243cdb7", "BuildPlannerProduce",
            "Build Planner: Produce", PlannerAction.Produce,
            KeyboardInputs.N, KeyboardInputs.Shift),

        new BuildPlannerAction(
            "bb63eb10-06e3-438c-927b-c0f87b8a44f1", "BuildPlannerProduceTen",
            "Build Planner: Produce x10", PlannerAction.ProduceTen,
            KeyboardInputs.N, KeyboardInputs.Shift, KeyboardInputs.Control),

        new BuildPlannerAction(
            "a9d8df0c-cd32-4f23-bf98-136691f07dd6", "BuildPlannerClearQueue",
            "Build Planner: Clear Queue", PlannerAction.ClearQueue,
            KeyboardInputs.N, KeyboardInputs.Shift, KeyboardInputs.Alt),

        new BuildPlannerAction(
            "8a7997c8-9cfb-466c-bc4d-053cbc8f7038", "BuildPlannerDiagnose",
            "Build Planner: Dump State To Log", PlannerAction.Diagnose,
            KeyboardInputs.N, KeyboardInputs.Shift, KeyboardInputs.Alt, KeyboardInputs.Control),
    };

    /// <summary>The action the queue key drives. Bound in its own context (welder only).</summary>
    internal static BuildPlannerAction Queue => Find(PlannerAction.Queue);

    /// <summary>Everything except the queue key: one always-active context holds these.</summary>
    internal static IEnumerable<BuildPlannerAction> Planner
    {
        get
        {
            foreach (var action in All)
                if (action.Performs != PlannerAction.Queue)
                    yield return action;
        }
    }

    internal static BuildPlannerAction Find(PlannerAction performs)
    {
        foreach (var action in All)
            if (action.Performs == performs)
                return action;

        throw new InvalidOperationException($"no Build Planner action performs {performs}");
    }

    private static bool _created;

    /// <summary>
    /// Create the action definitions, once, and make sure the engine can resolve them by GUID.
    ///
    /// Called from the ControlCustomizationEngineComponent.SetMapping hook rather than from
    /// Attach(): that hook runs during startup, BEFORE InputGameComponent.Init, and the controls
    /// menu is populated from the mapping published there. Creating the actions any later meant the
    /// menu was built without them and never rebuilt.
    /// </summary>
    internal static void EnsureCreated(DefinitionManager? definitions = null)
    {
        definitions ??= TryGetDefinitionManager();

        if (!_created)
        {
            var category = ResolveCategory(definitions);

            foreach (var action in All)
                action.Definition = Create(action, category);

            _created = true;
        }
        else if (!_categoryAssigned)
        {
            // The category may not have been resolvable when the actions were first created - this
            // runs during startup, from the first SetMapping - so fill it in as soon as it is. An
            // action with a null Category is dropped by ControlCustomizationViewModel and never
            // appears in Options -> Controls.
            AssignCategory(ResolveCategory(definitions));
        }

        // Registration is re-checked every time rather than done once: the definition sets are a
        // stack that the engine pushes and pops, and an action the DefinitionManager cannot resolve
        // is exactly the failure this whole class exists to prevent.
        Register(definitions);
    }

    /// <summary>
    /// The DefinitionManager, or null before one exists.
    /// <c>Singleton&lt;T&gt;.Instance</c> is not guaranteed to be set this early in startup.
    /// </summary>
    private static DefinitionManager? TryGetDefinitionManager()
    {
        try
        {
            return Singleton<DefinitionManager>.Instance;
        }
        catch (Exception ex)
        {
            Log.Debug($"  debug: DefinitionManager not available yet ({ex.GetType().Name})");
            return null;
        }
    }

    private static InputActionDefinition Create(BuildPlannerAction action, ActionCategoryDefinition? category)
    {
        // Built through the object builder, not `new InputActionDefinition(...)`, because that is
        // the only route that gives the definition a Guid: RuntimeDefinitionHelper.Create runs the
        // engine's own Init/PostInit and (with keepBuilderGuid) keeps the GUID we chose.
        var builder = new InputActionDefinitionObjectBuilder
        {
            Guid = action.Guid,
            Name = StringId.Get(action.Name),
            DisplayName = LocKey.FromString(action.DisplayName),
            ExpectedInputType = InputType.Digital,
            Category = category,
        };

        var definition = RuntimeDefinitionHelper.Create<InputActionDefinition>(
            builder, context: null, keepBuilderGuid: true);

        Log.Debug($"  debug: created action {action.Name} ({definition.Guid})");
        return definition;
    }

    private static bool _categoryAssigned;

    /// <summary>
    /// ControlCustomizationViewModel drops any action whose Category is null or the hidden category,
    /// and orders groups by ActionCategoryConfiguration.OrderedControlCategories - so the actions
    /// need vanilla's "BuildingControls" category to appear in Options -&gt; Controls at all.
    /// </summary>
    private static ActionCategoryDefinition? ResolveCategory(DefinitionManager? definitions)
    {
        try
        {
            if (definitions != null
                && definitions.TryGetDefinition<ActionCategoryDefinition>(
                    BuildPlannerInstaller.BuildingCategoryGuid, out var category))
            {
                _categoryAssigned = true;
                Log.Write("  action category set to BuildingControls");
                return category;
            }

            Log.Write("  BuildingControls category not resolvable yet; will retry");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("resolving the action category failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Set the category on definitions that were created before it could be resolved.
    ///
    /// InputActionDefinition.Category is `private set` because definitions are normally built by the
    /// content pipeline from .def files, so the backing field is the only way in after Init.
    /// </summary>
    private static void AssignCategory(ActionCategoryDefinition? category)
    {
        if (category == null) return;

        try
        {
            var field = typeof(InputActionDefinition).GetField(
                "<Category>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                Log.Write("  WARNING: InputActionDefinition.<Category>k__BackingField not found;"
                          + " actions will not be listed in Options");
                return;
            }

            foreach (var action in All)
                if (action.Definition != null) field.SetValue(action.Definition, category);
        }
        catch (Exception ex)
        {
            Log.Error("assigning the action category failed", ex);
        }
    }

    /// <summary>
    /// Put our definitions where <c>DefinitionManager.TryGetDefinition(guid)</c> will find them.
    ///
    /// There is no public API for registering a runtime definition - PushDefinitionSetAsync wants an
    /// object-builder locator and a full async load - but the lookup itself is a plain dictionary:
    /// <c>DefinitionSet.TryGetAnyDefinition</c> is nothing but
    /// <c>_definitionsById.TryGetValue(id, out definition)</c>. So the definitions are added to the
    /// same set that already owns vanilla's input actions, which gives them exactly its lifetime.
    /// </summary>
    private static void Register(DefinitionManager? definitions)
    {
        if (definitions == null)
        {
            Log.Write("  ERROR: DefinitionManager unavailable; actions cannot be made rebindable");
            return;
        }

        try
        {
            var missing = 0;
            foreach (var action in All)
            {
                if (action.Definition == null) continue;
                if (!definitions.TryGetDefinition<InputActionDefinition>(action.Guid, out _)) missing++;
            }

            if (missing == 0) return;

            var index = FindInputActionIndex(definitions);
            if (index == null)
            {
                Log.Write("  ERROR: could not reach the definition set holding vanilla input actions;"
                          + " rebinding will not stick");
                return;
            }

            foreach (var action in All)
            {
                if (action.Definition == null) continue;
                index[action.Guid] = action.Definition;
            }

            // Verify through the public API, not by trusting the write.
            foreach (var action in All)
            {
                if (!definitions.TryGetDefinition<InputActionDefinition>(action.Guid, out _))
                    Log.Write($"  ERROR: {action.Name} is still not resolvable after registration");
            }

            Log.Write($"  registered {All.Length} input actions with the DefinitionManager");
        }
        catch (Exception ex)
        {
            Log.Error("registering the Build Planner input actions failed", ex);
        }
    }

    /// <summary>
    /// The GUID index of the definition set that owns vanilla's input actions.
    ///
    /// Located by looking for an action we know exists (ToolTertiary) rather than by set name or
    /// position, so it stays correct if Keen moves the input definitions between sets.
    /// </summary>
    private static Dictionary<Guid, Definition>? FindInputActionIndex(DefinitionManager definitions)
    {
        var setsField = typeof(DefinitionManager).GetField(
            "_definitionSets", BindingFlags.Instance | BindingFlags.NonPublic);

        if (setsField?.GetValue(definitions) is not ImmutableArray<DefinitionSet> sets)
        {
            Log.Write("  WARNING: DefinitionManager._definitionSets not readable");
            return null;
        }

        var indexField = typeof(DefinitionSet).GetField(
            "_definitionsById", BindingFlags.Instance | BindingFlags.NonPublic);

        if (indexField == null)
        {
            Log.Write("  WARNING: DefinitionSet._definitionsById not found");
            return null;
        }

        foreach (var set in sets)
        {
            if (indexField.GetValue(set) is not Dictionary<Guid, Definition> index) continue;
            if (index.ContainsKey(BuildPlannerInstaller.ToolTertiaryActionGuid)) return index;
        }

        return null;
    }

    /// <summary>
    /// The control an action ships with: the main input, plus its modifiers as a composite.
    ///
    /// Built with the engine's own composer, which is the same code the rebinding dialog uses
    /// (InputCompositionDialogViewModel), so a default chord is indistinguishable from one the
    /// player set by hand.
    /// </summary>
    internal static InputControl? DefaultControl(BuildPlannerAction action)
    {
        try
        {
            if (action.Definition == null) return null;

            return InputControlComposer.KeyboardDefault.Compose(
                action.Definition, action.MainInput, action.Modifiers);
        }
        catch (Exception ex)
        {
            Log.Error($"composing the default control for {action.Name} failed", ex);
            return null;
        }
    }
}
