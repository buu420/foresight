namespace ChronoTriggerAccessibility.Native.Capture;

public sealed partial class FieldSubmenuCapture
{
    // The constructors/builders at 1BE730, 1BEAC0, 1BF330 and 1BFBB0 establish these
    // relationships. Party buttons contain icons; character text belongs to separate cards.
    private const uint FormationParkedKey = 999;
    private const uint PlainStateVtableRva = 0x3ABC2C;
    private const uint CustomButtonVtableRva = 0x3A4364;
    private const uint ChildrenBeginOffset = 0x160;
    private const int FormationInstructionMessageId = 0x95;
    private const int FormationCombosMessageId = 0x83;

    private sealed record FormationMember(uint Key, int CharacterId, bool Locked, nuint Card);
    private sealed record FormationFrame(
        uint FocusKey, int HeldKey, nuint ComboPanel,
        FormationMember[] Current, FormationMember[] Reserve, string Signature);

    private FieldSubmenuSnapshot? CaptureFormation(nuint imageBase, nuint node)
    {
        if (!TryReadFormationFrame(imageBase, node, out var frame)) return null;
        var caption = text.Get(imageBase, MenuCaptionBank, FormationCaptionMessageId);
        var comboCaption = text.Get(imageBase, MenuCaptionBank, FormationCombosMessageId);
        if (string.IsNullOrWhiteSpace(caption) || string.IsNullOrWhiteSpace(comboCaption)) return null;

        var parts = new List<string>();
        if (frame.FocusKey == FormationParkedKey)
        {
            // 999 is a plain State, installed when the intended current member is locked.
            // There is no focused control. Describe the actual roster without inventing focus.
            var instruction = text.Get(imageBase, MenuCaptionBank, FormationInstructionMessageId);
            if (string.IsNullOrWhiteSpace(instruction)) return null;
            parts.Add(instruction);
            foreach (var (heading, members) in new[] { ("Current party", frame.Current), ("Reserve", frame.Reserve) })
            {
                parts.Add(heading);
                if (members.Length == 0) parts.Add("Empty");
                foreach (var member in members)
                {
                    if (!TryReadFormationCard(member, out var card, out _)) return null;
                    parts.Add(card);
                    if (member.Locked) parts.Add("Locked");
                }
            }
        }
        else
        {
            var members = frame.FocusKey < 10 ? frame.Current : frame.Reserve;
            var index = (int)(frame.FocusKey < 10 ? frame.FocusKey : frame.FocusKey - 10);
            var member = members[index]; // Frame validation established this key's range and owner.
            if (!TryReadFormationCard(member, out var card, out _)) return null;
            parts.Add($"{(frame.FocusKey < 10 ? "Current party" : "Reserve")}, {index + 1} of {members.Length}");
            parts.Add(card);
            if (member.Locked) parts.Add("Locked");
            if (frame.HeldKey == (int)frame.FocusKey) parts.Add("Picked up");
            else if (frame.HeldKey >= 0)
            {
                var held = frame.Current.Concat(frame.Reserve).Single(m => m.Key == (uint)frame.HeldKey);
                if (!TryReadFormationCard(held, out _, out var name)) return null;
                parts.Add($"Moving {name}");
            }
        }

        // 1BF480 clears this panel and 1BF750 adds ability-name Labels for the previewed party.
        // Group backgrounds and portraits can remain with no abilities. A successful rendered
        // traversal with no Labels therefore means none; an unreadable Label still fails capture.
        var combos = rendered.Read(frame.ComboPanel);
        if (combos is null || combos.Count > MaximumRenderedLines) return null;
        parts.Add(comboCaption);
        parts.Add(combos.Count == 0 ? "None" : JoinRenderedLines(combos));

        if (!TryReadFormationFrame(imageBase, node, out var again) || again.Signature != frame.Signature)
            return null;
        var body = string.Join(". ", parts.Select(p => p.TrimEnd().TrimEnd('.'))) + ".";
        return new(FormationKind, caption, $"formation:{frame.Signature}", body);
    }

    private bool TryReadFormationCard(FormationMember member, out string card, out string name)
    {
        card = name = string.Empty;
        var lines = rendered.Read(member.Card);
        if (lines is null || lines.Count == 0 || lines.Count > MaximumRenderedLines) return false;
        // The card builder 23B210 renders the character name first, followed by level, HP and MP.
        name = lines[0];
        card = JoinRenderedLines(lines);
        return !string.IsNullOrWhiteSpace(card);
    }

    private bool TryReadFormationFrame(nuint imageBase, nuint node, out FormationFrame frame)
    {
        frame = null!;
        if (!Pointer(node, out var vtable) || vtable != imageBase + ClassicFormationNodeVtableRva ||
            !HasAncestor(node, node) || !TryReadTopManager(imageBase, node, out var manager) ||
            !Byte(manager + 0x290, out var disabled) || disabled != 0 ||
            !UInt32(manager + ManagerFocusKeyOffset, out var focus) ||
            !Int32(node + 0x2FC, out var currentIndex) || !Int32(node + 0x300, out var reserveIndex) ||
            !Int32(node + 0x334, out var held) || held < -1 ||
            !Int32(node + 0x338, out var preview) || preview < -1 ||
            !Pointer(node + 0x304, out var panel) || !HasAncestor(panel, node) ||
            !Pointer(node + 0x308, out var currentPanel) || !HasAncestor(currentPanel, panel) ||
            !Pointer(node + 0x318, out var comboPanel) || !HasAncestor(comboPanel, node)) return false;

        var version = new List<ulong>
        {
            (ulong)manager, focus, (uint)currentIndex, (uint)reserveIndex, (uint)held, (uint)preview,
            (ulong)panel, (ulong)currentPanel, (ulong)comboPanel,
        };
        if (!TryReadFormationVector(currentPanel + ChildrenBeginOffset, 4, 3, version, out var activeCards, out var activeSlots) ||
            activeSlots != 3 ||
            !TryReadFormationVector(node + 0x30C, 4, 6, version, out var reserveCards, out var reserveSlots) ||
            !TryReadFormationVector(comboPanel + ChildrenBeginOffset, 4, MaximumRows, version, out _, out _) ||
            !TryReadFormationMembers(imageBase, node + 0x2CC, node + 0x2E4, manager, currentPanel, panel,
                activeCards, activeSlots, true, version, out var current) ||
            current.Length == 0 || currentIndex < 0 || currentIndex >= current.Length ||
            !TryReadFormationMembers(imageBase, node + 0x2D8, node + 0x2F0, manager, panel, panel,
                reserveCards, reserveSlots, false, version, out var reserve) ||
            reserve.Length != reserveSlots || reserveIndex < 0 ||
            (reserve.Length == 0 ? reserveIndex != 0 : reserveIndex >= reserve.Length)) return false;

        var members = current.Concat(reserve).ToArray();
        if (members.Select(m => m.CharacterId).Distinct().Count() != members.Length ||
            (held >= 0 && !members.Any(m => m.Key == (uint)held)) ||
            (preview >= 0 && !members.Any(m => m.Key == (uint)preview))) return false;

        if (focus == FormationParkedKey)
        {
            if (!current[currentIndex].Locked || held != -1 ||
                !TryFindManagerState(manager, focus, out var parked) ||
                !Pointer(parked, out var parkedVtable) || parkedVtable != imageBase + PlainStateVtableRva)
                return false;
            version.Add((ulong)parked);
        }
        else if (!members.Any(m => m.Key == focus)) return false;

        frame = new(focus, held, comboPanel, current, reserve, string.Join(":", version));
        return true;
    }

    private bool TryReadFormationMembers(
        nuint imageBase, nuint recordsAt, nuint buttonsAt, nuint manager, nuint cardOwner, nuint buttonOwner,
        nuint cards, int cardCount, bool current, List<ulong> version, out FormationMember[] members)
    {
        members = [];
        var maximum = current ? 3 : 6;
        if (!TryReadFormationVector(recordsAt, 8, maximum, version, out var records, out var count) ||
            !TryReadFormationVector(buttonsAt, 4, maximum, version, out var buttons, out var buttonCount) ||
            count > cardCount || count != buttonCount) return false;

        members = new FormationMember[count];
        for (var index = 0; index < count; index++)
        {
            var record = records + (nuint)(index * 8);
            var key = (uint)((current ? 0 : 10) + index);
            if (!Int32(record, out var id) || id is < 0 or >= 0x80 ||
                !Byte(record + 4, out var side) || side != (current ? 1 : 0) ||
                !Byte(record + 5, out var locked) || locked > 1 ||
                !Pointer(cards + (nuint)(index * 4), out var card) || !HasAncestor(card, cardOwner) ||
                !Pointer(buttons + (nuint)(index * 4), out var button) || !HasAncestor(button, buttonOwner) ||
                !Pointer(button, out var buttonVtable) || buttonVtable != imageBase + CustomButtonVtableRva ||
                !TryFindManagerState(manager, key, out var state) ||
                !Pointer(state, out var stateVtable) || stateVtable != imageBase + FocusableStateVtableRva ||
                !Pointer(state + FocusableStateControlOffset, out var control) || control != button ||
                !Byte(state + 0x11, out var stateLocked) || stateLocked != locked) return false;

            version.AddRange([(uint)id, side, locked, (ulong)card, (ulong)button, (ulong)state]);
            members[index] = new(key, id, locked != 0, card);
        }
        return true;
    }

    private bool TryReadFormationVector(
        nuint address, int stride, int maximum, List<ulong> version, out nuint begin, out int count)
    {
        begin = 0;
        count = 0;
        if (!UInt32(address, out var first) || !UInt32(address + 4, out var end) || end < first ||
            (end - first) % stride != 0 || (end - first) / stride > maximum || (first == 0 && end != 0))
            return false;
        begin = first;
        count = (int)((end - first) / stride);
        version.Add(first);
        version.Add(end);
        return true;
    }
}
