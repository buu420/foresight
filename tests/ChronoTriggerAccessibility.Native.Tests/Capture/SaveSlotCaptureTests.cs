using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class SaveSlotCaptureTests
{
    private const uint Image = 0x400000, Node = 0x1000000, Manager = 0x2000000,
        Cards = 0x2100000, Card = 0x2200000, Preview = 0x2300000, Records = 0x2400000,
        Layer = 0x2500000, Bar = 0x2600000, StatusLines = 0x2700000, StatusText = 0x2800000;
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
        // 0x218A20 adds the StatusBar to the node's layer and 0x22F160 stores its complete lines.
        RenderedNodeTextCaptureTests.Node(m, Layer, Node);
        RenderedNodeTextCaptureTests.Node(m, Bar, Layer)
            .Word(Node + SaveSlotCapture.StatusBarOffset, Bar)
            .Word(Bar, Image + TopMenuCaptureScope.StatusBarVtableRva);
        StatusBarLines(m, "Please select a file.");
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
        Assert.Equal("Save. Please select a file.", result.Title);
        Assert.Equal("File 2 of 20. Truce Canyon. Crono LV 1 HP 23/70 MP 8/8. 00:35. 509 G", result.Text);
    }
    [Theory]
    [InlineData("You cannot save at this time.")]
    [InlineData("You can save your current progress.")]
    public void TitleCarriesTheStatusBarInstructionTheNodeShows(string instruction)
    {
        var m = StatusBarLines(Fixture(), instruction);
        Assert.Equal($"Save. {instruction}", new SaveSlotCapture(m).Capture(Image, Node)!.Title);
    }
    [Fact] public void InstructionKeepsEachOfTheMessagesOwnLines()
    {
        var m = StatusBarLines(Fixture(), "Please select", "a file.");
        Assert.Equal("Save. Please select a file.", new SaveSlotCapture(m).Capture(Image, Node)!.Title);
    }
    [Fact] public void MissingHiddenOrForeignStatusBarReportsNoSnapshot()
    {
        Assert.Null(new SaveSlotCapture(Fixture().Word(Node + SaveSlotCapture.StatusBarOffset, 0)).Capture(Image, Node));
        Assert.Null(new SaveSlotCapture(Fixture().Byte(Bar + 0x1AD, 0)).Capture(Image, Node));
        Assert.Null(new SaveSlotCapture(Fixture().Word(Bar, Image + 0x3A5D0C)).Capture(Image, Node));
        Assert.Null(new SaveSlotCapture(Fixture().Word(Bar + 0x16C, 0)).Capture(Image, Node));
        Assert.Null(new SaveSlotCapture(StatusBarLines(Fixture())).Capture(Image, Node));
    }
    [Fact] public void RejectsAnInstructionReplacedDuringCapture()
    {
        var m = Fixture();
        var lineReads = 0;
        // The second read of the instruction sees a different line than the first.
        m.BeforeRead = p => { if (p == StatusLines && ++lineReads == 2) StatusBarLines(m, "Please select a file to load."); };
        Assert.Null(new SaveSlotCapture(m).Capture(Image, Node));
        Assert.Equal(2, lineReads);
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

    /// <summary>Writes the StatusBar's line vector: one heap-backed MSVC wide string per line.</summary>
    private static NavigationMemory StatusBarLines(NavigationMemory m, params string[] lines)
    {
        m.Word(Bar + 0x2D4, StatusLines).Word(Bar + 0x2D8, StatusLines + (uint)(lines.Length * 24));
        for (var index = 0; index < lines.Length; index++)
        {
            var layout = new byte[24];
            var data = StatusText + (uint)(index * 0x1000);
            BinaryPrimitives.WriteUInt32LittleEndian(layout, data);
            BinaryPrimitives.WriteInt32LittleEndian(layout.AsSpan(0x10), lines[index].Length);
            BinaryPrimitives.WriteInt32LittleEndian(layout.AsSpan(0x14), Math.Max(8, lines[index].Length));
            m.Add(StatusLines + (uint)(index * 24), layout).Add(data, Encoding.Unicode.GetBytes(lines[index]));
        }
        return m;
    }
}
