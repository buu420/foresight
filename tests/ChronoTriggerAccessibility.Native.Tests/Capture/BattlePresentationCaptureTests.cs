using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class BattlePresentationCaptureTests
{
    private const nuint Image = 0x400000, Canvas = 0x1000000, Menu = 0x2000000, Script = 0x3000000;
    private static NavigationMemory Memory() => new NavigationMemory()
        .Word(Image + 0x41B4C4, (uint)Canvas).Word(Image + 0x41B4BC, (uint)Script)
        .Word(Menu, (uint)(Image + BattleCapture.ClassicBattleMenuVtableRva))
        .Word(Canvas + 0x19EEC, 1).Word(Canvas + 0x19E78, 0).Word(Canvas + 0x19F08, 0)
        .Byte(Menu + 0x538, 1).Byte(Menu + 0x54C, 0).Byte(Menu + 0x560, 0).Byte(Menu + 0x574, 0)
        .Add(Canvas + 0x1ACD4, Enumerable.Repeat((byte)255, 44).ToArray());

    [Fact]
    public void ReadsCommittedGroupRegardlessOfAlternatingArrowFramesAndIgnoresCandidateList()
    {
        var m = Memory().Word(Canvas + 0x1ACD4, 3).Word(Canvas + 0x1ACD8, 4)
            .Word(Canvas + 0x1ACDC, 5).Word(Canvas + 0x1ACE0, 6).Word(Canvas + 0x1A140, 10);
        var c = new BattlePresentationCapture(m);
        Assert.Equal([3,4,5,6], c.ReadTargets(Image, Menu)!.Slots);
        m.Byte(Menu + 0x538, 0).Byte(Menu + 0x54C, 1);
        Assert.Equal([3,4,5,6], c.ReadTargets(Image, Menu)!.Slots);
        m.Byte(Menu + 0x54C, 0);
        Assert.Empty(c.ReadTargets(Image, Menu)!.Slots);
    }

    [Fact]
    public void GroupKeepsLaterRecipientsAcrossAnEmptyCellButSingleTargetIgnoresStaleGroupTail()
    {
        var m = Memory().Word(Canvas + 0x1ACD4, 3).Word(Canvas + 0x1ACD8, 4)
            .Word(Canvas + 0x1ACE0, 6).Word(Canvas + 0x1ACD4 + 8 * 4, 9);
        var c = new BattlePresentationCapture(m);
        Assert.Equal([3,4,6], c.ReadTargets(Image, Menu)!.Slots);
        m.Word(Canvas + 0x1ACD8, 255);
        Assert.Equal([3], c.ReadTargets(Image, Menu)!.Slots);
    }

    [Fact]
    public void AppliedStatusUsesTheGamesLoadedLocalizedName()
    {
        const uint manager = 0x5000000, banks = 0x5001000, bank = 0x5002000, lines = 0x5003000;
        var m = Memory().Word(Canvas + 0x19FA0, 1).Byte(Script + 0x28441, 14)
            .Word(Canvas + 0x1AA30, 14).Word(Image + 0x41C3D8, manager)
            .Word(manager, banks).Word(manager + 4, banks + 4).Word(banks, bank)
            .Word(bank, lines).Word(bank + 4, lines + 24 * 51).String(lines + 8 * 24, "Toxin");
        Assert.Equal("Toxin", new BattlePresentationCapture(m).ReadStatus(Image, Menu, 0));
    }

    [Fact]
    public void SupportsPartyTargetsAndRejectsStaleSelectionOrOwner()
    {
        var m = Memory().Word(Canvas + 0x1ACD4, 1);
        var c = new BattlePresentationCapture(m);
        Assert.Equal([1], c.ReadTargets(Image, Menu)!.Slots);
        m.Word(Canvas + 0x19EEC, 0);
        Assert.False(c.ReadTargets(Image, Menu)!.IsTargeting);
        m.Word(Canvas + 0x19EEC, 1).Word(Canvas + 0x1ACD4, 11);
        Assert.Null(c.ReadTargets(Image, Menu));
        m.Word(Menu, 0);
        Assert.Null(c.ReadTargets(Image, Menu));
    }

    [Theory]
    [InlineData(2,"Stop")][InlineData(4,"Confuse")][InlineData(5,"Sleep")]
    [InlineData(8,"Lock")][InlineData(9,"Blind")][InlineData(13,"Slow")][InlineData(14,"Poison")]
    [InlineData(3,"Berserk")][InlineData(6,"Barrier")][InlineData(7,"Shield")][InlineData(12,"Haste")]
    public void ReadsAppliedVisualStatusAndDoesNotReadUnderlyingHiddenStatusBits(int code, string expected)
    {
        var m = Memory().Word(Canvas + 0x19FA0, 1).Byte(Script + 0x28441, (byte)code)
            .Word(Canvas + 0x1AA30, (uint)code);
        Assert.Equal(expected, new BattlePresentationCapture(m).ReadStatus(Image, Menu, 0));
        m.Word(Canvas + 0x1AA30, 255); // queued state has not reached its visual effect
        Assert.Null(new BattlePresentationCapture(m).ReadStatus(Image, Menu, 0));
    }

    [Fact]
    public void NoStatusIsDistinctFromUnreadableOrChangingState()
    {
        var m = Memory().Word(Canvas + 0x19FA0, 1).Byte(Script + 0x28441, 255)
            .Word(Canvas + 0x1AA30, 255);
        var c = new BattlePresentationCapture(m);
        Assert.Equal("", c.ReadStatus(Image, Menu, 0));
        Assert.Null(c.ReadStatus(Image, Menu, 3));
        var reads = 0;
        m.BeforeRead = a => { if (a == Image + 0x41B4C4 && ++reads == 2) m.Word(a, 0); };
        Assert.Null(c.ReadStatus(Image, Menu, 0));
    }
}
