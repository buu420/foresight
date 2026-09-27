using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>A place the player must reach or an actor they must interact with.
/// <paramref name="Actors"/> are native actor indexes in <paramref name="Scene"/>.
/// Both lists empty means entering the scene is itself the step, which is only used
/// where the native scene advances on arrival.</summary>
public sealed record StoryGoal(int Scene, int[] Actors, string[] Targets);

/// <summary>One objective. Several <see cref="Goals"/> are alternative ways to reach
/// the same action, never ordered steps.</summary>
public sealed record FullStoryObjective(string Id, string Label, StoryGoal[] Goals,
    string? Instruction = null, NavigationCategory Category = NavigationCategory.StoryEvents);

/// <summary>
/// The story from the end of the first End of Time lesson through Lavos, every late
/// optional quest, and the two bonus areas, broken into the substeps a player has to
/// perform rather than one pointer per chapter.
/// <para>
/// Each substep names the native actor whose script performs it and the script flag,
/// extended cell or held item that records it as done, so the catalogue advances with
/// the game. Bindings come from the installed PC scripts by way of
/// <see cref="GameNavigationCatalog"/>'s guarded actions; the decoding tools and their
/// output are in <c>artifacts/research/full-story-0323/claude/</c> and the reasoning is
/// in <c>docs/full-story-objectives.md</c>.
/// </para>
/// </summary>
public static class FullStoryObjectives
{
    /// <summary>The counter value this catalogue starts at. Values up to 76 belong to
    /// <see cref="FutureStoryTargets"/> and are untouched.</summary>
    public const int FirstPoint = 77;

    /// <summary>Gaspar's room. Actors 15 to 23 are the pillars of light, actor 9 is the
    /// gate beside them and actor 28 is Gaspar.</summary>
    public const int EndOfTime = 464;

    private const int PillarFuture = 21, PillarProto = 15, PillarPresent = 16, PillarFair = 18;
    private const int PillarForest = 20, PillarMiddleAges = 19, PillarPrehistory = 17;
    private const int PillarLairRuins = 22, PillarAntiquity = 23;

    /// <summary>The gate beside the pillars. Below vortex stage 2 its only destination is
    /// Lavos; from stage 2 it also offers Time's Eclipse.</summary>
    private const int LavosGate = 9;

    /// <summary>Gaspar. His dialogue branches on the same quest flags used below, and he
    /// is the actor who hands over the time egg.</summary>
    private const int Gaspar = 28;

    /// <summary>Pillar actor to the scene its change-location operand names.</summary>
    public static IReadOnlyDictionary<int, int> PillarDestinations { get; } =
        new Dictionary<int, int>
        {
            [PillarProto] = 227, [PillarPresent] = 36, [PillarPrehistory] = 272,
            [PillarFair] = 8, [PillarMiddleAges] = 113, [PillarForest] = 20,
            [PillarFuture] = 208, [PillarLairRuins] = 307, [PillarAntiquity] = 351,
        }.AsReadOnly();

    private delegate bool? Cond(FieldStoryState state);

    private sealed record Step(string Id, string Label, StoryGoal[] Goals, Cond Done, string? Instruction)
    {
        public FullStoryObjective Objective(NavigationCategory category) =>
            new(Id, Label, Goals, Instruction, category);
    }

    private sealed record Chapter(int Until, Step[] Steps);

    // -- authoring helpers -----------------------------------------------------------

    private static Step Sub(string id, string label, StoryGoal[] goals, Cond done,
        string? instruction = null) => new("full:" + id, label, goals, done, instruction);

    /// <summary>The last substep of a chapter or quest: what retires it is the chapter's
    /// own counter value or the quest's own completion flag, not a substep flag.</summary>
    private static Step Tail(string id, string label, StoryGoal[] goals, string? instruction = null) =>
        new("full:" + id, label, goals, Open, instruction);

    private static Chapter Upto(int until, params Step[] steps) => new(until, steps);

    private static StoryGoal At(int scene) => new(scene, [], []);
    private static StoryGoal With(int scene, params int[] actors) => new(scene, actors, []);
    private static StoryGoal Pillar(int actor) => new(EndOfTime, [actor], []);

    private static bool? Open(FieldStoryState state) => false;
    private static Cond Flag(int index, int mask) => state => state.Flag(index, mask);

    // == the main chain ==============================================================
    //
    // Ordered by the counter value each chapter produces; Build takes the first chapter
    // the counter has not reached. Inside a chapter the substeps are ordered and Build
    // takes the earliest one that is not provably finished, so an unreadable flag holds
    // position instead of skipping ahead. Each goal is the actor root's catalogue
    // exposes for that step; enter-the-scene goals are only used where the catalogue has
    // a Progress trigger or a startup script fires the step on arrival
    // (artifacts/research/full-story-0323/claude/bindings.txt derives every one).

    private static readonly Chapter[] MainChain =
    [
        Upto(0x4E, Tail("leave-end-of-time", "Take the pillar of light back to 1000 AD",
            [Pillar(PillarPresent), At(36)],
            "The lesson is over. That pillar opens into a house in Medina Village.")),

        Upto(0x51, Tail("heckran", "Search Heckran Cave and beat what lives in it", [At(47)],
            "The cave mouth is on the 1000 AD map well south-west of Medina, just north of Melchior's cabin.")),

        Upto(0x54, Tail("return-middle-ages", "Go back to 600 AD to look for Magus",
            [Pillar(PillarMiddleAges), At(113)],
            "The Truce Canyon gate, this pillar and the Guardia Forest gate all work.")),

        Upto(0x57,
            Sub("zenan-request", "Ask the captain at Zenan Bridge what the soldiers need",
                [With(134, 13), With(135, 13)], Flag(0xA9, 0x04)),
            Sub("zenan-kitchen", "Ask the castle cook for food for the soldiers",
                [With(123, 10)], Flag(0xA9, 0x08)),
            Sub("zenan-rations", "Collect the rations as you leave Guardia Castle",
                [With(120, 15)], Flag(0xA9, 0x10),
                "Return through the main hall toward the castle exit. The cook catches up with you."),
            Tail("zenan-deliver", "Take the food to the captain at Zenan Bridge",
                [With(134, 13), With(135, 13)])),

        Upto(0x5A, Tail("zombor", "Cross Zenan Bridge and beat what blocks it",
            [At(134), At(135)])),

        Upto(0x5D, Tail("denadoro", "Climb the Denadoro Mountains", [At(145)],
            "The trail is on the 600 AD map a short way east of Dorino.")),

        Upto(0x60,
            Sub("masa-mune", "Approach the sword and face Masa and Mune", [With(151, 11)], Flag(0xF3, 0x20)),
            Tail("broken-blade", "Pick up the broken sword", [With(151, 10)])),

        Upto(0x66, Tail("tata", "Get the badge from Tata", [With(152, 9)],
            "Tata's house is in Porre in 600 AD, at the south end of the map.")),

        Upto(0x69,
            Sub("frog-medal", "Talk to Frog in his hideaway", [With(141, 4)], Flag(0xF3, 0x10)),
            Tail("frog-hilt", "Examine the sparkling chest in Frog's hideaway", [With(141, 10)])),

        Upto(0x6C, Tail("melchior-shows", "Take both halves of the broken sword to Melchior",
            [With(40, 8)],
            "His cabin is on the 1000 AD map on the Medina landmass, just south of the Heckran cave mouth.")),

        Upto(0x6F, Tail("meet-ayla", "Travel to prehistory and follow the mountain trail",
            [Pillar(PillarPrehistory), With(273, 6)])),

        Upto(0x72, Tail("soup", "Take part in the feast at Ioka Village", [With(280, 6)],
            "Press Confirm repeatedly for the drinking contest.")),

        Upto(0x75, Tail("ioka-morning", "Return to the Ioka meeting grounds", [At(279)])),

        Upto(0x78, Tail("chief-hut", "Ask the chief about the missing gate key", [With(275, 6)])),

        Upto(0x7B, Tail("forest-maze", "Find Kino in the Forest Maze", [With(282, 19)])),

        Upto(0x7C, Tail("nizbel", "Fight through the Reptite Lair", [At(289)],
            "The lair is south of the Forest Maze on the prehistoric map.")),

        Upto(0x7D, Tail("return-chief", "Take the gate key back to the chief's hut", [With(275, 8)])),

        Upto(0x7E, Tail("leave-prehistory", "Leave prehistory by the Mystic Mountains gate",
            [With(272, 10)])),

        Upto(0x81,
            Sub("reforge-request", "Bring the Dreamstone to Melchior", [With(40, 8)], Flag(0x19B, 0x10)),
            Sub("reforge-workshop", "Follow Melchior to his basement workshop", [With(41, 8)], Flag(0x19B, 0x20),
                "The reforging scene proceeds in the workshop. Return upstairs when it finishes."),
            Tail("reforge", "Collect the reforged Masamune from Melchior", [With(40, 8)])),

        Upto(0x84, Tail("give-frog", "Bring the reforged sword back to Frog", [At(141)])),

        Upto(0x87, Tail("magic-cave", "Open the sealed mountain in 600 AD", [At(163)],
            "Frog opens it. The cave is on the 600 AD map west of Fiendlord's Keep.")),

        Upto(0x88, Tail("enter-keep", "Enter Magus's Keep", [At(165)])),

        Upto(0x89,
            Sub("keep-west", "Explore the west wing of the keep", [At(169)], Flag(0xA3, 0x10)),
            Sub("keep-east", "Explore the east wing of the keep", [At(173)], Flag(0xA3, 0x20)),
            Sub("keep-entrance", "Return to the light in the keep's entrance hall", [With(166, 10)], Flag(0xA3, 0x02)),
            Sub("keep-slash-guards", "Approach the figure in the west wing", [With(169, 9)], Flag(0xA3, 0x40)),
            Sub("keep-slash", "Face Slash in the west wing", [With(169, 11)], Flag(0xA3, 0x04)),
            Sub("keep-flea", "Face Flea in the east wing", [With(173, 11)], Flag(0xA3, 0x08)),
            Tail("keep-ozzie", "Follow the entrance-hall warp and pursue Ozzie", [With(174, 10)],
                "Both lieutenants must be defeated before the entrance-hall light leads farther into the keep.")),

        Upto(0x8A, Tail("magus", "Face Magus in his chamber", [With(172, 9)])),

        Upto(0x8D, Tail("after-magus", "Follow the party to the Ioka chief's hut", [With(275, 8)])),

        Upto(0x8E, Tail("laruba", "Reach Laruba Ruins", [At(292)],
            "The ruins are west of Dactyl Nest on the prehistoric map.")),

        Upto(0x90, Tail("dactyl-nest", "Climb to the top of Dactyl Nest", [At(295)])),

        Upto(0x93, Tail("call-dactyls", "Call the dactyls from the peak", [At(295)])),

        Upto(0x96, Tail("tyranno-lair", "Break into the Tyranno Lair", [At(302)],
            "The lair is the fortress on the eastern spur of the prehistoric map.")),

        Upto(0x97, Tail("free-kino", "Free the prisoner inside the lair", [With(302, 9)])),

        Upto(0x98, Tail("fossil-head", "Follow Kino to the fossil head", [At(430)])),

        // Scene301 has no startup coordinate region: the six markers across
        // row10 share the native Confirm/Touch entry that starts the encounter.
        Upto(0x99, Tail("black-tyranno", "Face the Black Tyranno at the top of the lair",
            [With(301, 15, 16, 17, 18, 19, 20)], "Use Confirm at the approach to begin the encounter.")),

        Upto(0x9F, Tail("to-antiquity", "Take the new gate to 12000 BC",
            [Pillar(PillarAntiquity), At(351)])),

        Upto(0xA2, Tail("sealed-door", "Watch the sealed door in Zeal Palace", [At(329)],
            "The palace is on the floating continent; the land bridges lead to it.")),

        Upto(0xA5, Tail("charge-pendant", "Charge the pendant at the Mammon Machine",
            [With(331, 16)])),

        Upto(0xA8, Tail("queens-chamber", "Enter the queen's chamber", [At(332)])),

        Upto(0xAB, Tail("thrown-out", "Follow the party out of antiquity", [At(307)])),

        Upto(0xAE,
            Sub("epoch-examine", "Examine the time machine in Keeper's Dome", [With(243, 8, 9, 10)], Flag(0xEE, 0x02)),
            Sub("epoch-nu", "Return toward the hangar entrance to meet the Nu", [With(243, 11, 12)], Flag(0xEE, 0x80)),
            Tail("epoch", "Board the time machine", [With(243, 15)],
                "The dome is in 2300 AD. Its sealed corridor leads to the hangar.")),

        Upto(0xB4, Tail("mudbeast", "Return to 12000 BC and reach the Mudbeast den",
            [At(388)])),

        Upto(0xB7, Tail("woe-summit", "Climb the Mountain of Woe and free what is chained there",
            [With(397, 9)],
            "The base hangs off the south of the 12000 BC map and is reached from the Mudbeast den. The way up runs base, station three, station five, station eight, summit.")),

        Upto(0xBA, Tail("algetty-elder", "Report to the elder in Algetty", [With(382, 13)])),

        Upto(0xBB, Tail("ruby-knife", "Take what Melchior offers in the elder's room",
            [With(382, 10)])),

        Upto(0xBD, Tail("dalton-throne", "Face Dalton in the Zeal throne room", [At(334)])),

        Upto(0xC9,
            Sub("palace-east-switch", "Step on the eastern upper-floor switch", [With(406, 10)], Flag(0x162, 0x04)),
            Sub("palace-west-switch", "Step on the western upper-floor switch", [With(406, 16)], Flag(0x162, 0x02)),
            Sub("palace-switch", "Activate the central upper-floor switch", [With(407, 8)], Flag(0x162, 0x01)),
            Sub("palace-lower-west", "Activate the first lower-level wall switch", [With(406, 22)], Flag(0x162, 0x10)),
            Sub("palace-lower-east", "Activate the other lower-level wall switch", [With(406, 26)], Flag(0x162, 0x20)),
            Sub("palace-bridge", "Step on the floor switch to extend the bridge", [With(412, 9)], Flag(0x162, 0x80)),
            Tail("golem-twins", "Cross the bridge and face the Golem Twins", [At(414)])),

        Upto(0xCB, Tail("ocean-palace-lavos", "Face what rises in the Ocean Palace",
            [With(415, 11), At(419), At(420), At(421), At(423)],
            "Continue to the Mammon Machine. When you regain movement, walk toward Lavos.")),

        Upto(0xCC, Tail("ocean-palace-end", "Follow the collapse of the Ocean Palace",
            [At(415), At(493)])),

        Upto(0xCD, Tail("last-village", "Wake in the surviving village", [At(425)])),

        Upto(0xCF, Tail("blackbird-boarding", "Deal with Dalton at the village",
            [With(424, 8), At(371)])),

        // 0xAF bits0..2 track the three stolen equipment sets. 0xBA tracks the
        // separate money and item stores. Vent discovery is handled by scene below.
        Upto(0xD2,
            Sub("blackbird-equipment-3", "Recover the equipment in Blackbird room 4", [With(373, 9)], Flag(0xAF, 0x04)),
            Sub("blackbird-equipment-2", "Recover the equipment in Blackbird room 8", [With(443, 6)], Flag(0xAF, 0x02)),
            Sub("blackbird-equipment-1", "Recover the equipment in Blackbird room 9", [With(444, 6)], Flag(0xAF, 0x01)),
            Sub("blackbird-store-1", "Recover your money in Blackbird room 1", [With(370, 6)], Flag(0xBA, 0x04)),
            Sub("blackbird-store-2", "Recover your items in Blackbird room 5", [With(374, 9)], Flag(0xBA, 0x02)),
            Tail("blackbird-escape", "Leave through the Blackbird's left deck", [With(363, 22)],
                "Follow the wing to the encounter, then board the Epoch.")),

        Upto(0xD3, Tail("north-cape", "Meet the figure waiting at North Cape", [With(428, 16)],
            "North Cape is the northern tip of the 12000 BC map after the cataclysm.")),

        Upto(0xD4, Tail("black-omen-rises", "Take the Epoch up and watch the sky", [With(477, 8)],
            "Taking off after North Cape raises the Black Omen and opens the rest of the world. Boarding and flying the Epoch is done from the world map.")),

        Upto(0xD5, Tail("time-egg", "Ask Gaspar at the End of Time for the time egg",
            [With(EndOfTime, Gaspar), At(EndOfTime)],
            "The egg is what makes reviving Crono possible. Every remaining quest is optional from here.")),

        // Nothing after the time egg is compulsory. The gate beside the pillars is the
        // shortest honest answer; the Black Omen and the Epoch are alternatives and live
        // in the optional list, so neither is ever presented as required.
        Upto(int.MaxValue, Tail("lavos", "Face Lavos when you are ready",
            [With(EndOfTime, LavosGate)],
            "The gate beside the pillars goes straight there. Flying the Epoch into the shell and fighting up through the Black Omen are the other two ways in, and both are optional.")),
    ];

    private static readonly FullStoryObjective[] ContextSteps =
    [
        new("full:blackbird-look-outside", "Look outside the Blackbird's cell",
            [new(371, [], ["exit:0"])], "Use the ladder to the right deck, then return to the cell."),
        new("full:blackbird-return-cell", "Return to the Blackbird's cell",
            [new(364, [], ["exit:0"])]),
        new("full:blackbird-find-duct", "Inspect the cell's air duct", [With(371, 10)]),
        new("full:lavos-enter-shell", "Enter the opening in Lavos's shell", [With(466, 13)]),
        new("full:lavos-inner", "Continue through Lavos to the final battle", [With(474, 9)]),
    ];

    private static FullStoryObjective? ContextObjective(int scene, FieldStoryState state)
    {
        // The cell's ceiling cannot be reached before the party notices the
        // air duct. Returning from the right deck sets AF20; touching actor10
        // writes B340 and enables actor11's climb into the ventilation shaft.
        if (state.Point is >= 207 and < 210 && state.Flag(0xB3, 0x40) == false && scene is 371 or 364)
            return scene == 364 ? ContextSteps[1] :
                state.Flag(0xAF, 0x20) == true ? ContextSteps[2] : ContextSteps[0];
        if (scene == 466) return ContextSteps[3];
        if (scene == 474) return ContextSteps[4];
        return null;
    }

    /// <summary>Every objective this catalogue can ever return, for validation.</summary>
    public static IReadOnlyList<FullStoryObjective> All { get; } =
        [.. MainChain.SelectMany(chapter => chapter.Steps)
            .Select(step => step.Objective(NavigationCategory.StoryEvents))
            .Concat(ContextSteps).Concat(ClassicOptionalObjectives.All).Concat(BonusStoryObjectives.All)];

    /// <summary>
    /// The current objectives for a scene. One story objective is always produced above
    /// <see cref="FirstPoint"/>; optional objectives appear only inside their own chain's
    /// scenes, only once unlocked, and never after they are done. A predicate whose flag
    /// could not be read yields no optional objective at all rather than a guess.
    /// </summary>
    public static IReadOnlyList<FullStoryObjective> Build(int scene, FieldStoryState? state)
    {
        if (state is null || state.Point < FirstPoint) return [];
        var result = new List<FullStoryObjective>();
        if (MainChain.FirstOrDefault(chapter => state.Point < chapter.Until) is { } current)
            result.Add(ContextObjective(scene, state) ?? Current(current.Steps, state).Objective(NavigationCategory.StoryEvents));
        result.AddRange(ClassicOptionalObjectives.Build(scene, state));
        result.AddRange(BonusStoryObjectives.Build(scene, state));
        return result;
    }

    // The earliest substep that is not provably finished. An unreadable flag therefore
    // holds the objective at the earlier step instead of skipping ahead, and a chain
    // whose substep flags are all set still produces its tail.
    private static Step Current(Step[] steps, FieldStoryState state) =>
        steps.FirstOrDefault(step => step.Done(state) != true) ?? steps[^1];
}
