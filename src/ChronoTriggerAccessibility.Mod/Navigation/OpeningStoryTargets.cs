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
            (2, _) => ("exit:0", "Go downstairs"),
            (1, false) => ("actor:15:4:37", "Talk with Mother"),
            (1, true) => ("exit:0", "Leave for the Millennial Fair"),
            _ => (string.Empty, string.Empty),
        };
        return binding.Item1.Length == 0 ? [] : [StoryTarget.Bind(binding.Item1, binding.Item2, available)];
    }

    public static string? ActorLabel(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) switch
        {
            (1, 15, 4, 0x25) or (2, 8, 4, 0x25) => "Mother",
            (8, 11, 4, 0x63) or (439, 15, 4, 0x63) => "Fallen pendant",
            (8, 13, 3, 2) => "Lucca",
            (8, 10, 4, 0x27) => "Taban",
            (439, 3, 3, 1) => "Young woman",
            (5, 17, 4, 0x50) => "Fairgoer by the fountain",
            (72, 22, 4, 41) => "Prisoner at the guillotine",
            (122, 9, 4, 51) => "Guard outside the queen's room",
            (29, 12, 4, 162) => "Document on the floor",
            _ => null,
        };

    public static string ExitLabel(int scene, int exit) => (scene, exit) switch
    {
        (2, 0) => "Stairs downstairs",
        (1, 1) => "Stairs upstairs",
        (1, 0) => "Front door",
        (5, 0) => "Leave Leene Square",
        (5, 1) => "Rear plaza",
        (439, 0) => "Central plaza",
        (439, 1) => "Lucca's exhibit",
        (439, 2) => "Gato's exhibit",
        (439, 3) => "Prehistoric dancers",
        (8, 0) => "Rear plaza",
        (6, 0) or (7, 0) => "Return to the fair",
        (113, 0) => "Truce Canyon",
        (112, 0) => "Leave Truce Canyon",
        (112, 1) => "Arrival clearing",
        (119, 0) => "South forest exit",
        (119, 1) => "North forest exit toward the castle",
        (120, 0) => "Leave Guardia Castle",
        (120, 1) => "Knights' quarters",
        (120, 2) => "King's tower stairs",
        (120, 3) => "Queen's tower stairs",
        (120, 4) => "Dining hall",
        (468 or 480, >= 0 and <= 2) => "Stairs up",
        (468 or 480, >= 3 and <= 5) => "Stairs down",
        (121 or 122, 0) => "Tower stairs",
        (123 or 124, 0) => "Main hall",
        (129, 0) => "Leave the cathedral",
        (129, 1) => "Cathedral passage",
        (130, >= 0 and <= 3) => "Side room",
        (130, 4) => "Chapel",
        (130, 5) => "Inner cathedral",
        (131, 0) => "Front hall",
        (131, 1) => "Northern passage",
        (132, 0) => "Inner cathedral",
        (132, 1) => "Inner chamber",
        (198, 0) => "Cathedral passage",
        (199 or 201 or 202, 0) => "Front hall",
        (200, 0) => "Front hall",
        (203, 0) => "Return to the side room",
        (19, 0) => "Forest clearing",
        (19, 1) => "South forest exit",
        (19, 2) => "North forest exit toward the castle",
        (20, 0) => "Guardia Forest",
        (21, 0) => "Leave Guardia Castle",
        (21, 2) => "Prison tower stairs",
        (29, 0) => "Upper prison bridge",
        (29, 1) => "Prison stairs",
        (30, 0) => "Leave the execution chamber",
        (75, 0 or 1) => "Return inside the prison tower",
        (489, >= 0 and <= 2) => "Stairs up",
        (489, >= 3 and <= 5) => "Stairs down",
        _ => "Exit",
    };
}
