using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Tyranno Lair doorways named by what a player sees: the exterior's four terraces and
/// each doorway's left-to-right place on them, and where an inner passage comes out. Debug-map
/// names would name rooms (and a boss) the player has not seen, and give several doorways the
/// same name. Exit numbers and arrivals are the installed MapJumpDataTbl records
/// (artifacts/research/tyranno-0358/exit-tables.txt).</summary>
public static class TyrannoLairLabels
{
    // 298: exit -> (terrace, door). Sources (10,8) (20,8) | (34,8) (44,8) | (9,27) (19,27) (29,27)
    // | (9,53) (23,48) (37,53).
    private static readonly string[] Exterior =
    [
        "Northwest terrace, left doorway", "Northwest terrace, right doorway",
        "Northeast terrace, left doorway", "Northeast terrace, right doorway",
        "Middle terrace, left doorway", "Middle terrace, centre doorway", "Middle terrace, right doorway",
        "Southern terrace, left doorway", "Southern terrace, centre doorway", "Southern terrace, right doorway",
    ];

    /// <summary>Null keeps the existing label (unknown exit, or one this helper does not own).</summary>
    public static string? ExitLabel(int scene, int exit, FieldStoryState? story) => (scene, exit) switch
    {
        // 298 actor 0 fn1 copies the open doorway over the drawn gate on 0xED bits 80 / 40.
        (298, 5) => Gated(Exterior[5], story, 0x80),
        (298, 8) => Gated(Exterior[8], story, 0x40),
        (298, >= 0 and <= 9) => Exterior[exit],
        (302, 0) => "Doorway to the northwest terrace",
        (302, 1) => "Doorway to the northeast terrace, west end",  // arrives at 298 (35,9)
        (302, 2) => "Doorway to the northeast terrace, east end",  // arrives at 298 (45,9)
        (302, 4) => "Inner doorway",
        (302, 5) => "Doorway to the middle terrace",
        (302, 6) => "Inner doorway",
        (302, 7) => "Doorway to the southern terrace",
        (303, 0) => "Doorway to the southern terrace",
        (303, 1) => "Doorway to the middle terrace",
        (304, 0) => "North doorway",
        (304, 1) => "Doorway to the middle terrace",
        (305, 0) => "Inner doorway",
        (305, 1) => "Doorway to the northwest terrace",
        (300, 0) => "Doorway to the southern terrace",
        (300, 1) => "North passage",
        _ => null,
    };

    /// <summary>Atel_0167 (303): actors 9, 11, 12, 13, 16 are floor switches, 10 the west skull,
    /// 17 and 18 the wall switches inside the skulls. What each does is learned by using it.</summary>
    public static string? ActorLabel(int scene, int actor) => (scene, actor) switch
    {
        (303, 9 or 11 or 12 or 13 or 16) => "Floor switch",
        (303, 10) => "Skull",
        (303, 17 or 18) => "Wall switch",
        _ => null,
    };

    // Unknown gate state says nothing either way.
    private static string Gated(string label, FieldStoryState? story, int mask) =>
        story?.Global(0xED) is { } gates && (gates & mask) == 0 ? label + ", gated" : label;
}
