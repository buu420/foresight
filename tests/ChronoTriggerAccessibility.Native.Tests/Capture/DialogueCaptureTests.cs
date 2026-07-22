using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class DialogueCaptureTests
{
    private const nuint ImageBase = 0x400000;
    private const nuint WindowAddress = 0x1000;
    private const nuint StringsAddress = 0x6000;
    private const nuint FlagsAddress = 0x8000;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TryCreateSnapshot_ActiveRenderingPhasesCaptureOnlyCurrentLine(int phase)
    {
        var memory = CreateMemory(
            ["Earlier", "Current line", "Future line"],
            [0x01, 0x08, 0x04],
            currentLine: 1,
            pageBase: 0,
            phase: phase);

        var succeeded = DialogueCapture.TryCreateSnapshot(
            memory, ImageBase, WindowAddress, out var snapshot, out var error);

        Assert.True(succeeded, error);
        Assert.Equal(WindowAddress, snapshot.Window);
        Assert.Equal(1, snapshot.Cursor);
        Assert.Equal(0, snapshot.PageBase);
        Assert.Equal(phase, snapshot.Phase);
        Assert.Equal(new DialogueLineSnapshot(1, "Current line", 0x08), snapshot.Line);
        Assert.Null(snapshot.Choices);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void TryCreateSnapshot_WaitAndPageTransitionPhasesDoNotExposeNextParsedLine(int phase)
    {
        var memory = CreateMemory(
            ["Visible line", "Not yet"],
            [0x08, 0x04],
            currentLine: 1,
            pageBase: 0,
            phase: phase);

        var succeeded = DialogueCapture.TryCreateSnapshot(
            memory, ImageBase, WindowAddress, out var snapshot, out var error);

        Assert.True(succeeded, error);
        Assert.Equal(phase, snapshot.Phase);
        Assert.Null(snapshot.Line);
        Assert.Null(snapshot.Choices);
    }

    [Fact]
    public void TryCreateSnapshot_CursorAtEndIsValidCommittedOpenStateWithoutFutureLine()
    {
        var memory = CreateMemory(["Last line"], [0x08], currentLine: 1, pageBase: 0, phase: 1);

        var succeeded = DialogueCapture.TryCreateSnapshot(
            memory, ImageBase, WindowAddress, out var snapshot, out var error);

        Assert.True(succeeded, error);
        Assert.Equal(1, snapshot.Cursor);
        Assert.Null(snapshot.Line);
        Assert.Null(snapshot.Choices);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void TryCreateSnapshot_PhaseFourCapturesContiguousChoicesAndValidFocus(int selectedChoice)
    {
        var memory = CreateMemory(
            ["Question", "Yes", "No"],
            [0x08, 0x11, 0x10],
            currentLine: 3,
            pageBase: 0,
            phase: 4,
            selectedChoice: selectedChoice,
            choiceCount: 2);

        var succeeded = DialogueCapture.TryCreateSnapshot(
            memory, ImageBase, WindowAddress, out var snapshot, out var error);

        Assert.True(succeeded, error);
        Assert.Null(snapshot.Line);
        Assert.NotNull(snapshot.Choices);
        Assert.Equal(1, snapshot.Choices.FirstLineIndex);
        Assert.Equal(["Yes", "No"], snapshot.Choices.Labels);
        Assert.Equal([0x11u, 0x10u], snapshot.Choices.Flags);
        Assert.Equal(selectedChoice, snapshot.Choices.SelectedIndex);
        Assert.Throws<NotSupportedException>(() =>
        {
            ((IList<string>)snapshot.Choices.Labels)[0] = "Maybe";
        });
        Assert.Throws<NotSupportedException>(() =>
        {
            ((IList<uint>)snapshot.Choices.Flags)[0] = 0;
        });
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x04)]
    [InlineData(0x08)]
    [InlineData(0x0F)]
    public void TryCreateSnapshot_KnownOrdinaryFlagsIncludingAlignmentAreAccepted(int flags)
    {
        var memory = CreateMemory(["Line"], [flags], currentLine: 0, pageBase: 0, phase: 0);

        var succeeded = DialogueCapture.TryCreateSnapshot(
            memory, ImageBase, WindowAddress, out var snapshot, out var error);

        Assert.True(succeeded, error);
        Assert.Equal((uint)flags, snapshot.Line!.Flags);
    }

    [Theory]
    [MemberData(nameof(MalformedScalarStates))]
    public void TryCreateSnapshot_MalformedScalarStateReturnsDiagnosticAndNoPartialSnapshot(
        int active,
        int currentLine,
        int pageBase,
        int phase,
        int selectedChoice,
        int choiceCount,
        string diagnostic)
    {
        var memory = CreateMemory(
            ["Line", "Yes", "No", "Later", "Last"],
            [0x08, 0x10, 0x10, 0, 0],
            active,
            currentLine,
            pageBase,
            phase,
            selectedChoice,
            choiceCount);

        AssertFails(memory, diagnostic);
    }

    public static TheoryData<int, int, int, int, int, int, string> MalformedScalarStates => new()
    {
        { 0, 0, 0, 0, -1, 0, "active" },
        { 2, 0, 0, 0, -1, 0, "active" },
        { 1, -1, 0, 0, -1, 0, "current line" },
        { 1, 6, 0, 0, -1, 0, "current line" },
        { 1, 1, -1, 0, -1, 0, "page" },
        { 1, 1, 2, 0, -1, 0, "page" },
        { 1, 5, 0, 0, -1, 0, "four" },
        { 1, 0, 0, -1, -1, 0, "phase" },
        { 1, 0, 0, 5, -1, 0, "phase" },
        { 1, 2, 0, 4, -1, 0, "choice count" },
        { 1, 2, 0, 4, -1, 3, "range" },
        { 1, 3, 0, 4, -2, 2, "selected" },
        { 1, 3, 0, 4, 2, 2, "selected" },
        { 1, 5, 1, 4, -1, 5, "four" },
    };

    [Fact]
    public void TryCreateSnapshot_StringAndFlagCountsMustMatch()
    {
        var memory = CreateMemory(["Line", "Future"], [0x08], currentLine: 0, pageBase: 0, phase: 0);

        AssertFails(memory, "equal");
    }

    [Fact]
    public void TryCreateSnapshot_ActiveWindowRequiresAtLeastOneParsedLine()
    {
        var memory = CreateMemory([], [], currentLine: 0, pageBase: 0, phase: 0);

        AssertFails(memory, "empty");
    }

    [Fact]
    public void TryCreateSnapshot_UnknownFlagBitsAreRejected()
    {
        var memory = CreateMemory(["Line"], [0x20], currentLine: 0, pageBase: 0, phase: 0);

        AssertFails(memory, "flag");
    }

    [Fact]
    public void TryCreateSnapshot_ChoiceRangeMustBeContiguouslyFlagged()
    {
        var memory = CreateMemory(
            ["Question", "Yes", "No"],
            [0x08, 0x10, 0x01],
            currentLine: 3,
            pageBase: 0,
            phase: 4,
            selectedChoice: 0,
            choiceCount: 2);

        AssertFails(memory, "choice flag");
    }

    [Fact]
    public void TryCreateSnapshot_ChoiceRangeMustBeContainedInVisiblePage()
    {
        var memory = CreateMemory(
            ["Question", "Yes", "No"],
            [0x08, 0x10, 0x10],
            currentLine: 3,
            pageBase: 2,
            phase: 4,
            selectedChoice: 0,
            choiceCount: 2);

        AssertFails(memory, "visible page");
    }

    [Fact]
    public void TryCreateSnapshot_OrdinaryPhaseCannotExposeChoiceFlagAsDialogueLine()
    {
        var memory = CreateMemory(["Yes"], [0x10], currentLine: 0, pageBase: 0, phase: 0);

        AssertFails(memory, "choice flag");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreateSnapshot_BlankRequiredVisibleTextIsRejected(string text)
    {
        var memory = CreateMemory([text], [0x08], currentLine: 0, pageBase: 0, phase: 0);

        AssertFails(memory, "blank");
    }

    [Fact]
    public void TryCreateSnapshot_InvalidUtf8InRequiredLineReturnsNoPartialSnapshot()
    {
        var memory = CreateMemory(["Line"], [0x08], currentLine: 0, pageBase: 0, phase: 0);
        memory.BytesAt(StringsAddress)[0] = 0xC3;
        memory.BytesAt(StringsAddress)[1] = 0x28;

        AssertFails(memory, "UTF-8");
    }

    [Fact]
    public void TryCreateSnapshot_UnreadableRequiredFlagElementReturnsNoPartialSnapshot()
    {
        var memory = CreateMemory(["Line"], [0x08], currentLine: 0, pageBase: 0, phase: 0);
        memory.Remove(FlagsAddress);

        AssertFails(memory, "flags");
    }

    [Fact]
    public void TryCreateSnapshot_FlagCapacityLimitIsRejectedBeforeReadingElements()
    {
        var memory = CreateMemory(["Line"], [0x08], currentLine: 0, pageBase: 0, phase: 0);
        var window = memory.BytesAt(WindowAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(
            window.AsSpan(DialogueCapture.FlagsOffset + 8),
            checked((uint)(FlagsAddress + 0x10000u)));

        AssertFails(memory, "capacity");
    }

    [Fact]
    public void TryCreateSnapshot_MalformedCapturedVectorHeadersReturnNoPartialSnapshot()
    {
        var strings = CreateMemory(["Line"], [0x08], currentLine: 0, pageBase: 0, phase: 0);
        var stringWindow = strings.BytesAt(WindowAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(
            stringWindow.AsSpan(DialogueCapture.ParsedStringsOffset + 4),
            checked((uint)(StringsAddress - 1)));
        AssertFails(strings, "reversed");

        var flags = CreateMemory(["Line"], [0x08], currentLine: 0, pageBase: 0, phase: 0);
        var flagWindow = flags.BytesAt(WindowAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(
            flagWindow.AsSpan(DialogueCapture.FlagsOffset + 4),
            checked((uint)(FlagsAddress + 3)));
        AssertFails(flags, "4-byte");
    }

    [Fact]
    public void TryCreateSnapshot_UnreadableOrUnexpectedVtableFailsBeforeObjectDereference()
    {
        var unreadable = new SegmentedMemory();
        AssertFails(unreadable, "vtable");
        Assert.Single(unreadable.Reads);

        var unexpected = CreateMemory(["Line"], [0x08], currentLine: 0, pageBase: 0, phase: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(unexpected.BytesAt(WindowAddress), 0xDEADBEEF);
        AssertFails(unexpected, "vtable");
        Assert.Single(unexpected.Reads);
    }

    [Fact]
    public void TryCreateSnapshot_WindowAndImageAddressOverflowFailBeforeRead()
    {
        var memory = new SegmentedMemory();

        var imageSucceeded = DialogueCapture.TryCreateSnapshot(
            memory, uint.MaxValue, WindowAddress, out var imageSnapshot, out var imageError);
        var windowSucceeded = DialogueCapture.TryCreateSnapshot(
            memory, ImageBase, uint.MaxValue - DialogueCapture.ObjectSize + 2u,
            out var windowSnapshot, out var windowError);

        Assert.False(imageSucceeded);
        Assert.Null(imageSnapshot);
        Assert.Contains("x86", imageError, StringComparison.OrdinalIgnoreCase);
        Assert.False(windowSucceeded);
        Assert.Null(windowSnapshot);
        Assert.Contains("x86", windowError, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(memory.Reads);
    }

    [Fact]
    public void TryCreateSnapshot_ThrownMemoryExceptionIsConvertedToDiagnostic()
    {
        var succeeded = DialogueCapture.TryCreateSnapshot(
            new ThrowingMemory(), ImageBase, WindowAddress, out var snapshot, out var error);

        Assert.False(succeeded);
        Assert.Null(snapshot);
        Assert.Contains("failed safely", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateSnapshot_ChoiceStringsAndFlagsAreDefensiveSnapshots()
    {
        var memory = CreateMemory(
            ["Yes", "No"], [0x10, 0x11], currentLine: 2, pageBase: 0, phase: 4,
            selectedChoice: 0, choiceCount: 2);
        Assert.True(DialogueCapture.TryCreateSnapshot(
            memory, ImageBase, WindowAddress, out var snapshot, out var error), error);

        memory.BytesAt(StringsAddress)[0] = (byte)'X';
        BinaryPrimitives.WriteUInt32LittleEndian(memory.BytesAt(FlagsAddress), 0);

        Assert.Equal(["Yes", "No"], snapshot.Choices!.Labels);
        Assert.Equal([0x10u, 0x11u], snapshot.Choices.Flags);
    }

    private static void AssertFails(IReadableMemory memory, string diagnostic)
    {
        var succeeded = DialogueCapture.TryCreateSnapshot(
            memory, ImageBase, WindowAddress, out var snapshot, out var error);

        Assert.False(succeeded);
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains(diagnostic, error, StringComparison.OrdinalIgnoreCase);
    }

    private static SegmentedMemory CreateMemory(
        string[] strings,
        int[] flags,
        int active = 1,
        int currentLine = 0,
        int pageBase = 0,
        int phase = 0,
        int selectedChoice = -1,
        int choiceCount = 0)
    {
        var window = new byte[DialogueCapture.ObjectSize];
        BinaryPrimitives.WriteUInt32LittleEndian(
            window,
            checked((uint)(ImageBase + DialogueCapture.VtableRva)));
        window[DialogueCapture.ActiveOffset] = checked((byte)active);
        WriteInt32(window, DialogueCapture.CurrentLineOffset, currentLine);
        WriteInt32(window, DialogueCapture.PageBaseOffset, pageBase);
        WriteInt32(window, DialogueCapture.PhaseOffset, phase);
        WriteInt32(window, DialogueCapture.SelectedChoiceOffset, selectedChoice);
        WriteInt32(window, DialogueCapture.ChoiceCountOffset, choiceCount);

        var stringElements = new byte[strings.Length * MsvcStringReader.LayoutSize];
        for (var index = 0; index < strings.Length; index++)
        {
            CreateInlineLayout(strings[index]).CopyTo(stringElements, index * MsvcStringReader.LayoutSize);
        }
        WriteVectorHeader(window, DialogueCapture.ParsedStringsOffset, StringsAddress, stringElements.Length);

        var flagElements = new byte[flags.Length * sizeof(int)];
        for (var index = 0; index < flags.Length; index++)
        {
            WriteInt32(flagElements, index * sizeof(int), flags[index]);
        }
        WriteVectorHeader(window, DialogueCapture.FlagsOffset, FlagsAddress, flagElements.Length);

        return new SegmentedMemory()
            .Add(WindowAddress, window)
            .Add(StringsAddress, stringElements)
            .Add(FlagsAddress, flagElements);
    }

    private static void WriteVectorHeader(byte[] destination, int offset, nuint begin, int byteLength)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset), checked((uint)begin));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 4), checked((uint)(begin + (nuint)byteLength)));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 8), checked((uint)(begin + (nuint)byteLength)));
    }

    private static void WriteInt32(byte[] destination, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset), value);

    private static byte[] CreateInlineLayout(string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        Assert.True(encoded.Length <= 15, "Dialogue fixture must fit the x86 MSVC small-string buffer.");
        var layout = new byte[MsvcStringReader.LayoutSize];
        encoded.CopyTo(layout, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 15);
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

        public byte[] BytesAt(nuint address) => segments[address];

        public void Remove(nuint address) => segments.Remove(address);

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

    private sealed class ThrowingMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) =>
            throw new InvalidOperationException("synthetic memory failure");
    }
}
