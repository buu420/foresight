using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class WorldNavigationGraph : INavigationGraph
{
    public const int Step = 128; // Eight pixels, using the field's 16 units per pixel.
    private readonly byte[] map;
    private readonly byte[] properties;

    public WorldNavigationGraph(byte[] map, byte[] properties)
    {
        if (map.Length != 96 * 64 || properties.Length != 512)
            throw new ArgumentException("World collision data must contain the complete native map and property table.");
        this.map = map;
        this.properties = properties;
    }

    public bool CanStand(NavigationPoint point)
    {
        if (point.Layer != 1 || point.X is < 0 or >= 24576 || point.Y is < 0 or >= 16384) return false;
        var x = point.X / Step; var y = point.Y / Step;
        // 267E40 samples the top pair of the actor's four chip properties.
        // 264C40 masks the resulting word with 0303 before committing a step.
        return ((Property(x - 1, y - 1) | Property(x, y - 1)) & 3) == 0;
    }

    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
    {
        if (point.Layer != 1 || point.X is < 0 or >= 24576 || point.Y is < 0 or >= 16384) yield break;
        // A request can arrive part way through the game's current eight-pixel
        // movement. Join its lattice before offering a turn at a new waypoint.
        if (point.X % Step != 0)
        {
            foreach (var x in new[] { point.X / Step * Step, (point.X / Step + 1) * Step })
                if (CanStand(point with { X = x })) yield return point with { X = x };
            yield break;
        }
        if (point.Y % Step != 0)
        {
            foreach (var y in new[] { point.Y / Step * Step, (point.Y / Step + 1) * Step })
                if (CanStand(point with { Y = y })) yield return point with { Y = y };
            yield break;
        }
        foreach (var (dx, dy) in new[] { (0, -Step), (0, Step), (-Step, 0), (Step, 0) })
        {
            var next = point with { X = point.X + dx, Y = point.Y + dy };
            if (CanStand(next)) yield return next;
        }
    }

    private int Property(int chipX, int chipY)
    {
        // The native sampler wraps the left edge and masks its vertical chip
        // coordinate. Routes themselves stop at the map boundary.
        chipX = (chipX + 192) % 192;
        chipY = (chipY + 128) % 128;
        var tile = map[chipY / 2 * 96 + chipX / 2];
        var packed = properties[tile * 2 + (chipY & 1)];
        return (chipX & 1) == 0 ? packed >> 4 : packed & 15;
    }
}
