using System;
using System.Collections.Generic;
using System.Linq;
using BuildPlanner;
using Xunit;

namespace BuildPlanner.Tests;

/// <summary>
/// The action table (<see cref="BuildPlannerActions.All"/>) is now the whole control scheme: it
/// carries the identity a rebinding is stored against, the name the controls menu sorts by, and the
/// default chord. All three have to be unique per action, and none of that needs the game running.
///
/// This replaces the old ModifiersTests. That suite checked a modifier-to-action function that no
/// longer exists — the chords are data now, not a switch over live keyboard state.
/// </summary>
public class BuildPlannerActionsTests
{
    [Fact]
    public void EveryActionHasARealGuid()
    {
        foreach (var action in BuildPlannerActions.All)
            Assert.NotEqual(Guid.Empty, action.Guid);
    }

    /// <summary>
    /// A shared GUID would silently merge two actions' bindings: the customisation is persisted by
    /// GUID, so the second write would overwrite the first.
    /// </summary>
    [Fact]
    public void GuidsAreUnique()
    {
        var guids = BuildPlannerActions.All.Select(x => x.Guid).ToList();
        Assert.Equal(guids.Count, guids.Distinct().Count());
    }

    /// <summary>
    /// InputActionDefinitionPostProcessor.Validate reports duplicate action names as an error, and
    /// the controls menu sorts by Name inside a category.
    /// </summary>
    [Fact]
    public void NamesAreUnique()
    {
        var names = BuildPlannerActions.All.Select(x => x.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void DisplayNamesAreUnique()
    {
        var names = BuildPlannerActions.All.Select(x => x.DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    /// <summary>
    /// Two actions on the same chord is unresolvable: the disambiguating filter picks by input
    /// count, so an exact tie leaves the winner to frame ordering.
    /// </summary>
    [Fact]
    public void DefaultBindingsAreUnique()
    {
        var bindings = new List<string>();

        foreach (var action in BuildPlannerActions.All)
        {
            var modifiers = action.Modifiers.Select(x => x.ToString()).OrderBy(x => x, StringComparer.Ordinal);
            bindings.Add($"{action.MainInput} + {string.Join(",", modifiers)}");
        }

        Assert.Equal(bindings.Count, bindings.Distinct().Count());
    }

    /// <summary>
    /// Every operation the controller can perform must be reachable from some key, or it is dead
    /// code that no player can invoke.
    /// </summary>
    [Fact]
    public void EveryPlannerActionIsBoundExactlyOnce()
    {
        foreach (PlannerAction performs in Enum.GetValues(typeof(PlannerAction)))
            Assert.Single(BuildPlannerActions.All, x => x.Performs == performs);
    }

    /// <summary>
    /// The queue key lives in its own context, activated only while a welder shows its block panel,
    /// so it must not also sit in the always-active one.
    /// </summary>
    [Fact]
    public void QueueIsExcludedFromThePlannerContext()
    {
        Assert.Equal(PlannerAction.Queue, BuildPlannerActions.Queue.Performs);
        Assert.DoesNotContain(BuildPlannerActions.Planner, x => x.Performs == PlannerAction.Queue);
        Assert.Equal(BuildPlannerActions.All.Length - 1, BuildPlannerActions.Planner.Count());
    }
}
