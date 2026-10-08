using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Plan to an automatic tile-copy contact on the current floor, then
/// replan from a fresh capture. The preview proves which contact opens a route;
/// it never becomes movement authority and never writes to the game.</summary>
public sealed class FieldTerrainGraph(FieldMapSnapshot map, IReadOnlyList<FieldActorSnapshot> actors,
    FieldStoryState story, GameNavigationCatalog.Scene scene,
    IReadOnlyList<(string Id, int Left, int Top, int Right, int Bottom)> terminals,
    FieldActorCollisionRules? collisions = null,
    IReadOnlyList<(int Actor, IReadOnlyList<NavigationPoint> Goals)>? touchGoals = null,
    IReadOnlyList<NavigationPoint>? selectedGoals = null) : IStagedNavigationGraph
{
    private readonly INavigationGraph live = Wrap(map, terminals, collisions, touchGoals).ForGoals(selectedGoals ?? []);
    public INavigationGraph ForGoals(IReadOnlyList<NavigationPoint> goals) =>
        new FieldTerrainGraph(map, actors, story, scene, terminals, collisions, touchGoals, goals);
    public static INavigationGraph Create(FieldMapSnapshot map, IReadOnlyList<FieldActorSnapshot> actors,
        FieldStoryState story, GameNavigationCatalog.Scene scene,
        IReadOnlyList<(string Id, int Left, int Top, int Right, int Bottom)> terminals,
        FieldActorCollisionRules? collisions = null,
        IReadOnlyList<(int Actor, IReadOnlyList<NavigationPoint> Goals)>? touchGoals = null) =>
        actors.Any(a => a.ClassTag == 7 && a.IsUsable && !a.IsPartyMember && a.ScriptCallsEnabled &&
            scene.Actors.Any(m => m.Matches(a) && m.Actions.Any(c => c.Touch && c.Kind == "Terrain" &&
                c.Copy is { } copy && ActionAvailable(scene.Id, a.Index, c, story) && HasChanges(map, copy))))
            ? new FieldTerrainGraph(map, actors, story, scene, terminals, collisions, touchGoals) : Wrap(map, terminals, collisions, touchGoals);
    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => live.Neighbours(point);
    public bool IsTerminal(NavigationPoint point) => live.IsTerminal(point);
    public bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => live.IsSameTerminal(point, goal);

    public NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> goals, int maximumVisited)
    {
        // Only native class-7 touch handlers, with their current guards and call
        // gates. Confirm switches and hidden sprites are not automatic passages.
        var candidates = actors.Where(a => a.ClassTag == 7 && a.IsUsable && !a.IsPartyMember && a.ScriptCallsEnabled &&
            a.FineX >= 0 && a.FineY >= 0 && a.TileX < map.Width && a.TileY < map.Height)
            .Select(a => (Actor: a, Actions: scene.Actors.FirstOrDefault(m => m.Matches(a))?.Actions
                .Where(c => c.Touch && c.Kind == "Terrain" && c.Copy is not null && ActionAvailable(scene.Id, a.Index, c, story)).ToArray() ?? []))
            .Where(c => c.Actions.Length != 0)
            .OrderBy(c => Math.Abs((long)c.Actor.FineX - start.X) + Math.Abs((long)c.Actor.FineY - start.Y)).Take(16);
        foreach (var (actor, actions) in candidates)
        {
            // Each copy must independently establish the continuation; unrelated
            // possible script branches must never be combined into invented floor.
            foreach (var action in actions)
            {
                var preview = Preview(map, action.Copy!);
                if (preview is null) continue;
                var nativeTouch = scene.Id == 464 && actor.Index is 24 or 25;
                // The End of Time route from the pillar platform needs the stair
                // contact before the door contact. Preview only the remaining
                // audited contacts, removing the actor just previewed so this
                // proof is finite. Movement still uses the first live leg only.
                INavigationGraph after = nativeTouch
                    ? new FieldTerrainGraph(preview, actors.Where(a => a.Index is 24 or 25 && a.Index != actor.Index).ToArray(),
                        story, scene, terminals, collisions, touchGoals)
                    : Wrap(preview, terminals, collisions, touchGoals);
                foreach (var contact in Contacts(actor, start.Layer, scene.Id).Concat(nativeTouch ? EdgeContacts(actor, start.Layer) : []))
                {
                    if (live.IsTerminal(contact) || !action.AcceptsPosition(contact.X, contact.Y, NavigationUnits.LocalStep / 8)) continue;
                    var approach = NavigationPathfinder.Search(live, start, [contact], maximumVisited);
                    if (approach.Route is null) continue;
                    var continuation = NavigationPathfinder.Search(after, contact, goals, maximumVisited);
                    if (continuation.Route is not null)
                        return approach with
                        {
                            IntermediateId = $"landmark:{actor.Index}",
                            IntermediateContact = nativeTouch
                                ? new(actor.FineX - actor.CollisionOffsetX * 16, actor.FineY, start.Layer) : null,
                        };
                }
            }
        }
        return new(null, false);
    }

    internal static bool ActionAvailable(int scene, int actor, GameNavigationCatalog.Action action, FieldStoryState story) =>
        action.Guards.All(guard => scene == 464 && actor == 25 && action.Touch && action.Kind == "Terrain" &&
            guard is { Source: "Local", Index: 11, Operation: 0, Value: 0, Expected: true }
                // Atel0283's main loop copies the lead's facing into local0B.
                // The northward finish below enforces this native condition;
                // facing elsewhere while planning does not lock the passage.
                ? story.Local(11) is >= 0 and <= 3 : guard.Allows(story));

    internal static IEnumerable<NavigationPoint> Contacts(FieldActorSnapshot actor, int layer, int scene = 0)
    {
        var x = (actor.FineX + 32) / 64 * 64;
        var y = (actor.FineY + 32) / 64 * 64;
        // 178980 tests the leading probe against +/-160 native units. Up's
        // probe is 112 above the foot; horizontal probes are 64 above it.
        yield return new(x, y + 128, layer);
        if (scene == 464 && actor.Index == 25) yield break; // Native door requires facing up.
        yield return new(x, y - 64, layer);
        yield return new(x - 128, y + 64, layer);
        yield return new(x + 128, y + 64, layer);
    }

    private static IEnumerable<NavigationPoint> EdgeContacts(FieldActorSnapshot actor, int layer)
    {
        // End of Time's closed stair/door floor keeps the old central contacts
        // inaccessible. 178980 accepts the leading movement probe before the
        // terrain check: +/-112 in X, -64 in Y for side probes, -112 for up.
        // Reach a real floor point outside the closed tiles, then keep moving
        // toward the live marker until its script opens the captured terrain.
        var x = (actor.FineX - actor.CollisionOffsetX * 16 + 32) / 64 * 64;
        var y = (actor.FineY + 32) / 64 * 64;
        if (actor.Index == 25)
        {
            yield return new(x, y + 256, layer);
            yield break;
        }
        yield return new(x - 256, y - 64, layer);
        yield return new(x + 256, y - 64, layer);
        yield return new(x, y + 256, layer);
        yield return new(x, y - 128, layer);
    }

    private static INavigationGraph Wrap(FieldMapSnapshot value,
        IReadOnlyList<(string Id, int Left, int Top, int Right, int Bottom)> passages,
        FieldActorCollisionRules? collisions,
        IReadOnlyList<(int Actor, IReadOnlyList<NavigationPoint> Goals)>? touchGoals) =>
        passages.Count == 0 ? new FieldNavigationGraph(value, collisions, touchGoals)
            : new ScriptPassageGraph(new FieldNavigationGraph(value, collisions, touchGoals), passages);

    internal static FieldMapSnapshot? Preview(FieldMapSnapshot source, GameNavigationCatalog.TileCopy copy)
    {
        if (!HasChanges(source, copy)) return null;
        var width = copy.Right - copy.Left + 1; var height = copy.Bottom - copy.Top + 1;
        var result = source with { CollisionShapes = (byte[])source.CollisionShapes.Clone(),
            TerrainFlags = (byte[])source.TerrainFlags.Clone(), CollisionLayers = (byte[])source.CollisionLayers.Clone() };
        Copy(source.CollisionShapes, result.CollisionShapes, 8);
        Copy(source.TerrainFlags, result.TerrainFlags, 16);
        Copy(source.CollisionLayers, result.CollisionLayers, 32);
        return result;

        void Copy(byte[] from, byte[] to, int flag)
        {
            if ((copy.Flags & flag) == 0) return;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = from[(copy.Top + y) * source.Width + copy.Left + x];
                var index = (copy.Y + y) * source.Width + copy.X + x;
                to[index] = value;
            }
        }
    }

    private static bool HasChanges(FieldMapSnapshot source, GameNavigationCatalog.TileCopy copy)
    {
        var width = copy.Right - copy.Left + 1; var height = copy.Bottom - copy.Top + 1;
        if (width <= 0 || height <= 0 || copy.Left < 0 || copy.Top < 0 || copy.X < 0 || copy.Y < 0 ||
            copy.Right >= source.Width || copy.Bottom >= source.Height || copy.X + width > source.Width ||
            copy.Y + height > source.Height || (copy.Flags & 0x38) == 0) return false;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var from = (copy.Top + y) * source.Width + copy.Left + x;
            var to = (copy.Y + y) * source.Width + copy.X + x;
            if ((copy.Flags & 8) != 0 && source.CollisionShapes[from] != source.CollisionShapes[to] ||
                (copy.Flags & 16) != 0 && source.TerrainFlags[from] != source.TerrainFlags[to] ||
                (copy.Flags & 32) != 0 && source.CollisionLayers[from] != source.CollisionLayers[to]) return true;
        }
        return false;
    }
}
