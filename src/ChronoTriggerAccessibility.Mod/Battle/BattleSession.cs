using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Battle;

/// <summary>Connects audited display captures to one battle lifetime.</summary>
public sealed class BattleSession
{
    private readonly BattleRuntime runtime;
    private readonly BattleCapture capture;
    private readonly BattleFeedbackCapture feedback;
    private readonly BattlePresentationCapture presentation;
    private readonly Action<string> diagnostic;
    private nuint image, owner;
    private int missingFrames;
    private bool reportedMissing;
    private string? unresolvedFocus;

    public BattleSession(IReadableMemory memory, BattleRuntime runtime, Action<string> diagnostic)
    {
        this.runtime = runtime; this.diagnostic = diagnostic;
        capture = new(memory); feedback = new(memory); presentation = new(memory);
    }
    public void BindImageBase(nuint value) => image = value;

    public void Tick(nuint menu)
    {
        if (image == 0 || menu == 0) return;
        if (owner != menu)
        {
            owner = menu; missingFrames = 0; reportedMissing = false; unresolvedFocus = null;
            diagnostic($"Battle reader entered menu 0x{menu:X}.");
        }
        var snapshot = capture.Capture(image, menu);
        BattleFrame? frame = null;
        if (snapshot is not null)
        {
            // A stable identity for each already-targeted enemy replaces the spatial
            // distinction a sighted player gets from separate sprites. It is keyed to
            // its native slot, never to an inferred count or roster of living enemies.
            var names = snapshot.BattlerNames.ToDictionary(p => p.Key, p =>
                p.Key >= 3 ? $"{p.Value} {(char)('A' + p.Key - 3)}" : p.Value);
            var party = snapshot.Party.Select(p => new BattlePartyMember(p.Slot, p.Name, p.Hp,
                p.MaximumHp, p.Mp, p.MaximumMp, presentation.ReadStatus(image, menu, p.Slot))).ToArray();
            var identity = snapshot.FocusIdentity;
            var text = snapshot.FocusText;
            var targets = presentation.ReadTargets(image, menu);
            if (targets?.IsTargeting == true)
            {
                identity = null; text = null;
                if (targets.Slots.Count > 0)
                {
                    identity = "targets:" + string.Join(",", targets.Slots);
                    text = (targets.Slots.Count == 1 ? "Target. " : "Targets. ") +
                        string.Join(", ", targets.Slots.Select(s => names.GetValueOrDefault(s) ?? BattleIdentity.Unnamed(s))) + ".";
                }
            }
            // The legacy single-target fallback must not escape a failed renderer read.
            else if (targets is null && identity?.StartsWith("target:", StringComparison.Ordinal) == true)
            { identity = null; text = null; }
            frame = new(party, identity, text, names);
            missingFrames = 0;
            if (reportedMissing) { diagnostic("Battle capture restored."); reportedMissing = false; }
            if (identity is not null && string.IsNullOrWhiteSpace(text))
            {
                if (unresolvedFocus != identity) diagnostic($"Battle focus has no readable label: {identity}.");
                unresolvedFocus = identity;
                frame = frame with { FocusText = "Battle selection label is unavailable." };
            }
            else if (!string.IsNullOrWhiteSpace(text)) unresolvedFocus = null;
        }
        runtime.Observe(menu, frame);
        runtime.Message(menu, feedback.ReadMessage(menu));
        if (snapshot is null && ++missingFrames >= 60 && !reportedMissing)
        {
            reportedMissing = true;
            diagnostic($"Battle capture unavailable for 60 updates at menu 0x{menu:X}.");
            runtime.Message(menu, "Battle information is unavailable. The failure has been logged.");
        }
    }

    public void Message(nuint menu) { EnsureOwner(menu); runtime.Message(menu, feedback.ReadMessage(menu)); }
    public void Number(nuint menu, int slot)
    {
        EnsureOwner(menu);
        var kind = feedback.ReadEffectKind(image, slot);
        if (kind == 0) diagnostic($"Unclassified battle popup in slot {slot + 1}.");
        runtime.MarkPopup(menu, slot, kind);
    }
    public void Miss(nuint menu, int slot) { EnsureOwner(menu); runtime.MarkPopup(menu, slot, 5); }
    public void Render(nuint menu) { EnsureOwner(menu); runtime.RenderPopups(menu, feedback.ReadPopups(menu)); }
    public void Close(nuint menu)
    {
        runtime.End(menu);
        if (owner != menu) return;
        owner = 0; missingFrames = 0; reportedMissing = false; unresolvedFocus = null;
        diagnostic("Battle reader released its menu.");
    }
    public void Disable() { runtime.Disable(); owner = 0; image = 0; }
    private void EnsureOwner(nuint menu) { if (owner != menu) Tick(menu); }
}
