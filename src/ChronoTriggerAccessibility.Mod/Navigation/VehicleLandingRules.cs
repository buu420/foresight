using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>The native landing tests, in world pixels. Epoch: 28E1D0 state 0x1A
/// (six chips, then 2906E0 against the Dactyls, then 286FE0 era rectangles).
/// Dactyls: 28A1E0 state 7 (six chips, 267DB0, 290590 against the Epoch, 286D10
/// rectangle). Positions must also keep the game's own prompt authoritative: the
/// mod never presses Confirm, it only chooses where to stop.</summary>
public static class VehicleLandingRules
{
    public sealed record TileRectangle(int Left, int Top, int Right, int Bottom)
    {
        /// <summary>Inclusive lower bounds, exclusive upper bounds, in 8-pixel tiles.</summary>
        public bool Contains(int tileX, int tileY) => tileX >= Left && tileX < Right && tileY >= Top && tileY < Bottom;
    }

    /// <summary>286FE0: world 0 x 0x41..0x71 y 0x64..0x7E; world 1 x 0x60..0x6D y 0x76..0x7E;
    /// world 3 x 0..0xD y 0..0x33; world 4 x 0..0x1F y 0x60..0x7E; none elsewhere.</summary>
    public static TileRectangle? EpochForbidden(int world) => world switch
    {
        0 => new(0x41, 0x64, 0x72, 0x7F),
        1 => new(0x60, 0x76, 0x6E, 0x7F),
        3 => new(0x00, 0x00, 0x0E, 0x34),
        4 => new(0x00, 0x60, 0x20, 0x7F),
        _ => null,
    };

    /// <summary>28A1E0 state 7 writes the same 65 000 000 BC rectangle into D+2E002..2E005.</summary>
    public static TileRectangle DactylForbidden { get; } = new(0x00, 0x00, 0x0E, 0x34);

    /// <summary>267E40 plus 291190: columns x-1..x of rows y-1..y+1, x=X>>3, y=Y>>3.
    /// Every property nibble must have bits 0..2 clear (the 0x0707 mask on each pair).</summary>
    public static bool ChipsClear(byte[] map, byte[] properties, int pixelX, int pixelY)
    {
        WorldChips.Validate(map, properties);
        var x = pixelX / WorldChips.PixelsPerChip; var y = pixelY / WorldChips.PixelsPerChip;
        for (var cy = y - 1; cy <= y + 1; cy++)
            for (var cx = x - 1; cx <= x; cx++)
                if ((WorldChips.Property(map, properties, cx, cy) & 7) != 0) return false;
        return true;
    }

    public static bool InsideForbiddenRectangle(VehicleKind kind, int world, int pixelX, int pixelY)
    {
        var rectangle = kind == VehicleKind.Dactyl ? DactylForbidden : EpochForbidden(world);
        return rectangle?.Contains(pixelX / WorldChips.PixelsPerChip, pixelY / WorldChips.PixelsPerChip) == true;
    }

    public static bool ClearOfOtherVehicle(int pixelX, int pixelY, WorldPixelPoint? other,
        VehicleContactShape? movingShape, VehicleContactShape? parkedShape) =>
        other is not { } point || movingShape is not null && parkedShape is not null &&
            !VehicleContactGeometry.Overlaps(pixelX, pixelY, movingShape, point, parkedShape);

    public static bool CanLand(VehicleKind kind, int world, byte[] map, byte[] properties, int pixelX, int pixelY,
        WorldPixelPoint? otherVehicle = null, VehicleContactShape? movingShape = null, VehicleContactShape? parkedShape = null)
    {
        if (pixelX is < 0 or >= 1536 || pixelY is < 0 or >= 1024) return false;
        if (kind == VehicleKind.Dactyl && world != 3) return false;
        // Dactyl267DB0 rejects both upper chips carrying bit2. The stricter six-chip
        // 0707 test already rejects either upper chip carrying that bit.
        return ChipsClear(map, properties, pixelX, pixelY) && !InsideForbiddenRectangle(kind, world, pixelX, pixelY) &&
            ClearOfOtherVehicle(pixelX, pixelY, otherVehicle, movingShape, parkedShape);
    }

    public static bool CanLand(VehicleKind kind, int world, byte[] map, byte[] properties, NavigationPoint fine,
        WorldPixelPoint? otherVehicle = null, VehicleContactShape? movingShape = null, VehicleContactShape? parkedShape = null) =>
        fine.Layer == 1 && fine.X % 16 == 0 && fine.Y % 16 == 0 &&
        CanLand(kind, world, map, properties, fine.X / 16, fine.Y / 16, otherVehicle, movingShape, parkedShape);
}
