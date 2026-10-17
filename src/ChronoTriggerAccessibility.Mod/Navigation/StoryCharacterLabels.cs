using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Recurring story characters by the names the game shows, limited to the
/// installed script slots that speak under that name, after the name was first shown.
/// Evidence: artifacts/research/npc-castle-0356/findings.md and
/// artifacts/research/ocean-palace-0367/claude-entities/findings.md.</summary>
public static class StoryCharacterLabels
{
    public static string? ActorLabel(int scene, FieldActorSnapshot actor, FieldStoryState? state) =>
        OnScreenName(scene, actor, state) ?? (scene, actor.Index, actor.ClassTag & 0x7F, actor.VisualIndex) switch
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

    /// <summary>The Zeal and Ocean Palace names. Each identifies a character the player can
    /// see; by itself it is not a destination, so a slot drawn out of view is not announced
    /// before it is seen or has a native interaction.</summary>
    public static string? OnScreenName(int scene, FieldActorSnapshot actor, FieldStoryState? state) =>
        (scene, actor.Index, actor.ClassTag & 0x7F, actor.VisualIndex) switch
        {
            // load_enemy A1: native monster.txt "Dalton". Atel_0204 (Queen's Chamber) shows
            // MSG03_094 "Dalton: Yes, Your Majesty!" before setting story point 0xA8, and no
            // other script sets it. Each slot below runs "Dalton:" lines itself. Algetty's
            // slot 13 is drawn only while parked for its scene, which names him itself.
            (334, 10, 5 or 6, 0xA1) or (414, 14, 5 or 6, 0xA1) or
            (424, 17, 5 or 6, 0xA1) when state is { Point: >= 0xA8 } => "Dalton",
            // load_person 43 speaking "Elder:". Algetty's cutscene runs KMES1_094 through
            // slot 8 and ends by setting 0xBA. The surviving village's Beds cutscene runs
            // CMES3_017 before setting 0xCD; the plaza follows it.
            (382, 8, 4, 0x43) when state is { Point: >= 0xBA } => "Elder",
            (424, 8, 4, 0x43) or (425, 9, 4, 0x43) when state is { Point: >= 0xCD } => "Elder",
            // load_person 00 speaking "Melchior:". Only his cabin's handler (Atel_0421) sets
            // 0x6C, then continues straight into KMES0_115.
            (382, 10, 4, 0x00) when state is { Point: >= 0x6C } => "Melchior",
            // load_enemy 98/97: native monster.txt "Mune"/"Masa", each speaking under that
            // name. Denadoro (Atel_0188) shows both names before setting 0x60, and Tata's
            // later 0x66 is reachable only from 0x60.
            (404, 9, 5 or 6, 0x98) when state is { Point: >= 0x60 } => "Mune",
            (409, 33, 5 or 6, 0x97) when state is { Point: >= 0x60 } => "Masa",
            // Atel_0204 shows "Queen:" (MSG03_090) and "Prophet:" (MSG03_091) before setting
            // 0xA8. Slot 14's other load, person 0C when local08 is 4, is Belthasar.
            (415, 13, 4, 0x4A) when state is { Point: >= 0xA8 } => "Queen",
            (415, 14, 4, 0x3C) when state is { Point: >= 0xA8 } => "Prophet",
            // Atel_0204 never shows "Schala:". Atel_0205, entered straight after 0xA8, does
            // on every path before Atel_0058 sets 0xAB.
            (415, 12, 4, 0x09) when state is { Point: >= 0xAB } => "Schala",
            // This room follows the Ocean Palace Lavos battle; scene 414 has set 0xC9 by then.
            (423, 20, 4, 0x4A) when state is { Point: >= 0xC9 } => "Queen",
            (423, 22, 5 or 6, 0xCA) when state is { Point: >= 0xC9 } => "Lavos",
            _ => null,
        };
}
