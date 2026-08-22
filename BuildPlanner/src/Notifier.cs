using System;
using System.Collections.Generic;
using System.Text;
using Keen.Game2.Client.UI.HUD.Notification;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Localization;

namespace BuildPlanner;

/// <summary>
/// HUD feedback.
///
/// The SE1 Build Planner always tells the player why nothing happened ("If the engineer's inventory
/// is full, or the components are not yet produced, you'll get a warning"). Silent failure is the
/// single worst outcome for this feature — the player would keep pressing a key that does nothing —
/// so every path through the withdrawal reports something.
///
/// Text is passed through <see cref="LocKey.FromString"/>. The mod ships no localization entries, and
/// an unknown key renders as the key itself, so these read as plain English rather than breaking.
/// </summary>
internal sealed class Notifier
{
    private readonly Action<HudNotification> _show;

    internal Notifier(Action<HudNotification> show) => _show = show;

    private void Text(string message, NotificationType type)
    {
        try
        {
            _show(HudNotification.CreateTextNotification(
                default, LocKey.FromString(message), NotificationPriority.Normal, type));
        }
        catch (Exception ex)
        {
            Log.Error("failed to show notification", ex);
        }
    }

    internal void Info(string message) => Text(message, NotificationType.Info);

    internal void Warning(string message) => Text(message, NotificationType.Error);


    internal void QueuedBlock(string blockName, int queueSize) =>
        Info($"Build Planner: queued {blockName} ({queueSize} total)");

    internal void QueueCleared() => Info("Build Planner: queue cleared");

    internal void NothingQueued() => Warning("Build Planner: nothing queued");

    internal void NoTarget() => Warning("Build Planner: not looking at a container");

    internal void NothingToQueue() => Warning("Build Planner: not looking at an unfinished block");

    internal void AlreadyComplete() => Info("Build Planner: that block is already finished");

    internal void AlreadyHaveEverything() => Info("Build Planner: you already have everything queued");

    internal void Withdrew(IReadOnlyList<ItemAmount> transferred) =>
        Info($"Build Planner: withdrew {Summarize(transferred)}");

    internal void WithdrewPartial(IReadOnlyList<ItemAmount> transferred, IReadOnlyList<ItemAmount> missing) =>
        Warning($"Build Planner: withdrew {Summarize(transferred)} — still short {Summarize(missing)}");

    internal void NothingAvailable(IReadOnlyList<ItemAmount> missing) =>
        Warning($"Build Planner: could not find {Summarize(missing)}");

    internal void Deposited(int stacks) =>
        Info(stacks > 0 ? $"Build Planner: deposited {stacks} stack(s)" : "Build Planner: nothing to deposit");

    /// <summary>Renders item amounts compactly, capped so a long list cannot flood the HUD.</summary>
    private static string Summarize(IReadOnlyList<ItemAmount> items)
    {
        if (items == null || items.Count == 0) return "nothing";

        const int maxListed = 4;
        var sb = new StringBuilder();

        for (var i = 0; i < items.Count && i < maxListed; i++)
        {
            if (sb.Length > 0) sb.Append(", ");
            var item = items[i];
            sb.Append((int)item.Amount).Append("x ").Append(item.Item?.DisplayName.ToString() ?? "?");
        }

        if (items.Count > maxListed) sb.Append(" +").Append(items.Count - maxListed).Append(" more");

        return sb.ToString();
    }
}
