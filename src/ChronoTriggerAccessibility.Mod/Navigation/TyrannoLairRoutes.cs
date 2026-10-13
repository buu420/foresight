using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Story 0x98 in the Tyranno Lair. The exterior (298) is four disconnected terraces,
/// and two of its doorways are gates the scripts open later, so matching scenes alone sends
/// the player back outside. Each room picks its native passage from the live gate state.
///
/// Installed scripts (artifacts/research/tyranno-0358/findings.md):
/// 298 actor 0 fn1 opens the exit-5 gate on 0xED bit 80 and the exit-8 gate on bit 40.
/// 303's wall switches toggle those bits (actor 18 east, 17 west); their skulls open on
/// locals 0C/0D (floor switches 13/16) and 08 (actor 10). 304's north markers 9/10 start the
/// battle and set 0xEE bit 01. 300 actor 0 runs the face-off below row 20, sets 0xEE bit 08 and
/// opens exit 1. 299 opens its inner passage on 0x5C bit 10. Room bands are the installed
/// floors' connected sections; they select a native passage, never a walk coordinate.</summary>
public static class TyrannoLairRoutes
{
    private const int StoryPoint = 0x98;
    private const string Continue = "Continue through the Tyranno Lair.";

    public static FullStoryObjective ForRoom(FullStoryObjective objective, int scene, NavigationPoint player,
        FieldStoryState state)
    {
        if (objective.Id != "full:black-tyranno" || state.Point != StoryPoint) return objective;
        var x = player.X / 256;
        var y = player.Y / 256;
        int? Bit(int index, int mask) => state.Global(index) is { } value ? value & mask : null;
        var gates = state.Global(0xED);
        return scene switch
        {
            298 => x is >= 8 and <= 23 && y is >= 8 and <= 13 ? Exit(0)    // northwest terrace
                : x is >= 32 and <= 46 && y is >= 8 and <= 13 ? Exit(2)    // northeast, via 302's top room
                : x is >= 8 and <= 31 && y is >= 25 and <= 34 ? Gate(0x80, open: 5, closed: 6)
                : x is >= 8 and <= 39 && y is >= 46 and <= 61 ? Gate(0x40, open: 8, closed: 9)
                : None(),
            305 => x is >= 11 and <= 40 && y is >= 38 and <= 62 ? Exit(0) : None(),
            302 => x is >= 9 and <= 35 && y is >= 9 and <= 18 ? Exit(0)    // top room, to the northwest terrace
                : x is >= 49 and <= 58 && y is >= 40 and <= 44 ? Exit(2)
                : x is >= 9 and <= 17 && y is >= 55 and <= 62 ? Exit(5)    // to the middle terrace
                : x is >= 28 and <= 36 && y is >= 56 and <= 62 ? Exit(7)   // to the southern terrace
                : x is >= 46 and <= 62 && y is >= 9 and <= 24 ? Exit(8)    // prison section, via 430
                : None(),
            430 => x is >= 9 and <= 36 && y is >= 32 and <= 45 ? Exit(0) : None(),
            299 => x is >= 46 and <= 61 && y is >= 32 and <= 62
                ? Bit(0x5C, 0x10) switch { null or 0 => None(), _ => Exit(2) } : None(),
            303 => y is >= 5 and <= 29 ? x is >= 7 and <= 15 ? Skull(0x40, 0x08, [10], 17, 0)
                : x is >= 26 and <= 32 ? Skull(0x80, 0x0C, [13, 16], 18, 1) : None() : None(),
            304 => x is >= 2 and <= 12 && y is >= 5 and <= 27
                ? Bit(0xEE, 0x01) switch { null => None(), 0 => Actor(9, 10), _ => Exit(0) } : None(),
            300 => x is >= 43 and <= 55 && y is >= 6 and <= 28
                ? Bit(0xEE, 0x08) switch { null => None(), 0 => Actor(0), _ => Exit(1) } : None(),
            _ => objective,
        };

        FullStoryObjective Gate(int mask, int open, int closed) =>
            gates is { } value ? Exit((value & mask) != 0 ? open : closed) : None();

        // A wall switch toggles its gate, so a completed gate is never offered again.
        FullStoryObjective Skull(int mask, int skullLocal, int[] skull, int wallSwitch, int back)
        {
            if (gates is not { } value) return None();
            if ((value & mask) != 0) return Exit(back);
            var opened = skullLocal == 0x0C ? new[] { state.Local(0x0C), state.Local(0x0D) } : [state.Local(skullLocal)];
            if (opened.Any(o => o is null)) return None();
            return opened.Any(o => o == 1) ? Actor(wallSwitch) : Actor(skull);
        }

        FullStoryObjective Exit(int id) => objective with
        {
            Goals = [new(scene, [], [$"exit:{id}"])], Instruction = Continue,
        };
        FullStoryObjective Actor(params int[] ids) => objective with
        {
            Goals = [new(scene, ids, [])], Instruction = Continue,
        };
        // Unknown native state or a pose outside the proven floors: promise nothing.
        FullStoryObjective None() => objective with { Goals = [], Instruction = Continue };
    }
}
