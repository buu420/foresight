using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldNavigationSource(IReadableMemory memory, Action<string> diagnostic)
{
    private readonly Dictionary<string, NavigationTarget> discovered = new(StringComparer.Ordinal);
    private string? scene;
    private string? lastFailure;
    private string? lastInventory;
    private bool storyUnavailable;

    public void Reset() { discovered.Clear(); scene = null; lastInventory = null; }

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
        var storyAnchors = new List<NavigationTarget>();
        var activeIds = new HashSet<string>();
        foreach (var actor in field.Actors)
        {
            if (!actor.IsUsable || !InsideMap(actor) || !actor.IsDrawn || !actor.ClassTagKnown || actor.IsPartyMember || actor.Index == 0 ||
                (actor.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) != 0) continue;
            var description = FieldVisualLabels.Describe(actor);
            // People includes visible characters without a talk action, such as
            // Crono's cat. The native confirm filter only gates interactable objects.
            if (description.Category == NavigationCategory.Objects && !actor.IsActivationCandidate) continue;
            var position = Position(actor.FineX, actor.FineY);
            var id = $"actor:{actor.Index}:{actor.ClassTag}:{actor.VisualIndex}";
            var label = field.SceneIdCoherent ? OpeningStoryTargets.ActorLabel(field.SceneId, actor) : null;
            Add(id, label ?? description.Label, description.Category, position, Approach(position), viewport.Contains(position.X, position.Y));
        }
        foreach (var chest in treasures)
        {
            var position = Position(chest.FineX, chest.FineY);
            Add($"chest:{chest.Index}", "Treasure chest", NavigationCategory.Objects, position,
                Approach(position), TileVisible(chest.FineX / 256, chest.FineY / 256));
        }
        if (field.SceneIdCoherent)
        foreach (var actor in field.Actors)
        {
            // These exact script slots are interaction markers for scenery drawn in
            // the map. Their class 7 intentionally has no sprite. Never promote other
            // hidden actors, and use their live coordinates rather than guide positions.
            if (!InsideMap(actor)) continue;
            var label = EarlyStoryTargets.Landmark(field.SceneId, story, actor, field.Actors);
            if (label is null) continue;
            var position = Position(actor.FineX, actor.FineY);
            var touch = EarlyStoryTargets.IsTouchLandmark(field.SceneId, actor.Index);
            var approaches = touch
                ? At(actor.TileX * 256 + 128, actor.TileY * 256 + 128).Where(p => !graph.IsTerminal(p)).ToArray()
                : Approach(position);
            Add($"landmark:{actor.Index}", label, NavigationCategory.Objects, position, approaches,
                TileVisible(actor.TileX, actor.TileY), storyOnly: touch);
        }
        var exitGroups = new Dictionary<int, List<NavigationPoint>>();
        var exitCellCount = 0;
        var visibleExitCellCount = 0;
        for (var y = 0; y < map.ExitHeight; y++)
        for (var x = 0; x < map.ExitWidth; x++)
        {
            var id = map.ExitCells[y * map.ExitWidth + x];
            if (id >= 128) continue;
            exitCellCount++;
            if (!exitGroups.TryGetValue(id, out var points)) exitGroups[id] = points = [];
            var px = x * 256 + 128; var py = y * 256 + 128;
            points.Add(Position(px, py));
            if (TileVisible(x, y)) visibleExitCellCount++;
        }
        foreach (var (id, points) in exitGroups.OrderBy(entry => entry.Key))
        {
            var key = $"exit:{id}";
            // Visibility discovers the destination. Its connected native footprint
            // supplies usable entry points even when the camera clips the stairs.
            var visible = points.Where(p => TileVisible(p.X / 256, p.Y / 256)).ToArray();
            if (visible.Length == 0)
            {
                activeIds.Add(key);
                if (discovered.TryGetValue(key, out var old)) targets.Add(old with
                {
                    Visible = false, Discovered = true,
                    ApproachPoints = old.ApproachPoints.Where(p => StillExitGoal(p, id)).ToArray(),
                });
                continue;
            }
            var position = visible.OrderBy(Distance).First();
            // Preserve discovered approaches while the exit still owns their cells.
            // Re-ranking a large exit by the moving player evicts a routed goal.
            var retained = discovered.TryGetValue(key, out var knownExit)
                ? knownExit.ApproachPoints.Where(p => StillExitGoal(p, id)).ToArray() : [];
            var footprint = ConnectedExitFootprint(points, visible.Concat(retained));
            var approaches = retained.Concat(footprint.SelectMany(ExitApproach).Distinct().OrderBy(Distance)).Distinct().Take(64)
                .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Layer).ToArray();
            Add(key, OpeningStoryTargets.ExitLabel(field.SceneId, id), NavigationCategory.Exits, position, approaches, true);
        }
        foreach (var id in discovered.Keys.Where(id => !activeIds.Contains(id)).ToArray()) discovered.Remove(id);
        if (field.SceneIdCoherent)
        {
            var available = targets.Concat(storyAnchors).ToArray();
            targets.AddRange(OpeningStoryTargets.Build(field.SceneId, story, available));
            targets.AddRange(EarlyStoryTargets.Build(field.SceneId, story, available, player));
        }
        ReportInventory();
        return new(identity, field.SceneIdCoherent && field.ControlFlag != 0 && field.InputMode == 0 &&
            field.LeadPlayer is { IsUsable: true, IsDrawn: true } && !map.TransitionPending,
            player, targets.AsReadOnly(), graph, 256);

        void ReportInventory()
        {
            var inventory = $"people={targets.Count(t => t.Category == NavigationCategory.People)}; " +
                $"exits={targets.Count(t => t.Category == NavigationCategory.Exits)}; " +
                $"objects={targets.Count(t => t.Category == NavigationCategory.Objects)}; " +
                $"storyEvents={targets.Count(t => t.Category == NavigationCategory.StoryEvents)}; " +
                $"actors={field.Actors.Count}; usable={field.Actors.Count(a => a.IsUsable)}; " +
                $"drawn={field.Actors.Count(a => a.IsDrawn)}; activationCandidates={field.Actors.Count(a => a.IsActivationCandidate)}; " +
                $"exitCells={exitCellCount}; visibleExitCells={visibleExitCellCount}; " +
                $"renderedChests={treasures.Count}; storyPoint={story?.Point.ToString() ?? "unknown"}; " +
                $"motherIntroduced={story?.MotherIntroducedFriend.ToString() ?? "unknown"}";
            if (inventory == lastInventory) return;
            lastInventory = inventory;
            diagnostic($"Navigation inventory: scene={field.SceneId}; {inventory}; viewport={viewport}.");
            // Raw field facts stay in the diagnostic log. Bound the detail and only
            // emit it when inventory changes, including the first read of an area.
            diagnostic("Navigation actor facts: " + string.Join(" | ", field.Actors.Take(32).Select(a =>
                $"id={a.Index},class=0x{a.ClassTag:X},visual=0x{a.VisualIndex:X},draw=0x{a.DrawMode:X}," +
                $"loaded={a.LoadedFlag},usable={a.IsUsable},party={a.IsPartyMember}," +
                $"flag152={a.ActivationEnabled},field20={a.ActivationBinding},pos=({a.FineX},{a.FineY})")) + ".");
        }

        // Scripts park retired actors at tile FF,FF without necessarily clearing
        // their draw mode. Such actors must not keep their last discovered position.
        bool InsideMap(FieldActorSnapshot actor) => actor.FineX >= 0 && actor.FineY >= 0 &&
            actor.FineX / 256 < map.Width && actor.FineY / 256 < map.Height;

        NavigationPoint Position(int x, int y)
        {
            if (x < 0 || y < 0 || x / 256 >= map.Width || y / 256 >= map.Height) return new(x, y, player.Layer);
            var index = y / 256 * map.Width + x / 256;
            var region = FieldCollisionRules.Region(map.CollisionShapes[index], map.CollisionLayers[index], x, y);
            return new(x, y, region.NeutralMask == 0 && region.Layer != 0 ? region.Layer : player.Layer);
        }
        bool TileVisible(int x, int y) => viewport.Intersects(x * 256, y * 256, (x + 1) * 256, (y + 1) * 256);
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
            IReadOnlyList<NavigationPoint> approaches, bool visible, bool storyOnly = false)
        {
            activeIds.Add(id);
            var output = storyOnly ? storyAnchors : targets;
            if (visible)
            {
                var target = new NavigationTarget(id, label, category, position, approaches, true, true);
                discovered[id] = target; output.Add(target);
            }
            else if (discovered.TryGetValue(id, out var known)) output.Add(known with { Visible = false, Discovered = true });
        }
    }

    private static IEnumerable<NavigationPoint> ConnectedExitFootprint(IEnumerable<NavigationPoint> cells,
        IEnumerable<NavigationPoint> discoveredPoints)
    {
        // A disconnected reuse of an exit id must be seen separately. Flood only
        // through cardinally adjacent cells belonging to this same native exit.
        var remaining = cells.ToDictionary(p => (p.X / 256, p.Y / 256));
        var pending = new Queue<(int X, int Y)>(discoveredPoints.Select(p => (p.X / 256, p.Y / 256)).Distinct());
        while (pending.TryDequeue(out var tile))
        {
            if (!remaining.Remove(tile, out var point)) continue;
            yield return point;
            pending.Enqueue((tile.X - 1, tile.Y));
            pending.Enqueue((tile.X + 1, tile.Y));
            pending.Enqueue((tile.X, tile.Y - 1));
            pending.Enqueue((tile.X, tile.Y + 1));
        }
    }

    private NavigationFrame? Failed(string message)
    {
        if (lastFailure != message) diagnostic($"Navigation capture unavailable: {message}");
        lastFailure = message;
        return null;
    }
}
