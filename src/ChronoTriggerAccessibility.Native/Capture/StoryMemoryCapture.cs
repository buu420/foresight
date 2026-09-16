using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Read stable expanded script cells. Callers bracket this with their
/// own field/world identity and story-counter checks.</summary>
public static class StoryMemoryCapture
{
    /// <summary>D2/CF call 183090 modes 0/3, whose 183220/183410 helpers
    /// search the first three/all nine expanded roster slots at A+1324C.</summary>
    public static IReadOnlyList<int>? Party(IReadableMemory memory, nuint actors)
    {
        var cells = StableWords(memory, actors + 0x1324Cu, 9, 4, false);
        return cells.Count == 9 && cells.Values.All(v => v is >= 0 and <= 6 or 255)
            ? Enumerable.Range(0, 9).Select(i => cells[i]).ToArray() : null;
    }

    /// <summary>CC calls 182DA0 mode3: compare the dword at A+1A24.</summary>
    public static int? Gold(IReadableMemory memory, nuint actors)
    {
        var cells = StableWords(memory, actors + 0x1A24u, 1, 4, false);
        return cells.TryGetValue(0, out var value) && value is >= 0 and <= 9_999_999 ? value : null;
    }

    /// <summary>Opcode C9 -> 182DA0 mode0 -> 15BE40 scans 0x15B twelve-byte
    /// inventory records at A+28+7E8. IDs and counts are dwords. No item names or
    /// unopened treasure contents are read here.</summary>
    public static IReadOnlyDictionary<int, int>? Inventory(IReadableMemory memory, nuint actors)
    {
        const int count = 0x15B, stride = 12;
        var address = actors + 0x810u;
        if (actors == 0 || (ulong)address + count * stride > uint.MaxValue) return null;
        Span<byte> first = stackalloc byte[count * stride];
        Span<byte> second = stackalloc byte[count * stride];
        if (!memory.TryRead(address, first) || !memory.TryRead(address, second) || !first.SequenceEqual(second)) return null;
        var result = new Dictionary<int, int>();
        for (var i = 0; i < count; i++)
        {
            var item = BinaryPrimitives.ReadInt32LittleEndian(first[(i * stride)..]);
            var held = BinaryPrimitives.ReadInt32LittleEndian(first[(i * stride + 4)..]);
            if (held == 0) continue;
            if (item is <= 0 or > 0xFFFF || held is < 0 or > 99) return null;
            result.TryAdd(item, held); // The native search returns its first match.
        }
        return result;
    }

    public static IReadOnlyDictionary<int, int> StableWords(IReadableMemory memory, nuint address,
        int count, int stride, bool byteValues)
    {
        var result = new Dictionary<int, int>();
        if (count is < 1 or > 512 || stride is not (4 or 8) || address == 0 ||
            (ulong)address + (uint)(count * stride) > uint.MaxValue) return result;
        Span<byte> first = stackalloc byte[count * stride];
        Span<byte> second = stackalloc byte[count * stride];
        if (memory.TryRead(address, first) && memory.TryRead(address, second))
        {
            for (var index = 0; index < count; index++)
            {
                var a = BinaryPrimitives.ReadInt32LittleEndian(first[(index * stride)..]);
                var b = BinaryPrimitives.ReadInt32LittleEndian(second[(index * stride)..]);
                if (a == b) result[index] = byteValues ? a & 255 : a;
            }
        }
        else
        {
            Span<byte> a = stackalloc byte[4];
            Span<byte> b = stackalloc byte[4];
            for (var index = 0; index < count; index++)
            {
                var current = address + (nuint)(index * stride);
                if (!memory.TryRead(current, a) || !memory.TryRead(current, b) || !a.SequenceEqual(b)) continue;
                var value = BinaryPrimitives.ReadInt32LittleEndian(a);
                result[index] = byteValues ? value & 255 : value;
            }
        }
        return result;
    }
}
