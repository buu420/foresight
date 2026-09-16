using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>PC bonus quests, selected by the flags written by their native scripts.
/// See docs/bonus-story-objectives.md for the script and item evidence.</summary>
public static class BonusStoryObjectives
{
    private const int Sand = 0x5019, Hammer = 0x501A, Prism = 0x501B, Saint = 0x501C;
    private const int Mark = 0x501D, Waystone = 0x501E, Wood = 0x501F, Steel = 0x5020;
    private const int Vines = 0x5021, Lunch = 0x5022, Blade = 0x5023, Lumicite = 0x5024, Banana = 0x401E;
    private static StoryGoal Actor(int scene, params int[] actors) => new(scene, actors, []);
    private static StoryGoal Arrival(int scene) => new(scene, [], []);
    private static StoryGoal Target(int scene, string target) => new(scene, [], [target]);

    // Resolve only the identities of audited spatial script actions. A missing
    // compiler binding must never silently turn a spatial action into Arrival.
    private static StoryGoal Region(int scene, int left, int top, int right, int bottom) =>
        new(scene, [], Regions(scene).Where(r => r.Left == left && r.Top == top &&
            r.Right == right && r.Bottom == bottom && r.Kind is not "Warp")
            .Select(r => r.Id).Distinct().DefaultIfEmpty($"unbound-bonus-region:{scene}:{left}:{top}:{right}:{bottom}").ToArray());
    private static StoryGoal Switch(int scene, int index, int value) =>
        new(scene, [], Regions(scene).Where(r => r.Kind == "Switch" && r.Source == "Extra" &&
            r.Index == index && r.Value == value && r.Set).Select(r => r.Id).Distinct()
            .DefaultIfEmpty($"unbound-bonus-switch:{scene}:{index}:{value}").ToArray());
    private static IEnumerable<GameNavigationCatalog.Region> Regions(int scene) =>
        GameNavigationCatalog.ForScene(scene)?.Regions ?? [];

    private static FullStoryObjective S(string id, string label, StoryGoal goal,
        NavigationCategory category = NavigationCategory.Objects, string? instruction = null) =>
        new("bonus:sanctum:" + id, label, [goal], instruction, category);
    private static FullStoryObjective V(string id, string label, StoryGoal goal,
        NavigationCategory category = NavigationCategory.Objects, string? instruction = null) =>
        new("bonus:vortex:" + id, label, [goal], instruction, category);

    private static readonly FullStoryObjective[] entries =
    [
        S("arrival", "Enter the Lost Sanctum", Arrival(653), NavigationCategory.Exits),
        S("forest:group-1", "Clear the monsters from the prehistoric Millennia Wood", Actor(587, 10, 11, 12)),
        S("forest:group-2", "Clear the monsters from the prehistoric Millennia Wood", Actor(587, 13)),
        S("forest:group-3", "Clear the monsters from the prehistoric Millennia Wood", Region(587, 22, 23, 29, 31)),
        S("forest:group-4", "Clear the monsters from the prehistoric Millennia Wood", Actor(587, 22)),
        S("forest:group-6", "Clear the monsters from the prehistoric Millennia Wood", Region(587, 9, 15, 18, 22)),
        S("forest:report", "Return to the prehistoric village after clearing the wood", Switch(583, 0x11, 8), NavigationCategory.People),
        S("middle-forest:request", "Meet the villagers in the Lost Sanctum, 600 AD", Switch(585, 0x18, 1), NavigationCategory.People),
        S("middle-forest:group-1", "Clear the monsters from Millennia Wood, 600 AD", Actor(589, 9, 11)),
        S("middle-forest:group-2", "Clear the monsters from Millennia Wood, 600 AD", Actor(589, 13)),
        S("middle-forest:group-3", "Clear the monsters from Millennia Wood, 600 AD", Actor(589, 17)),
        S("middle-forest:group-4", "Clear the monsters from Millennia Wood, 600 AD", Region(589, 8, 53, 15, 60)),
        S("middle-forest:group-5", "Clear the monsters from Millennia Wood, 600 AD", Region(589, 8, 18, 16, 24)),
        S("middle-forest:report", "Report to the villagers after clearing Millennia Wood, 600 AD", Switch(585, 0x18, 3), NavigationCategory.People),
        S("hammer:request", "Ask the prehistoric villager about a Golden Hammer", Actor(583, 9), NavigationCategory.People),
        S("hammer:sand", "Collect Golden Sand in the Great Southern Swamp", Actor(576, 9)),
        S("hammer:sapling", "Use Golden Sand on the sapling in Millennia Wood", Actor(587, 48)),
        S("hammer:tracks", "Investigate the golden tree's site in Millennia Wood, 600 AD", Switch(589, 0x1B, 8)),
        S("hammer:goldhammer", "Catch the Goldhammer in Millennia Wood, 600 AD", Region(589, 18, 47, 34, 51)),
        S("hammer:deliver", "Bring the Golden Hammer to the prehistoric villager", Actor(583, 9), NavigationCategory.People),
        S("prism:request", "Ask the prehistoric villager about the Prismastone", Actor(583, 10), NavigationCategory.People),
        S("prism:guardian", "Challenge the Nu Guardian on prehistoric Mount Emerald", Actor(532, 9), NavigationCategory.People),
        S("prism:collect", "Collect the Prismastone at Mount Emerald's summit", Actor(535, 9)),
        S("prism:deliver", "Show the Prismastone to the prehistoric villager", Actor(583, 10), NavigationCategory.People),
        S("saint:request", "Ask the prehistoric villager about the Saintstone", Actor(583, 14), NavigationCategory.People),
        S("saint:recover-left", "Recover the Prismastone from the left prehistoric altar", Actor(584, 8)),
        S("saint:recover-right", "Recover the Prismastone from the right prehistoric altar", Actor(584, 9)),
        new("bonus:sanctum:saint:first-altar", "Place the Prismastone on a prehistoric shrine altar",
            [Actor(584, 8, 9)], Category: NavigationCategory.Objects),
        S("saint:elder", "Ask the elder in 600 AD for access to the shrine", Actor(585, 8), NavigationCategory.People),
        S("saint:mark", "Recover the Reptmark in Millennia Wood, 600 AD", Actor(589, 8)),
        S("saint:mark-return", "Show the Reptmark to the elder in 600 AD", Actor(585, 8), NavigationCategory.People),
        S("saint:future-left", "Take the Prismastone from the left altar in 600 AD", Actor(586, 8)),
        S("saint:future-right", "Take the Prismastone from the right altar in 600 AD", Actor(586, 9)),
        S("saint:past-left", "Place the second Prismastone on the empty left prehistoric altar", Actor(584, 8)),
        S("saint:past-right", "Place the second Prismastone on the empty right prehistoric altar", Actor(584, 9)),
        S("saint:deliver", "Show the Saintstone to the prehistoric villager", Actor(583, 14), NavigationCategory.People),
        S("nu:request", "Ask about the Nu on Mount Emerald, 600 AD", Actor(585, 9), NavigationCategory.People),
        S("nu:wake", "Speak to the Nu at the foot of Mount Emerald, 600 AD", Actor(536, 18), NavigationCategory.People),
        S("nu:ladder", "Inspect the broken ladder on Mount Emerald, 600 AD", Switch(536, 0x15, 0x40)),
        S("nu:vines", "Collect Sturdy Vines in the Southern Glade", Actor(577, 8)),
        S("nu:try-vines", "Bring the Sturdy Vines to the broken ladder in 600 AD", Switch(536, 0x1E, 0x80)),
        S("nu:hang-vines", "Hang the Sturdy Vines from prehistoric Mount Emerald", Switch(532, 0x1B, 0x20)),
        S("nu:master", "Meet the Nu Master atop Mount Emerald, 600 AD", Arrival(538), NavigationCategory.People),
        S("nu:report", "Tell the villager in 600 AD about the Nu Master", Actor(585, 9), NavigationCategory.People),
        S("materials:request", "Ask the bridge builder in 600 AD what he needs", Actor(585, 14), NavigationCategory.People),
        S("materials:wood", "Ask the prehistoric woodcutter for Godwood", Actor(587, 49), NavigationCategory.People),
        S("materials:hammer", "Borrow the Golden Hammer from the prehistoric villager", Actor(583, 9), NavigationCategory.People),
        S("materials:steel", "Search the Winding Passage in 600 AD for the Steel Ingot", Target(581, "chest:301")),
        S("materials:deliver", "Give the bridge builder the materials you are carrying", Actor(585, 14), NavigationCategory.People),
        S("bridge:revisit", "Revisit the Lost Sanctum entrance while the builder is away", Arrival(654), NavigationCategory.Exits),
        S("bridge:request", "Ask the villager in 600 AD about the missing bridge builder", Actor(585, 12), NavigationCategory.People),
        S("bridge:nu", "Ask the Nu Master about the missing bridge builder", Actor(538, 9), NavigationCategory.People),
        S("bridge:rescue", "Reach the stranded builder at the Great Bridge", Arrival(556), NavigationCategory.People),
        S("bridge:help", "Ask the Nu Master to help build the bridge", Actor(538, 9), NavigationCategory.People),
        S("bridge:work", "Return to the Great Bridge with the Nu Master", Arrival(556), NavigationCategory.People),
        S("bridge:lunch", "Ask the villager in 600 AD for the builder's lunch", Actor(585, 9), NavigationCategory.People),
        S("bridge:deliver-lunch", "Bring the Hearty Lunch to the bridge builder", Actor(556, 9), NavigationCategory.People),
        S("bridge:banana", "Search the Great Southern Swamp for the Nu's sweet fruit", Region(576, 40, 5, 48, 10)),
        S("bridge:feed", "Bring the Sweet Banana to the Nu at the Great Bridge", Actor(556, 8), NavigationCategory.People),
        S("bridge:finish", "Return to the Great Bridge after feeding the Nu and defending the village", Arrival(556), NavigationCategory.People),
        S("bridge:report", "Return to the village in 600 AD after completing the bridge", Switch(585, 0x19, 0x20), NavigationCategory.People),
        S("waystone:hint", "Ask the prehistoric villager where to place the Saintstone", Actor(583, 12), NavigationCategory.People),
        S("waystone:place", "Place the Saintstone on prehistoric Mount Emerald", Switch(535, 0x12, 0x20)),
        S("waystone:collect", "Collect the Waystone atop Mount Emerald, 600 AD", Actor(539, 8)),
        S("cave:request", "Show the Waystone to the prehistoric cave explorer", Actor(583, 13), NavigationCategory.People),
        S("cave:light", "Bring the Waystone into the Lightless Cave", Arrival(580)),
        S("cave:fortress", "Explore the entrance of the Primeval Fortress", Arrival(540)),
        S("cave:report", "Warn the prehistoric village about the fortress", Arrival(583), NavigationCategory.People),
        S("defend:leaders", "Confront the enemy leaders in the Primeval Fortress", Arrival(555)),
        S("defend:report", "Return to the prehistoric village after defending it", Arrival(583), NavigationCategory.People),
        S("tower:request", "Meet the villagers in 600 AD about the tower beyond the bridge", Switch(585, 0x19, 0x40), NavigationCategory.People),
        S("tower:guardians", "Confront the guardians at the top of the Tower of the Ancients", Region(575, 12, 18, 18, 19)),
        S("tower:idols", "Investigate the room beyond the tower guardians", Arrival(652)),
        S("tower:report", "Report the tower discovery to the village in 600 AD", Switch(585, 0x19, 0x80), NavigationCategory.People),
        S("smith:request", "Ask the prehistoric smith about a rusted blade", Actor(583, 11), NavigationCategory.People),
        S("smith:blade", "Search the Tower of the Ancients for the Rusted Blade", Target(570, "chest:286")),
        S("smith:deliver", "Bring the Rusted Blade to the prehistoric smith", Actor(583, 11), NavigationCategory.People),
        S("lumicite:request", "Ask the smith in 600 AD about lumicite", Actor(585, 11), NavigationCategory.People),
        S("lumicite:hunt-537", "Look for the smith's lumicite on the Wonder Rock", Actor(537, 15), instruction: "The Wonder Rock appears only on some visits."),
        S("lumicite:hunt-587", "Look for the smith's lumicite on the Wonder Rock", Actor(587, 24), instruction: "The Wonder Rock appears only on some visits."),
        S("lumicite:hunt-589", "Look for the smith's lumicite on the Wonder Rock", Actor(589, 29), instruction: "The Wonder Rock appears only on some visits."),
        S("lumicite:deliver", "Bring the Lumicite Shard to the smith in 600 AD", Actor(585, 11), NavigationCategory.People),
        V("present:enter", "Explore the Dimensional Vortex in 1000 AD", Switch(595, 0x28, 0), NavigationCategory.Exits),
        V("future:enter", "Explore the Dimensional Vortex in 2300 AD", Switch(661, 0x28, 0), NavigationCategory.Exits),
        V("antiquity:enter", "Explore the Dimensional Vortex in 12000 BC", Switch(662, 0x28, 0), NavigationCategory.Exits),
        new("bonus:vortex:shifting-path", "Follow the shifting passages to the Nameless Cave",
            [Actor(601, 10), Actor(663, 10), Actor(664, 10)], Category: NavigationCategory.Exits),
        V("present:cave", "Take the forward Gate from the Nameless Cave", Actor(601, 10), NavigationCategory.Exits),
        V("antiquity:cave", "Take the forward Gate from the Nameless Cave", Actor(664, 10), NavigationCategory.Exits),
        V("future:cave", "Take the forward Gate from the Nameless Cave", Actor(663, 10), NavigationCategory.Exits),
        V("present:switch", "Press the volcano entrance switch", Switch(608, 0x23, 1)),
        V("present:ladder", "Press the crater surveillance ladder switch", Switch(613, 0x23, 2)),
        V("present:dalton", "Confront Dalton in the Twilight Grotto", Region(624, 0, 27, 255, 27), NavigationCategory.People),
        V("present:grotto", "Follow Crono into the Twilight Grotto", Region(624, 28, 7, 28, 7), NavigationCategory.Exits,
            "Bring Crono in the active party."),
        V("antiquity:grotto", "Follow Marle into the Twilight Grotto", Region(666, 28, 7, 28, 7), NavigationCategory.Exits,
            "Bring Marle in the active party."),
        V("future:grotto", "Follow Lucca into the Twilight Grotto", Region(665, 28, 7, 28, 7), NavigationCategory.Exits,
            "Bring Lucca in the active party."),
        V("antiquity:shade", "Approach the Alabaster Shade in the Castle Dreamscape", Region(645, 35, 51, 46, 51)),
        V("present:shade", "Confront the Steel Shade in the Leene Square Dreamscape", Arrival(646)),
        V("future:shade", "Confront the Crimson Shade in the Household Dreamscape", Arrival(647)),
        V("future:security", "Use the Temporal Research Lab security console", Actor(620, 8)),
        V("future:door", "Return to the laboratory security door", Actor(618, 13)),
        V("future:rescuers", "Meet the other party members at the laboratory entrance", Arrival(617), NavigationCategory.People),
        V("future:alarm", "Reach the east laboratory's security chamber", Arrival(622)),
        V("future:register", "Register an administrator at the east laboratory console", Actor(622, 8)),
        V("future:release", "Release the security locks using the laboratory console", Actor(620, 8)),
        new("bonus:eclipse:gaspar", "Ask Gaspar about the three completed Dimensional Vortices",
            [Actor(464, 28)], Category: NavigationCategory.People),
        new("bonus:eclipse:gate", "Use the End of Time Gate to reach Time's Eclipse",
            [Actor(464, 9)], "Choose Time's Eclipse.", NavigationCategory.Exits),
        new("bonus:eclipse:devourer", "Enter the portal to confront the Dream Devourer",
            [Actor(591, 16)], "Choose Fight when ready.", NavigationCategory.Exits),
    ];
    private static readonly IReadOnlyDictionary<string, FullStoryObjective> byId = entries.ToDictionary(e => e.Id);
    public static IReadOnlyList<FullStoryObjective> All { get; } = Array.AsReadOnly(entries);

    public static IReadOnlyList<FullStoryObjective> Build(int scene, FieldStoryState? state)
    {
        if (state is null) return [];
        var result = new List<FullStoryObjective>();
        var world = scene is >= 496 and <= 502;
        var sanctum = scene is >= 532 and <= 587 or 589 or 652 or 653 or 654 or 668;
        if ((world && state.Point >= 0xD2 || sanctum) &&
            Enumerable.Range(0x10, 0x10).All(i => state.Extra(i).HasValue))
            Sanctum(state, result);
        if (world || scene == 464 || scene is >= 591 and <= 650 or >= 655 and <= 666)
            Vortex(scene, state, result, world);
        // A random spawn can only bind in its own room, to its current native
        // actor. Do not route across eras to an enemy that may not have spawned.
        if (scene is 537 or 587 or 589 && Has(state, 0x14, 0x40) && !Has(state, 0x14, 0x80) &&
            state.ItemCount(Lumicite) == 0)
            Add(result, "sanctum:lumicite:hunt-" + scene);
        return result;
    }

    private static bool Has(FieldStoryState s, int cell, int mask) => s.Extra(cell) is { } n && (n & mask) == mask;
    private static void Add(List<FullStoryObjective> result, string id) => result.Add(byId["bonus:" + id]);
    private static void Sanctum(FieldStoryState s, List<FullStoryObjective> result)
    {
        void AddS(string id) => Add(result, "sanctum:" + id);
        bool H(int c, int m) => Has(s, c, m);
        int? I(int item) => s.ItemCount(item);

        if (!H(0x1F, 1)) { AddS("arrival"); return; }
        if (!H(0x12, 8))
        {
            var groups = new[] { (4, "1"), (8, "2"), (16, "3"), (32, "4"), (128, "6") };
            foreach (var (mask, group) in groups)
                if (!H(0x16, mask)) { AddS("forest:group-" + group); return; }
            return; // The forest controller records completion on its next tick.
        }
        if (!H(0x11, 8)) { AddS("forest:report"); return; }

        // Independent quests can be active together. Completed hand-ins precede
        // inventory checks because the native scripts consume their quest items.
        if (s.Extra(0x18) == 0) AddS("middle-forest:request");
        else if (s.Extra(0x18) == 1)
        {
            for (var i = 0; i < 5; i++)
                if (!H(0x1C, 1 << i)) { AddS("middle-forest:group-" + (i + 1)); break; }
        }
        else if (s.Extra(0x18) == 2) AddS("middle-forest:report");

        if (!H(0x10, 2))
        {
            if (!H(0x10, 1)) AddS("hammer:request");
            else if (I(Hammer) > 0) AddS("hammer:deliver");
            else if (!H(0x12, 0x10))
            {
                if (I(Sand) > 0) AddS("hammer:sapling");
                else if (I(Sand) == 0 && !H(0x12, 0x40)) AddS("hammer:sand");
            }
            else if (!H(0x1B, 8)) AddS("hammer:tracks");
            else if (!H(0x1B, 0x10) && s.Extra(0x18) >= 2) AddS("hammer:goldhammer");
        }
        if (H(0x10, 2) && !H(0x10, 8))
        {
            if (!H(0x10, 4)) AddS("prism:request");
            else if (I(Prism) > 0) AddS("prism:deliver");
            else if (!H(0x1F, 0x20) && I(Prism) == 0) AddS("prism:guardian");
            else if (!H(0x17, 0x80) && I(Prism) == 0) AddS("prism:collect");
        }
        if (H(0x10, 8) && !H(0x10, 0x20))
        {
            if (!H(0x10, 0x10))
            {
                // The offer tests the held Prismastone. Placing it before
                // accepting the quest is reversible at either occupied altar.
                if (I(Prism) > 0) AddS("saint:request");
                else if (I(Prism) == 0 && (H(0x1F, 0x40) || H(0x1F, 0x80)))
                    AddS(H(0x1F, 0x40) ? "saint:recover-left" : "saint:recover-right");
            }
            else if (I(Saint) > 0) AddS("saint:deliver");
            else if (I(Prism) > 0 && !H(0x1F, 0xC0))
                AddS(!H(0x1F, 0x40) && !H(0x1F, 0x80) ? "saint:first-altar" :
                    H(0x1F, 0x40) ? "saint:past-right" : "saint:past-left");
            else if (I(Prism) == 0 && I(Saint) == 0 && (H(0x1F, 0x40) || H(0x1F, 0x80)))
            {
                if (!H(0x1A, 0x80))
                {
                    if (I(Mark) > 0) AddS("saint:mark-return");
                    else if (s.Extra(0x18) == 4 && I(Mark) == 0) AddS("saint:mark");
                    else if (s.Extra(0x18) >= 3) AddS("saint:elder");
                }
                else if (H(0x1B, 2)) AddS("saint:future-left");
                else if (H(0x1B, 4)) AddS("saint:future-right");
            }
        }
        if (H(0x10, 8) && s.Extra(0x18) >= 2 && !H(0x19, 8))
        {
            if (!H(0x19, 4)) AddS("nu:request");
            else if (H(0x15, 0x80)) AddS("nu:report");
            else if (!H(0x1D, 1)) AddS("nu:wake");
            else if (H(0x1B, 0x20)) AddS("nu:master");
            else if (!H(0x15, 0x40)) AddS("nu:ladder");
            else if (I(Vines) > 0) AddS(H(0x1E, 0x80) ? "nu:hang-vines" : "nu:try-vines");
            else if (I(Vines) == 0) AddS("nu:vines");
        }
        if (s.Extra(0x18) >= 3 && H(0x10, 2) && !H(0x19, 2))
        {
            if (!H(0x19, 1)) AddS("materials:request");
            else if (!H(0x1A, 1) && I(Wood) > 0 || !H(0x1A, 2) && I(Hammer) > 0 || !H(0x1A, 4) && I(Steel) > 0)
                AddS("materials:deliver");
            else if (!H(0x1A, 1) && I(Wood) == 0) AddS("materials:wood");
            else if (!H(0x1A, 2) && I(Hammer) == 0) AddS("materials:hammer");
            else if (!H(0x1A, 4) && I(Steel) == 0) AddS("materials:steel");
        }
        if (H(0x19, 0x0A) && !H(0x19, 0x20))
        {
            if (H(0x1A, 0x40)) AddS("bridge:report");
            else if (!H(0x19, 0x10)) AddS(H(0x1A, 0x20) ? "bridge:request" : "bridge:revisit");
            else if (!H(0x16, 2)) AddS("bridge:nu");
            else if (!H(0x1D, 2)) AddS("bridge:rescue");
            else if (!H(0x15, 8)) AddS(H(0x1B, 0x40) ? "bridge:work" : "bridge:help");
            else if (!H(0x1B, 0x80))
            {
                if (I(Lunch) > 0) AddS("bridge:deliver-lunch");
                else if (I(Lunch) == 0) AddS("bridge:lunch");
            }
            else if (!H(0x1D, 0x80))
            {
                if (I(Banana) > 0) AddS("bridge:feed");
                else if (I(Banana) == 0) AddS("bridge:banana");
            }
            else if (H(0x11, 2)) AddS("bridge:finish");
        }
        if (H(0x10, 0x20) && !H(0x11, 1))
        {
            if (H(0x14, 1)) AddS("cave:report");
            else if (H(0x13, 1)) AddS("cave:fortress");
            else if (H(0x10, 0x40) && I(Waystone) > 0) AddS("cave:light");
            else if (I(Waystone) > 0) AddS("cave:request");
            else if (H(0x12, 0x20) && !H(0x1F, 4)) AddS("waystone:collect");
            else if (!H(0x12, 0x20) && I(Saint) > 0 && s.Extra(0x31).HasValue)
                AddS(H(0x31, 1) ? "waystone:place" : "waystone:hint");
        }
        if (H(0x11, 1) && !H(0x11, 2)) AddS(H(0x14, 2) ? "defend:report" : "defend:leaders");
        if (H(0x19, 0x20) && !H(0x19, 0x80))
            AddS(!H(0x19, 0x40) ? "tower:request" : H(0x1B, 1) ? "tower:report" :
                H(0x1F, 8) ? "tower:idols" : "tower:guardians");
        if (!H(0x14, 0x20) && (!H(0x11, 1) || H(0x11, 2)))
        {
            if (I(Blade) > 0) AddS("smith:deliver");
            else if (!H(0x14, 0x10)) AddS("smith:request");
            else if (H(0x1A, 0x40) && I(Blade) == 0) AddS("smith:blade");
        }
        if (!H(0x14, 0x80))
        {
            if (I(Lumicite) > 0) AddS("lumicite:deliver");
            else if (!H(0x14, 0x40)) AddS("lumicite:request");
            // The optional local hunt binding is selected separately in Build.
        }
    }

    private static void Vortex(int scene, FieldStoryState s, List<FullStoryObjective> result, bool world)
    {
        if (s.Extra(0x0A) is not { } stage || stage == 255 || s.Extra(0x22) is not { } clears) return;
        void AddV(string id) => Add(result, "vortex:" + id);
        if (world || scene == 464)
        {
            // E0A and E22 are both zero before the first clear. The native
            // world scripts enable these entrances from Global1FB, whose 0x45
            // value is written by the clear-save setup at RVA 2B214E.
            if ((clears & 1) == 0 && s.Flag(0x1FB, 0x40) == true) AddV("antiquity:enter");
            if ((clears & 4) == 0 && s.Flag(0x1FB, 1) == true) AddV("present:enter");
            if ((clears & 2) == 0 && s.Flag(0x1FB, 4) == true) AddV("future:enter");
            if (stage == 1) Add(result, "eclipse:gaspar");
            else if (stage >= 2) Add(result, "eclipse:gate");
            return;
        }
        if (scene == 591 && stage >= 2) { Add(result, "eclipse:devourer"); return; }
        if (scene is >= 592 and <= 594) return; // Native battle and ending sequence.
        if (scene is 595 or 661 or 662)
        {
            var mask = scene == 595 ? 4 : scene == 661 ? 2 : 1;
            if ((clears & mask) == 0) AddV(scene == 595 ? "present:enter" : scene == 661 ? "future:enter" : "antiquity:enter");
        }
        else if (scene is >= 625 and <= 644 or >= 596 and <= 600) AddV("shifting-path");
        else if (scene is 601 or 663 or 664)
            AddV(scene == 601 ? "present:cave" : scene == 663 ? "future:cave" : "antiquity:cave");
        else if (scene == 645 && (clears & 1) == 0) AddV("antiquity:shade");
        else if (scene == 646 && (clears & 4) == 0) AddV("present:shade");
        else if (scene == 647 && (clears & 2) == 0) AddV("future:shade");
        else if (scene is >= 602 and <= 607 or 648 or 666 && (clears & 1) == 0) AddV("antiquity:grotto");
        else if (scene is >= 608 and <= 615 or 624 or 649 or 655 && (clears & 4) == 0)
        {
            if (s.Extra(0x23) is null || s.Extra(0x21) is null) return;
            if (scene != 624 && !Has(s, 0x23, 1)) AddV("present:switch");
            else if (scene != 624 && !Has(s, 0x23, 2)) AddV("present:ladder");
            else AddV(Has(s, 0x21, 0x20) ? "present:grotto" : "present:dalton");
        }
        else if (scene is >= 616 and <= 623 or 650 or 665 && (clears & 2) == 0)
        {
            if (scene == 665) { AddV("future:grotto"); return; }
            if (s.Extra(0x2A) is null) return;
            if (Has(s, 0x2A, 4)) AddV(Has(s, 0x2A, 1) ? "future:grotto" : "future:release");
            else if (!Has(s, 0x2A, 2))
                AddV(!Has(s, 0x2A, 1) ? "future:security" : scene == 617 ? "future:rescuers" : "future:door");
            else AddV(Has(s, 0x2A, 0x10) ? "future:register" : "future:alarm");
        }
    }
}
