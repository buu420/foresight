using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldNavigationSource(IReadableMemory memory, Action<string> diagnostic)
{
    private readonly Dictionary<string, NavigationTarget> discovered = new(StringComparer.Ordinal);
    private string? scene;
    private string? lastFailure;
    private bool storyUnavailable;

    public void Reset() { discovered.Clear(); scene = null; }

    public NavigationFrame? Capture(nint engine)
    {
        if (!FieldNavigationCapture.TryCapture(memory, (nuint)engine, out var field, out var error)) return Failed(error);
        if (!FieldMapCapture.TryCapture(memory, field, out var map, out error)) return Failed(error);
        if (!FieldEnvironmentCapture.TryViewport(memory, field, out var viewport)) return Failed("Native camera bounds are unavailable.");
        if (!FieldEnvironmentCapture.TryTreasures(memory, field, map, out var treasures)) return Failed("Native treasure visibility state is unavailable.");
        lastFailure = null;
        var story = FieldStoryCapture.Capture(memory, field);
        if (story is null && !storyUnavailable) diagnostic("Navigation story context is unavailable; field targets remain usable.");
        storyUnavailable = story is null;
        return Build(field, map, viewport, treasures, story);
    }

    public NavigationFrame Build(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldViewport viewport,
        IReadOnlyList<FieldTreasure> treasures, FieldStoryState? story = null)
    {
        var identity = $"{field.Engine:X8}:{field.SceneId}";
        if (scene != identity)
        {
            Reset(); scene = identity;
            diagnostic($"Navigation field: scene={field.SceneId}; engine=0x{field.Engine:X}; map={map.Width}x{map.Height}; layer={map.PlayerLayer}; actors={field.ActorCount}; viewport={viewport}.");
        }
        var graph = new FieldNavigationGraph(map);
        var player = new NavigationPoint(field.LeadPlayer?.FineX ?? 0, field.LeadPlayer?.FineY ?? 0, map.PlayerLayer);
        var targets = new List<NavigationTarget>();
        var activeIds = new HashSet<string>();
        foreach (var actor in field.Actors)
        {
            if (!actor.IsUsable || !actor.IsDrawn || !actor.IsActivationCandidate || actor.Index == 0) continue;
            var description = FieldVisualLabels.Describe(actor);
            var position = Position(actor.FineX, actor.FineY);
            var id = $"actor:{actor.Index}:{actor.ClassTag}:{actor.VisualIndex}";
            var label = field.SceneIdCoherent ? OpeningStoryTargets.ActorLabel(field.SceneId, actor) : null;
            Add(id, label ?? description.Label, description.Category, position, Approach(position), viewport.Contains(position.X, position.Y));
        }
        foreach (var chest in treasures)
        {
            var position = Position(chest.FineX, chest.FineY);
            Add($"chest:{chest.Index}", "Treasure chest", NavigationCategory.Objects, position,
                Approach(position), viewport.Contains(position.X, position.Y));
        }
        var exitGroups = new Dictionary<int, List<NavigationPoint>>();
        for (var y = 0; y < map.ExitHeight; y++)
        for (var x = 0; x < map.ExitWidth; x++)
        {
            var id = map.ExitCells[y * map.ExitWidth + x];
            if (id >= 128) continue;
            if (!exitGroups.TryGetValue(id, out var points)) exitGroups[id] = points = [];
            // Keep the exit's discovered footprint: an off-screen extension is not a new target.
            var px = x * 256 + 128; var py = y * 256 + 128;
            if (viewport.Contains(px, py)) points.Add(Position(px, py));
        }
        foreach (var (id, points) in exitGroups.OrderBy(entry => entry.Key))
        {
            var key = $"exit:{id}";
            if (points.Count == 0)
            {
                activeIds.Add(key);
                if (discovered.TryGetValue(key, out var old)) targets.Add(old with
                {
                    Visible = false, Discovered = true,
                    ApproachPoints = old.ApproachPoints.Where(p => StillExitGoal(p, id)).ToArray(),
                });
                continue;
            }
            var position = points.OrderBy(Distance).First();
            // Preserve discovered approaches while the exit still owns their cells.
            // Re-ranking a large exit by the moving player evicts a routed goal.
            var retained = discovered.TryGetValue(key, out var knownExit)
                ? knownExit.ApproachPoints.Where(p => StillExitGoal(p, id)) : [];
            var approaches = retained.Concat(points.SelectMany(ExitApproach).Distinct().OrderBy(Distance)).Distinct().Take(64)
                .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Layer).ToArray();
            Add(key, OpeningStoryTargets.ExitLabel(field.SceneId, id), NavigationCategory.Exits, position, approaches, true);
        }
        foreach (var id in discovered.Keys.Where(id => !activeIds.Contains(id)).ToArray()) discovered.Remove(id);
        if (field.SceneIdCoherent) targets.AddRange(OpeningStoryTargets.Build(field.SceneId, story, targets));
        return new(identity, field.SceneIdCoherent && field.ControlFlag != 0 && field.InputMode == 0 &&
            field.LeadPlayer is { IsUsable: true, IsDrawn: true } && !map.TransitionPending,
            player, targets.AsReadOnly(), graph, 256);

        NavigationPoint Position(int x, int y)
        {
            if (x < 0 || y < 0 || x / 256 >= map.Width || y / 256 >= map.Height) return new(x, y, player.Layer);
            var index = y / 256 * map.Width + x / 256;
            var region = FieldCollisionRules.Region(map.CollisionShapes[index], map.CollisionLayers[index], x, y);
            return new(x, y, region.NeutralMask == 0 && region.Layer != 0 ? region.Layer : player.Layer);
        }
        double Distance(NavigationPoint p) => Math.Abs((double)p.X - player.X) + Math.Abs((double)p.Y - player.Y);
        bool StillExitGoal(NavigationPoint p, int id) => graph.ExitAt(p.X, p.Y) == id &&
            graph.TryPosition(p.X, p.Y, p.Layer, out var current) && current == p;
        IEnumerable<NavigationPoint> At(int x, int y)
        {
            for (var layer = 1; layer <= 3; layer++)
                if (graph.TryPosition(x, y, layer, out var point)) yield return point;
        }
        IReadOnlyList<NavigationPoint> Approach(NavigationPoint p)
        {
            var x = (p.X + 32) / 64 * 64; var y = (p.Y + 32) / 64 * 64;
            return new[] { (x - 256, y), (x + 256, y), (x, y - 256), (x, y + 256) }
                .SelectMany(pair => At(pair.Item1, pair.Item2)).Where(point => !graph.IsTerminal(point))
                .Distinct().ToArray();
        }
        IEnumerable<NavigationPoint> ExitApproach(NavigationPoint p)
        {
            // Stay farther inside than the two-pixel arrival radius, so reaching
            // the approach cannot stop walking before the actual exit cell.
            for (var x = p.X / 256 * 256 + 64; x < p.X / 256 * 256 + 256; x += 64)
            for (var y = p.Y / 256 * 256 + 64; y < p.Y / 256 * 256 + 256; y += 64)
                foreach (var point in At(x, y)) yield return point;
        }
        void Add(string id, string label, NavigationCategory category, NavigationPoint position,
            IReadOnlyList<NavigationPoint> approaches, bool visible)
        {
            activeIds.Add(id);
            if (visible)
            {
                var target = new NavigationTarget(id, label, category, position, approaches, true, true);
                discovered[id] = target; targets.Add(target);
            }
            else if (discovered.TryGetValue(id, out var known)) targets.Add(known with { Visible = false, Discovered = true });
        }
    }

    private NavigationFrame? Failed(string message)
    {
        if (lastFailure != message) diagnostic($"Navigation capture unavailable: {message}");
        lastFailure = message;
        return null;
    }
}
