using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Keen.Game2.Client.UI.TerminalScreen;
using Keen.Game2.Client.UI.TerminalScreen.BuildPlanners;
using Keen.Game2.Simulation.GameSystems.BuildPlanners;
using Keen.VRage.UI.Shared.Controls;

// The terminal screen's type and its namespace are both called "TerminalScreen", so the type needs
// an alias to be nameable here — the same collision GScreen has in BuildPlannerInstaller.
using TerminalScreenView = Keen.Game2.Client.UI.TerminalScreen.TerminalScreen;

namespace BuildPlanner;

/// <summary>
/// Turns on the build planner panel Keen already shipped in the terminal screen.
///
/// **The panel exists and is complete.** `TerminalScreen.axaml` builds it in full: a bottom-right
/// Grid inside a <see cref="LayoutTimer"/> labelled <c>Terminal.BuildPlanner</c>, holding a Border,
/// the labels "Build" / "Planner" / "(WIP Pos)", a **Produce** button bound to
/// <c>BuildPlannerBlock_ScheduleAll</c>, an <c>ItemsControl</c> whose <c>ItemsSource</c> is a
/// compiled binding to <c>TerminalScreenViewModel.BuildPlannerBlocks</c> with a
/// <see cref="BuildPlannerIconControl"/> item template (its ProduceCommand and RemoveCommand bound
/// to <c>ProduceBuildPlannerBlock</c> / <c>RemoveBuildPlannerBlock</c>), and a **Clear** button
/// bound to <c>BuildPlannerBlock_ClearAll</c>.
///
/// Three things — and only three — stop it working in the shipping game:
///
/// 1. **The Grid is hidden.** The compiled XAML sets <c>IsVisible = false</c> as a literal, not a
///    binding (`grid29.IsVisible = false;` in the decompiled `TerminalScreen.InitializeComponent`,
///    IL `IL_1336: ldc.i4.0` / `IL_1337: callvirt Visual::set_IsVisible`). The "(WIP Pos)" label
///    beside it says why: Keen parked the panel mid-development.
/// 2. **`TerminalScreenViewModel._buildPlannerData` is never assigned.** Verified at IL level
///    across the whole of `Game2.Client.dll`: the field is `initonly`, is read by six `ldfld`s, and
///    has **no `stfld` anywhere in the assembly**. It is therefore always null, so all four verbs
///    (`BuildPlannerBlock_ScheduleAll`, `BuildPlannerBlock_ClearAll`, `ProduceBuildPlannerBlock`,
///    `RemoveBuildPlannerBlock`) would throw a NullReferenceException the moment a button was
///    pressed.
/// 3. **`UpdateBuildPlannerBlocks` is never subscribed.** It is a
///    <see cref="PropertyChangedEventHandler"/>-shaped method with no `ldftn` reference anywhere —
///    nothing ever hands it to `_buildPlannerData.PropertyChanged`, so `BuildPlannerBlocks` would
///    stay empty even if the data existed.
///
/// This class supplies all three. That is deliberately preferred over drawing our own overlay: the
/// panel is Keen's, styled with the terminal's own resources, and it will keep whatever polish they
/// add later. See notes/build-planner-api.md, "The terminal's build planner panel is complete but
/// switched off".
///
/// **Best-effort throughout.** <see cref="BuildPlannerQueue"/> remains the source of truth and the
/// withdrawal path never reads any of this, so every failure here is logged and swallowed.
/// </summary>
internal static class TerminalPlannerPanel
{
    /// <summary>
    /// The <see cref="LayoutTimer.Label"/> Keen gave the panel's decorator. This is the finder,
    /// because the Grid itself carries no <c>x:Name</c>.
    /// </summary>
    private const string PanelLabel = "Terminal.BuildPlanner";

    /// <summary>Keen's own work-in-progress marker, hidden rather than shown to a player.</summary>
    private const string WipPlaceholder = "(WIP Pos)";

    /// <summary>
    /// How many block icons the panel will show, matching Keen's own
    /// <c>UpdateBuildPlannerBlocks</c>: <c>for (i = 0; i &lt; Math.Min(10, plannedBlocks.Count); i++)</c>.
    ///
    /// This is not arbitrary and must not be raised without changing the layout. The ItemsControl's
    /// ItemsPanel is a plain <c>StackPanel</c> with spacing and no <c>Orientation</c> set — so it is
    /// **vertical** — inside a Grid anchored to the bottom-right with no scroll viewer. An
    /// uncapped list grows upward off the top of the screen; a 40-block queue would cover the
    /// terminal. Keen's ten is the number that fits.
    /// </summary>
    private const int MaxShown = 10;

    private static TerminalScreenView? _boundScreen;
    private static TerminalScreenViewModel? _boundViewModel;
    private static BuildPlannerData? _boundData;
    private static PropertyChangedEventHandler? _boundHandler;

    /// <summary>
    /// Nesting depth of an in-progress batch write; while non-zero, notifications are ignored.
    /// </summary>
    private static int _batchDepth;

    /// <summary>
    /// Suppress refreshes for the duration of a multi-step write, then run exactly one.
    ///
    /// **Measured, not theoretical.** <see cref="EngineQueueMirror.Sync"/> rebuilds the engine's list
    /// by removing every block and re-adding every block, and each of those raises
    /// <c>OnPropertyChanged("PlannedBlocks")</c>. Without this, queueing one block with twelve
    /// already queued ran ~25 complete rebuilds of the bound list — each clearing and repopulating
    /// the ItemsControl. The game log from 2026-08-22 shows the storm plainly: counts ticking
    /// 12,11,10…0 then 1,2,3…13 for a single keypress. Cost grows with the square of the queue.
    ///
    /// Batching turns that into one refresh per queue action.
    /// </summary>
    internal static void BeginBatch() => _batchDepth++;

    /// <inheritdoc cref="BeginBatch"/>
    internal static void EndBatch()
    {
        if (_batchDepth > 0) _batchDepth--;
        if (_batchDepth > 0) return;

        var viewModel = _boundViewModel;
        var data = _boundData;
        if (viewModel == null || data == null) return;

        Refresh(viewModel, data);
    }

    /// <summary>
    /// Write to the view model's private readonly <c>_buildPlannerData</c>.
    ///
    /// <c>UnsafeAccessor</c> rather than reflection: the field is <c>initonly</c>, this is checked
    /// against the real field type at JIT time, and it costs nothing per call. It throws
    /// <see cref="MissingFieldException"/> on first use if a game update renames the field, which is
    /// caught and reported at the call site.
    /// </summary>
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_buildPlannerData")]
    private static extern ref BuildPlannerData BuildPlannerDataField(TerminalScreenViewModel viewModel);

    /// <summary>
    /// Called once per terminal screen instance, straight after its XAML has been built.
    ///
    /// The work is attempted three times over the screen's life, because the two halves become
    /// possible at different moments and neither is guaranteed at any single one:
    ///
    /// - after <c>InitializeComponent</c> the panel is in the LOGICAL tree, but a UserControl's
    ///   content does not enter the VISUAL tree until its template is applied, so a visual-tree
    ///   search here would find nothing;
    /// - <c>DataContext</c> (the view model) is assigned by <c>ScreenView</c> separately from
    ///   construction, so it may not be set yet either.
    ///
    /// Both steps are idempotent, so running them repeatedly is free and covers screen reuse
    /// (<c>TerminalScreen</c> is <c>IReusableScreen</c> — the same control comes back with a new
    /// view model).
    /// </summary>
    internal static void Install(TerminalScreenView? screen)
    {
        if (screen == null)
        {
            Log.Write("  terminal: InitializeComponent ran without a screen instance; panel not wired");
            return;
        }

        Wire(screen, "init");
        screen.AttachedToVisualTree += (_, _) => Wire(screen, "attach");
        screen.DataContextChanged += (_, _) => Wire(screen, "datacontext");
        Log.Debug("  terminal: watching the screen for attach and data-context changes");
    }

    private static void Wire(TerminalScreenView screen, string when)
    {
        try
        {
            Reveal(screen, when);
            Bind(screen, when);
        }
        catch (Exception ex)
        {
            // Never fatal: the terminal must still open, and queueing never touches this.
            Log.Error($"wiring the terminal build planner panel ({when}) failed", ex);
        }
    }

    /// <summary>Flip the shipped panel's hardcoded <c>IsVisible = false</c>.</summary>
    private static void Reveal(TerminalScreenView screen, string when)
    {
        var decorator = FindPanelDecorator(screen);
        if (decorator == null)
        {
            // Expected at "init" if the tree is not walkable yet; the attach pass covers it. Logged
            // regardless, because a panel that never appears must not be a silent code path.
            Log.Debug($"  terminal[{when}]: no LayoutTimer labelled '{PanelLabel}' found yet");
            return;
        }

        if (decorator.Child is not Control panel)
        {
            Log.Write($"  terminal[{when}]: '{PanelLabel}' has no child control; nothing to reveal");
            return;
        }

        if (panel.IsVisible)
        {
            Log.Debug($"  terminal[{when}]: planner panel already visible");
            return;
        }

        panel.IsVisible = true;
        HideWipPlaceholder(panel, when);
        Log.Write($"  terminal[{when}]: revealed the shipped build planner panel");
    }

    /// <summary>
    /// Hide Keen's "(WIP Pos)" label.
    ///
    /// It is a positioning note to themselves, not text meant for a player, and it sits in the
    /// middle of the panel's StackPanel. Hidden rather than retexted so nothing here pretends to be
    /// a translated string.
    /// </summary>
    private static void HideWipPlaceholder(Control panel, string when)
    {
        var placeholder = panel.GetLogicalDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == WipPlaceholder);

        if (placeholder == null)
        {
            Log.Debug($"  terminal[{when}]: no '{WipPlaceholder}' label to hide");
            return;
        }

        placeholder.IsVisible = false;
        Log.Debug($"  terminal[{when}]: hid Keen's '{WipPlaceholder}' label");
    }

    /// <summary>
    /// Give the view model the data Keen never assigned, and keep its bound list in step with it.
    /// </summary>
    private static void Bind(TerminalScreenView screen, string when)
    {
        if (screen.DataContext is not TerminalScreenViewModel viewModel)
        {
            // Closing sets DataContext to null (ScreenView does `base.DataContext = null`). Let go
            // then, rather than waiting for the next terminal to open: the subscription outlives the
            // screen otherwise, and a queue change in between would refill a disposed view model's
            // list. Guarded on identity because `init` and `attach` also arrive with no data context,
            // and those must not tear down a binding another screen still owns.
            if (ReferenceEquals(screen, _boundScreen))
            {
                Log.Debug($"  terminal[{when}]: the bound screen lost its view model; releasing");
                Release();
            }

            Log.Debug($"  terminal[{when}]: data context is not a TerminalScreenViewModel yet");
            return;
        }

        if (ReferenceEquals(viewModel, _boundViewModel))
        {
            Log.Debug($"  terminal[{when}]: view model already bound");
            return;
        }

        var data = BuildPlannerBinding.CurrentPlannerData();
        if (data == null)
        {
            // Resolve has already said why in its own log lines.
            Log.Write($"  terminal[{when}]: no BuildPlannerData; the panel will show an empty queue");
            return;
        }

        Release();

        try
        {
            BuildPlannerDataField(viewModel) = data;
        }
        catch (Exception ex)
        {
            // Without the field the panel still LISTS blocks (that binding is to BuildPlannerBlocks,
            // which we fill directly below), but Produce/Clear/Remove would throw when pressed. Say
            // so plainly rather than leaving the player to find it by clicking.
            Log.Error("could not set TerminalScreenViewModel._buildPlannerData;" +
                      " the panel's Produce/Clear buttons will not work", ex);
            return;
        }

        _boundScreen = screen;
        _boundViewModel = viewModel;
        _boundData = data;

        // The subscription Keen omitted. Their own UpdateBuildPlannerBlocks is private and never
        // referenced, so this reimplements it against public API only: BuildPlannerBlocks has a
        // public getter and BuildPlannerBlockModel a public constructor. Doing it ourselves also
        // means no reflection on a method name that could drift.
        _boundHandler = (_, e) =>
        {
            if (e?.PropertyName is not null and not nameof(BuildPlannerData.PlannedBlocks)) return;

            // Mid-rebuild the list is a half-written intermediate state; EndBatch runs the one
            // refresh that matters. See BeginBatch for what this costs when left unbatched.
            if (_batchDepth > 0) return;

            Refresh(viewModel, data);
        };

        data.PropertyChanged += _boundHandler;

        // Seed once: the queue almost always predates the terminal being opened, and PropertyChanged
        // only fires on the NEXT change.
        Refresh(viewModel, data);

        Log.Write($"  terminal[{when}]: bound the planner panel to BuildPlannerData" +
                  $" ({data.PlannedBlocks?.Count ?? 0} block(s) queued)");
    }

    /// <summary>
    /// Rebuild the bound list from the planned blocks.
    ///
    /// <c>BuildPlannerBlocks</c> is an <c>AvaloniaList</c>, which raises collection-changed on its
    /// own, so the ItemsControl re-renders without any further notification plumbing. Rebuilding
    /// wholesale rather than diffing keeps this a pure projection — the same choice, for the same
    /// reason, as <see cref="EngineQueueMirror"/>.
    /// </summary>
    private static void Refresh(TerminalScreenViewModel viewModel, BuildPlannerData data)
    {
        try
        {
            var blocks = viewModel.BuildPlannerBlocks;
            if (blocks == null)
            {
                Log.Write("  terminal: BuildPlannerBlocks is null; cannot show the queue");
                return;
            }

            blocks.Clear();

            var planned = data.PlannedBlocks;
            if (planned == null)
            {
                Log.Debug("  terminal: no planned blocks to show");
                return;
            }

            var shown = 0;
            foreach (var block in planned)
            {
                if (block == null) continue;
                if (shown >= MaxShown) break;
                blocks.Add(new BuildPlannerBlockModel(block));
                shown++;
            }

            // Say when the panel is not showing everything. The queue is the thing the player is
            // reasoning about, and a panel that silently stops at ten would make a 12-block queue
            // look like a 10-block one — with the withdrawal still pulling for all 12.
            if (planned.Count > shown)
                Log.Write($"  terminal: showing {shown} of {planned.Count} queued block(s)" +
                          $" (the panel's layout holds {MaxShown})");
            else
                Log.Debug($"  terminal: panel now showing {shown} block(s)");
        }
        catch (Exception ex)
        {
            Log.Error("refreshing the terminal planner panel failed", ex);
        }
    }

    /// <summary>
    /// Drop the previous subscription.
    ///
    /// <see cref="BuildPlannerData"/> outlives any one terminal screen — it is per-player data on
    /// the server store — so leaving handlers attached would accumulate one dead view model per
    /// terminal the player ever opened.
    /// </summary>
    private static void Release()
    {
        if (_boundData != null && _boundHandler != null)
        {
            _boundData.PropertyChanged -= _boundHandler;
            Log.Debug("  terminal: released the previous panel binding");
        }

        _boundScreen = null;
        _boundViewModel = null;
        _boundData = null;
        _boundHandler = null;
    }

    // ---- The four buttons ------------------------------------------------------------------
    //
    // Each of these REPLACES the view model's own verb rather than running alongside it: Keen's
    // implementations are half-built (see BuildPlannerController's "Terminal panel entry points"),
    // and running both would double up. The engine's PlannedBlocks list still ends up correct,
    // because every one of these routes through the mod's queue and EngineQueueMirror rebuilds the
    // engine list from it afterwards.
    //
    // Every path reports itself. A button that silently does nothing is indistinguishable from a
    // button that was never wired, and that ambiguity has cost this project several game restarts.

    /// <summary>The panel's "Produce" button.</summary>
    internal static void OnProduceAll(TerminalScreenViewModel viewModel)
    {
        var controller = Controller("Produce");
        controller?.ProduceQueueFromTerminal();
    }

    /// <summary>A single block's produce button, on its icon.</summary>
    internal static void OnProduceBlock(TerminalScreenViewModel viewModel, BuildPlannerBlockModel block)
    {
        var controller = Controller("Produce (single block)");
        if (controller == null) return;

        var index = IndexOf(viewModel, block, "produce");
        if (index < 0) return;

        controller.ProduceOneFromTerminal(index);
    }

    /// <summary>A single block's remove button, on its icon.</summary>
    internal static void OnRemoveBlock(TerminalScreenViewModel viewModel, BuildPlannerBlockModel block)
    {
        var controller = Controller("Remove");
        if (controller == null) return;

        var index = IndexOf(viewModel, block, "remove");
        if (index < 0) return;

        controller.RemoveQueuedFromTerminal(index);
    }

    /// <summary>The panel's "Clear" button.</summary>
    internal static void OnClearAll(TerminalScreenViewModel viewModel)
    {
        var controller = Controller("Clear");
        controller?.ClearQueueFromTerminal();
    }

    private static BuildPlannerController? Controller(string what)
    {
        var controller = BuildPlannerBinding.Controller;
        if (controller == null)
            Log.Write($"  panel: '{what}' pressed before the Build Planner was bound; ignored");

        return controller;
    }

    /// <summary>
    /// Which queue entry an icon stands for.
    ///
    /// The displayed list is built by <see cref="Refresh"/> in queue order, so display index and
    /// queue index are the same — including under the ten-item cap, which only ever truncates the
    /// tail.
    /// </summary>
    private static int IndexOf(TerminalScreenViewModel viewModel, BuildPlannerBlockModel block, string what)
    {
        var blocks = viewModel.BuildPlannerBlocks;
        var index = blocks?.IndexOf(block) ?? -1;

        if (index < 0)
            Log.Write($"  panel: could not place the block to {what} in the displayed list; ignored");

        return index;
    }

    /// <summary>
    /// Find the panel's decorator by the label Keen gave it.
    ///
    /// Searches the logical tree first — it is populated as soon as the XAML is built — then the
    /// visual tree, which only fills in once templates are applied. Neither alone is reliable at
    /// every call site, and the search is cheap next to opening a terminal screen.
    /// </summary>
    private static LayoutTimer? FindPanelDecorator(TerminalScreenView screen)
    {
        return Match(screen.GetLogicalDescendants().OfType<LayoutTimer>())
            ?? Match(screen.GetVisualDescendants().OfType<LayoutTimer>());

        static LayoutTimer? Match(IEnumerable<LayoutTimer> timers)
            => timers.FirstOrDefault(t => t.Label == PanelLabel);
    }
}
