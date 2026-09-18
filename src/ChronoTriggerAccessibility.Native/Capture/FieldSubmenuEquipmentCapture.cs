using System.Globalization;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed partial class FieldSubmenuCapture
{
    // CharaEquipManager's candidate MenuListView is a separate input owner from
    // both the character selector and the equipped-slot list. See the 0.3.26 audit.
    private bool TryReadEquipmentCandidates(nuint image, nuint node,
        out nuint child, out nuint list, out nuint manager)
        => TryReadEquipmentList(image, node, 0x30C, out child, out list, out manager);

    private bool TryReadEquipmentList(nuint image, nuint node, uint offset,
        out nuint child, out nuint list, out nuint manager)
    {
        list = manager = 0;
        return TryReadEquipmentChild(image, node, out child) &&
            Pointer(child + offset, out list) && Pointer(list, out var vt) && vt == image + MenuListViewVtableRva &&
            HasAncestor(list, child) && Pointer(list + 0x280, out manager) &&
            Pointer(manager, out var managerVt) && managerVt == image + ManagerVtableRva && HasAncestor(manager, list);
    }

    private bool ReadEquippedSlot(nuint image, nuint node, uint slot, out string caption, out string name)
    {
        caption = name = string.Empty;
        if (slot > 3 || !TryReadEquipmentList(image, node, 0x308, out var child, out var list, out var manager) ||
            !TryFindFocusableControl(image, manager, slot, out var control) || !HasAncestor(control, list) ||
            rendered.Read(control) is not { Count: <= 1 } lines) return false;
        // 20A720 stores the equipped ID at +328 + slot*12 and renders its low
        // twelve-bit item index. Index zero is the empty name in each gear group.
        // A blank Label alone must not imply that equipped gear disappeared.
        if (lines.Count == 0 && (!UInt32(child + 0x328 + slot * 12, out var encoded) ||
            encoded >= 0x4000 || (encoded & 0xFFF) != 0)) return false;
        var label = text.Get(image, MenuCaptionBank, 0x3C + (int)slot);
        if (string.IsNullOrWhiteSpace(label)) return false;
        caption = label; name = lines.Count == 0 ? "None" : lines[0];
        return true;
    }

    private FieldSubmenuSnapshot? CaptureEquippedSlot(nuint image, nuint node, string caption, in PageState state)
    {
        if (!ReadEquippedSlot(image, node, state.FocusKey, out var slot, out var name)) return null;
        var body = $"Equipped {slot}: {name}.";
        if (TryReadEquipmentDetail(image, node, false, out var detail)) body += $" Current stats: {detail}";
        if (TryReadEquipmentDescription(image, node, out var description) && description.Length > 0)
            body += $" Item details: {description}.";
        if (!ReadEquippedSlot(image, node, state.FocusKey, out var slotAgain, out var nameAgain) ||
            slotAgain != slot || nameAgain != name) return null;
        return new(EquipmentKind, caption, $"equipment:equipped:{state.Manager:X}:{state.FocusKey}:{name}", body);
    }

    private FieldSubmenuSnapshot? CaptureEquipmentCandidate(nuint image, nuint node, string caption, in PageState state)
    {
        if (!TryReadEquipmentCandidates(image, node, out var child, out var list, out var manager) || manager != state.Manager ||
            !UInt32(child + 0x304, out var slot) || slot > 3 ||
            !ReadEquippedSlot(image, node, slot, out var slotName, out var equipped) ||
            !UInt32(child + 0x35C, out var cursor) || cursor != state.FocusKey ||
            !Pointer(child + 0x31C, out var begin) || !Pointer(child + 0x320, out var end) || end <= begin ||
            (end - begin) % ItemRowStride != 0 || end - begin > MaximumRows * ItemRowStride ||
            state.FocusKey >= (end - begin) / ItemRowStride ||
            !TryReadRow(image, begin, (int)state.FocusKey, out var name, out var quantity, out var encoded) || quantity > 99 ||
            rendered.Read(state.Control) is not { Count: 2 } lines || lines[0] != name ||
            !int.TryParse(lines[1], NumberStyles.None, CultureInfo.InvariantCulture, out var displayedQuantity) ||
            displayedQuantity != quantity) return null;
        var count = (end - begin) / ItemRowStride;
        var body = $"Preview: {name}. Currently equipped {slotName}: {equipped}. Quantity {quantity}, {state.FocusKey + 1} of {count}.";
        if (TryReadEquipmentDescription(image, node, out var description) && description.Length > 0)
            body += $" Item details: {description}.";
        if (TryReadEquipmentDetail(image, node, true, out var detail)) body += $" If equipped: {detail}";
        if (!TryReadEquipmentCandidates(image, node, out var childAgain, out var listAgain, out var managerAgain) ||
            childAgain != child || listAgain != list || managerAgain != manager ||
            !UInt32(child + 0x304, out var slotAgain) || slotAgain != slot ||
            !ReadEquippedSlot(image, node, slot, out _, out var equippedAgain) || equippedAgain != equipped ||
            !UInt32(child + 0x35C, out var cursorAgain) || cursorAgain != cursor ||
            !Pointer(child + 0x31C, out var beginAgain) || beginAgain != begin ||
            !Pointer(child + 0x320, out var endAgain) || endAgain != end ||
            !TryReadRow(image, begin, (int)state.FocusKey, out var nameAgain, out var quantityAgain, out var encodedAgain) ||
            nameAgain != name || quantityAgain != quantity || encodedAgain != encoded) return null;
        return new(EquipmentKind, caption, $"equipment:item:{manager:X}:{slot}:{state.FocusKey}:{encoded}:{quantity}", body);
    }

    private bool TryReadEquipmentDescription(nuint image, nuint node, out string description)
    {
        description = string.Empty;
        return TryReadEquipmentChild(image, node, out var child) &&
            TryReadStatusBarDescription(image, child, 0x2F0, out description) &&
            TryReadEquipmentChild(image, node, out var again) && child == again;
    }

    private bool TryReadStatusBarDescription(nuint image, nuint owner, uint offset, out string description)
    {
        description = string.Empty;
        // 22F160 stores complete UTF-16 lines at +2D4/+2D8. 22F7B0 reveals them
        // one character at a time through the Label strings; those prefixes must
        // not become separate announcements. Read the owned source twice instead.
        if (!Pointer(owner + offset, out var bar) ||
            !Pointer(bar, out var vt) || vt != image + TopMenuCaptureScope.StatusBarVtableRva || !HasAncestor(bar, owner) ||
            !ReadStatusBarLines(bar, out var begin, out var end, out var lines) ||
            !Pointer(owner + offset, out var barAgain) || barAgain != bar ||
            !HasAncestor(bar, owner) ||
            !ReadStatusBarLines(bar, out var beginAgain, out var endAgain, out var again) ||
            beginAgain != begin || endAgain != end || !lines.SequenceEqual(again)) return false;
        description = JoinRenderedLines(lines).Trim().TrimEnd('.');
        return true;
    }

    private bool ReadStatusBarLines(nuint bar, out uint begin, out uint end, out List<string> lines)
    {
        begin = end = 0; lines = [];
        if (!UInt32(bar + 0x2D4, out begin) || !UInt32(bar + 0x2D8, out end) || end < begin ||
            (end - begin) % MsvcWideStringReader.LayoutSize != 0 ||
            (end - begin) / MsvcWideStringReader.LayoutSize > MaximumPanelLines) return false;
        var reader = new MsvcWideStringReader(memory);
        for (nuint at = begin; at < end; at += MsvcWideStringReader.LayoutSize)
        {
            if (!reader.TryRead(at, out var line, out _)) return false;
            if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.Trim());
        }
        return true;
    }

    // 2090F0 builds ten ParameterLabels, ordered by 20A800/20A900. Seven captions
    // are text; Attack and Defense use icons. +278 is the equipped baseline;
    // 2326E0 compares against it and renders the candidate value through +280.
    private bool TryReadEquipmentDetail(nuint image, nuint node, bool preview, out string detail)
    {
        detail = string.Empty;
        if (!TryReadEquipmentChild(image, node, out var child) ||
            !Pointer(child + 0x310, out var begin) || !Pointer(child + 0x314, out var end) || end != begin + 40)
            return false;
        var parts = new List<string>();
        var values = new (nuint Address, int Original, string Display)[10];
        // Read Attack and Defense first, matching their position above the named stats.
        foreach (var i in new[] { 7, 8, 0, 1, 2, 3, 4, 5, 6, 9 })
        {
            if (!Pointer(begin + (nuint)(i * 4), out var stat) ||
                !ReadEquipmentStat(image, child, stat, out var original, out var display, out var current)) return false;
            if (!preview && current != original) return false;
            values[i] = (stat, original, display);
            var name = i == 9 ? "Maximum HP" : text.Get(image, MenuCaptionBank, i < 7 ? 0x33 + i : 0x31 + i - 7);
            if (string.IsNullOrWhiteSpace(name)) return false;
            var change = current > original ? $", increased from {original}" :
                current < original ? $", decreased from {original}" : string.Empty;
            parts.Add($"{name} {display}{change}");
        }
        if (!TryReadEquipmentChild(image, node, out var childAgain) || childAgain != child ||
            !Pointer(child + 0x310, out var beginAgain) || beginAgain != begin ||
            !Pointer(child + 0x314, out var endAgain) || endAgain != end) return false;
        for (var i = 0; i < values.Length; i++)
        {
            var expected = values[i];
            if (!Pointer(begin + (nuint)(i * 4), out var stat) || stat != expected.Address ||
                !ReadEquipmentStat(image, child, stat, out var original, out var display, out _) ||
                original != expected.Original || display != expected.Display) return false;
        }
        detail = string.Join(". ", parts) + ".";
        return true;
    }

    private bool ReadEquipmentStat(nuint image, nuint child, nuint stat,
        out int original, out string display, out int current)
    {
        original = current = 0; display = string.Empty;
        if (!Pointer(stat, out var vt) || vt != image + 0x3AC408 || !HasAncestor(stat, child) ||
            !Int32(stat + 0x278, out original) || original < 0 ||
            rendered.Read(stat) is not { Count: 1 } lines) return false;
        // 232790 renders the two stars at 3A3620 when value >= the positive cap.
        // Announce that displayed maximum, without exposing an above-cap value.
        if (lines[0] == "\u2605\u2605" && Int32(stat + 0x27C, out var maximum) && maximum > 0)
        {
            current = maximum; original = Math.Min(original, maximum);
            display = $"maximum {maximum}";
            return true;
        }
        if (!int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out current)) return false;
        display = lines[0];
        return true;
    }
}
