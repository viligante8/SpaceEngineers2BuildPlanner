using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Simulation.GameSystems.BuildPlanners;
using Keen.Game2.Simulation.GameSystems.Player;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks.Production.ItemConverters;
using Keen.Game2.Simulation.WorldObjects.Tools;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;

namespace BuildPlanner;

/// <summary>
/// A runtime inspector, standing in for the debugger a plugin cannot attach.
///
/// **Why this exists.** A game restart plus world load costs about five minutes, so the expensive
/// resource is not log space, it is *round trips*. Two things reduce them:
///
/// 1. A dump key (SHIFT + ALT + CTRL + the withdraw key) snapshots live state on demand, so a
///    question can be asked at the exact moment something looks wrong rather than being guessed
///    at beforehand.
/// 2. A **file-driven query list**, so asking a NEW question needs no rebuild and no restart — the
///    game holds the DLL open, so a code change would force a relaunch, whereas a data change does
///    not. Write paths into
///        %APPDATA%\SpaceEngineers2\BuildPlanner\queries.txt
///    and press the dump key again; the file is re-read every time.
///
/// Query syntax is one path per line, from a named root:
///     tool                      the captured IntegrityToolUIComponent
///     tool._playerData          field/property navigation, any depth
///     planner                   the local player's BuildPlannerData
///     planner.PlannedBlocks[0]  index into any list or array
///     tool._model !2            trailing !N expands N levels deep (default 1)
/// Lines starting with # are comments.
///
/// Indexing and depth are what make this a substitute for stepping in a debugger: finding something
/// interesting and digging into it costs a new line in a text file, not a rebuild and a relaunch.
/// </summary>
internal static class Diagnostics
{
    /// <summary>Everything worth knowing, plus whatever queries.txt asks for.</summary>
    internal static void DumpAll(BuildPlannerQueue queue, Session? clientSession, Session? serverSession)
    {
        Log.Write("=== DIAGNOSTIC DUMP ===");

        try
        {
            Log.Write($"  sessions: client={(clientSession != null)} server={(serverSession != null)}");
            DumpQueue(queue);
            DumpConverters(serverSession);
            DumpTool();
            DumpPlannerChain(clientSession);
            DumpServerPlanner(clientSession, serverSession);
            RunFileQueries(clientSession);
        }
        catch (Exception ex)
        {
            Log.Error("diagnostic dump failed", ex);
        }

        Log.Write("=== END DUMP ===");
    }

    private static void DumpQueue(BuildPlannerQueue queue)
    {
        Log.Write($"  queue: {queue.Count} block(s)");
        foreach (var block in queue.Blocks)
            Log.Write($"    - {block?.UIData?.Name.ToString() ?? "?"}");
    }

    /// <summary>
    /// The item converters SHIFT-produce would reach, and what they are doing.
    ///
    /// Produce is the one action whose result is invisible at the moment of pressing the key: the
    /// components appear minutes later, somewhere else. When it looks like nothing happened, the
    /// question is always one of "was a converter found", "does it have a recipe for this", "was its
    /// queue full" — so the dump answers all three without another game restart.
    /// </summary>
    private static void DumpConverters(Session? serverSession)
    {
        if (serverSession == null)
        {
            Log.Write("  converters: no server session");
            return;
        }

        try
        {
            var character = PlayerAccess.GetLocalCharacter(serverSession);
            var provider = character?.FirstOrDefault<IInteractedEntityProvider>();
            var target = provider?.InteractedEntity;

            if (target == null)
            {
                Log.Write("  converters: not aiming at anything (aim at a block to see its reach)");
                return;
            }

            var converters = InventorySources.CollectConvertersFrom(target);
            Log.Write($"  converters reachable from '{target.DebugName}': {converters.Count}");

            foreach (var converter in converters)
            {
                if (converter == null) continue;

                var definition = converter.Definition;
                Log.Write($"    - {ComponentProduction.Describe(converter)}" +
                          $" crafting={converter.Crafting}" +
                          $" maxQueue={definition?.MaxQueueSize.ToString() ?? "?"}");

                DumpConverterQueue(converter);
            }
        }
        catch (Exception ex)
        {
            Log.Error("dumping converters failed", ex);
        }
    }

    /// <summary>
    /// One converter's current and queued recipes, including who asked for each.
    /// </summary>
    /// <remarks>
    /// **<c>Requester</c> is the point of this method.** It is documented as "Entity of another
    /// ItemConverterComponent that requested production of this item", so it is the direct evidence
    /// for the sub-component cascade: a recipe this mod enqueued has no requester, while one the
    /// engine delegated downward names the assembler that asked for it. Without this line, "did the
    /// cascade happen" could only be guessed at from queue depths.
    ///
    /// The current recipe lives in a separate <c>ConversionQueueItem</c> data slot from the pending
    /// <c>ConversionQueueData.ConversionQueue</c>, so both are read — a converter partway through the
    /// only thing it was given would otherwise look idle with an empty queue.
    /// </remarks>
    private static void DumpConverterQueue(ItemConverterComponent converter)
    {
        try
        {
            if (converter.Data.TryGet<ItemConverterComponent.ConversionQueueItem>(out var current))
                Log.Write($"        current: {DescribeQueueItem(current)}");
            else
                Log.Write("        current: (idle)");
        }
        catch (Exception ex)
        {
            Log.Write($"        current: unreadable ({ex.GetType().Name})");
        }

        try
        {
            var queue = converter.Data.Get<ItemConverterComponent.ConversionQueueData>().ConversionQueue;
            if (queue.Count == 0)
            {
                Log.Write("        queued: (none)");
                return;
            }

            for (var i = 0; i < queue.Count; i++)
                Log.Write($"        queued[{i}]: {DescribeQueueItem(queue[i])}");
        }
        catch (Exception ex)
        {
            // Not an error: the queue data may not be reserved on this block yet.
            Log.Write($"        queued: unreadable ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// A queue entry as "&lt;what it makes&gt; x&lt;runs&gt; requestedBy=&lt;who&gt;".
    /// </summary>
    /// <remarks>
    /// Recipes are named by their first output rather than <c>DisplayNameOverride</c>, which is null
    /// on every vanilla recipe inspected — the output item is what the player and this mod both care
    /// about anyway.
    /// </remarks>
    private static string DescribeQueueItem(ItemConverterComponent.ConversionQueueItem item)
    {
        var recipe = item.Recipe.Value;
        var name = "?";

        if (recipe != null && !recipe.Outputs.IsDefaultOrEmpty)
        {
            var output = recipe.Outputs[0];
            name = $"{(int)output.Amount}x {output.Item?.DisplayName.ToString() ?? "?"}";
        }

        var requester = item.Requester?.Value;
        var by = requester == null ? "you (top-level)" : requester.DebugName;

        return $"{name} x{item.Times} run(s) requestedBy={by}";
    }

    private static void DumpTool()
    {
        var tool = IntegrityToolAccess.Captured;
        if (tool == null)
        {
            Log.Write("  tool: NOT captured (no welder equipped since load?)");
            return;
        }

        Log.Write($"  tool: {tool.GetType().Name} #{Id(tool)}");
        DumpMembers(tool, "    tool.", depth: 0);
    }

    /// <summary>
    /// The whole mirror chain, each link identified.
    ///
    /// The identity hash matters more than the values: if the terminal holds a *different*
    /// BuildPlannerData instance than the one written to, the write is fine and the target is wrong —
    /// which is the single most likely explanation for a queue that never appears on screen.
    /// </summary>
    private static void DumpPlannerChain(Session? clientSession)
    {
        var tool = IntegrityToolAccess.Captured;
        if (tool == null || clientSession == null)
        {
            Log.Write("  planner chain: unavailable (no tool or no client session)");
            return;
        }

        var perPlayer = GetMember(tool, "_playerData") as IPerPlayerData;
        Log.Write($"  planner chain: IPerPlayerData={Describe(perPlayer)}");

        var players = clientSession.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
        if (players == null)
        {
            Log.Write("  planner chain: no ClientPlayersSessionComponent");
            return;
        }

        Log.Write($"  planner chain: LocalPlayerIdentity={players.LocalPlayerIdentity}");

        if (perPlayer == null) return;

        try
        {
            var data = perPlayer.GetPerPlayerData<BuildPlannerData>(players.LocalPlayerIdentity);
            Log.Write($"  planner chain: BuildPlannerData={Describe(data)}");

            if (data?.PlannedBlocks != null)
            {
                Log.Write($"  planner chain: PlannedBlocks has {data.PlannedBlocks.Count} entry(ies)");
                foreach (var b in data.PlannedBlocks)
                    Log.Write($"    - {b?.UIData?.Name.ToString() ?? "?"}");
            }

            // Every identity, in case the local one is not the one the UI reads.
            foreach (var id in perPlayer.GetIdentityIds())
            {
                var other = perPlayer.GetPerPlayerData<BuildPlannerData>(id);
                Log.Write($"    identity {id}: BuildPlannerData={Describe(other)}" +
                          $" planned={other?.PlannedBlocks?.Count.ToString() ?? "-"}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("dumping the planner chain failed", ex);
        }
    }

    /// <summary>
    /// The SERVER-side planner data, alongside the client copy dumped above.
    ///
    /// These are different objects and the difference is the whole story: the mirror writes the
    /// server instance, and the client instance only changes if the write raised OnPropertyChanged.
    /// Seeing both counts side by side shows immediately whether replication happened.
    /// </summary>
    private static void DumpServerPlanner(Session? clientSession, Session? serverSession)
    {
        try
        {
            var data = EngineQueueMirror.Resolve(clientSession, serverSession);
            Log.Write($"  SERVER planner: BuildPlannerData={Describe(data)}");

            if (data?.PlannedBlocks == null) return;

            Log.Write($"  SERVER planner: PlannedBlocks has {data.PlannedBlocks.Count} entry(ies)" +
                      "   <- compare with the client count above; a mismatch means replication did not fire");

            foreach (var b in data.PlannedBlocks)
                Log.Write($"    - {b?.UIData?.Name.ToString() ?? "?"}");
        }
        catch (Exception ex)
        {
            Log.Error("dumping the server planner failed", ex);
        }
    }


    /// <summary>
    /// Run whatever queries.txt asks for. Re-read every dump, so new questions cost no rebuild.
    /// </summary>
    private static void RunFileQueries(Session? clientSession)
    {
        try
        {
            var dir = Path.GetDirectoryName(Log.PathForDiagnostics);
            if (dir == null) return;

            var file = Path.Combine(dir, "queries.txt");
            if (!File.Exists(file))
            {
                Log.Write($"  queries: none ({file} does not exist - create it to ask for more)");
                return;
            }

            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                try
                {
                    var depth = 1;
                    var expression = line;

                    var bang = line.LastIndexOf('!');
                    if (bang > 0 && int.TryParse(line[(bang + 1)..].Trim(), out var parsed))
                    {
                        depth = Math.Clamp(parsed, 1, 4);
                        expression = line[..bang].Trim();
                    }

                    Log.Write($"  query '{expression}' (depth {depth}):");
                    var value = Evaluate(expression, clientSession);
                    Log.Write($"    = {Describe(value)}");
                    if (value != null) DumpMembers(value, "      ", depth - 1);
                }
                catch (Exception ex)
                {
                    Log.Write($"    ! failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("running file queries failed", ex);
        }
    }

    /// <summary>Resolve a dotted path from a named root.</summary>
    private static object? Evaluate(string path, Session? clientSession)
    {
        var parts = path.Split('.');
        object? current = null;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            int? index = null;

            // Trailing [n] indexes into the member's value.
            var open = part.IndexOf('[');
            if (open > 0 && part.EndsWith("]"))
            {
                if (int.TryParse(part[(open + 1)..^1], out var parsedIndex)) index = parsedIndex;
                part = part[..open];
            }

            current = i == 0 ? Root(part, clientSession) : (current == null ? null : GetMember(current, part));
            if (current == null) return null;

            if (index.HasValue) current = ElementAt(current, index.Value);
        }

        return current;
    }

    /// <summary>Index into a list, array, or any enumerable.</summary>
    private static object? ElementAt(object collection, int index)
    {
        if (collection is IList list)
            return index >= 0 && index < list.Count ? list[index] : null;

        if (collection is IEnumerable enumerable)
        {
            var i = 0;
            foreach (var item in enumerable)
                if (i++ == index) return item;
        }

        return null;
    }

    private static object? Root(string name, Session? clientSession)
    {
        switch (name.ToLowerInvariant())
        {
            case "tool": return IntegrityToolAccess.Captured;
            case "clientsession": return clientSession;
            case "planner":
            {
                var tool = IntegrityToolAccess.Captured;
                var perPlayer = tool == null ? null : GetMember(tool, "_playerData") as IPerPlayerData;
                var players = clientSession?.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
                if (perPlayer == null || players == null) return null;
                return perPlayer.GetPerPlayerData<BuildPlannerData>(players.LocalPlayerIdentity);
            }
            default: throw new ArgumentException($"unknown root '{name}' (try tool, planner, clientsession)");
        }
    }

    /// <summary>Field or property by name, public or not, anywhere up the type hierarchy.</summary>
    private static object? GetMember(object instance, string name)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.DeclaredOnly;

        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(instance);

            var property = type.GetProperty(name, Flags);
            if (property != null) return property.GetValue(instance);
        }

        return null;
    }

    /// <summary>List an object's fields and properties with their values, one level deep.</summary>
    private static void DumpMembers(object instance, string prefix, int depth)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.DeclaredOnly;

        try
        {
            for (var type = instance.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(Flags))
                {
                    object? value;
                    try { value = field.GetValue(instance); }
                    catch (Exception ex) { value = $"<threw {ex.GetType().Name}>"; }

                    Log.Write($"{prefix}{field.Name} = {Describe(value)}");

                    // Recurse into engine objects only. Following primitives, strings and framework
                    // types explodes the log without adding anything.
                    if (depth > 0 && value != null && IsWorthExpanding(value))
                        DumpMembers(value, prefix + "  ", depth - 1);
                }

                // Collections are usually the interesting thing, so list their contents.
                if (depth > 0 && instance is IEnumerable items and not string)
                {
                    var n = 0;
                    foreach (var item in items)
                    {
                        Log.Write($"{prefix}[{n}] = {Describe(item)}");
                        if (item != null && IsWorthExpanding(item)) DumpMembers(item, prefix + "  ", depth - 1);
                        if (++n >= 20) { Log.Write($"{prefix}... (truncated at 20)"); break; }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("dumping members failed", ex);
        }
    }

    /// <summary>
    /// Whether recursing into a value tells us anything. Engine types yes; primitives, strings and
    /// BCL plumbing no.
    /// </summary>
    private static bool IsWorthExpanding(object value)
    {
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string) return false;

        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("Keen") || value is IEnumerable;
    }

    /// <summary>Type, identity and a value preview — enough to compare two references.</summary>
    private static string Describe(object? value)
    {
        if (value == null) return "null";

        try
        {
            var type = value.GetType();

            if (value is string s) return $"\"{s}\"";
            if (type.IsPrimitive || type.IsEnum) return $"{value} ({type.Name})";

            if (value is ICollection collection)
                return $"{type.Name} #{Id(value)} count={collection.Count}";

            var text = value.ToString();
            if (text != null && text != type.FullName && text.Length < 120)
                return $"{type.Name} #{Id(value)} \"{text}\"";

            return $"{type.Name} #{Id(value)}";
        }
        catch (Exception ex)
        {
            return $"<describe threw {ex.GetType().Name}>";
        }
    }

    /// <summary>
    /// Reference identity, so two logged references can be compared. This is the whole reason the
    /// dump is useful for the mirror question: same number means same object.
    /// </summary>
    private static int Id(object value) =>
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
}
