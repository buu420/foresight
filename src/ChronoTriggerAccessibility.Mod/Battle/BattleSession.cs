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
    // Enemy slots the game proved share a native name with another current enemy, with that name.
    private readonly Dictionary<int, string> lettered = [];
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
            owner = menu; missingFrames = 0; reportedMissing = false; unresolvedFocus = null; lettered.Clear();
            diagnostic($"Battle reader entered menu 0x{menu:X}.");
        }
        var snapshot = capture.Capture(image, menu);
        BattleFrame? frame = null;
        BattleTargetsSnapshot? targets = null;
        if (snapshot is not null)
        {
            targets = presentation.ReadTargets(image, menu);
            var names = Identify(snapshot.BattlerNames, targets);
            var party = snapshot.Party.Select(p => new BattlePartyMember(p.Slot, p.Name, p.Hp,
                p.MaximumHp, p.Mp, p.MaximumMp, presentation.ReadStatus(image, menu, p.Slot))).ToArray();
            var identity = snapshot.FocusIdentity;
            var text = snapshot.FocusText;
            if (targets?.IsTargeting == true)
            {
                identity = null; text = null;
                if (targets.Slots.Count > 0)
                {
                    string Label(int s) => names.GetValueOrDefault(s) ?? BattleIdentity.Unnamed(s);
                    // Enemy labels are part of the identity, so a letter proven after the first
                    // frame is announced; ally HP changes still only update Repeat.
                    identity = "targets:" + string.Join(",", targets.Slots.Select(s => s < 3 ? $"{s}" : $"{s}={Label(s)}"));
                    text = (targets.Slots.Count == 1 ? "Target. " : "Targets. ") +
                        string.Join(" ", targets.Slots.Select(s => party.FirstOrDefault(p => p.Slot == s) is { } member
                            ? BattleRuntime.Describe(member)
                            : Label(s) + "."));
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
        var message = feedback.ReadMessage(menu);
        runtime.Message(menu, message, !RepeatsTargetName(message, targets, snapshot?.BattlerNames));
        if (snapshot is null && ++missingFrames >= 60 && !reportedMissing)
        {
            reportedMissing = true;
            diagnostic($"Battle capture unavailable for 60 updates at menu 0x{menu:X}.");
            runtime.Message(menu, "Battle information is unavailable. The failure has been logged.");
        }
    }

    public void Message(nuint menu)
    {
        EnsureOwner(menu);
        var message = feedback.ReadMessage(menu);
        var targets = message is null ? null : presentation.ReadTargets(image, menu);
        var names = targets?.NameLabelSlot is null ? null : capture.Capture(image, menu)?.BattlerNames;
        runtime.Message(menu, message, !RepeatsTargetName(message, targets, names));
    }
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
        owner = 0; missingFrames = 0; reportedMissing = false; unresolvedFocus = null; lettered.Clear();
        diagnostic("Battle reader released its menu.");
    }
    public void Disable() { runtime.Disable(); owner = 0; image = 0; lettered.Clear(); }
    private void EnsureOwner(nuint menu) { if (owner != menu) Tick(menu); }

    /// <summary>
    /// Native names, plus the fixed slot letter (A-H for slots 3-10) on an enemy the game proved
    /// shares its name with another current enemy. That replaces the spatial distinction a sighted
    /// player gets from separate sprites. Only this selection's fresh candidate list and its
    /// committed recipients count as current; a stale name in an unused slot never does. A proven
    /// (slot, name) keeps its letter for the rest of the battle, so hits on the survivor match
    /// what targeting said, and loses it when that slot shows a different name.
    /// </summary>
    private Dictionary<int, string> Identify(IReadOnlyDictionary<int, string> native, BattleTargetsSnapshot? targets)
    {
        foreach (var (slot, proven) in lettered.ToArray())
            if (native.TryGetValue(slot, out var shown) && shown != proven) lettered.Remove(slot);
        if (targets?.IsTargeting == true)
        {
            var current = targets.Slots.Concat(targets.CandidateSlots ?? []).Where(s => s > 2 && native.ContainsKey(s));
            foreach (var same in current.Distinct().GroupBy(s => native[s], StringComparer.Ordinal).Where(g => g.Count() > 1))
                foreach (var enemy in same) lettered[enemy] = same.Key;
        }
        return native.ToDictionary(p => p.Key, p => TargetName(p.Key, p.Value));
    }

    private string TargetName(int slot, string name) =>
        lettered.TryGetValue(slot, out var proven) && proven == name ? $"{name} {(char)('A' + slot - 3)}" : name;

    /// <summary>
    /// 44B390 copies a single targeted enemy's monster.txt line into the battle message label when
    /// an Attack is aimed (46ED80, then 19940 type 0x0D). The target focus has already said that
    /// name, so only that exact text is a repeat. The revealed-HP form (46ECD0), any other message,
    /// and text shown for Tech or Item targets, groups or allies are still spoken.
    /// </summary>
    private bool RepeatsTargetName(string? message, BattleTargetsSnapshot? targets,
        IReadOnlyDictionary<int, string>? names) =>
        message is not null && targets is { IsTargeting: true, NameLabelSlot: { } slot } &&
        names is not null && names.TryGetValue(slot, out var name) &&
        string.Equals(message.Trim(), name.Trim(), StringComparison.Ordinal) &&
        runtime.HasPresentedFocus(owner, $"targets:{slot}={TargetName(slot, name)}");
}
