using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Dialogue;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Dialogue;

public sealed class DialogueNarratorTests
{
    [Fact]
    public void LinesQueueAndSameTextIsSuppressedOnlyWithinTheSameLineIdentity()
    {
        var narrator = OpenDialogue();

        var first = Assert.Single(narrator.Apply(new DialogueLinePresented(0, 0, "The millennial fair has begun.")));
        var repeated = narrator.Apply(new DialogueLinePresented(0, 0, "The millennial fair has begun."));
        var laterPage = Assert.Single(narrator.Apply(new DialogueLinePresented(1, 0, "The millennial fair has begun.")));

        Assert.Equal("The millennial fair has begun.", first.Text);
        Assert.Equal(AnnouncementPriority.Queued, first.Priority);
        Assert.False(first.Interrupt);
        Assert.Empty(repeated);
        Assert.Equal("The millennial fair has begun.", laterPage.Text);
    }

    [Fact]
    public void ExactLineIdentityIsSuppressedAfterAnotherLineWasPresented()
    {
        var narrator = OpenDialogue();
        narrator.Apply(new DialogueLinePresented(0, 0, "First line."));
        narrator.Apply(new DialogueLinePresented(0, 1, "Second line."));

        var repeated = narrator.Apply(new DialogueLinePresented(0, 0, "First line."));

        Assert.Empty(repeated);
    }

    [Fact]
    public void ChoicePresentationInterruptsOnceQueuesVisibleChoicesThenFocusedChoice()
    {
        var narrator = OpenDialogue();

        var announcements = narrator.Apply(new DialogueChoicesPresented(["Yes", "No"], 1));

        Assert.Equal(["Yes", "No", "No, 2 of 2"], announcements.Select(item => item.Text));
        Assert.Equal([true, false, false], announcements.Select(item => item.Interrupt));
        Assert.Equal(
            [AnnouncementPriority.Interrupt, AnnouncementPriority.Queued, AnnouncementPriority.Queued],
            announcements.Select(item => item.Priority));
    }

    [Fact]
    public void ChoicePresentationWithNegativeOneSelectionQueuesChoicesWithoutFocus()
    {
        var narrator = OpenDialogue();

        var announcements = narrator.Apply(new DialogueChoicesPresented(["Yes", "No"], -1));

        Assert.Equal(["Yes", "No"], announcements.Select(item => item.Text));
        Assert.Equal([true, false], announcements.Select(item => item.Interrupt));
    }

    [Fact]
    public void ChoiceFocusMovementInterruptsOnlyTheNewSelectionAndActivationUsesVisibleLabel()
    {
        var narrator = OpenDialogue();
        narrator.Apply(new DialogueChoicesPresented(["Yes", "No"], 0));

        var moved = Assert.Single(narrator.Apply(new DialogueChoiceFocused("No", 1, 2)));
        var activated = Assert.Single(narrator.Apply(new DialogueChoiceActivated("No")));

        Assert.Equal("No, 2 of 2", moved.Text);
        Assert.True(moved.Interrupt);
        Assert.Equal("No selected.", activated.Text);
        Assert.True(activated.Interrupt);
    }

    [Fact]
    public void CloseAndReopenResetDialogueAndChoiceIdentities()
    {
        var narrator = OpenDialogue();
        narrator.Apply(new DialogueLinePresented(0, 0, "Hello."));
        narrator.Apply(new DialogueChoicesPresented(["Yes", "No"], 0));
        narrator.Apply(new DialogueClosed());
        narrator.Apply(new DialogueOpened());

        var line = Assert.Single(narrator.Apply(new DialogueLinePresented(0, 0, "Hello.")));
        var choices = narrator.Apply(new DialogueChoicesPresented(["Yes", "No"], 0));

        Assert.Equal("Hello.", line.Text);
        Assert.Equal(["Yes", "No", "Yes, 1 of 2"], choices.Select(item => item.Text));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    public void InvalidChoiceSelectionFailsBeforePartialOutput(int selectedIndex)
    {
        var narrator = OpenDialogue();

        var failure = Assert.Single(narrator.Apply(new DialogueChoicesPresented(["Yes", "No"], selectedIndex)));

        Assert.StartsWith("Chrono Trigger accessibility stopped:", failure.Text, StringComparison.Ordinal);
        Assert.Empty(narrator.Apply(new DialogueLinePresented(0, 0, "No partial output after fault.")));
    }

    [Fact]
    public void EmptyChoiceCountFailsBeforePartialOutput()
    {
        var narrator = OpenDialogue();

        var failure = Assert.Single(narrator.Apply(new DialogueChoicesPresented([], -1)));

        Assert.StartsWith("Chrono Trigger accessibility stopped:", failure.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankLineAndBlankChoiceFaultTheFamilyOnce()
    {
        var blankLine = OpenDialogue();
        var blankLineFailure = Assert.Single(blankLine.Apply(new DialogueLinePresented(0, 0, "")));
        Assert.Contains("line", blankLineFailure.Text, StringComparison.OrdinalIgnoreCase);

        var blankChoice = OpenDialogue();
        var blankChoiceFailure = Assert.Single(blankChoice.Apply(new DialogueChoicesPresented(["Yes", ""], 0)));
        Assert.Contains("choice", blankChoiceFailure.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(blankChoice.Apply(new DialogueCoverageFailed("second failure")));
    }

    [Fact]
    public void ExplicitCoverageFailurePreservesDiagnosticAndFaultsOnce()
    {
        var narrator = new DialogueNarrator();

        var failure = Assert.Single(narrator.Apply(new DialogueCoverageFailed("Dialogue vector is misaligned.")));

        Assert.Equal(
            "Chrono Trigger accessibility stopped: Dialogue vector is misaligned.",
            failure.Text);
        Assert.Empty(narrator.Apply(new DialogueCoverageFailed("another failure")));
    }

    [Fact]
    public void ChoicesAreDefensiveSnapshots()
    {
        var choices = new List<string> { "Yes", "No" };
        var presented = new DialogueChoicesPresented(choices, 0);
        choices[0] = "changed";

        Assert.Equal("Yes", presented.Choices![0]);
    }

    private static DialogueNarrator OpenDialogue()
    {
        var narrator = new DialogueNarrator();
        Assert.Empty(narrator.Apply(new DialogueOpened()));
        return narrator;
    }
}
