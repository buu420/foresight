using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>The Epoch's time gauge: <c>AgeSelectScene</c> (vtable RVA 3AF778). Slot
/// <c>this+0x2B8</c> is the highlighted era, <c>this+0x2BC</c> the era the Epoch is in
/// (drawn grey, never selectable), <c>this+0x17E4</c> the closing flag. The commit
/// 29A150 writes the chosen location id to D+2E2AF; cancel writes 0xFFFF.</summary>
public sealed record TimeGaugeSnapshot(int Slot, int CurrentEraSlot, bool Closing, int Result,
    string? Label, string? Description)
{
    public const int SlotCount = 7;
    public bool Cancelled => Closing && (Result & 0x8000) != 0;
    public bool Committed => Closing && (Result & 0x8000) == 0 && Result != 0;
}

public sealed class TimeGaugeCapture(IReadableMemory memory)
{
    public const int SlotOffset = 0x2B8, CurrentEraOffset = 0x2BC, ClosingOffset = 0x17E4;
    public const int TextBank = 0x23;

    /// <summary>Table 3AF758: slot order is End of Time, 2300 AD, 1999 AD, 1000 AD, 600 AD,
    /// 12000 BC, 65 000 000 BC. The commit turns slot 5 into 0x1F6 late in the story.</summary>
    public static IReadOnlyList<int> SlotLocations { get; } = [0x1D9, 0x1F2, 0x1F7, 0x1F0, 0x1F1, 0x1F4, 0x1F3];

    /// <summary>Description ids from table 3AF720; slot 0 has none.</summary>
    private static readonly int[] DescriptionIds = [-1, 0xA5, 0xA6, 0xA4, 0xA3, 0xA2, 0xA1];

    public static int LabelId(int slot) => 0xAD - slot;

    public static int? SlotForLocation(int location)
    {
        if (location == 0x1F6) return 5;
        for (var slot = 0; slot < SlotLocations.Count; slot++) if (SlotLocations[slot] == location) return slot;
        return null;
    }

    public TimeGaugeSnapshot? Capture(nuint imageBase, nuint scene)
    {
        try
        {
            if (imageBase == 0 || scene == 0 || (ulong)scene > uint.MaxValue ||
                !Word(scene + SlotOffset, out var slot) || slot >= TimeGaugeSnapshot.SlotCount ||
                !Word(scene + CurrentEraOffset, out var current) || current >= TimeGaugeSnapshot.SlotCount ||
                !Byte(scene + ClosingOffset, out var closing) ||
                !Word(imageBase + 0x41B4BCu, out var data) || data == 0 ||
                !Short((nuint)data + 0x2E2AFu, out var result)) return null;
            var label = Message(imageBase, TextBank, LabelId((int)slot));
            var description = DescriptionIds[slot] < 0 ? null : Message(imageBase, TextBank, DescriptionIds[slot]);
            return new((int)slot, (int)current, closing != 0, result, Clean(label), Clean(description));
        }
        catch { return null; }
    }

    public string? SlotLabel(nuint imageBase, int slot)
    {
        try { return slot is < 0 or >= TimeGaugeSnapshot.SlotCount ? null : Clean(Message(imageBase, TextBank, LabelId(slot))); }
        catch { return null; }
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Replace("\\n", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return trimmed.Contains('<') || trimmed.Contains('>') || trimmed.Length == 0 ? null : trimmed;
    }

    // Same loaded TextManager layout 1B9060 reads: EXE+41C3D8 -> vector of banks ->
    // vector of 24-byte MSVC strings. Read twice so a reloading bank cannot tear.
    private string? Message(nuint imageBase, int bank, int index)
    {
        if (bank < 0 || index < 0 || !Word(imageBase + 0x41C3D8u, out var manager) || manager == 0 ||
            !Vector(manager, 4, 256, out var banks, out var bankCount) || bank >= bankCount ||
            !Word(banks + (nuint)(bank * 4), out var file) || file == 0 ||
            !Vector(file, 24, 16384, out var lines, out var lineCount) || index >= lineCount ||
            !new MsvcStringReader(memory).TryRead(lines + (nuint)(index * 24), out var text, out _) ||
            !Vector(manager, 4, 256, out var banksAgain, out var bankCountAgain) || banks != banksAgain || bankCount != bankCountAgain ||
            !Vector(file, 24, 16384, out var linesAgain, out var lineCountAgain) || lines != linesAgain || lineCount != lineCountAgain)
            return null;
        return text;
    }

    private bool Vector(nuint address, int stride, int maximum, out nuint begin, out int count)
    {
        count = 0; begin = 0;
        if (!Word(address, out var start) || start == 0 || !Word(address + 4, out var end) || end < start ||
            (end - start) % (uint)stride != 0 || (end - start) / (uint)stride > (uint)maximum) return false;
        begin = start; count = (int)((end - start) / (uint)stride); return true;
    }

    private bool Word(nuint address, out uint value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[4];
        if (address == 0 || (ulong)address + 3 > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return true;
    }

    private bool Short(nuint address, out int value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[2];
        if (address == 0 || (ulong)address + 1 > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt16LittleEndian(bytes); return true;
    }

    private bool Byte(nuint address, out byte value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[1];
        if (address == 0 || (ulong)address > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
        value = bytes[0]; return true;
    }
}
