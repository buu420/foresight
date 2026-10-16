using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Atel0186 actor8's automatic reveal, not a hidden NPC interaction.
/// Raw0727 requires local08=0; raw072C skips the loop after globalFF bit80.
/// The live controller then tests leader tile X&lt;8 at0739 and calls actor4
/// fn3 at0740. That function shows Frog only at0628. Actor0 updates the
/// leader coordinates with opcode22; the controller has no map position.
/// RVA164D10 admits the priority2 call only while actor4's call/processing
/// gates are open and its current priority exceeds2.</summary>
public static class FrogHideawayTargets
{
    public const string Id = "hideaway:reveal";

    public static bool Available(FieldNavigationSnapshot field, FieldStoryState? state) =>
        field.SceneIdCoherent && field.SceneId == 141 && field.ControlFlag != 0 && field.InputMode == 0 &&
        field.LeadPlayer is { IsUsable: true, IsDrawn: true } &&
        state?.Local(8) == 0 && state.Flag(0xFF, 0x80) == false &&
        field.Actors.Any(a => a.Index == 8 && a.ClassTag == 7 && a.IsUsable &&
            !a.IsPartyMember && a.ScriptProcessingEnabled) &&
        field.Actors.Any(a => a.Index == 4 && a.ClassTag == 3 && a.VisualIndex == 4 &&
            a.IsUsable && !a.IsPartyMember && a.DrawMode == 0 && (a.LoadedFlag & 1) != 0 &&
            a.ScriptCallsEnabled && a.ScriptProcessingEnabled && a.ScriptPriority is > 2);
}
