using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldTechCharacterCaptureTests
{
    private const uint Image = 0x400000, Node = 0x1000000, Manager = 0x1100000,
        Icon = 0x1200000, Panel = 0x1300000, Card = 0x1400000;

    // Retail Tech has an empty base manager stack. The character manager is +2E8,
    // its icon vector is +2D4, and the text cards are separate children of +2EC.
    private static NavigationMemory Fixture()
    {
        var m = RenderedNodeTextCaptureTests.Node(new(), Node).Word(Node, Image + 0x3A3C4C)
            .Word(Node + 0x308, 0).Word(Node + 0x2E8, Manager).Word(Node + 0x2E4, 1)
            .Word(Node + 0x2FC, 1)
            .Word(Node + 0x2C8, 0x1500000).Word(Node + 0x2CC, 0x150000C)
            .Word(0x1500000, 0).Word(0x1500004, 2).Word(0x1500008, 4)
            .Word(Node + 0x2D4, 0x1600000).Word(Node + 0x2D8, 0x160000C)
            .Word(0x1600004, Icon).Word(Node + 0x2EC, Panel);
        RenderedNodeTextCaptureTests.Node(m, Manager, Node).Word(Manager, Image + 0x3A5D0C)
            .Byte(Manager + 0x290, 0).Word(Manager + 0x2C4, 1)
            .Word(Manager + 0x294, 0x1700000).Word(0x1700004, 0x1700100)
            .Word(0x1700100, 0x1700200).Word(0x1700200, 0x1700100)
            .Word(0x1700208, 1).Word(0x170020C, 0x1700300)
            .Word(0x1700300, Image + 0x3AC3F4).Word(0x1700314, Icon);
        RenderedNodeTextCaptureTests.Node(m, Icon, Node).Word(Icon, Image + 0x3A4364);
        RenderedNodeTextCaptureTests.Node(m, Panel, Node)
            .Word(Panel + 0x160, 0x1800000).Word(Panel + 0x164, 0x180000C).Word(0x1800004, Card);
        RenderedNodeTextCaptureTests.Text(m, Card, "Lucca", Panel);
        m.Word(Image + 0x41C3D8, 0x1900000).Word(0x1900000, 0x1901000).Word(0x1900004, 0x1901100)
            .Word(0x1901000 + 0x23 * 4, 0x1902000).Word(0x1902000, 0x1910000)
            .Word(0x1902004, 0x1910000 + 0xB2 * 24)
            .String(0x1910000 + 0x22 * 24, "Techs")
            .String(0x1910000 + 0x55 * 24, "Dual Techs")
            .String(0x1910000 + 0xB1 * 24, "Select a tech");
        return m;
    }

    [Fact]
    public void ReadsTheSelectedCharacterCardBehindAnIconOnlyButton()
    {
        var result = new FieldTechDetailsCapture(Fixture()).Capture(Image, Node);
        Assert.NotNull(result);
        Assert.Equal("Techs", result.Title);
        Assert.Equal("Lucca. Dual Techs. Select a tech", result.Text);
    }

    [Theory]
    [InlineData(Node + 0x2E4, 0)] // committed character does not match focus
    [InlineData(Node + 0x2FC, 3)] // unknown category
    [InlineData(0x1600004, 0xDEADBEEF)] // icon vector disagrees with the focus map
    [InlineData(Panel + 0x164, 0x1800008)] // roster and card counts disagree
    [InlineData(Card + 0x16C, Node)] // card moved out of its character panel
    [InlineData(Node + 0x308, 0xDEADBEEF)] // another active selection must not reuse old character focus
    public void RefusesAnUncorrelatedCharacterSelection(uint address, uint value)
        => Assert.Null(new FieldTechDetailsCapture(Fixture().Word(address, value)).Capture(Image, Node));

    [Theory]
    [InlineData(Manager + 0x290, 1)]
    [InlineData(Icon + 0x1AD, 0)]
    [InlineData(Card + 0x1AD, 0)]
    public void RefusesDisabledOrHiddenCharacterSelections(uint address, byte value)
        => Assert.Null(new FieldTechDetailsCapture(Fixture().Byte(address, value)).Capture(Image, Node));

    [Fact]
    public void RechecksTheRosterWhenReadingItsText()
    {
        var m = Fixture();
        m.BeforeRead = p => { if (p == Card + 0x2A0) m.Word(0x1500004, 4); };
        Assert.Null(new FieldTechDetailsCapture(m).Capture(Image, Node));
    }
}
