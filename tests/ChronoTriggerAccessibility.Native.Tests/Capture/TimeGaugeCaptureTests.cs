using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class TimeGaugeCaptureTests
{
    private const nuint Image = 0x400000, Scene = 0x700000, Data = 0x200000, Manager = 0x600000,
        Banks = 0x610000, Menu = 0x620000, Lines = 0x630000;

    private static readonly string[] Labels =
        ["65 000 000 B.C.", "12 000 B.C.", "600 A.D.", "1000 A.D.", "1999 A.D.", "2300 A.D.", "End of Time"];

    [Fact]
    public void ReadsTheHighlightedEraItsLabelAndTheCurrentEraFromTheScene()
    {
        var snapshot = new TimeGaugeCapture(Memory(slot: 3, current: 3)).Capture(Image, Scene);
        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot.Slot);
        Assert.Equal(3, snapshot.CurrentEraSlot);
        Assert.Equal("1000 A.D.", snapshot.Label);
        Assert.Equal("Description 0xA4", snapshot.Description);
        Assert.False(snapshot.Closing);
        Assert.False(snapshot.Committed);
        Assert.False(snapshot.Cancelled);
    }

    [Fact]
    public void SlotOrderMatchesTheNativeTableAndLabelsCountDownFromMessage0xAD()
    {
        Assert.Equal([0x1D9, 0x1F2, 0x1F7, 0x1F0, 0x1F1, 0x1F4, 0x1F3], TimeGaugeCapture.SlotLocations);
        var capture = new TimeGaugeCapture(Memory(0, 0));
        Assert.Equal("End of Time", capture.SlotLabel(Image, 0));
        Assert.Equal("65 000 000 B.C.", capture.SlotLabel(Image, 6));
        Assert.Null(capture.SlotLabel(Image, 7));
        Assert.Equal(5, TimeGaugeCapture.SlotForLocation(0x1F6));
        Assert.Equal(2, TimeGaugeCapture.SlotForLocation(0x1F7));
        Assert.Null(TimeGaugeCapture.SlotForLocation(12));
        Assert.Null(new TimeGaugeCapture(Memory(0, 0)).Capture(Image, Scene)!.Description);
    }

    [Fact]
    public void CommitAndCancelAreDistinguishedByTheNativeResultWord()
    {
        var committed = new TimeGaugeCapture(Memory(1, 3, closing: true, result: 0x1F2)).Capture(Image, Scene)!;
        Assert.True(committed.Closing);
        Assert.True(committed.Committed);
        Assert.False(committed.Cancelled);
        var cancelled = new TimeGaugeCapture(Memory(1, 3, closing: true, result: 0xFFFF)).Capture(Image, Scene)!;
        Assert.True(cancelled.Cancelled);
        Assert.False(cancelled.Committed);
    }

    [Fact]
    public void UnreadableOrOutOfRangeSelectionYieldsNothing()
    {
        Assert.Null(new TimeGaugeCapture(Memory(7, 3)).Capture(Image, Scene));
        Assert.Null(new TimeGaugeCapture(Memory(3, 9)).Capture(Image, Scene));
        Assert.Null(new TimeGaugeCapture(new NavigationMemory()).Capture(Image, Scene));
        var missingText = Memory(3, 3).Word(Image + 0x41C3D8, 0);
        var snapshot = new TimeGaugeCapture(missingText).Capture(Image, Scene);
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Label);
    }

    private static NavigationMemory Memory(int slot, int current, bool closing = false, int result = 0)
    {
        var memory = new NavigationMemory()
            .Word(Scene + TimeGaugeCapture.SlotOffset, (uint)slot).Word(Scene + TimeGaugeCapture.CurrentEraOffset, (uint)current)
            .Byte(Scene + TimeGaugeCapture.ClosingOffset, (byte)(closing ? 1 : 0))
            .Word(Image + 0x41B4BC, (uint)Data).Short(Data + 0x2E2AF, (ushort)result)
            .Word(Image + 0x41C3D8, (uint)Manager)
            .Word(Manager, (uint)Banks).Word(Manager + 4, (uint)Banks + 0x24 * 4)
            .Word(Banks + 0x23 * 4, (uint)Menu)
            .Word(Menu, (uint)Lines).Word(Menu + 4, (uint)Lines + 0xB0 * 24);
        for (var id = 0; id < 0xB0; id++) memory.String(Lines + (nuint)(id * 24), $"Description 0x{id:X2}");
        for (var s = 0; s < Labels.Length; s++) memory.String(Lines + (nuint)(TimeGaugeCapture.LabelId(s) * 24), Labels[6 - s]);
        return memory;
    }
}
