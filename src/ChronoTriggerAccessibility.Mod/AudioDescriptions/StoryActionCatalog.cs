using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>Individual PC assemblies and their animation slots were rendered and
/// reviewed from the supported resources. An animation number alone has no meaning.
/// Physical gestures only: no intentions, outcomes, item identities or hidden flags.</summary>
public static class StoryActionCatalog
{
    private static readonly Dictionary<(int Sprite, int Animation), string> Actions = Build();
    public static int ReviewedAnimationCount => Actions.Count;

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
            _ => throw new ArgumentOutOfRangeException(nameof(candidate)),
        };
    }

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
