using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Memory;

public sealed class MsvcStringReaderTests
{
    private const nuint StringAddress = 0x1000;
    private const nuint HeapAddress = 0x9000;

    [Fact]
    public void TryRead_InlineUtf8String_ReturnsDecodedText()
    {
        var memory = new SegmentedMemory().Add(StringAddress, CreateInlineLayout("Crono"));

        var succeeded = new MsvcStringReader(memory).TryRead(StringAddress, out var value, out var error);

        Assert.True(succeeded, error);
        Assert.Equal("Crono", value);
    }

    [Fact]
    public void TryRead_HeapUtf8String_UsesValidatedX86Pointer()
    {
        const string expected = "クロノ・トリガー";
        var encoded = Encoding.UTF8.GetBytes(expected);
        var memory = new SegmentedMemory()
            .Add(StringAddress, CreateHeapLayout(HeapAddress, encoded.Length, encoded.Length))
            .Add(HeapAddress, encoded);

        var succeeded = new MsvcStringReader(memory).TryRead(StringAddress, out var value, out var error);

        Assert.True(succeeded, error);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryRead_UnreadableLayout_IsRejectedWithoutDereferencingIt()
    {
        var succeeded = new MsvcStringReader(new SegmentedMemory()).TryRead(
            StringAddress,
            out var value,
            out var error);

        Assert.False(succeeded);
        Assert.Equal(string.Empty, value);
        Assert.Contains("layout", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_UnreadableHeapPointer_IsRejected()
    {
        var memory = new SegmentedMemory().Add(StringAddress, CreateHeapLayout(0xDEADBEEF, 16, 16));

        var succeeded = new MsvcStringReader(memory).TryRead(StringAddress, out _, out var error);

        Assert.False(succeeded);
        Assert.Contains("data", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_LengthOver4096_IsRejectedBeforeReadingData()
    {
        var memory = new SegmentedMemory().Add(StringAddress, CreateHeapLayout(0xDEADBEEF, 4097, 4097));

        var succeeded = new MsvcStringReader(memory).TryRead(StringAddress, out _, out var error);

        Assert.False(succeeded);
        Assert.Contains("4,096", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_CapacitySmallerThanLength_IsRejected()
    {
        var memory = new SegmentedMemory().Add(StringAddress, CreateInlineLayout("Crono", capacity: 4));

        var succeeded = new MsvcStringReader(memory).TryRead(StringAddress, out _, out var error);

        Assert.False(succeeded);
        Assert.Contains("capacity", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_InvalidUtf8_IsRejectedStrictly()
    {
        var layout = CreateInlineLayout("ok");
        layout[0] = 0xC3;
        layout[1] = 0x28;
        var memory = new SegmentedMemory().Add(StringAddress, layout);

        var succeeded = new MsvcStringReader(memory).TryRead(StringAddress, out _, out var error);

        Assert.False(succeeded);
        Assert.Contains("UTF-8", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Crono")]
    [InlineData("クロノ五")]
    [InlineData("😀😁😂😃😄")]
    [InlineData("ééééé")]
    public void TryReadName_AtMostFiveUnicodeTextElements_IsAccepted(string expected)
    {
        var memory = CreateMemoryForText(expected);

        var succeeded = new MsvcStringReader(memory).TryReadName(StringAddress, out var value, out var error);

        Assert.True(succeeded, error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("Chrono")]
    [InlineData("😀😁😂😃😄😅")]
    [InlineData("éééééé")]
    public void TryReadName_MoreThanFiveUnicodeTextElements_IsRejected(string text)
    {
        var memory = CreateMemoryForText(text);

        var succeeded = new MsvcStringReader(memory).TryReadName(StringAddress, out _, out var error);

        Assert.False(succeeded);
        Assert.Contains("five", error, StringComparison.OrdinalIgnoreCase);
    }

    private static SegmentedMemory CreateMemoryForText(string text)
    {
        var encoded = Encoding.UTF8.GetBytes(text);
        return encoded.Length < 16
            ? new SegmentedMemory().Add(StringAddress, CreateInlineLayout(text))
            : new SegmentedMemory()
                .Add(StringAddress, CreateHeapLayout(HeapAddress, encoded.Length, encoded.Length))
                .Add(HeapAddress, encoded);
    }

    private static byte[] CreateInlineLayout(string value, int? capacity = null)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        Assert.True(encoded.Length <= 15, "Inline fixture does not fit the MSVC x86 small-string buffer.");
        var layout = new byte[24];
        encoded.CopyTo(layout, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), (uint)(capacity ?? 15));
        return layout;
    }

    private static byte[] CreateHeapLayout(nuint pointer, int length, int capacity)
    {
        var layout = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(layout, checked((uint)pointer));
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), checked((uint)length));
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), checked((uint)capacity));
        return layout;
    }

    private sealed class SegmentedMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public SegmentedMemory Add(nuint address, byte[] bytes)
        {
            segments.Add(address, bytes);
            return this;
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            foreach (var (segmentAddress, segment) in segments)
            {
                if (address < segmentAddress)
                {
                    continue;
                }

                var offset = address - segmentAddress;
                if (offset > int.MaxValue || (ulong)offset + (ulong)destination.Length > (ulong)segment.Length)
                {
                    continue;
                }

                segment.AsSpan((int)offset, destination.Length).CopyTo(destination);
                return true;
            }

            return false;
        }
    }
}
