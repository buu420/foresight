using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Visible path connections between physical layers 1 and 2. A layer-3
/// tile alone is insufficient: native walking must reach both layers nearby.</summary>
public sealed class FieldLevelCrossings
{
    private int scene = -1, width, height, exitWidth, exitHeight;
    private byte[] shapes = [], flags = [], layers = [], exits = [];
    private IReadOnlyList<NavigationTarget> targets = [];

    public IReadOnlyList<NavigationTarget> Find(int currentScene, FieldMapSnapshot map)
    {
        if (currentScene is not (142 or 144 or 145)) return [];
        if (scene == currentScene && width == map.Width && height == map.Height &&
            exitWidth == map.ExitWidth && exitHeight == map.ExitHeight && exits.AsSpan().SequenceEqual(map.ExitCells) &&
            shapes.AsSpan().SequenceEqual(map.CollisionShapes) && flags.AsSpan().SequenceEqual(map.TerrainFlags) &&
            layers.AsSpan().SequenceEqual(map.CollisionLayers)) return targets;
        scene = currentScene; width = map.Width; height = map.Height;
        exitWidth = map.ExitWidth; exitHeight = map.ExitHeight; exits = map.ExitCells.ToArray();
        shapes = map.CollisionShapes.ToArray(); flags = map.TerrainFlags.ToArray(); layers = map.CollisionLayers.ToArray();
        var graph = new FieldNavigationGraph(map);
        var cells = new Dictionary<(int X, int Y), NavigationPoint>();
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            if ((layers[y * width + x] & 3) != 3 && (layers[y * width + x] >> 3 & 3) != 3) continue;
            foreach (var oy in new[] { 128, 64, 192 })
            foreach (var ox in new[] { 128, 64, 192 })
            {
                if (cells.ContainsKey((x, y))) continue;
                if (graph.TryPosition(x * 256 + ox, y * 256 + oy, 3, out var p) && p.Layer == 3 && JoinsBothLayers(graph, p))
                    cells.Add((x, y), p);
            }
        }
        var result = new List<NavigationTarget>();
        while (cells.Count != 0)
        {
            var first = cells.Keys.OrderBy(p => p.Y).ThenBy(p => p.X).First();
            var queue = new Queue<(int X, int Y)>(); queue.Enqueue(first);
            var points = new List<NavigationPoint>();
            while (queue.TryDequeue(out var cell))
            {
                if (!cells.Remove(cell, out var point)) continue;
                points.Add(point);
                foreach (var adjacent in new[] { (cell.X - 1, cell.Y), (cell.X + 1, cell.Y), (cell.X, cell.Y - 1), (cell.X, cell.Y + 1) })
                    if (cells.ContainsKey(adjacent)) queue.Enqueue(adjacent);
            }
            var suffix = result.Count < 26 ? ((char)('A' + result.Count)).ToString() : (result.Count + 1).ToString();
            result.Add(new($"level-crossing:{first.X}:{first.Y}", $"Level crossing {suffix}", NavigationCategory.Objects,
                points[0], points.Take(64).ToArray(), false, false)
                { GuideAvailable = true, Instruction = "Path between map levels." });
        }
        return targets = result.AsReadOnly();
    }

    private static bool JoinsBothLayers(FieldNavigationGraph graph, NavigationPoint start)
    {
        var seen = new HashSet<NavigationPoint> { start };
        var queue = new Queue<NavigationPoint>(); queue.Enqueue(start);
        var found = 0;
        while (queue.TryDequeue(out var p) && seen.Count <= 1024)
        {
            if (p.Layer is 1 or 2) found |= 1 << p.Layer;
            if (found == 6) return true;
            if (graph.IsTerminal(p)) continue;
            foreach (var next in graph.Neighbours(p))
                if (Math.Abs((long)next.X - start.X) <= 512 && Math.Abs((long)next.Y - start.Y) <= 512 && seen.Add(next))
                    queue.Enqueue(next);
        }
        return false;
    }
}
