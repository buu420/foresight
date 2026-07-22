using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Memory;

public sealed class MsvcStringVectorReaderTests
{
    private const nuint VectorAddress = 0x1000;
    private const nuint ElementsAddress = 0x2000;
    private const nuint HeapAddress = 0x9000;

    [Fact]
    public void TryRead_InlineAndHeapStringsAtExactStride_ReturnsImmutableSnapshot()
    {
        const string heapText = "クロノ・トリガー";
        var heapBytes = Encoding.UTF8.GetBytes(heapText);
        var elements = new byte[2 * MsvcStringReader.LayoutSize];
        CreateInlineLayout("Crono").CopyTo(elements, 0);
        CreateHeapLayout(HeapAddress, heapBytes.Length, heapBytes.Length)
            .CopyTo(elements, MsvcStringReader.LayoutSize);
        var memory = new SegmentedMemory()
            .Add(VectorAddress, CreateVectorHeader(ElementsAddress, elements.Length, elements.Length))
            .Add(ElementsAddress, elements)
            .Add(HeapAddress, heapBytes);

        var succeeded = new MsvcStringVectorReader(memory).TryRead(
            VectorAddress,
            out var values,
            out var error);

        Assert.True(succeeded, error);
        Assert.Equal(["Crono", heapText], values);
        Assert.Throws<NotSupportedException>(() =>
        {
            ((IList<string>)values)[0] = "Marle";
        });

        elements[0] = (byte)'X';
        heapBytes[0] = (byte)'X';
        Assert.Equal(["Crono", heapText], values);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x2000u)]
    public void TryRead_EmptyVector_ReturnsEmptySnapshot(uint pointer)
    {
        var memory = new SegmentedMemory().Add(VectorAddress, CreateVectorHeader(pointer, 0, 0));

        var succeeded = new MsvcStringVectorReader(memory).TryRead(
            VectorAddress,
            out var values,
            out var error);

        Assert.True(succeeded, error);
        Assert.Empty(values);
    }

    [Fact]
    public void TryRead_NullEmptyBeginAndEndRequireNullCapacity()
    {
        var memory = new SegmentedMemory().Add(
            VectorAddress,
            CreateVectorHeader((nuint)0, (nuint)0, (nuint)MsvcStringReader.LayoutSize));

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("null", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(memory.Reads);
    }

    [Theory]
    [MemberData(nameof(InvalidBounds))]
    public void TryRead_InvalidBoundsFailClosed(uint begin, uint end, uint capacity, string diagnostic)
    {
        var memory = new SegmentedMemory().Add(VectorAddress, CreateVectorHeader(begin, end, capacity));

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains(diagnostic, error, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<uint, uint, uint, string> InvalidBounds => new()
    {
        { 0x2000, 0x1FFF, 0x2000, "reversed" },
        { 0x2000, 0x2018, 0x2010, "capacity" },
        { 0x2000, 0x2017, 0x2018, "0x18" },
        { 0x2000, 0x2018, 0x2020, "capacity" },
        { 0x0001, 0xFFFFFFF1, 0xFFFFFFF1, "limit" },
    };

    [Fact]
    public void TryRead_HeaderAddressWhoseTwelveBytesCrossX86Space_IsRejectedBeforeRead()
    {
        var memory = new SegmentedMemory();

        var succeeded = new MsvcStringVectorReader(memory).TryRead(
            uint.MaxValue - 7u,
            out var values,
            out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("x86", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(memory.Reads);
    }

    [Fact]
    public void TryRead_CountLimitIsCheckedBeforeElementAddressMultiplication()
    {
        var count = MsvcStringVectorReader.MaximumElementCount + 1u;
        var span = checked(count * (uint)MsvcStringReader.LayoutSize);
        var memory = new SegmentedMemory().Add(
            VectorAddress,
            CreateVectorHeader(ElementsAddress, checked((int)span), checked((int)span)));

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("count", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(memory.Reads);
    }

    [Fact]
    public void TryRead_VectorByteLimitIsCheckedBeforeReadingElements()
    {
        var byteLength = MsvcStringVectorReader.MaximumVectorByteLength + MsvcStringReader.LayoutSize;
        var memory = new SegmentedMemory().Add(
            VectorAddress,
            CreateVectorHeader(ElementsAddress, byteLength, byteLength));

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("byte", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(memory.Reads);
    }

    [Fact]
    public void TryRead_CapacityByteLimitIsRejectedEvenWhenLiveSpanIsSmall()
    {
        var element = CreateInlineLayout("Crono");
        var capacityLength = MsvcStringVectorReader.MaximumVectorByteLength + MsvcStringReader.LayoutSize;
        var memory = new SegmentedMemory()
            .Add(VectorAddress, CreateVectorHeader(ElementsAddress, element.Length, capacityLength))
            .Add(ElementsAddress, element);

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("capacity", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(memory.Reads);
    }

    [Fact]
    public void TryRead_UnreadableHeader_IsRejected()
    {
        var succeeded = new MsvcStringVectorReader(new SegmentedMemory()).TryRead(
            VectorAddress,
            out var values,
            out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("header", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_UnreadableElement_IsRejectedWithoutPartialSnapshot()
    {
        var memory = new SegmentedMemory()
            .Add(VectorAddress, CreateVectorHeader(ElementsAddress, 2 * MsvcStringReader.LayoutSize, 2 * MsvcStringReader.LayoutSize))
            .Add(ElementsAddress, CreateInlineLayout("Crono"));

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("element 1", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_InvalidUtf8Element_IsRejectedWithoutPartialSnapshot()
    {
        var invalid = CreateInlineLayout("ok");
        invalid[0] = 0xC3;
        invalid[1] = 0x28;
        var memory = new SegmentedMemory()
            .Add(VectorAddress, CreateVectorHeader(ElementsAddress, invalid.Length, invalid.Length))
            .Add(ElementsAddress, invalid);

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("UTF-8", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_PerStringByteLimitIsInheritedFromMsvcStringReader()
    {
        var layout = CreateHeapLayout(
            HeapAddress,
            MsvcStringReader.MaximumByteLength + 1,
            MsvcStringReader.MaximumByteLength + 1);
        var memory = new SegmentedMemory()
            .Add(VectorAddress, CreateVectorHeader(ElementsAddress, layout.Length, layout.Length))
            .Add(ElementsAddress, layout);

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("4,096", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_HeapElementCrossingX86AddressSpaceFailsWithoutPartialSnapshot()
    {
        const nuint crossingDataAddress = uint.MaxValue - 7u;
        var element = CreateHeapLayout(crossingDataAddress, 16, 16);
        var memory = new SegmentedMemory()
            .Add(VectorAddress, CreateVectorHeader(ElementsAddress, element.Length, element.Length))
            .Add(ElementsAddress, element)
            .Add(crossingDataAddress, Enumerable.Repeat((byte)'A', 16).ToArray());

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("x86", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(memory.Reads, read => read.Address == crossingDataAddress);
    }

    [Fact]
    public void TryRead_TotalDecodedByteLimitIsRejectedWithoutPartialSnapshot()
    {
        var perStringLength = MsvcStringReader.MaximumByteLength;
        var count = (MsvcStringVectorReader.MaximumTotalStringByteLength / perStringLength) + 1;
        var elements = new byte[count * MsvcStringReader.LayoutSize];
        var memory = new SegmentedMemory()
            .Add(VectorAddress, CreateVectorHeader(ElementsAddress, elements.Length, elements.Length));
        for (var index = 0; index < count; index++)
        {
            var heapAddress = HeapAddress + checked((nuint)(index * (perStringLength + 0x100)));
            CreateHeapLayout(heapAddress, perStringLength, perStringLength)
                .CopyTo(elements, index * MsvcStringReader.LayoutSize);
            memory.Add(heapAddress, Enumerable.Repeat((byte)'A', perStringLength).ToArray());
        }
        memory.Add(ElementsAddress, elements);

        var succeeded = new MsvcStringVectorReader(memory).TryRead(VectorAddress, out var values, out var error);

        Assert.False(succeeded);
        Assert.Empty(values);
        Assert.Contains("total", error, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateVectorHeader(nuint begin, int span, int capacitySpan) =>
        CreateVectorHeader(begin, checked(begin + (nuint)span), checked(begin + (nuint)capacitySpan));

    private static byte[] CreateVectorHeader(nuint begin, nuint end, nuint capacity)
    {
        var header = new byte[MsvcStringVectorReader.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)begin));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)end));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), checked((uint)capacity));
        return header;
    }

    private static byte[] CreateInlineLayout(string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        Assert.True(encoded.Length <= 15);
        var layout = new byte[MsvcStringReader.LayoutSize];
        encoded.CopyTo(layout, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 15);
        return layout;
    }

    private static byte[] CreateHeapLayout(nuint pointer, int length, int capacity)
    {
        var layout = new byte[MsvcStringReader.LayoutSize];
        BinaryPrimitives.WriteUInt32LittleEndian(layout, checked((uint)pointer));
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), checked((uint)length));
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), checked((uint)capacity));
        return layout;
    }

    private sealed class SegmentedMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public List<(nuint Address, int Length)> Reads { get; } = [];

        public SegmentedMemory Add(nuint address, byte[] bytes)
        {
            segments.Add(address, bytes);
            return this;
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            Reads.Add((address, destination.Length));
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
