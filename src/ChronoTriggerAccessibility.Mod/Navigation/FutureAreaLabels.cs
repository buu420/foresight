using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Installed PC script identities and map-jump labels. Unlisted records
/// retain the ordinary appearance label; this table supplies no coordinates.</summary>
public static class FutureAreaLabels
{
    public static string? ActorLabel(int scene, FieldActorSnapshot actor, FieldStoryState? state) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) switch
        {
            (210, 8, 4, 26) or (214, 11, 4, 29) => "Merchant",
            (214, 14, 4, 28) => state is { Point: >= 52 } ? "Doan" : "Old man",
            (216, 9, 4, 112) => "Right door console",
            (218, 8, 4, 127) => "Computer display",
            (221, 8, 5, 134) => "Rat statue",
            (221, 12 or 13, 5, 134) => "Rat",
            (344, 8, 4, 26) => "Man in the food stores",
            (223, 13, 4, 2) or (225, 8, 4, 2) => "Johnny",
            (226, 4, 3, 3) => state is { Point: < 60 } ? "Broken robot" : "Robo",
            (226, 2, 3, 1) => "Marle",
            (226, 3, 3, 2) => "Lucca",
            (227, 11, 4, 151) => "Gate",
            (228, 8, 4, 127) => "Entrance security terminal",
            (229, 12, 4, 127) => "Hatch control terminal",
            (230, 17, 4, 127) => "Laser control terminal",
            (231, >= 12 and <= 15, 4, 170) => "Conveyor robot",
            (234, 8, 4, 127) => "Security-code terminal",
            (235, 14, 4, 127) => "Security lock display",
            (259, 8, 4, 127) => "Crane instructions terminal",
            (464, 28, 4, 72) => "Old man under the lamp",
            (465, 10, 5 or 6, >= 224 and <= 229) => "Spekkio",
            _ => null,
        };

    public static string? Landmark(int scene, FieldStoryState? state, FieldActorSnapshot actor)
    {
        if (!actor.IsUsable || actor.ClassTag != 7 || actor.IsPartyMember || !actor.ScriptCallsEnabled) return null;
        // Native Confirm markers for the bike remain available on later visits.
        if (IsBike(scene, actor.Index)) return "Jet bike";
        if (state is not { Point: >= 51 and <= 77 }) return null;
        return (scene, actor.Index, state.Point) switch
        {
            (208, 8, _) or (210, 17, _) or (217, 9, _) => "Sealed door",
            (210, 16, _) or (214, 12, _) or (226, 14, _) => "Enertron",
            (216, 8, _) => "Left door console",
            (217, 8, _) => "Door console",
            (218, 9, _) => "Left computer control",
            (218, 10, _) => "Right computer control",
            (228, 9, < 63) => "Lift to the laboratory",
            (228, 10, < 63) => "Lift to the warehouse",
            (228, 14, _) => "Laboratory and factory sign",
            (229, 8, < 63) or (230, 16, < 63) or (235, 9, < 63) => "Laboratory elevator control",
            (231, 8, < 63) => "Lift to the factory entrance",
            (231, 9, < 63) => "Crane controls",
            (235, 10, < 63) when state.Flag(0x1D0, 1) == true => "Power switch",
            (235, 15, < 63) => "Security passcode console",
            (464, 24, 73) => "Steps toward the pillars of light",
            (465, >= 16 and <= 20, 76) => "Clockwise walking checkpoint",
            _ => null,
        };
    }

    public static bool IsTouchLandmark(int scene, int actor) => (scene, actor) == (464, 24) ||
        scene == 465 && actor is >= 16 and <= 20;

    public static bool IsBike(int scene, int actor) => (scene, actor) is (223, 8) or (225, 9);

    // Atel0283's pillar actors stay at the off-map sentinel. Their loops use
    // player-tile guards instead. The first three lights are part of the map;
    // the other six are drawn by the same A6 flags that guard their regions.
    public static bool IsPillar(int scene, GameNavigationCatalog.Region region) =>
        scene == 464 && region.Kind == "Warp" &&
        FullStoryObjectives.PillarDestinations.TryGetValue(region.Actor, out var destination) &&
        destination == region.Destination;

    public static bool PillarVisible(GameNavigationCatalog.Region region, FieldStoryState? state) =>
        state is not null && region.Available(state with { Point = Math.Max(77, state.Point) });

    public static string PillarInstruction(FieldStoryState state) => state.Point < 77
        ? "Complete the old man's introduction and Spekkio's lesson to use the pillars."
        : "Stand in the light and press Confirm to read its destination.";

    public static string? ExitLabel(int scene, int exit) => (scene, exit) switch
    {
        (208, 0) => "Leave Bangor Dome",
        (208, 1) or (210, 1) or (217, 2) => "Sealed chamber",
        (209, 0) => "Bangor Dome",
        (210, 0) => "Leave Trann Dome",
        (211, 0) => "Trann Dome",
        (212, 0) => "West exit toward Trann Dome",
        (212, 1) => "Eastern half of Site 16",
        (213, 0) => "East exit toward Arris Dome",
        (213, 1) => "Western half of Site 16",
        (214, 0) => "Leave Arris Dome",
        (214, 1) => "Ladder to the basement",
        (215, 0) => "Basement entrance",
        (215, 1) => "Northern basement passage",
        (216, 0) => "Ladder to the living quarters",
        (216, 1) => "Rafters toward the food stores",
        (216, 2) => "Eastern basement passage",
        (217, 0) => "Southern basement passage",
        (217, 1) => "Computer room",
        (218, 0) => "Leave the computer room",
        (219, 0) => "Return to the rafters",
        (219, 1) => "Food stores",
        (220, 0) => "Basement passage",
        (221, 0) => "Basement entrance and consoles",
        (221, 1) => "Chamber before the food stores",
        (344, 0) => "Return toward the rafters",
        (223, 0) => "West exit toward Arris Dome",
        (224, 0) => "Western parking lot",
        (224, 1) => "Eastern parking lot",
        (225, 0) => "East exit toward Proto Dome",
        (225, 1) => "Highway toward the west",
        (226, 0) => "Leave Proto Dome",
        (226, 1) => "Gate chamber",
        (227, 0) => "Proto Dome",
        (228, 0) => "Leave the Derelict Factory",
        (229, 0) => "Ladder down to the lower laboratory",
        (230, 0) => "Ladder up to the upper laboratory",
        (230, 1) => "Ladder down to the power controls",
        (231, 0) => "Western passage to the crane",
        (231, 1) => "Eastern passage to the crane",
        (231, 2) => "Security-code room",
        (231, 3) => "Crane instructions room",
        (231, 4) => "West entrance to the conveyor passage",
        (231, 5) => "East entrance to the conveyor passage",
        (232, 0) => "Western warehouse walkway",
        (232, 1) => "Eastern warehouse walkway",
        (233, 0) => "Western doorway toward the crane",
        (233, 1) => "Eastern doorway to the warehouse",
        (234, 0) or (259, 0) => "Warehouse walkway",
        (235, 0) => "Escape ladder to the lower laboratory",
        (464, 0) => "Room behind the old man",
        (465, 0) => "End of Time platform",
        _ => null,
    };
}
