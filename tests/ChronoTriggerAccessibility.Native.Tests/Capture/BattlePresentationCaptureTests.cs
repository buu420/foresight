using System.Buffers.Binary;
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
    public void ReportsTheFreshCandidateListTheCursorWalksBesideTheCommittedTarget()
    {
        // 44B770 resets the eleven cells to FF and 44B9C0 fills them with present, eligible
        // battlers; 44B100/44B160 skip any cell whose low byte is negative.
        var m = Memory().Word(Canvas + 0x1ACD4, 5).Add(Canvas + 0x1A140, Cells(5, 3, 0x80, 0x1FF, 9));
        var targets = new BattlePresentationCapture(m).ReadTargets(Image, Menu)!;
        Assert.Equal([5], targets.Slots);
        Assert.Equal([5, 3, 9], targets.CandidateSlots!);
    }

    [Theory]
    [InlineData(0x19EECu, 0u)] // not selecting a target
    [InlineData(0x19F08u, 0x80u)] // 44B770 found nothing selectable
    [InlineData(0x19E78u, 0xFFu)] // no acting party member
    public void CandidatesAreOnlyReportedUnderTheProvenTargetingGates(uint offset, uint value)
    {
        var m = Memory().Word(Canvas + 0x1ACD4, 5).Add(Canvas + 0x1A140, Cells(5, 3)).Word(Canvas + offset, value);
        Assert.Null(new BattlePresentationCapture(m).ReadTargets(Image, Menu)!.CandidateSlots);
    }

    [Fact]
    public void CandidatesAreNotReportedWhileNoTargetArrowIsDrawn()
    {
        var m = Memory().Word(Canvas + 0x1ACD4, 5).Add(Canvas + 0x1A140, Cells(5, 3)).Byte(Menu + 0x538, 0);
        Assert.Null(new BattlePresentationCapture(m).ReadTargets(Image, Menu)!.CandidateSlots);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("out-of-range")]
    [InlineData("repeated")]
    [InlineData("changing")]
    public void UnprovenCandidatesAreIgnoredWhileTheCommittedTargetIsKept(string kind)
    {
        var m = Memory().Word(Canvas + 0x1ACD4, 5);
        if (kind == "out-of-range") m.Add(Canvas + 0x1A140, Cells(5, 11));
        if (kind == "repeated") m.Add(Canvas + 0x1A140, Cells(5, 3, 5));
        if (kind == "changing")
        {
            m.Add(Canvas + 0x1A140, Cells(5, 3));
            var reads = 0;
            m.BeforeRead = a => { if (a == Canvas + 0x1A140 && ++reads == 2) m.Word(a + 4, 0xFF); };
        }
        var targets = new BattlePresentationCapture(m).ReadTargets(Image, Menu)!;
        Assert.Equal([5], targets.Slots);
        Assert.Null(targets.CandidateSlots);
    }

    [Fact]
    public void OnlyASingleEnemyTargetDuringAnAttackCanCarryTheNativeNameLabel()
    {
        // 44B390 writes the name label from its single-target branch, for slots above 2, when
        // the panel mode (battle data 42F0, canvas 19E90) is the base command panel.
        var m = Memory().Word(Canvas + 0x1ACD4, 5).Word(Canvas + 0x19E90, 0);
        var c = new BattlePresentationCapture(m);
        Assert.Equal(5, c.ReadTargets(Image, Menu)!.NameLabelSlot);
        m.Word(Canvas + 0x19E90, 1);
        Assert.Null(c.ReadTargets(Image, Menu)!.NameLabelSlot);
        m.Word(Canvas + 0x19E90, 0).Word(Canvas + 0x1ACD8, 6);
        Assert.Null(c.ReadTargets(Image, Menu)!.NameLabelSlot);
        m.Word(Canvas + 0x1ACD8, 0xFF).Word(Canvas + 0x1ACD4, 1);
        Assert.Null(c.ReadTargets(Image, Menu)!.NameLabelSlot);
    }

    [Fact]
    public void AnUnreadableOrChangingPanelModeProvesNoNameLabelButKeepsTheTarget()
    {
        var m = Memory().Word(Canvas + 0x1ACD4, 5);
        var c = new BattlePresentationCapture(m);
        Assert.Null(c.ReadTargets(Image, Menu)!.NameLabelSlot);
        m.Word(Canvas + 0x19E90, 0);
        var reads = 0;
        m.BeforeRead = a => { if (a == Canvas + 0x19E90 && ++reads == 2) m.Word(a, 1); };
        var targets = c.ReadTargets(Image, Menu)!;
        Assert.Equal([5], targets.Slots);
        Assert.Null(targets.NameLabelSlot);
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

    /// <summary>Eleven candidate cells, with the FF 44B770 leaves in every cell not given.</summary>
    private static byte[] Cells(params uint[] values)
    {
        var cells = new byte[44];
        for (var i = 0; i < 11; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(cells.AsSpan(i * 4), i < values.Length ? values[i] : 0xFF);
        return cells;
    }
}
