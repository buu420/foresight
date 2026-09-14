using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class RenderedNodeTextCaptureTests
{
    private const uint Root = 0x1000000, Label = 0x1001000, Hidden = 0x1002000, Children = 0x2000000;
    internal static NavigationMemory Node(NavigationMemory m, uint p, uint parent = 0) =>
        m.Word(p + 0x16C, parent).Byte(p + 0x1AD, 1)
            .Word(p + 0x160, 0).Word(p + 0x164, 0);
    internal static NavigationMemory Text(NavigationMemory m, uint p, string text, uint parent = 0) =>
        Node(m, p, parent).Word(p + 0x278, 0x3000000).Word(0x3000008, 0x3001000)
            .Add(0x3001000, [0x8D, 0x41, 0x28, 0xC3]).String(p + 0x2A0, text);
    private static NavigationMemory Fixture()
    {
        var m = Node(new(), Root).Word(Root + 0x160, Children).Word(Root + 0x164, Children + 8)
            .Word(Children, Label).Word(Children + 4, Hidden);
        Text(m, Label, "Potion", Root);
        Text(m, Hidden, "Unselected item", Root).Byte(Hidden + 0x1AD, 0);
        return m;
    }
    [Fact] public void ReadsVisibleLabelsOnlyWithinTheSpecifiedPanel()
    {
        var m = Fixture();
        Text(m, 0x1003000, "Elsewhere");
        Assert.Equal(["Potion"], new RenderedNodeTextCapture(m).Read(Root));
    }
    [Fact] public void RejectsAReparentedChildRatherThanReadingAnUnrelatedScreen()
    {
        var m = Fixture().Word(Label + 0x16C, 0xDEADBEEF);
        Assert.Null(new RenderedNodeTextCapture(m).Read(Root));
    }
    [Fact] public void RejectsChildListMutationDuringCapture()
    {
        var m = Fixture();
        m.BeforeRead = address => { if (address == Label + 0x2A0) m.Word(Root + 0x164, Children); };
        Assert.Null(new RenderedNodeTextCapture(m).Read(Root));
    }
    [Fact] public void DoesNotInterpretAnArbitraryNodePayloadAsText()
    {
        var m = Fixture().Add(0x3001000, [0xC3, 0, 0, 0]);
        Assert.Empty(new RenderedNodeTextCapture(m).Read(Root)!);
    }
    [Fact] public void RejectsCyclesAndUnboundedVectors()
    {
        var m = Fixture().Word(Label + 0x160, Children).Word(Label + 0x164, Children + 4);
        Assert.Null(new RenderedNodeTextCapture(m).Read(Root));
        m.Word(Root + 0x164, Children + 4096);
        Assert.Null(new RenderedNodeTextCapture(m).Read(Root));
    }
}
