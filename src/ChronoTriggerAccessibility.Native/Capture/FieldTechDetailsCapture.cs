using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Classic Tech's separately managed row list and visible description/requirements.
/// 1CDE0C owns the row manager at +308; 1CE110/1CE350 render the selected row's sibling panels.</summary>
public sealed partial class FieldTechDetailsCapture(IReadableMemory memory)
{
    private readonly RenderedNodeTextCapture rendered = new(memory);
    private readonly LoadedGameTextCapture messages = new(memory);
    private sealed record State(uint Manager, uint Category, uint Row, uint Begin, uint End,
        uint Control, uint Description, uint Components, uint Costs, string Record);

    public FieldSubmenuSnapshot? Capture(nuint image, nuint node)
    {
        if (image == 0 || node == 0) return null;
        // 1CCE50 owns character selection separately from MenuNodeBase's stack.
        // 1CDD70 replaces it with the row manager. A failed row read cannot use
        // the previous character selection as a fallback.
        if (Word(node + 0x308, out var rowManager) && rowManager == 0)
            return CaptureCharacter(image, node);
        var state = ReadState(image, node);
        if (state is null) return null;
        var selected = rendered.Read(state.Control);
        var description = rendered.Read(state.Description);
        var components = rendered.Read(state.Components);
        var costs = state.Costs == 0 ? [] : rendered.Read(state.Costs);
        var title = messages.Get(image, 0x23, 0x22);
        if (title is null || selected is null || selected.Count == 0 || selected.Count > 12 ||
            description is null || components is null || costs is null || ReadState(image, node) != state) return null;
        var parts = new List<string> { string.Join(", ", selected) };
        if (description.Count > 0) parts.Add(string.Join(" ", description));
        if (components.Count > 0) parts.Add(string.Join(" ", components));
        if (costs.Count > 0) parts.Add(string.Join(" ", costs));
        return new("Tech", title, $"tech:{state.Category}:{state.Row}",
            string.Join(". ", parts.Select(p => p.TrimEnd('.'))));
    }

    private State? ReadState(nuint image, nuint node)
    {
        if (!Word(node, out var vt) || vt != image + FieldSubmenuCapture.ClassicTechNodeVtableRva ||
            !Visible(node) || !Word(node + 0x308, out var manager) ||
            !Word(manager, out var mt) || mt != image + 0x3A5D0C ||
            !Byte(manager + 0x290, out var disabled) || disabled != 0 ||
            !Word(node + 0x2FC, out var category) || category >= 3 ||
            !Word(node + 0x300, out var row) ||
            !Word(manager + 0x2C4, out var key) || key != row ||
            !Word(node + 0x318 + category * 12, out var begin) || begin == 0 ||
            !Word(node + 0x31C + category * 12, out var end) || end < begin ||
            (end - begin) % 12 != 0 || (end - begin) / 12 > 256 || row >= (end - begin) / 12 ||
            !FindControl(image, manager, key, out var control) || !Under(node, control) ||
            !Word(node + 0x33C, out var description) || !Under(node, description) ||
            !Word(node + 0x340, out var components) || !Under(node, components) ||
            !Word(node + 0x344, out var costs) || (costs != 0 && !Under(node, costs))) return null;
        Span<byte> record = stackalloc byte[12];
        var address = (ulong)begin + row * 12UL;
        if (address + 12 > 0x100000000UL || !memory.TryRead((nuint)address, record)) return null;
        return new(manager, category, row, begin, end, control, description, components, costs, Convert.ToHexString(record));
    }
    private bool FindControl(nuint image, uint manager, uint key, out uint control)
    {
        control = 0;
        if (!Word(manager + 0x294, out var map) || !Word(map + 4, out var sentinel) || sentinel == 0) return false;
        var entry = sentinel;
        for (var i = 0; i < 512; i++)
        {
            if (!Word(entry, out entry) || entry == 0 || entry == sentinel || !Word(entry + 8, out var candidate)) return false;
            if (candidate != key) continue;
            return Word(entry + 12, out var state) && Word(state, out var vt) && vt == image + 0x3AC3F4 &&
                Word(state + 0x14, out control) && Visible(control) && Word(control, out var cv) && cv != image + 0x3A60A0;
        }
        return false;
    }
    private bool Under(nuint root, uint node)
    {
        for (var i = 0; node != 0 && i < 64; i++)
        {
            if (!Visible(node)) return false;
            if (node == root) return true;
            if (!Word(node + 0x16C, out node)) return false;
        }
        return false;
    }
    private bool Visible(nuint p) => Byte(p + 0x1AD, out var visible) && visible != 0;
    private bool Word(nuint p, out uint value)
    {
        value = 0; Span<byte> data = stackalloc byte[4];
        if (p == 0 || (ulong)p + 4 > 0x100000000UL || !memory.TryRead(p, data)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(data); return true;
    }
    private bool Byte(nuint p, out byte value)
    {
        value = 0; Span<byte> data = stackalloc byte[1];
        if (p == 0 || (ulong)p + 1 > 0x100000000UL || !memory.TryRead(p, data)) return false;
        value = data[0]; return true;
    }
}
