using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Loaded TextManager messages, using the same native vectors as RVA 1B9060.</summary>
public sealed class LoadedGameTextCapture(IReadableMemory memory)
{
    public string? Get(nuint image, int bank, int index)
    {
        if (bank < 0 || index < 0 || !Word(image + 0x41C3D8, out var manager) || manager == 0 ||
            !Vector(manager, 4, 256, out var banks, out var bankCount) || bank >= bankCount ||
            !Word(banks + (nuint)(bank * 4), out var file) || file == 0 ||
            !Vector(file, 24, 16384, out var lines, out var lineCount) || index >= lineCount ||
            !new MsvcStringReader(memory).TryRead(lines + (nuint)(index * 24), out var text, out _) ||
            string.IsNullOrWhiteSpace(text) ||
            !Word(image + 0x41C3D8, out var m2) || m2 != manager ||
            !Vector(manager, 4, 256, out var b2, out var bc2) || b2 != banks || bc2 != bankCount ||
            !Word(banks + (nuint)(bank * 4), out var f2) || f2 != file ||
            !Vector(file, 24, 16384, out var l2, out var lc2) || l2 != lines || lc2 != lineCount) return null;
        return text.Trim();
    }
    private bool Vector(nuint p, int stride, int max, out uint begin, out int count)
    {
        count = 0;
        if (!Word(p, out begin) || begin == 0 || !Word(p + 4, out var end) || end < begin ||
            (end - begin) % stride != 0 || (end - begin) / stride > max) return false;
        count = (int)((end - begin) / stride); return true;
    }
    private bool Word(nuint address, out uint value)
    {
        value = 0; Span<byte> data = stackalloc byte[4];
        if (address == 0 || (ulong)address + 4 > 0x100000000UL || !memory.TryRead(address, data)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(data); return true;
    }
}
