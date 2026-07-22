using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Memory;

public sealed class MsvcWideStringReaderTests
{
    private const nuint StringAddress = 0x1000;
    private const nuint HeapAddress = 0x9000;

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void TryRead_InlineCapacityBelowEight_ReturnsStrictUtf16(int length)
    {
        var expected = new string('\u03A9', length);
        var memory = new SegmentedMemory().Add(StringAddress, Inline(expected, capacity: 7));

        var succeeded = new MsvcWideStringReader(memory).TryRead(StringAddress, out var value, out var error);

        Assert.True(succeeded, error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("Chrono Trigger")]
    [InlineData("時の最果て")]
    [InlineData("A😀B")]
    public void TryRead_HeapCapacityEightOrMore_ReturnsUnicode(string expected)
    {
        var encoded = Encoding.Unicode.GetBytes(expected);
        var memory = new SegmentedMemory()
            .Add(StringAddress, Heap(HeapAddress, expected.Length, Math.Max(8, expected.Length)))
            .Add(HeapAddress, encoded);

        var succeeded = new MsvcWideStringReader(memory).TryRead(StringAddress, out var value, out var error);

        Assert.True(succeeded, error);
        Assert.Equal(expected, value);
        Assert.Equal([(StringAddress, 24), (HeapAddress, encoded.Length)], memory.Reads);
    }

    [Fact]
    public void TryRead_IsolatedSurrogate_IsRejectedStrictly()
    {
        var layout = Inline("A", capacity: 7);
        BinaryPrimitives.WriteUInt16LittleEndian(layout, 0xD800);
        var memory = new SegmentedMemory().Add(StringAddress, layout);

        Assert.False(new MsvcWideStringReader(memory).TryRead(StringAddress, out var value, out var error));
        Assert.Equal(string.Empty, value);
        Assert.Contains("UTF-16", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(9, 8)]
    [InlineData(2049, 2049)]
    [InlineData(1, 2049)]
    public void TryRead_InvalidCapacityOrDefensiveLimits_AreRejected(uint length, uint capacity)
    {
        var memory = new SegmentedMemory().Add(StringAddress, Heap(HeapAddress, length, capacity));

        Assert.False(new MsvcWideStringReader(memory).TryRead(StringAddress, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Single(memory.Reads);
    }

    [Fact]
    public void TryRead_UnreadableLayoutAndData_ReturnDiagnostics()
    {
        AssertFailure(new SegmentedMemory(), StringAddress);
        AssertFailure(
            new SegmentedMemory().Add(StringAddress, Heap(HeapAddress, 8, 8)),
            StringAddress);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue - 22u)]
    public void TryRead_NullOrCrossingLayout_IsRejectedBeforeRead(uint address)
    {
        var memory = new SegmentedMemory();

        AssertFailure(memory, address);
        Assert.Empty(memory.Reads);
    }

    [Fact]
    public void TryRead_NullOrCrossingHeapRange_IsRejectedBeforeDataRead()
    {
        var nullMemory = new SegmentedMemory().Add(StringAddress, Heap(0, 8, 8));
        AssertFailure(nullMemory, StringAddress);
        Assert.Single(nullMemory.Reads);

        var crossingMemory = new SegmentedMemory().Add(
            StringAddress,
            Heap(uint.MaxValue - 2u, 2, 8));
        AssertFailure(crossingMemory, StringAddress);
        Assert.Single(crossingMemory.Reads);
    }

    [Fact]
    public void TryRead_LayoutsAndHeapDataEndingAtX86MaximumRemainValid()
    {
        const nuint exactLayoutEnd = uint.MaxValue - MsvcWideStringReader.LayoutSize + 1u;
        var inline = new SegmentedMemory().Add(exactLayoutEnd, Inline("ok", 7));
        Assert.True(new MsvcWideStringReader(inline).TryRead(
            exactLayoutEnd, out var inlineValue, out var inlineError), inlineError);
        Assert.Equal("ok", inlineValue);

        const nuint exactHeapEnd = uint.MaxValue - 3u;
        var heap = new SegmentedMemory()
            .Add(StringAddress, Heap(exactHeapEnd, 2, 8))
            .Add(exactHeapEnd, Encoding.Unicode.GetBytes("ok"));
        Assert.True(new MsvcWideStringReader(heap).TryRead(
            StringAddress, out var heapValue, out var heapError), heapError);
        Assert.Equal("ok", heapValue);
    }

    [Fact]
    public void TryRead_MemoryException_IsContained()
    {
        Assert.False(new MsvcWideStringReader(new ThrowingMemory()).TryRead(
            StringAddress,
            out var value,
            out var error));
        Assert.Equal(string.Empty, value);
        Assert.Contains("failed safely", error, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertFailure(IReadableMemory memory, nuint address)
    {
        Assert.False(new MsvcWideStringReader(memory).TryRead(address, out var value, out var error));
        Assert.Equal(string.Empty, value);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private static byte[] Inline(string value, uint capacity)
    {
        var layout = new byte[24];
        Encoding.Unicode.GetBytes(value).CopyTo(layout, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), checked((uint)value.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), capacity);
        return layout;
    }

    private static byte[] Heap(nuint pointer, int length, int capacity) =>
        Heap(pointer, checked((uint)length), checked((uint)capacity));

    private static byte[] Heap(nuint pointer, uint length, uint capacity)
    {
        var layout = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(layout, checked((uint)pointer));
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), length);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), capacity);
        return layout;
    }

    private sealed class SegmentedMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public List<(nuint Address, int Length)> Reads { get; } = [];

        public SegmentedMemory Add(nuint address, byte[] value)
        {
            segments.Add(address, value);
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
                if (offset <= int.MaxValue &&
                    (ulong)offset + (ulong)destination.Length <= (ulong)segment.Length)
                {
                    segment.AsSpan((int)offset, destination.Length).CopyTo(destination);
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class ThrowingMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) =>
            throw new InvalidOperationException("fixture fault");
    }
}
