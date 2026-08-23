using System;
using System.Collections.Generic;
using Keen.Game2.Client.UI.HUD.Notification;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Render;
using Keen.VRage.Library.Localization;
using Keen.VRage.Library.Utils;

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

    /// <summary>
    /// The player IS aiming at a block, but it holds nothing and nothing is reachable through it.
    /// </summary>
    /// <remarks>
    /// Reported by a player aiming at a Survival Kit: "it tells me it's not a container", followed
    /// by a reasonable but wrong guess that small conveyors were filtering components out.
    ///
    /// The Survival Kit is a respawn point with a conveyor port and no inventory of its own - its
    /// server entity carries eleven components and <c>InventoryComponent</c> is not among them.
    /// "Not looking at a container" was technically true and sent them hunting the wrong problem,
    /// so the message now names the block and says what is actually missing.
    /// </remarks>
    internal void TargetHoldsNothing(string blockName) =>
        Warning($"Build Planner: {blockName} holds nothing - aim at a container");

    internal void NothingToQueue() => Warning("Build Planner: not looking at an unfinished block");

    internal void AlreadyHaveEverything() => Info("Build Planner: you already have everything queued");

    internal void Withdrew(IReadOnlyList<ItemAmount> transferred) => Gained(transferred);

    internal void WithdrewPartial(IReadOnlyList<ItemAmount> transferred, IReadOnlyList<ItemAmount> missing)
    {
        // Rows, not a sentence. This was once "withdrew A, B — still short C", which the HUD trimmed
        // at the dash, losing the half the player most needs.
        Gained(transferred);
        PerItem(missing, "short", NotificationType.Error);
    }

    /// <summary>
    /// Report received items the way the game does: one structured notification each.
    ///
    /// This is how vanilla reports a transfer - <c>InventoryNotificationsSessionComponent.DisplayItem</c>
    /// calls <c>CreateMaterialNotification(icon, name, amount, total)</c> per item and never composes
    /// a sentence. The HUD is built for exactly that: <c>TryUpdateNotification</c> finds an existing
    /// row with the same name and *adds* to it (<c>Amount += ...</c>) rather than stacking a second,
    /// and the amount and name are separate bound fields, so neither competes for the one line of
    /// width that free text has to fit into.
    ///
    /// Withdrawing twice therefore updates one row instead of filling the stack, which matters:
    /// <c>MaterialNotificationConfiguration.MaxStackCount</c> is 2 in vanilla, and anything beyond it waits in a
    /// queue until an earlier notification expires.
    /// </summary>
    private void Gained(IReadOnlyList<ItemAmount> items)
    {
        if (items == null || items.Count == 0) return;

        foreach (var item in items)
        {
            if (item.Item == null) continue;

            try
            {
                // The cast is the one vanilla's own DisplayItem performs: an item's Icon is a
                // ResourceHandle<PngAsset>, and the notification wants a ResourceHandle<GUIAsset>.
                var icon = (ResourceHandle<GUIAsset>)(ResourceHandle)item.Item.Icon;

                // Total left null: it means "how many you now hold", which is the destination
                // inventory's business, not the withdrawal's. Reporting a wrong total would be worse
                // than reporting none - the field simply goes unshown.
                _show(HudNotification.CreateMaterialNotification(
                    icon, item.Item.DisplayName, (int)item.Amount, null));
            }
            catch (Exception ex)
            {
                Log.Error($"failed to show a material notification for {item.Item.DisplayName}", ex);
            }
        }
    }

    /// <summary>
    /// One row per item for things that are NOT a gain — what is missing, what cannot be made.
    ///
    /// **Why not the material notification used for gains.** That template is hardcoded as a gain:
    /// the "+" is a literal TextBlock and both it and the amount take the "Success" brush, so the
    /// number is green whatever the value, and a negative amount would render as "+ -100".
    /// <c>MaterialNotificationViewModel</c> also never copies <c>notification.Type</c> (unlike the
    /// text one), so the Error flag cannot recolour it either. Saying "you gained 100 Heavy-Duty
    /// Plate" about something the player did not get is worse than a plainer row.
    ///
    /// A text notification still takes an icon — vanilla's own "inventory full" passes one — so
    /// these keep the item's picture and the red Error styling, one row per item, matching the
    /// rhythm of the gains beside them.
    /// </summary>
    private void PerItem(IReadOnlyList<ItemAmount> items, string verb, NotificationType type)
    {
        if (items == null || items.Count == 0) return;

        foreach (var item in items)
        {
            if (item.Item == null) continue;

            try
            {
                var icon = (ResourceHandle<GUIAsset>)(ResourceHandle)item.Item.Icon;

                _show(HudNotification.CreateTextNotification(
                    icon,
                    LocKey.FromString($"{verb} {(int)item.Amount}x {item.Item.DisplayName}"),
                    NotificationPriority.Normal,
                    type));
            }
            catch (Exception ex)
            {
                Log.Error($"failed to show a notification for {item.Item.DisplayName}", ex);
            }
        }
    }

    internal void NothingAvailable(IReadOnlyList<ItemAmount> missing) =>
        PerItem(missing, "missing", NotificationType.Error);

    internal void Producing(IReadOnlyList<ProductionOrder> orders) =>
        PerItem(AsItems(orders), "making", NotificationType.Info);

    internal void ProducingPartial(IReadOnlyList<ProductionOrder> orders, IReadOnlyList<ItemAmount> unproducible)
    {
        PerItem(AsItems(orders), "making", NotificationType.Info);
        PerItem(unproducible, "cannot make", NotificationType.Error);
    }

    internal void CannotProduce(IReadOnlyList<ItemAmount> unproducible) =>
        PerItem(unproducible, "cannot make", NotificationType.Error);

    internal void NoConverter() =>
        Warning("Build Planner: no assembler or refinery connected to that block");

    internal void AlreadyHaveEverythingToProduce() =>
        Info("Build Planner: you already have everything queued; nothing to produce");

    /// <summary>
    /// A deposit that ran out of room. Reports what went in, then what is still being carried.
    ///
    /// **One row for the whole remainder, not one per item.** Reported in game: a deposit that left
    /// both Cobalt and Silicon behind showed only the Silicon. The log proved both notifications
    /// were raised ("still carrying 1348x Cobalt" / "still carrying 3140x Silicon"), so the HUD
    /// dropped one - `MaterialNotificationConfiguration.MaxStackCount` is 2 in vanilla, and it is
    /// the only notification configuration the game ships.
    ///
    /// Rather than depend on exactly how the HUD stacks and queues rows, this raises a single
    /// notification, which no stack cap can truncate. The remainder is the one thing the player must
    /// not lose - it is the whole reason this method exists - so it must not be the thing competing
    /// for the last slot.
    ///
    /// Width is bounded instead: the HUD does not wrap, so the list is capped and the overflow
    /// summarised. The full per-item breakdown always goes to the log.
    /// </summary>
    internal void DepositedPartial(int itemTypes, IReadOnlyList<ItemAmount> stillCarried)
    {
        // Only when something actually moved. Deposited(0) renders "nothing to deposit", which
        // paired with the remainder produced a flat contradiction on the headline case this method
        // exists for: a full container gave "nothing to deposit" immediately followed by
        // "still carrying 500x Steel Plate".
        if (itemTypes > 0) Deposited(itemTypes);

        if (stillCarried == null || stillCarried.Count == 0) return;

        // Full detail to the log regardless of what fits on screen.
        foreach (var item in stillCarried)
            if (item.Item != null)
                Log.Debug($"  debug: still carrying {(int)item.Amount} x {item.Item.DisplayName}");

        // "still carrying", not "no room for". The remainder is a fact; the cause is not - the loop
        // reaches here when containers were full, when a filter rejected the item, and when every
        // transfer threw. Naming a cause we did not establish is how a log stops being trustworthy.
        Text(DescribeRemainder(stillCarried), NotificationType.Error);
    }

    /// <summary>
    /// "still carrying 1348x Cobalt, 3140x Silicon", trimmed to fit one HUD line.
    /// </summary>
    /// <remarks>
    /// The HUD notification is NoWrap between a 317 and 480 pixel width, so roughly sixty
    /// characters survive. Two items fit comfortably; beyond that the rest are counted rather than
    /// named, which is honest at a glance and keeps the number the player needs - how much is still
    /// on them - visible for every item in the log.
    /// </remarks>
    private static string DescribeRemainder(IReadOnlyList<ItemAmount> items)
    {
        var pairs = new List<(string Name, int Amount)>(items.Count);
        foreach (var item in items)
            if (item.Item != null)
                pairs.Add((item.Item.DisplayName.ToString(), (int)item.Amount));

        return DescribeRemainder(pairs);
    }

    /// <summary>
    /// The formatting itself, over plain name/amount pairs so it can be unit-tested -
    /// <see cref="ItemDefinition"/> cannot be constructed outside a loaded game, and the truncation
    /// rule is the part that can silently regress.
    /// </summary>
    internal static string DescribeRemainder(IReadOnlyList<(string Name, int Amount)> items)
    {
        const int Shown = 2;

        if (items == null || items.Count == 0) return "Build Planner: nothing was deposited";

        var named = new List<string>(Shown);
        var extra = 0;

        foreach (var (name, amount) in items)
        {
            if (named.Count < Shown) named.Add($"{amount}x {name}");
            else extra++;
        }

        var listed = string.Join(", ", named);
        return extra > 0
            ? $"still carrying {listed} +{extra} more"
            : $"still carrying {listed}";
    }

    /// <summary>
    /// Report a deposit by the number of item TYPES moved, which is what the caller counts.
    ///
    /// This said "stack(s)", which it never was: deposit de-duplicates by item definition and hands
    /// each type over in one go, spread across as many containers as it takes. Five stacks of Steel
    /// Plate leaving the player is one entry in that count, and calling it "1 stack" was simply a
    /// wrong number on screen.
    /// </summary>
    internal void Deposited(int itemTypes) =>
        Info(itemTypes > 0
            ? $"Build Planner: deposited {itemTypes} item type(s)"
            : "Build Planner: nothing to deposit");

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

}
