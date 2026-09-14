using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Battle;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Battle;

public sealed class BattleRuntimeTests
{
    private static BattleFrame Frame(int marleHp = 50, string? focus = "Crono. Attack") => new(
        [new(0, "Crono", 43, 70, 8, 8, ""), new(1, "Marle", marleHp, 80, 7, 12, "")],
        focus is null ? null : "command:0:0", focus, new Dictionary<int,string> { [0] = "Crono", [1] = "Marle", [3] = "Blue Imp 1", [4] = "Blue Imp 2" });

    [Fact]
    public void RepeatedDisplayedMessageSpeaksOnceUntilHiddenButIdenticalNewPresentationSpeaksAgain()
    {
        var events = new List<AccessibilityEvent>(); var runtime = Runtime(events);
        runtime.Observe(100, Frame());
        runtime.Message(100, "Gained 10 EXP."); runtime.Message(100, "Gained 10 EXP.");
        Assert.Single(events.OfType<BattleFeedbackPresented>());
        runtime.Message(100, null); runtime.Message(100, "Gained 10 EXP.");
        Assert.Equal(2, events.OfType<BattleFeedbackPresented>().Count());
    }

    [Fact]
    public void StatusChangesSurviveAnUnreadableStatusFrameAndAreNotRepeatedByIconAnimation()
    {
        var events = new List<AccessibilityEvent>(); var runtime = Runtime(events);
        BattleFrame Status(string? text) => Frame() with { Party = [Frame().Party[0] with { Status = text }] };
        runtime.Observe(100, Status("")); runtime.Observe(100, Status("Poison"));
        runtime.Observe(100, Status(null)); runtime.Observe(100, Status("Poison"));
        Assert.Equal("Crono. Poison.", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
        runtime.Observe(100, Status(""));
        Assert.Equal("Crono. Poison ended.", events.OfType<BattleFeedbackPresented>().Last().Text);
    }

    [Fact]
    public void NumberSelectionPersistsAcrossTurnsAndReadsUpdatedMemberHpAndMp()
    {
        var events = new List<AccessibilityEvent>();
        var runtime = Runtime(events);
        runtime.Observe(100, Frame());
        runtime.Handle(BattleCommand.SelectSecond);
        runtime.Handle(BattleCommand.ReadHp);
        runtime.Handle(BattleCommand.ReadMp);
        Assert.Equal(["Marle.", "Marle. HP 50 of 80.", "Marle. MP 7 of 12."], Inspections(events));
        runtime.Observe(100, Frame(32, "Marle. Tech"));
        runtime.Handle(BattleCommand.ReadHp);
        Assert.Equal("Marle. HP 32 of 80.", Inspections(events)[^1]);
        runtime.Handle(BattleCommand.Repeat);
        Assert.Equal("Marle. Tech", Inspections(events)[^1]);
    }

    [Fact]
    public void MissingSlotDoesNotSilentlySelectSomeoneElseAndHiddenFocusDoesNotRepeatOldAttack()
    {
        var events = new List<AccessibilityEvent>(); var runtime = Runtime(events);
        runtime.Observe(100, Frame()); runtime.Handle(BattleCommand.SelectSecond);
        runtime.Handle(BattleCommand.SelectThird); runtime.Handle(BattleCommand.ReadMp);
        Assert.Contains("Party slot 3 is empty.", Inspections(events));
        Assert.Equal("Marle. MP 7 of 12.", Inspections(events)[^1]);
        runtime.Observe(100, Frame(focus: null)); runtime.Handle(BattleCommand.Repeat);
        Assert.Equal("No battle command is currently selected.", Inspections(events)[^1]);
    }

    [Fact]
    public void TeardownAndReentryReleaseNavigationAndDiscardInspectionAndPopupState()
    {
        var events = new List<AccessibilityEvent>(); var active = new List<bool>();
        var runtime = Runtime(events, active.Add);
        runtime.Observe(100, Frame()); runtime.Handle(BattleCommand.SelectSecond);
        runtime.MarkPopup(100, 3, 3); runtime.End(99); Assert.True(runtime.IsActive);
        runtime.End(100); Assert.False(runtime.IsActive);
        var count = events.Count; runtime.Handle(BattleCommand.ReadHp); Assert.Equal(count, events.Count);
        runtime.Observe(101, Frame()); runtime.Handle(BattleCommand.ReadHp);
        Assert.Equal("Crono. HP 43 of 70.", Inspections(events)[^1]);
        runtime.RenderPopups(101, [new(3, "100", 255, 255, 255)]);
        Assert.Empty(events.OfType<BattleFeedbackPresented>());
        Assert.Equal([true, false, true], active);
    }

    [Fact]
    public void VisiblePopupUsesDisplayedNumberAndRetainedRecipientOncePerWriter()
    {
        var events = new List<AccessibilityEvent>(); var runtime = Runtime(events);
        runtime.Observe(100, Frame());
        runtime.MarkPopup(100, 3, 3);
        var popup = new BattlePopupSnapshot(3, "1200", 255, 255, 255);
        runtime.RenderPopups(100, []); Assert.Empty(events.OfType<BattleFeedbackPresented>());
        runtime.Observe(100, Frame() with { BattlerNames = new Dictionary<int,string>() });
        runtime.RenderPopups(100, [popup]); runtime.RenderPopups(100, [popup]);
        Assert.Equal("Blue Imp 1 takes 1200 damage.", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
        runtime.MarkPopup(100, 3, 3); runtime.RenderPopups(100, [popup]);
        Assert.Equal(2, events.OfType<BattleFeedbackPresented>().Count());
    }

    [Theory]
    [InlineData(1, "Crono recovers 8.")]
    [InlineData(2, "Crono recovers 8.")]
    [InlineData(4, "Crono takes 8 damage.")]
    [InlineData(5, "Crono. MISS!")]
    public void PopupKindUsesAuditedPresentationSemantics(int kind, string expected)
    {
        var events = new List<AccessibilityEvent>(); var runtime = Runtime(events);
        runtime.Observe(100, Frame()); runtime.MarkPopup(100, 0, kind);
        runtime.RenderPopups(100, [new(0, kind == 5 ? "MISS!" : "8", 0,230,0)]);
        Assert.Equal(expected, Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
    }

    [Fact]
    public void UnreadableFrameAndBackgroundCannotSpeakOldValuesOrQueuedHits()
    {
        var events = new List<AccessibilityEvent>(); var foreground = true;
        var runtime = new BattleRuntime(events.Add, new BattleKeyboard(_ => false, () => foreground), () => foreground, _ => { });
        runtime.Observe(100, Frame()); runtime.Observe(100, null); runtime.Handle(BattleCommand.ReadHp);
        Assert.Equal("Battle information is unavailable.", Inspections(events)[^1]);
        foreground = false; var count = events.Count;
        runtime.Observe(100, Frame()); runtime.Handle(BattleCommand.ReadHp);
        runtime.MarkPopup(100, 0, 3); runtime.RenderPopups(100, [new(0,"8",255,255,255)]);
        runtime.Message(100, "A message"); Assert.Equal(count, events.Count);
        foreground = true; runtime.Observe(100, Frame());
        runtime.RenderPopups(100, [new(0,"8",255,255,255)]);
        Assert.Empty(events.OfType<BattleFeedbackPresented>());
    }

    [Fact]
    public void LosingForegroundBeforeTheNextDrawDiscardsAnOldPendingPopup()
    {
        var events = new List<AccessibilityEvent>(); var foreground = true;
        var runtime = new BattleRuntime(events.Add, new BattleKeyboard(_ => false, () => foreground), () => foreground, _ => { });
        runtime.Observe(100, Frame()); runtime.MarkPopup(100, 0, 3);
        foreground = false; runtime.Observe(100, Frame());
        foreground = true; runtime.Observe(100, Frame());
        runtime.RenderPopups(100, [new(0,"8",255,255,255)]);
        Assert.Empty(events.OfType<BattleFeedbackPresented>());
    }

    private static BattleRuntime Runtime(List<AccessibilityEvent> events, Action<bool>? active = null) =>
        new(events.Add, new BattleKeyboard(_ => false, () => true), () => true, active ?? (_ => { }));
    private static string[] Inspections(List<AccessibilityEvent> events) => events.OfType<BattleInspectionRequested>().Select(x => x.Text).ToArray();
}
