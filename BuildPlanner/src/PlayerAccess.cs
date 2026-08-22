using System;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.Core.Game.Systems;
using Keen.Game2.Client.GameSystems.BlockPlacement.BlockPlacer;
using Keen.Game2.Simulation.GameSystems.BlockPlacement;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Utils;

namespace BuildPlanner;

/// <summary>
/// Locating the local player's character and inventory.
///
/// The traversal mirrors <c>ControlledEntityDebugScreen.GetControlledEntity</c> in Game2.Client:
/// walk the local controller's controlled entities newest-first, and if the player is seated, follow
/// <c>SeatComponent.Pilot</c> to the character rather than stopping at the seat — otherwise a player
/// in a cockpit would resolve to the ship and we would fill the wrong inventory.
/// </summary>
internal static class PlayerAccess
{
    /// <summary>Bound on the parent walk so a malformed hierarchy cannot loop forever.</summary>
    private const int MaxHierarchyDepth = 8;

    internal static Entity? GetLocalCharacter(Session session)
    {
        try
        {
            // The client session has ClientPlayersSessionComponent; the server session has the base
            // PlayersSessionComponent, and Get<T> throws InvalidCastException when asked for the
            // client type there (seen in game). TryGet keeps both sessions usable.
            var players = session?.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
            var controller = players?.LocalPlayerController;
            if (controller == null)
            {
                Log.Write("  debug: no LocalPlayerController (likely the server session)");

                // Server session: there is no local controller, but the character entities are all
                // here. Take the one that owns an inventory - this is the half that actually holds
                // the player's items.
                if (session != null)
                {
                    foreach (var entity in session.GetEntitiesOfType<
                                 Keen.Game2.Simulation.WorldObjects.Characters.CharacterComponent>())
                    {
                        if (entity == null) continue;

                        var inventory = entity.FirstOrDefault<InventoryComponent>();
                        Log.Write($"  debug: server character '{entity.DebugName}'" +
                                  $" components={entity.Components.Length} hasInventory={inventory != null}");

                        if (inventory != null)
                        {
                            LogInventoryContents(entity.DebugName, inventory);
                            return entity;
                        }
                    }
                }

                return null;
            }

            Log.Write($"  debug: controller has {controller.ControlledEntities.Count} controlled entities");

            for (var i = controller.ControlledEntities.Count - 1; i >= 0; i--)
            {
                var (controllable, _) = controller.ControlledEntities[i];
                var entity = controllable?.Entity;
                if (entity == null)
                {
                    Log.Write($"  debug: controlled[{i}] has no entity");
                    continue;
                }

                var hasInventory = entity.FirstOrDefault<InventoryComponent>() != null;
                Log.Write($"  debug: controlled[{i}] '{entity.DebugName}'" +
                          $" components={entity.Components.Length} hasInventory={hasInventory}");

                // Seated: the character is the pilot, not the seat's grid.
                var seat = entity.TryGet<Keen.Game2.Simulation.WorldObjects.CubeBlocks.Pilotable.SeatComponent>();
                if (seat != null)
                {
                    Log.Write($"  debug: controlled[{i}] is a seat; following pilot '{seat.Pilot?.DebugName}'");
                    return seat.Pilot;
                }

                if (entity.Data.Has<Keen.VRage.Core.WorldTransform>())
                {
                    Log.Write($"  debug: selected '{entity.DebugName}' as the character");
                    return entity;
                }

                Log.Write($"  debug: controlled[{i}] has no WorldTransform; skipping");
            }

            Log.Write("  debug: no controlled entity qualified as the character");
        }
        catch (Exception ex)
        {
            Log.Error("GetLocalCharacter failed", ex);
        }

        return null;
    }

    /// <summary>
    /// The character's main inventory — the one bound to the "Inventory" tag slot in
    /// CompositeCharacterServer.def. Consumables and datapad inventories are separate components and
    /// must not receive build components.
    /// </summary>
    internal static InventoryComponent? GetCharacterInventory(Entity? character, Session? session = null)
    {
        if (character == null) return null;

        try
        {
            // Use the engine's own helper rather than looking the component up directly.
            //
            // In-game diagnostics proved the character entity (CompositeCharacterServer, 58
            // components) carries NO InventoryComponent at all — so tag lookups, untagged lookups
            // and a direct component scan all correctly found nothing. The inventory lives further
            // up the entity hierarchy.
            //
            // FirstInventoryAdapterComponent.GetFirstInventory does exactly this: FirstOrDefault<T>
            // on the entity (which resolves interfaces, unlike TryGet), then recurses through
            // ParentData. Note it asserts rather than returning null when nothing is found, hence
            // the manual walk below instead of calling it directly.
            var direct = character.FirstOrDefault<InventoryComponent>();
            if (direct != null) return direct;

            var current = character;
            for (var depth = 0; depth < MaxHierarchyDepth; depth++)
            {
                if (!current.Data.TryGet<ParentData>(out var parentData)) break;

                var parent = parentData.GetEntity(current.Scene);
                if (parent == null) break;

                var inherited = parent.FirstOrDefault<InventoryComponent>();
                if (inherited != null)
                {
                    Log.Write($"  debug: inventory found on parent entity '{parent.DebugName}'");
                    return inherited;
                }

                current = parent;
            }

            // Search DOWN the hierarchy too. HierarchyComponent.Children is public and the character
            // owns a HierarchyComponent, so child entities (equipped tools, attached rigs) are
            // reachable. Confirmed by diagnostics: the character itself and all its ancestors have no
            // InventoryComponent, so if the player has an inventory at all it must hang below.
            var fromChildren = FindInChildren<InventoryComponent>(character, 0);
            if (fromChildren != null)
            {
                Log.Write("  debug: inventory found on a child entity");
                return fromChildren;
            }

            // Nothing in the character's own entity graph has an inventory. Shipping mission code
            // (ItemsInPlayerInventoryProgressTrackerComponent) locates player inventories with
            //     Session.GetEntitiesOfType<CharacterComponent>()  then  entity.All<InventoryComponent>()
            // so ask the session for every character entity and take the one that actually has an
            // inventory. Diagnostics show ours is the server-side composite, which carries none.
            if (session != null)
            {
                var fromSession = FindInventoryAmongCharacters(session, character);
                if (fromSession != null) return fromSession;
            }
            else
            {
                Log.Write("  debug: no session passed; cannot search character entities");
            }

            Log.Write($"  debug: no InventoryComponent on '{DescribeEntity(character)}', its parents, or its children");

            // Full component dump, chunked so nothing is lost to log-line truncation. The filtered
            // summary above hides exactly the component whose real name we need.
            DumpAllComponents(character);

            // The character is a client/server pair in VRAGE3; if we are holding the server half and
            // inventories live on the client half (or vice versa), the counterpart is the answer.
            // Log the hierarchy so the relationship is visible rather than assumed.
            DumpHierarchy(character);
            DumpChildren(character, 0);

            return null;
        }
        catch (Exception ex)
        {
            Log.Error("GetCharacterInventory failed", ex);
            return null;
        }
    }

    /// <summary>
    /// The block the block-placer is currently aligned to - a real block OR a projection.
    ///
    /// BlockPlacerEntityComponent.AlignedBlock is public and is set by BlockPlacementAlignment to a
    /// CubeBlockPlacementTarget or a ProjectionBlockPlacementTarget depending on what is under the
    /// crosshair, so one lookup covers both. Projections are "non-real" blocks with no entity of
    /// their own, which is why they cannot be found with TryGet&lt;CubeBlockComponent&gt;.
    /// </summary>
    internal static BlockPlacementTarget? GetAlignedBlockTarget(Entity? character)
    {
        if (character == null) return null;

        try
        {
            // The character carries BlockPlacerStateProviderComponent, not BlockPlacerEntityComponent
            // (confirmed in game). The placer itself lives elsewhere in the hierarchy, so search the
            // entity and then its parents, the same shape as the inventory lookup.
            var placer = FindInHierarchy<BlockPlacerEntityComponent>(character);
            if (placer == null)
            {
                Log.Write("  debug: no BlockPlacerEntityComponent on character or its parents");
                return null;
            }

            var target = placer.AlignedBlock?.Target;
            if (target != null) return target;

            // AlignedBlock is cleared on every frame the raycast misses, and is only maintained
            // while the block placer is actively aligning. BlockPlacementAlignment keeps a second
            // target, _lastLookedAtBlock, which retains the last block hit instead of clearing �
            // more forgiving when the crosshair drifts off by a pixel.
            var lastLookedAt = GetLastLookedAtTarget(placer);
            if (lastLookedAt != null)
            {
                Log.Write("  debug: using last-looked-at block (AlignedBlock was null)");
                return lastLookedAt;
            }

            Log.Write("  debug: placer present but no aligned or last-looked-at block");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("GetAlignedBlockTarget failed", ex);
            return null;
        }
    }

    /// <summary>
    /// BlockPlacementAlignment._lastLookedAtBlock - the most recent block target, retained rather
    /// than cleared on a miss. Private, so reached by reflection; failure is non-fatal because the
    /// caller already has AlignedBlock as its primary source.
    /// </summary>
    private static BlockPlacementTarget? GetLastLookedAtTarget(BlockPlacerEntityComponent placer)
    {
        try
        {
            var alignment = placer.Alignment;
            if (alignment == null) return null;

            var field = alignment.GetType().GetField(
                "_lastLookedAtBlock",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            var weak = field?.GetValue(alignment) as WeakBlockPlacementTarget;
            return weak?.Target;
        }
        catch (Exception ex)
        {
            Log.Error("GetLastLookedAtTarget failed", ex);
            return null;
        }
    }

    /// <summary>Diagnostic: identify an entity by debug name and the components it carries.</summary>
    internal static string DescribeEntity(Entity entity)
    {
        try
        {
            var name = entity.DebugName ?? "<null>";

            // Only the inventory/placer components matter here, and the full list overflows a log
            // line — the first attempt at this diagnostic was truncated exactly where the answer
            // would have been. Report the relevant ones plus a total count.
            var interesting = new System.Text.StringBuilder();
            var total = 0;

            foreach (var component in entity.Components)
            {
                total++;
                var typeName = component?.GetType().Name;
                if (typeName == null) continue;

                if (typeName.Contains("Inventory") || typeName.Contains("Placer"))
                {
                    if (interesting.Length > 0) interesting.Append(", ");
                    interesting.Append(typeName);
                }
            }

            var summary = interesting.Length > 0 ? interesting.ToString() : "NONE";
            return $"{name} ({total} components) inventory/placer: {summary}";
        }
        catch (Exception ex)
        {
            return $"<describe failed: {ex.Message}>";
        }
    }

    /// <summary>
    /// Find a component on an entity or anywhere up its parent chain.
    ///
    /// Mirrors FirstInventoryAdapterComponent.GetFirstInventory: FirstOrDefault on the entity, then
    /// recurse through ParentData. Uses FirstOrDefault rather than TryGet because the latter matches
    /// only concrete Component types bound to tag slots.
    /// </summary>
    private static T? FindInHierarchy<T>(Entity entity) where T : class
    {
        var current = entity;

        for (var depth = 0; depth < MaxHierarchyDepth && current != null; depth++)
        {
            var found = current.FirstOrDefault<T>();
            if (found != null) return found;

            if (!current.Data.TryGet<ParentData>(out var parentData)) return null;
            current = parentData.GetEntity(current.Scene);
        }

        return null;
    }


    /// <summary>
    /// Log every component on an entity, chunked across several lines.
    ///
    /// The filtered summary hid the answer twice: the first dump was cut off mid-list, and the
    /// second only showed names containing "Inventory" or "Placer" - which is useless when the real
    /// component is called something else entirely.
    /// </summary>
    private static void DumpAllComponents(Entity entity)
    {
        try
        {
            var line = new System.Text.StringBuilder();
            var index = 0;

            foreach (var component in entity.Components)
            {
                var name = component?.GetType().Name ?? "?";
                if (line.Length > 0) line.Append(", ");
                line.Append(name);

                if (++index % ComponentsPerLogLine == 0)
                {
                    Log.Write($"  debug: components[{index - ComponentsPerLogLine}..{index - 1}]: {line}");
                    line.Clear();
                }
            }

            if (line.Length > 0) Log.Write($"  debug: components[tail]: {line}");
        }
        catch (Exception ex)
        {
            Log.Error("DumpAllComponents failed", ex);
        }
    }

    /// <summary>
    /// Log an entity's ancestry, and for each level whether it owns an inventory.
    ///
    /// Answers in one run whether the inventory sits above the character (hierarchy problem), or on
    /// a sibling/counterpart entity (client-vs-server problem) - two very different fixes.
    /// </summary>
    private static void DumpHierarchy(Entity entity)
    {
        try
        {
            var current = entity;

            for (var depth = 0; depth < MaxHierarchyDepth && current != null; depth++)
            {
                var hasInventory = current.FirstOrDefault<InventoryComponent>() != null;
                var componentCount = current.Components.Length;

                Log.Write($"  debug: hierarchy[{depth}] '{current.DebugName}'" +
                          $" components={componentCount} hasInventory={hasInventory}");

                if (!current.Data.TryGet<ParentData>(out var parentData))
                {
                    Log.Write($"  debug: hierarchy[{depth}] has no parent (top of chain)");
                    break;
                }

                current = parentData.GetEntity(current.Scene);
            }
        }
        catch (Exception ex)
        {
            Log.Error("DumpHierarchy failed", ex);
        }
    }

    /// <summary>Components per log line in the full dump - keeps lines readable and untruncated.</summary>
    private const int ComponentsPerLogLine = 10;

    /// <summary>
    /// Search an entity's descendants for a component.
    ///
    /// HierarchyComponent.Children is public; recursion is depth-bounded so a cyclic or very deep
    /// hierarchy cannot hang the game thread.
    /// </summary>
    private static T? FindInChildren<T>(Entity entity, int depth) where T : class
    {
        if (depth >= MaxHierarchyDepth) return null;

        try
        {
            var hierarchy = entity.TryGet<Keen.VRage.Core.Game.Components.HierarchyComponent>();
            var children = hierarchy?.Children;
            if (children == null) return null;

            foreach (var child in children)
            {
                if (child == null) continue;

                var found = child.FirstOrDefault<T>();
                if (found != null)
                {
                    Log.Write($"  debug: found {typeof(T).Name} on child '{child.DebugName}' at depth {depth + 1}");
                    return found;
                }

                var deeper = FindInChildren<T>(child, depth + 1);
                if (deeper != null) return deeper;
            }
        }
        catch (Exception ex)
        {
            Log.Error("FindInChildren failed", ex);
        }

        return null;
    }

    /// <summary>Diagnostic: log an entity's child tree with inventory presence at each node.</summary>
    private static void DumpChildren(Entity entity, int depth)
    {
        if (depth >= MaxHierarchyDepth) return;

        try
        {
            var hierarchy = entity.TryGet<Keen.VRage.Core.Game.Components.HierarchyComponent>();
            var children = hierarchy?.Children;

            if (children == null)
            {
                if (depth == 0) Log.Write("  debug: character has no HierarchyComponent children");
                return;
            }

            foreach (var child in children)
            {
                if (child == null) continue;

                var childInventory = child.FirstOrDefault<InventoryComponent>();
                Log.Write($"  debug: child[depth {depth + 1}] '{child.DebugName}'" +
                          $" components={child.Components.Length} hasInventory={childInventory != null}");

                if (childInventory != null) LogInventoryContents(child.DebugName, childInventory);

                DumpChildren(child, depth + 1);
            }
        }
        catch (Exception ex)
        {
            Log.Error("DumpChildren failed", ex);
        }
    }

    /// <summary>
    /// Log what an inventory holds.
    ///
    /// Lets the player identify their own inventory by putting something distinctive in it, which
    /// beats inferring ownership from entity names and component lists.
    /// </summary>
    internal static void LogInventoryContents(string owner, InventoryComponent inventory)
    {
        try
        {
            var contents = new System.Text.StringBuilder();
            var count = 0;

            foreach (var (stack, _) in inventory.IterateItemsReverse())
            {
                if (stack.Definition == null) continue;

                if (contents.Length > 0) contents.Append(", ");
                contents.Append((int)stack.Amount).Append(" x ").Append(stack.Definition.DisplayName.ToString());

                if (++count >= MaxItemsLogged)
                {
                    contents.Append(", ...");
                    break;
                }
            }

            Log.Write($"  debug: inventory of '{owner}' holds: {(count == 0 ? "EMPTY" : contents.ToString())}");
        }
        catch (Exception ex)
        {
            Log.Error($"LogInventoryContents failed for {owner}", ex);
        }
    }

    /// <summary>Cap on items listed per inventory so a full container cannot flood the log.</summary>
    private const int MaxItemsLogged = 8;

    /// <summary>
    /// Find the inventory by asking the session for every character entity.
    ///
    /// Mirrors ItemsInPlayerInventoryProgressTrackerComponent: GetEntitiesOfType&lt;CharacterComponent&gt;
    /// then All&lt;InventoryComponent&gt;. Needed because the entity the player controls is the
    /// server-side composite, which has no inventory of its own - verified in game across its whole
    /// parent and child graph.
    /// </summary>
    private static InventoryComponent? FindInventoryAmongCharacters(Session session, Entity ourCharacter)
    {
        try
        {
            var examined = 0;
            InventoryComponent? firstFound = null;

            foreach (var entity in session.GetEntitiesOfType<
                         Keen.Game2.Simulation.WorldObjects.Characters.CharacterComponent>())
            {
                if (entity == null) continue;
                examined++;

                var inventory = entity.FirstOrDefault<InventoryComponent>();
                if (inventory == null) continue;

                var isOurs = ReferenceEquals(entity, ourCharacter);
                Log.Write($"  debug: character entity '{entity.DebugName}' HAS an inventory (isOurs={isOurs})");
                LogInventoryContents(entity.DebugName, inventory);

                firstFound ??= inventory;
            }

            Log.Write($"  debug: examined {examined} character entities in the session");
            return firstFound;
        }
        catch (Exception ex)
        {
            Log.Error("FindInventoryAmongCharacters failed", ex);
            return null;
        }
    }

    /// <summary>
    /// The client session's character - the half that carries client-only components such as
    /// BlockPlacerEntityComponent. Distinct from the server character that owns the inventory,
    /// despite both reporting the debug name "CompositeCharacterServer".
    /// </summary>
    internal static Entity? GetClientCharacter(Session? clientSession)
    {
        if (clientSession == null) return null;

        try
        {
            var players = clientSession.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
            var controller = players?.LocalPlayerController;
            if (controller == null) return null;

            for (var i = controller.ControlledEntities.Count - 1; i >= 0; i--)
            {
                var (controllable, _) = controller.ControlledEntities[i];
                var entity = controllable?.Entity;
                if (entity == null) continue;

                var seat = entity.TryGet<Keen.Game2.Simulation.WorldObjects.CubeBlocks.Pilotable.SeatComponent>();
                if (seat != null) return seat.Pilot;

                if (entity.Data.Has<Keen.VRage.Core.WorldTransform>()) return entity;
            }
        }
        catch (Exception ex)
        {
            Log.Error("GetClientCharacter failed", ex);
        }

        return null;
    }
}
