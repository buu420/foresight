using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class ModeSelectCaptureTests
{
    private const nuint Scene = 0x10000;
    private const nuint RecordsBegin = 0x20000;

    [Fact]
    public void CapturesExactThreeRowVectorLocalizedStateAndCompositeFocus()
    {
        Assert.Equal(
            new ModeSelectTextContract[]
            {
                new(new(0x23, 0x5A), [new(0x23, 0xC0), new(0x23, 0xC1)], [new(0x3F, 0x05), new(0x3F, 0x06)]),
                new(new(0x3F, 0x31), [new(0x23, 0xC7), new(0x23, 0xC6)], [new(0x3F, 0x32), new(0x3F, 0x33)]),
                new(new(0x42, 0x1A), [new(0x41, 0x55), new(0x41, 0x54)], [new(0x42, 0x1C), new(0x42, 0x1D)]),
            },
            ModeSelectCapture.TextContracts);
        Assert.Equal(new LocalizedMessageKey(0x23, 0xD7), ModeSelectCapture.StartTextKey);
        Assert.Equal(new LocalizedMessageKey(0x23, 0x20), ModeSelectCapture.LowerHelpTextKey);

        var memory = CreateValidMemory(compositeFocus: 11);
        var localizedRows = CreateLocalizedRows();

        Assert.True(ModeSelectCapture.TryCreateSnapshot(
            memory,
            Scene,
            localizedRows,
            "Start",
            "Choose settings, then start.",
            out var snapshot,
            out var error), error);

        Assert.Equal(RecordsBegin, snapshot.RecordsBegin);
        Assert.Equal(RecordsBegin + 3 * ModeSelectCapture.RecordStride, snapshot.RecordsEnd);
        Assert.Equal(3, snapshot.Rows.Count);
        Assert.Equal(new[] { "Battle Mode", "Graphics", "Interface" }, snapshot.Rows.Select(row => row.Label));
        Assert.Equal("Original", snapshot.Rows[1].CurrentValue);
        Assert.Equal("Original graphics help", snapshot.Rows[1].CurrentHelp);
        Assert.Equal(11, snapshot.CompositeFocus);
        Assert.Equal(1, snapshot.Focus.RowIndex);
        Assert.Equal(ModeSelectSubfocus.Right, snapshot.Focus.Subfocus);
        Assert.False(snapshot.Focus.IsStart);
    }

    [Fact]
    public void PreflightsTheEntireAuditedRuntimeLayoutBeforeNativeGettersAreNeeded()
    {
        var memory = CreateValidMemory(compositeFocus: 21);

        Assert.True(ModeSelectCapture.TryValidateRuntimeLayout(
            memory, Scene, out var layout, out var error), error);

        Assert.Equal(RecordsBegin, layout.RecordsBegin);
        Assert.Equal(RecordsBegin + 3 * ModeSelectCapture.RecordStride, layout.RecordsEnd);
        Assert.Equal(21, layout.CompositeFocus);
        Assert.Equal(2, layout.Focus.RowIndex);
        Assert.Equal(3, layout.Rows.Count);
        Assert.Equal(
            new nuint[] { 0x50000, 0x50100, 0x50200 },
            layout.Rows.Select(row => row.GetterTarget));
        Assert.All(layout.Rows, row =>
            Assert.Equal((nuint)(2 * MsvcStringReader.LayoutSize), row.ValuesEnd - row.ValuesBegin));
    }

    [Fact]
    public void PreflightRejectsAnyLaterRowCorruptionWithoutReturningAPartialLayout()
    {
        var memory = CreateValidMemory(compositeFocus: 0);
        var lastRecord = RecordsBegin + 2 * ModeSelectCapture.RecordStride;
        memory.AddPointer(
            lastRecord + ModeSelectCapture.ValuesCapacityOffset,
            0x30200 + 2 * MsvcStringReader.LayoutSize + 1);

        Assert.False(ModeSelectCapture.TryValidateRuntimeLayout(
            memory, Scene, out var layout, out var error));

        Assert.Null(layout);
        Assert.Contains("row 2", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aligned", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 0, ModeSelectSubfocus.Left, false)]
    [InlineData(1, 0, ModeSelectSubfocus.Right, false)]
    [InlineData(10, 1, ModeSelectSubfocus.Left, false)]
    [InlineData(11, 1, ModeSelectSubfocus.Right, false)]
    [InlineData(20, 2, ModeSelectSubfocus.Left, false)]
    [InlineData(21, 2, ModeSelectSubfocus.Right, false)]
    [InlineData(30, 3, ModeSelectSubfocus.Start, true)]
    public void DecodesOnlyAuditedCompositeFocusKeys(
        int compositeFocus,
        int expectedRow,
        ModeSelectSubfocus expectedSubfocus,
        bool expectedStart)
    {
        Assert.True(ModeSelectCapture.TryCreateSnapshot(
            CreateValidMemory(compositeFocus), Scene, CreateLocalizedRows(),
            "Start", "Lower help", out var snapshot, out var error), error);

        Assert.Equal(expectedRow, snapshot.Focus.RowIndex);
        Assert.Equal(expectedSubfocus, snapshot.Focus.Subfocus);
        Assert.Equal(expectedStart, snapshot.Focus.IsStart);
    }

    [Fact]
    public void RejectsInvalidVectorRecordAndFocusStateWithoutPublishingAPartialSnapshot()
    {
        var wrongLength = CreateValidMemory(0)
            .AddPointer(Scene + ModeSelectCapture.RecordsEndOffset, RecordsBegin + 2 * ModeSelectCapture.RecordStride);
        AssertRejected(wrongLength, CreateLocalizedRows(), "0x198");

        var shortCapacity = CreateValidMemory(0)
            .AddPointer(Scene + ModeSelectCapture.RecordsCapacityOffset, RecordsBegin + ModeSelectCapture.RecordStride);
        AssertRejected(shortCapacity, CreateLocalizedRows(), "capacity");

        var wrongRecordOrder = CreateLocalizedRows();
        wrongRecordOrder[1] = wrongRecordOrder[1] with { RecordAddress = RecordsBegin + 2 * ModeSelectCapture.RecordStride };
        AssertRejected(CreateValidMemory(0), wrongRecordOrder, "ordering");

        var wrongValueSpan = CreateValidMemory(0)
            .AddPointer(RecordsBegin + ModeSelectCapture.ValuesEndOffset, 0x30000 + MsvcStringReader.LayoutSize);
        AssertRejected(wrongValueSpan, CreateLocalizedRows(), "value vector");

        var misalignedValueCapacity = CreateValidMemory(0)
            .AddPointer(
                RecordsBegin + ModeSelectCapture.ValuesCapacityOffset,
                0x30000 + 2 * MsvcStringReader.LayoutSize + 1);
        AssertRejected(misalignedValueCapacity, CreateLocalizedRows(), "aligned");

        var nullGetter = CreateValidMemory(0)
            .AddPointer(RecordsBegin + ModeSelectCapture.CurrentValueGetterTargetOffset, 0);
        AssertRejected(nullGetter, CreateLocalizedRows(), "getter");

        AssertRejected(CreateValidMemory(2), CreateLocalizedRows(), "focus");

        var invalidValueIndex = CreateLocalizedRows();
        invalidValueIndex[0] = invalidValueIndex[0] with { CurrentValueIndex = 2 };
        AssertRejected(CreateValidMemory(0), invalidValueIndex, "current value index");

        var blankLocalized = CreateLocalizedRows();
        blankLocalized[0] = blankLocalized[0] with { Help = ["Active help", ""] };
        AssertRejected(CreateValidMemory(0), blankLocalized, "blank");

        var nullRow = CreateLocalizedRows();
        nullRow[0] = null!;
        AssertRejected(CreateValidMemory(0), nullRow, "null");

        var nullValues = CreateLocalizedRows();
        nullValues[0] = nullValues[0] with { Values = null! };
        AssertRejected(CreateValidMemory(0), nullValues, "null");

        AssertRejected(new ThrowingMemory(), CreateLocalizedRows(), "memory");
    }

    private static TestMemory CreateValidMemory(int compositeFocus)
    {
        var memory = new TestMemory()
            .AddPointer(Scene + ModeSelectCapture.RecordsBeginOffset, RecordsBegin)
            .AddPointer(Scene + ModeSelectCapture.RecordsEndOffset, RecordsBegin + 3 * ModeSelectCapture.RecordStride)
            .AddPointer(Scene + ModeSelectCapture.RecordsCapacityOffset, RecordsBegin + 4 * ModeSelectCapture.RecordStride)
            .AddInt32(Scene + ModeSelectCapture.CompositeFocusOffset, compositeFocus);

        for (var index = 0; index < 3; index++)
        {
            var record = RecordsBegin + (nuint)(index * ModeSelectCapture.RecordStride);
            var values = 0x30000u + (nuint)(index * 0x100);
            memory
                .AddPointer(record + ModeSelectCapture.ValuesBeginOffset, values)
                .AddPointer(record + ModeSelectCapture.ValuesEndOffset, values + 2 * MsvcStringReader.LayoutSize)
                .AddPointer(record + ModeSelectCapture.ValuesCapacityOffset, values + 2 * MsvcStringReader.LayoutSize)
                .AddPointer(record + ModeSelectCapture.CurrentValueGetterTargetOffset, 0x50000u + (nuint)(index * 0x100));
        }

        return memory;
    }

    private static List<ModeSelectLocalizedRow> CreateLocalizedRows() =>
    [
        new(RecordsBegin, "Battle Mode", ["ACTIVE", "WAIT"], ["Active help", "Wait help"], 1),
        new(RecordsBegin + ModeSelectCapture.RecordStride, "Graphics", ["Original", "High resolution"], ["Original graphics help", "High resolution help"], 0),
        new(RecordsBegin + 2 * ModeSelectCapture.RecordStride, "Interface", ["Gamepad", "Keyboard"], ["Gamepad help", "Keyboard help"], 1),
    ];

    private static void AssertRejected(
        IReadableMemory memory,
        IReadOnlyList<ModeSelectLocalizedRow> rows,
        string expectedDiagnostic)
    {
        var exception = Record.Exception(() =>
        {
            Assert.False(ModeSelectCapture.TryCreateSnapshot(
                memory, Scene, rows, "Start", "Lower help",
                out var snapshot, out var error));
            Assert.Null(snapshot);
            Assert.Contains(expectedDiagnostic, error, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Null(exception);
    }

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public TestMemory AddPointer(nuint address, nuint value) =>
            AddUInt32(address, checked((uint)value));

        public TestMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            segments[address] = bytes;
            return this;
        }

        private TestMemory AddUInt32(nuint address, uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            segments[address] = bytes;
            return this;
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (!segments.TryGetValue(address, out var bytes) || bytes.Length < destination.Length)
            {
                return false;
            }

            bytes.AsSpan(0, destination.Length).CopyTo(destination);
            return true;
        }
    }

    private sealed class ThrowingMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) =>
            throw new AccessViolationException("simulated malformed pointer");
    }
}
