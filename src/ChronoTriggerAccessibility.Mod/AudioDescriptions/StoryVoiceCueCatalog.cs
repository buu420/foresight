using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Core.Startup;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

public sealed record StoryNarration(string Text, string CueId, string VoiceText);
public sealed record StoryVoiceCueDefinition(string Id, string Text);

public static class StoryVoiceCueCatalog
{
    public static StoryNarration? ForOpening(string text)
    {
        for (var index = 0; index < OpeningMovieTimeline.Entries.Count; index++)
            if (text == OpeningMovieTimeline.Entries[index].Text)
            {
                var id = $"movie-opening-{index:D2}";
                return new(text, id, Texts[id]);
            }
        return null;
    }
    private static readonly string[] Names = ["Crono", "Marle", "Lucca", "Robo", "Frog", "Ayla"];
    public static IReadOnlyList<StoryVoiceCueDefinition> All { get; } = Build();
    private static readonly IReadOnlyDictionary<string, string> Texts =
        All.ToDictionary(cue => cue.Id, cue => cue.Text, StringComparer.Ordinal);

    public static StoryNarration ForAnimation(StoryActionSnapshot before, int animation, string text)
    {
        var sprite = before.ActorState.Visual;
        var named = StoryActionCatalog.Describe(before with { StoryPoint = 0x100 }, animation, Name);
        // A recording must never silently substitute a default identity for a
        // custom name. An appearance description stays true without online TTS.
        var id = $"party-{sprite:X2}-{animation:X2}-{(text == named ? "named" : "appearance")}".ToLowerInvariant();
        return new(text, id, Texts[id]);
    }

    public static StoryNarration ForScene(StorySceneActionCandidate scene, string text)
    {
        var id = SceneId(scene.Action);
        if (HasBoyName(scene.Action))
            id += text == StoryActionCatalog.DescribeScene(scene, Name) ? "-named" : "-appearance";
        return new(text, id, Texts[id]);
    }

    private static IReadOnlyList<StoryVoiceCueDefinition> Build()
    {
        var result = new List<StoryVoiceCueDefinition>();
        foreach (var (sprite, animation) in StoryActionCatalog.ReviewedAnimations)
        {
            var frame = Frame(sprite);
            var prefix = $"party-{sprite:X2}-{animation:X2}".ToLowerInvariant();
            result.Add(new(prefix + "-named", StoryActionCatalog.Describe(frame, animation, Name)!));
            result.Add(new(prefix + "-appearance", StoryActionCatalog.Describe(frame, animation, _ => null)!));
        }
        foreach (var action in Enum.GetValues<StorySceneAction>())
        {
            var before = Frame(0);
            var scene = new StorySceneActionCandidate(action, before, before.ActorState, null, false);
            var id = SceneId(action);
            if (HasBoyName(action))
            {
                result.Add(new(id + "-named", StoryActionCatalog.DescribeScene(scene, Name)));
                result.Add(new(id + "-appearance", StoryActionCatalog.DescribeScene(scene, _ => null)));
            }
            else result.Add(new(id, StoryActionCatalog.DescribeScene(scene, Name)));
        }
        for (var index = 0; index < OpeningMovieTimeline.Entries.Count; index++)
            result.Add(new($"movie-opening-{index:D2}", OpeningMovieTimeline.Entries[index].Text));
        return result.AsReadOnly();
    }

    private static string? Name(int sprite) => sprite >= 0 && sprite < Names.Length ? Names[sprite] : null;
    private static string SceneId(StorySceneAction action) => "scene-" + action.ToString().ToLowerInvariant();
    private static bool HasBoyName(StorySceneAction action) => action is
        StorySceneAction.BoyGetsOutOfBedAndStretches or StorySceneAction.FairCollision or StorySceneAction.BoyGetsUp;
    private static StoryActionSnapshot Frame(int sprite) => new(
        new(0, 0, 0, 0, 0, 1, 1, 0, 1, 0, 0xAA), "AA00000000000000",
        new(0, 0, sprite, 1, 0, 0, 0, 0, 0, 0, 0, 0, true, true), 0x100, 0, 0, 0, -1);
}
