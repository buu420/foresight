using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed class NavigationTextCapture(IReadableMemory memory)
{
    private nuint fieldManager;
    private int fieldBank = -1;

    /// <summary>294550 requests file 46 hex, then substitutes saved character names
    /// via 14830. Read the same loaded text and names without invoking game code.</summary>
    public string? WorldName(nuint imageBase, int index)
    {
        try
        {
            if (index is < 0 or >= 112 || !Word(imageBase + 0x41C3D8u, out var manager)) return null;
            var text = Message(manager, 0x46, index);
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var (token, character) in new[] { ("<NAME_CRO>", 0), ("<NAME_LUC>", 2),
                ("<NAME_MAG>", 6), ("<NAME_AYL>", 5) })
            {
                if (!text.Contains(token, StringComparison.Ordinal)) continue;
                if (!Word(imageBase + 0x41B4C4u, out var actors) ||
                    !new MsvcStringReader(memory).TryReadName(actors + 0x1908u + (nuint)(character * 24), out var name, out _)) return null;
                text = text.Replace(token, name, StringComparison.Ordinal);
            }
            return text.Contains('<') || text.Contains('>') ? null : text.Trim();
        }
        catch { return null; }
    }

    public string? FieldName(nuint imageBase, int scene)
    {
        try
        {
            if (scene is <= 0 or >= 496 || !Word(imageBase + 0x41C3D8u, out var manager)) return null;
            if (fieldManager != manager || fieldBank < 0)
            {
                fieldManager = manager; fieldBank = -1;
                if (!Vector(manager, 4, 256, out _, out var count)) return null;
                for (var bank = 0; bank < count; bank++)
                    if (Message(manager, bank, 0)?.StartsWith("MSG_DEBUG_MAP_01,", StringComparison.Ordinal) == true)
                    {
                        if (fieldBank >= 0) { fieldBank = -1; return null; }
                        fieldBank = bank;
                    }
            }
            var text = fieldBank < 0 ? null : Message(manager, fieldBank, scene - 1);
            var expected = $"MSG_DEBUG_MAP_{scene:00},";
            if (text?.StartsWith(expected, StringComparison.Ordinal) != true) return null;
            return text[expected.Length..].Replace("\\", ", ", StringComparison.Ordinal).Trim();
        }
        catch { return null; }
    }

    // 1B9060 indexes a vector of pointers to vectors of 24-byte MSVC strings.
    private string? Message(nuint manager, int bank, int index)
    {
        if (bank < 0 || index < 0 || !Vector(manager, 4, 256, out var banks, out var bankCount) || bank >= bankCount ||
            !Word(banks + (nuint)(bank * 4), out var file) ||
            !Vector(file, 24, 16384, out var lines, out var lineCount) || index >= lineCount ||
            !new MsvcStringReader(memory).TryRead(lines + (nuint)(index * 24), out var text, out _) ||
            !Vector(manager, 4, 256, out var banksAgain, out var bankCountAgain) || banks != banksAgain || bankCount != bankCountAgain ||
            !Vector(file, 24, 16384, out var linesAgain, out var lineCountAgain) || lines != linesAgain || lineCount != lineCountAgain)
            return null;
        return text;
    }

    private bool Vector(nuint address, int stride, int maximum, out nuint begin, out int count)
    {
        count = 0;
        if (!Word(address, out begin) || !Word(address + 4, out var end) || end < begin ||
            (end - begin) % (nuint)stride != 0 || (end - begin) / (nuint)stride > (nuint)maximum) return false;
        count = (int)((end - begin) / (nuint)stride); return true;
    }

    private bool Word(nuint address, out nuint value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[4];
        if (address == 0 || (ulong)address + 3 > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return value != 0;
    }
}
