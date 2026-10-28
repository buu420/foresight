using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Atel0621's drawn E7 pads and E1 same-room landings. Names describe
/// map locations, not concealed rewards or the result of a battle.</summary>
internal static class TemporalLabTargets
{
    internal sealed record Warp(GameNavigationCatalog.Action Action, string Label, string Landing, NavigationPoint Arrival);
    private static readonly (int X, int Y, string Name)[] landings =
    [
        (16, 23, "western junction"), (22, 17, "northwestern junction"),
        (26, 16, "central corridor"), (22, 8, "upper western corridor"),
        (57, 16, "eastern junction"), (47, 6, "upper eastern corridor"),
        (43, 20, "eastern corridor"), (37, 28, "lower eastern corridor"),
        (32, 28, "lower central corridor"),
    ];
    // Atel0621 init positions, idle return PCs (B2-1), and E1 entry flags.
    private static readonly (int X, int Y, int Pc, int Flags)[] identities =
    [
        (1288,4608,0x04A4,2), (3336,2304,0x04D5,13), (2312,3072,0x0506,3),
        (4360,3072,0x0531,12), (9480,2304,0x055C,14), (13064,4352,0x0587,3),
        (14088,5376,0x05B2,12), (12040,4352,0x05DD,3), (14344,7424,0x0608,14),
        (5640,7424,0x0633,14), (9480,5376,0x065E,14), (6664,2304,0x0673,14),
        (15112,1792,0x0688,14), (10496,4352,0x069D,14), (6664,6400,0x06B2,14),
    ];

    public static Warp? Pad(int scene, FieldActorSnapshot actor, FieldStoryState? story, IReadOnlyList<FieldActorSnapshot> actors)
    {
        if (scene != 621 || !Idle(actor, true, 3) || story is not { Point: >= 3 }) return null;
        // Pads18..22 request actor17 fn3 at priority4. The visible caller is
        // insufficient proof when that shared native handler cannot be called.
        if (actor.Index >= 18 && !actors.Any(a => a.Index == 17 && Idle(a, false, 5))) return null;
        var expected = actor.Index <= 16 ? landings[actor.Index - 8] : (8, 29, "entrance corridor");
        var action = GameNavigationCatalog.ActorInfo(scene, actor)?.Actions.FirstOrDefault(a => a.Touch && a.Kind == "Warp" &&
            a.Destination == 621 && a.ArrivalX == expected.Item1 && a.ArrivalY == expected.Item2 && a.Available(story));
        // Native16A7D0: mask04 selects X fraction0 instead of80; mask08 selects
        // Y fraction7F instead ofFF. The catalogue omits these E1 flags.
        var flags = identities[actor.Index - 8].Flags;
        return action is null ? null : new(action, $"Warp pad {(char)('A' + actor.Index - 8)} to {expected.Item3}", expected.Item3,
            new(action.ArrivalX * 256 + ((flags & 4) == 0 ? 128 : 0), action.ArrivalY * 256 + ((flags & 8) == 0 ? 255 : 127), 1));
    }

    private static bool Idle(FieldActorSnapshot actor, bool drawn, int minimumPriority)
    {
        if (actor.Index is < 8 or > 22 || actor.ClassTag != 4 || actor.VisualIndex != 231 || !actor.IsUsable ||
            drawn && !actor.IsDrawn || actor.IsPartyMember || (actor.LoadedFlag & 1) == 0 ||
            !actor.ScriptCallsEnabled || !actor.ScriptProcessingEnabled || actor.ScriptPriority is not { } priority ||
            priority < minimumPriority || priority > 7) return false;
        var expected = identities[actor.Index - 8];
        return actor.FineX == expected.X && actor.FineY == expected.Y && actor.ScriptAddress == expected.Pc;
    }

    public static bool ConfirmDoor(int scene, FieldActorSnapshot actor) => scene == 621 && actor.Index == 23 && actor.ClassTag == 7;
    public static int? DoorExit(int scene, FieldActorSnapshot actor) => actor.ClassTag == 7 &&
        (scene, actor.Index) is (617, 11) ? 1 : null;
    public static string? DoorLabel(int scene, FieldActorSnapshot actor) => DoorExit(scene, actor) is null && !ConfirmDoor(scene, actor) ? null :
        scene == 617 ? "Warp-maze entrance door" : "Security chamber door";
    public static string? ExitRestriction(int scene, int exit, FieldStoryState? story) =>
        scene == 621 && exit == 1 && story?.Local(30) == 0
            ? "The security chamber door is closed. Choose Security chamber door under Interactable Objects and press Confirm." : null;
    public static string? AreaName(int scene) => scene switch
    {
        617 => "Temporal Research Lab, conveyor room", 621 => "Temporal Research Lab, warp maze",
        622 => "Temporal Research Lab, security chamber", _ => null,
    };
}
