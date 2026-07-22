using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.State;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.State;

public sealed class AccessibilityStateTests
{
    [Fact]
    public void ScreenEntered_TitlePrompt_EmitsInterruptingPrompt()
    {
        var state = new AccessibilityState();

        var announcements = state.Apply(new ScreenEntered(ScreenKind.TitlePrompt));

        var announcement = Assert.Single(announcements);
        Assert.Equal("Chrono Trigger. Press confirm.", announcement.Text);
        Assert.Equal(AnnouncementPriority.Interrupt, announcement.Priority);
        Assert.True(announcement.Interrupt);
    }

    [Fact]
    public void ScreenEntered_SameScreenWithoutExit_EmitsOnlyOnce()
    {
        var state = new AccessibilityState();

        state.Apply(new ScreenEntered(ScreenKind.TitlePrompt));
        var repeatedAnnouncements = state.Apply(new ScreenEntered(ScreenKind.TitlePrompt));

        Assert.Empty(repeatedAnnouncements);
    }

    [Fact]
    public void FocusChanged_NewFocus_EmitsLabelAndPosition()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.TitleMenu));

        var announcements = state.Apply(new FocusChanged("New Game", 2, 7, false));

        var announcement = Assert.Single(announcements);
        Assert.Equal("New Game, 2 of 7", announcement.Text);
    }

    [Fact]
    public void FocusChanged_IdenticalEvent_EmitsNothing()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.TitleMenu));
        state.Apply(new FocusChanged("New Game", 2, 7, false));

        var announcements = state.Apply(new FocusChanged("New Game", 2, 7, false));

        Assert.Empty(announcements);
    }

    [Fact]
    public void ValueChanged_EmitsLabelValueAndHelp()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.NewGameConfiguration));

        var announcements = state.Apply(new ValueChanged(
            "Battle Mode",
            "WAIT",
            "Battles pause while you choose commands."));

        var announcement = Assert.Single(announcements);
        Assert.Equal("Battle Mode: WAIT. Battles pause while you choose commands.", announcement.Text);
    }

    [Fact]
    public void ValueChanged_SameLabelWithNewValue_EmitsAgain()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.NewGameConfiguration));
        state.Apply(new ValueChanged("Battle Mode", "ACTIVE", "Battles continue while you choose commands."));

        var announcements = state.Apply(new ValueChanged(
            "Battle Mode",
            "WAIT",
            "Battles pause while you choose commands."));

        Assert.Equal("Battle Mode: WAIT. Battles pause while you choose commands.", Assert.Single(announcements).Text);
    }

    [Fact]
    public void NameChanged_EmitsTheFullResultingName()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.NameEntry));

        var announcements = state.Apply(new NameChanged("Crono"));

        Assert.Equal("Crono", Assert.Single(announcements).Text);
    }

    [Fact]
    public void ConfirmationOpened_AnnouncesPromptAndFocusedChoice()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.NameEntry));

        var announcements = state.Apply(new ConfirmationOpened(
            "Start with the name Crono?",
            ["Yes", "No"],
            0));

        Assert.Collection(
            announcements,
            announcement => Assert.Equal("Start with the name Crono?", announcement.Text),
            announcement => Assert.Equal("Yes, 1 of 2", announcement.Text));
    }

    [Fact]
    public void ConfirmationOpened_ChangedChoicesWithSamePromptAndIndex_EmitsUpdatedChoice()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.NameEntry));
        state.Apply(new ConfirmationOpened(
            "Start with the name Crono?",
            ["Yes", "No"],
            0));

        var announcements = state.Apply(new ConfirmationOpened(
            "Start with the name Crono?",
            ["Confirm", "Cancel"],
            0));

        Assert.Collection(
            announcements,
            announcement => Assert.Equal("Start with the name Crono?", announcement.Text),
            announcement => Assert.Equal("Confirm, 1 of 2", announcement.Text));
    }

    [Fact]
    public void ScreenExited_OpeningMovie_IncrementsGenerationAndRejectsStaleTimedDescriptions()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.OpeningMovie));
        var movieGeneration = state.Generation;

        state.Apply(new ScreenExited(ScreenKind.OpeningMovie));
        var announcements = state.Apply(new TimedDescription("A pendulum swings.", movieGeneration));

        Assert.Equal(movieGeneration + 1, state.Generation);
        Assert.Empty(announcements);
    }

    [Fact]
    public void TimedDescription_UnchangedEventIsSuppressedButNewTextEmits()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.OpeningMovie));
        var movieGeneration = state.Generation;

        var firstAnnouncements = state.Apply(new TimedDescription("A pendulum swings.", movieGeneration));
        var repeatedAnnouncements = state.Apply(new TimedDescription("A pendulum swings.", movieGeneration));
        var changedAnnouncements = state.Apply(new TimedDescription("A clock face fills the screen.", movieGeneration));

        Assert.Equal("A pendulum swings.", Assert.Single(firstAnnouncements).Text);
        Assert.Empty(repeatedAnnouncements);
        Assert.Equal("A clock face fills the screen.", Assert.Single(changedAnnouncements).Text);
    }

    [Fact]
    public void StartupScenesAnnouncePublisherOpeningAndTitleWithoutDuplicatingTheLivePrompt()
    {
        var state = new AccessibilityState();

        Assert.Equal("Square Enix.", Assert.Single(
            state.Apply(new StartupSceneEntered(StartupSceneKind.SquareEnixLogo))).Text);
        Assert.Equal("Opening movie.", Assert.Single(
            state.Apply(new StartupSceneEntered(StartupSceneKind.OpeningMovie))).Text);
        Assert.Equal("Chrono Trigger.", Assert.Single(
            state.Apply(new StartupSceneEntered(StartupSceneKind.Title))).Text);
        Assert.Equal("Press confirm.", Assert.Single(
            state.Apply(new ScreenEntered(ScreenKind.TitlePrompt))).Text);
    }

    [Fact]
    public void ActivatingTitleItemUsesTheCapturedVisibleLabel()
    {
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.TitleMenu));

        var announcement = Assert.Single(state.Apply(new ControlActivated("New Game +")));

        Assert.Equal("New Game + selected.", announcement.Text);
        Assert.True(announcement.Interrupt);
    }
}
