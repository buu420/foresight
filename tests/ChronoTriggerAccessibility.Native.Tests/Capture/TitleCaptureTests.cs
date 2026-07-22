using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class TitleCaptureTests
{
    private const nuint RowBase = 0x12000;

    public static TheoryData<int[], string[]> VisibleMenus => new()
    {
        { [1, 4, 5, 6], ["New Game", "Extras", "Settings", "Quit"] },
        { [0, 1, 4, 5, 6], ["Resume", "New Game", "Extras", "Settings", "Quit"] },
        { [0, 1, 2, 3, 4, 5, 6], ["Resume", "New Game", "Load Game", "New Game +", "Extras", "Settings", "Quit"] },
    };

    [Theory]
    [MemberData(nameof(VisibleMenus))]
    public void BuilderCapturesOnlyConstructedRowsAndResolvesSparseRawFocusKeys(
        int[] visibleRawKeys,
        string[] labels)
    {
        var capture = new TitleCapture();
        using var scope = capture.BeginBuilder();
        ObserveValidatedRowBase(capture);

        for (var index = 0; index < visibleRawKeys.Length; index++)
        {
            var rawKey = visibleRawKeys[index];
            capture.ObserveConstructedRow(
                RowBase + (nuint)(rawKey * TitleCapture.RowStride),
                rowControl: 0x5000u + (nuint)index,
                labels[index]);
        }

        Assert.True(capture.TryCompleteBuilder(out var snapshot, out var error), error);
        Assert.Equal(visibleRawKeys, snapshot.RawKeys);

        for (var index = 0; index < visibleRawKeys.Length; index++)
        {
            Assert.True(snapshot.TryResolve(visibleRawKeys[index], out var item));
            Assert.Equal(labels[index], item.Label);
            Assert.Equal(index + 1, item.Position);
            Assert.Equal(labels.Length, item.Count);
        }

        // Native wraparound arrives as the first raw key again and remains resolvable.
        Assert.True(snapshot.TryResolve(visibleRawKeys[^1], out var last));
        Assert.True(snapshot.TryResolve(visibleRawKeys[0], out var wrapped));
        Assert.Equal(labels[^1], last.Label);
        Assert.Equal(labels[0], wrapped.Label);
    }

    [Fact]
    public void SparseRawKeyIsNeverUsedAsACompactVisibleIndex()
    {
        var capture = new TitleCapture();
        using var scope = capture.BeginBuilder();
        ObserveValidatedRowBase(capture);
        CaptureRow(capture, 1, 0x5100, "New Game");
        CaptureRow(capture, 4, 0x5200, "Extras");
        CaptureRow(capture, 5, 0x5300, "Settings");
        CaptureRow(capture, 6, 0x5400, "Quit");
        Assert.True(capture.TryCompleteBuilder(out var snapshot, out var error), error);

        Assert.False(snapshot.TryResolve(3, out _));
        Assert.Equal("Quit", snapshot.Items[3].Label);
    }

    [Fact]
    public void NullFactoryResultIsNotAnEnabledVisibleRow()
    {
        var capture = new TitleCapture();
        using var scope = capture.BeginBuilder();
        ObserveValidatedRowBase(capture);
        CaptureRow(capture, 1, 0x5100, "New Game");
        CaptureRow(capture, 3, 0, "New Game +");

        Assert.True(capture.TryCompleteBuilder(out var snapshot, out var error), error);
        Assert.True(snapshot.TryResolve(1, out _));
        Assert.False(snapshot.TryResolve(3, out _));
    }

    [Fact]
    public void BuilderRejectsSingleCandidateAndMisalignedFactoryRecord()
    {
        var capture = new TitleCapture();
        using var scope = capture.BeginBuilder();
        capture.ObserveLocalizedResult(0x41, 3, RowBase, "Resume");
        capture.ObserveConstructedRow(RowBase + 1, 0x5000, "Resume");

        Assert.False(capture.TryCompleteBuilder(out _, out var error));
        Assert.Contains("two", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewBuilderInvocationResetsAllStackBaseCandidates()
    {
        var capture = new TitleCapture();
        using (capture.BeginBuilder())
        {
            ObserveValidatedRowBase(capture);
            CaptureRow(capture, 1, 0x5100, "New Game");
            Assert.True(capture.TryCompleteBuilder(out _, out _));
        }

        using var second = capture.BeginBuilder();
        CaptureRow(capture, 1, 0x5100, "New Game");
        Assert.False(capture.TryCompleteBuilder(out _, out var error));
        Assert.Contains("base", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SceneInspectorRecognizesOnlyAuditedTapToStartAndTitleMenuVtables()
    {
        const nuint imageBase = 0x400000;
        const nuint scene = 0xA000;
        const nuint mode = 0xB000;
        var memory = new SegmentedMemory()
            .AddPointer(scene, imageBase + TitleSceneInspector.TitleSceneVtableRva)
            .AddPointer(scene + TitleSceneInspector.CurrentModeOffset, mode)
            .AddPointer(mode, imageBase + TitleSceneInspector.TapToStartVtableRva);
        var inspector = new TitleSceneInspector(memory, imageBase);

        Assert.Equal(TitleModeKind.TapToStart, inspector.InspectMode(scene));

        memory.AddPointer(mode, imageBase + TitleSceneInspector.TitleMenuModeVtableRva);
        Assert.Equal(TitleModeKind.TitleMenu, inspector.InspectMode(scene));

        memory.AddPointer(mode, imageBase + 0x1234);
        Assert.Equal(TitleModeKind.Unknown, inspector.InspectMode(scene));

        memory.AddPointer(mode, imageBase + TitleSceneInspector.TapToStartVtableRva);
        memory.AddPointer(scene, imageBase + 0x7777);
        Assert.Equal(TitleModeKind.Unknown, inspector.InspectMode(scene));
    }

    [Fact]
    public void SceneInspectorRequiresCompleteOwnerModeManagerChainBeforeReadingFocus()
    {
        const nuint imageBase = 0x400000;
        const nuint scene = 0xA000;
        const nuint mode = 0xB000;
        const nuint manager = 0xC000;
        var memory = new SegmentedMemory()
            .AddPointer(scene, imageBase + TitleSceneInspector.TitleSceneVtableRva)
            .AddPointer(scene + TitleSceneInspector.CurrentModeOffset, mode)
            .AddPointer(mode, imageBase + TitleSceneInspector.TitleMenuModeVtableRva)
            .AddPointer(mode + TitleSceneInspector.OwnerOffset, scene)
            .AddPointer(mode + TitleSceneInspector.ManagerOffset, manager)
            .AddPointer(manager, imageBase + TitleSceneInspector.ManagerVtableRva)
            .AddInt32(manager + TitleSceneInspector.ManagerFocusOffset, 4);
        var inspector = new TitleSceneInspector(memory, imageBase);

        Assert.True(inspector.TryInspectTitleMenu(mode, out var state, out var error), error);
        Assert.Equal(scene, state.Owner);
        Assert.Equal(manager, state.Manager);
        Assert.Equal(4, state.RawFocusKey);

        memory.AddPointer(scene + TitleSceneInspector.CurrentModeOffset, 0xDEAD);
        Assert.False(inspector.TryInspectTitleMenu(mode, out _, out var invalidError));
        Assert.Contains("owner", invalidError, StringComparison.OrdinalIgnoreCase);
    }

    private static void ObserveValidatedRowBase(TitleCapture capture)
    {
        capture.ObserveLocalizedResult(0x41, 3, RowBase, "Resume");
        capture.ObserveLocalizedResult(0x41, 0, RowBase + TitleCapture.RowStride, "New Game");
        capture.ObserveLocalizedResult(0x41, 1, RowBase + (2 * TitleCapture.RowStride), "Load Game");
    }

    private static void CaptureRow(TitleCapture capture, int rawKey, nuint control, string label) =>
        capture.ObserveConstructedRow(
            RowBase + (nuint)(rawKey * TitleCapture.RowStride),
            control,
            label);

    private sealed class SegmentedMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public SegmentedMemory AddPointer(nuint address, nuint value) =>
            AddInt32(address, checked((int)value));

        public SegmentedMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
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
}
