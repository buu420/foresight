using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Menus;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Menus;

public sealed class MenuNarratorTests
{
    private static readonly MenuOwner TopMenuOwner = new("TopMenu", 0x0050_1000);
    private static readonly MenuOwner SettingsOwner = new("SteamSettings", 0x0060_2000);

    [Fact]
    public void EntryInterruptsWithTitleThenQueuesFocusAndVisibleStatusInOrder()
    {
        var narrator = new MenuNarrator();

        var announcements = narrator.Apply(new MenuPresented(
            TopMenuOwner,
            "Menu",
            new MenuFocus("Items", null, 1, 7, null, false),
            ["12:34", "600G", "Truce Canyon", "Crono 120 / 120"]));

        Assert.Equal(
            ["Menu.", "Items, 1 of 7", "12:34", "600G", "Truce Canyon", "Crono 120 / 120"],
            announcements.Select(item => item.Text));
        Assert.Equal(
            [AnnouncementPriority.Interrupt, AnnouncementPriority.Queued, AnnouncementPriority.Queued,
                AnnouncementPriority.Queued, AnnouncementPriority.Queued, AnnouncementPriority.Queued],
            announcements.Select(item => item.Priority));
        Assert.Equal([true, false, false, false, false, false], announcements.Select(item => item.Interrupt));
    }

    [Fact]
    public void FocusFormatsOnlyPresentValueAndHelpAndMarksUnavailable()
    {
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuPresented(
            TopMenuOwner,
            "Settings",
            new MenuFocus("Battle", "Active", 1, 6, "Battles continue while you choose commands.", false),
            []));

        var valueAndHelp = Assert.Single(narrator.Apply(new MenuFocusChanged(
            new MenuFocus("Battle", "Wait", 1, 6, "Battles pause while you choose commands.", false))));
        var helpOnly = Assert.Single(narrator.Apply(new MenuFocusChanged(
            new MenuFocus("Back", null, 6, 6, "Return to the title screen.", false))));
        var disabled = Assert.Single(narrator.Apply(new MenuFocusChanged(
            new MenuFocus("Load", null, 6, 7, null, true))));

        Assert.Equal("Battle: Wait, 1 of 6. Battles pause while you choose commands.", valueAndHelp.Text);
        Assert.Equal("Back, 6 of 6. Return to the title screen.", helpOnly.Text);
        Assert.Equal("Load, 6 of 7, unavailable", disabled.Text);
        Assert.True(valueAndHelp.Interrupt);
        Assert.True(disabled.Interrupt);
    }

    [Fact]
    public void ActivationUsesCapturedVisibleLabel()
    {
        var narrator = ActiveMenu();

        var announcement = Assert.Single(narrator.Apply(new MenuActivated("Settings")));

        Assert.Equal("Settings selected.", announcement.Text);
        Assert.True(announcement.Interrupt);
    }

    [Fact]
    public void ExitResetsFocusIdentityForReopenedMenu()
    {
        var narrator = ActiveMenu();
        narrator.Apply(new MenuFocusChanged(new MenuFocus("Items", null, 1, 7, null, false)));

        Assert.Empty(narrator.Apply(new MenuExited(TopMenuOwner)));
        var reopened = narrator.Apply(new MenuPresented(
            TopMenuOwner,
            "Menu", new MenuFocus("Items", null, 1, 7, null, false), []));

        Assert.Equal(["Menu.", "Items, 1 of 7"], reopened.Select(item => item.Text));
    }

    [Fact]
    public void UnsupportedBoundaryNarratesOnlyTheSuppliedSelectionBoundaryAndReturnInstruction()
    {
        var narrator = ActiveMenu();

        var announcements = narrator.Apply(new MenuUnsupported(
            "Movies",
            "Detailed reading is unavailable on this screen.",
            "Use the localized Back control to return."));

        Assert.Equal(
            ["Movies", "Detailed reading is unavailable on this screen.", "Use the localized Back control to return."],
            announcements.Select(item => item.Text));
        Assert.Equal([true, false, false], announcements.Select(item => item.Interrupt));
    }

    [Theory]
    [InlineData("", "Detailed reading is unavailable.", "Use Back to return.")]
    [InlineData("Movies", "", "Use Back to return.")]
    [InlineData("Movies", "Detailed reading is unavailable.", "")]
    public void UnsupportedBoundaryRejectsAnyBlankFieldBeforePartialOutput(
        string selectedLabel,
        string boundaryText,
        string returnInstruction)
    {
        var narrator = ActiveMenu();

        var announcements = narrator.Apply(new MenuUnsupported(
            selectedLabel,
            boundaryText,
            returnInstruction));

        var failure = Assert.Single(announcements);
        Assert.StartsWith("Chrono Trigger accessibility stopped:", failure.Text, StringComparison.Ordinal);
        Assert.True(failure.Interrupt);
    }

    [Fact]
    public void ConfirmationInterruptsPromptQueuesFocusedChoiceAndFocusMovementInterrupts()
    {
        var narrator = ActiveMenu();

        var presented = narrator.Apply(new MenuConfirmationPresented(
            "Restore default settings?", ["Yes", "No"], 0));
        var focused = Assert.Single(narrator.Apply(new MenuConfirmationFocused("No", 1, 2)));

        Assert.Equal(["Restore default settings?", "Yes, 1 of 2"], presented.Select(item => item.Text));
        Assert.Equal([true, false], presented.Select(item => item.Interrupt));
        Assert.Equal("No, 2 of 2", focused.Text);
        Assert.True(focused.Interrupt);
    }

    [Fact]
    public void ExactDuplicateFocusAndConfirmationAreSuppressed()
    {
        var narrator = ActiveMenu();
        var focus = new MenuFocus("Items", null, 1, 7, null, false);
        narrator.Apply(new MenuFocusChanged(focus));
        narrator.Apply(new MenuConfirmationPresented("Restore defaults?", ["Yes", "No"], 0));

        Assert.Empty(narrator.Apply(new MenuFocusChanged(focus)));
        Assert.Empty(narrator.Apply(new MenuConfirmationPresented("Restore defaults?", ["Yes", "No"], 0)));
        Assert.Empty(narrator.Apply(new MenuConfirmationFocused("Yes", 0, 2)));
    }

    [Fact]
    public void InvalidPresentationFailsBeforePartialOutputAndFaultsOnlyMenuFamily()
    {
        var narrator = new MenuNarrator();

        var failure = Assert.Single(narrator.Apply(new MenuPresented(
            TopMenuOwner,
            "Menu",
            new MenuFocus("Items", null, 1, 7, null, false),
            ["12:34", ""] )));

        Assert.Equal(
            "Chrono Trigger accessibility stopped: Menu presentation contains a null or blank visible status detail.",
            failure.Text);
        Assert.True(failure.Interrupt);
        Assert.Empty(narrator.Apply(new MenuCoverageFailed("second failure")));
        Assert.Empty(narrator.Apply(new MenuPresented(
            TopMenuOwner,
            "Menu", new MenuFocus("Items", null, 1, 7, null, false), [])));
    }

    [Fact]
    public void ExplicitCoverageFailurePreservesDiagnosticAndFaultsOnce()
    {
        var narrator = new MenuNarrator();

        var failure = Assert.Single(narrator.Apply(new MenuCoverageFailed("Missing localized Settings label.")));

        Assert.Equal(
            "Chrono Trigger accessibility stopped: Missing localized Settings label.",
            failure.Text);
        Assert.Empty(narrator.Apply(new MenuCoverageFailed("another failure")));
    }

    [Fact]
    public void EventsSnapshotEveryInputList()
    {
        var status = new List<string> { "12:34" };
        var choices = new List<string> { "Yes", "No" };
        var presented = new MenuPresented(
            TopMenuOwner,
            "Menu", new MenuFocus("Items", null, 1, 7, null, false), status);
        var confirmation = new MenuConfirmationPresented("Restore defaults?", choices, 0);
        status[0] = "changed";
        choices[0] = "changed";

        Assert.Equal("12:34", Assert.Single(presented.StatusDetails!));
        Assert.Equal("Yes", confirmation.Choices![0]);
    }

    [Fact]
    public void StaleParentCloseAfterChildPresentationKeepsTheChildMenuActive()
    {
        // Exact native ordering observed at 22:47:02 in the 2026-09-11 session log: the top
        // menu activates Settings, the Settings node presents, and only then does the parent
        // top-menu StatusBar/root deleting destructor run and publish its close.
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuPresented(
            TopMenuOwner,
            "Menu", new MenuFocus("Settings", null, 5, 7, null, false), []));
        narrator.Apply(new MenuActivated("Settings"));
        narrator.Apply(new MenuPresented(
            SettingsOwner,
            "Settings",
            new MenuFocus("Battle", null, 1, 6, "Change battle settings.", false),
            []));

        Assert.Empty(narrator.Apply(new MenuExited(TopMenuOwner)));

        var focused = Assert.Single(narrator.Apply(new MenuFocusChanged(
            new MenuFocus("Sound", null, 2, 6, "Adjust the sound volume.", false))));
        Assert.Equal("Sound, 2 of 6. Adjust the sound volume.", focused.Text);
    }

    [Fact]
    public void OwningCloseAfterAStaleParentCloseStillEndsTheMenu()
    {
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuPresented(
            TopMenuOwner, "Menu", new MenuFocus("Settings", null, 5, 7, null, false), []));
        narrator.Apply(new MenuPresented(
            SettingsOwner, "Settings", new MenuFocus("Battle", null, 1, 6, null, false), []));
        narrator.Apply(new MenuExited(TopMenuOwner));

        Assert.Empty(narrator.Apply(new MenuExited(SettingsOwner)));

        var failure = Assert.Single(narrator.Apply(new MenuFocusChanged(
            new MenuFocus("Sound", null, 2, 6, null, false))));
        Assert.Equal(
            "Chrono Trigger accessibility stopped: Menu focus changed without a validated active menu.",
            failure.Text);
    }

    [Fact]
    public void SameOwnerCloseEndsTheMenuAndADifferentInstanceOfTheSameSourceDoesNot()
    {
        var narrator = ActiveMenu();
        var otherInstance = TopMenuOwner with { Instance = TopMenuOwner.Instance + 0x40 };

        Assert.Empty(narrator.Apply(new MenuExited(otherInstance)));
        Assert.Single(narrator.Apply(new MenuFocusChanged(
            new MenuFocus("Inventory", null, 2, 7, null, false))));

        Assert.Empty(narrator.Apply(new MenuExited(TopMenuOwner)));
        var failure = Assert.Single(narrator.Apply(new MenuFocusChanged(
            new MenuFocus("Equipment", null, 1, 7, null, false))));
        Assert.StartsWith("Chrono Trigger accessibility stopped:", failure.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PresentationWithoutAnOwningNativeMenuIdentityFailsClosed(string? source)
    {
        var narrator = new MenuNarrator();
        var owner = source is null ? null : new MenuOwner(source, 0x1234);

        var failure = Assert.Single(narrator.Apply(new MenuPresented(
            owner, "Menu", new MenuFocus("Items", null, 1, 7, null, false), [])));

        Assert.Equal(
            "Chrono Trigger accessibility stopped: Menu presentation has no owning native menu identity.",
            failure.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CloseWithoutAnOwningNativeMenuIdentityFailsClosed(string? source)
    {
        var narrator = ActiveMenu();
        var owner = source is null ? null : new MenuOwner(source, 0x1234);

        var failure = Assert.Single(narrator.Apply(new MenuExited(owner)));

        Assert.Equal(
            "Chrono Trigger accessibility stopped: Menu exit has no owning native menu identity.",
            failure.Text);
    }

    private static MenuNarrator ActiveMenu()
    {
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuPresented(
            TopMenuOwner,
            "Menu", new MenuFocus("Items", null, 1, 7, null, false), []));
        return narrator;
    }
}
