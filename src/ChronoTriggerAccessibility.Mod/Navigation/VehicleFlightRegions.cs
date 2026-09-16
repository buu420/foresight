using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Dactyl flight components. A walkable passage can be too narrow for
/// its four-chip flight test, so landing search must stay in the current component.</summary>
internal sealed class VehicleFlightRegions
{
    private byte[]? map, properties;
    private readonly int[] regions = new int[192 * 128];

    public void Update(byte[] currentMap, byte[] currentProperties, VehicleFlightGraph graph)
    {
        if (map is not null && properties is not null && map.AsSpan().SequenceEqual(currentMap) &&
            properties.AsSpan().SequenceEqual(currentProperties)) return;
        map = (byte[])currentMap.Clone(); properties = (byte[])currentProperties.Clone();
        Array.Fill(regions, -1);
        var nextRegion = 0;
        var queue = new Queue<int>();
        for (var i = 0; i < regions.Length; i++)
        {
            if (regions[i] != -1 || !graph.CanFly(Point(i))) continue;
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
            if (regions[i] != -1 || !graph.CanFly(Point(i))) return;
            regions[i] = nextRegion; queue.Enqueue(i);
        }
    }

    public bool Connected(NavigationPoint from, NavigationPoint to)
    {
        var start = Region(from);
        return start >= 0 && start == Region(to);
    }
    private int Region(NavigationPoint p) => p.Layer == 1 && p.X is >= 0 and < VehicleFlightGraph.Width &&
        p.Y is >= 0 and < VehicleFlightGraph.Height ? regions[p.Y / VehicleFlightGraph.Step * 192 + p.X / VehicleFlightGraph.Step] : -1;
    private static NavigationPoint Point(int i) => new(i % 192 * VehicleFlightGraph.Step, i / 192 * VehicleFlightGraph.Step, 1);
}
