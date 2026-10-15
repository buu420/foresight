using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public static class NuNpcLabels
{
    /// <summary>Only audited nonbattle NPC placements, with their live drawing/call gates
    /// and a currently available native conversation or menu. Enemy visual zero is Nu;
    /// load_enemy's high-bit mode changes class 5 to 6 without changing that visual id.
    /// Ordinary NPC class 4 uses a different sprite table.</summary>
    public static string? ActorLabel(int scene, FieldActorSnapshot actor, FieldStoryState? state = null)
    {
        if (!actor.IsUsable || !actor.IsDrawn || actor.IsPartyMember || !actor.ScriptCallsEnabled ||
            actor.ClassTag is not (5 or 6) || actor.VisualIndex is not (0 or 130) ||
            !NpcPlacement(scene, actor.Index) || GameNavigationCatalog.ActorInfo(scene, actor) is not { } metadata)
            return null;
        var actions = metadata.Actions.Where(a => a.Available(state)).ToArray();
        if (!actions.Any(a => a.Kind is "Talk" or "Menu") || actions.Any(a => a.Kind == "Encounter")) return null;
        return (scene, actor.Index) is (241, 9 or 10) or (242, 16) or (243, 14)
            ? "Nu, Strange Construct" : "Nu";
    }

    // Installed script audit: the other enemy-0/130 placements are encounters,
    // concealed/decorative actors, or have no verified NPC interaction.
    private static bool NpcPlacement(int scene, int actor) => (scene, actor) is
        (42, 21) or (140, 20) or (241, 9 or 10) or (242, 16) or (243, 14) or
        (292, 13) or (315, 10 or 11) or (360, 17 or 23) or (379, 11) or
        (401, 15 or 32 or 33 or 34) or (415, 24) or (426, 8) or (667, 10);
}
