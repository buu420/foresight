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
    [Theory]
    [InlineData(0u, "HP MP Cost : 2")]
    [InlineData(1u, "HP PM 20/30 MP Cost : 2")]
    [InlineData(2u, "HP PM 20/30 MP Cost : 2")]
    public void TechCostsOmitNamedBackgroundStatRowsButKeepParticipantsAndRelevantMp(uint category, string expectedCosts)
    {
        var m = Fixture();
        m.Word(Node + 0x2FC, category).Word(Node + 0x318 + category * 12, Rows)
            .Word(Node + 0x31C + category * 12, Rows + 24);
        RenderedNodeTextCaptureTests.Node(m, Costs, Node)
            .Word(Costs + 0x278, 0)
            .Word(Costs + 0x160, 0x2600000).Word(Costs + 0x164, 0x2600014);
        // Retail 23A140 names its stat-row subtrees lv/hp/mp. These are
        // native identifiers, independent of translated labels or custom names.
        var texts = new[] { "HP", "Niveau 20", "PV 175/221", "PM 20/30", "MP Cost : 2" };
        var names = new[] { "", "lv", "hp", "mp", "" };
        for (var i = 0; i < texts.Length; i++)
        {
            var child = 0x2700000u + (uint)i * 0x1000;
            m.Word(0x2600000u + (uint)i * 4, child);
            RenderedNodeTextCaptureTests.Text(m, child, texts[i], Costs).String(child + 0x178, names[i]);
        }
        var result = new FieldTechDetailsCapture(m).Capture(Image, Node);
        Assert.NotNull(result);
        // A character actually renamed "HP" remains audible; text filtering
        // would wrongly remove it. Cost and selected Tech information survive.
        Assert.Equal("Cyclone. Strike nearby enemies. " + expectedCosts, result.Text);
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
