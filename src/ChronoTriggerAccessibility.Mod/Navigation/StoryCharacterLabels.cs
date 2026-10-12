using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Recurring story characters by the names the game shows, limited to the
/// installed script slots that speak under that name, after the name was first shown.
/// Evidence: artifacts/research/npc-castle-0356/findings.md.</summary>
public static class StoryCharacterLabels
{
    public static string? ActorLabel(int scene, FieldActorSnapshot actor, FieldStoryState? state) =>
        (scene, actor.Index, actor.ClassTag & 0x7F, actor.VisualIndex) switch
        {
            // load_enemy A6/A7/A8: native monster.txt "Ozzie"/"Ozzie"/"Ozzie the Great".
            // Zenan Bridge first shows his name (KMES0) and ends by setting story point 0x5A.
            (166, 11, 5 or 6, 0xA6) or (167, 8, 5 or 6, 0xA6) or (168, 9, 5 or 6, 0xA6) or
            (170, 10, 5 or 6, 0xA6) or (174, 10, 5 or 6, 0xA7) or (175, 11, 5 or 6, 0xA6) or
            (177, 8, 5 or 6, 0xA8) or (178, 12, 5 or 6, 0xA8) or (179, 9, 5 or 6, 0xA8) or
            (180, 8, 5 or 6, 0xA8) or (181, 8, 5 or 6, 0xA7 or 0xA8) or (183, 8, 5 or 6, 0xA8) or
            (184, 8, 5 or 6, 0xA8) or (205, 9, 5 or 6, 0xA6) when state is { Point: >= 0x5A } => "Ozzie",
            // load_person 06 in prehistory. The Forest Maze talk handler sets 0x7B just before
            // KMES1_002, the first mandatory line that shows "Kino:". Scene 430 slot 17 has no
            // lines of its own: it appears only at story point 0x97, which only Kino's handler
            // in 302 (Atel_0057 actor 9, person 06) sets right after MESK1_099 "Follow Kino!".
            (275, 12, 4, 6) or (279, 8, 4, 6) or (280, 15, 4, 6) or (282, 19, 4, 6) or
            (299, 8, 4, 6) or (301, 14, 4, 6) or (302, 9, 4, 6) or (430, 17, 4, 6)
                when state is { Point: >= 0x7B } => "Kino",
            _ => null,
        };
}
