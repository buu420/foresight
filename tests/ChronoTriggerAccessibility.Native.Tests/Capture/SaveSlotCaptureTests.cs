using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class SaveSlotCaptureTests
{
    private const uint Image = 0x400000, Node = 0x1000000, Manager = 0x2000000,
        Cards = 0x2100000, Card = 0x2200000, Preview = 0x2300000, Records = 0x2400000;
    private static NavigationMemory Fixture()
    {
        var m = RenderedNodeTextCaptureTests.Node(new(), Node)
            .Word(Node, Image + SaveSlotCapture.NodeVtableRva).Word(Node + 0x2CC, 0)
            .Byte(Node + 0x2EC, 0).Word(Node + 0x304, Manager)
            .Word(Manager, Image + 0x3A5D0C).Word(Manager + 0x2C4, 1)
            .Word(Node + 0x2F8, Cards).Word(Node + 0x2FC, Cards + 80)
            .Word(Cards + 4, Card).Word(Node + 0x2E8, Preview)
            .Word(Node + 0x2D0, Records).Word(Node + 0x2D4, Records + 20 * 0x940)
            .Byte(Records + 0x940, 1);
        RenderedNodeTextCaptureTests.Text(m, Card, "Truce Canyon", Node);
        RenderedNodeTextCaptureTests.Text(m, Preview, "Crono LV 1 HP 23/70 MP 8/8. 00:35. 509 G", Node);
        m.Word(Image + 0x41C3D8, 0x5000000).Word(0x5000000, 0x5001000)
            .Word(0x5000004, 0x5001000 + 0x42 * 4).Word(0x5001000 + 0x23 * 4, 0x5002000)
            .Word(0x5002000, 0x5010000).Word(0x5002004, 0x5010000 + 0x40 * 24)
            .String(0x5010000 + 0x26 * 24, "Save");
        return m;
    }
    [Fact] public void ReadsOnlyTheSelectedFileAndItsRenderedPreview()
    {
        var c = new SaveSlotCapture(Fixture());
        var result = c.Capture(Image, Node)!;
        Assert.Equal("Save", result.Title);
        Assert.Equal("File 2 of 20. Truce Canyon. Crono LV 1 HP 23/70 MP 8/8. 00:35. 509 G", result.Text);
    }
    [Fact] public void ConfirmationOwnsSpeechWhileOpen()
    {
        var m = Fixture().Byte(Node + 0x2EC, 1);
        Assert.Null(new SaveSlotCapture(m).Capture(Image, Node));
    }
    [Fact] public void EmptySaveSlotKeepsTheGamesEmptyLabelAndDoesNotReadOldPreview()
    {
        var m = Fixture().Byte(Records + 0x940, 0);
        RenderedNodeTextCaptureTests.Text(m, Card, "No data", Node);
        Assert.Equal("File 2 of 20. No data", new SaveSlotCapture(m).Capture(Image, Node)!.Text);
    }
    [Fact] public void RejectsSelectionAndRecordBoundsThatDisagree()
    {
        var m = Fixture().Word(Node + 0x2D4, Records + 0x940);
        Assert.Null(new SaveSlotCapture(m).Capture(Image, Node));
        m = Fixture().Word(Manager + 0x2C4, 20);
        Assert.Null(new SaveSlotCapture(m).Capture(Image, Node));
    }
    [Fact] public void RejectsASelectionChangeDuringTextCapture()
    {
        var m = Fixture();
        m.BeforeRead = p => { if (p == Preview + 0x2A0) m.Word(Manager + 0x2C4, 2); };
        Assert.Null(new SaveSlotCapture(m).Capture(Image, Node));
    }
    [Fact] public void RejectsAPreviewDetachedDuringCapture()
    {
        var m = Fixture();
        m.BeforeRead = p => { if (p == Preview + 0x2A0) m.Word(Preview + 0x16C, 0); };
        Assert.Null(new SaveSlotCapture(m).Capture(Image, Node));
    }
}
