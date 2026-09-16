using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>264530/264690 and290B70/290E60, verified by offline execution of
/// the installed x86. Coordinates and directional extents are native pixels.</summary>
public static class VehicleContactGeometry
{
    public static bool Overlaps(int x, int y, VehicleContactShape moving, WorldPixelPoint other, VehicleContactShape fixedShape)
    {
        var dx = other.X - x; var dy = other.Y - y;
        return (dx == 0 || (dx > 0 ? dx < moving.Right + fixedShape.Left : -dx < moving.Left + fixedShape.Right)) &&
            (dy == 0 || (dy > 0 ? dy < moving.Bottom + fixedShape.Top : -dy < moving.Top + fixedShape.Bottom));
    }

    public static IEnumerable<NavigationPoint> BoardingApproaches(WorldPixelPoint vehicle, VehicleContactShape party,
        VehicleContactShape parked, NavigationPoint lattice)
    {
        var phaseX = lattice.X / 16 % 8; var phaseY = lattice.Y / 16 % 8;
        var left = Math.Max(0, vehicle.X - parked.Left - party.Right);
        var right = Math.Min(1535, vehicle.X + parked.Right + party.Left);
        var top = Math.Max(0, vehicle.Y - parked.Top - party.Bottom);
        var bottom = Math.Min(1023, vehicle.Y + parked.Bottom + party.Top);
        for (var y = top + (phaseY - top % 8 + 8) % 8; y <= bottom; y += 8)
            for (var x = left + (phaseX - left % 8 + 8) % 8; x <= right; x += 8)
                if (Overlaps(x, y, party, vehicle, parked)) yield return new(x * 16, y * 16, 1);
    }

    /// <summary>The name/prompt appears on direct overlap or if one native8px
    /// segment in any of eight directions would overlap. 290B70 skips the extra
    /// neighbour checks when the current X or Y is below16.</summary>
    public static bool BlackOmenContact(int x, int y, VehicleContactShape epoch, VehicleBlackOmen omen)
    {
        var other = new WorldPixelPoint(omen.X, omen.Y);
        if (Overlaps(x, y, epoch, other, omen.Shape)) return true;
        if (x < 16 || y < 16) return false;
        for (var dx = -8; dx <= 8; dx += 8)
            for (var dy = -8; dy <= 8; dy += 8)
                if ((dx != 0 || dy != 0) && Overlaps(x + dx, y + dy, epoch, other, omen.Shape)) return true;
        return false;
    }

    public static IEnumerable<NavigationPoint> BlackOmenApproaches(VehicleBlackOmen omen, VehicleContactShape epoch,
        NavigationPoint lattice)
    {
        var phaseX = lattice.X / 16 % 8; var phaseY = lattice.Y / 16 % 8;
        var left = Math.Max(0, omen.X - omen.Shape.Left - epoch.Right - 8);
        var right = Math.Min(1535, omen.X + omen.Shape.Right + epoch.Left + 8);
        var top = Math.Max(0, omen.Y - omen.Shape.Top - epoch.Bottom - 8);
        var bottom = Math.Min(1023, omen.Y + omen.Shape.Bottom + epoch.Top + 8);
        for (var y = top + (phaseY - top % 8 + 8) % 8; y <= bottom; y += 8)
            for (var x = left + (phaseX - left % 8 + 8) % 8; x <= right; x += 8)
                if (BlackOmenContact(x, y, epoch, omen)) yield return new(x * 16, y * 16, 1);
    }
}
