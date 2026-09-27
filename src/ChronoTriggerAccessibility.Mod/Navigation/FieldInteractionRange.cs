using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Where the player has to be standing for the game to let them act on an actor.
///
/// The confirm handler at RVA 17D230 writes the player's own X and Y to engine+13124 and
/// engine+13128, clears the chosen actor at engine+13174 to 0x80, then walks the actor
/// table and dispatches on the player's facing, taken from engine+120D0:
/// 17D4C0 up, 17D610 down, 17D760 left, 17D8B0 right.
///
/// All four routines are the same shape. The gap along the faced axis is formed as a
/// 16-bit subtract, rejected unless its sign puts the actor on the side the player faces
/// (17D4C0 and 17D760 take the negative branch, 17D610 and 17D8B0 the positive one), then
/// its magnitude is tested with ADD ECX,0xFE40 and rejected on borrow, so it must be under
/// 0x1C0. The gap across that axis is made absolute and tested with ADD ECX,0xFF40, so it
/// must be under 0xC0. Whichever actor survives is stored at engine+13174 and run by
/// 17FA20.
///
/// The player picks their own facing, so the side test is always satisfiable for the
/// direction that points at the actor. What remains is a plus shape: either lobe will do.
///
/// Disassembly: artifacts/research/shop-enemies-0325/navigation/confirm-disasm.txt.</summary>
public static class FieldInteractionRange
{
    /// <summary>Limit on the gap along the faced axis, from ADD ECX,0xFE40.</summary>
    public const int Along = 0x1C0;

    /// <summary>Limit on the gap across the faced axis, from ADD ECX,0xFF40.</summary>
    public const int Lateral = 0xC0;

    /// <summary>True when a player standing here could confirm an actor there.</summary>
    public static bool Reaches(int playerX, int playerY, int actorX, int actorY) =>
        ReachesWithin(playerX, playerY, actorX, actorY, 0);

    /// <summary>True when a player anywhere within <paramref name="tolerance"/> of here, on
    /// either axis, could confirm that actor. Guidance stops as soon as the player is inside
    /// the controller's arrival box, so a goal is only honest if the whole of that box is
    /// still in range; otherwise the walk ends and the confirm quietly does nothing. Both
    /// lobes are axis-aligned, so testing the far corner covers every point in the box.</summary>
    public static bool ReachesWithin(int playerX, int playerY, int actorX, int actorY, int tolerance)
    {
        var dx = Math.Abs((long)actorX - playerX) + tolerance;
        var dy = Math.Abs((long)actorY - playerY) + tolerance;
        return (dx < Lateral && dy < Along) || (dy < Lateral && dx < Along);
    }

    /// <summary>The leader's facing at actor+0x60 as the confirm handler uses it. FUN_0057cfd0
    /// and FUN_0057d0c0 pass the leader's word at table base 0x6940 + 0x60 (engine+0x69A0 in
    /// their slot arithmetic) doubled; 17D230 keeps <c>value &amp; 6</c> and switches on
    /// <c>value / 2</c>: 0 calls 17D4C0 (up), 1 17D610 (down), 2 17D760 (left), 3 17D8B0
    /// (right). These are the literals the ActorFacingSet opcodes 0x0F/0x17/0x1B/0x1D store.</summary>
    public static NavigationDirection NativeFacing(int facing) => facing switch
    {
        0 => NavigationDirection.North, 1 => NavigationDirection.South,
        2 => NavigationDirection.West, 3 => NavigationDirection.East,
        _ => NavigationDirection.None,
    };

    /// <summary>Every facing from which Confirm at this exact player position reaches the actor,
    /// the facing along the larger gap first. Unlike <see cref="ReachesWithin"/> this keeps the
    /// handler's side test: 17D4C0 and 17D760 require the 16-bit actor-minus-player gap to be
    /// negative (actor above / left), 17D610 and 17D8B0 non-negative. All four read the actor X
    /// as <c>X - 16 * [actor+0x14C]</c> (the collision offset) and Y unchanged. The subtracts
    /// carry the flag register's previous carry, so each bound is held one unit inside the
    /// handler's own: a gap of 1..0x1BF along and at most 0xBF across.</summary>
    public static IReadOnlyList<NavigationDirection> Facings(int playerX, int playerY, int actorX, int actorY,
        int collisionOffsetX = 0) => FacingsWithin(playerX, playerY, actorX, actorY, collisionOffsetX, 0);

    /// <summary>The facings that reach the actor from every point within <paramref name="tolerance"/>
    /// of here on both axes: the controller's arrival box. A route goal is only honest when one
    /// facing survives the whole box, because walking stops anywhere inside it.</summary>
    public static IReadOnlyList<NavigationDirection> FacingsWithin(int playerX, int playerY, int actorX, int actorY,
        int collisionOffsetX, int tolerance)
    {
        var dx = (long)actorX - collisionOffsetX * 16L - playerX;
        var dy = (long)actorY - playerY;
        var found = new List<(NavigationDirection Direction, long Along)>();
        if (Math.Abs(dx) + tolerance < Lateral && dy + tolerance <= -1 && dy - tolerance > -Along) found.Add((NavigationDirection.North, -dy));
        if (Math.Abs(dx) + tolerance < Lateral && dy - tolerance >= 1 && dy + tolerance < Along) found.Add((NavigationDirection.South, dy));
        if (Math.Abs(dy) + tolerance < Lateral && dx + tolerance <= -1 && dx - tolerance > -Along) found.Add((NavigationDirection.West, -dx));
        if (Math.Abs(dy) + tolerance < Lateral && dx - tolerance >= 1 && dx + tolerance < Along) found.Add((NavigationDirection.East, dx));
        return found.OrderByDescending(f => f.Along).Select(f => f.Direction).ToArray();
    }

    /// <summary>The carry each facing routine starts with, as the 17D230 loop leaves it. For class
    /// byte 7 the loop's last flag write is <c>class + 0xF9</c>, which carries (7 + 0xF9 = 0x100).
    /// For any other class it is the slot compare <c>slot*2 + ~leader*2 + 1</c>, which carries
    /// exactly when the slot is above the leader's.</summary>
    public static int ScanCarry(FieldActorSnapshot actor, int leaderIndex) =>
        (actor.ClassTag & 0xFF) == 7 || actor.Index > leaderIndex ? 1 : 0;

    /// <summary>Exact acceptance of 17D4C0/17D610/17D760/17D8B0 for a given incoming carry C.
    /// Along the faced axis each routine forms <c>actor + ~player + C</c> (16 bits): up/left need
    /// it negative, then accept <c>player - actor - C &lt; 0x1C0</c>; down/right need it non-negative
    /// and below 0x1C0. The accepted add leaves carry 0, so across the axis the gap
    /// <c>actor - player - 1</c> (or its complement when negative) must be under 0xC0:
    /// actor - player in [-191, 192]. With gap d = actor - player along the axis:
    /// up/left accept d in [-447, 0] (C=0) or [-448, -1] (C=1); down/right [1, 448] or [0, 447].
    /// With a tolerance, <paramref name="anyPoint"/> asks whether some point of the box is
    /// accepted (a competitor that might take Confirm); otherwise every point must be.</summary>
    public static bool NativeAccepts(int playerX, int playerY, int actorX, int actorY, int collisionOffsetX,
        NavigationDirection facing, int carry, int tolerance = 0, bool anyPoint = false)
    {
        if (facing is not (NavigationDirection.North or NavigationDirection.South or NavigationDirection.West or NavigationDirection.East))
            return false;
        var dx = (long)actorX - collisionOffsetX * 16L - playerX;
        var dy = (long)actorY - playerY;
        var (along, across) = facing is NavigationDirection.North or NavigationDirection.South ? (dy, dx) : (dx, dy);
        var (low, high) = facing is NavigationDirection.North or NavigationDirection.West
            ? carry == 0 ? (-447L, 0L) : (-448L, -1L)
            : carry == 0 ? (1L, 448L) : (0L, 447L);
        return anyPoint
            ? along + tolerance >= low && along - tolerance <= high && across + tolerance >= -191 && across - tolerance <= 192
            : along - tolerance >= low && along + tolerance <= high && across - tolerance >= -191 && across + tolerance <= 192;
    }

    /// <summary>True when this actor, with its own scan carry, could take a Confirm with this facing
    /// from some point within <paramref name="tolerance"/> of here (outward, exact bounds).</summary>
    public static bool MayReach(int playerX, int playerY, FieldActorSnapshot actor, int leaderIndex, int tolerance,
        NavigationDirection facing) =>
        NativeAccepts(playerX, playerY, actor.FineX, actor.FineY, actor.CollisionOffsetX, facing,
            ScanCarry(actor, leaderIndex), tolerance, anyPoint: true);

    /// <summary>The loop at 17D230 that follows the facing dispatch: it starts at slot
    /// <c>[script + 0x12000] - 1</c> (the capture's own actor count), steps down to slot 1 (slot 0
    /// is never tested) and stops at the first actor that its facing routine accepts, using each
    /// slot's exact incoming carry (<see cref="ScanCarry"/>). A slot is tested only when byte
    /// actor+0x20 (<see cref="FieldActorSnapshot.ActivationBinding"/>) is non-zero and the class
    /// byte at +0x40 has no sign bit; the leader's own slot (engine+1229C) is skipped unless its
    /// class byte is 7. Returns the slot that would take Confirm, or -1 (none, or no leader).
    /// Only the fields this loop reads are used; script eligibility beyond them is not guessed.</summary>
    public static int ConfirmWinner(IEnumerable<FieldActorSnapshot> actors, int leaderIndex, int playerX, int playerY,
        NavigationDirection facing)
    {
        if (leaderIndex < 0) return -1;
        foreach (var actor in actors.Where(a => a.Index >= 1).OrderByDescending(a => a.Index))
            if (ConfirmScanned(actor, leaderIndex) &&
                NativeAccepts(playerX, playerY, actor.FineX, actor.FineY, actor.CollisionOffsetX, facing, ScanCarry(actor, leaderIndex)))
                return actor.Index;
        return -1;
    }

    /// <summary>The per-slot gate of the 17D230 loop, before its geometric test.</summary>
    public static bool ConfirmScanned(FieldActorSnapshot actor, int leaderIndex) =>
        (actor.ActivationBinding & 0xFF) != 0 && (actor.ClassTag & 0x80) == 0 &&
        (actor.Index != leaderIndex || (actor.ClassTag & 0xFF) == 7);

    /// <summary>Treasure chests and non-chest pickups are opened by 179940, which the confirm path
    /// (17D0C0) calls after its actor check. It dispatches on the same leader facing (actor+0x60):
    /// 179C30 (up) tests the leader's tile row - 1 and then row - 2, 179CA0 (down) row + 1, 179CF0
    /// (left) column - 1 and 179D40 (right) column + 1, using the leader's tile bytes at +0x80 and
    /// +0x8C (FineX &gt;&gt; 8, FineY &gt;&gt; 8). 179D90 then reads the chest grid at engine+E44
    /// (width E50, height E54); a byte under 0x80 is a treasure. The up probe stops at the first
    /// treasure it finds, so row - 2 only counts when row - 1 holds none.</summary>
    public static IReadOnlyList<NavigationDirection> TreasureFacings(int playerX, int playerY, int chestTileX, int chestTileY,
        Func<int, int, bool> treasureAt)
    {
        var (tx, ty) = (playerX >> 8, playerY >> 8);
        if (tx == chestTileX && (chestTileY == ty - 1 || chestTileY == ty - 2 && !treasureAt(tx, ty - 1)))
            return [NavigationDirection.North];
        if (tx == chestTileX && chestTileY == ty + 1) return [NavigationDirection.South];
        if (ty == chestTileY && chestTileX == tx - 1) return [NavigationDirection.West];
        if (ty == chestTileY && chestTileX == tx + 1) return [NavigationDirection.East];
        return [];
    }
}
