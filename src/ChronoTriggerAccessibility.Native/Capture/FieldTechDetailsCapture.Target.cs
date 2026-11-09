using System.Buffers.Binary;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed partial class FieldTechDetailsCapture
{
    private const int MaximumTargetWindowChildren = 64;
    private const int MaximumTargetMembers = 3;
    private sealed record TargetState(uint Key, int Members, bool All, uint[] Cards, string Signature);

    /// <summary>The "use on whom" window built by 1CEB40 after a field Tech row is confirmed.
    /// 1CDFF0 has already released row manager +308. The window root is +34C; 1CF320 fills
    /// sheet +350 with one 23CB60 character card per party member, in roster order. The
    /// target Manager (590340) and its buttons (23C670 per member, or one 23C8B0 for an
    /// all-party Tech) are direct children of the window; 23CD80 keys them 0..N-1.
    /// 1CF240 handles its input and 1CF2E0 clears +34C/+350 on close.</summary>
    private FieldSubmenuSnapshot? CaptureTarget(nuint image, nuint node)
    {
        var state = ReadTargetState(image, node);
        if (state is null) return null;
        var parts = new List<string>();
        foreach (var card in state.Cards)
        {
            var lines = rendered.Read(card);
            if (lines is null || lines.Count is 0 or > 32) return null;
            parts.Add(RenderedCharacterCardSpeech.Format(lines).TrimEnd('.'));
        }
        var title = messages.Get(image, 0x23, 0x22);
        if (string.IsNullOrWhiteSpace(title) || ReadTargetState(image, node) is not { } again ||
            again.Signature != state.Signature) return null;
        var target = state.All ? "All party members" : $"Target, {state.Key + 1} of {state.Members}";
        return new("Tech", title, $"tech:target:{state.Key}:{state.All}",
            $"{target}. {string.Join(". ", parts)}.");
    }

    /// <summary>True when <paramref name="manager"/> is the target window's own Manager.</summary>
    public bool OwnsTargetManager(nuint image, nuint node, nuint manager) =>
        image != 0 && Word(node + 0x34C, out var window) && window != 0 &&
        Word(manager, out var mt) && mt == image + FieldSubmenuCapture.ManagerVtableRva &&
        Word(manager + 0x16C, out var parent) && parent == window;

    private bool TargetWindowOpen(nuint node) => Word(node + 0x34C, out var window) && window != 0;

    private TargetState? ReadTargetState(nuint image, nuint node)
    {
        if (!Word(node, out var vt) || vt != image + FieldSubmenuCapture.ClassicTechNodeVtableRva ||
            !Visible(node) || !Word(node + 0x308, out var rowManager) || rowManager != 0 ||
            !Word(node + 0x34C, out var window) || !Under(node, window) ||
            !Word(node + 0x350, out var sheet) || !Under(node, sheet) ||
            !Word(sheet + 0x16C, out var sheetParent) || sheetParent != window ||
            !Children(window, MaximumTargetWindowChildren, out var windowChildren) ||
            !Children(sheet, MaximumTargetMembers, out var cards) || cards.Length == 0) return null;

        uint manager = 0;
        foreach (var child in windowChildren)
        {
            if (!Word(child, out var cv)) return null;
            if (cv != image + FieldSubmenuCapture.ManagerVtableRva) continue;
            if (manager != 0) return null;
            manager = child;
        }
        if (manager == 0 || !Byte(manager + 0x290, out var disabled) || disabled != 0 ||
            !Word(manager + 0x2C4, out var key) || !Word(manager + 0x294, out var map) ||
            !Word(map + 8, out var controls) || controls is < 1 or > MaximumTargetMembers ||
            key >= controls || (controls != cards.Length && controls != 1)) return null;

        var version = new List<string> { $"{window:X}", $"{sheet:X}", $"{manager:X}", $"{key}", $"{controls}" };
        for (uint i = 0; i < controls; i++)
        {
            if (!FindControl(image, manager, i, out var control) || !Under(window, control) ||
                !Word(control + 0x16C, out var controlParent) || controlParent != window) return null;
            version.Add($"{control:X}");
        }
        foreach (var card in cards)
        {
            if (!Word(card + 0x16C, out var cardParent) || cardParent != sheet) return null;
            version.Add($"{card:X}");
        }

        var all = controls == 1 && cards.Length > 1;
        if (!all && !Under(sheet, cards[key])) return null;
        return new(key, cards.Length, all, all ? cards : [cards[key]], string.Join(":", version));
    }

    private bool Children(nuint parent, int maximum, out uint[] children)
    {
        children = [];
        if (!Word(parent + 0x160, out var begin) || !Word(parent + 0x164, out var end) ||
            end < begin || (end - begin) % 4 != 0 || (end - begin) / 4 > maximum) return false;
        var count = (int)((end - begin) / 4);
        if (count == 0) return true;
        var data = new byte[count * 4];
        if ((ulong)begin + (ulong)data.Length > 0x100000000UL || !memory.TryRead(begin, data)) return false;
        children = new uint[count];
        for (var i = 0; i < count; i++)
            children[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i * 4, 4));
        return true;
    }
}
