using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Opening guide references bound to installed scene scripts and live,
/// already available targets. See docs/navigation-counted-guidance.md for evidence.</summary>
public static class OpeningStoryTargets
{
    public static IReadOnlyList<NavigationTarget> Build(int scene, FieldStoryState? story,
        IReadOnlyList<NavigationTarget> available)
    {
        if (story is not { Point: 3 }) return [];
        var binding = (scene, story.MotherIntroducedFriend) switch
        {
            (2, false) => ("exit:0", "Go downstairs"),
            (1, false) => ("actor:15:4:37", "Talk with Mother"),
            (1, true) => ("exit:0", "Leave for the Millennial Fair"),
            _ => (string.Empty, string.Empty),
        };
        var target = available.FirstOrDefault(t => t.Id == binding.Item1 && (t.Visible || t.Discovered));
        return target is null ? [] : [target with
        {
            Id = "story:" + target.Id, Label = binding.Item2, Category = NavigationCategory.StoryEvents,
        }];
    }

    public static string? ActorLabel(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) switch
        {
            (1, 15, 4, 0x25) or (2, 8, 4, 0x25) => "Mother",
            (8, 11, 4, 0x63) => "Telepod",
            _ => null,
        };

    public static string ExitLabel(int scene, int exit) => (scene, exit) switch
    {
        (2, 0) => "Stairs downstairs",
        (1, 1) => "Stairs upstairs",
        (1, 0) => "Front door",
        _ => "Exit",
    };
}
