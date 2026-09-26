using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Steam save/load file list. See docs/field-save-slots-native-audit.md.</summary>
public sealed class SaveSlotCapture(IReadableMemory memory)
{
    public const uint NodeVtableRva = 0x3A980C;

    /// <summary>The node's StatusBar: 0x218A20 creates it (0x22E500) under the node's layer and
    /// fills it once through 0x22F160 with 0x218E00's line for the mode: "select a file", "you
    /// can save your current progress", "you cannot save at this time", or "select a file to
    /// load".</summary>
    public const uint StatusBarOffset = 0x2DC;
    private const int MaximumStatusLines = 8;
    private readonly RenderedNodeTextCapture labels = new(memory);
    private readonly LoadedGameTextCapture messages = new(memory);
    private readonly MsvcWideStringReader wide = new(memory);

    public FieldSubmenuSnapshot? Capture(nuint image, nuint node)
    {
        if (image == 0 || node == 0 || !Owner(image, node) || !Word(node + 0x2CC, out var mode) || mode > 5 || mode is 2 or 3 ||
            !Word(node + 0x304, out var manager) || !Word(manager, out var mt) || mt != image + 0x3A5D0C ||
            !Word(manager + 0x2C4, out var key) ||
            !Vector(node + 0x2F8, 4, out var cards, out var count) || key >= count ||
            !Word(cards + key * 4, out var card) ||
            !Vector(node + 0x2D0, 0x940, out var records, out var recordCount) || key >= recordCount ||
            !Byte(records + key * 0x940, out var occupied) ||
            !Word(node + 0x2E8, out var preview) || !Under(node, card) ||
            (occupied != 0 && !Under(node, preview))) return null;
        var cardText = labels.Read(card);
        var details = occupied != 0 ? labels.Read(preview) : [];
        var title = mode == 1 ? messages.Get(image, 0x41, 0x0A) : messages.Get(image, 0x23, 0x26);
        if (!TryReadInstruction(image, node, out var bar, out var instruction)) return null;
        if (title is null || cardText is null || cardText.Count == 0 || details is null ||
            !TryReadInstruction(image, node, out var bar2, out var instruction2) || bar2 != bar ||
            instruction2 != instruction ||
            (occupied != 0 && details.Count == 0) || !Owner(image, node) ||
            !Word(node + 0x2CC, out var mode2) || mode2 != mode ||
            !Word(node + 0x304, out var m2) || m2 != manager ||
            !Word(manager + 0x2C4, out var k2) || k2 != key ||
            !Vector(node + 0x2F8, 4, out var c2, out var n2) || c2 != cards || n2 != count ||
            !Word(cards + key * 4, out var card2) || card2 != card ||
            !Vector(node + 0x2D0, 0x940, out var r2, out var rn2) || r2 != records || rn2 != recordCount ||
            !Byte(records + key * 0x940, out var o2) || o2 != occupied ||
            !Word(node + 0x2E8, out var p2) || p2 != preview || !Under(node, card) ||
            (occupied != 0 && !Under(node, preview))) return null;
        var text = $"File {key + 1} of {count}. {string.Join(" ", cardText)}";
        if (details.Count > 0) text = $"{text.TrimEnd('.')}. {string.Join(" ", details)}";
        if (mode == 1 && occupied == 0) text += ". Unavailable";
        // The instruction is part of the page, so it is spoken when the page is presented rather
        // than again for every file.
        return new("SaveSlots", $"{title.TrimEnd('.')}. {instruction}", $"save:{mode}:{key}", text);
    }
    public bool ConfirmationActive(nuint node) => Byte(node + 0x2EC, out var active) && active != 0;

    /// <summary>0x22F160 keeps the complete UTF-16 lines, split only at the message's own line
    /// breaks, at +0x2D4/+0x2D8 while 0x22F7B0 reveals them a character at a time through its
    /// labels; the complete source is read so a partly revealed line is never spoken.</summary>
    private bool TryReadInstruction(nuint image, nuint node, out uint bar, out string instruction)
    {
        instruction = string.Empty;
        if (!Word(node + StatusBarOffset, out bar) || !Word(bar, out var vt) ||
            vt != image + TopMenuCaptureScope.StatusBarVtableRva || !Under(node, bar) ||
            !Word(bar + 0x2D4, out var begin) || !Word(bar + 0x2D8, out var end) || end < begin ||
            (end - begin) % MsvcWideStringReader.LayoutSize != 0 ||
            (end - begin) / MsvcWideStringReader.LayoutSize > MaximumStatusLines) return false;
        var lines = new List<string>();
        for (var at = begin; at < end; at += MsvcWideStringReader.LayoutSize)
        {
            if (!wide.TryRead(at, out var line, out _)) return false;
            if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.Trim());
        }
        instruction = string.Join(" ", lines);
        return lines.Count > 0;
    }
    private bool Under(nuint root, uint node)
    {
        for (var i = 0; node != 0 && i < 64; i++)
        {
            if (!Byte(node + 0x1AD, out var visible) || visible == 0) return false;
            if (node == root) return true;
            if (!Word(node + 0x16C, out node)) return false;
        }
        return false;
    }
    private bool Owner(nuint image, nuint node) => Word(node, out var vt) && vt == image + NodeVtableRva &&
        Byte(node + 0x1AD, out var visible) && visible != 0 && Byte(node + 0x2EC, out var confirmation) && confirmation == 0;
    private bool Vector(nuint p, uint stride, out uint begin, out uint count)
    {
        count = 0;
        if (!Word(p, out begin) || begin == 0 || !Word(p + 4, out var end) || end < begin ||
            (end - begin) % stride != 0 || (end - begin) / stride > 20) return false;
        count = (end - begin) / stride; return count > 0;
    }
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
