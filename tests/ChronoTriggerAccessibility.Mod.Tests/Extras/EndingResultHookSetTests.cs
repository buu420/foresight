using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Core.State;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Extras;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Extras;

/// <summary>
/// SaveEndingResultScene (scene 0x1B) shows the ending result, the clear-data Yes/No questions
/// and the saving notices after every ending, and nothing spoke any of them. Native evidence and
/// the replayed sequences are in artifacts/research/engine-0332 (claude-ui-report.md). The strings
/// here are synthetic; the structure is native: 0x5C-split lines as 0x40FC80 splits them, one
/// window control at key 0 for messages, two captioned buttons with focus key 1 for questions.
/// </summary>
public sealed class EndingResultHookSetTests
{
    private const nuint ImageBase = 0x400000;
    private const nuint Scene = 0x02000000;
    private const nuint OtherScene = 0x02010000;
    private const nuint Container = 0x02020000;
    private const nuint OtherContainer = 0x02028000;
    private const nuint Window = 0x02030000;
    private const nuint Gate = 0x02040000;
    private const nuint SavingCapture = 0x02050000;
    private const nuint CompleteCapture = 0x02060000;
    private const nuint OldTextNode = 0x02070000;
    private const nuint Manager = 0x02100000;
    private const nuint OtherManager = 0x02110000;
    private const nuint GateManager = 0x02120000;
    private const nuint Control = 0x02200000;
    private const nuint SecondControl = 0x02210000;
    private const nuint ThirdControl = 0x02220000;
    private const nuint State = 0x02300000;
    private const nuint SecondState = 0x02310000;
    private const nuint ThirdState = 0x02320000;
    private const nuint LabelNode = 0x02400000;
    private const nuint UnmappedText = 0x07F00000;

    private const string FirstLine = "Result line one";
    private const string SecondLine = "Result line two";
    private const string ThirdLine = "Result line three";
    private const string Question = "Question one?";
    private const string OtherQuestion = "Question two?";
    private const string Yes = "Choice A";
    private const string No = "Choice B";
    private const string SavingText = "Notice saving.";
    private const string CompleteText = "Notice complete.";

    private static readonly HookId[] ExpectedHookIds =
    [
        HookId.EndingResultDialogBuilder,
        HookId.SaveEndingResultSceneDestructor,
        HookId.EndingConfirmationBuilder,
        HookId.EndingSavingNotice,
        HookId.EndingSaveCompleteNotice,
    ];

    private static MenuOwner SceneOwner => new(EndingResultHookSet.OwnerSource, Scene);

    [Fact]
    public void PreparesEveryAuditedAddressInactiveThenActivatesThem()
    {
        var harness = Harness.Create();

        harness.Installer.PrepareAll(CreateBuild(), harness.Boundary);

        Assert.Equal(ExpectedHookIds, harness.Set.RequiredHookIds);
        Assert.Equal(ExpectedHookIds, harness.Factory.Created.Select(created => created.Id));
        Assert.Equal(
            new[] { 0x2B1390u, 0x2B09F0u, 0x2B2200u, 0x2B1F10u, 0x2B2020u }.Select(rva => ImageBase + rva),
            harness.Factory.Created.Select(created => created.Address));
        Assert.All(harness.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));

        harness.Installer.ActivateAll();

        Assert.All(harness.Installer.PreparedHooks, hook => Assert.True(hook.IsActive));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void AnOrdinaryEndingPresentsBothRenderedLinesInOrder()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine));

        Assert.Empty(harness.Dispatcher.Failures);
        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(SceneOwner, notice.Owner);
        Assert.Equal([FirstLine, SecondLine], notice.Lines!);
        Assert.Equal(Scene, harness.Set.ActiveScene);
        Assert.Equal(1, harness.MessageCalls);
    }

    /// <summary>The English first-clear message joins an empty text, the sentence and another
    /// empty text with 0x5C. The native splitter keeps the leading empty line, drops the trailing
    /// one, and 0x2B06B0 draws a label for each line it keeps.</summary>
    [Fact]
    public void AFirstClearWithEmptyOuterLinesSpeaksOnlyItsVisibleSentence()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(string.Empty, SecondLine, string.Empty));

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal([string.Empty, SecondLine], harness.RenderedLabels);
        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal([SecondLine], notice.Lines!);
    }

    [Fact]
    public void AThreeLineMessageKeepsEveryLineInNativeOrder()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine, ThirdLine));

        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal([FirstLine, SecondLine, ThirdLine], notice.Lines!);
    }

    [Fact]
    public void AWhitespaceOnlyLineIsDrawnButNotSpoken()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(string.Empty, SecondLine, " "));

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal([string.Empty, SecondLine, " "], harness.RenderedLabels);
        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal([SecondLine], notice.Lines!);
    }

    /// <summary>"Save failed." (0x2B1B40) reaches the same window as one line.</summary>
    [Fact]
    public void ASingleLineMessageIsSpokenOnce()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(FirstLine);

        Assert.Empty(harness.Dispatcher.Failures);
        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal([FirstLine], notice.Lines!);
    }

    /// <summary>The production path: labels arrive through the real shared fan-out root, the
    /// same instance every other shared-hook observer is attached to.</summary>
    [Fact]
    public void LinesRenderedThroughTheRealSharedLabelFactoryAreCaptured()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.RouteLabelsThroughTheSharedFanout();

        harness.ShowMessage(Compose(FirstLine, SecondLine));

        Assert.Empty(harness.Dispatcher.Failures);
        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal([FirstLine, SecondLine], notice.Lines!);
        Assert.Equal(2, harness.RootLabelCalls);
    }

    [Fact]
    public void TheUserHearsTheFirstLineInterruptingThenTheRestQueued()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        var state = new AccessibilityState();

        harness.ShowMessage(Compose(FirstLine, SecondLine, ThirdLine));
        var announcements = state.Apply(Assert.Single(harness.Dispatcher.Events));

        Assert.Equal([FirstLine, SecondLine, ThirdLine], announcements.Select(item => item.Text));
        Assert.Equal([true, false, false], announcements.Select(item => item.Interrupt));
        Assert.Equal(AnnouncementPriority.Queued, announcements[^1].Priority);

        harness.DestroyScene(Scene);
        Assert.True(Assert.Single(state.Apply(Assert.IsType<MenuExited>(harness.Dispatcher.Events[^1]))).StopSpeech);
    }

    [Fact]
    public void RenderedLabelsThatDisagreeWithTheMessageFailClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine), rendered: [FirstLine, ThirdLine]);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal((nuint)0, harness.Set.ActiveScene);
        Assert.Equal(1, harness.MessageCalls);
    }

    [Fact]
    public void AMessageLineThatWasNeverRenderedFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine), rendered: [FirstLine]);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    /// <summary>A plain split would expect a third, empty label here; the native splitter never
    /// draws one, so a window that did would not be the audited window.</summary>
    [Fact]
    public void ATrailingEmptyLabelTheNativeSplitterNeverDrawsFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(string.Empty, SecondLine, string.Empty),
            rendered: [string.Empty, SecondLine, string.Empty]);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\\")]
    [InlineData(" \\ ")]
    public void AWindowWithNothingVisibleFailsClosedInsteadOfBeingSilent(string composed)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(composed);

        Assert.Contains("no visible text", Assert.Single(harness.Dispatcher.Failures), StringComparison.Ordinal);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void MoreLinesThanTheWindowHoldsFailClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(Enumerable.Repeat(FirstLine, EndingResultHookSet.MaximumLineCount + 1).ToArray()));

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AnUnexpectedFontSizeIsNotTreatedAsAMessageLine()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine), fontSize: 0x10);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AMessageBuilderOnAnotherClassNeverSpeaksButStillRunsTheGame()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Memory.AddPointer(Scene, ImageBase + 0x3B3818);

        harness.ShowMessage(Compose(FirstLine, SecondLine));

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(1, harness.MessageCalls);
    }

    [Fact]
    public void AnUnreadableMessageFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine), textAddress: UnmappedText);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(1, harness.MessageCalls);
    }

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(2, 0, 0, true)]
    [InlineData(1, 1, 1, true)]
    [InlineData(1, 0, 1, true)]
    [InlineData(1, 0, 0, false)]
    public void AMessageWindowWithoutExactlyItsOneKeyZeroControlFailsClosed(
        int controls,
        int bindKey,
        int focusKey,
        bool focus)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine), controls: controls, bindKey: bindKey,
            focusKey: focusKey, focus: focus);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AMessageLabelDrawnAfterTheWindowControlFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine), afterControl: () => harness.Label(ThirdLine));

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void SharedObservationsOutsideTheBuildersBelongToSomeoneElse()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        var text = harness.AddText(FirstLine);

        harness.Set.AfterMenuTextLabelFactory(0x1234, (nint)text, 0, EndingResultHookSet.LineFontSize, (nint)LabelNode);
        harness.Set.AfterTextManagerGetMsg(0x7777, (nint)text, EndingResultHookSet.StartTextFileId, 0x11, (nint)text);
        harness.Set.AfterCustomButtonConstructed((nint)Control, (nint)Control);
        harness.Set.AfterControlBound((nint)Manager, (nint)State, 0);
        harness.MoveFocus(0);

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void DestroyingThePresentedSceneExitsOnlyItsOwnOwner()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.ShowMessage(Compose(FirstLine, SecondLine));
        harness.Dispatcher.Events.Clear();

        Assert.Equal((nint)OtherScene, harness.DestroyScene(OtherScene));
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(Scene, harness.Set.ActiveScene);

        Assert.Equal((nint)Scene, harness.DestroyScene(Scene));
        var exited = Assert.IsType<MenuExited>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(SceneOwner, exited.Owner);
        Assert.Equal((nuint)0, harness.Set.ActiveScene);

        harness.DestroyScene(Scene);
        Assert.Single(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void AReusedSceneAddressPresentsFreshContentNotTheStaleResult()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.ShowMessage(Compose(FirstLine, SecondLine));
        harness.DestroyScene(Scene);
        harness.Dispatcher.Events.Clear();

        harness.ShowMessage(Compose(ThirdLine, FirstLine));

        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal([ThirdLine, FirstLine], notice.Lines!);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void LosingTheHookLifecycleMidBuildPublishesNothing()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowMessage(Compose(FirstLine, SecondLine), duringBuild: harness.Set.AfterHooksDisabled);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal((nuint)0, harness.Set.ActiveScene);
        Assert.Equal(1, harness.MessageCalls);
    }

    [Fact]
    public void AfterHooksAreDisabledEveryBuilderOnlyRunsTheGame()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Set.AfterHooksDisabled();

        harness.ShowMessage(Compose(FirstLine, SecondLine));
        harness.Ask(Question);
        harness.ShowSaving(SavingText);
        harness.ShowSaveComplete(CompleteText);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal((1, 1, 2), (harness.MessageCalls, harness.ConfirmationCalls, harness.NoticeCalls));
    }

    public static TheoryData<string, string[]> NativeSplits => new()
    {
        { "a\\\\b", ["a", "", "b"] },
        { "\\X\\", ["", "X"] },
        { "A\\\\", ["A"] },
        { "A\\\\B\\", ["A", "", "B"] },
        { "\\", [] },
        { "", [""] },
        { "X", ["X"] },
        { " \\ ", [" ", " "] },
    };

    /// <summary>0x40FC80 counts the run of empty segments at the end and allocates only the
    /// segments before it; text without a separator is copied as one segment.</summary>
    [Theory]
    [MemberData(nameof(NativeSplits))]
    public void SplittingMatchesTheNativeSplitter(string text, string[] expected) =>
        Assert.Equal(expected, EndingResultHookSet.SplitLines(text));

    [Fact]
    public void AQuestionPresentsItsPromptAndThePreselectedSecondButtonAsItsOwnWindow()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question);

        Assert.Empty(harness.Dispatcher.Failures);
        var presented = Assert.IsType<MenuConfirmationPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(SceneOwner, presented.Owner);
        Assert.Equal(Question, presented.Prompt);
        Assert.Equal([Yes, No], presented.Choices!);
        Assert.Equal(1, presented.SelectedIndex);
        Assert.Equal(Scene, harness.Set.ActiveScene);
    }

    [Fact]
    public void TheUserHearsTheQuestionAndEveryFocusMove()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        var state = new AccessibilityState();

        harness.Ask(Question);
        var opened = state.Apply(harness.Dispatcher.Events[^1]);
        harness.MoveFocus(0);
        var left = state.Apply(harness.Dispatcher.Events[^1]);
        harness.MoveFocus(1);
        var right = state.Apply(harness.Dispatcher.Events[^1]);

        Assert.Equal([Question, $"{No}, 2 of 2"], opened.Select(item => item.Text));
        Assert.Equal($"{Yes}, 1 of 2", Assert.Single(left).Text);
        Assert.Equal($"{No}, 2 of 2", Assert.Single(right).Text);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    /// <summary>Keys come from the binder, never from the button order: with the keys swapped
    /// the committed key still names the button that is actually focused.</summary>
    [Fact]
    public void FocusFollowsTheBoundControlNotAnAssumedKeyOrder()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, keys: [1, 0], focusKey: 1);
        harness.MoveFocus(0);

        var presented = Assert.IsType<MenuConfirmationPresented>(harness.Dispatcher.Events[0]);
        Assert.Equal(0, presented.SelectedIndex);
        var moved = Assert.IsType<MenuConfirmationFocused>(harness.Dispatcher.Events[1]);
        Assert.Equal((No, 1, 2), (moved.Label, moved.SelectedIndex, moved.Count));
    }

    [Fact]
    public void AChosenWindowIsForgottenOnceTheGameRemovesIt()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Ask(Question);
        harness.Dispatcher.Events.Clear();

        harness.RemoveWindow();
        harness.MoveFocus(0);
        harness.Memory.AddPointer(Window + EndingResultHookSet.NodeParentOffset, Container);
        harness.MoveFocus(1);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    /// <summary>The next screen (the pushed save list) can allocate its manager where the
    /// removed window's manager was; that manager lives under another parent.</summary>
    [Fact]
    public void AManagerAddressReusedByAnotherScreenIsNotFollowed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Ask(Question);
        harness.Dispatcher.Events.Clear();

        harness.Memory.AddPointer(Manager + EndingResultHookSet.NodeParentOffset, OtherContainer);
        harness.MoveFocus(0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void FocusOnAnUnrelatedManagerIsIgnored()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Ask(Question);
        harness.Dispatcher.Events.Clear();

        harness.Memory.AddInt32(OtherManager + EndingResultHookSet.ManagerFocusKeyOffset, 0);
        harness.Set.AfterFocusSet((nint)OtherManager, 0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void ALiveFocusThatDidNotCommitFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Ask(Question);
        harness.Dispatcher.Events.Clear();

        harness.Memory.AddInt32(Manager + EndingResultHookSet.ManagerFocusKeyOffset, 1);
        harness.Set.AfterFocusSet((nint)Manager, 0);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DisablingDuringLiveFocusCaptureCannotSpeakOrReportAStaleFailure(int scenario)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Ask(Question);
        harness.Dispatcher.Events.Clear();
        var requestedKey = scenario == 2 ? 5 : 0;
        harness.Memory.AddInt32(Manager + EndingResultHookSet.ManagerFocusKeyOffset,
            scenario == 1 ? 1 : requestedKey);
        harness.Memory.BeforeRead = address =>
        {
            if (address != Manager) return;
            harness.Memory.BeforeRead = null;
            // The focus callback has captured the open confirmation; unload
            // retires its epoch before the first native liveness read finishes.
            harness.Set.AfterHooksDisabled();
            if (scenario == 3) throw new InvalidOperationException("Simulated capture interruption");
        };

        harness.Set.AfterFocusSet((nint)Manager, requestedKey);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void ALiveFocusOnAnUnboundKeyFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Ask(Question);
        harness.Dispatcher.Events.Clear();

        harness.MoveFocus(5);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AMessageAfterAQuestionReplacesIt()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Ask(Question);
        harness.ShowMessage(FirstLine);
        harness.Dispatcher.Events.Clear();

        harness.MoveFocus(0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void DestroyingTheSceneForgetsItsQuestion()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Ask(Question);
        harness.DestroyScene(Scene);
        harness.Dispatcher.Events.Clear();

        harness.MoveFocus(0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void AMultiLinePromptIsSpokenAsItsVisibleLines()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Compose(FirstLine, string.Empty, SecondLine));

        var presented = Assert.IsType<MenuConfirmationPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal($"{FirstLine} {SecondLine}", presented.Prompt);
    }

    [Fact]
    public void APromptLabelThatDisagreesWithThePromptFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, renderedPrompt: [OtherQuestion]);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(1, harness.ConfirmationCalls);
    }

    public static TheoryData<string[], string[]> BrokenCaptions => new()
    {
        { [Yes, No], [Yes, OtherQuestion] },
        { [Yes], [Yes, No] },
        { [Yes, No], [Yes, "  "] },
    };

    [Theory]
    [MemberData(nameof(BrokenCaptions))]
    public void AButtonWithoutExactlyItsLocalizedCaptionFailsClosed(string[] localized, string[] captions)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, localized: localized, captions: captions);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AButtonThatDrewTwoCaptionsFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, afterFirstCaption: () => harness.Label(Yes));

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AQuestionWithoutExactlyTwoButtonsFailsClosed(int buttons)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, buttons: buttons);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AQuestionWithDuplicateKeysFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, keys: [0, 0], focusKey: 0);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AQuestionFocusedOnAnUnboundKeyFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, focusKey: 4);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AQuestionOutsideItsScenesContainerFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, container: OtherContainer);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(1, harness.ConfirmationCalls);
    }

    [Fact]
    public void AQuestionWhoseManagerIsNotInsideItsWindowFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Ask(Question, windowParent: OtherContainer);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AQuestionOnAnotherClassNeverSpeaksButStillRunsTheGame()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Memory.AddPointer(Scene, ImageBase + 0x3B3818);

        harness.Ask(Question);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(1, harness.ConfirmationCalls);
    }

    /// <summary>With no remembered slot, "Yes" pushes the save list (scene 0xF). Its own
    /// presentation and close run in between; returning without saving asks again.</summary>
    [Fact]
    public void TheSameQuestionAskedAgainAfterTheSaveListIsSpokenAgain()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        var state = new AccessibilityState();
        var saveList = new MenuOwner("field-submenu", 0x09000000);

        harness.Ask(Question);
        state.Apply(harness.Dispatcher.Events[^1]);
        harness.RemoveWindow();
        state.Apply(new MenuContentPresented(saveList, "List", "File 1 of 3."));
        state.Apply(new MenuExited(saveList));
        harness.Ask(Question);
        var again = state.Apply(harness.Dispatcher.Events[^1]);

        Assert.Equal([Question, $"{No}, 2 of 2"], again.Select(item => item.Text));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void TheSavingAndCompleteNoticesAreSpokenInTurn()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowSaving(SavingText);
        harness.ShowSaveComplete(CompleteText);

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Collection(
            harness.Dispatcher.Events,
            saving => Assert.Equal([SavingText], Assert.IsType<MenuNoticePresented>(saving).Lines!),
            complete => Assert.Equal([CompleteText], Assert.IsType<MenuNoticePresented>(complete).Lines!));
        Assert.All(harness.Dispatcher.Events, item => Assert.Equal(SceneOwner, ((MenuNoticePresented)item).Owner));
    }

    [Fact]
    public void ANoticeWhoseCaptureDoesNotNameTheSceneFailsClosedButStillRunsTheGame()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Memory.AddPointer(SavingCapture + 4, OtherContainer);

        harness.ShowSaving(SavingText);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(1, harness.NoticeCalls);
    }

    [Fact]
    public void ANoticeGateOutsideTheScenesContainerFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Memory.AddPointer(Gate + EndingResultHookSet.NodeParentOffset, OtherContainer);

        harness.ShowSaveComplete(CompleteText);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void ANoticeThatNeverLocalizedItsMessageFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowSaving(SavingText, localize: false);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void ANoticeLocalizedFromAnotherMessageIdFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowSaving(SavingText, messageId: EndingResultHookSet.SaveCompleteMessageId);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void ANoticeWhoseLabelsDisagreeFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowSaveComplete(CompleteText, rendered: [SavingText]);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void ANoticeThatBuiltAControlIsNotTheAuditedNotice()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowSaving(SavingText, controls: 1);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    /// <summary>The whole post-ending flow when the game remembers the loaded slot: result,
    /// "save completion data?", "overwrite?", saving, complete, then the scene is replaced.</summary>
    [Fact]
    public void TheWholeClearDataFlowIsSpokenInOrderAndEndsWithTheScene()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        var state = new AccessibilityState();
        var spoken = new List<string>();
        var cancellations = 0;
        void Hear()
        {
            foreach (var item in state.Apply(harness.Dispatcher.Events[^1]))
            {
                if (item.StopSpeech) cancellations++;
                else spoken.Add(item.Text);
            }
        }

        harness.ShowMessage(Compose(FirstLine, SecondLine));
        Hear();
        harness.Ask(Question);
        Hear();
        harness.MoveFocus(0);
        Hear();
        harness.RemoveWindow();
        harness.Ask(OtherQuestion);
        Hear();
        harness.MoveFocus(0);
        Hear();
        harness.RemoveWindow();
        harness.ShowSaving(SavingText);
        Hear();
        harness.ShowSaveComplete(CompleteText);
        Hear();
        harness.DestroyScene(Scene);
        Hear();

        Assert.Equal(
            [
                FirstLine, SecondLine,
                Question, $"{No}, 2 of 2", $"{Yes}, 1 of 2",
                OtherQuestion, $"{No}, 2 of 2", $"{Yes}, 1 of 2",
                SavingText, CompleteText,
            ],
            spoken);
        Assert.IsType<MenuExited>(harness.Dispatcher.Events[^1]);
        Assert.Equal(1, cancellations);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    private static string Compose(params string[] lines) =>
        string.Join(EndingResultHookSet.LineSeparator, lines);

    private static VerifiedBuild CreateBuild() => new(
        ImageBase,
        ExpectedHookIds.ToDictionary(id => id, id => ImageBase + GameVersionCatalog.Get(id).Rva));

    private static UnmanagedBoundaryGuard CreateBoundary() =>
        new(new SilentLog(), new SilentFatal());

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class Harness
    {
        private int nextTextAddress;
        private MenuTextLabelFactoryDelegate? sharedLabelFactory;

        private Harness(
            EndingResultHookSet set,
            TestMemory memory,
            RecordingDispatcher dispatcher,
            RecordingHookFactory factory,
            UnmanagedBoundaryGuard boundary,
            ReloadedHookInstaller installer)
        {
            Set = set;
            Memory = memory;
            Dispatcher = dispatcher;
            Factory = factory;
            Boundary = boundary;
            Installer = installer;
        }

        public EndingResultHookSet Set { get; }
        public TestMemory Memory { get; }
        public RecordingDispatcher Dispatcher { get; }
        public RecordingHookFactory Factory { get; }
        public UnmanagedBoundaryGuard Boundary { get; }
        public ReloadedHookInstaller Installer { get; }
        public int MessageCalls { get; private set; }
        public int ConfirmationCalls { get; private set; }
        public int NoticeCalls { get; private set; }
        public int RootLabelCalls { get; private set; }
        public List<string> RenderedLabels { get; } = [];

        public static Harness Create()
        {
            const uint parent = EndingResultHookSet.NodeParentOffset;
            var memory = new TestMemory();
            memory
                .AddPointer(Scene, ImageBase + EndingResultHookSet.SceneVtableRva)
                .AddPointer(Scene + EndingResultHookSet.SceneContainerOffset, Container)
                .AddPointer(OtherScene, ImageBase + EndingResultHookSet.SceneVtableRva)
                .AddPointer(Window + parent, Container)
                .AddPointer(Gate + parent, Container)
                .AddPointer(SavingCapture, Gate)
                .AddPointer(SavingCapture + 4, Scene)
                .AddPointer(CompleteCapture, OldTextNode)
                .AddPointer(CompleteCapture + 4, Gate)
                .AddPointer(CompleteCapture + 8, Scene)
                .AddPointer(Manager, ImageBase + EndingResultHookSet.ManagerVtableRva)
                .AddPointer(Manager + parent, Window)
                .AddPointer(OtherManager, ImageBase + EndingResultHookSet.ManagerVtableRva)
                .AddPointer(Control, ImageBase + EndingResultHookSet.CustomButtonVtableRva)
                .AddPointer(SecondControl, ImageBase + EndingResultHookSet.CustomButtonVtableRva)
                .AddPointer(ThirdControl, ImageBase + EndingResultHookSet.CustomButtonVtableRva)
                .AddPointer(State, ImageBase + EndingResultHookSet.FocusableStateVtableRva)
                .AddPointer(State + EndingResultHookSet.FocusableStateControlOffset, Control)
                .AddPointer(SecondState, ImageBase + EndingResultHookSet.FocusableStateVtableRva)
                .AddPointer(SecondState + EndingResultHookSet.FocusableStateControlOffset, SecondControl)
                .AddPointer(ThirdState, ImageBase + EndingResultHookSet.FocusableStateVtableRva)
                .AddPointer(ThirdState + EndingResultHookSet.FocusableStateControlOffset, ThirdControl);
            var dispatcher = new RecordingDispatcher();
            var factory = new RecordingHookFactory();
            var set = new EndingResultHookSet(factory, memory, dispatcher);
            var installer = new ReloadedHookInstaller(set.Registrations, [set]);
            return new Harness(set, memory, dispatcher, factory, CreateBoundary(), installer);
        }

        public void PrepareAndActivate()
        {
            Installer.PrepareAll(CreateBuild(), Boundary);
            Installer.ActivateAll();
        }

        /// <summary>Roots the shared label factory exactly as production composes it.</summary>
        public void RouteLabelsThroughTheSharedFanout()
        {
            var passthrough = new PassthroughHookFactory();
            var fanout = new SharedNativeHookFanoutFactory(passthrough, [Set], _ => { });
            fanout.CreateHook<MenuTextLabelFactoryDelegate>(
                HookId.MenuTextLabelFactory,
                (_, _, _, _) =>
                {
                    RootLabelCalls++;
                    return (nint)LabelNode;
                },
                0x2400B0);
            sharedLabelFactory = passthrough.GetDetour<MenuTextLabelFactoryDelegate>(HookId.MenuTextLabelFactory);
        }

        public nuint AddText(string text)
        {
            var address = 0x03000000u + (nuint)(nextTextAddress++ * 0x1000);
            Memory.AddString(address, text);
            return address;
        }

        /// <summary>One call of the UTF-8 label factory 0x2400B0.</summary>
        public void Label(string text, int fontSize = EndingResultHookSet.LineFontSize)
        {
            var address = AddText(text);
            RenderedLabels.Add(text);
            if (sharedLabelFactory is not null)
            {
                sharedLabelFactory(0x1234, (nint)address, 0x5678, fontSize);
            }
            else
            {
                Set.AfterMenuTextLabelFactory(0x1234, (nint)address, 0x5678, fontSize, (nint)LabelNode);
            }
        }

        /// <summary>One call of TextManager::getMsg on file 0x41, returning its result buffer.</summary>
        public void Localize(int messageId, nuint textAddress) =>
            Set.AfterTextManagerGetMsg(0x7777, (nint)textAddress, EndingResultHookSet.StartTextFileId, messageId,
                (nint)textAddress);

        public void MoveFocus(int key)
        {
            Memory.AddInt32(Manager + EndingResultHookSet.ManagerFocusKeyOffset, key);
            Set.AfterFocusSet((nint)Manager, key);
        }

        /// <summary>0x2B3060 and 0x2B2EE0 call removeFromParent on the window before the callback.</summary>
        public void RemoveWindow() => Memory.AddPointer(Window + EndingResultHookSet.NodeParentOffset, 0);

        /// <summary>
        /// Replays 0x2B1390: one label per line through 0x2B06B0 at font size 0x0C, then the shared
        /// window 0x23D520: one CustomButton (0x23D49B), its binding through 0x1DD210 → 0x1DD260,
        /// and focus key 0 committed at manager + 0x2C4 (0x23D641).
        /// </summary>
        public void ShowMessage(
            string composed,
            IReadOnlyList<string>? rendered = null,
            int fontSize = EndingResultHookSet.LineFontSize,
            int controls = 1,
            int bindKey = 0,
            int focusKey = 0,
            bool focus = true,
            nuint? textAddress = null,
            Action? duringBuild = null,
            Action? afterControl = null)
        {
            var text = textAddress ?? AddText(composed);
            var lines = rendered ?? EndingResultHookSet.SplitLines(composed);
            Factory.MessageBody = (_, _, _) =>
            {
                MessageCalls++;
                foreach (var line in lines)
                {
                    Label(line, fontSize);
                }

                duringBuild?.Invoke();
                var constructed = new[] { Control, SecondControl };
                var states = new[] { State, SecondState };
                for (var index = 0; index < controls; index++)
                {
                    Set.AfterCustomButtonConstructed((nint)constructed[index], (nint)constructed[index]);
                }

                afterControl?.Invoke();
                for (var index = 0; index < Math.Max(controls, 1); index++)
                {
                    Set.AfterControlBound((nint)GateManager, (nint)states[index], bindKey + index);
                }

                if (focus)
                {
                    Memory.AddPointer(GateManager, ImageBase + EndingResultHookSet.ManagerVtableRva);
                    Memory.AddInt32(GateManager + EndingResultHookSet.ManagerFocusKeyOffset, focusKey);
                    Set.AfterFocusSet((nint)GateManager, focusKey);
                }
            };
            Memory.AddPointer(GateManager, ImageBase + EndingResultHookSet.ManagerVtableRva);
            Factory.GetDetour<EndingResultDialogBuilderDelegate>(HookId.EndingResultDialogBuilder)(
                (nint)Scene, (nint)text, 0x5555);
        }

        /// <summary>
        /// Replays 0x2B2200: the window joins the container (0x2B224B), 0x2B06B0 draws the prompt,
        /// 0x2B25C0 constructs each button, localizes (0x41, 0x11 + i) and draws it as the
        /// caption (0x2B29AB), 0x1DD210 binds both, 0x1DD3E0 commits focus key 1 (0x2B22E1) and
        /// the manager joins the window (0x2B239F).
        /// </summary>
        public void Ask(
            string prompt,
            IReadOnlyList<string>? renderedPrompt = null,
            string[]? localized = null,
            string[]? captions = null,
            int[]? keys = null,
            int focusKey = 1,
            int buttons = 2,
            nuint container = Container,
            nuint windowParent = Container,
            Action? afterFirstCaption = null)
        {
            var promptAddress = AddText(prompt);
            var lines = renderedPrompt ?? EndingResultHookSet.SplitLines(prompt);
            localized ??= [Yes, No, No];
            captions ??= localized;
            keys ??= [0, 1, 2];
            Factory.ConfirmationBody = (_, _, _, _) =>
            {
                ConfirmationCalls++;
                Memory.AddPointer(Window + EndingResultHookSet.NodeParentOffset, windowParent);
                foreach (var line in lines)
                {
                    Label(line);
                }

                var controls = new[] { Control, SecondControl, ThirdControl };
                var states = new[] { State, SecondState, ThirdState };
                for (var index = 0; index < buttons; index++)
                {
                    Set.AfterCustomButtonConstructed((nint)controls[index], (nint)controls[index]);
                    if (index < localized.Length)
                    {
                        Localize(EndingResultHookSet.FirstChoiceMessageId + index, AddText(localized[index]));
                    }

                    if (index < captions.Length)
                    {
                        Label(captions[index]);
                    }

                    if (index == 0)
                    {
                        afterFirstCaption?.Invoke();
                    }
                }

                for (var index = 0; index < buttons; index++)
                {
                    Set.AfterControlBound((nint)Manager, (nint)states[index], keys[index]);
                }

                MoveFocus(focusKey);
                Memory.AddPointer(Manager + EndingResultHookSet.NodeParentOffset, Window);
            };
            Factory.GetDetour<EndingConfirmationBuilderDelegate>(HookId.EndingConfirmationBuilder)(
                (nint)Scene, (nint)container, (nint)promptAddress, 0x6666);
        }

        /// <summary>Replays 0x2B1F10: getMsg (0x41, 0x43), then 0x2B06B0 into the gate node.</summary>
        public void ShowSaving(
            string text,
            IReadOnlyList<string>? rendered = null,
            bool localize = true,
            int messageId = EndingResultHookSet.SavingMessageId,
            int controls = 0)
        {
            ReplayNotice(text, rendered, localize, messageId, controls);
            Factory.GetDetour<EndingSavingNoticeDelegate>(HookId.EndingSavingNotice)(
                (nint)SavingCapture, (nint)GateManager, 0x7777);
        }

        /// <summary>Replays 0x2B2020: removes the old text node, getMsg (0x41, 0x44), then 0x2B06B0.</summary>
        public void ShowSaveComplete(string text, IReadOnlyList<string>? rendered = null)
        {
            ReplayNotice(text, rendered, localize: true, EndingResultHookSet.SaveCompleteMessageId, controls: 0);
            Factory.GetDetour<EndingSaveCompleteNoticeDelegate>(HookId.EndingSaveCompleteNotice)((nint)CompleteCapture);
        }

        private void ReplayNotice(string text, IReadOnlyList<string>? rendered, bool localize, int messageId, int controls)
        {
            var textAddress = AddText(text);
            Factory.NoticeBody = () =>
            {
                NoticeCalls++;
                if (localize)
                {
                    Localize(messageId, textAddress);
                }

                foreach (var line in rendered ?? EndingResultHookSet.SplitLines(text))
                {
                    Label(line);
                }

                for (var index = 0; index < controls; index++)
                {
                    Set.AfterCustomButtonConstructed((nint)Control, (nint)Control);
                }
            };
        }

        public nint DestroyScene(nuint scene) =>
            Factory.GetDetour<SaveEndingResultSceneDestructorDelegate>(HookId.SaveEndingResultSceneDestructor)(
                (nint)scene, 1);
    }

    private sealed class RecordingHookFactory : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals;
        private readonly Dictionary<HookId, Delegate> detours = [];

        public RecordingHookFactory() => originals = new Dictionary<HookId, Delegate>
        {
            // The hook set captures OriginalFunction once at preparation, so the stand-in
            // originals stay fixed and forward to bodies the test can swap in later.
            [HookId.EndingResultDialogBuilder] =
                (EndingResultDialogBuilderDelegate)((scene, text, continuation) =>
                    MessageBody?.Invoke(scene, text, continuation)),
            [HookId.SaveEndingResultSceneDestructor] =
                (SaveEndingResultSceneDestructorDelegate)((scene, _) => scene),
            [HookId.EndingConfirmationBuilder] =
                (EndingConfirmationBuilderDelegate)((scene, container, prompt, callback) =>
                    ConfirmationBody?.Invoke(scene, container, prompt, callback)),
            [HookId.EndingSavingNotice] =
                (EndingSavingNoticeDelegate)((_, _, _) => NoticeBody?.Invoke()),
            [HookId.EndingSaveCompleteNotice] =
                (EndingSaveCompleteNoticeDelegate)(_ => NoticeBody?.Invoke()),
        };

        public List<(HookId Id, nuint Address)> Created { get; } = [];
        public Action<nint, nint, nint>? MessageBody { get; set; }
        public Action<nint, nint, nint, nint>? ConfirmationBody { get; set; }
        public Action? NoticeBody { get; set; }

        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate =>
            (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            Created.Add((id, address));
            detours[id] = detour;
            return new FakeHook<TDelegate>((TDelegate)originals[id]);
        }
    }

    private sealed class FakeHook<TDelegate>(TDelegate original) : IHook<TDelegate>
        where TDelegate : Delegate
    {
        public TDelegate OriginalFunction { get; } = original;
        public IReverseWrapper<TDelegate> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 1;
        public nint OriginalFunctionWrapperAddress => 1;
        public IHook<TDelegate> Activate() { IsHookEnabled = true; IsHookActivated = true; return this; }
        IHook IHook.Activate() => Activate();
        public void Disable() => IsHookEnabled = false;
        public void Enable() => IsHookEnabled = true;
    }

    /// <summary>Roots shared hooks for the fanout without pretending to own a native original.</summary>
    private sealed class PassthroughHookFactory : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> detours = [];

        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate =>
            (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            _ = address;
            detours[id] = detour;
            return new FakeHook<TDelegate>(detour);
        }
    }

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];
        public Action<nuint>? BeforeRead { get; set; }

        public TestMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            segments[address] = bytes;
            return this;
        }

        public TestMemory AddPointer(nuint address, nuint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value));
            segments[address] = bytes;
            return this;
        }

        public TestMemory AddString(nuint address, string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            var layout = new byte[MsvcStringReader.LayoutSize];
            if (encoded.Length < 16)
            {
                encoded.CopyTo(layout, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 15);
            }
            else
            {
                var dataAddress = address + 0x40;
                BinaryPrimitives.WriteUInt32LittleEndian(layout, checked((uint)dataAddress));
                BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), (uint)encoded.Length);
                segments[dataAddress] = encoded;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            segments[address] = layout;
            return this;
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            BeforeRead?.Invoke(address);
            foreach (var (start, bytes) in segments)
            {
                if (address >= start && address + (nuint)destination.Length <= start + (nuint)bytes.Length)
                {
                    bytes.AsSpan(checked((int)(address - start)), destination.Length).CopyTo(destination);
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class RecordingDispatcher : ISemanticEventDispatcher
    {
        public int Generation => 0;
        public List<AccessibilityEvent> Events { get; } = [];
        public List<string> Failures { get; } = [];
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent accessibilityEvent) => Events.Add(accessibilityEvent);
        public void ReportCoverageFailure(string message) => Failures.Add(message);
    }

    private sealed class SilentLog : IModLog
    {
        public void Info(string message) { }
        public void Error(string message) { }
    }

    private sealed class SilentFatal : IAccessibleFatalError
    {
        public void Show(string message) { }
    }
}
