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
}
