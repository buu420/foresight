using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace ChronoTriggerAccessibility.Native.Memory;

public sealed class MsvcStringVectorReader
{
    public const int HeaderSize = 12;
    public const uint MaximumElementCount = 512;
    public const int MaximumVectorByteLength = 6_144;
    public const int MaximumTotalStringByteLength = 524_288;

    private readonly IReadableMemory memory;
    private readonly MsvcStringReader stringReader;

    public MsvcStringVectorReader(IReadableMemory memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        stringReader = new MsvcStringReader(memory);
    }

    public bool TryRead(nuint vectorAddress, out IReadOnlyList<string> values, out string error)
    {
        try
        {
            return TryReadCore(vectorAddress, out values, out error);
        }
        catch (Exception exception)
        {
            values = Array.Empty<string>();
            error = $"MSVC string vector capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private bool TryReadCore(nuint vectorAddress, out IReadOnlyList<string> values, out string error)
    {
        values = Array.Empty<string>();
        if (!FitsX86Range(vectorAddress, HeaderSize))
        {
            error = $"MSVC string vector header at 0x{vectorAddress:X} crosses the x86 address space.";
            return false;
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        if (!memory.TryRead(vectorAddress, header))
        {
            error = $"MSVC string vector header at 0x{vectorAddress:X8} is unreadable.";
            return false;
        }

        var begin = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (end < begin)
        {
            error = "MSVC string vector end pointer is reversed before its begin pointer.";
            return false;
        }
        if (capacity < end)
        {
            error = "MSVC string vector capacity pointer is before its end pointer.";
            return false;
        }

        var span = end - begin;
        var capacitySpan = capacity - begin;
        if (span % MsvcStringReader.LayoutSize != 0)
        {
            error = "MSVC string vector byte span is not aligned to the exact 0x18-byte string stride.";
            return false;
        }
        if (capacitySpan % MsvcStringReader.LayoutSize != 0)
        {
            error = "MSVC string vector capacity is not aligned to the exact 0x18-byte string stride.";
            return false;
        }

        var count = span / MsvcStringReader.LayoutSize;
        if (count > MaximumElementCount)
        {
            error = $"MSVC string vector count {count} exceeds the {MaximumElementCount}-element safety limit.";
            return false;
        }
        if (span > MaximumVectorByteLength)
        {
            error = $"MSVC string vector byte span {span} exceeds the {MaximumVectorByteLength}-byte safety limit.";
            return false;
        }
        if (capacitySpan > MaximumVectorByteLength)
        {
            error = $"MSVC string vector capacity span {capacitySpan} exceeds the {MaximumVectorByteLength}-byte safety limit.";
            return false;
        }
        if (span == 0)
        {
            error = string.Empty;
            return true;
        }
        if (begin == 0)
        {
            error = "Non-empty MSVC string vector has a null begin pointer.";
            return false;
        }

        var captured = new string[checked((int)count)];
        var totalStringBytes = 0;
        for (var index = 0; index < captured.Length; index++)
        {
            var offset = checked((uint)index * (uint)MsvcStringReader.LayoutSize);
            if (!TryAddX86(begin, offset, out var elementAddress))
            {
                error = $"MSVC string vector element {index} address overflows the x86 address space.";
                return false;
            }
            if (!stringReader.TryRead(elementAddress, out var value, out var stringError))
            {
                error = $"MSVC string vector element {index} is invalid: {stringError}";
                return false;
            }

            totalStringBytes = checked(totalStringBytes + Encoding.UTF8.GetByteCount(value));
            if (totalStringBytes > MaximumTotalStringByteLength)
            {
                error = $"MSVC string vector total decoded UTF-8 length exceeds the {MaximumTotalStringByteLength}-byte safety limit.";
                return false;
            }
            captured[index] = new string(value.AsSpan());
        }

        values = new ReadOnlyCollection<string>(captured);
        error = string.Empty;
        return true;
    }

    private static bool FitsX86Range(nuint address, int byteLength) =>
        byteLength >= 0 && address <= uint.MaxValue &&
        (ulong)address + (uint)byteLength - (byteLength == 0 ? 0u : 1u) <= uint.MaxValue;

    private static bool TryAddX86(uint address, uint offset, out nuint result)
    {
        var sum = (ulong)address + offset;
        if (sum > uint.MaxValue)
        {
            result = 0;
            return false;
        }
        result = (nuint)sum;
        return true;
    }
}
