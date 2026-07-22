using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Startup;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Startup;

public sealed class StartupTitleHookSet : IHookActivationObserver
{
    public const uint CurrentSceneGlobalRva = 0x41C3E8;
    public const int SquareEnixSceneId = 2;
    public const int TitleSceneId = 3;
    public const int OpeningMovieSceneId = 0x1E;

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly OpeningMovieTimeline movieTimeline;
    private readonly MsvcStringReader stringReader;
    private readonly LocalizedTextCache localizedText = new();
    private readonly ITitleCapture titleCapture;
    private readonly object titleGate = new();
    private readonly object movieGate = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private TitleSceneInspector? titleInspector;
    private TitleMenuSnapshot? activeTitleMenu;
    private nuint activeTitleMode;
    private nuint activeTitleOwner;
    private nuint activeTitleManager;
    private TitleInteraction titleInteraction;
    private CancellationTokenSource? movieCancellation;
    private int currentScene = -1;

    public StartupTitleHookSet(
        IRuntimeNativeHookFactory hookFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher,
        OpeningMovieTimeline movieTimeline,
        ITitleCapture? titleCapture = null)
    {
        this.hookFactory = hookFactory ?? throw new ArgumentNullException(nameof(hookFactory));
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.movieTimeline = movieTimeline ?? throw new ArgumentNullException(nameof(movieTimeline));
        this.titleCapture = titleCapture ?? new TitleCapture();
        stringReader = new MsvcStringReader(memory);

        RequiredHookIds = new ReadOnlyCollection<HookId>(
        [
            HookId.TextManagerGetMsg,
            HookId.SceneManagerCreate,
            HookId.SceneManagerNextScene,
            HookId.TitleMenuModeEnter,
            HookId.TitleRowFactory,
            HookId.TitleSceneUpdate,
            HookId.NsMenuFocusSetter,
            HookId.TitleMenuCallback,
        ]);
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            CreateRegistration(HookId.TextManagerGetMsg, PrepareTextManager),
            CreateRegistration(HookId.SceneManagerCreate, PrepareSceneCreate),
            CreateRegistration(HookId.SceneManagerNextScene, PrepareNextScene),
            CreateRegistration(HookId.TitleMenuModeEnter, PrepareTitleEnter),
            CreateRegistration(HookId.TitleRowFactory, PrepareTitleRowFactory),
            CreateRegistration(HookId.TitleSceneUpdate, PrepareTitleUpdate),
            CreateRegistration(HookId.NsMenuFocusSetter, PrepareFocusSetter),
            CreateRegistration(HookId.TitleMenuCallback, PrepareTitleCallback),
        ]);
    }

    public IReadOnlyList<HookId> RequiredHookIds { get; }
    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    public void AfterHooksActivated()
    {
        EnsureInitialized();
        if (!TryReadInt32(imageBase + CurrentSceneGlobalRva, out var sceneId))
        {
            dispatcher.ReportCoverageFailure(
                $"Current startup scene at RVA 0x{CurrentSceneGlobalRva:X} is unreadable after hook activation.");
            throw new InvalidOperationException("Required current-scene snapshot is unreadable after hook activation.");
        }

        ObserveScene(sceneId);
    }

    public void AfterHooksDisabled()
    {
        Interlocked.Exchange(ref currentScene, -1);
        CancelOpeningMovie(publishExit: false);
        ClearTitleInteraction(publishExit: false);
    }

    private IPreparedHook PrepareTextManager(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TextManagerGetMsgDelegate>(
            HookId.TextManagerGetMsg,
            build,
            original => (textManager, result, fileId, messageId) => boundary.Run(
                "TextManager::getMsg",
                () =>
                {
                    var returned = original()(textManager, result, fileId, messageId);
                    boundary.Run("TextManager::getMsg capture", () =>
                    {
                        var stringAddress = returned != 0 ? (nuint)returned : (nuint)result;
                        if (!stringReader.TryRead(stringAddress, out var label, out var error))
                        {
                            if (titleCapture.IsBuilderActive)
                            {
                                titleCapture.ReportBuilderError(
                                    $"Localized title text ({fileId:X},{messageId:X}) is unreadable: {error}");
                            }
                            return;
                        }

                        if (!string.IsNullOrWhiteSpace(label))
                        {
                            localizedText.Store(fileId, messageId, label);
                            titleCapture.ObserveLocalizedResult(
                                fileId,
                                messageId,
                                (nuint)result,
                                label);
                        }
                    });

                    return returned;
                },
                result));

    private IPreparedHook PrepareSceneCreate(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SceneManagerCreateDelegate>(
            HookId.SceneManagerCreate,
            build,
            original => (sceneId, argument) => boundary.Run(
                "SceneManager::create",
                () =>
                {
                    var result = original()(sceneId, argument);
                    boundary.Run("SceneManager::create capture", () => ObserveScene(sceneId));
                    return result;
                },
                0));

    private IPreparedHook PrepareNextScene(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SceneManagerNextSceneDelegate>(
            HookId.SceneManagerNextScene,
            build,
            original => action => boundary.Run(
                "SceneManager::NextScene",
                () =>
                {
                    Exception? instrumentationFailure = null;
                    try
                    {
                        BeforeSceneTransition();
                    }
                    catch (Exception exception)
                    {
                        instrumentationFailure = exception;
                    }

                    original()(action);
                    try
                    {
                        if (!TryReadInt32(imageBase + CurrentSceneGlobalRva, out var sceneId))
                        {
                            dispatcher.ReportCoverageFailure(
                                $"Current scene is unreadable after NextScene action 0x{action:X8}.");
                        }
                        else
                        {
                            ObserveScene(sceneId);
                        }
                    }
                    catch (Exception exception)
                    {
                        instrumentationFailure = Combine(instrumentationFailure, exception);
                    }

                    if (instrumentationFailure is not null)
                    {
                        throw instrumentationFailure;
                    }
                }));

    private IPreparedHook PrepareTitleEnter(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TitleMenuModeEnterDelegate>(
            HookId.TitleMenuModeEnter,
            build,
            original => mode => boundary.Run(
                "TitleMenuMode::enter",
                () =>
                {
                    IDisposable? captureScope = null;
                    Exception? captureStartFailure = null;
                    try
                    {
                        captureScope = titleCapture.BeginBuilder();
                    }
                    catch (Exception exception)
                    {
                        captureStartFailure = exception;
                    }

                    try
                    {
                        original()(mode);
                    }
                    catch
                    {
                        captureScope?.Dispose();
                        throw;
                    }

                    if (captureStartFailure is not null)
                    {
                        throw captureStartFailure;
                    }

                    try
                    {
                        if (!titleCapture.TryCompleteBuilder(out var snapshot, out var captureError))
                        {
                            dispatcher.ReportCoverageFailure(
                                $"Dynamic title menu capture is incomplete: {captureError}");
                            return;
                        }

                        var inspector = GetTitleInspector();
                        if (!inspector.TryInspectTitleMenu((nuint)mode, out var nativeState, out var stateError))
                        {
                            dispatcher.ReportCoverageFailure(
                                $"Dynamic title menu state is invalid: {stateError}");
                            return;
                        }

                        EnterTitleMenu((nuint)mode, nativeState, snapshot);
                    }
                    finally
                    {
                        captureScope?.Dispose();
                    }
                }));

    private IPreparedHook PrepareTitleRowFactory(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TitleRowFactoryDelegate>(
            HookId.TitleRowFactory,
            build,
            original => labelRecord => boundary.Run(
                "Title row factory",
                () =>
                {
                    string? label = null;
                    string? readError = null;
                    var captureActive = false;
                    Exception? captureFailure = null;
                    try
                    {
                        captureActive = titleCapture.IsBuilderActive;
                        if (captureActive &&
                            !stringReader.TryRead((nuint)labelRecord, out label, out readError))
                        {
                            label = null;
                        }
                    }
                    catch (Exception exception)
                    {
                        captureFailure = exception;
                    }

                    var rowControl = original()(labelRecord);
                    boundary.Run("Title row factory capture", () =>
                    {
                        if (captureFailure is not null)
                        {
                            throw captureFailure;
                        }

                        if (captureActive)
                        {
                            if (label is null)
                            {
                                titleCapture.ReportBuilderError(
                                    $"Constructed title row label is unreadable: {readError}");
                            }
                            else
                            {
                                titleCapture.ObserveConstructedRow(
                                    (nuint)labelRecord,
                                    (nuint)rowControl,
                                    label);
                            }
                        }
                    });

                    return rowControl;
                },
                0));

    private IPreparedHook PrepareTitleUpdate(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TitleSceneUpdateDelegate>(
            HookId.TitleSceneUpdate,
            build,
            original => (scene, deltaSeconds) => boundary.Run(
                "TitleScene::update",
                () =>
                {
                    original()(scene, deltaSeconds);
                    var kind = GetTitleInspector().InspectMode((nuint)scene);
                    if (kind == TitleModeKind.TapToStart)
                    {
                        EnterTitlePrompt();
                    }
                    else if (kind == TitleModeKind.TitleMenu && !HasValidatedTitleMenu((nuint)scene))
                    {
                        dispatcher.ReportCoverageFailure(
                            "Title menu mode became active without a validated dynamic row/focus capture.");
                    }
                }));

    private IPreparedHook PrepareFocusSetter(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NsMenuFocusSetterDelegate>(
            HookId.NsMenuFocusSetter,
            build,
            original => (manager, rawKey) => boundary.Run(
                "nsMenu focus setter",
                () =>
                {
                    original()(manager, rawKey);
                    if (!titleCapture.IsBuilderActive)
                    {
                        PublishCurrentFocus((nuint)manager);
                    }
                }));

    private IPreparedHook PrepareTitleCallback(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TitleMenuCallbackDelegate>(
            HookId.TitleMenuCallback,
            build,
            original => (closure, eventTypePointer, actionPointer) => boundary.Run(
                "TitleMenu callback",
                () =>
                {
                    TitleMenuItem? activation = null;
                    Exception? captureFailure = null;
                    try
                    {
                        activation = CaptureActivationBeforeOriginal(
                            (nuint)closure,
                            (nuint)eventTypePointer,
                            (nuint)actionPointer);
                    }
                    catch (Exception exception)
                    {
                        captureFailure = exception;
                    }

                    if (activation is not null)
                    {
                        try
                        {
                            dispatcher.Publish(new ControlActivated(activation.Label));
                        }
                        catch (Exception exception)
                        {
                            captureFailure = Combine(captureFailure, exception);
                        }
                    }

                    original()(closure, eventTypePointer, actionPointer);
                    if (captureFailure is not null)
                    {
                        throw captureFailure;
                    }
                }));

    private IPreparedHook PrepareHook<TDelegate>(
        HookId id,
        IVerifiedGameBuild build,
        Func<Func<TDelegate>, TDelegate> createDetour)
        where TDelegate : Delegate
    {
        InitializeBuild(build);
        if (!build.HookAddresses.TryGetValue(id, out var address))
        {
            throw new InvalidOperationException($"Verified address for required hook '{id}' is missing.");
        }

        TDelegate? original = null;
        TDelegate GetOriginal() => original ?? throw new InvalidOperationException(
            $"Original function for '{id}' is not bound.");
        var detour = createDetour(GetOriginal);
        var hook = hookFactory.CreateHook(id, detour, address);
        original = hook.OriginalFunction;
        return new ReloadedPreparedHook<TDelegate>(GameVersionCatalog.Get(id).Symbol, hook, detour);
    }

    private void InitializeBuild(IVerifiedGameBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.ImageBaseAddress == 0)
        {
            throw new InvalidOperationException("Verified image base is unavailable for startup/title hooks.");
        }

        if (imageBase == 0)
        {
            imageBase = build.ImageBaseAddress;
            titleInspector = new TitleSceneInspector(memory, imageBase);
        }
        else if (imageBase != build.ImageBaseAddress)
        {
            throw new InvalidOperationException("Startup/title hooks received conflicting image bases.");
        }
    }

    private void EnsureInitialized()
    {
        if (imageBase == 0 || titleInspector is null)
        {
            throw new InvalidOperationException("Startup/title hooks were not prepared before activation.");
        }
    }

    private TitleSceneInspector GetTitleInspector() => titleInspector ??
        throw new InvalidOperationException("Title scene inspector is unavailable.");

    private void ObserveScene(int sceneId)
    {
        var previous = Interlocked.Exchange(ref currentScene, sceneId);
        if (previous == sceneId)
        {
            return;
        }

        if (previous == OpeningMovieSceneId)
        {
            CancelOpeningMovie(publishExit: true);
        }
        if (previous == TitleSceneId)
        {
            ClearTitleInteraction();
        }

        switch (sceneId)
        {
            case SquareEnixSceneId:
                dispatcher.Publish(new StartupSceneEntered(StartupSceneKind.SquareEnixLogo));
                break;
            case OpeningMovieSceneId:
                StartOpeningMovie();
                break;
            case TitleSceneId:
                dispatcher.Publish(new StartupSceneEntered(StartupSceneKind.Title));
                break;
        }
    }

    private void BeforeSceneTransition()
    {
        var previous = Interlocked.Exchange(ref currentScene, -1);
        if (previous == OpeningMovieSceneId)
        {
            CancelOpeningMovie(publishExit: true);
        }
        if (previous == TitleSceneId)
        {
            ClearTitleInteraction();
        }
    }

    private void StartOpeningMovie()
    {
        CancelOpeningMovie(publishExit: false);
        dispatcher.Publish(new StartupSceneEntered(StartupSceneKind.OpeningMovie));
        var generation = dispatcher.Generation;
        var cancellation = new CancellationTokenSource();
        lock (movieGate)
        {
            movieCancellation = cancellation;
        }

        var run = movieTimeline.RunAsync(
            generation,
            () => Volatile.Read(ref currentScene) == OpeningMovieSceneId &&
                dispatcher.Generation == generation,
            dispatcher.Publish,
            cancellation.Token);
        _ = run.ContinueWith(
            completed => dispatcher.ReportCoverageFailure(
                $"Opening movie narration failed: {completed.Exception}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void CancelOpeningMovie(bool publishExit)
    {
        CancellationTokenSource? cancellation;
        lock (movieGate)
        {
            cancellation = movieCancellation;
            movieCancellation = null;
        }

        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        if (publishExit)
        {
            dispatcher.Publish(new ScreenExited(ScreenKind.OpeningMovie));
        }
    }

    private void EnterTitleMenu(
        nuint mode,
        TitleMenuNativeState nativeState,
        TitleMenuSnapshot snapshot)
    {
        TitleInteraction previous;
        lock (titleGate)
        {
            previous = titleInteraction;
            activeTitleMode = mode;
            activeTitleOwner = nativeState.Owner;
            activeTitleManager = nativeState.Manager;
            activeTitleMenu = snapshot;
            titleInteraction = TitleInteraction.Menu;
        }

        if (previous == TitleInteraction.Prompt)
        {
            dispatcher.Publish(new ScreenExited(ScreenKind.TitlePrompt));
        }
        dispatcher.Publish(new ScreenEntered(ScreenKind.TitleMenu));
        PublishResolvedFocus(snapshot, nativeState.RawFocusKey);
    }

    private void EnterTitlePrompt()
    {
        TitleInteraction previous;
        lock (titleGate)
        {
            previous = titleInteraction;
            if (previous == TitleInteraction.Prompt)
            {
                return;
            }

            activeTitleMode = 0;
            activeTitleOwner = 0;
            activeTitleManager = 0;
            activeTitleMenu = null;
            titleInteraction = TitleInteraction.Prompt;
        }

        if (previous == TitleInteraction.Menu)
        {
            dispatcher.Publish(new ScreenExited(ScreenKind.TitleMenu));
        }
        dispatcher.Publish(new ScreenEntered(ScreenKind.TitlePrompt));
    }

    private void ClearTitleInteraction(bool publishExit = true)
    {
        TitleInteraction previous;
        lock (titleGate)
        {
            previous = titleInteraction;
            titleInteraction = TitleInteraction.None;
            activeTitleMode = 0;
            activeTitleOwner = 0;
            activeTitleManager = 0;
            activeTitleMenu = null;
        }

        if (!publishExit)
        {
            return;
        }

        if (previous == TitleInteraction.Prompt)
        {
            dispatcher.Publish(new ScreenExited(ScreenKind.TitlePrompt));
        }
        else if (previous == TitleInteraction.Menu)
        {
            dispatcher.Publish(new ScreenExited(ScreenKind.TitleMenu));
        }
    }

    private bool HasValidatedTitleMenu(nuint owner)
    {
        nuint mode;
        nuint capturedOwner;
        lock (titleGate)
        {
            if (titleInteraction != TitleInteraction.Menu || activeTitleMenu is null)
            {
                return false;
            }
            mode = activeTitleMode;
            capturedOwner = activeTitleOwner;
        }

        return capturedOwner == owner &&
            GetTitleInspector().TryInspectTitleMenu(mode, out _, out _);
    }

    private void PublishCurrentFocus(nuint manager)
    {
        TitleMenuSnapshot? snapshot;
        nuint mode;
        nuint expectedManager;
        lock (titleGate)
        {
            if (titleInteraction != TitleInteraction.Menu || manager != activeTitleManager)
            {
                return;
            }
            snapshot = activeTitleMenu;
            mode = activeTitleMode;
            expectedManager = activeTitleManager;
        }

        if (snapshot is null)
        {
            dispatcher.ReportCoverageFailure("Title focus source became invalid: captured rows are unavailable.");
            return;
        }

        if (!GetTitleInspector().TryInspectTitleMenu(mode, out var state, out var error))
        {
            dispatcher.ReportCoverageFailure($"Title focus source became invalid: {error}");
            return;
        }

        if (state.Manager != expectedManager)
        {
            dispatcher.ReportCoverageFailure("Title focus source became invalid: manager mismatch.");
            return;
        }

        PublishResolvedFocus(snapshot, state.RawFocusKey);
    }

    private void PublishResolvedFocus(TitleMenuSnapshot snapshot, int rawKey)
    {
        if (!snapshot.TryResolve(rawKey, out var item))
        {
            dispatcher.ReportCoverageFailure(
                $"Title manager raw focus key {rawKey} is not an enabled captured row.");
            return;
        }

        dispatcher.Publish(new FocusChanged(item.Label, item.Position, item.Count, Disabled: false));
    }

    private TitleMenuItem? CaptureActivationBeforeOriginal(
        nuint closure,
        nuint eventTypePointer,
        nuint actionPointer)
    {
        TitleMenuSnapshot? snapshot;
        nuint mode;
        lock (titleGate)
        {
            if (titleInteraction != TitleInteraction.Menu || activeTitleMenu is null)
            {
                return null;
            }
            snapshot = activeTitleMenu;
            mode = activeTitleMode;
        }

        if (!TryReadPointer(closure + 4, out var closureMode) || closureMode != mode)
        {
            return null;
        }

        if (!TryReadInt32(eventTypePointer, out var eventType) ||
            !TryReadInt32(actionPointer, out var action))
        {
            dispatcher.ReportCoverageFailure("Title activation callback arguments are unreadable.");
            return null;
        }
        _ = action;
        if (eventType != 0)
        {
            return null;
        }

        if (!GetTitleInspector().TryInspectTitleMenu(mode, out var nativeState, out var error) ||
            !snapshot.TryResolve(nativeState.RawFocusKey, out var item))
        {
            dispatcher.ReportCoverageFailure(
                $"Title activation label cannot be resolved from the current focus: {error}");
            return null;
        }

        return item;
    }

    private bool TryReadPointer(nuint address, out nuint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private bool TryReadInt32(nuint address, out int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private static Exception Combine(Exception? first, Exception second) =>
        first is null ? second : new AggregateException(first, second);

    private static IHookRegistration CreateRegistration(
        HookId id,
        Func<IVerifiedGameBuild, UnmanagedBoundaryGuard, IPreparedHook> prepare) =>
        new HookRegistration(GameVersionCatalog.Get(id).Symbol, prepare);

    private sealed class HookRegistration(
        string name,
        Func<IVerifiedGameBuild, UnmanagedBoundaryGuard, IPreparedHook> prepare) : IHookRegistration
    {
        public string Name { get; } = name;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
            prepare(build, boundary);
    }

    private enum TitleInteraction
    {
        None,
        Prompt,
        Menu,
    }
}
