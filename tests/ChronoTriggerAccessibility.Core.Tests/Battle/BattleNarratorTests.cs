using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Core.State;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Battle;

public sealed class BattleNarratorTests
{
    [Fact]
    public void BattleOwnsFocusAndClearsItOnExitAndReentry()
    {
        var state = new AccessibilityState();
        Assert.Empty(state.Apply(new BattleFocusChanged(1, "attack", "Crono. Attack")));
        Assert.Equal("Battle.", Assert.Single(state.Apply(new BattleStarted(1))).Text);
        Assert.Single(state.Apply(new BattleFocusChanged(1, "attack", "Crono. Attack")));
        Assert.Empty(state.Apply(new BattleFocusChanged(1, "attack", "Crono. Attack")));
        Assert.Single(state.Apply(new BattleEnded(1)));
        Assert.Empty(state.Apply(new BattleInspectionRequested(1, "Crono. HP 43 of 70.")));
        Assert.Single(state.Apply(new BattleStarted(2)));
        Assert.Single(state.Apply(new BattleFocusChanged(2, "attack", "Crono. Attack")));
        Assert.Empty(state.Apply(new BattleEnded(1)));
        Assert.Single(state.Apply(new BattleInspectionRequested(2, "Crono. HP 43 of 70.")));
    }

    [Fact]
    public void DifferentSameNamedTargetsAndRepeatedIdenticalHitsRemainAudible()
    {
        var state = new AccessibilityState();
        state.Apply(new BattleStarted(1));
        Assert.Single(state.Apply(new BattleFocusChanged(1, "target:3", "Blue Imp")));
        Assert.Single(state.Apply(new BattleFocusChanged(1, "target:4", "Blue Imp")));
        var first = Assert.Single(state.Apply(new BattleFeedbackPresented(1, 10, "Crono takes 8 damage.")));
        Assert.False(first.Interrupt);
        Assert.Empty(state.Apply(new BattleFeedbackPresented(1, 10, "Crono takes 8 damage.")));
        Assert.Single(state.Apply(new BattleFeedbackPresented(1, 12, "Crono takes 8 damage.")));
        Assert.Single(state.Apply(new BattleFeedbackPresented(1, 11, "Marle takes 8 damage.")));
    }

    [Fact]
    public void ExplicitInspectionInterruptsAndCanRepeatWithoutChangingFocus()
    {
        var state = new AccessibilityState();
        state.Apply(new BattleStarted(1));
        var focus = new BattleFocusChanged(1, "tech:0", "Crono. Cyclone, 2 MP");
        Assert.True(Assert.Single(state.Apply(focus)).Interrupt);
        var inspection = new BattleInspectionRequested(1, "Marle. MP 7 of 12.");
        Assert.True(Assert.Single(state.Apply(inspection)).Interrupt);
        Assert.Single(state.Apply(inspection));
        Assert.Empty(state.Apply(focus));
    }

    [Fact]
    public void HiddenFocusCanReturnAndNewBattleRejectsOldFeedback()
    {
        var state = new AccessibilityState();
        state.Apply(new BattleStarted(1));
        var focus = new BattleFocusChanged(1, "attack", "Crono. Attack");
        state.Apply(focus);
        Assert.Empty(state.Apply(new BattleFocusChanged(1, null, null)));
        Assert.Single(state.Apply(focus));
        state.Apply(new BattleStarted(2));
        Assert.Empty(state.Apply(new BattleFeedbackPresented(1, 1, "Old damage")));
        Assert.Empty(state.Apply(new BattleInspectionRequested(2, " ")));
    }
}
