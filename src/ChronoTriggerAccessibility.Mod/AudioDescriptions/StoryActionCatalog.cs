using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>Individual PC assemblies and their animation slots were rendered and
/// reviewed from the supported resources. An animation number alone has no meaning.
/// Physical gestures only: no intentions, outcomes, item identities or hidden flags.</summary>
public static class StoryActionCatalog
{
    private static readonly Dictionary<(int Sprite, int Animation), string> Actions = Build();
    public static int ReviewedAnimationCount => Actions.Count;
    public static IReadOnlyList<(int Sprite, int Animation)> ReviewedAnimations { get; } =
        Array.AsReadOnly(Actions.Keys.OrderBy(key => key.Sprite).ThenBy(key => key.Animation).ToArray());

    public static string DescribeScene(StorySceneActionCandidate candidate, Func<int, string?> characterName)
    {
        string Boy() => Subject(0, candidate.Before.StoryPoint, characterName);
        return candidate.Action switch
        {
            StorySceneAction.CurtainsOpen => "Mother opens the curtains, filling the room with sunlight.",
            StorySceneAction.MotherHeadsDownstairs => "Mother heads downstairs.",
            StorySceneAction.BoyGetsOutOfBedAndStretches => $"{Boy()} climbs out of bed and stretches.",
            StorySceneAction.FairCollision => $"{Boy()} and a blonde girl bump into each other and fall to the ground.",
            StorySceneAction.GirlGetsUp => "The blonde girl gets to her feet.",
            StorySceneAction.BoyGetsUp => $"{Boy()} gets to their feet.",
            StorySceneAction.GirlHops => "The blonde girl hops up and down.",
            // Zeal plaza books: stable per-room letters for fixed native actors. The visible
            // change only; no element, sequence, counter or what the books unlock.
            StorySceneAction.BookAOpens => "Book A opens. A flame flickers above it.",
            StorySceneAction.BookACloses => "Book A closes, and the flame disappears.",
            StorySceneAction.BookBOpens => "Book B opens. Water splashes above it.",
            StorySceneAction.BookBCloses => "Book B closes, and the water disappears.",
            StorySceneAction.BookCOpens => "Book C opens. Blue-green wisps swirl above it.",
            StorySceneAction.BookCCloses => "Book C closes, and the swirling wisps disappear.",
            StorySceneAction.PassageOpens => "A passage opens.",
            StorySceneAction.NuApproachesParty => "The Nu walks up to the party.",
            StorySceneAction.NuRaisesArms => "The Nu raises both arms and opens its mouth wide.",
            StorySceneAction.NuSettlesDown => "The Nu settles down beneath the Epoch.",
            // Ocean Palace: only the proven physical change, in the scene that shows it. The white
            // room, flashes, lights and other unproven effects are deliberately not mentioned.
            StorySceneAction.MuneSpinsAndVanishes => "Mune spins around and vanishes.",
            StorySceneAction.MasaSpinsAndVanishes => "Masa spins around and vanishes.",
            StorySceneAction.CronoRaisesHandsToMachine => $"{Boy()} raises both hands toward the machine.",
            StorySceneAction.KnifeRises => "A glowing red knife rises.",
            StorySceneAction.KnifeShootsIntoMachine => "The knife shoots up into the machine.",
            StorySceneAction.SwordAppearsInMachine => "A sword appears in the machine.",
            StorySceneAction.SchalaHoldsOutHands => "Schala holds out both hands.",
            StorySceneAction.PartyGoneSchalaKneels => "Schala kneels alone, head bowed.",
            StorySceneAction.MagusStandsWhereProphetWas => "The hooded Prophet is gone. Magus stands in his place.",
            StorySceneAction.SchalaKnockedToKnees => "Schala drops to her knees.",
            StorySceneAction.MagusThrownDown => "Magus is thrown backward, face down.",
            StorySceneAction.CronoRaisesArmsBeforeLavos => $"{Boy()} raises both arms toward Lavos.",
            StorySceneAction.CronoCrumbles => $"{Boy()} turns into a dark silhouette and crumbles away.",
            _ => throw new ArgumentOutOfRangeException(nameof(candidate)),
        };
    }

    /// <summary>Which book a toggle belongs to and whether it opens it; null for other actions.</summary>
    public static (char Book, bool Opens)? BookToggle(StorySceneAction action) => action switch
    {
        StorySceneAction.BookAOpens => ('A', true),
        StorySceneAction.BookACloses => ('A', false),
        StorySceneAction.BookBOpens => ('B', true),
        StorySceneAction.BookBCloses => ('B', false),
        StorySceneAction.BookCOpens => ('C', true),
        StorySceneAction.BookCCloses => ('C', false),
        _ => null,
    };

    public static string? Describe(StoryActionSnapshot snapshot, int animation, Func<int, string?> characterName)
    {
        // In the opening bedroom this slot runs under the blanket. The camera
        // includes the actor, but only a tuft of hair is visible.
        if (animation == 0x17 && snapshot.Location is
            { Scene: 2, ScriptId: 324, Actor: 1, Address: 0x40F }) return null;
        var actor = snapshot.ActorState;
        if ((actor.ClassTag & 0x7F) is < 0 or > 3 ||
            !Actions.TryGetValue((actor.Visual, animation), out var action)) return null;
        var subject = Subject(actor.Visual, snapshot.StoryPoint, characterName);
        return $"{subject} {action}";
    }

    private static string Subject(int sprite, int storyPoint, Func<int, string?> characterName)
    {
        // Conservative boundaries *after* the installed naming branches: 0074
        // sets 08, 0177's chapter reaches 15, 0347 sets 3C, 0364 sets 6F.
        // Lucca's naming branch is in 0323, before the cathedral chapter ends.
        var introduced = sprite switch
        {
            0 => true,
            1 => storyPoint >= 0x08,
            2 or 4 => storyPoint >= 0x15,
            3 => storyPoint >= 0x3C,
            5 => storyPoint >= 0x6F,
            _ => false,
        };
        if (introduced && characterName(sprite) is { } name && !string.IsNullOrWhiteSpace(name))
            return name.Trim();
        return sprite switch
        {
            0 => "The red-haired boy",
            1 => "The blonde girl",
            2 => "The girl in the purple helmet",
            3 => "The robot",
            4 => "The frog knight",
            5 => "The blonde woman in furs",
            _ => "The person",
        };
    }

    private static Dictionary<(int, int), string> Build()
    {
        var result = new Dictionary<(int, int), string>();
        void Add(int sprite, int animation, string text) => result.Add((sprite, animation), text);
        // Each of these six separate assemblies was checked in all four facings.
        for (var sprite = 0; sprite <= 5; sprite++)
        {
            Add(sprite, 0x12, "gets to their feet.");
            Add(sprite, 0x16, "nods.");
            Add(sprite, 0x17, "shakes their head.");
            Add(sprite, 0x1B, "covers their face.");
            Add(sprite, 0x1D, "is crouched on the ground.");
        }
        foreach (var sprite in new[] { 0, 1, 2, 4, 5 }) Add(sprite, 0x1A, "laughs.");
        Add(3, 0x1A, "rocks their upper body.");
        foreach (var sprite in new[] { 0, 1, 3, 5 })
        {
            Add(sprite, 0x10, "raises their arms.");
            Add(sprite, 0x22, "waves their arms.");
        }
        Add(4, 0x10, "spreads their arms.");
        Add(4, 0x22, "waves their arms.");
        Add(4, 0x11, "holds their arms out.");
        foreach (var sprite in new[] { 0, 1 }) Add(sprite, 0x11, "holds their arms up.");
        // Robo has no frames in 11. Ayla's three-frame 11 is a different motion.
        Add(5, 0x11, "raises their arms, then crouches.");
        Add(2, 0x11, "raises a hand.");
        Add(2, 0x20, "raises their arms overhead.");
        Add(0, 0x23, "holds out their sword.");
        Add(1, 0x23, "raises their crossbow.");
        Add(2, 0x23, "raises their gun.");
        Add(4, 0x23, "holds out their sword.");
        Add(1, 0x0B, "jumps up.");
        return result;
    }
}
