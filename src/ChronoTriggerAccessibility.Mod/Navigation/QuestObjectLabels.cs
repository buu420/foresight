using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Installed Atel0133/0097 shell, Atel0021 Moon Stone and
/// Atel0354 Sun Stone identities. Empty shrine controls keep a place label;
/// the Sun Temple's pre-battle glint and ordinary capsule glints stay unnamed.</summary>
internal static class QuestObjectLabels
{
    public static (string Label, string Description)? Actor(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) switch
        {
            (197,10,4,149) or (440,9,4,149) => ("Rainbow Shell", "A large shell with a multicolored surface."),
            (251,17,4,110) => ("Moon Stone", "A small, dark, round stone."),
            (255,8,4,110) => ("Sun Stone", "A round stone glows brightly."),
            (327,8,4,112) => ("Sunlit spot", "Sunlight illuminates this spot in the shrine."),
            _ => null,
        };

    public static string? Marker(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag) is (64,9,7) or (255,9,7) ? "Sunlit spot" : null;
}
