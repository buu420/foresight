using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>
/// The classic optional quests, authored against the installed PC event packets.
/// A quest has one current action, while the native catalogue supplies its position,
/// activation conditions and route. Persistent handoff flags take precedence over
/// inventory: handing an item in must never restart its collection step.
/// See docs/classic-optional-objectives.md and artifacts/research/full-story-0323/optional.
/// </summary>
public static class ClassicOptionalObjectives
{
    private const int Flight = 0xD2;
    private const int EndOfTime = 464;
    private static readonly HashSet<int> DesertScenes = [57, 158, 159, 160, 161, 401, 424];
    private static readonly HashSet<int> CyrusScenes = [58, 59, 61, 62, 65, 66, 67, 68, 69, 70, 73, 185, 186, 187, 188, 189, 190];
    private static readonly HashSet<int> SunScenes = [4, 50, 51, 53, 54, 64, 154, 155, 156, 157, 191, 251, 253, 254, 255, 327];
    private static readonly HashSet<int> GenoScenes = [126, 127, 128, 248, 249, 250, 256, 257, 258, 267, 268, 269];
    private static readonly HashSet<int> RainbowScenes = [21, 22, 23, 24, 25, 26, 62, 63, 109, 110, 111, 120, 121, 188, 195, 196, 197, 296, 297, 438, 440, 441, 468, 480, 486, 487, 488, 490];
    private static readonly HashSet<int> OzzieScenes = [177, 178, 179, 180, 181, 182, 183, 184];
    private static readonly HashSet<int> RevivalScenes = [1, 2, 5, 6, 7, 8, 241, 242, 243, 244, 245, 246, 247, 260, 262, 263, 264, 265, 429, 434, 495];
    private static readonly HashSet<int> OmenScenes = [96, 97, 98, 99, 100, 101, 107, 308, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322, 323, 324, 325, 326, 422, 449, 450, 451];

    private static FullStoryObjective Actor(string id, string label, int scene, int actor,
        string? instruction = null, NavigationCategory category = NavigationCategory.Objects) =>
        new("full:quest:" + id, label, [new(scene, [actor], [])], instruction, category);

    private static FullStoryObjective Person(string id, string label, int scene, int actor, string? instruction = null) =>
        Actor(id, label, scene, actor, instruction, NavigationCategory.People);

    private static FullStoryObjective Exit(string id, string label, int scene, int exit, string? instruction = null) =>
        new("full:quest:" + id, label, [new(scene, [], [$"exit:{exit}"])], instruction, NavigationCategory.Exits);

    private static FullStoryObjective Arrival(string id, string label, int scene, string? instruction = null) =>
        new("full:quest:" + id, label, [new(scene, [], [])], instruction, NavigationCategory.Objects);

    private static FullStoryObjective Target(string id, string label, int scene, string target,
        string? instruction = null, NavigationCategory category = NavigationCategory.Objects) =>
        new("full:quest:" + id, label, [new(scene, [], [target])], instruction, category);

    public static IReadOnlyList<FullStoryObjective> All { get; } =
    [
        Person("desert:seed-zeal", "Ask the woman in Zeal Palace to keep the sapling", 401, 12,
            "Tell her to plant it rather than burn it. This is the prerequisite for Fiona's desert."),
        Person("desert:seed-survivors", "Ask the woman in the surviving village to plant the sapling", 424, 25),
        Person("desert:fiona", "Speak with Fiona about the desert", 158, 8),
        new("full:quest:desert:retinite", "Defeat Retinite beneath the Sunken Desert",
            [new(161, [2, 3, 4, 5], [])],
            "The four native encounter regions follow its appearances in the sand. Water magic weakens its defense.", NavigationCategory.Objects),
        Person("desert:robo", "Return to Fiona with Robo and offer his help", 158, 8,
            "Robo must be in the active party. Agree to leave him to restore the forest."),
        Person("desert:recover", "Recover Robo from Fiona's Shrine in 1000 AD", 57, 8,
            "Examine Robo at the back of the shrine. The campfire scene follows."),
        Actor("lucca:gate", "Follow the red gate from the campfire", 83, 4, category: NavigationCategory.Exits),
        Actor("lucca:machine", "Reach the machine's emergency control in Lucca's past", 4, 12,
            "The nearby notes identify Lara's name as the password. Enter it at the control when prompted; this sequence is timed."),
        Actor("lucca:return", "Return through the gate in Lucca's room", 10, 13, category: NavigationCategory.Exits),
        Person("lucca:robo", "Speak with Robo after returning from Lucca's past", 83, 2),

        Person("cyrus:ask-tools", "Ask the carpenter in 600 AD Choras about his tools", 188, 15),
        Person("cyrus:descendant", "Ask the carpenter in the 1000 AD Choras inn for tools", 62, 11),
        Person("cyrus:tools", "Collect the tools from the carpenter's wife", 61, 8),
        Person("cyrus:deliver-tools", "Bring the tools to the carpenter in 600 AD", 188, 15),
        Person("cyrus:hire", "Ask the carpenter at his house to repair the Northern Ruins", 186, 8),
        Arrival("cyrus:repairs-arrival", "Meet the carpenters at the Northern Ruins entrance", 65,
            "Use the 600 AD ruins. Their report plays when you enter."),
        Actor("cyrus:lower-monsters", "Clear the Sentries from the lower hall of the Northern Ruins", 66, 0,
            "Clear the west and east groups, then the returning group, so the carpenter can repair this section."),
        Person("cyrus:lower-repair", "Pay the carpenter to repair the lower hall", 186, 8,
            "The repair costs 2000 G. Return to the 600 AD ruins afterward."),
        Actor("cyrus:grave", "Examine Cyrus's grave with Frog in the party", 73, 8,
            "Use the 600 AD grave. Frog must be active; the native script does not require him to lead."),
        Actor("cyrus:upper-monsters", "Clear the monsters from the upper hall of the Northern Ruins", 68, 0),
        Person("cyrus:upper-repair", "Pay the carpenter to finish the upper hall repairs", 186, 8,
            "The repair costs 2000 G. The restored rooms and their sealed chests can then be explored in both eras."),

        Actor("sun:battle", "Defeat the Son of the Sun in the future Sun Temple", 251, 8),
        Actor("sun:moon-stone", "Collect the Moon Stone after the Sun Temple battle", 251, 17),
        Actor("sun:prehistoric", "Leave the Moon Stone in the prehistoric Sun Shrine", 327, 8),
        Actor("sun:missing", "Check the missing Moon Stone in the 1000 AD Sun Shrine", 64, 9),
        Person("sun:jerky", "Buy spiced jerky at the Porre Snail Stop in 1000 AD", 53, 9,
            "The jerky costs 9900 G. It is needed for the family's kindness in 600 AD."),
        Person("sun:kindness", "Give the jerky to the woman in the 600 AD elder's house", 154, 11,
            "Choose to give it away. Selling it consumes the jerky without setting the kindness flag."),
        Person("sun:mayor", "Ask the mayor of Porre in 1000 AD to return the Moon Stone", 50, 8),
        Actor("sun:replace", "Return the Moon Stone to the 1000 AD Sun Shrine", 64, 9),
        Actor("sun:future", "Collect the charged Sun Stone with Lucca in 2300 AD", 255, 8,
            "Lucca must be in the active party. Her workshop scene and rewards follow automatically."),

        Actor("geno:console", "Use the Geno Dome entrance console with Robo", 248, 8,
            "Robo must be in the active party. The console moves him into the lead for the dome."),
        Actor("geno:switch-left", "Set the left switch in Geno Dome's three-switch room", 256, 14),
        Actor("geno:switch-right", "Set the right switch in Geno Dome's three-switch room", 256, 16),
        Actor("geno:switch-middle", "Restore the middle switch to green", 256, 15,
            "The two outside switches must be on and the middle switch off."),
        Actor("geno:charger-open", "Open the charging pod beside Geno Dome's computer", 256, 17),
        Actor("geno:charge-first", "Charge Robo in the pod beside the computer", 256, 25,
            "Then hurry to the pod below the three switches before the charge expires."),
        Actor("geno:pod-first", "Discharge Robo into the pod below the three switches", 256, 27),
        Actor("geno:doll-first", "Collect the doll beyond the three-switch door", 256, 23),
        Exit("geno:circuit-start", "Take the southeastern elevator to reach the far side of Geno Dome", 256, 3,
            "The conveyor-control switch is reached through the upper floor and its long passage."),
        Actor("geno:circuit-lift", "Ride the elevator along Geno Dome's upper-floor circuit", 127, 8, category: NavigationCategory.Exits),
        Actor("geno:circuit-unboard", "Leave this elevator to continue along the upper floor", 127, 9, category: NavigationCategory.Exits),
        Actor("geno:circuit-door", "Open the elevator door at the rear of the long passage", 267, 8),
        Exit("geno:circuit-rear", "Enter the rear elevator and descend to the conveyor control", 267, 1),
        Actor("geno:reverse-conveyor", "Reverse the conveyor with the hidden control switch", 256, 12),
        Actor("geno:laser-barrier", "Deactivate the laser barrier on the far side of Geno Dome", 256, 13),
        Actor("geno:escort-pod-open", "Open the charging receiver for the guardian robot's room", 256, 11),
        Actor("geno:charge-second", "Charge Robo again for the guardian robot's door", 256, 25,
            "Use the reversed conveyor to reach the northern receiver before the charge expires."),
        Actor("geno:pod-second", "Discharge Robo into the northern receiver", 256, 28),
        Actor("geno:escort", "Lead the friendly robot to the robot guarding the second doll", 256, 8,
            "Touch the friendly robot to make it follow. Move slowly around corners toward the southwestern guardian."),
        Actor("geno:escort-guard", "Bring the following robot beside the southwestern guardian", 256, 29,
            "Keep the follower close. The native event completes when the two robots meet."),
        Actor("geno:doll-second", "Collect the doll behind the guardian robot", 256, 22),
        Actor("geno:atropos", "Meet Atropos on Geno Dome's upper floor", 268, 29,
            "Robo fights this battle alone."),
        Actor("geno:pedestal-left", "Put the guardian-room doll on the left pedestal", 268, 10),
        Actor("geno:pedestal-right", "Put the three-switch-room doll on the right pedestal", 268, 11),
        Actor("geno:brain", "Enter Mother Brain's chamber", 268, 30,
            "Both dolls must be on their pedestals. The battle and Robo's rewards follow."),

        Person("rainbow:toma", "Take Toma's request at the 600 AD Choras tavern", 188, 13),
        Actor("rainbow:tomb", "Pour Toma's drink on his tomb at West Cape in 1000 AD", 63, 8),
        Exit("rainbow:claw-entrance", "Follow Giant's Claw's entrance passage to the throne room", 195, 1),
        Exit("rainbow:claw-throne", "Continue south through Giant's Claw's throne room", 297, 0),
        Exit("rainbow:claw-switch-room", "Continue to the first skull-switch room", 195, 9),
        Target("rainbow:claw-floor-switch", "Step on the left floor switch in Giant's Claw", 110,
            "script-terrain:1:0:10:26:10:26", "The left switch opens the holes needed to descend."),
        Target("rainbow:claw-drop", "Descend through the opened hole", 110,
            "script-region:0:110:0:20:10:24", category: NavigationCategory.Exits),
        Target("rainbow:claw-skull-switch", "Open the skull with the left switch in the lower room", 110,
            "script-terrain:2:0:28:23:28:23"),
        Exit("rainbow:claw-lower-door", "Leave the lower skull-switch room to the south", 110, 1),
        Exit("rainbow:claw-lower-path", "Follow the lower path through Giant's Claw", 196, 3),
        Exit("rainbow:claw-southern-ladder", "Use the southern ladder and continue to the western door", 196, 0,
            "The northern broken ladder returns to an earlier part of the dungeon."),
        Exit("rainbow:claw-skull-hall", "Climb through the open skull toward the next hall", 296, 2),
        Exit("rainbow:claw-east-door", "Continue through the southeastern door of the monster hall", 109, 7),
        Exit("rainbow:claw-trap-room", "Cross the Rubble room to its northeastern door", 195, 5),
        Actor("rainbow:claw-chest-trap", "Open the right chest to descend into the prison cells", 109, 12),
        Exit("rainbow:claw-cell-stairs", "Leave the cell and take the eastern stairs down", 109, 1),
        Actor("rainbow:claw-bars", "Open the bars beside Giant's Claw's final save point", 109, 13),
        Exit("rainbow:claw-boss-door", "Go through the opened cell to the Rust Tyranno", 109, 10),
        Exit("rainbow:claw-broken-return", "Return from the broken ladder toward the main route", 195, 6),
        Exit("rainbow:claw-return-stairs", "Leave the broken-ladder passage", 111, 0),
        Exit("rainbow:claw-return-hall", "Return to the hall before the skull-switch room", 109, 9),
        Exit("rainbow:claw-west-return", "Return from the western side room", 195, 3),
        Actor("rainbow:tyranno", "Reach and defeat the Rust Tyranno in Giant's Claw", 197, 0),
        Actor("rainbow:shell", "Examine the Rainbow Shell behind the Rust Tyranno", 197, 10,
            "After examining the shell, leave its chamber to ask Guardia Castle to keep it safe."),
        Actor("rainbow:leave-shell", "Leave the Rainbow Shell chamber to request Guardia's help", 197, 1,
            "Walk south after the party examines the shell. This starts the return to Guardia Castle.", NavigationCategory.Exits),
        Person("rainbow:trial", "Take Marle to the guards outside the 1000 AD Hall of Justice", 441, 8,
            "Marle must be in the active party. Speak to the guard to investigate the king's trial."),
        Actor("rainbow:proof", "Find the Rainbow Shell in Guardia's treasury with Marle", 440, 9,
            "The shell supplies the proof for the trial. Marle must be in the active party."),
        Actor("rainbow:chancellor", "Return to the Hall of Justice with the shell fragment", 438, 10),
        Person("rainbow:melchior", "Collect Melchior's Rainbow Shell equipment in the treasury", 440, 23,
            "Choose one dress or three helms."),
        Person("rainbow:forge", "Show Melchior the Sun Stone in the treasury", 440, 23,
            "Speak again after choosing the Rainbow Shell equipment to collect the combined rewards."),

        Actor("ozzie:flea", "Defeat Flea in Ozzie's Fort", 183, 9),
        Actor("ozzie:slash", "Defeat Slash in Ozzie's Fort", 184, 9),
        Actor("ozzie:guillotine", "Pass the guillotine trap in Ozzie's Fort", 179, 0,
            "Approach the northeastern passage. Do not stand beneath the blade for the chest."),
        Actor("ozzie:three", "Face Ozzie, Flea and Slash together", 180, 8),
        Actor("ozzie:switch", "Face Ozzie and strike the switch behind him", 181, 8,
            "The battle drops the party into the previous room; return to Ozzie's room afterward."),
        Actor("ozzie:return", "Return to Ozzie's room after the floor drops", 181, 8),

        Person("revival:egg", "Ask Gaspar for the means to bring Crono back", EndOfTime, 28,
            "Speak to him again after he calls the party back."),
        Person("revival:doll-game", "Get Crono's doppelganger from Norstein Bekkler", 434, 11,
            "Play the imitation game at the fair. A won or purchased doll is sent to Crono's room; it still has to be collected."),
        Person("revival:mother", "Speak to Crono's mother upstairs at his house", 2, 8),
        Actor("revival:doll-collect", "Collect Crono's doppelganger from his room", 2, 19),
        Person("revival:belthasar", "Show the doppelganger to Belthasar's Nu in Keeper's Dome", 242, 16,
            "The Nu sends three Poyozo dolls to Death Peak. Let that preparation finish before climbing."),
        Person("revival:wind", "Speak to the Poyozo doll at Death Peak's entrance", 244, 9,
            "It becomes a tree. Shelter directly below the trees during gusts, and advance when the wind eases."),
        Actor("revival:spawn-one", "Defeat the first Lavos Spawn inside Death Peak", 264, 0),
        Actor("revival:cave-switch", "Press the sparkle to open the next Death Peak passage", 246, 8),
        Exit("revival:icy-ledge", "Cross the narrow icy ledge on Death Peak", 262, 0,
            "The Poyozo doll here warns about the slippery ledge. Keep to its center; falling returns the party to the lower slope."),
        Actor("revival:spawn-two", "Defeat the second Lavos Spawn on Death Peak", 247, 0),
        Actor("revival:spawn-three", "Defeat the final Lavos Spawn on Death Peak", 495, 8),
        Actor("revival:shell", "Push the empty shell beneath the ladder", 495, 9,
            "Use the shell as a step to reach the upper ledge."),
        Arrival("revival:summit", "Reach Death Peak's summit with the Time Egg and doppelganger", 265,
            "The summit starts the revival scene automatically."),

        Actor("omen:entrance", "Open the Black Omen's entrance", 449, 8,
            "It can be cleared in 1000 AD, then 600 AD, then 12000 BC. A clear removes it from that era and all later eras. The future entrance does not open."),
        Actor("omen:mega", "Defeat the Mega Mutant inside the Black Omen", 308, 0),
        Actor("omen:lift-down", "Use the left sparkle to descend through the Black Omen", 99, 3,
            category: NavigationCategory.Exits),
        Actor("omen:lift-up", "Use the right sparkle to ascend to the Black Omen's final levels", 99, 2,
            category: NavigationCategory.Exits),
        Actor("omen:lift-exit", "Leave the stopped Black Omen elevator", 99, 1, category: NavigationCategory.Exits),
        Actor("omen:nu-door", "Open the wall north of the two Nus", 315, 8),
        Actor("omen:giga", "Reach and defeat the Giga Mutant", 323, 10,
            "Use the native warp pads and elevator to cross the middle levels."),
        Actor("omen:terra-door", "Open the door beyond the Black Omen's lower elevator", 324, 20),
        Actor("omen:terra", "Reach and defeat the Terra Mutant", 325, 16),
        Actor("omen:treasure-door", "Open the door beyond the Terra Mutant's chamber", 325, 17),
        Actor("omen:spawn-door", "Open the door at the top of the Black Omen's long path", 326, 8),
        Actor("omen:spawn", "Defeat the Lavos Spawn in the Black Omen", 96, 0),
        Actor("omen:zeal", "Face Queen Zeal in the lowest chamber", 451, 8),
        Actor("omen:machine", "Face the Mammon Machine", 422, 8),
        Actor("omen:queen", "Face Queen Zeal above the Black Omen", 107, 9),
    ];

    private static readonly IReadOnlyDictionary<string, FullStoryObjective> ById =
        All.ToDictionary(o => o.Id["full:quest:".Length..]);

    public static IReadOnlyList<FullStoryObjective> Build(int scene, FieldStoryState? state)
    {
        if (state is null || state.Point < FullStoryObjectives.FirstPoint) return [];
        var result = new List<FullStoryObjective>();
        Add(Desert(scene, state));
        Add(Lucca(scene, state));
        Add(Cyrus(scene, state));
        Add(Sun(scene, state));
        Add(Geno(scene, state));
        Add(Rainbow(scene, state));
        Add(Ozzie(scene, state));
        Add(Revival(scene, state));
        Add(Omen(scene, state));
        return result;

        void Add(string? id) { if (id is not null) result.Add(ById[id]); }
    }

    private static bool In(int scene, HashSet<int> scenes) =>
        scene == EndOfTime || scene is >= 496 and <= 502 || scenes.Contains(scene);
    private static bool Known(FieldStoryState state, params int[] indices) => indices.All(i => state.Global(i).HasValue);
    private static bool Bit(FieldStoryState state, int index, int mask) => state.Flag(index, mask) == true;

    private static bool AncientRuins(int scene, FieldStoryState state) => scene == 497 ||
        scene is >= 185 and <= 190 || scene is 65 or 66 or 67 or 68 or 69 or 70 or 73 && Bit(state, 0x1A3, 0x10);

    private static string? Desert(int scene, FieldStoryState s)
    {
        if (!In(scene, DesertScenes) || !Known(s, 0xF7, 0x19E, 0x1AD, 0x1A3) || Bit(s, 0x19E, 2)) return null;
        if (Bit(s, 0x19E, 1)) return "desert:recover";
        if (Bit(s, 0x1A3, 1)) return "desert:robo";
        if (!Bit(s, 0xF7, 2))
        {
            if (s.Point >= Flight) return "desert:seed-survivors";
            return s.Point is >= 0x9F and < 0xCB ? "desert:seed-zeal" : null;
        }
        return Bit(s, 0x1AD, 0x80) ? "desert:retinite" : "desert:fiona";
    }

    private static string? Lucca(int scene, FieldStoryState s)
    {
        // These locations are available only during the native overnight event.
        // A past accident is not a permanent errand at Lucca's ordinary house.
        if (scene is not (436 or 83 or 10 or 4 or 3 or 9) ||
            !Known(s, 0x19E, 0x5A, 0x6A, 0x7C) || !Bit(s, 0x19E, 2)) return null;
        if (scene is 10 or 4 or 3 or 9 && !Bit(s, 0x5A, 2)) return null;
        if (Bit(s, 0x6A, 0x10)) return scene == 83 ? "lucca:robo" : null;
        if (Bit(s, 0x7C, 0x40)) return "lucca:return";
        if (Bit(s, 0x5A, 4)) return "lucca:machine";
        return "lucca:gate";
    }

    private static string? Cyrus(int scene, FieldStoryState s)
    {
        if (s.Point < Flight || !In(scene, CyrusScenes) || !Known(s, 0x19E, 0x19F, 0x1A3, 0x1AC)) return null;
        if (Bit(s, 0x1A3, 0x40) && Bit(s, 0x1AC, 0x80)) return null;
        if (!Bit(s, 0x19E, 0x40))
        {
            if (Bit(s, 0x19E, 0x80)) return "cyrus:deliver-tools";
            if (Bit(s, 0x19E, 0x20)) return "cyrus:tools";
            return Bit(s, 0x19E, 0x10) ? "cyrus:descendant" : "cyrus:ask-tools";
        }
        if (Bit(s, 0x19F, 1)) return AncientRuins(scene, s) ? "cyrus:repairs-arrival" : null;
        if (!Bit(s, 0x19F, 2)) return "cyrus:hire";
        if (!Bit(s, 0x19F, 4)) return Bit(s, 0x19F, 0x20) ? "cyrus:lower-repair" : AncientRuins(scene, s) ? "cyrus:lower-monsters" : null;
        if (!Bit(s, 0x1A3, 0x40)) return AncientRuins(scene, s) ? "cyrus:grave" : null;
        if (!Bit(s, 0x19F, 8)) return Bit(s, 0x19F, 0x40) ? "cyrus:upper-repair" : AncientRuins(scene, s) ? "cyrus:upper-monsters" : null;
        return !Bit(s, 0x1AC, 0x80) && AncientRuins(scene, s) ? "cyrus:repairs-arrival" : null;
    }

    private static string? Sun(int scene, FieldStoryState s)
    {
        if (s.Point < Flight || !In(scene, SunScenes) || !Known(s, 0x13A, 0x1D2)) return null;
        // 40 is collected; 80 is the automatic workshop reward scene finishing.
        if (Bit(s, 0x13A, 0xC0)) return null;
        if (Bit(s, 0x13A, 0x20)) return "sun:future";
        if (Bit(s, 0x13A, 0x10)) return "sun:replace";
        if (Bit(s, 0x13A, 8))
        {
            if (Bit(s, 0x1D2, 4)) return "sun:mayor";
            return s.ItemCount(0x500C) is { } jerky ? jerky > 0 ? "sun:kindness" : "sun:jerky" : null;
        }
        if (Bit(s, 0x13A, 4)) return "sun:missing";
        if (Bit(s, 0x13A, 2)) return "sun:prehistoric";
        return Bit(s, 0x13A, 1) ? "sun:moon-stone" : "sun:battle";
    }

    private static string? Geno(int scene, FieldStoryState s)
    {
        if (s.Point < Flight || !In(scene, GenoScenes) || !Known(s, 0x13B, 0x13C, 0x13D, 0x13E) || Bit(s, 0x13B, 0x10)) return null;
        var first = Bit(s, 0x13C, 0x24); // obtained or already placed
        var second = Bit(s, 0x13C, 0x12);
        if (first && second)
        {
            if (!Bit(s, 0x13B, 0x20)) return "geno:atropos";
            if (!Bit(s, 0x13C, 0x10)) return "geno:pedestal-left";
            if (!Bit(s, 0x13C, 0x20)) return "geno:pedestal-right";
            return "geno:brain";
        }
        if (!Bit(s, 0x13C, 1)) return "geno:console";
        if (!first)
        {
            if (Bit(s, 0x13E, 4)) return "geno:doll-first";
            if (!Bit(s, 0x13D, 0x10)) return "geno:switch-left";
            if (!Bit(s, 0x13D, 0x40)) return "geno:switch-right";
            if (Bit(s, 0x13D, 0x20)) return "geno:switch-middle";
            if (!Bit(s, 0x13D, 0x80)) return "geno:charger-open";
            return Bit(s, 0x13C, 0x40) ? "geno:pod-first" : "geno:charge-first";
        }
        if (Bit(s, 0x13C, 0x80)) return "geno:doll-second";
        if (Bit(s, 0x13E, 2))
            return scene == 256 && s.Local(12) == 1 ? "geno:escort-guard" : "geno:escort";
        if (!Bit(s, 0x13D, 4)) return GenoConveyorCircuit(scene, s);
        if (!Bit(s, 0x13D, 8)) return "geno:laser-barrier";
        if (!Bit(s, 0x13D, 2)) return "geno:escort-pod-open";
        if (!Bit(s, 0x13D, 0x80)) return "geno:charger-open";
        return Bit(s, 0x13C, 0x40) ? "geno:pod-second" : "geno:charge-second";
    }

    private static string GenoConveyorCircuit(int scene, FieldStoryState s)
    {
        // Atel355 actor0 continuously copies the leader to local8=X/local9=Y.
        // Atel341 records previous scene's low byte in local6, entrance X in local7.
        // Only read these scratch cells in their owning scene.
        if (scene == 256 && s.Local(8) is >= 30 && s.Local(9) is <= 16) return "geno:reverse-conveyor";
        if (scene == 127)
            return s.Local(6) == 12 || s.Local(6) == 0 && s.Local(7) == 43
                ? "geno:circuit-unboard" : "geno:circuit-lift";
        if (scene == 267) return s.Local(6) == 1 ? "geno:circuit-rear" : "geno:circuit-door";
        if (scene is 268 or 128) return "geno:circuit-door";
        return "geno:circuit-start";
    }

    private static string? Rainbow(int scene, FieldStoryState s)
    {
        if (s.Point < Flight || !In(scene, RainbowScenes) || !Known(s, 0x1A0, 0x1A3, 0x1AF, 0x1D2, 0xA9, 0xA2, 0x50, 0x6D, 0x13A)) return null;
        if (Bit(s, 0x6D, 0x20)) return Bit(s, 0x6D, 0x10) && Bit(s, 0x13A, 0x80) ? "rainbow:forge" : null;
        if (Bit(s, 0x50, 0x40) || Bit(s, 0x6D, 0x10)) return "rainbow:melchior";
        if (Bit(s, 0xA2, 0x80)) return "rainbow:chancellor";
        if (Bit(s, 0xA9, 0x80)) return Bit(s, 0x50, 0x20) ? "rainbow:proof" : "rainbow:trial";
        if (Bit(s, 0x1AF, 2)) return "rainbow:leave-shell";
        if (Bit(s, 0x1D2, 0x40)) return "rainbow:shell";
        if (Bit(s, 0x1A3, 0x80) || scene is 195 or 196 or 197 or 109 or 110 or 111 or 296 or 297 or 490)
            return GiantClaw(scene, s);
        return Bit(s, 0x1A0, 2) ? "rainbow:tomb" : "rainbow:toma";
    }

    private static string GiantClaw(int scene, FieldStoryState s)
    {
        // These maps contain several physically separate rooms with the same
        // scene id. A scene-only shortest path would point back through the door
        // just used. The installed MapTable planes establish these separated
        // sections; each controller continuously stores the leader in locals7/8.
        // The coordinates choose a section, never supply a movement target.
        if (scene == 297) return "rainbow:claw-throne";
        if (scene == 296) return "rainbow:claw-skull-hall";
        if (scene == 111) return "rainbow:claw-return-stairs";
        if (scene == 197) return "rainbow:tyranno";
        if (scene is not (109 or 110 or 195 or 196)) return "rainbow:claw-entrance";
        if (s.Local(7) is not { } x || s.Local(8) is not { } y) return "rainbow:tyranno";
        if (scene == 195)
        {
            if (y >= 45) return x >= 45 ? "rainbow:claw-broken-return" : "rainbow:claw-switch-room";
            if (y >= 24) return x >= 35 ? "rainbow:claw-west-return" : "rainbow:claw-entrance";
            return "rainbow:claw-trap-room";
        }
        if (scene == 196) return y >= 40 ? "rainbow:claw-lower-path" : "rainbow:claw-southern-ladder";
        if (scene == 110)
        {
            if (x < 20) return s.Local(9) == 1 ? "rainbow:claw-drop" : "rainbow:claw-floor-switch";
            return s.Local(11) == 1 ? "rainbow:claw-lower-door" : "rainbow:claw-skull-switch";
        }
        if (y >= 50) return "rainbow:claw-return-hall";
        if (y >= 29) return x >= 40 ? "rainbow:claw-chest-trap" : "rainbow:claw-cell-stairs";
        if (x < 40) return "rainbow:claw-east-door";
        return s.Local(12) == 1 ? "rainbow:claw-boss-door" : "rainbow:claw-bars";
    }

    private static string? Ozzie(int scene, FieldStoryState s)
    {
        if (s.Point < Flight || !In(scene, OzzieScenes) || !Known(s, 0x1A1, 0x1A0) || Bit(s, 0x1A1, 0x80)) return null;
        if (Bit(s, 0x1A1, 0x40)) return Bit(s, 0x1A0, 0x80) ? "ozzie:return" : "ozzie:switch";
        if (Bit(s, 0x1A1, 0x10)) return "ozzie:three";
        if (Bit(s, 0x1A1, 8)) return "ozzie:guillotine";
        return Bit(s, 0x1A1, 4) ? "ozzie:slash" : "ozzie:flea";
    }

    private static string? Revival(int scene, FieldStoryState s)
    {
        if (s.Point < Flight || !In(scene, RevivalScenes) || !Known(s, 0x57, 0x7C, 0x70, 0x5E, 0x14F, 0x58, 0x64) || Bit(s, 0x57, 0x40)) return null;
        if (!Bit(s, 0x70, 4))
        {
            if (!Bit(s, 0x7C, 1) && s.Point < 0xD5) return "revival:egg";
            if (s.ItemCount(0x5013) is not { } doll) return null;
            if (doll > 0) return "revival:belthasar";
            if (!Bit(s, 0x5E, 1)) return "revival:doll-game";
            return Bit(s, 0x14F, 1) ? "revival:doll-collect" : "revival:mother";
        }
        if (!Bit(s, 0x7C, 4)) return "revival:wind";
        if (!Bit(s, 0x64, 8)) return "revival:spawn-one";
        if (!Bit(s, 0x58, 0x80)) return "revival:cave-switch";
        if (!Bit(s, 0x64, 0x10)) return scene == 262 ? "revival:icy-ledge" : "revival:spawn-two";
        if (!Bit(s, 0x64, 0x20)) return "revival:spawn-three";
        // Atel101 actor0 reads shell actor9's tile into locals11/12. Once placed
        // at the native ladder check (16,15), the remaining action is the summit.
        if (scene == 495 && s.Local(11) is { } x && s.Local(12) is { } y && (x != 16 || y != 15))
            return "revival:shell";
        return "revival:summit";
    }

    private static string? Omen(int scene, FieldStoryState s)
    {
        if (s.Point < 0xD4 || !Known(s, 0x1F7, 0x1A7, 0x1A8, 0x1A9, 0x71, 0x15A)) return null;
        if (scene == 107) return "omen:queen";
        if (scene == 422) return "omen:machine";
        if (!OmenScenes.Contains(scene))
        {
            // Same scene449 serves all eras. Do not send a 1000 AD player to a
            // cleared Omen just because the 600 AD one is still present.
            var mask = scene switch { 496 => 8, 497 => 4, 502 => 2, EndOfTime => 14, _ => 0 };
            return mask != 0 && Bit(s, 0x1F7, mask) ? "omen:entrance" : null;
        }
        if (scene == 99)
        {
            if (Bit(s, 0x1A7, 1)) return "omen:lift-down";
            if (Bit(s, 0x1A7, 2)) return "omen:lift-exit";
            if (Bit(s, 0x1A7, 8)) return "omen:lift-exit";
            if (Bit(s, 0x1A7, 4)) return "omen:lift-up";
        }
        if (scene == 449 && Bit(s, 0x1A8, 0x10)) return null; // future platform, sealed forever
        if (scene == 449 && s.Local(12) == 0) return "omen:entrance";
        if (scene == 315 && s.Local(8) == 0) return "omen:nu-door";
        if (scene == 324 && s.Local(10) == 0) return "omen:terra-door";
        if (scene == 325 && Bit(s, 0x15A, 1) && s.Local(10) == 0) return "omen:treasure-door";
        if (scene == 326 && s.Local(6) == 0) return "omen:spawn-door";
        if (!Bit(s, 0x1A8, 1)) return "omen:mega";
        if (!Bit(s, 0x71, 0x10)) return "omen:giga";
        if (!Bit(s, 0x15A, 1)) return "omen:terra";
        if (!Bit(s, 0x1A9, 1)) return "omen:spawn";
        return "omen:zeal";
    }
}
