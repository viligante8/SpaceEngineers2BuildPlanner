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
        List("Build Planner: withdrew", transferred, NotificationType.Info);

    internal void WithdrewPartial(IReadOnlyList<ItemAmount> transferred, IReadOnlyList<ItemAmount> missing)
    {
        // Two messages, not one sentence. Joining them with an em dash produced a line far past what
        // the HUD can show, so the half the player most needs - what is still missing - was the half
        // that got cut off.
        List("Build Planner: withdrew", transferred, NotificationType.Info);
        List("Build Planner: still short", missing, NotificationType.Error);
    }

    internal void NothingAvailable(IReadOnlyList<ItemAmount> missing) =>
        List("Build Planner: could not find", missing, NotificationType.Error);

    internal void Producing(IReadOnlyList<ProductionOrder> orders) =>
        List("Build Planner: producing", AsItems(orders), NotificationType.Info);

    internal void ProducingPartial(IReadOnlyList<ProductionOrder> orders, IReadOnlyList<ItemAmount> unproducible)
    {
        List("Build Planner: producing", AsItems(orders), NotificationType.Info);
        List("Build Planner: cannot make", unproducible, NotificationType.Error);
    }

    internal void CannotProduce(IReadOnlyList<ItemAmount> unproducible) =>
        List("Build Planner: nothing can make", unproducible, NotificationType.Error);

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
    private static List<ItemAmount> AsItems(IReadOnlyList<ProductionOrder> orders)
    {
        var items = new List<ItemAmount>(orders?.Count ?? 0);
        if (orders == null) return items;

        foreach (var order in orders) items.Add(new ItemAmount(order.Item, order.Amount));
        return items;
    }

    /// <summary>
    /// The HUD notification is ONE LINE and does not wrap.
    ///
    /// From the compiled XAML of <c>HUDNotificationView</c>, the text block bound to Content is built
    /// with:
    ///
    /// <code>
    /// textBlock3.TextTrimming = TextTrimming.CharacterEllipsis;
    /// textBlock3.TextWrapping = TextWrapping.NoWrap;
    /// </code>
    ///
    /// inside a column of <c>MaxWidth = 480</c> with 20px margins either side. So anything longer
    /// than roughly 440px of text is cut off with an ellipsis and simply cannot be read — which is
    /// what happened to every multi-item withdrawal message.
    ///
    /// Long lists are therefore split across several notifications, which the HUD stacks. Continuing
    /// lines are marked so they do not read as separate events.
    ///
    /// **The budget is measured, not calculated.** A screenshot of the real HUD settled it: a line of
    /// 43 characters ("Build Planner: … 17x Construction Component") rendered with room to spare,
    /// while one of about 58 was cut after roughly 48. The font is proportional, so a character count
    /// is only a proxy - hence a deliberately conservative number rather than the observed maximum.
    /// </summary>
    private const int MaxLineChars = 44;

    /// <summary>
    /// Continuation lines drop the "Build Planner: " prefix.
    ///
    /// On a ~44 character budget that prefix was taking a third of every line for no information -
    /// the first line already says whose message this is, and the ellipsis marks the rest as its
    /// continuation.
    /// </summary>
    private const string ContinuationPrefix = "…";

    /// <summary>
    /// How many lines one report may take before the rest is summarised as a count. An area welder
    /// selection can need a dozen component types, and burying the screen is its own failure.
    /// </summary>
    private const int MaxLines = 4;

    private void List(string prefix, IReadOnlyList<ItemAmount> items, NotificationType type)
    {
        var parts = new List<string>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item.Item == null) continue;
                parts.Add($"{(int)item.Amount}x {item.Item.DisplayName}");
            }
        }

        if (parts.Count == 0)
        {
            Text($"{prefix} nothing", type);
            return;
        }

        var index = 0;
        var line = 0;

        // A long prefix plus a long item name cannot share a line: "Build Planner: nothing can make"
        // is 31 characters and "17x Construction Component" is 26, so pairing them would overflow and
        // be trimmed - the very thing this method exists to prevent. When they do not fit, the prefix
        // becomes a header on its own and the items start on the next line, where only the ellipsis
        // precedes them.
        if (prefix.Length + 1 + parts[0].Length > MaxLineChars)
        {
            Text(prefix, type);
            line = 1;
        }

        while (index < parts.Count)
        {
            if (line == MaxLines)
            {
                Text($"{ContinuationPrefix} +{parts.Count - index} more", type);
                return;
            }

            var sb = new StringBuilder(line == 0 ? prefix : ContinuationPrefix);
            var taken = 0;

            while (index + taken < parts.Count)
            {
                var next = parts[index + taken];
                var separator = taken == 0 ? " " : ", ";

                // Always take at least one item, however long its name: a line that overflows is
                // still better than a loop that never advances.
                if (taken > 0 && sb.Length + separator.Length + next.Length > MaxLineChars) break;

                sb.Append(separator).Append(next);
                taken++;
            }

            Text(sb.ToString(), type);
            index += taken;
            line++;
        }
    }
}
