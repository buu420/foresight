using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Plan to an automatic tile-copy contact on the current floor, then
/// replan from a fresh capture. The preview proves which contact opens a route;
/// it never becomes movement authority and never writes to the game.</summary>
public sealed class FieldTerrainGraph(FieldMapSnapshot map, IReadOnlyList<FieldActorSnapshot> actors,
    FieldStoryState story, GameNavigationCatalog.Scene scene,
    IReadOnlyList<(string Id, int Left, int Top, int Right, int Bottom)> terminals) : IStagedNavigationGraph
{
    private readonly INavigationGraph live = Wrap(map, terminals);
    public static INavigationGraph Create(FieldMapSnapshot map, IReadOnlyList<FieldActorSnapshot> actors,
        FieldStoryState story, GameNavigationCatalog.Scene scene,
        IReadOnlyList<(string Id, int Left, int Top, int Right, int Bottom)> terminals) =>
        actors.Any(a => a.ClassTag == 7 && a.IsUsable && !a.IsPartyMember && a.ScriptCallsEnabled &&
            scene.Actors.Any(m => m.Matches(a) && m.Actions.Any(c => c.Touch && c.Kind == "Terrain" &&
                c.Copy is { } copy && c.Available(story) && HasChanges(map, copy))))
            ? new FieldTerrainGraph(map, actors, story, scene, terminals) : Wrap(map, terminals);
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
                .Where(c => c.Touch && c.Kind == "Terrain" && c.Copy is not null && c.Available(story)).ToArray() ?? []))
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
                var after = Wrap(preview, terminals);
                foreach (var contact in Contacts(actor, start.Layer))
                {
                    if (live.IsTerminal(contact)) continue;
                    var approach = NavigationPathfinder.Search(live, start, [contact], maximumVisited);
                    if (approach.Route is null) continue;
                    var continuation = NavigationPathfinder.Search(after, contact, goals, maximumVisited);
                    if (continuation.Route is not null)
                        return approach with { IntermediateId = $"landmark:{actor.Index}" };
                }
            }
        }
        return new(null, false);
    }

    internal static IEnumerable<NavigationPoint> Contacts(FieldActorSnapshot actor, int layer)
    {
        var x = (actor.FineX + 32) / 64 * 64;
        var y = (actor.FineY + 32) / 64 * 64;
        // 178980 tests the leading probe against +/-160 native units. Up's
        // probe is 112 above the foot; horizontal probes are 64 above it.
        yield return new(x, y + 128, layer);
        yield return new(x, y - 64, layer);
        yield return new(x - 128, y + 64, layer);
        yield return new(x + 128, y + 64, layer);
    }

    private static INavigationGraph Wrap(FieldMapSnapshot value,
        IReadOnlyList<(string Id, int Left, int Top, int Right, int Bottom)> passages) =>
        passages.Count == 0 ? new FieldNavigationGraph(value) : new ScriptPassageGraph(new FieldNavigationGraph(value), passages);

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
