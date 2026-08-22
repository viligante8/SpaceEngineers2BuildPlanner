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

    /// <summary>
    /// One message for a whole area-welder selection.
    ///
    /// An area welder can show dozens of blocks at once, and reporting each one separately would
    /// bury the HUD under its own notifications.
    /// </summary>
    internal void QueuedBlocks(int added, int queueSize) =>
        Info($"Build Planner: queued {added} blocks ({queueSize} total)");

    internal void QueueCleared(int cleared) =>
        Info($"Build Planner: cleared {cleared} queued block(s)");

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

    internal void Producing(IReadOnlyList<ProductionOrder> orders) =>
        Info($"Build Planner: producing {SummarizeOrders(orders)}");

    internal void ProducingPartial(IReadOnlyList<ProductionOrder> orders, IReadOnlyList<ItemAmount> unproducible) =>
        Warning($"Build Planner: producing {SummarizeOrders(orders)} — cannot make {Summarize(unproducible)}");

    internal void CannotProduce(IReadOnlyList<ItemAmount> unproducible) =>
        Warning($"Build Planner: nothing in reach can make {Summarize(unproducible)}");

    internal void NoConverter() =>
        Warning("Build Planner: no assembler or refinery connected to that block");

    internal void AlreadyHaveEverythingToProduce() =>
        Info("Build Planner: you already have everything queued; nothing to produce");

    internal void Deposited(int stacks) =>
        Info(stacks > 0 ? $"Build Planner: deposited {stacks} stack(s)" : "Build Planner: nothing to deposit");

    /// <summary>
    /// Renders production orders as amounts, not run counts.
    ///
    /// The player queued blocks and thinks in components — "producing 4 runs" is meaningless to
    /// them, while "producing 120x Steel Plate" is the number they can compare against the block
    /// panel. The run count stays in the log, where it is a diagnostic.
    /// </summary>
    private static string SummarizeOrders(IReadOnlyList<ProductionOrder> orders)
    {
        if (orders == null || orders.Count == 0) return "nothing";

        var items = new List<ItemAmount>(orders.Count);
        foreach (var order in orders) items.Add(new ItemAmount(order.Item, order.Amount));

        return Summarize(items);
    }

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
