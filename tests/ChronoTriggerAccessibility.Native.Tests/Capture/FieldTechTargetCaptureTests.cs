using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldTechTargetCaptureTests
{
    private const uint Image = 0x400000, Node = 0x1000000, Window = 0x1100000, Sheet = 0x1200000,
        Manager = 0x1300000, Map = 0x1400000, Button = 0x1500000, Card = 0x1600000, Portrait = 0x1700000;

    // 1CEB40 releases row manager +308 and builds the target window at +34C. Its Manager and
    // buttons are direct window children; 1CF320 fills sheet +350 with one card per member.
    private static NavigationMemory Fixture(int controls = 3, uint key = 1)
    {
        var m = RenderedNodeTextCaptureTests.Node(new(), Node).Word(Node, Image + 0x3A3C4C)
            .Word(Node + 0x308, 0).Word(Node + 0x34C, Window).Word(Node + 0x350, Sheet);
        var windowChildren = new List<uint> { Sheet, Portrait };
        for (uint i = 0; i < controls; i++) windowChildren.Add(Button + i * 0x10000);
        windowChildren.Add(Manager);
        RenderedNodeTextCaptureTests.Node(m, Window, Node).Word(Window + 0x160, 0x1800000)
            .Word(Window + 0x164, 0x1800000 + (uint)windowChildren.Count * 4);
        for (var i = 0; i < windowChildren.Count; i++) m.Word(0x1800000 + (uint)i * 4, windowChildren[i]);
        RenderedNodeTextCaptureTests.Node(m, Portrait, Window).Word(Portrait, Image + 0x3A0000);
        RenderedNodeTextCaptureTests.Node(m, Sheet, Window).Word(Sheet, Image + 0x3A0000)
            .Word(Sheet + 0x160, 0x1900000).Word(Sheet + 0x164, 0x190000C);
        string[] names = ["Crono", "Marle", "Lucca"];
        for (uint i = 0; i < 3; i++)
        {
            m.Word(0x1900000 + i * 4, Card + i * 0x10000);
            RenderedNodeTextCaptureTests.Text(m, Card + i * 0x10000, names[i], Sheet);
        }

        RenderedNodeTextCaptureTests.Node(m, Manager, Window).Word(Manager, Image + 0x3A5D0C)
            .Byte(Manager + 0x290, 0).Word(Manager + 0x2C4, key).Word(Manager + 0x294, Map)
            .Word(Map + 4, 0x1A00000).Word(Map + 8, (uint)controls).Word(0x1A00000, 0x1A00100);
        for (uint i = 0; i < controls; i++)
        {
            var entry = 0x1A00100 + i * 0x100;
            var state = 0x1B00000 + i * 0x100;
            var button = Button + i * 0x10000;
            m.Word(entry, i + 1 == controls ? 0x1A00000 : entry + 0x100).Word(entry + 8, i)
                .Word(entry + 12, state).Word(state, Image + 0x3AC3F4).Word(state + 0x14, button);
            RenderedNodeTextCaptureTests.Node(m, button, Window).Word(button, Image + 0x3A1000);
        }
        m.Word(Image + 0x41C3D8, 0x1C00000).Word(0x1C00000, 0x1C01000).Word(0x1C00004, 0x1C01100)
            .Word(0x1C01000 + 0x23 * 4, 0x1C02000).Word(0x1C02000, 0x1C10000)
            .Word(0x1C02004, 0x1C10000 + 0xB2 * 24)
            .String(0x1C10000 + 0x22 * 24, "Techs");
        return m;
    }

    [Fact]
    public void ReadsTheFocusedTargetCard()
    {
        var result = new FieldTechDetailsCapture(Fixture()).Capture(Image, Node);
        Assert.NotNull(result);
        Assert.Equal("Techs", result.Title);
        Assert.Equal("Target, 2 of 3. Marle.", result.Text);
    }

    [Fact]
    public void ReadsEveryCardForAnAllPartyTech()
    {
        var result = new FieldTechDetailsCapture(Fixture(controls: 1, key: 0)).Capture(Image, Node);
        Assert.NotNull(result);
        Assert.Equal("All party members. Crono. Marle. Lucca.", result.Text);
    }

    [Fact]
    public void RecognizesTheTargetManagerForRefresh()
    {
        var capture = new FieldTechDetailsCapture(Fixture());
        Assert.True(capture.OwnsTargetManager(Image, Node, Manager));
        Assert.False(capture.OwnsTargetManager(Image, Node, Button));
    }

    [Theory]
    [InlineData(Node + 0x308, 0xDEADBEEF)] // row list still owns the page
    [InlineData(Manager + 0x2C4, 3)] // focus outside the targets
    [InlineData(Map + 8, 2)] // control count disagrees with the cards
    [InlineData(Card + 0x1016C, Window)] // focused card moved out of the sheet
    [InlineData(Sheet + 0x16C, Node)] // sheet is not the window's
    public void RefusesAnUncorrelatedTarget(uint address, uint value)
        => Assert.Null(new FieldTechDetailsCapture(Fixture().Word(address, value)).Capture(Image, Node));

    [Theory]
    [InlineData(Manager + 0x290, 1)]
    [InlineData(Window + 0x1AD, 0)]
    [InlineData(Card + 0x101AD, 0)]
    public void RefusesDisabledOrHiddenTargets(uint address, byte value)
        => Assert.Null(new FieldTechDetailsCapture(Fixture().Byte(address, value)).Capture(Image, Node));

    [Fact]
    public void RechecksFocusAfterReadingTheCard()
    {
        var m = Fixture();
        m.BeforeRead = p => { if (p == Card + 0x10000 + 0x2A0) m.Word(Manager + 0x2C4, 2); };
        Assert.Null(new FieldTechDetailsCapture(m).Capture(Image, Node));
    }

    [Fact]
    public void FallsBackToCharacterSelectionWhenNoTargetWindowIsOpen()
        => Assert.Null(new FieldTechDetailsCapture(Fixture().Word(Node + 0x34C, 0)).Capture(Image, Node));
}
