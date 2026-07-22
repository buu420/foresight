using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class SettingsCaptureTests
{
    private const nuint ImageBase = 0x400000;

    [Fact]
    public void CapturesTitleTypeZeroRowAndCommittedDisplayedValue()
    {
        var fixture = new SettingsFixture(
            context: 1,
            uiType: 0,
            values: ["Windowed", "Fullscreen"],
            selectedIndex: 1,
            rowIndex: 2,
            rowCount: 6,
            activePage: 1,
            pageCount: 3);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(SettingsContext.Title, snapshot.Context);
        Assert.Equal(1, snapshot.ActivePage);
        Assert.Equal(3, snapshot.PageCount);
        Assert.Equal(0, snapshot.UiType);
        Assert.Equal(10, snapshot.NativeKey);
        Assert.Equal(1, snapshot.SelectedIndex);
        Assert.Equal("Setting 2", snapshot.Control.Label);
        Assert.Equal("Fullscreen", snapshot.Control.Value);
        Assert.Equal("Help 2", snapshot.Control.Help);
        Assert.Equal(3, snapshot.Control.Position);
        Assert.Equal(6, snapshot.Control.Count);
        Assert.True(snapshot.Control.Enabled);
        Assert.True(snapshot.Control.Visible);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 3)]
    [InlineData(2, 1)]
    public void CapturesInGameTypeOneAndTwoRowsWithDynamicLocalizedValues(int uiType, int selectedIndex)
    {
        var values = uiType == 1
            ? new[] { "800 x 600", "1280 x 720", "1600 x 900", "1920 x 1080" }
            : new[] { "Off", "On" };
        var fixture = new SettingsFixture(
            context: 0,
            uiType: uiType,
            values: values,
            selectedIndex: selectedIndex,
            rowIndex: 4,
            rowCount: 9,
            activePage: 0,
            pageCount: 2);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(SettingsContext.InGame, snapshot.Context);
        Assert.Equal(uiType, snapshot.UiType);
        Assert.Equal(17, snapshot.NativeKey);
        Assert.Equal(selectedIndex, snapshot.SelectedIndex);
        Assert.Equal(values[selectedIndex], snapshot.Control.Value);
        Assert.Equal(5, snapshot.Control.Position);
        Assert.Equal(9, snapshot.Control.Count);
    }

    [Fact]
    public void RejectsWrongOwnerIdentityContextAndPagerIdentity()
    {
        AssertFixtureFailure("config vtable", fixture =>
            SettingsFixture.WritePointer(fixture.Config, 0, ImageBase + 0x3A702D));
        AssertFixtureFailure("negative context", fixture =>
            SettingsFixture.WriteInt32(fixture.Config, 0x2F8, -1));
        AssertFixtureFailure("unknown context", fixture =>
            SettingsFixture.WriteInt32(fixture.Config, 0x2F8, 2));
        AssertFixtureFailure("null pager", fixture =>
            SettingsFixture.WritePointer(fixture.Config, 0x2D4, 0));
        AssertFixtureFailure("pager vtable", fixture =>
            SettingsFixture.WritePointer(fixture.Pager, 0, ImageBase + 0x3AC941));
    }

    [Fact]
    public void RejectsInvalidPagerPageAndTransitionState()
    {
        AssertFixtureFailure("zero page count", fixture =>
            SettingsFixture.WriteInt32(fixture.Pager, 0x2D0, 0));
        AssertFixtureFailure("negative page count", fixture =>
            SettingsFixture.WriteInt32(fixture.Pager, 0x2D0, -1));
        AssertFixtureFailure("page count above maximum", _ => { }, new SettingsFixture(pageCount: 33));
        AssertFixtureFailure("negative active page", fixture =>
            SettingsFixture.WriteInt32(fixture.Pager, 0x2D4, -1));
        AssertFixtureFailure("active page out of range", fixture =>
            SettingsFixture.WriteInt32(fixture.Pager, 0x2D4, 1));
        AssertFixtureFailure("config and pager page mismatch", fixture =>
            SettingsFixture.WriteInt32(fixture.Config, 0x2E8, 1));
        AssertFixtureFailure("null active root", fixture =>
            SettingsFixture.WritePointer(fixture.Pager, 0x2D8, 0));
        AssertFixtureFailure("transition", fixture => fixture.Pager[0x2E1] = 1);
    }

    [Fact]
    public void RejectsInvalidDescriptorVectorBoundsAndCount()
    {
        AssertFixtureFailure("null begin", fixture =>
            WriteRawVector(fixture.Config, 0x2C8, 0, 0x3000C, 0x3000C));
        AssertFixtureFailure("reversed end", fixture =>
            WriteRawVector(fixture.Config, 0x2C8, 0x3000C, 0x30000, 0x3000C));
        AssertFixtureFailure("capacity before end", fixture =>
            WriteRawVector(fixture.Config, 0x2C8, 0x30000, 0x3000C, 0x30008));
        AssertFixtureFailure("misaligned begin", fixture =>
            WriteRawVector(fixture.Config, 0x2C8, 0x30001, 0x3000D, 0x3000D));
        AssertFixtureFailure("misaligned end", fixture =>
            WriteRawVector(fixture.Config, 0x2C8, 0x30000, 0x3000D, 0x30018));
        AssertFixtureFailure("misaligned capacity", fixture =>
            WriteRawVector(fixture.Config, 0x2C8, 0x30000, 0x3000C, 0x3000D));
        AssertFixtureFailure("descriptor count mismatch", fixture =>
            SettingsFixture.WriteVector(fixture.Config, 0x2C8, SettingsFixture.DescriptorsAddress, 0));
        AssertFixtureFailure("descriptor capacity above maximum", fixture =>
            WriteRawVector(fixture.Config, 0x2C8, 0x30000, 0x3000C, 0x3018C));
    }

    [Fact]
    public void RejectsInvalidRowVectorBoundsAndCount()
    {
        AssertFixtureFailure("null row begin", fixture =>
            WriteRawVector(fixture.Descriptors, 0, 0, 0x40098, 0x40098));
        AssertFixtureFailure("reversed row end", fixture =>
            WriteRawVector(fixture.Descriptors, 0, 0x40098, 0x40000, 0x40098));
        AssertFixtureFailure("row capacity before end", fixture =>
            WriteRawVector(fixture.Descriptors, 0, 0x40000, 0x40098, 0x40094));
        AssertFixtureFailure("misaligned row begin", fixture =>
            WriteRawVector(fixture.Descriptors, 0, 0x40001, 0x40099, 0x40099));
        AssertFixtureFailure("misaligned row end", fixture =>
            WriteRawVector(fixture.Descriptors, 0, 0x40000, 0x40099, 0x40130));
        AssertFixtureFailure("misaligned row capacity", fixture =>
            WriteRawVector(fixture.Descriptors, 0, 0x40000, 0x40098, 0x40099));
        AssertFixtureFailure("empty rows", fixture =>
            SettingsFixture.WriteVector(fixture.Descriptors, 0, SettingsFixture.RowsAddress, 0));
        AssertFixtureFailure("row count above maximum", fixture =>
            SettingsFixture.WriteVector(fixture.Descriptors, 0, SettingsFixture.RowsAddress, 65 * 0x98));
        AssertFixtureFailure("row capacity above maximum", fixture =>
            WriteRawVector(fixture.Descriptors, 0, 0x40000, 0x40098, 0x42698));
    }

    [Fact]
    public void RejectsUnknownUiTypesAndInvalidSelectedIndices()
    {
        AssertFixtureFailure("negative ui type", fixture =>
            SettingsFixture.WriteInt32(fixture.Rows, 0, -1));
        AssertFixtureFailure("unknown ui type", fixture =>
            SettingsFixture.WriteInt32(fixture.Rows, 0, 3));
        AssertFixtureFailure("negative selected index", _ => { }, new SettingsFixture(selectedIndex: -1, nativeKey: 1));
        AssertFixtureFailure("selected index beyond values", _ => { }, new SettingsFixture(selectedIndex: 2, nativeKey: 1));
    }

    [Fact]
    public void RejectsEveryInvalidManagerKeyCorrelation()
    {
        AssertFixtureFailure("negative key", _ => { }, new SettingsFixture(nativeKey: -1));
        AssertFixtureFailure("sentinel key", _ => { }, new SettingsFixture(nativeKey: int.MinValue));
        AssertFixtureFailure("reserved key", _ => { }, new SettingsFixture(nativeKey: 1000));
        AssertFixtureFailure("large reserved key", _ => { }, new SettingsFixture(nativeKey: int.MaxValue));
        AssertFixtureFailure("row outside vector", _ => { }, new SettingsFixture(nativeKey: 5));
        AssertFixtureFailure("type zero key selects wrong value", _ => { }, new SettingsFixture(selectedIndex: 1, nativeKey: 1));
        AssertFixtureFailure("type zero zero subcontrol", _ => { }, new SettingsFixture(nativeKey: 0));
        AssertFixtureFailure("type zero excess subcontrol", _ => { }, new SettingsFixture(nativeKey: 3));
        AssertFixtureFailure("type one wrong subcontrol", _ => { }, new SettingsFixture(uiType: 1, nativeKey: 2));
        AssertFixtureFailure("type two wrong subcontrol", _ => { }, new SettingsFixture(uiType: 2, nativeKey: 2));
    }

    [Fact]
    public void RejectsInvalidValueCountsAndBlankLocalizedContent()
    {
        AssertFixtureFailure("type zero requires two values", _ => { },
            new SettingsFixture(uiType: 0, values: ["Only"], nativeKey: 1));
        AssertFixtureFailure("type two requires two values", _ => { },
            new SettingsFixture(uiType: 2, values: ["One", "Two", "Three"], nativeKey: 1));
        AssertFixtureFailure("empty values", _ => { },
            new SettingsFixture(uiType: 1, values: [], nativeKey: 1));
        AssertFixtureFailure("all values blank", _ => { },
            new SettingsFixture(uiType: 1, values: [" ", "\t"], nativeKey: 1));
        AssertFixtureFailure("blank label", fixture =>
            SettingsFixture.WriteInlineString(fixture.Rows, 0x04, " "));
        AssertFixtureFailure("blank help", fixture =>
            SettingsFixture.WriteInlineString(fixture.Rows, 0x1C, " "));
    }

    [Fact]
    public void RejectsInvalidValueVectorBoundsAlignmentAndCapacity()
    {
        AssertFixtureFailure("value null begin", fixture =>
            WriteRawVector(fixture.Rows, 0x34, 0, 0x50018, 0x50018));
        AssertFixtureFailure("value reversed end", fixture =>
            WriteRawVector(fixture.Rows, 0x34, 0x50018, 0x50000, 0x50018));
        AssertFixtureFailure("value capacity before end", fixture =>
            WriteRawVector(fixture.Rows, 0x34, 0x50000, 0x50018, 0x50010));
        AssertFixtureFailure("value span misalignment", fixture =>
            WriteRawVector(fixture.Rows, 0x34, 0x50000, 0x50019, 0x50030));
        AssertFixtureFailure("value capacity misalignment", fixture =>
            WriteRawVector(fixture.Rows, 0x34, 0x50000, 0x50018, 0x50019));
        AssertFixtureFailure("value capacity above reader maximum", fixture =>
            WriteRawVector(fixture.Rows, 0x34, 0x50000, 0x50018, 0x51818));
    }

    [Fact]
    public void RejectsUnreadableOrMalformedLocalizedStringsAndValues()
    {
        AssertFixtureFailure("unreadable label", fixture =>
            WriteUnreadableHeapString(fixture.Rows, 0x04));
        AssertFixtureFailure("unreadable help", fixture =>
            WriteUnreadableHeapString(fixture.Rows, 0x1C));
        AssertFixtureFailure("invalid label utf8", fixture =>
            WriteInvalidInlineString(fixture.Rows, 0x04));
        AssertFixtureFailure("invalid help utf8", fixture =>
            WriteInvalidInlineString(fixture.Rows, 0x1C));
        AssertFixtureFailure("unreadable values", fixture => fixture.Memory.Remove(SettingsFixture.ValuesAddress));
        AssertFixtureFailure("invalid value utf8", fixture =>
            WriteInvalidInlineString(fixture.Values, 0));
    }

    [Fact]
    public void RejectsUnreadableNativeOwnersHeadersAndRows()
    {
        AssertFixtureFailure("config unreadable", fixture => fixture.Memory.Remove(SettingsFixture.ConfigAddress));
        AssertFixtureFailure("pager unreadable", fixture => fixture.Memory.Remove(SettingsFixture.PagerAddress));
        AssertFixtureFailure("descriptors unreadable", fixture => fixture.Memory.Remove(SettingsFixture.DescriptorsAddress));
        AssertFixtureFailure("rows unreadable", fixture => fixture.Memory.Remove(SettingsFixture.RowsAddress));
        AssertFixtureFailure("manager unreadable", fixture => fixture.Memory.Remove(SettingsFixture.ManagerAddress));

        AssertCaptureFailure(new ThrowingMemory(), ImageBase, SettingsFixture.ConfigAddress, SettingsFixture.ManagerAddress, "memory exception");
    }

    [Fact]
    public void RejectsZeroAndOverflowingX86Addresses()
    {
        var fixture = new SettingsFixture();
        var aboveX86 = unchecked((nuint)uint.MaxValue + (nuint)1);
        AssertCaptureFailure(null, ImageBase, SettingsFixture.ConfigAddress, SettingsFixture.ManagerAddress, "null memory");
        AssertCaptureFailure(fixture.Memory, 0, SettingsFixture.ConfigAddress, SettingsFixture.ManagerAddress, "zero image base");
        AssertCaptureFailure(fixture.Memory, uint.MaxValue, SettingsFixture.ConfigAddress, SettingsFixture.ManagerAddress, "image-base overflow");
        AssertCaptureFailure(fixture.Memory, aboveX86, SettingsFixture.ConfigAddress, SettingsFixture.ManagerAddress, "image base above x86");
        AssertCaptureFailure(fixture.Memory, ImageBase, 0, SettingsFixture.ManagerAddress, "zero config");
        AssertCaptureFailure(fixture.Memory, ImageBase, (nuint)uint.MaxValue - 0x2F7u, SettingsFixture.ManagerAddress, "config overflow");
        AssertCaptureFailure(fixture.Memory, ImageBase, aboveX86, SettingsFixture.ManagerAddress, "config above x86");
        AssertCaptureFailure(fixture.Memory, ImageBase, SettingsFixture.ConfigAddress, 0, "zero manager");
        AssertCaptureFailure(fixture.Memory, ImageBase, SettingsFixture.ConfigAddress, (nuint)uint.MaxValue - 0x2C3u, "manager overflow");
        AssertCaptureFailure(fixture.Memory, ImageBase, SettingsFixture.ConfigAddress, aboveX86, "manager above x86");

        AssertFixtureFailure("pager overflow", current =>
            SettingsFixture.WritePointer(current.Config, 0x2D4, (nuint)uint.MaxValue - 0x2E0u));
        AssertFixtureFailure("descriptor range overflow", current =>
            WriteRawVector(current.Config, 0x2C8, 0xFFFF_FFF8, 0xFFFF_FFFC, 0xFFFF_FFFC));
        AssertFixtureFailure("row range overflow", current =>
            WriteRawVector(current.Descriptors, 0, 0xFFFF_FF70, 0xFFFF_FFF8, 0xFFFF_FFF8));
        AssertFixtureFailure("value vector range overflow", current =>
            WriteRawVector(current.Rows, 0x34, 0xFFFF_FFF0, 0xFFFF_FFF8, 0xFFFF_FFF8));
    }

    [Fact]
    public void RevalidatesConfigVtableAfterCapturingDependentState()
    {
        var fixture = new SettingsFixture();
        var memory = new MutatingVtableMemory(fixture.Memory, SettingsFixture.ConfigAddress);

        AssertCaptureFailure(memory, ImageBase, SettingsFixture.ConfigAddress, SettingsFixture.ManagerAddress, "mutated config vtable");
    }

    [Fact]
    public void CopiesStringsAndDoesNotDereferenceStoredGetterOrSetterObjects()
    {
        var fixture = new SettingsFixture(
            uiType: 1,
            values: ["800 x 600", "1920 x 1080"],
            selectedIndex: 1,
            rowCount: 2,
            rowIndex: 1);
        const nuint getterObject = 0x71000;
        const nuint setterObject = 0x72000;
        SettingsFixture.WritePointer(fixture.Rows, 0x98 + 0x4C, getterObject);
        SettingsFixture.WritePointer(fixture.Rows, 0x98 + 0x70, setterObject);
        fixture.Memory.Forbid(getterObject).Forbid(setterObject);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Array.Fill(fixture.Rows, (byte)0);
        Array.Fill(fixture.Values, (byte)0);
        Array.Fill(fixture.Config, (byte)0);
        Array.Fill(fixture.Pager, (byte)0);

        Assert.Equal(SettingsContext.Title, snapshot.Context);
        Assert.Equal("Setting 1", snapshot.Control.Label);
        Assert.Equal("1920 x 1080", snapshot.Control.Value);
        Assert.Equal("Help 1", snapshot.Control.Help);
        Assert.DoesNotContain(fixture.Memory.Reads, read => read.Address == getterObject || read.Address == setterObject);
    }

    private static void AssertFixtureFailure(
        string name,
        Action<SettingsFixture> mutate,
        SettingsFixture? fixture = null)
    {
        fixture ??= new SettingsFixture();
        mutate(fixture);
        var succeeded = fixture.TryCapture(out var snapshot, out var diagnostic);
        Assert.True(!succeeded, name);
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }

    private static void AssertCaptureFailure(
        IReadableMemory? memory,
        nuint imageBase,
        nuint config,
        nuint manager,
        string name)
    {
        var succeeded = SettingsCapture.TryCreateSnapshot(
            memory, imageBase, config, manager, out var snapshot, out var diagnostic);
        Assert.True(!succeeded, name);
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }

    private static void WriteRawVector(byte[] destination, int offset, uint begin, uint end, uint capacity)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset), begin);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 4), end);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 8), capacity);
    }

    private static void WriteUnreadableHeapString(byte[] destination, int offset)
    {
        destination.AsSpan(offset, MsvcStringReader.LayoutSize).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset), 0x90000);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x10), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x14), 16);
    }

    private static void WriteInvalidInlineString(byte[] destination, int offset)
    {
        destination.AsSpan(offset, MsvcStringReader.LayoutSize).Clear();
        destination[offset] = 0xFF;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x14), 15);
    }

    private sealed class SettingsFixture
    {
        internal const nuint ConfigAddress = 0x10000;
        internal const nuint PagerAddress = 0x20000;
        internal const nuint DescriptorsAddress = 0x30000;
        internal const nuint RowsAddress = 0x40000;
        internal const nuint ValuesAddress = 0x50000;
        internal const nuint ManagerAddress = 0x60000;
        internal const nuint ActiveRootAddress = 0x70000;

        private const int ConfigSize = 0x2FC;
        private const int PagerSize = 0x2E2;
        private const int ManagerSize = 0x2C8;
        private const int DescriptorStride = 0x0C;
        private const int RowStride = 0x98;

        public SettingsFixture(
            int context = 1,
            int uiType = 0,
            IReadOnlyList<string>? values = null,
            int selectedIndex = 0,
            int rowIndex = 0,
            int rowCount = 1,
            int activePage = 0,
            int pageCount = 1,
            int? nativeKey = null)
        {
            values ??= ["Low", "High"];
            Config = new byte[ConfigSize];
            Pager = new byte[PagerSize];
            Descriptors = new byte[checked(pageCount * DescriptorStride)];
            Rows = new byte[checked(rowCount * RowStride)];
            Manager = new byte[ManagerSize];

            WritePointer(Config, 0, ImageBase + 0x3A702C);
            WriteVector(Config, 0x2C8, DescriptorsAddress, Descriptors.Length);
            WritePointer(Config, 0x2D4, PagerAddress);
            WriteInt32(Config, 0x2E8, activePage);
            WriteInt32(Config, 0x2F8, context);

            WritePointer(Pager, 0, ImageBase + 0x3AC940);
            WriteInt32(Pager, 0x2D0, pageCount);
            WriteInt32(Pager, 0x2D4, activePage);
            WritePointer(Pager, 0x2D8, ActiveRootAddress);
            Pager[0x2E1] = 0;

            for (var page = 0; page < pageCount; page++)
            {
                var begin = page == activePage ? RowsAddress : RowsAddress + 0x10000u + (nuint)(page * 0x1000);
                WriteVector(Descriptors, page * DescriptorStride, begin, Rows.Length);
            }

            var rowOffset = checked(rowIndex * RowStride);
            WriteInt32(Rows, rowOffset, uiType);
            WriteInlineString(Rows, rowOffset + 0x04, $"Setting {rowIndex}");
            WriteInlineString(Rows, rowOffset + 0x1C, $"Help {rowIndex}");
            Values = CreateStringVector(values);
            WriteVector(Rows, rowOffset + 0x34, ValuesAddress, Values.Length);
            WriteInt32(Rows, rowOffset + 0x90, selectedIndex);

            var key = nativeKey ?? (uiType == 0 ? checked(rowIndex * 4 + selectedIndex + 1) : checked(rowIndex * 4 + 1));
            WriteInt32(Manager, 0x2C4, key);

            Memory = new SegmentedMemory()
                .Add(ConfigAddress, Config)
                .Add(PagerAddress, Pager)
                .Add(DescriptorsAddress, Descriptors)
                .Add(RowsAddress, Rows)
                .Add(ValuesAddress, Values)
                .Add(ManagerAddress, Manager)
                .Add(ActiveRootAddress, [0]);
        }

        public SegmentedMemory Memory { get; }
        public byte[] Config { get; }
        public byte[] Pager { get; }
        public byte[] Descriptors { get; }
        public byte[] Rows { get; }
        public byte[] Values { get; }
        public byte[] Manager { get; }

        public bool TryCapture(out SettingsSnapshot snapshot, out string diagnostic) =>
            SettingsCapture.TryCreateSnapshot(
                Memory,
                ImageBase,
                ConfigAddress,
                ManagerAddress,
                out snapshot,
                out diagnostic);

        private static byte[] CreateStringVector(IReadOnlyList<string> values)
        {
            var bytes = new byte[checked(values.Count * MsvcStringReader.LayoutSize)];
            for (var index = 0; index < values.Count; index++)
            {
                WriteInlineString(bytes, index * MsvcStringReader.LayoutSize, values[index]);
            }
            return bytes;
        }

        internal static void WriteVector(byte[] destination, int offset, nuint begin, int byteLength)
        {
            WritePointer(destination, offset, begin);
            WritePointer(destination, offset + 4, begin + (nuint)byteLength);
            WritePointer(destination, offset + 8, begin + (nuint)byteLength);
        }

        internal static void WriteInlineString(byte[] destination, int offset, string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            Assert.True(encoded.Length <= 15, "Settings test strings must fit the x86 MSVC small-string buffer.");
            encoded.CopyTo(destination, offset);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x10), (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x14), 15);
        }

        internal static void WritePointer(byte[] destination, int offset, nuint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset), checked((uint)value));

        internal static void WriteInt32(byte[] destination, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset), value);
    }

    private sealed class SegmentedMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];
        private readonly HashSet<nuint> forbiddenAddresses = [];

        public List<(nuint Address, int Length)> Reads { get; } = [];

        public SegmentedMemory Add(nuint address, byte[] bytes)
        {
            segments.Add(address, bytes);
            return this;
        }

        public void Remove(nuint address) => segments.Remove(address);

        public SegmentedMemory Forbid(nuint address)
        {
            forbiddenAddresses.Add(address);
            return this;
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            Reads.Add((address, destination.Length));
            if (forbiddenAddresses.Contains(address))
            {
                throw new InvalidOperationException($"Forbidden function object dereference at 0x{address:X8}.");
            }
            foreach (var (segmentAddress, segment) in segments)
            {
                if (address < segmentAddress)
                {
                    continue;
                }
                var offset = address - segmentAddress;
                if (offset <= int.MaxValue && (ulong)offset + (ulong)destination.Length <= (ulong)segment.Length)
                {
                    segment.AsSpan((int)offset, destination.Length).CopyTo(destination);
                    return true;
                }
            }
            return false;
        }
    }

    private sealed class MutatingVtableMemory(IReadableMemory inner, nuint configAddress) : IReadableMemory
    {
        private int configReadCount;

        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address == configAddress)
            {
                configReadCount++;
                if (configReadCount > 1 && destination.Length == sizeof(uint))
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(destination, 0xDEADBEEF);
                    return true;
                }
            }
            return inner.TryRead(address, destination);
        }
    }

    private sealed class ThrowingMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) =>
            throw new InvalidOperationException("Synthetic Settings memory failure.");
    }
}
