using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Contact and confirm battles, including contact signals received by
/// another actor's startup script. D8 and the signal predicates come from the
/// installed script; class or sprite appearance alone never identifies an enemy.</summary>
internal static class FieldEncounters
{
    internal static GameNavigationCatalog.Action[] Available(FieldNavigationSnapshot field,
        FieldActorSnapshot actor, FieldStoryState? story, GameNavigationCatalog.Actor? metadata)
    {
        if (!field.SceneIdCoherent || story is null || !actor.IsUsable || actor.IsPartyMember ||
            !actor.ScriptCallsEnabled || (actor.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) != 0 ||
            metadata is null || !metadata.Matches(actor) || !metadata.Marker && !actor.IsDrawn) return [];
        // Class7 markers intentionally have no sprite. Loaded enemies must
        // have appeared: a hidden initial slot can be cinematic staging
        // (Denadoro 146/12 is shown before moving into the fight).
        return metadata.Actions.Where(action => action.Kind == "Encounter" && action.Available(story) &&
            (action.Controller < 0 || field.Actors.Any(controller => controller.Index == action.Controller &&
                controller.IsUsable && controller.ScriptProcessingEnabled &&
                (controller.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0))).ToArray();
    }
}
