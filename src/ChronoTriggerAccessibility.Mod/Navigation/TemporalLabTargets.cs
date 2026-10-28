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

    public static Warp? Pad(int scene, FieldActorSnapshot actor, FieldStoryState? story)
    {
        if (scene != 621 || actor.Index is < 8 or > 22 || actor.ClassTag != 4 || actor.VisualIndex != 231 ||
            !actor.IsUsable || !actor.IsDrawn || actor.IsPartyMember || (actor.LoadedFlag & 1) == 0 ||
            !actor.ScriptCallsEnabled || !actor.ScriptProcessingEnabled || story is not { Point: >= 3 }) return null;
        var expected = actor.Index <= 16 ? landings[actor.Index - 8] : (8, 29, "entrance corridor");
        var action = GameNavigationCatalog.ActorInfo(scene, actor)?.Actions.FirstOrDefault(a => a.Touch && a.Kind == "Warp" &&
            a.Destination == 621 && a.ArrivalX == expected.Item1 && a.ArrivalY == expected.Item2 && a.Available(story));
        // E1 flags02/03 use the tile's centre/bottom;0C/0D use its left/mid
        // alignment. The catalogue retains tiles but not these entry flags.
        var centred = actor.Index is 8 or 10 or 13 or 15;
        return action is null ? null : new(action, $"Warp pad {(char)('A' + actor.Index - 8)} to {expected.Item3}", expected.Item3,
            new(action.ArrivalX * 256 + (centred ? 128 : 0), action.ArrivalY * 256 + (centred ? 255 : 127), 1));
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
