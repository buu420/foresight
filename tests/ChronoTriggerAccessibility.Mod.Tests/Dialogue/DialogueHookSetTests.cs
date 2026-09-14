using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Dialogue;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Dialogue;

public sealed class DialogueHookSetTests
{
    private const nuint ImageBase = 0x00400000;
    private const nuint Window = 0x01000000;
    private const nuint OtherWindow = 0x01200000;
    private static readonly HookId[] ExpectedHookIds =
    [
        HookId.MsgWindowOpen,
        HookId.MsgWindowUpdate,
        HookId.MsgWindowClose,
        HookId.MsgWindowChoiceConfirmCallSite,
    ];

    [Fact]
    public void PreparationOwnsFourVerifiedHooksUsesSafeProbeAndRollsBackProbeBeforeClose()
    {
        var harness = CreateHarness();

        Assert.Equal(ExpectedHookIds, harness.Set.RequiredHookIds);
        Assert.Equal(
            ExpectedHookIds.Select(id => GameVersionCatalog.Get(id).Symbol),
            harness.Set.Registrations.Select(registration => registration.Name));

        harness.Installer.PrepareAll(CreateBuild(), harness.Boundary);

        Assert.Equal(ExpectedHookIds[..3], harness.Functions.Created.Select(item => item.Id));
        Assert.Equal(HookId.MsgWindowChoiceConfirmCallSite, harness.Assembly.Id);
        Assert.Equal(
            ExpectedHookIds.Select(id => ImageBase + GameVersionCatalog.Get(id).Rva),
            harness.Installer.PreparedHooks.Select((_, index) => index < 3
                ? harness.Functions.Created[index].Address
                : harness.Assembly.Address));
        Assert.All(harness.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.All(harness.Functions.Hooks, hook => Assert.Equal(0, hook.ActivateCount));
        Assert.Equal(0, harness.Assembly.NativeHook.ActivateCount);
        Assert.Equal(
            new RuntimeAsmHookOptions(
                AsmHookBehaviour.ExecuteFirst,
                HookLength: 5,
                PreferRelativeJump: true,
                MaxOpcodeSize: 5),
            harness.Assembly.Options);
        Assert.Equal(ExpectedProbeAssembly, harness.Assembly.Code);
        Assert.Contains(harness.Assembly.Callback, harness.Assembly.Prepared.LifetimeRoots);
        Assert.Contains(harness.Assembly.ReverseWrapper, harness.Assembly.Prepared.LifetimeRoots);
        Assert.Contains(harness.Assembly.Code, harness.Assembly.Prepared.LifetimeRoots);
        Assert.Equal(
            GameVersionCatalog.MsgWindowChoiceConfirmReturnRva,
            GameVersionCatalog.MsgWindowChoiceConfirmCallRva + 5);

        harness.Installer.ActivateAll();

        Assert.All(harness.Installer.PreparedHooks, hook => Assert.True(hook.IsActive));
        Assert.Equal(1, harness.Assembly.NativeHook.ActivateCount);
        Assert.Equal(1, harness.Assembly.NativeHook.EnableCount);

        harness.Installer.DisableAll();

        Assert.Equal(
        [
            "disable:asm",
            $"disable:{HookId.MsgWindowClose}",
            $"disable:{HookId.MsgWindowUpdate}",
            $"disable:{HookId.MsgWindowOpen}",
        ], harness.Lifecycle.Where(item => item.StartsWith("disable:", StringComparison.Ordinal)));
    }

    [Fact]
    public void ActivationAndPreparationRejectMissingOrConflictingVerifiedBuildState()
    {
        var unprepared = CreateHarness();
        Assert.Throws<InvalidOperationException>(unprepared.Set.AfterHooksActivated);

        var zeroBase = CreateHarness();
        Assert.Throws<InvalidOperationException>(() =>
            zeroBase.Installer.PrepareAll(CreateBuild(imageBase: 0), zeroBase.Boundary));

        foreach (var missing in ExpectedHookIds)
        {
            var harness = CreateHarness();
            Assert.Throws<InvalidOperationException>(() =>
                harness.Installer.PrepareAll(CreateBuild(missing: missing), harness.Boundary));
            Assert.All(harness.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        }

        var conflicting = CreateHarness();
        var first = conflicting.Set.Registrations[0].Prepare(CreateBuild(), conflicting.Boundary);
        Assert.False(first.IsActive);
        Assert.Throws<InvalidOperationException>(() =>
            conflicting.Set.Registrations[1].Prepare(
                CreateBuild(imageBase: ImageBase + 0x100000),
                conflicting.Boundary));
        Assert.Throws<InvalidOperationException>(conflicting.Set.AfterHooksActivated);
    }

    [Fact]
    public void FunctionDetoursForwardEveryRawArgumentCallOriginalOnceAndCaptureOnlyAfterOriginal()
    {
        const uint openRawWord = 0x11223344;
        const uint updateRawWord = 0x55667788;
        const uint closeRawWord = 0x99AABBCC;
        var harness = CreateHarness();
        var openCalls = 0;
        var updateCalls = 0;
        var closeCalls = 0;
        harness.Functions.SetOriginal<MsgWindowOpenDelegate>(HookId.MsgWindowOpen, (window, rawWord) =>
        {
            openCalls++;
            Assert.Equal((nint)Window, window);
            Assert.Equal(openRawWord, rawWord);
            harness.Memory.SetOrdinary(Window, ["Opened", "Future"], cursor: 0, pageBase: 0);
        });
        harness.Functions.SetOriginal<MsgWindowUpdateDelegate>(HookId.MsgWindowUpdate, (window, rawWord) =>
        {
            updateCalls++;
            Assert.Equal((nint)Window, window);
            Assert.Equal(updateRawWord, rawWord);
            harness.Memory.SetOrdinary(Window, ["Opened", "Updated"], cursor: 1, pageBase: 0);
        });
        harness.Functions.SetOriginal<MsgWindowCloseDelegate>(HookId.MsgWindowClose, (window, rawWord) =>
        {
            closeCalls++;
            Assert.Equal((nint)Window, window);
            Assert.Equal(closeRawWord, rawWord);
            harness.Memory.SetInactive(Window);
        });
        harness.PrepareAndActivate();

        harness.Open(openRawWord);
        harness.Update(updateRawWord);
        harness.Close(closeRawWord);

        Assert.Equal(1, openCalls);
        Assert.Equal(1, updateCalls);
        Assert.Equal(1, closeCalls);
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.IsType<DialogueOpened>(item),
            item => Assert.Equal(new DialogueLinePresented(0, 0, "Opened"), item),
            item => Assert.Equal(new DialogueLinePresented(0, 1, "Updated"), item),
            item => Assert.IsType<DialogueClosed>(item));
        Assert.DoesNotContain(
            harness.Dispatcher.Events.OfType<DialogueLinePresented>(),
            line => line.Text == "Future");
    }

    [Fact]
    public void DelayedNativeOpeningWaitsThroughInactiveUpdatesThenOpensWithFirstCompleteSnapshot()
    {
        var harness = CreateHarness();
        harness.Memory.SetInactive(Window);
        harness.PrepareAndActivate();

        harness.Open();
        harness.Update();
        harness.Update();

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);

        harness.Memory.SetOrdinary(Window, ["Now visible"], cursor: 0, pageBase: 0);
        harness.Update();

        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.IsType<DialogueOpened>(item),
            item => Assert.Equal(new DialogueLinePresented(0, 0, "Now visible"), item));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void PendingOpeningInvalidStateFailsBeforeAnyOpenOrPartialText()
    {
        var harness = CreateHarness();
        harness.Memory.SetInactive(Window);
        harness.PrepareAndActivate();
        harness.Open();
        Assert.Empty(harness.Dispatcher.Failures);

        harness.Memory.SetInvalidActive(Window, 2);
        harness.Update();

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void PendingWindowCloseCallsOriginalAndResetsWithCloseButNeverFabricatesOpen()
    {
        var harness = CreateHarness();
        var closeCalls = 0;
        harness.Functions.SetOriginal<MsgWindowCloseDelegate>(HookId.MsgWindowClose, (_, _) => closeCalls++);
        harness.Memory.SetInactive(Window);
        harness.PrepareAndActivate();
        harness.Open();

        harness.Close();

        Assert.Equal(1, closeCalls);
        Assert.Collection(harness.Dispatcher.Events, item => Assert.IsType<DialogueClosed>(item));
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueOpened>());
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueLinePresented>());
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void DisableWhilePendingSuppressesLateActivationAndClearsPendingWindow()
    {
        var harness = CreateHarness();
        harness.Memory.SetInactive(Window);
        harness.PrepareAndActivate();
        harness.Open();

        harness.Installer.DisableAll();
        harness.Memory.SetOrdinary(Window, ["Too late"], cursor: 0, pageBase: 0);
        harness.Update();

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void OrdinaryLinesIgnoreFutureVectorEntriesDeduplicateRevealAndResetAfterClose()
    {
        var harness = CreateHarness();
        harness.Memory.SetWaiting(Window, ["Future", "Later"]);
        harness.PrepareAndActivate();

        harness.Open();
        Assert.Single(harness.Dispatcher.Events, item => item is DialogueOpened);

        harness.Memory.SetOrdinary(Window, ["Same", "Same"], cursor: 0, pageBase: 0);
        harness.Update();
        harness.Update();
        harness.Memory.SetOrdinary(Window, ["Same", "Same"], cursor: 1, pageBase: 1);
        harness.Update();
        harness.Close();
        harness.Memory.SetOrdinary(Window, ["Same"], cursor: 0, pageBase: 0);
        harness.Open();

        Assert.Equal(
        [
            new DialogueLinePresented(0, 0, "Same"),
            new DialogueLinePresented(1, 1, "Same"),
            new DialogueLinePresented(0, 0, "Same"),
        ], harness.Dispatcher.Events.OfType<DialogueLinePresented>());
        Assert.DoesNotContain(
            harness.Dispatcher.Events.OfType<DialogueLinePresented>(),
            line => line.Text is "Future" or "Later");
        Assert.Equal(2, harness.Dispatcher.Events.Count(item => item is DialogueOpened));
        Assert.Single(harness.Dispatcher.Events, item => item is DialogueClosed);
    }

    [Fact]
    public void ChoicesPublishCompleteListsThenOnlyLaterValidFocusChanges()
    {
        var harness = CreateHarness();
        harness.Memory.SetWaiting(Window, ["Prompt"]);
        harness.PrepareAndActivate();
        harness.Open();

        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: -1);
        harness.Update();
        harness.Update();
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 0);
        harness.Update();
        harness.Update();
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 1);
        harness.Update();
        harness.Memory.SetChoices(Window, "Prompt", ["Fight", "Flee"], selectedIndex: 1);
        harness.Update();

        var semantic = harness.Dispatcher.Events.Where(item => item is not DialogueOpened).ToArray();
        Assert.Collection(
            semantic,
            item => AssertChoices(item, ["Yes", "No"], -1),
            item => Assert.Equal(new DialogueChoiceFocused("Yes", 0, 2), item),
            item => Assert.Equal(new DialogueChoiceFocused("No", 1, 2), item),
            item => AssertChoices(item, ["Fight", "Flee"], 1));
    }

    [Fact]
    public void ExactConfirmProbeMarksOnlyAndPublishesRetainedSelectionBeforeClose()
    {
        const uint closeRawWord = 0xCAFEBABE;
        var harness = CreateHarness();
        var closeCalls = 0;
        harness.Functions.SetOriginal<MsgWindowCloseDelegate>(HookId.MsgWindowClose, (window, rawWord) =>
        {
            closeCalls++;
            Assert.Equal((nint)Window, window);
            Assert.Equal(closeRawWord, rawWord);
        });
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 1);
        harness.PrepareAndActivate();
        harness.Open();

        harness.Assembly.Invoke(Window);

        Assert.Equal(0, closeCalls);
        Assert.DoesNotContain(harness.Dispatcher.Events, item => item is DialogueChoiceActivated);

        harness.Close(closeRawWord);

        Assert.Equal(1, closeCalls);
        Assert.Collection(
            harness.Dispatcher.Events.TakeLast(2),
            item => Assert.Equal(new DialogueChoiceActivated("No"), item),
            item => Assert.IsType<DialogueClosed>(item));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RacePredictionTransitionsReadBothChoiceListsAndAllRunnerFocusChanges(int promptPhase)
    {
        const string prompt = "Try and guess the next winner?";
        string[] answers = ["Sure!", "Not this time."];
        string[] promptLines = [prompt, .. answers];
        uint[] promptFlags = [0, 0x10, 0x10];
        string[] runners =
        [
            "No. 1   Steel Runner", "No. 2   Green Ambler",
            "No. 3   Catalack", "No. 4   G.I. Jogger",
        ];
        uint[] runnerFlags = [0x10, 0x10, 0x10, 0x10];
        var harness = CreateHarness();
        harness.Memory.SetSnapshot(Window, promptLines, promptFlags,
            cursor: 0, pageBase: 0, phase: 0, choiceCount: 0, selectedIndex: -1, active: 1);
        harness.PrepareAndActivate();
        harness.Open();

        // The native update advances the cursor before it commits the choice controls.
        harness.Memory.SetSnapshot(Window, promptLines, promptFlags,
            cursor: 1, pageBase: 0, phase: promptPhase, choiceCount: 0, selectedIndex: -1, active: 1);
        harness.Update();
        harness.Update();

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Collection(harness.Dispatcher.Events,
            item => Assert.IsType<DialogueOpened>(item),
            item => Assert.Equal(new DialogueLinePresented(0, 0, prompt), item));

        harness.Memory.SetSnapshot(Window, promptLines, promptFlags,
            cursor: 3, pageBase: 0, phase: 4, choiceCount: 2, selectedIndex: -1, active: 1);
        harness.Update();
        harness.Update();
        harness.Memory.SetSnapshot(Window, promptLines, promptFlags,
            cursor: 3, pageBase: 0, phase: 4, choiceCount: 2, selectedIndex: 0, active: 1);
        harness.Update();
        harness.Assembly.Invoke(Window);
        harness.Close();

        // The next window contains four choices and no ordinary prompt line.
        harness.Memory.SetSnapshot(Window, runners, runnerFlags,
            cursor: 0, pageBase: 0, phase: 0, choiceCount: 0, selectedIndex: -1, active: 1);
        harness.Open();
        harness.Update();
        Assert.Single(harness.Dispatcher.Events.OfType<DialogueChoicesPresented>());
        Assert.Single(harness.Dispatcher.Events.OfType<DialogueLinePresented>());
        harness.Memory.SetSnapshot(Window, runners, runnerFlags,
            cursor: 4, pageBase: 0, phase: 4, choiceCount: 4, selectedIndex: -1, active: 1);
        harness.Update();
        harness.Update();
        for (var index = 0; index < runners.Length; index++)
        {
            harness.Memory.SetSnapshot(Window, runners, runnerFlags,
                cursor: 4, pageBase: 0, phase: 4, choiceCount: 4, selectedIndex: index, active: 1);
            harness.Update();
            harness.Update();
        }
        harness.Assembly.Invoke(Window);
        harness.Close();
        harness.Memory.SetOrdinary(Window, ["Good luck!"], cursor: 0, pageBase: 0);
        harness.Open();

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Collection(harness.Dispatcher.Events.OfType<DialogueChoicesPresented>(),
            item => AssertChoices(item, answers, -1),
            item => AssertChoices(item, runners, -1));
        Assert.Equal(
            new[] { new DialogueChoiceFocused(answers[0], 0, 2) }
                .Concat(runners.Select((label, index) => new DialogueChoiceFocused(label, index, 4))),
            harness.Dispatcher.Events.OfType<DialogueChoiceFocused>());
        Assert.Equal(
            [new DialogueChoiceActivated(answers[0]), new DialogueChoiceActivated(runners[3])],
            harness.Dispatcher.Events.OfType<DialogueChoiceActivated>());
        Assert.Equal(2, harness.Dispatcher.Events.Count(item => item is DialogueClosed));
        Assert.Equal(new DialogueLinePresented(0, 0, "Good luck!"), harness.Dispatcher.Events.Last());

        var narrator = new DialogueNarrator();
        var speech = harness.Dispatcher.Events.SelectMany(narrator.Apply).Select(item => item.Text).ToArray();
        Assert.Contains(prompt, speech);
        Assert.All(answers.Concat(runners), label => Assert.Contains(label, speech));
        Assert.All(runners.Select((label, index) => $"{label}, {index + 1} of 4"),
            focused => Assert.Contains(focused, speech));
        Assert.Contains($"{runners[3]} selected.", speech);
        Assert.Equal("Good luck!", speech.Last());
    }

    [Fact]
    public void SameUpdateMovementThenConfirmUsesFreshPreCloseChoiceAndPublishesFocusFirst()
    {
        var harness = CreateHarness();
        var closeCalls = 0;
        harness.Functions.SetOriginal<MsgWindowUpdateDelegate>(HookId.MsgWindowUpdate, (_, _) =>
        {
            harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 1);
            harness.Assembly.Invoke(Window);
            harness.Close();
        });
        harness.Functions.SetOriginal<MsgWindowCloseDelegate>(HookId.MsgWindowClose, (_, _) => closeCalls++);
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 0);
        harness.PrepareAndActivate();
        harness.Open();

        harness.Update();

        Assert.Equal(1, closeCalls);
        Assert.Collection(
            harness.Dispatcher.Events.TakeLast(3),
            item => Assert.Equal(new DialogueChoiceFocused("No", 1, 2), item),
            item => Assert.Equal(new DialogueChoiceActivated("No"), item),
            item => Assert.IsType<DialogueClosed>(item));
        Assert.DoesNotContain(
            harness.Dispatcher.Events.OfType<DialogueChoiceActivated>(),
            item => item.Label == "Yes");
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void SameUpdateFirstSelectionThenConfirmPromotesPresentedNegativeOneBeforeActivation()
    {
        var harness = CreateHarness();
        harness.Functions.SetOriginal<MsgWindowUpdateDelegate>(HookId.MsgWindowUpdate, (_, _) =>
        {
            harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 0);
            harness.Assembly.Invoke(Window);
            harness.Close();
        });
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: -1);
        harness.PrepareAndActivate();
        harness.Open();

        harness.Update();

        Assert.Collection(
            harness.Dispatcher.Events.TakeLast(3),
            item => Assert.Equal(new DialogueChoiceFocused("Yes", 0, 2), item),
            item => Assert.Equal(new DialogueChoiceActivated("Yes"), item),
            item => Assert.IsType<DialogueClosed>(item));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void MarkedCloseListMismatchCallsOriginalOnceThenFailsWithoutStaleActivationOrClose()
    {
        var harness = CreateHarness();
        var closeCalls = 0;
        harness.Functions.SetOriginal<MsgWindowCloseDelegate>(HookId.MsgWindowClose, (_, _) => closeCalls++);
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 0);
        harness.PrepareAndActivate();
        harness.Open();
        harness.Assembly.Invoke(Window);
        harness.Memory.SetChoices(Window, "Prompt", ["Fight", "Flee"], selectedIndex: 1);

        harness.Close();

        Assert.Equal(1, closeCalls);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueChoiceActivated>());
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueClosed>());
    }

    [Fact]
    public void MissingWrongWindowNegativeAndCrossThreadProbeMarkersNeverActivate()
    {
        var harness = CreateHarness();
        harness.PrepareAndActivate();

        OpenChoices(harness, selectedIndex: 0);
        harness.Close();

        OpenChoices(harness, selectedIndex: 0);
        harness.Assembly.Invoke(OtherWindow);
        harness.Close();

        OpenChoices(harness, selectedIndex: -1);
        harness.Assembly.Invoke(Window);
        harness.Close();

        harness.Memory.SetOrdinary(Window, ["Line"], cursor: 0, pageBase: 0);
        harness.Open();
        harness.Assembly.Invoke(Window);
        harness.Close();

        OpenChoices(harness, selectedIndex: 1);
        Exception? callbackFailure = null;
        var callbackThread = new Thread(() =>
        {
            try
            {
                harness.Assembly.Invoke(Window);
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
            }
        });
        callbackThread.Start();
        Assert.True(callbackThread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(callbackFailure);
        harness.Close();

        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueChoiceActivated>());
        Assert.Equal(5, harness.Dispatcher.Events.Count(item => item is DialogueClosed));
    }

    [Fact]
    public void InvalidOpenAndOutOfRangeChoiceCaptureFailCoverageBeforePartialEvents()
    {
        var invalidOpen = CreateHarness();
        invalidOpen.Memory.SetInvalidActive(Window, 2);
        invalidOpen.PrepareAndActivate();

        var openException = Record.Exception(() => invalidOpen.Open());

        Assert.Null(openException);
        Assert.Empty(invalidOpen.Dispatcher.Events);
        Assert.Single(invalidOpen.Dispatcher.Failures);
        Assert.False(string.IsNullOrWhiteSpace(invalidOpen.Dispatcher.Failures[0]));
        invalidOpen.Memory.SetOrdinary(Window, ["Late"], cursor: 0, pageBase: 0);
        invalidOpen.Open();
        Assert.Empty(invalidOpen.Dispatcher.Events);

        var invalidChoice = CreateHarness();
        invalidChoice.Memory.SetWaiting(Window, ["Prompt"]);
        invalidChoice.PrepareAndActivate();
        invalidChoice.Open();
        invalidChoice.Memory.SetChoices(
            Window,
            "Prompt",
            ["Yes", "No"],
            selectedIndex: 2);

        var updateException = Record.Exception(() => invalidChoice.Update());
        invalidChoice.Assembly.Invoke(Window);
        invalidChoice.Close();

        Assert.Null(updateException);
        Assert.Single(invalidChoice.Dispatcher.Failures);
        Assert.Single(invalidChoice.Dispatcher.Events, item => item is DialogueOpened);
        Assert.Empty(invalidChoice.Dispatcher.Events.OfType<DialogueChoicesPresented>());
        Assert.Empty(invalidChoice.Dispatcher.Events.OfType<DialogueChoiceActivated>());
        Assert.Empty(invalidChoice.Dispatcher.Events.OfType<DialogueClosed>());
    }

    [Fact]
    public void RegisteredWindowCaptureFailureClearsStateAndNeverFallsBackToStaleClose()
    {
        var harness = CreateHarness();
        harness.Memory.SetOrdinary(Window, ["Visible"], cursor: 0, pageBase: 0);
        harness.PrepareAndActivate();
        harness.Open();
        harness.Memory.ThrowOnRead = true;

        var updateException = Record.Exception(() => harness.Update());
        harness.Memory.ThrowOnRead = false;
        harness.Assembly.Invoke(Window);
        harness.Close();

        Assert.Null(updateException);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.IsType<DialogueOpened>(item),
            item => Assert.Equal(new DialogueLinePresented(0, 0, "Visible"), item));
    }

    [Fact]
    public void OriginalCloseFailureIsContainedClearsPendingActivationAndFaultsEpoch()
    {
        var harness = CreateHarness();
        var closeCalls = 0;
        harness.Functions.SetOriginal<MsgWindowCloseDelegate>(HookId.MsgWindowClose, (_, _) =>
        {
            closeCalls++;
            throw new InvalidOperationException("simulated close failure");
        });
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 0);
        harness.PrepareAndActivate();
        harness.Open();
        harness.Assembly.Invoke(Window);

        var exception = Record.Exception(() => harness.Close());
        harness.Memory.SetOrdinary(Window, ["Late"], cursor: 0, pageBase: 0);
        harness.Update();

        Assert.Null(exception);
        Assert.Equal(1, closeCalls);
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueChoiceActivated>());
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueClosed>());
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueLinePresented>());
        Assert.Single(harness.Fatal.Messages);
    }

    [Fact]
    public void DispatcherAndCoverageReporterFailuresCannotEscapeOrLeakLaterEvents()
    {
        var publishFailure = CreateHarness();
        publishFailure.Memory.SetOrdinary(Window, ["Line"], cursor: 0, pageBase: 0);
        publishFailure.Dispatcher.ThrowOnPublish = true;
        publishFailure.PrepareAndActivate();

        var publishException = Record.Exception(() => publishFailure.Open());
        publishFailure.Dispatcher.ThrowOnPublish = false;
        publishFailure.Update();

        Assert.Null(publishException);
        Assert.Empty(publishFailure.Dispatcher.Events);
        Assert.Single(publishFailure.Fatal.Messages);

        var reportFailure = CreateHarness();
        reportFailure.Memory.SetInvalidActive(Window, 2);
        reportFailure.Dispatcher.ThrowOnCoverageFailure = true;
        reportFailure.PrepareAndActivate();

        var reportException = Record.Exception(() => reportFailure.Open());

        Assert.Null(reportException);
        Assert.Single(reportFailure.Dispatcher.Failures);
        Assert.Empty(reportFailure.Dispatcher.Events);
        Assert.Single(reportFailure.Fatal.Messages);
    }

    [Fact]
    public async Task DisableDuringBlockedOriginalSuppressesLateCaptureAndClearsProbeMarker()
    {
        var harness = CreateHarness();
        using var enteredOriginal = new ManualResetEventSlim(false);
        using var releaseOriginal = new ManualResetEventSlim(false);
        var closeCalls = 0;
        harness.Functions.SetOriginal<MsgWindowUpdateDelegate>(HookId.MsgWindowUpdate, (_, _) =>
        {
            enteredOriginal.Set();
            if (!releaseOriginal.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("blocked update original was not released");
            }
            harness.Memory.SetOrdinary(Window, ["Late"], cursor: 0, pageBase: 0);
        });
        harness.Functions.SetOriginal<MsgWindowCloseDelegate>(HookId.MsgWindowClose, (_, _) => closeCalls++);
        harness.PrepareAndActivate();
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 0);
        harness.Open();
        harness.Assembly.Invoke(Window);

        var updateTask = Task.Run(() => harness.Update());
        Assert.True(enteredOriginal.Wait(TimeSpan.FromSeconds(5)));
        harness.Installer.DisableAll();
        harness.Assembly.Invoke(Window);
        releaseOriginal.Set();
        await updateTask.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Close();

        Assert.Equal(1, closeCalls);
        Assert.Collection(
            harness.Dispatcher.Events,
            @event => Assert.IsType<DialogueOpened>(@event),
            @event => Assert.IsType<DialogueChoicesPresented>(@event));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public async Task DisableDuringBlockedCloseSuppressesPreparedActivationAndLateClose()
    {
        var harness = CreateHarness();
        using var enteredOriginal = new ManualResetEventSlim(false);
        using var releaseOriginal = new ManualResetEventSlim(false);
        var closeCalls = 0;
        harness.Functions.SetOriginal<MsgWindowCloseDelegate>(HookId.MsgWindowClose, (_, _) =>
        {
            closeCalls++;
            enteredOriginal.Set();
            if (!releaseOriginal.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("blocked close original was not released");
            }
        });
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex: 1);
        harness.PrepareAndActivate();
        harness.Open();

        var closeTask = Task.Run(() =>
        {
            harness.Assembly.Invoke(Window);
            harness.Close();
        });
        Assert.True(enteredOriginal.Wait(TimeSpan.FromSeconds(5)));
        harness.Installer.DisableAll();
        releaseOriginal.Set();
        await closeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, closeCalls);
        Assert.Collection(
            harness.Dispatcher.Events,
            @event => Assert.IsType<DialogueOpened>(@event),
            @event => Assert.IsType<DialogueChoicesPresented>(@event));
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueChoiceActivated>());
        Assert.Empty(harness.Dispatcher.Events.OfType<DialogueClosed>());
        Assert.Empty(harness.Dispatcher.Failures);
    }

    private static IReadOnlyList<string> ExpectedProbeAssembly =>
    [
        "use32",
        "pushfd",
        "push eax\npush ecx\npush edx",
        "push ecx",
        "call dword [managed_callback]",
        "add esp, 4",
        "pop edx\npop ecx\npop eax",
        "popfd",
    ];

    private static Harness CreateHarness() => new();

    private static VerifiedBuild CreateBuild(
        nuint imageBase = ImageBase,
        HookId? missing = null)
    {
        var addresses = ExpectedHookIds
            .Where(id => id != missing)
            .ToDictionary(id => id, id => checked(imageBase + GameVersionCatalog.Get(id).Rva));
        return new VerifiedBuild(imageBase, addresses);
    }

    private static void OpenChoices(Harness harness, int selectedIndex)
    {
        harness.Memory.SetChoices(Window, "Prompt", ["Yes", "No"], selectedIndex);
        harness.Open();
    }

    private static void AssertChoices(
        AccessibilityEvent accessibilityEvent,
        IReadOnlyList<string> expected,
        int selectedIndex)
    {
        var presented = Assert.IsType<DialogueChoicesPresented>(accessibilityEvent);
        Assert.Equal(expected, presented.Choices);
        Assert.Equal(selectedIndex, presented.SelectedIndex);
    }

    private sealed class Harness
    {
        public Harness()
        {
            Functions = new RecordingFunctionHookFactory(Lifecycle);
            Assembly = new RecordingAsmHookFactory(Lifecycle);
            Set = new DialogueHookSet(Functions, Assembly, Memory, Dispatcher);
            Installer = new ReloadedHookInstaller(Set.Registrations, [Set]);
        }

        public List<string> Lifecycle { get; } = [];
        public DialogueMemory Memory { get; } = new();
        public RecordingDispatcher Dispatcher { get; } = new();
        public RecordingLog Log { get; } = new();
        public RecordingFatal Fatal { get; } = new();
        public RecordingFunctionHookFactory Functions { get; }
        public RecordingAsmHookFactory Assembly { get; }
        public DialogueHookSet Set { get; }
        public ReloadedHookInstaller Installer { get; }
        public UnmanagedBoundaryGuard Boundary =>
            boundary ??= new UnmanagedBoundaryGuard(Log, Fatal);
        private UnmanagedBoundaryGuard? boundary;

        public void PrepareAndActivate()
        {
            Installer.PrepareAll(CreateBuild(), Boundary);
            Installer.ActivateAll();
        }

        public void Open(uint rawWord = 0) =>
            Functions.GetDetour<MsgWindowOpenDelegate>(HookId.MsgWindowOpen)((nint)Window, rawWord);

        public void Update(uint rawWord = 0) =>
            Functions.GetDetour<MsgWindowUpdateDelegate>(HookId.MsgWindowUpdate)((nint)Window, rawWord);

        public void Close(uint rawWord = 0) =>
            Functions.GetDetour<MsgWindowCloseDelegate>(HookId.MsgWindowClose)((nint)Window, rawWord);
    }

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class RecordingFunctionHookFactory(List<string> lifecycle) : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals = new()
        {
            [HookId.MsgWindowOpen] = (MsgWindowOpenDelegate)((_, _) => { }),
            [HookId.MsgWindowUpdate] = (MsgWindowUpdateDelegate)((_, _) => { }),
            [HookId.MsgWindowClose] = (MsgWindowCloseDelegate)((_, _) => { }),
        };
        private readonly Dictionary<HookId, Delegate> detours = [];

        public List<(HookId Id, nuint Address)> Created { get; } = [];
        public List<IRecordingHook> Hooks { get; } = [];

        public void SetOriginal<TDelegate>(HookId id, TDelegate original)
            where TDelegate : Delegate => originals[id] = original;

        public TDelegate GetDetour<TDelegate>(HookId id)
            where TDelegate : Delegate => (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            Created.Add((id, address));
            detours[id] = detour;
            var hook = new RecordingHook<TDelegate>(id, (TDelegate)originals[id], lifecycle);
            Hooks.Add(hook);
            return hook;
        }
    }

    private interface IRecordingHook : IHook
    {
        int ActivateCount { get; }
    }

    private sealed class RecordingHook<TDelegate>(
        HookId id,
        TDelegate original,
        List<string> lifecycle) : IHook<TDelegate>, IRecordingHook
        where TDelegate : Delegate
    {
        public TDelegate OriginalFunction { get; } = original;
        public IReverseWrapper<TDelegate> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 1;
        public nint OriginalFunctionWrapperAddress => 2;
        public int ActivateCount { get; private set; }

        public IHook<TDelegate> Activate()
        {
            ActivateCount++;
            IsHookActivated = true;
            IsHookEnabled = true;
            lifecycle.Add($"activate:{id}");
            return this;
        }

        IHook IHook.Activate() => Activate();

        public void Enable() => IsHookEnabled = true;

        public void Disable()
        {
            lifecycle.Add($"disable:{id}");
            IsHookEnabled = false;
        }
    }

    private sealed class RecordingAsmHookFactory(List<string> lifecycle) : IRuntimeNativeAsmHookFactory
    {
        public HookId Id { get; private set; }
        public nuint Address { get; private set; }
        public RuntimeAsmHookOptions Options { get; private set; }
        public IReadOnlyList<string> Code { get; private set; } = [];
        public Delegate Callback { get; private set; } = null!;
        public object ReverseWrapper { get; } = new();
        public RecordingNativeAsmHook NativeHook { get; } = new(lifecycle);
        public ReloadedPreparedAsmHook Prepared { get; private set; } = null!;

        public IPreparedHook CreateAsmHook<TDelegate>(
            HookId id,
            string name,
            TDelegate callback,
            nuint address,
            Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly,
            RuntimeAsmHookOptions options)
            where TDelegate : Delegate
        {
            Id = id;
            Address = address;
            Options = options;
            Callback = callback;
            Code = new ReadOnlyCollection<string>(buildAssembly(new RuntimeAsmHookAssemblyContext(
                "call dword [managed_callback]",
                "push eax\npush ecx\npush edx",
                "pop edx\npop ecx\npop eax")).ToArray());
            Prepared = new ReloadedPreparedAsmHook(name, NativeHook, [callback, ReverseWrapper, Code]);
            return Prepared;
        }

        public void Invoke(nuint window) =>
            Assert.IsType<MsgWindowChoiceConfirmProbeDelegate>(Callback)((nint)window);
    }

    private sealed class RecordingNativeAsmHook(List<string> lifecycle) : IAsmHook
    {
        public bool IsEnabled { get; private set; }
        public int ActivateCount { get; private set; }
        public int EnableCount { get; private set; }

        public IAsmHook Activate()
        {
            ActivateCount++;
            lifecycle.Add("activate:asm");
            return this;
        }

        public void Enable()
        {
            EnableCount++;
            IsEnabled = true;
        }

        public void Disable()
        {
            lifecycle.Add("disable:asm");
            IsEnabled = false;
        }
    }

    private sealed class DialogueMemory : IReadableMemory
    {
        private readonly object gate = new();
        private readonly Dictionary<nuint, byte[]> segments = [];
        public bool ThrowOnRead { get; set; }

        public void SetOrdinary(
            nuint window,
            IReadOnlyList<string> strings,
            int cursor,
            int pageBase) =>
            SetSnapshot(window, strings, Enumerable.Repeat(0u, strings.Count).ToArray(),
                cursor, pageBase, phase: 0, choiceCount: 0, selectedIndex: -1, active: 1);

        public void SetWaiting(nuint window, IReadOnlyList<string> strings) =>
            SetSnapshot(window, strings, Enumerable.Repeat(0u, strings.Count).ToArray(),
                cursor: 0, pageBase: 0, phase: 1, choiceCount: 0, selectedIndex: -1, active: 1);

        public void SetChoices(
            nuint window,
            string prompt,
            IReadOnlyList<string> labels,
            int selectedIndex)
        {
            var strings = new[] { prompt }.Concat(labels).ToArray();
            var flags = new uint[strings.Length];
            for (var index = 1; index < flags.Length; index++)
            {
                flags[index] = 0x10;
            }
            SetSnapshot(window, strings, flags, cursor: strings.Length, pageBase: 0,
                phase: 4, choiceCount: labels.Count, selectedIndex, active: 1);
        }

        public void SetInactive(nuint window) =>
            SetSnapshot(window, ["Inactive"], [0], cursor: 0, pageBase: 0,
                phase: 0, choiceCount: 0, selectedIndex: -1, active: 0);

        public void SetInvalidActive(nuint window, byte active)
        {
            Assert.NotEqual((byte)0, active);
            Assert.NotEqual((byte)1, active);
            SetSnapshot(window, ["Invalid"], [0], cursor: 0, pageBase: 0,
                phase: 0, choiceCount: 0, selectedIndex: -1, active);
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            lock (gate)
            {
                if (ThrowOnRead)
                {
                    throw new InvalidOperationException("simulated dialogue memory failure");
                }
                if (destination.IsEmpty)
                {
                    return true;
                }

                foreach (var (segmentAddress, bytes) in segments)
                {
                    if (address < segmentAddress)
                    {
                        continue;
                    }
                    var offset = (ulong)address - segmentAddress;
                    if (offset + (uint)destination.Length > (uint)bytes.Length)
                    {
                        continue;
                    }
                    bytes.AsSpan(checked((int)offset), destination.Length).CopyTo(destination);
                    return true;
                }
                return false;
            }
        }

        public void SetSnapshot(
            nuint window,
            IReadOnlyList<string> strings,
            IReadOnlyList<uint> flags,
            int cursor,
            int pageBase,
            int phase,
            int choiceCount,
            int selectedIndex,
            byte active)
        {
            Assert.Equal(strings.Count, flags.Count);
            var stringBase = checked(window + 0x1000);
            var flagsBase = checked(window + 0x4000);
            var objectBytes = new byte[DialogueCapture.ObjectSize];
            WriteUInt32(objectBytes, 0, checked((uint)(ImageBase + DialogueCapture.VtableRva)));
            objectBytes[DialogueCapture.ActiveOffset] = active;
            WriteInt32(objectBytes, DialogueCapture.CurrentLineOffset, cursor);
            WriteInt32(objectBytes, DialogueCapture.PageBaseOffset, pageBase);
            WriteInt32(objectBytes, DialogueCapture.PhaseOffset, phase);
            WriteInt32(objectBytes, DialogueCapture.SelectedChoiceOffset, selectedIndex);
            WriteInt32(objectBytes, DialogueCapture.ChoiceCountOffset, choiceCount);
            WriteVectorHeader(
                objectBytes,
                DialogueCapture.ParsedStringsOffset,
                stringBase,
                checked((uint)(strings.Count * MsvcStringReader.LayoutSize)));
            WriteVectorHeader(
                objectBytes,
                DialogueCapture.FlagsOffset,
                flagsBase,
                checked((uint)(flags.Count * sizeof(uint))));

            lock (gate)
            {
                segments[window] = objectBytes;
                for (var index = 0; index < strings.Count; index++)
                {
                    var address = checked(stringBase + (nuint)(index * MsvcStringReader.LayoutSize));
                    var encoded = Encoding.UTF8.GetBytes(strings[index]);
                    if (encoded.Length < 16)
                    {
                        segments[address] = CreateInlineString(strings[index]);
                    }
                    else
                    {
                        var dataAddress = checked(window + 0x8000u + (nuint)(index * 0x1000));
                        var layout = new byte[MsvcStringReader.LayoutSize];
                        WriteUInt32(layout, 0, checked((uint)dataAddress));
                        WriteUInt32(layout, 0x10, checked((uint)encoded.Length));
                        WriteUInt32(layout, 0x14, checked((uint)encoded.Length));
                        segments[address] = layout;
                        segments[dataAddress] = encoded;
                    }
                }
                var flagBytes = new byte[flags.Count * sizeof(uint)];
                for (var index = 0; index < flags.Count; index++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        flagBytes.AsSpan(index * sizeof(uint)),
                        flags[index]);
                }
                segments[flagsBase] = flagBytes;
            }
        }

        private static byte[] CreateInlineString(string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            Assert.True(encoded.Length < 16, "Test dialogue strings must use the audited inline MSVC layout.");
            var bytes = new byte[MsvcStringReader.LayoutSize];
            encoded.CopyTo(bytes, 0);
            WriteUInt32(bytes, 0x10, checked((uint)encoded.Length));
            WriteUInt32(bytes, 0x14, 15);
            return bytes;
        }

        private static void WriteVectorHeader(byte[] bytes, int offset, nuint begin, uint byteLength)
        {
            WriteUInt32(bytes, offset, checked((uint)begin));
            WriteUInt32(bytes, offset + 4, checked((uint)(begin + byteLength)));
            WriteUInt32(bytes, offset + 8, checked((uint)(begin + byteLength)));
        }

        private static void WriteInt32(byte[] bytes, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);

        private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }

    private sealed class RecordingDispatcher : ISemanticEventDispatcher
    {
        private readonly object gate = new();
        public int Generation => 0;
        public bool ThrowOnPublish { get; set; }
        public bool ThrowOnCoverageFailure { get; set; }
        public List<AccessibilityEvent> Events { get; } = [];
        public List<string> Failures { get; } = [];
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }

        public void Publish(AccessibilityEvent accessibilityEvent)
        {
            if (ThrowOnPublish)
            {
                throw new InvalidOperationException("simulated dispatcher failure");
            }
            lock (gate)
            {
                Events.Add(accessibilityEvent);
            }
        }

        public void ReportCoverageFailure(string message)
        {
            lock (gate)
            {
                Failures.Add(message);
            }
            if (ThrowOnCoverageFailure)
            {
                throw new InvalidOperationException("simulated coverage reporter failure");
            }
        }
    }

    private sealed class RecordingLog : IModLog
    {
        public List<string> Errors { get; } = [];
        public void Info(string message) { }
        public void Error(string message) => Errors.Add(message);
    }

    private sealed class RecordingFatal : IAccessibleFatalError
    {
        public List<string> Messages { get; } = [];
        public void Show(string message) => Messages.Add(message);
    }
}
