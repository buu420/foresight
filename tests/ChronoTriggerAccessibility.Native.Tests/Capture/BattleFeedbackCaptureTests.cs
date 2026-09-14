using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class BattleFeedbackCaptureTests
{
    private const nuint Menu = 0x100000, Label = 0x110000, Protocol = 0x120000, Getter = 0x130000;
    [Fact]
    public void NumericKindComesFromTheDisplayedResultBucketAndRejectsUnboundedRecords()
    {
        const nuint image = 0x400000, canvas = 0x1000000;
        var m = new NavigationMemory().Word(image + 0x41B4C4, (uint)canvas)
            .Word(canvas + 0x1A89C, 2).Word(image + 0x398E68 + 8, 88)
            .Word(canvas + 0x140AC + 88 * 4 + 3 * 16, 4);
        var c = new BattleFeedbackCapture(m);
        Assert.Equal(4, c.ReadEffectKind(image, 3));
        Assert.Equal(0, c.ReadEffectKind(image, 11));
        m.Word(canvas + 0x1A89C, 6);
        Assert.Equal(0, c.ReadEffectKind(image, 3));
    }
    [Fact]
    public void ReadsFinishedNativeMessageAndRejectsWrongGetterOrHiddenLabel()
    {
        var memory = new NavigationMemory().Word(Menu + 0x234, (uint)Label)
            .Word(Label + 0x278, (uint)Protocol).Word(Protocol + 8, (uint)Getter)
            .Add(Getter, [0x8D, 0x41, 0x28, 0xC3]).Byte(Label + 0x1AD, 1)
            .String(Label + 0x2A0, "Crono gained 12 EXP!");
        var capture = new BattleFeedbackCapture(memory);
        Assert.Equal("Crono gained 12 EXP!", capture.ReadMessage(Menu));
        memory.Byte(Label + 0x1AD, 0);
        Assert.Null(capture.ReadMessage(Menu));
        memory.Byte(Label + 0x1AD, 1).Byte(Getter, 0xC3);
        Assert.Null(capture.ReadMessage(Menu));
        Assert.Null(capture.ReadMessage(uint.MaxValue - 4));
    }

    [Fact]
    public void OnlyVisiblePopupsAreReadAndAHiddenBadStringCannotSuppressOtherSlots()
    {
        var memory = new NavigationMemory().Add(Menu + 0x1B5, new byte[11])
            .String(Menu + 0x34 + 3 * 24, "1200").Add(Menu + 0x194 + 3 * 3, [255,255,255]);
        var capture = new BattleFeedbackCapture(memory);
        Assert.Empty(capture.ReadPopups(Menu));
        memory.Byte(Menu + 0x1B5 + 3, 1);
        var popup = Assert.Single(capture.ReadPopups(Menu));
        Assert.Equal(3, popup.Slot); Assert.Equal("1200", popup.Text);
        memory.Byte(Menu + 0x1B5 + 3, 0);
        Assert.Empty(capture.ReadPopups(Menu));
        Assert.Empty(capture.ReadPopups(uint.MaxValue - 4));
    }

    [Fact]
    public void ChangingMessageOwnerDuringCaptureDoesNotReturnStaleText()
    {
        var memory = new NavigationMemory().Word(Menu + 0x234, (uint)Label)
            .Word(Label + 0x278, (uint)Protocol).Word(Protocol + 8, (uint)Getter)
            .Add(Getter, [0x8D, 0x41, 0x28, 0xC3]).Byte(Label + 0x1AD, 1)
            .String(Label + 0x2A0, "Victory!");
        var reads = 0;
        memory.BeforeRead = address => { if (address == Menu + 0x234 && ++reads == 2) memory.Word(address, 0); };
        Assert.Null(new BattleFeedbackCapture(memory).ReadMessage(Menu));
    }
}
