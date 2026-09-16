using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Chip property lookup shared by the vehicle graphs and landing rules.
/// Identical to the walking graph: the live 96x64 layer-two map at D+23800 and the
/// 512 packed property bytes at D+25000; chips are 8 pixels, wrapping like 2775D0.</summary>
internal static class WorldChips
{
    public const int PixelsPerChip = 8;

    public static int Property(byte[] map, byte[] properties, int chipX, int chipY)
    {
        chipX = ((chipX % 192) + 192) % 192;
        chipY = ((chipY % 128) + 128) % 128;
        var tile = map[chipY / 2 * 96 + chipX / 2];
        var packed = properties[tile * 2 + (chipY & 1)];
        return (chipX & 1) == 0 ? packed >> 4 : packed & 15;
    }

    public static void Validate(byte[] map, byte[] properties)
    {
        if (map.Length != 96 * 64 || properties.Length != 512)
            throw new ArgumentException("World collision data must contain the complete native map and property table.");
    }
}

/// <summary>Eight-pixel flight lattice in fine units, the same segment length the
/// vehicle tasks move between input reads (268190 targets pos +/- 8 px per axis).
/// Routes never cross the world's wrapping edges: the core route model has no seam
/// legs, so a seam crossing is left to manual flight.</summary>
public abstract class VehicleFlightGraph : INavigationGraph
{
    public const int Step = WorldNavigationGraph.Step;
    public const int Width = 1536 * 16, Height = 1024 * 16;
    private readonly NavigationPoint? committedTarget;
    public NavigationPoint LatticeOrigin { get; }

    protected VehicleFlightGraph(NavigationPoint? latticeOrigin = null, NavigationPoint? committedTarget = null)
    {
        LatticeOrigin = latticeOrigin ?? new NavigationPoint(0, 0, 1);
        this.committedTarget = committedTarget;
    }

    public abstract VehicleKind Kind { get; }

    /// <summary>Whether the vehicle task would accept a segment ending here.</summary>
    public abstract bool CanFly(NavigationPoint point);

    protected static bool InBounds(NavigationPoint point) =>
        point.Layer == 1 && point.X is >= 0 and < Width && point.Y is >= 0 and < Height;

    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
    {
        if (!InBounds(point)) yield break;
        // A vehicle can be placed at any integer pixel, and its eight-pixel steps
        // preserve that phase. During motion the next endpoint is already committed
        // by the native task; never invent an earlier turn onto the origin-zero grid.
        if (point.X % Step != LatticeOrigin.X % Step || point.Y % Step != LatticeOrigin.Y % Step)
        {
            if (committedTarget is { } target && Math.Abs(point.X - target.X) < Step &&
                Math.Abs(point.Y - target.Y) < Step && CanFly(target)) yield return target;
            yield break;
        }
        foreach (var (dx, dy) in new[] { (0, -Step), (0, Step), (-Step, 0), (Step, 0) })
        {
            var next = point with { X = point.X + dx, Y = point.Y + dy };
            if (CanFly(next)) yield return next;
        }
    }
}

/// <summary>The winged Epoch (28E1D0 states 0x13-0x26) applies no terrain test while
/// flying; only bounds matter. Water, mountains and cliffs are all flyable.</summary>
public sealed class EpochFlightGraph(NavigationPoint? latticeOrigin = null, NavigationPoint? committedTarget = null)
    : VehicleFlightGraph(latticeOrigin, committedTarget)
{
    public override VehicleKind Kind => VehicleKind.Epoch;
    public override bool CanFly(NavigationPoint point) => InBounds(point);
}

/// <summary>The Dactyls (28A1E0 state 0x10) refuse a segment when any of the four
/// chips 267E40 samples at the target, columns x-1..x of rows y-1..y with x=X>>3,
/// has both low property bits set.</summary>
public sealed class DactylFlightGraph : VehicleFlightGraph
{
    private readonly byte[] map, properties;

    public DactylFlightGraph(byte[] map, byte[] properties, NavigationPoint? latticeOrigin = null,
        NavigationPoint? committedTarget = null) : base(latticeOrigin, committedTarget)
    {
        WorldChips.Validate(map, properties);
        this.map = map; this.properties = properties;
    }

    public override VehicleKind Kind => VehicleKind.Dactyl;

    public override bool CanFly(NavigationPoint point)
    {
        if (!InBounds(point)) return false;
        var x = point.X / Step; var y = point.Y / Step;
        foreach (var (cx, cy) in new[] { (x - 1, y - 1), (x, y - 1), (x - 1, y), (x, y) })
            if ((WorldChips.Property(map, properties, cx, cy) & 3) == 3) return false;
        return true;
    }
}
