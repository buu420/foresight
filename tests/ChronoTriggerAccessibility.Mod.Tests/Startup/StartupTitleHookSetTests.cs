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
            { Text: "A silver pendant spins in sunlight above the ocean." });
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
        public void Info(string message) { }
        public void Error(string message) { }
    }

    private sealed class RecordingFatal : IAccessibleFatalError
    {
        public void Show(string message) { }
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
}
