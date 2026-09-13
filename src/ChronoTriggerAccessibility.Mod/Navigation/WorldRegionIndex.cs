using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Connected walking regions of the current native world collision map.
/// Used to keep future-era story objectives on the player's side of the ruins.</summary>
internal sealed class WorldRegionIndex
{
    private byte[]? map, properties;
    private readonly int[] regions = new int[192 * 128];

    public void Update(byte[] currentMap, byte[] currentProperties, WorldNavigationGraph graph)
    {
        if (map is not null && properties is not null && map.AsSpan().SequenceEqual(currentMap) &&
            properties.AsSpan().SequenceEqual(currentProperties)) return;
        map = (byte[])currentMap.Clone(); properties = (byte[])currentProperties.Clone();
        Array.Fill(regions, -1);
        var nextRegion = 0;
        var queue = new Queue<int>();
        for (var i = 0; i < regions.Length; i++)
        {
            if (regions[i] != -1 || !graph.CanStand(Point(i))) continue;
            regions[i] = nextRegion;
            queue.Enqueue(i);
            while (queue.TryDequeue(out var current))
            {
                var x = current % 192; var y = current / 192;
                if (x > 0) Visit(current - 1);
                if (x < 191) Visit(current + 1);
                if (y > 0) Visit(current - 192);
                if (y < 127) Visit(current + 192);
            }
            nextRegion++;
        }

        void Visit(int i)
        {
            if (regions[i] != -1 || !graph.CanStand(Point(i))) return;
            regions[i] = nextRegion; queue.Enqueue(i);
        }
    }

    public bool Connected(NavigationPoint from, NavigationPoint to)
    {
        var start = Region(from);
        return start >= 0 && start == Region(to);
    }

    private int Region(NavigationPoint p) => p.Layer == 1 && p.X is >= 0 and < 24576 && p.Y is >= 0 and < 16384
        ? regions[p.Y / WorldNavigationGraph.Step * 192 + p.X / WorldNavigationGraph.Step] : -1;
    private static NavigationPoint Point(int i) => new(i % 192 * WorldNavigationGraph.Step, i / 192 * WorldNavigationGraph.Step, 1);
}
