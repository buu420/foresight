using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldTechDetailsCaptureTests
{
    private const uint Image = 0x400000, Node = 0x1000000, Manager = 0x2000000, Rows = 0x2100000,
        Control = 0x2200000, Description = 0x2300000, Components = 0x2400000, Costs = 0x2500000;
    private static NavigationMemory Fixture()
    {
        var m = RenderedNodeTextCaptureTests.Node(new(), Node).Word(Node, Image + 0x3A3C4C)
            .Word(Node + 0x308, Manager).Word(Manager, Image + 0x3A5D0C).Byte(Manager + 0x290, 0)
            .Word(Manager + 0x2C4, 1).Word(Node + 0x2FC, 0).Word(Node + 0x300, 1)
            .Word(Node + 0x318, Rows).Word(Node + 0x31C, Rows + 24).Add(Rows + 12, new byte[12])
            .Word(Manager + 0x294, 0x3000000).Word(0x3000004, 0x3010000)
            .Word(0x3010000, 0x3020000).Word(0x3020008, 1).Word(0x302000C, 0x3030000)
            .Word(0x3030000, Image + 0x3AC3F4).Word(0x3030014, Control)
            .Word(Node + 0x33C, Description).Word(Node + 0x340, Components).Word(Node + 0x344, Costs);
        RenderedNodeTextCaptureTests.Text(m, Control, "Cyclone", Node).Word(Control, Image + 0x3A4364);
        RenderedNodeTextCaptureTests.Text(m, Description, "Strike nearby enemies.", Node);
        RenderedNodeTextCaptureTests.Text(m, Components, "", Node);
        RenderedNodeTextCaptureTests.Text(m, Costs, "MP 2", Node);
        m.Word(Image + 0x41C3D8, 0x5000000).Word(0x5000000, 0x5001000).Word(0x5000004, 0x5001100)
            .Word(0x5001000 + 0x23 * 4, 0x5002000).Word(0x5002000, 0x5010000)
            .Word(0x5002004, 0x5010000 + 0x40 * 24).String(0x5010000 + 0x22 * 24, "Techs");
        return m;
    }
    [Fact] public void ReadsTheSelectedRowAndSeparatelyRenderedHelpAndMp()
    {
        Assert.Equal("Cyclone. Strike nearby enemies. MP 2", new FieldTechDetailsCapture(Fixture()).Capture(Image, Node)!.Text);
    }
    [Fact] public void DisabledRowManagerCannotOverrideACharacterOrOtherModalSelection()
    {
        var m = Fixture().Byte(Manager + 0x290, 1);
        Assert.Null(new FieldTechDetailsCapture(m).Capture(Image, Node));
    }
    [Fact] public void RejectsStaleRowFocusAndUnrelatedDetailPanels()
    {
        var m = Fixture().Word(Manager + 0x2C4, 0);
        Assert.Null(new FieldTechDetailsCapture(m).Capture(Image, Node));
        m = Fixture().Word(Description + 0x16C, 0xDEADBEEF);
        Assert.Null(new FieldTechDetailsCapture(m).Capture(Image, Node));
    }
    [Fact] public void DoesNotDeriveHiddenTechNamesFromTheRecords()
    {
        var m = Fixture(); RenderedNodeTextCaptureTests.Text(m, Control, "????", Node);
        Assert.StartsWith("????.", new FieldTechDetailsCapture(m).Capture(Image, Node)!.Text);
    }
    [Fact] public void RechecksTheRowRecordEvenWhenTheCursorDoesNotMove()
    {
        var m = Fixture();
        m.BeforeRead = p => { if (p == Costs + 0x2A0) m.Byte(Rows + 13, 9); };
        Assert.Null(new FieldTechDetailsCapture(m).Capture(Image, Node));
    }
}
