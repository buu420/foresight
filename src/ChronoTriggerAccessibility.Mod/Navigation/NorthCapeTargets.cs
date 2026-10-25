using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

internal static class NorthCapeTargets
{
    public const string EncounterInstruction =
        "A sparkling light at the cape's tip. Press Confirm to begin the encounter.";

    public static string? EncounterLabel(int scene, FieldActorSnapshot actor, FieldStoryState? story,
        Func<int, string?>? characterName)
    {
        // Atel0406 slot16 calls controller9/fn1 only while G138 is zero.
        // Afterward its same sprite can award an item. The light is not the
        // hidden Magus actor7, nor the separately gated doppel doll in slot18.
        if (scene != 428 || story?.Global(0x138) != 0 ||
            actor is not { Index: 16, ClassTag: 4, VisualIndex: 113, IsUsable: true, IsDrawn: true,
                ScriptCallsEnabled: true, IsPartyMember: false }) return null;
        var name = characterName?.Invoke(6);
        return (string.IsNullOrWhiteSpace(name) ? "Magus" : name.Trim()) + " encounter";
    }
}
