using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Startup;
using ChronoTriggerAccessibility.Core.State;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

public sealed class StartupTitleHookSetTests
{
    private const nuint ImageBase = 0x400000;

    private static readonly HookId[] ExpectedHookIds =
    [
        HookId.TextManagerGetMsg,
        HookId.SceneManagerCreate,
        HookId.SceneManagerNextScene,
        HookId.TitleMenuModeEnter,
        HookId.TitleRowFactory,
        HookId.TitleSceneUpdate,
        HookId.NsMenuFocusSetter,
        HookId.TitleMenuCallback,
    ];

    [Fact]
    public void ProductionSetPreparesAllEightVerifiedAddressesInactiveThenSnapshotsAfterActivation()
    {
        var factory = new RecordingHookFactory();
        var memory = new TestMemory().AddInt32(
            ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva,
            StartupTitleHookSet.SquareEnixSceneId);
        var dispatcher = new RecordingDispatcher();
        var set = new StartupTitleHookSet(factory, memory, dispatcher, new OpeningMovieTimeline());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        var build = new VerifiedBuild(ImageBase, ExpectedHookIds.ToDictionary(
            id => id,
            id => ImageBase + GameVersionCatalog.Get(id).Rva));

        installer.PrepareAll(build, CreateBoundary());

        Assert.Equal(ExpectedHookIds, set.RequiredHookIds);
        Assert.Equal(ExpectedHookIds, factory.Created.Select(created => created.Id));
        Assert.Equal(
            ExpectedHookIds.Select(id => build.HookAddresses[id]),
            factory.Created.Select(created => created.Address));
        Assert.All(installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.True(installer.LifetimeRootCount >= ExpectedHookIds.Length * 2);

        installer.ActivateAll();

        Assert.All(installer.PreparedHooks, hook => Assert.True(hook.IsActive));
        Assert.Contains(
            dispatcher.Events,
            accessibilityEvent => accessibilityEvent ==
                new StartupSceneEntered(StartupSceneKind.SquareEnixLogo));
    }

    [Fact]
    public void ModProductionCompositionUsesTheCompleteHookSetAndActivationObserver()
    {
        var factory = new RecordingHookFactory();
        var memory = new TestMemory().AddInt32(
            ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva,
            StartupTitleHookSet.SquareEnixSceneId);
        var dispatcher = new RecordingDispatcher();

        var composition = ChronoTriggerAccessibility.Mod.Mod.CreateStartupTitleComposition(
            factory,
            memory,
            dispatcher,
            new OpeningMovieTimeline());
        composition.Installer.PrepareAll(CreateBuild(), CreateBoundary());
        composition.Installer.ActivateAll();

        Assert.Equal(ExpectedHookIds, composition.HookSet.RequiredHookIds);
        Assert.Equal(ExpectedHookIds.Length, composition.Installer.PreparedHooks.Count);
        Assert.Contains(dispatcher.Events, item => item ==
            new StartupSceneEntered(StartupSceneKind.SquareEnixLogo));
    }

    [Fact]
    public void ProductionPrepareFailureLeavesEveryEarlierNativeHookInactive()
    {
        var factory = new RecordingHookFactory { FailAtCreation = 5 };
        var set = new StartupTitleHookSet(
            factory,
            new TestMemory(),
            new RecordingDispatcher(),
            new OpeningMovieTimeline());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        var build = new VerifiedBuild(ImageBase, ExpectedHookIds.ToDictionary(
            id => id,
            id => ImageBase + GameVersionCatalog.Get(id).Rva));

        Assert.Throws<InvalidOperationException>(() =>
            installer.PrepareAll(build, CreateBoundary()));

        Assert.All(factory.Hooks, hook => Assert.False(hook.IsHookEnabled));
        Assert.All(installer.PreparedHooks, hook => Assert.False(hook.IsActive));
    }

    [Fact]
    public void UnreadablePostActivationSceneSnapshotRollsBackEveryProductionHook()
    {
        var factory = new RecordingHookFactory();
        var dispatcher = new RecordingDispatcher();
        var set = new StartupTitleHookSet(
            factory,
            new TestMemory(),
            dispatcher,
            new OpeningMovieTimeline());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        Assert.Throws<InvalidOperationException>(installer.ActivateAll);

        Assert.All(installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.Single(dispatcher.Failures);
        Assert.Contains("current", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreparedProductionHooksRetainEveryHookAndDetourAcrossCollection()
    {
        var factory = new RecordingHookFactory();
        var set = new StartupTitleHookSet(
            factory,
            new TestMemory(),
            new RecordingDispatcher(),
            new OpeningMovieTimeline());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        var build = new VerifiedBuild(ImageBase, ExpectedHookIds.ToDictionary(
            id => id,
            id => ImageBase + GameVersionCatalog.Get(id).Rva));
        installer.PrepareAll(build, CreateBoundary());
        var roots = factory.DetourReferences.ToArray();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.All(roots, reference => Assert.True(reference.IsAlive));
        Assert.Equal(ExpectedHookIds.Length, installer.PreparedHooks.Count);
        Assert.All(installer.PreparedHooks, hook => Assert.NotEmpty(hook.LifetimeRoots));
    }

    [Fact]
    public void DynamicTitleHooksCaptureSparseRowsFocusAndActivationFromNativeState()
    {
        const nuint scene = 0xA000;
        const nuint mode = 0xB000;
        const nuint manager = 0xC000;
        const nuint closure = 0xD000;
        const nuint eventPointer = 0xE000;
        const nuint actionPointer = 0xE100;
        const nuint rowBase = 0x12000;
        var memory = new TestMemory()
            .AddPointer(scene, ImageBase + 0x3B806C)
            .AddPointer(scene + 0x290, mode)
            .AddPointer(mode, ImageBase + 0x3B7FA8)
            .AddPointer(mode + 4, scene)
            .AddPointer(mode + 0x10, manager)
            .AddPointer(manager, ImageBase + 0x3A5D0C)
            .AddInt32(manager + 0x2C4, 1)
            .AddPointer(closure + 4, mode)
            .AddInt32(eventPointer, 0)
            .AddInt32(actionPointer, 99);
        var labels = new Dictionary<int, string>
        {
            [0] = "Resume",
            [1] = "New Game",
            [2] = "Load Game",
            [4] = "Extras",
            [5] = "Settings",
            [6] = "Quit",
        };
        foreach (var (rawKey, label) in labels)
        {
            memory.AddInlineString(rowBase + (nuint)(rawKey * 0x1C), label);
        }

        var dispatcher = new RecordingDispatcher { ReduceEvents = true };
        var factory = new RecordingHookFactory();
        var getMsgCalls = 0;
        var rowFactoryCalls = 0;
        var enterCalls = 0;
        var focusCalls = 0;
        var callbackCalls = 0;
        factory.SetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (_, result, _, _) => { getMsgCalls++; return result; });
        factory.SetOriginal<TitleRowFactoryDelegate>(HookId.TitleRowFactory,
            record => { rowFactoryCalls++; return (nint)(record + 0x8000); });
        factory.SetOriginal<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter,
            (_, rawKey) => { focusCalls++; memory.AddInt32(manager + 0x2C4, rawKey); });
        factory.SetOriginal<TitleMenuCallbackDelegate>(HookId.TitleMenuCallback,
            (_, _, _) =>
            {
                callbackCalls++;
                dispatcher.Publish(new ScreenExited(ScreenKind.TitleMenu));
            });
        factory.SetOriginal<TitleMenuModeEnterDelegate>(HookId.TitleMenuModeEnter, _ =>
        {
            enterCalls++;
            var getMsg = factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg);
            getMsg(0x7000, (nint)rowBase, 0x41, 3);
            getMsg(0x7000, (nint)(rowBase + 0x1C), 0x41, 0);
            getMsg(0x7000, (nint)(rowBase + 0x38), 0x41, 1);
            var rowFactory = factory.GetDetour<TitleRowFactoryDelegate>(HookId.TitleRowFactory);
            foreach (var rawKey in new[] { 1, 4, 5, 6 })
            {
                rowFactory((nint)(rowBase + (nuint)(rawKey * 0x1C)));
            }
        });
        var set = new StartupTitleHookSet(factory, memory, dispatcher, new OpeningMovieTimeline());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        factory.GetDetour<TitleMenuModeEnterDelegate>(HookId.TitleMenuModeEnter)((nint)mode);
        factory.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)((nint)manager, 4);
        factory.GetDetour<TitleMenuCallbackDelegate>(HookId.TitleMenuCallback)(
            (nint)closure,
            (nint)eventPointer,
            (nint)actionPointer);

        memory.ThrowOnRead = true;
        factory.GetDetour<TitleMenuCallbackDelegate>(HookId.TitleMenuCallback)(
            (nint)closure,
            (nint)eventPointer,
            (nint)actionPointer);

        Assert.Equal(1, enterCalls);
        Assert.Equal(3, getMsgCalls);
        Assert.Equal(4, rowFactoryCalls);
        Assert.Equal(1, focusCalls);
        Assert.Equal(2, callbackCalls);
        Assert.Contains(dispatcher.Events, item => item == new ScreenEntered(ScreenKind.TitleMenu));
        Assert.Contains(dispatcher.Events, item => item == new FocusChanged("New Game", 1, 4, false));
        Assert.Contains(dispatcher.Events, item => item == new FocusChanged("Extras", 2, 4, false));
        Assert.Contains(dispatcher.Events, item => item == new ControlActivated("Extras"));
        Assert.Single(dispatcher.Announcements, text => text == "Extras selected.");
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void PreCallCaptureFailuresStillCallTitleEnterRowFactoryAndNextSceneOriginalExactlyOnce()
    {
        var memory = new TestMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var enterCalls = 0;
        var rowCalls = 0;
        var nextCalls = 0;
        factory.SetOriginal<TitleMenuModeEnterDelegate>(HookId.TitleMenuModeEnter, _ => enterCalls++);
        factory.SetOriginal<TitleRowFactoryDelegate>(HookId.TitleRowFactory, _ => { rowCalls++; return 1; });
        factory.SetOriginal<SceneManagerCreateDelegate>(HookId.SceneManagerCreate, (_, _) => 0xCAFE);
        factory.SetOriginal<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene, _ => nextCalls++);
        var capture = new ThrowingTitleCapture();
        var set = new StartupTitleHookSet(
            factory,
            memory,
            dispatcher,
            new OpeningMovieTimeline(),
            capture);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        factory.GetDetour<TitleMenuModeEnterDelegate>(HookId.TitleMenuModeEnter)(0xB000);
        memory.ThrowOnRead = true;
        var rowResult = factory.GetDetour<TitleRowFactoryDelegate>(HookId.TitleRowFactory)(0x12000);
        dispatcher.ThrowOnPublish = true;
        var sceneResult = factory.GetDetour<SceneManagerCreateDelegate>(HookId.SceneManagerCreate)(
            StartupTitleHookSet.OpeningMovieSceneId,
            0);
        factory.GetDetour<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene)(123);

        Assert.Equal(1, enterCalls);
        Assert.Equal(1, rowCalls);
        Assert.Equal(1, nextCalls);
        Assert.Equal((nint)1, rowResult);
        Assert.Equal((nint)0xCAFE, sceneResult);
    }

    [Fact]
    public async Task DisablingHooksCancelsMovieTimelineBeforeItCanPublishAnotherDescription()
    {
        var delay = new CancellationObservingDelay();
        var timeline = new OpeningMovieTimeline(delay);
        var memory = new TestMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var set = new StartupTitleHookSet(factory, memory, dispatcher, timeline);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        factory.GetDetour<SceneManagerCreateDelegate>(HookId.SceneManagerCreate)(
            StartupTitleHookSet.OpeningMovieSceneId,
            0);
        Assert.Single(dispatcher.Events.OfType<TimedDescription>());

        installer.DisableAll();
        await delay.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(dispatcher.Events.OfType<TimedDescription>());
    }

    [Fact]
    public async Task NextSceneThatKeepsOpeningMoviePreservesGenerationAndTimelineContinuity()
    {
        var delay = new TwoStageMovieDelay();
        var memory = new TestMemory().AddInt32(
            ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva,
            StartupTitleHookSet.OpeningMovieSceneId);
        var dispatcher = new RecordingDispatcher { ReduceEvents = true };
        var factory = new RecordingHookFactory();
        var originalCalls = 0;
        factory.SetOriginal<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene, _ => originalCalls++);
        var set = new StartupTitleHookSet(factory, memory, dispatcher, new OpeningMovieTimeline(delay));
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        factory.GetDetour<SceneManagerCreateDelegate>(HookId.SceneManagerCreate)(
            StartupTitleHookSet.OpeningMovieSceneId,
            0);
        await delay.FirstWaitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        factory.GetDetour<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene)(0x10203040);

        Assert.Equal(1, originalCalls);
        Assert.Single(
            dispatcher.Events.OfType<StartupSceneEntered>(),
            item => item.Scene == StartupSceneKind.OpeningMovie);
        Assert.Single(dispatcher.Events.OfType<TimedDescription>());
        Assert.Equal(0, delay.CancellationCount);

        delay.ReleaseFirstWait.TrySetResult();
        await delay.SecondWaitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var descriptions = dispatcher.Events.OfType<TimedDescription>().ToArray();
        Assert.Equal(2, descriptions.Length);
        Assert.Equal(descriptions[0].Generation, descriptions[1].Generation);
        Assert.Equal(OpeningMovieTimeline.Entries[1].Text, descriptions[1].Text);

        installer.DisableAll();
    }

    [Fact]
    public async Task UnreadablePostNextSceneSnapshotFailsCoverageAndInvalidatesMovie()
    {
        var delay = new CancellationObservingDelay();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var set = new StartupTitleHookSet(
            factory,
            new TestMemory(),
            dispatcher,
            new OpeningMovieTimeline(delay));
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        factory.GetDetour<SceneManagerCreateDelegate>(HookId.SceneManagerCreate)(
            StartupTitleHookSet.OpeningMovieSceneId,
            0);
        factory.GetDetour<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene)(0x99887766);
        await delay.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(dispatcher.Failures);
        Assert.Contains("unreadable", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dispatcher.Events, item => item == new ScreenExited(ScreenKind.OpeningMovie));
        Assert.Single(dispatcher.Events.OfType<TimedDescription>());
    }

    [Fact]
    public void NextSceneThatKeepsTitlePreservesSparseMenuMapForSubsequentFocus()
    {
        const nuint scene = 0xA000;
        const nuint mode = 0xB000;
        const nuint manager = 0xC000;
        const nuint rowBase = 0x12000;
        var memory = new TestMemory()
            .AddInt32(ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva, StartupTitleHookSet.TitleSceneId)
            .AddPointer(scene, ImageBase + TitleSceneInspector.TitleSceneVtableRva)
            .AddPointer(scene + TitleSceneInspector.CurrentModeOffset, mode)
            .AddPointer(mode, ImageBase + TitleSceneInspector.TitleMenuModeVtableRva)
            .AddPointer(mode + TitleSceneInspector.OwnerOffset, scene)
            .AddPointer(mode + TitleSceneInspector.ManagerOffset, manager)
            .AddPointer(manager, ImageBase + TitleSceneInspector.ManagerVtableRva)
            .AddInt32(manager + TitleSceneInspector.ManagerFocusOffset, 1)
            .AddInlineString(rowBase, "Resume")
            .AddInlineString(rowBase + TitleCapture.RowStride, "New Game")
            .AddInlineString(rowBase + (4 * TitleCapture.RowStride), "Extras");
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var nextCalls = 0;
        factory.SetOriginal<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene, _ => nextCalls++);
        factory.SetOriginal<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter, (_, rawKey) =>
            memory.AddInt32(manager + TitleSceneInspector.ManagerFocusOffset, rawKey));
        factory.SetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (_, result, _, _) => result);
        factory.SetOriginal<TitleRowFactoryDelegate>(HookId.TitleRowFactory,
            record => record + 0x8000);
        factory.SetOriginal<TitleMenuModeEnterDelegate>(HookId.TitleMenuModeEnter, _ =>
        {
            var getMsg = factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg);
            getMsg(0x7000, (nint)rowBase, 0x41, 3);
            getMsg(0x7000, (nint)(rowBase + TitleCapture.RowStride), 0x41, 0);
            var rowFactory = factory.GetDetour<TitleRowFactoryDelegate>(HookId.TitleRowFactory);
            rowFactory((nint)(rowBase + TitleCapture.RowStride));
            rowFactory((nint)(rowBase + (4 * TitleCapture.RowStride)));
        });
        var set = new StartupTitleHookSet(factory, memory, dispatcher, new OpeningMovieTimeline());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        factory.GetDetour<SceneManagerCreateDelegate>(HookId.SceneManagerCreate)(
            StartupTitleHookSet.TitleSceneId,
            0);
        factory.GetDetour<TitleMenuModeEnterDelegate>(HookId.TitleMenuModeEnter)((nint)mode);
        factory.GetDetour<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene)(0x55667788);
        factory.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)((nint)manager, 4);

        Assert.Equal(1, nextCalls);
        Assert.Contains(dispatcher.Events, item => item == new FocusChanged("Extras", 2, 2, false));
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public async Task TitleUpdateReturningAfterDisableAndDetachCannotPublishOrReportFatal()
    {
        const nuint scene = 0xA000;
        const nuint mode = 0xB000;
        var memory = new TestMemory()
            .AddInt32(ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva, StartupTitleHookSet.TitleSceneId)
            .AddPointer(scene, ImageBase + TitleSceneInspector.TitleSceneVtableRva)
            .AddPointer(scene + TitleSceneInspector.CurrentModeOffset, mode)
            .AddPointer(mode, ImageBase + TitleSceneInspector.TapToStartVtableRva);
        var log = new RecordingLog();
        var semanticFatal = new RecordingFatal();
        var boundaryFatal = new RecordingFatal();
        var session = new StrictPrismSession();
        var dispatcher = new SemanticEventDispatcher(log, semanticFatal);
        dispatcher.Attach(session);
        var originalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOriginal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalCalls = 0;
        var factory = new RecordingHookFactory();
        factory.SetOriginal<TitleSceneUpdateDelegate>(HookId.TitleSceneUpdate, (_, _) =>
        {
            Interlocked.Increment(ref originalCalls);
            originalEntered.TrySetResult();
            releaseOriginal.Task.GetAwaiter().GetResult();
        });
        var set = new StartupTitleHookSet(factory, memory, dispatcher, new OpeningMovieTimeline());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), new UnmanagedBoundaryGuard(log, boundaryFatal));
        installer.ActivateAll();
        session.Outputs.Clear();
        log.Infos.Clear();

        var update = Task.Run(() =>
            factory.GetDetour<TitleSceneUpdateDelegate>(HookId.TitleSceneUpdate)((nint)scene, 0.016f));
        await originalEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        installer.DisableAll();
        dispatcher.Detach(session);
        session.Dispose();
        releaseOriginal.TrySetResult();
        await update.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, originalCalls);
        Assert.Empty(session.Outputs);
        Assert.Equal(0, session.OutputAttemptsAfterDispose);
        Assert.DoesNotContain(log.Infos, line => line.Contains("Semantic event", StringComparison.Ordinal));
        Assert.Empty(semanticFatal.Messages);
        Assert.Empty(boundaryFatal.Messages);
    }

    [Fact]
    public async Task DelayedMovieItemRacingDisableCannotPublishAfterDispatcherDetach()
    {
        var delay = new GenerationRaceMovieDelay();
        var dispatcher = new GenerationRaceDispatcher();
        var session = new StrictPrismSession();
        dispatcher.Attach(session);
        var memory = new TestMemory();
        var factory = new RecordingHookFactory();
        var set = new StartupTitleHookSet(factory, memory, dispatcher, new OpeningMovieTimeline(delay));
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        factory.GetDetour<SceneManagerCreateDelegate>(HookId.SceneManagerCreate)(
            StartupTitleHookSet.OpeningMovieSceneId,
            0);
        Assert.Single(dispatcher.Events.OfType<TimedDescription>());
        delay.ReleaseFirstWait.TrySetResult();
        Assert.True(dispatcher.BlockedGeneration.Wait(TimeSpan.FromSeconds(2)));

        installer.DisableAll();
        dispatcher.Detach(session);
        session.Dispose();
        dispatcher.ReleaseGeneration.Set();

        var raceFinished = await Task.WhenAny(
            dispatcher.PostDetachPublish.Task,
            dispatcher.CoverageFailure.Task,
            delay.SecondWaitEntered.Task).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(delay.SecondWaitEntered.Task, raceFinished);
        Assert.Single(dispatcher.Events.OfType<TimedDescription>());
        Assert.Equal(0, dispatcher.PostDetachPublishAttempts);
        Assert.Empty(dispatcher.Failures);
        Assert.Equal(0, session.OutputAttemptsAfterDispose);
    }

    [Fact]
    public void SceneHooksPassActionUnchangedSynchronizeGlobalAndCancelOpeningOnTitleSkip()
    {
        var memory = new TestMemory().AddInt32(
            ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva,
            StartupTitleHookSet.SquareEnixSceneId);
        var factory = new RecordingHookFactory();
        var createCalls = 0;
        var nextCalls = 0;
        uint observedAction = 0;
        factory.SetOriginal<SceneManagerCreateDelegate>(HookId.SceneManagerCreate,
            (sceneId, _) => { createCalls++; return sceneId + 0x1000; });
        factory.SetOriginal<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene, action =>
        {
            nextCalls++;
            observedAction = action;
            memory.AddInt32(
                ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva,
                StartupTitleHookSet.TitleSceneId);
        });
        var dispatcher = new RecordingDispatcher();
        var set = new StartupTitleHookSet(factory, memory, dispatcher, new OpeningMovieTimeline());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());

        factory.GetDetour<SceneManagerCreateDelegate>(HookId.SceneManagerCreate)(
            StartupTitleHookSet.OpeningMovieSceneId,
            0);
        factory.GetDetour<SceneManagerNextSceneDelegate>(HookId.SceneManagerNextScene)(0xDEADBEEF);

        Assert.Equal(1, createCalls);
        Assert.Equal(1, nextCalls);
        Assert.Equal(0xDEADBEEFu, observedAction);
        Assert.Contains(dispatcher.Events, item => item ==
            new StartupSceneEntered(StartupSceneKind.OpeningMovie));
        Assert.Contains(dispatcher.Events, item => item is TimedDescription
            { Text: "Sunlight shines in a blue sky." });
        Assert.Contains(dispatcher.Events, item => item ==
            new StartupSceneEntered(StartupSceneKind.Title));
        Assert.Empty(dispatcher.Failures);
    }

    private static VerifiedBuild CreateBuild() => new(
        ImageBase,
        ExpectedHookIds.ToDictionary(
            id => id,
            id => ImageBase + GameVersionCatalog.Get(id).Rva));

    private static UnmanagedBoundaryGuard CreateBoundary() =>
        new(new RecordingLog(), new RecordingFatal());

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class RecordingHookFactory : IRuntimeNativeHookFactory
    {
        private int creationCount;
        private readonly Dictionary<HookId, Delegate> originals = new()
        {
            [HookId.TextManagerGetMsg] = (TextManagerGetMsgDelegate)((_, result, _, _) => result),
            [HookId.SceneManagerCreate] = (SceneManagerCreateDelegate)((_, _) => 0),
            [HookId.SceneManagerNextScene] = (SceneManagerNextSceneDelegate)(_ => { }),
            [HookId.TitleMenuModeEnter] = (TitleMenuModeEnterDelegate)(_ => { }),
            [HookId.TitleRowFactory] = (TitleRowFactoryDelegate)(_ => 1),
            [HookId.TitleSceneUpdate] = (TitleSceneUpdateDelegate)((_, _) => { }),
            [HookId.NsMenuFocusSetter] = (NsMenuFocusSetterDelegate)((_, _) => { }),
            [HookId.TitleMenuCallback] = (TitleMenuCallbackDelegate)((_, _, _) => { }),
        };
        private readonly Dictionary<HookId, Delegate> detours = [];
        public int? FailAtCreation { get; init; }
        public List<(HookId Id, nuint Address)> Created { get; } = [];
        public List<IHook> Hooks { get; } = [];
        public List<WeakReference> DetourReferences { get; } = [];

        public void SetOriginal<TDelegate>(HookId id, TDelegate original)
            where TDelegate : Delegate => originals[id] = original;

        public TDelegate GetDetour<TDelegate>(HookId id)
            where TDelegate : Delegate => (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(
            HookId id,
            TDelegate detour,
            nuint address)
            where TDelegate : Delegate
        {
            creationCount++;
            if (creationCount == FailAtCreation)
            {
                throw new InvalidOperationException($"simulated creation failure {creationCount}");
            }

            Created.Add((id, address));
            DetourReferences.Add(new WeakReference(detour));
            detours[id] = detour;
            var hook = new FakeHook<TDelegate>((TDelegate)originals[id]);
            Hooks.Add(hook);
            return hook;
        }
    }

    private sealed class FakeHook<TDelegate>(TDelegate original) : IHook<TDelegate>
        where TDelegate : Delegate
    {
        public TDelegate OriginalFunction { get; } = original;
        public IReverseWrapper<TDelegate> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public IntPtr OriginalFunctionAddress => (IntPtr)1;
        public IntPtr OriginalFunctionWrapperAddress => (IntPtr)1;
        public IHook<TDelegate> Activate()
        {
            IsHookActivated = true;
            IsHookEnabled = true;
            return this;
        }
        IHook IHook.Activate() => Activate();
        public void Disable() => IsHookEnabled = false;
        public void Enable() => IsHookEnabled = true;
    }

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];
        public bool ThrowOnRead { get; set; }
        public TestMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            segments[address] = bytes;
            return this;
        }

        public TestMemory AddPointer(nuint address, nuint value) =>
            AddInt32(address, checked((int)value));

        public TestMemory AddInlineString(nuint address, string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            Assert.True(encoded.Length < 16);
            var bytes = new byte[24];
            encoded.CopyTo(bytes, 0);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10), encoded.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x14), 15);
            segments[address] = bytes;
            return this;
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("simulated capture memory failure");
            }

            if (!segments.TryGetValue(address, out var bytes) || bytes.Length < destination.Length)
            {
                return false;
            }
            bytes.AsSpan(0, destination.Length).CopyTo(destination);
            return true;
        }
    }

    private sealed class RecordingDispatcher : ISemanticEventDispatcher
    {
        private readonly AccessibilityState reducedState = new();
        private int generation;
        public int Generation => ReduceEvents ? reducedState.Generation : generation;
        public bool ThrowOnPublish { get; set; }
        public bool ReduceEvents { get; set; }
        public List<AccessibilityEvent> Events { get; } = [];
        public List<string> Announcements { get; } = [];
        public List<string> Failures { get; } = [];
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent accessibilityEvent)
        {
            if (ThrowOnPublish)
            {
                throw new InvalidOperationException("simulated semantic capture failure");
            }
            Events.Add(accessibilityEvent);
            if (ReduceEvents)
            {
                Announcements.AddRange(reducedState.Apply(accessibilityEvent).Select(item => item.Text));
            }
            if (accessibilityEvent is StartupSceneEntered(StartupSceneKind.Title))
            {
                generation++;
            }
        }
        public void ReportCoverageFailure(string message) => Failures.Add(message);
    }

    private sealed class RecordingLog : IModLog
    {
        public List<string> Infos { get; } = [];
        public List<string> Errors { get; } = [];
        public void Info(string message) => Infos.Add(message);
        public void Error(string message) => Errors.Add(message);
    }

    private sealed class RecordingFatal : IAccessibleFatalError
    {
        public List<string> Messages { get; } = [];
        public void Show(string message) => Messages.Add(message);
    }

    private sealed class ThrowingTitleCapture : ITitleCapture
    {
        public bool IsBuilderActive => true;
        public IDisposable BeginBuilder() => throw new InvalidOperationException("simulated builder failure");
        public void ObserveLocalizedResult(int fileId, int messageId, nuint resultAddress, string label) { }
        public void ObserveConstructedRow(nuint recordAddress, nuint rowControl, string label) { }
        public void ReportBuilderError(string error) { }
        public bool TryCompleteBuilder(out TitleMenuSnapshot snapshot, out string error)
        {
            snapshot = null!;
            error = "simulated builder failure";
            return false;
        }
    }

    private sealed class CancellationObservingDelay : IOpeningMovieDelay
    {
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class TwoStageMovieDelay : IOpeningMovieDelay
    {
        private int callCount;
        private int cancellationCount;
        public int CancellationCount => Volatile.Read(ref cancellationCount);
        public TaskCompletionSource FirstWaitEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstWait { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondWaitEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1)
            {
                FirstWaitEntered.TrySetResult();
                try
                {
                    await ReleaseFirstWait.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancellationCount);
                    throw;
                }
                return;
            }

            SecondWaitEntered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancellationCount);
                throw;
            }
        }
    }

    private sealed class GenerationRaceMovieDelay : IOpeningMovieDelay
    {
        private int callCount;
        public TaskCompletionSource ReleaseFirstWait { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondWaitEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                await ReleaseFirstWait.Task;
                return;
            }

            SecondWaitEntered.TrySetResult();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class GenerationRaceDispatcher : ISemanticEventDispatcher
    {
        private int generation;
        private int generationReads;
        private int attached;
        private int postDetachPublishAttempts;
        public int PostDetachPublishAttempts => Volatile.Read(ref postDetachPublishAttempts);
        public ManualResetEventSlim BlockedGeneration { get; } = new(false);
        public ManualResetEventSlim ReleaseGeneration { get; } = new(false);
        public TaskCompletionSource PostDetachPublish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CoverageFailure { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<AccessibilityEvent> Events { get; } = [];
        public List<string> Failures { get; } = [];

        public int Generation
        {
            get
            {
                var captured = Volatile.Read(ref generation);
                if (Interlocked.Increment(ref generationReads) == 4)
                {
                    BlockedGeneration.Set();
                    if (!ReleaseGeneration.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Generation race was not released.");
                    }
                }
                return captured;
            }
        }

        public void Attach(IRuntimePrismSession session) => Volatile.Write(ref attached, 1);
        public void Detach(IRuntimePrismSession session) => Volatile.Write(ref attached, 0);

        public void Publish(AccessibilityEvent accessibilityEvent)
        {
            if (Volatile.Read(ref attached) == 0)
            {
                Interlocked.Increment(ref postDetachPublishAttempts);
                PostDetachPublish.TrySetResult();
                throw new InvalidOperationException("semantic publish attempted after detach");
            }

            lock (Events)
            {
                Events.Add(accessibilityEvent);
            }
            if (accessibilityEvent is ScreenExited(ScreenKind.OpeningMovie))
            {
                Interlocked.Increment(ref generation);
            }
        }

        public void ReportCoverageFailure(string message)
        {
            lock (Failures)
            {
                Failures.Add(message);
            }
            CoverageFailure.TrySetResult();
        }
    }

    private sealed class StrictPrismSession : IRuntimePrismSession
    {
        private int disposed;
        private int attemptsAfterDispose;
        public string BackendName => "Strict test backend";
        public int OutputAttemptsAfterDispose => Volatile.Read(ref attemptsAfterDispose);
        public List<(string Text, bool Interrupt)> Outputs { get; } = [];

        public void Output(string text, bool interrupt)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                Interlocked.Increment(ref attemptsAfterDispose);
                throw new ObjectDisposedException(nameof(StrictPrismSession));
            }
            Outputs.Add((text, interrupt));
        }

        public void Dispose() => Volatile.Write(ref disposed, 1);
    }
}
