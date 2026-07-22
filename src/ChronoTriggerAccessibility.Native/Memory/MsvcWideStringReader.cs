using System.Buffers.Binary;
using System.Text;

namespace ChronoTriggerAccessibility.Native.Memory;

public sealed class MsvcWideStringReader
{
    public const int LayoutSize = 24;
    public const int MaximumCodeUnitLength = 2_048;

    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    private readonly IReadableMemory memory;

    public MsvcWideStringReader(IReadableMemory memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public bool TryRead(nuint stringAddress, out string value, out string error)
    {
        try
        {
            return TryReadCore(stringAddress, out value, out error);
        }
        catch (Exception exception)
        {
            value = string.Empty;
            error = $"MSVC wide-string capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private bool TryReadCore(nuint stringAddress, out string value, out string error)
    {
        value = string.Empty;
        if (!FitsX86Range(stringAddress, LayoutSize))
        {
            error = $"MSVC wide-string layout at 0x{stringAddress:X} crosses the x86 address space.";
            return false;
        }

        Span<byte> layout = stackalloc byte[LayoutSize];
        if (!memory.TryRead(stringAddress, layout))
        {
            error = $"MSVC wide-string layout at 0x{stringAddress:X8} is unreadable.";
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(layout[0x10..]);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(layout[0x14..]);
        if (capacity < length)
        {
            error = $"MSVC wide-string capacity {capacity} is smaller than length {length}.";
            return false;
        }
        if (length > MaximumCodeUnitLength || capacity > MaximumCodeUnitLength)
        {
            error = $"MSVC wide-string length or capacity exceeds the {MaximumCodeUnitLength:N0}-code-unit safety limit.";
            return false;
        }

        var byteLength = checked((int)length * sizeof(char));
        byte[] encoded;
        if (capacity < 8)
        {
            encoded = layout[..byteLength].ToArray();
        }
        else
        {
            var dataAddress = BinaryPrimitives.ReadUInt32LittleEndian(layout);
            if (dataAddress == 0)
            {
                error = "Heap-backed MSVC wide-string has a null data pointer.";
                return false;
            }
            if (byteLength > 0 && !FitsX86Range(dataAddress, byteLength))
            {
                error = $"MSVC wide-string heap range at 0x{dataAddress:X8} crosses the x86 address space.";
                return false;
            }

            encoded = new byte[byteLength];
            if (byteLength > 0 && !memory.TryRead(dataAddress, encoded))
            {
                error = $"MSVC wide-string heap data at 0x{dataAddress:X8} is unreadable.";
                return false;
            }
        }

        try
        {
            value = StrictUtf16LittleEndian.GetString(encoded);
            error = string.Empty;
            return true;
        }
        catch (DecoderFallbackException)
        {
            value = string.Empty;
            error = "MSVC wide-string data is not valid UTF-16.";
            return false;
        }
    }

    private static bool FitsX86Range(nuint address, int byteLength) =>
        address != 0 && byteLength > 0 && address <= uint.MaxValue &&
        (ulong)address + (uint)byteLength - 1 <= uint.MaxValue;
}
