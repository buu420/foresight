using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Ocean Palace story controllers start cutscenes before their final
/// map jump. Their later destination is not a doorway the player can see.</summary>
internal static class OceanPalaceStoryTargets
{
    public static bool CinematicRegion(int scene, GameNavigationCatalog.Region region) =>
        region.Kind == "Warp" && (scene, region.Actor, region.Destination) is
            (415, 11, 467) or (423, 20, 415);

    public static GameNavigationCatalog.Region? NpcRegion(FieldNavigationSnapshot field,
        FieldActorSnapshot actor, FieldStoryState? story)
    {
        if (!field.SceneIdCoherent || field.SceneId != 423 || actor.Index != 22 ||
            actor.ClassTag is not (5 or 6) || actor.VisualIndex != 0xCA ||
            !actor.IsUsable || !actor.IsDrawn || !actor.ScriptCallsEnabled ||
            story is not { Point: >= 0xC9 and < 0xCB } ||
            !field.Actors.Any(a => a.Index == 20 && a.ClassTag == 4 && a.VisualIndex == 0x4A &&
                a.IsUsable && a.ScriptProcessingEnabled)) return null;
        // Atel0230 actor20's own loop checks 6<X<9 and Y<11, speaks as
        // the Queen and requests Lavos's fn3. ProgressCB uses those same bounds.
        return GameNavigationCatalog.ForScene(423)?.Regions.FirstOrDefault(r =>
            r.Actor == 20 && r.Kind == "Progress" && r.Value == 0xCB && r.Available(story));
    }

    public static FullStoryObjective ForScene(FullStoryObjective objective, int scene)
    {
        if (objective.Id != "full:ocean-palace-lavos") return objective;
        return scene == 423 ? objective with {
            Label = "Approach Lavos", Goals = [new(423, [22], [])],
            Instruction = "When you regain movement, walk toward Lavos.",
        } : objective with {
            Label = "Approach the Mammon Machine", Instruction = "Continue toward the Mammon Machine.",
        };
    }
}
