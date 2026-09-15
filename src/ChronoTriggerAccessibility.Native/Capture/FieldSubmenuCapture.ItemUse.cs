namespace ChronoTriggerAccessibility.Native.Capture;

public sealed partial class FieldSubmenuCapture
{
    private sealed record ItemUseFrame(uint Key, int Members, bool All, nuint[] Cards, string Signature);

    private FieldSubmenuSnapshot? CaptureItemUseTarget(
        nuint image, nuint node, string caption, in PageState state)
    {
        if (!TryReadItemUseFrame(image, node, state, out var frame)) return null;
        var parts = new List<string>();
        foreach (var card in frame.Cards)
        {
            var lines = rendered.Read(card);
            if (lines is null || lines.Count == 0 || lines.Count > MaximumPanelLines) return null;
            parts.Add(JoinRenderedLines(lines));
        }
        if (!TryReadItemUseFrame(image, node, state, out var again) || frame.Signature != again.Signature)
            return null;
        var target = frame.All ? "All party members" : $"Target, {frame.Key + 1} of {frame.Members}";
        return new(InventoryKind, caption, $"inventory:target:{frame.Key}:{frame.All}",
            $"{target}. {string.Join(". ", parts)}.");
    }

    private bool TryReadItemUseFrame(nuint image, nuint node, in PageState state, out ItemUseFrame frame)
    {
        frame = null!;
        // 1C7710 pushes +314 onto the existing manager stack. Its transparent buttons are
        // siblings of the text sheet, not parents of the selected character's labels.
        // 23BC40 always creates three card slots; +318/+31C counts populated portrait icons.
        // See docs/item-use-0322-native-audit.md for the native construction and teardown.
        if (!Pointer(node + 0x314, out var manager) || manager != state.Manager ||
            !UInt32(node + 0x324, out var key) || key != state.FocusKey ||
            !Pointer(node + 0x308, out var panel) || !HasAncestor(panel, node) ||
            !Pointer(node + 0x310, out var sheet) || !HasAncestor(sheet, panel) ||
            !HasAncestor(state.Control, panel) ||
            !Pointer(manager + FocusableMapOffset, out var map) ||
            !UInt32(map + 8, out var controls) || controls is < 1 or > 3 || key >= controls)
            return false;

        var version = new List<ulong> { (ulong)manager, key, (ulong)panel, (ulong)sheet, (ulong)map, controls };
        if (!TryReadBoundedVector(sheet + ChildrenBeginOffset, 4, 3, version, out var cards, out var slots) || slots != 3 ||
            !TryReadBoundedVector(node + 0x318, 4, 3, version, out var icons, out var members) || members == 0 ||
            (controls != members && controls != 1)) return false;

        // 23CD80 maps the sequential control vector to keys 0..N-1. An all-party item
        // creates one tall control (23C270); individual targets use one each (23C050).
        for (uint i = 0; i < controls; i++)
        {
            if (!TryFindFocusableControl(image, manager, i, out var control) ||
                !Pointer(control, out var vtable) || vtable != image + CustomButtonVtableRva ||
                !HasAncestor(control, panel) || (i == key && control != state.Control)) return false;
            version.Add((ulong)control);
        }

        var all = controls == 1 && members > 1;
        var selected = new List<nuint>();
        for (var i = 0; i < members; i++)
        {
            if (!Pointer(icons + (nuint)(i * 4), out var icon) ||
                !Pointer(icon + NodeParentOffset, out var iconParent) || iconParent != panel ||
                !Pointer(cards + (nuint)(i * 4), out var card) ||
                !Pointer(card + NodeParentOffset, out var cardParent) || cardParent != sheet)
                return false;
            version.Add((ulong)icon); version.Add((ulong)card);
            if (all || i == key)
            {
                if (!HasAncestor(card, sheet)) return false;
                selected.Add(card);
            }
        }
        frame = new(key, members, all, selected.ToArray(), string.Join(":", version));
        return true;
    }
}
