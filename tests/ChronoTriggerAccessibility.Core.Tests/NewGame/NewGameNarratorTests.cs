using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.NewGame;
using ChronoTriggerAccessibility.Core.State;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.NewGame;

public sealed class NewGameNarratorTests
{
    [Fact]
    public void ProductionAccessibilityStateRoutesNewGameEventsToNarrator()
    {
        var state = new AccessibilityState();
        var lines = Enumerable.Range(0, 25)
            .Select(index => new ControlDescriptionLine([], [$"Binding {index}"]))
            .ToArray();

        var announcements = state.Apply(new ControlDescriptionsPresented(
            "Control Descriptions", lines, "Next"));

        Assert.Equal(27, announcements.Count);
        Assert.Equal("Control Descriptions.", announcements[0].Text);
        Assert.Equal("Next, 1 of 1", announcements[^1].Text);
    }

    [Fact]
    public void ControlDescriptionsNarratesEveryLocalizedRecordInSpriteAssociatedReadingOrder()
    {
        var narrator = new NewGameNarrator();
        var visibleIndex = 0;
        var lineCounts = new[] { 2, 2, 2, 2, 3, 1, 3, 2, 2, 2, 2, 2 };
        var lines = lineCounts.Select((count, line) => new ControlDescriptionLine(
            line == 5 ? [] : [$"Button {line + 1}"],
            Enumerable.Range(0, count).Select(_ => $"localized record {++visibleIndex:D2}").ToArray()))
            .ToArray();

        var announcements = narrator.Apply(new ControlDescriptionsPresented(
            "Localized controls title", lines, "Localized next"));

        Assert.Equal(14, announcements.Count);
        Assert.Equal("Localized controls title.", announcements[0].Text);
        Assert.True(announcements[0].Interrupt);
        Assert.Equal("Localized next, 1 of 1", announcements[^1].Text);
        Assert.False(announcements[^1].Interrupt);
        var allText = string.Join(" ", announcements.Select(item => item.Text));
        for (var index = 1; index <= 25; index++)
        {
            Assert.Equal(1, CountOccurrences(allText, $"localized record {index:D2}"));
        }
        Assert.DoesNotContain("keyboard layout", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gamepad layout", allText, StringComparison.OrdinalIgnoreCase);

        var activation = Assert.Single(narrator.Apply(
            new ControlDescriptionNextActivated("Localized next")));
        Assert.Equal("Localized next selected.", activation.Text);
        Assert.True(activation.Interrupt);
    }

    [Fact]
    public void InvalidControlCoverageEmitsOneExactFatalAndNeverPartialScreenOutput()
    {
        var narrator = new NewGameNarrator();
        var onlyTwentyFour = Enumerable.Range(1, 24)
            .Select(index => new ControlDescriptionLine([], [$"record {index}"]))
            .ToArray();

        var failure = Assert.Single(narrator.Apply(new ControlDescriptionsPresented(
            "Controls", onlyTwentyFour, "Next")));
        Assert.True(failure.Interrupt);
        Assert.Equal(
            "Chrono Trigger accessibility stopped: Control Descriptions captured 24 localized records; expected exactly 25.",
            failure.Text);

        Assert.Empty(narrator.Apply(CreateValidControlPresentation()));
        Assert.Empty(narrator.Apply(new NewGameCoverageFailed("second failure")));
    }

    [Fact]
    public void ModeSelectNarratesFocusValuesHelpChangesActivationAndCancellation()
    {
        var narrator = new NewGameNarrator();
        var rows = new[]
        {
            new ModeSelectRowPresentation("Battle Mode", "ACTIVE", "Actions continue while commands are selected."),
            new ModeSelectRowPresentation("Graphics", "Original", "Uses the original presentation."),
            new ModeSelectRowPresentation("Interface", "Gamepad", "Shows gamepad button artwork."),
        };

        var entered = narrator.Apply(new ModeSelectPresented(
            rows, "Start", 0));
        Assert.Equal(new[]
        {
            "New Game settings.",
            "Battle Mode: ACTIVE. Actions continue while commands are selected. Left control, 1 of 4",
        }, entered.Select(item => item.Text));

        var focusChanged = Assert.Single(narrator.Apply(new ModeSelectChanged(
            rows, "Start", 11)));
        Assert.Equal(
            "Graphics: Original. Uses the original presentation. Right control, 2 of 4",
            focusChanged.Text);

        var changedRows = rows.ToArray();
        changedRows[1] = changedRows[1] with
        {
            Value = "High resolution",
            Help = "Uses higher-resolution graphics.",
        };
        var valueChanged = Assert.Single(narrator.Apply(new ModeSelectChanged(
            changedRows, "Start", 11)));
        Assert.Equal(
            "Graphics: High resolution. Uses higher-resolution graphics. Right control, 2 of 4",
            valueChanged.Text);
        Assert.Empty(narrator.Apply(new ModeSelectChanged(
            changedRows, "Start", 11)));

        var start = Assert.Single(narrator.Apply(new ModeSelectChanged(
            changedRows, "Start", 30)));
        Assert.Equal("Start, 4 of 4", start.Text);
        Assert.Equal("New Game settings closed.", Assert.Single(narrator.Apply(new ModeSelectCancelled())).Text);

        narrator.Apply(new ModeSelectPresented(
            changedRows, "Start", 30));
        Assert.Equal("Start selected.", Assert.Single(narrator.Apply(new ModeSelectActivated("Start"))).Text);
        var staleChange = Assert.Single(narrator.Apply(new ModeSelectChanged(
            changedRows, "Start", 11)));
        Assert.Contains("without a validated screen entry", staleChange.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NameEntryNarratesFullNameGridActionsEmptyRejectionAndCorrelatedConfirmation()
    {
        var narrator = new NewGameNarrator();

        var entered = Assert.Single(narrator.Apply(new NameEntryPresented(
            "Enter a name", "Crono", "Latin letters and symbols")));
        Assert.Equal(
            "Enter a name. Current name: Crono. Maximum five characters. Latin letters and symbols.",
            entered.Text);

        Assert.Equal(
            "A. Latin page, row 1, column 1.",
            Assert.Single(narrator.Apply(new NameGridFocused("A", "Latin", 0, 0))).Text);
        Assert.Equal("Character grid closed.",
            Assert.Single(narrator.Apply(new NameGridVisibilityChanged(Open: false))).Text);
        Assert.Empty(narrator.Apply(new NameGridVisibilityChanged(Open: true)));
        Assert.Equal(
            "A. Latin page, row 1, column 1.",
            Assert.Single(narrator.Apply(new NameGridFocused("A", "Latin", 0, 0))).Text);
        Assert.Equal("Name: Crona.",
            Assert.Single(narrator.Apply(new NewGameNameChanged("Crona"))).Text);
        Assert.Empty(narrator.Apply(new NewGameNameChanged("Crona")));
        Assert.Equal("Name: Crono.",
            Assert.Single(narrator.Apply(new NewGameNameChanged("Crono"))).Text);
        Assert.Equal("Name is empty.",
            Assert.Single(narrator.Apply(new NewGameNameChanged(string.Empty))).Text);

        Assert.Equal("Defaults, 1 of 4",
            Assert.Single(narrator.Apply(new NameActionFocused("Defaults", 0, 4))).Text);
        Assert.Equal("Defaults selected.",
            Assert.Single(narrator.Apply(new NameActionActivated("Defaults"))).Text);
        Assert.Equal("Accept, 2 of 4",
            Assert.Single(narrator.Apply(new NameActionFocused("Accept", 1, 4))).Text);
        Assert.Equal("Character grid, 4 of 4",
            Assert.Single(narrator.Apply(new NameActionFocused("Character grid", 3, 4))).Text);
        Assert.Equal("A name is required.",
            Assert.Single(narrator.Apply(new EmptyNameRejected())).Text);

        var confirmation = narrator.Apply(new NameConfirmationPresented(
            "Begin as Crono?",
            ["Localized choice A", "Localized choice B"],
            SelectedIndex: 1));
        Assert.Equal(new[] { "Begin as Crono?", "Localized choice B, 2 of 2" },
            confirmation.Select(item => item.Text));
        Assert.Equal("Localized choice A, 1 of 2",
            Assert.Single(narrator.Apply(new NameConfirmationFocused(
                "Localized choice A", 0, 2))).Text);
    }

    [Fact]
    public void KeyboardNameEntryNarratesOpenTypedNameAndCloseWithoutClippingBatchCompanions()
    {
        var narrator = new NewGameNarrator();
        narrator.Apply(new NameEntryPresented(
            "Enter a name", "Crono", "Use shown characters"));

        var opened = narrator.Apply(new NameAccessibilityBatch(
        [
            new NameGridVisibilityChanged(false),
            new KeyboardNameEntryFocused("Crono"),
        ]));

        Assert.Equal(
            ["Character grid closed.", "Keyboard name entry. Current name: Crono."],
            opened.Select(item => item.Text));
        Assert.Equal([true, false], opened.Select(item => item.Interrupt));
        Assert.Equal(
            "Name: Lucca.",
            Assert.Single(narrator.Apply(new NewGameNameChanged("Lucca"))).Text);
        Assert.Equal(
            "Keyboard name entry closed. Current name: Lucca.",
            Assert.Single(narrator.Apply(new KeyboardNameEntryClosed("Lucca"))).Text);
    }

    [Fact]
    public void InactiveGridKeyboardRoundTripReannouncesRestoredKeyTwoFocus()
    {
        var narrator = new NewGameNarrator();
        narrator.Apply(new NameEntryPresented(
            "Enter a name", "Crono", "Use the character grid or keyboard name entry"));
        Assert.Equal(
            "Name text field, 3 of 4",
            Assert.Single(narrator.Apply(
                new NameActionFocused("Name text field", 2, 4))).Text);
        narrator.Apply(new KeyboardNameEntryFocused("Crono"));

        var closed = narrator.Apply(new NameAccessibilityBatch(
        [
            new KeyboardNameEntryClosed("Crono"),
            new NameActionFocused("Name text field", 2, 4),
        ]));

        Assert.Equal(
            ["Keyboard name entry closed. Current name: Crono.", "Name text field, 3 of 4"],
            closed.Select(item => item.Text));
        Assert.Equal([true, false], closed.Select(item => item.Interrupt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void WrongNameActionCountFailsClosedBecauseRuntimeManagerHasExactlyFourControls(int count)
    {
        var narrator = new NewGameNarrator();
        narrator.Apply(new NameEntryPresented(
            "Enter a name", "Crono", "Use shown characters"));

        var failure = Assert.Single(narrator.Apply(
            new NameActionFocused("Accept", 1, count)));

        Assert.Contains("exact control correlation", failure.Text, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Chrono Trigger accessibility stopped:", failure.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidModeOrNamePresentationFailsBeforeAnyPartialNarration()
    {
        var invalidMode = new NewGameNarrator();
        var modeFailure = Assert.Single(invalidMode.Apply(new ModeSelectPresented(
            [new("Battle Mode", "ACTIVE", "Help")], "Start", 0)));
        Assert.Contains("exactly three", modeFailure.Text, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Chrono Trigger accessibility stopped:", modeFailure.Text, StringComparison.Ordinal);

        var invalidName = new NewGameNarrator();
        var nameFailure = Assert.Single(invalidName.Apply(new NameEntryPresented(
            "Enter a name", "😀ABCD", "Latin letters and symbols")));
        Assert.Contains("UTF-16", nameFailure.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(invalidName.Apply(new NameGridFocused("A", "Latin", 0, 0)));
    }

    [Fact]
    public void ExplicitCoverageFailurePreservesTheExactDiagnosticAndFaultsOnce()
    {
        var narrator = new NewGameNarrator();
        const string diagnostic = "Confirmation manager key 1 has no correlated localized control.";

        var output = Assert.Single(narrator.Apply(new NewGameCoverageFailed(diagnostic)));
        Assert.Equal($"Chrono Trigger accessibility stopped: {diagnostic}", output.Text);
        Assert.Equal(AnnouncementPriority.Interrupt, output.Priority);
        Assert.True(output.Interrupt);
        Assert.Empty(narrator.Apply(new NewGameCoverageFailed(diagnostic)));
    }

    private static ControlDescriptionsPresented CreateValidControlPresentation()
    {
        var index = 0;
        return new ControlDescriptionsPresented(
            "Controls",
            Enumerable.Range(0, 25)
                .Select(_ => new ControlDescriptionLine([], [$"record {++index}"]))
                .ToArray(),
            "Next");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }
}
