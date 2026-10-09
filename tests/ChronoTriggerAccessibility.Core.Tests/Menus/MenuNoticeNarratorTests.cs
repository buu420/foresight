using ChronoTriggerAccessibility.Core.Menus;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Menus;

/// <summary>
/// Windows that are only rendered text (ending results, saving and completion notices) and
/// confirmations that are windows of their own rather than layers over an active menu.
/// </summary>
public sealed class MenuNoticeNarratorTests
{
    private static readonly MenuOwner Ending = new("EndingResult", 0x100), SaveList = new("field-submenu", 0x200);
    private const string Stopped = "Chrono Trigger accessibility stopped:";

    [Fact]
    public void ANoticeInterruptsWithItsFirstLineAndQueuesTheRestInOrder()
    {
        var narrator = new MenuNarrator();

        var spoken = narrator.Apply(new MenuNoticePresented(Ending, ["Line one", "Line two", "Line three"]));

        Assert.Equal(["Line one", "Line two", "Line three"], spoken.Select(item => item.Text));
        Assert.Equal([true, false, false], spoken.Select(item => item.Interrupt));
    }

    [Fact]
    public void ASingleLineNoticeIsSpokenOnceWithoutAnInventedTitle()
    {
        var narrator = new MenuNarrator();

        var spoken = Assert.Single(narrator.Apply(new MenuNoticePresented(Ending, ["Only line."])));

        Assert.Equal("Only line.", spoken.Text);
        Assert.True(spoken.Interrupt);
    }

    [Fact]
    public void ANoticeOwnsNarrationUntilItsOwnOwnerCloses()
    {
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuContentPresented(SaveList, "Save", "File 1 of 3."));
        narrator.Apply(new MenuNoticePresented(Ending, ["Saving."]));

        Assert.Empty(narrator.Apply(new MenuContentChanged(SaveList, "File 2 of 3.")));
        Assert.Empty(narrator.Apply(new MenuExited(SaveList)));
        Assert.True(Assert.Single(narrator.Apply(new MenuExited(Ending))).StopSpeech);
        // Closed: a stale list update for the old owner stays silent.
        Assert.Empty(narrator.Apply(new MenuContentChanged(SaveList, "File 3 of 3.")));
    }

    [Fact]
    public void AMenuPresentingAfterANoticeTakesNarrationBack()
    {
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuNoticePresented(Ending, ["Save complete."]));

        var list = narrator.Apply(new MenuContentPresented(SaveList, "Save", "File 1 of 3."));

        Assert.Equal(["Save.", "File 1 of 3."], list.Select(item => item.Text));
        Assert.Empty(narrator.Apply(new MenuExited(Ending)));
        Assert.Single(narrator.Apply(new MenuContentChanged(SaveList, "File 2 of 3.")));
    }

    public static TheoryData<string?, string[]?> MalformedNotices => new()
    {
        { "EndingResult", null },
        { "EndingResult", [] },
        { "EndingResult", ["Line one", ""] },
        { "EndingResult", ["   "] },
        { "", ["Line one"] },
        { " ", ["Line one"] },
    };

    [Theory]
    [MemberData(nameof(MalformedNotices))]
    public void AMalformedNoticeFailsClosedBeforeAnyPartialOutput(string? source, string[]? lines)
    {
        var narrator = new MenuNarrator();

        var failure = Assert.Single(narrator.Apply(new MenuNoticePresented(new MenuOwner(source!, 1), lines)));

        Assert.StartsWith(Stopped, failure.Text, StringComparison.Ordinal);
        Assert.Empty(narrator.Apply(new MenuNoticePresented(Ending, ["Line one"])));
    }

    [Fact]
    public void NoticeLinesAreSnapshotted()
    {
        var lines = new List<string> { "Line one" };
        var notice = new MenuNoticePresented(Ending, lines);
        lines[0] = "changed";

        Assert.Equal("Line one", Assert.Single(notice.Lines!));
    }

    [Fact]
    public void AnOwnedConfirmationNeedsNoEarlierPresentationAndFollowsFocus()
    {
        var narrator = new MenuNarrator();

        var presented = narrator.Apply(new MenuConfirmationPresented(Ending, "Question?", ["Yes", "No"], 1));
        var moved = Assert.Single(narrator.Apply(new MenuConfirmationFocused("Yes", 0, 2)));

        Assert.Equal(["Question?", "No, 2 of 2"], presented.Select(item => item.Text));
        Assert.Equal([true, false], presented.Select(item => item.Interrupt));
        Assert.Equal("Yes, 1 of 2", moved.Text);
    }

    [Fact]
    public void TheSameOwnedQuestionBuiltAgainIsSpokenAgain()
    {
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuConfirmationPresented(Ending, "Question?", ["Yes", "No"], 1));

        var again = narrator.Apply(new MenuConfirmationPresented(Ending, "Question?", ["Yes", "No"], 1));

        Assert.Equal(["Question?", "No, 2 of 2"], again.Select(item => item.Text));
    }

    [Fact]
    public void AnOwnedConfirmationEndsOnlyWithItsOwnOwner()
    {
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuContentPresented(SaveList, "Save", "File 1 of 3."));
        narrator.Apply(new MenuConfirmationPresented(Ending, "Question?", ["Yes", "No"], 1));

        Assert.Empty(narrator.Apply(new MenuExited(SaveList)));
        Assert.Single(narrator.Apply(new MenuConfirmationFocused("Yes", 0, 2)));
        Assert.True(Assert.Single(narrator.Apply(new MenuExited(Ending))).StopSpeech);
        Assert.Empty(narrator.Apply(new MenuContentChanged(SaveList, "File 2 of 3.")));
    }

    [Fact]
    public void AnUnownedConfirmationStillRequiresAnActiveMenu()
    {
        var narrator = new MenuNarrator();

        var failure = Assert.Single(narrator.Apply(new MenuConfirmationPresented("Question?", ["Yes", "No"], 0)));

        Assert.StartsWith(Stopped, failure.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AnOwnedConfirmationWithoutANativeIdentityFailsClosed(string source)
    {
        var narrator = new MenuNarrator();

        var failure = Assert.Single(narrator.Apply(
            new MenuConfirmationPresented(new MenuOwner(source, 1), "Question?", ["Yes", "No"], 0)));

        Assert.StartsWith(Stopped, failure.Text, StringComparison.Ordinal);
    }
}
