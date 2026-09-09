using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ChronoTriggerAccessibility.Native.Memory;

public interface IReadableMemory
{
    bool TryRead(nuint address, Span<byte> destination);
}

public sealed class CurrentProcessReadableMemory : IReadableMemory
{
    public bool TryRead(nuint address, Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            return true;
        }

        if (!OperatingSystem.IsWindows() || address == 0)
        {
            return false;
        }

        var buffer = new byte[destination.Length];
        if (!ReadProcessMemory(GetCurrentProcess(), address, buffer, (nuint)buffer.Length, out var bytesRead) ||
            bytesRead != (nuint)buffer.Length)
        {
            return false;
        }

        buffer.CopyTo(destination);
        return true;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        nint process,
        nuint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);
}

public sealed class MsvcStringReader
{
    public const int LayoutSize = 24;
    public const int MaximumByteLength = 4096;
    public const int MaximumNameTextElements = 5;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IReadableMemory memory;

    public MsvcStringReader(IReadableMemory memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public static MsvcStringReader ForCurrentProcess() => new(new CurrentProcessReadableMemory());

    public bool TryRead(nuint stringAddress, out string value, out string error) =>
        TryRead(stringAddress, MaximumByteLength, out value, out error);

    public bool TryRead(
        nuint stringAddress,
        int maximumByteLength,
        out string value,
        out string error)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumByteLength);
        value = string.Empty;
        error = string.Empty;

        Span<byte> layout = stackalloc byte[LayoutSize];
        if (!FitsX86Range(stringAddress, LayoutSize))
        {
            error = $"MSVC string layout at 0x{stringAddress:X} is outside the readable x86 address space.";
            return false;
        }
        if (!memory.TryRead(stringAddress, layout))
        {
            error = $"MSVC string layout at 0x{stringAddress:X} is unreadable.";
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(layout[0x10..]);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(layout[0x14..]);
        if (length > (uint)maximumByteLength)
        {
            var formattedLimit = maximumByteLength.ToString("N0", CultureInfo.InvariantCulture);
            error = $"MSVC string length {length} exceeds the {formattedLimit}-byte safety limit.";
            return false;
        }

        if (capacity < length)
        {
            error = $"MSVC string capacity {capacity} is smaller than length {length}.";
            return false;
        }

        byte[] encoded;
        if (capacity < 16)
        {
            encoded = layout[..checked((int)length)].ToArray();
        }
        else
        {
            if (length == 0)
            {
                return true;
            }

            var dataAddress = BinaryPrimitives.ReadUInt32LittleEndian(layout);
            var dataLength = checked((int)length);
            if (!FitsX86Range(dataAddress, dataLength))
            {
                error = $"MSVC string data range at 0x{dataAddress:X8} crosses the x86 address space.";
                return false;
            }
            encoded = new byte[dataLength];
            if (!memory.TryRead(dataAddress, encoded))
            {
                error = $"MSVC string data at 0x{dataAddress:X8} is unreadable.";
                return false;
            }
        }

        try
        {
            value = StrictUtf8.GetString(encoded);
            return true;
        }
        catch (DecoderFallbackException)
        {
            error = "MSVC string data is not valid UTF-8.";
            return false;
        }
    }

    private static bool FitsX86Range(nuint address, int byteLength) =>
        address != 0 && byteLength > 0 && address <= uint.MaxValue &&
        (ulong)address + (uint)byteLength - 1 <= uint.MaxValue;

    public bool TryReadName(nuint stringAddress, out string value, out string error)
    {
        if (!TryRead(stringAddress, out value, out error))
        {
            return false;
        }

        var textElements = StringInfo.GetTextElementEnumerator(value);
        var count = 0;
        while (textElements.MoveNext())
        {
            count++;
            if (count > MaximumNameTextElements)
            {
                value = string.Empty;
                error = "Name contains more than five visible Unicode text elements.";
                return false;
            }

            if (!ContainsVisibleScalar(textElements.GetTextElement()))
            {
                value = string.Empty;
                error = "Name contains a non-visible Unicode text element.";
                return false;
            }
        }

        return true;
    }

    private static bool ContainsVisibleScalar(string textElement)
    {
        foreach (var rune in textElement.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is not UnicodeCategory.Control and
                not UnicodeCategory.Format and
                not UnicodeCategory.LineSeparator and
                not UnicodeCategory.ParagraphSeparator and
                not UnicodeCategory.SpaceSeparator and
                not UnicodeCategory.NonSpacingMark and
                not UnicodeCategory.SpacingCombiningMark and
                not UnicodeCategory.EnclosingMark and
                not UnicodeCategory.OtherNotAssigned)
            {
                return true;
            }
        }

        return false;
    }
}
