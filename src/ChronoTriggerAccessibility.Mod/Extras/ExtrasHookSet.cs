using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Extras;

public sealed class ExtrasHookSet : IHookActivationObserver, ISharedNativeHookObserver
{
    public const uint GalleryCurrentNodeOffset = 0x290;
    public const uint ManagerFocusKeyOffset = 0x2C4;
    public const uint FocusableStateControlOffset = 0x14;
    public const uint SaveDataGlobalRva = 0x41B4C4;
    public const uint EndingUnlockFlagsOffset = 0x6938;
    public const uint EndingRecordTableRva = 0x3FC9F4;
    public const uint SelectedEndingGlobalRva = 0x41C3EC;
    public const uint EndingRequirementTableRva = 0x3FAFE8;
    public const uint ExtrasHubVtableRva = ExtrasCapture.ExtrasHubVtableRva;
    public const uint EndingLogVtableRva = ExtrasCapture.EndingLogVtableRva;
    public const uint EndingDetailVtableRva = ExtrasCapture.EndingDetailVtableRva;
    public const uint ManagerVtableRva = 0x3A5D0C;
    public const uint CustomButtonVtableRva = 0x3A4364;
    public const uint FocusableStateVtableRva = 0x3AC3F4;

    /// <summary>GalleryNodeMovieTop, GalleryNodeSoundTop and GalleryNodeIllustTop.</summary>
    public const uint MoviesVtableRva = 0x3A52C8;
    public const uint SoundVtableRva = 0x3A5630;
    public const uint IllustrationsVtableRva = 0x3A4F2C;

    /// <summary>Title message IDs (bank 0x10) the list builders read per row: Movies 0x1D7570 and
    /// Illustrations 0x1D5380 step 0x20 through tables their static initializers fill (0x9B82,
    /// 0x9092); Sound 0x1D9200 steps 0xC through a static table (bank 1).</summary>
    public const uint MoviesTitleTableRva = 0x3FBEEC;
    public const uint IllustrationsTitleTableRva = 0x3FB9A4;
    public const uint SoundTitleTableRva = 0x39A340;
    public const int MovieCount = 8;
    public const int IllustrationCount = 16;
    public const int TrackCount = 65;

    /// <summary>0x1D83C0 stores the decided row here before asking for playback.</summary>
    public const uint MoviesSelectedRowOffset = 0x2C8;

    /// <summary>0x1DA600 sets this byte when a track starts; 0x1DA820 clears it.</summary>
    public const uint SoundPlayingOffset = 0x2E4;

    /// <summary>What the mod can say about content the game shows without text. The menu that
    /// opens it is fully read; the pictures and videos themselves are not described.</summary>
    public const string NoVideoDescription = "No video description is available.";
    public const string MoviesReturn = "The Movies list returns when the movie ends.";
    public const string NoImageDescription = "No image description is available.";
    public const string IllustrationReturn = "Confirm or Cancel returns to the illustration list.";
    public const string ReviewDescription = "The ending replays in the game. Its visuals are not described.";
    public const string ReviewReturn = "The ending details return when the replay ends.";

    private const int EndingCount = 19;
    private const int EndingRecordStride = 0x2C;
    private const int MaximumSelectedEnding = EndingCount - 1;
    private const int MediaTableStride = 0x20;
    private const int SoundTableStride = 0x0C;

    /// <summary>The Sound and Illustrations Back dispatchers carry no action; they are planned as Back.</summary>
    private const int BackAction = 4;

    /// <summary>The pending action of a page re-entered after a pushed scene pops.</summary>
    private const int ResumeAction = -1;

    private static readonly HookId[] DedicatedHookIds =
    [
        HookId.GallerySceneSwitchNode,
        HookId.ExtrasHubOnEnter,
        HookId.EndingLogOnEnter,
        HookId.EndingDetailOnEnter,
        HookId.ExtrasNodeOnExit,
        HookId.ExtrasHubDeletingDestructor,
        HookId.EndingLogDeletingDestructor,
        HookId.ExtrasHubCallback,
        HookId.EndingLogCallback,
        HookId.EndingDetailCallback,
        HookId.ExtrasLogTransition,
        HookId.ExtrasDetailTransition,
        HookId.GalleryHubDispatch,
        HookId.GalleryMoviesDispatch,
        HookId.GallerySoundBack,
        HookId.GalleryIllustrationsBack,
        HookId.ExtrasMoviesOnEnter,
        HookId.ExtrasSoundOnEnter,
        HookId.ExtrasIllustrationsOnEnter,
        HookId.ExtrasMoviesCallback,
        HookId.ExtrasSoundCallback,
        HookId.ExtrasIllustrationsCallback,
        HookId.ExtrasIllustrationViewerCallback,
        HookId.ExtrasSoundIdle,
    ];

    private static readonly LocalizedKey[] HubLabelKeys =
    [
        new(0x1A, 0x43),
        new(0x1A, 0x45),
        new(0x1A, 0x44),
        new(0x1A, 0x0F),
        new(0x23, 0xD8),
    ];

    [ThreadStatic]
    private static SwitchScope? threadSwitchScope;
    [ThreadStatic]
    private static BuildScope? threadBuildScope;
    [ThreadStatic]
    private static CallbackScope? threadCallbackScope;
    [ThreadStatic]
    private static TransitionScope? threadTransitionScope;
    [ThreadStatic]
    private static PendingNode? threadPendingNode;
    [ThreadStatic]
    private static IdleScope? threadIdleScope;

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly MsvcStringReader stringReader;
    private readonly object lifecycleGate = new();
    private readonly object stateGate = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private ActiveContext? active;

    /// <summary>The page a movie or ending replay left; only its own node may re-enter.</summary>
    private ActiveContext? suspended;

    /// <summary>
    /// Scheduled dispatchers per Gallery scene, oldest first. The page input managers
    /// (0x1DCF10 → 0x1DD0A0 → 0x1DD4C0) stay enabled after a decide or cancel, and each
    /// scheduled Sequence(DelayTime(1/120), CallFunc) runs on the scene's ActionManager in the
    /// order it was added, its first ActionInterval::step (0x295BE1) counting zero elapsed time.
    /// So a second decide or cancel can be scheduled before the first dispatcher runs, and each
    /// dispatcher consumes exactly the oldest request of its scene.
    /// </summary>
    private readonly Dictionary<nuint, List<QueuedDispatch>> dispatchQueues = [];
    private int activeEpoch;
    private int faulted;
    private bool hooksActive;

    public ExtrasHookSet(
        IRuntimeNativeHookFactory hookFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher)
    {
        this.hookFactory = hookFactory ?? throw new ArgumentNullException(nameof(hookFactory));
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        stringReader = new MsvcStringReader(memory);

        RequiredHookIds = new ReadOnlyCollection<HookId>(DedicatedHookIds.ToArray());
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            CreateRegistration(HookId.GallerySceneSwitchNode, PrepareSwitch),
            CreateRegistration(HookId.ExtrasHubOnEnter, PrepareHubOnEnter),
            CreateRegistration(HookId.EndingLogOnEnter, PrepareLogOnEnter),
            CreateRegistration(HookId.EndingDetailOnEnter, PrepareDetailOnEnter),
            CreateRegistration(HookId.ExtrasNodeOnExit, PrepareOnExit),
            CreateRegistration(HookId.ExtrasHubDeletingDestructor, PrepareHubDeletingDestructor),
            CreateRegistration(HookId.EndingLogDeletingDestructor, PrepareLogDeletingDestructor),
            CreateRegistration(HookId.ExtrasHubCallback, PrepareHubCallback),
            CreateRegistration(HookId.EndingLogCallback, PrepareLogCallback),
            CreateRegistration(HookId.EndingDetailCallback, PrepareDetailCallback),
            CreateRegistration(HookId.ExtrasLogTransition, PrepareLogTransition),
            CreateRegistration(HookId.ExtrasDetailTransition, PrepareDetailTransition),
            CreateRegistration(HookId.GalleryHubDispatch, PrepareHubDispatch),
            CreateRegistration(HookId.GalleryMoviesDispatch, PrepareMoviesDispatch),
            CreateRegistration(HookId.GallerySoundBack, PrepareSoundBack),
            CreateRegistration(HookId.GalleryIllustrationsBack, PrepareIllustrationsBack),
            CreateRegistration(HookId.ExtrasMoviesOnEnter, PrepareMoviesOnEnter),
            CreateRegistration(HookId.ExtrasSoundOnEnter, PrepareSoundOnEnter),
            CreateRegistration(HookId.ExtrasIllustrationsOnEnter, PrepareIllustrationsOnEnter),
            CreateRegistration(HookId.ExtrasMoviesCallback, PrepareMoviesCallback),
            CreateRegistration(HookId.ExtrasSoundCallback, PrepareSoundCallback),
            CreateRegistration(HookId.ExtrasIllustrationsCallback, PrepareIllustrationsCallback),
            CreateRegistration(HookId.ExtrasIllustrationViewerCallback, PrepareViewerCallback),
            CreateRegistration(HookId.ExtrasSoundIdle, PrepareSoundIdle),
        ]);
    }

    public IReadOnlyList<HookId> RequiredHookIds { get; }
    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    public void AfterHooksActivated()
    {
        lock (lifecycleGate)
        {
            if (imageBase == 0)
            {
                throw new InvalidOperationException("Extras hooks were not fully prepared before activation.");
            }
            if (Volatile.Read(ref faulted) != 0)
            {
                throw new InvalidOperationException("Extras hooks faulted before activation completed.");
            }
            hooksActive = true;
            activeEpoch = unchecked(activeEpoch + 1);
        }
    }

    public void AfterHooksDisabled()
    {
        lock (lifecycleGate)
        {
            hooksActive = false;
            activeEpoch = unchecked(activeEpoch + 1);
            ClearActiveStateWithoutEvent();
            ClearOwnedThreadState();
        }
    }

    public void AfterTextManagerGetMsg(
        nint textManager,
        nint result,
        int fileId,
        int messageId,
        nint returned)
    {
        _ = textManager;
        try
        {
            if (!TryCaptureActiveEpoch(out _))
            {
                return;
            }
            var target = GetActiveTextScope();
            if (target is null)
            {
                return;
            }

            var address = returned != 0 ? (nuint)returned : (nuint)result;
            if (!stringReader.TryRead(address, out var text, out var error) || string.IsNullOrWhiteSpace(text))
            {
                target.Errors.Add(
                    $"Localized Extras text ({fileId:X},{messageId:X}) is unreadable or blank: {error}");
                return;
            }
            target.Text.Add(new TextObservation(new LocalizedKey(fileId, messageId), new string(text.AsSpan())));
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Extras localized-text observation failed: {FormatException(exception)}");
        }
    }

    public void AfterCustomButtonConstructed(nint storage, nint returned)
    {
        try
        {
            // Pages construct their controls while building; the illustration viewer constructs
            // its one control inside the list callback that opens it.
            if (!TryCaptureActiveEpoch(out _) || GetOwnedControlSink() is not { } scope)
            {
                return;
            }
            var control = (nuint)returned;
            if (storage == 0 || returned != storage ||
                !TryReadExactVtable(control, CustomButtonVtableRva))
            {
                scope.Errors.Add(
                    $"Extras CustomButton construction did not return exact typed ECX storage (storage 0x{(nuint)storage:X}, returned 0x{control:X}).");
                return;
            }
            if (!scope.ConstructedControls.Add(control))
            {
                scope.Errors.Add($"Extras CustomButton 0x{control:X} was constructed more than once.");
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Extras CustomButton observation failed: {FormatException(exception)}");
        }
    }

    public void AfterControlBound(nint manager, nint focusableState, int managerKey)
    {
        try
        {
            if (!TryCaptureActiveEpoch(out _) || GetOwnedControlSink() is not { } scope)
            {
                return;
            }
            var managerPointer = (nuint)manager;
            var state = (nuint)focusableState;
            if (managerKey < 0 || !TryReadExactVtable(managerPointer, ManagerVtableRva) ||
                !TryReadExactVtable(state, FocusableStateVtableRva) ||
                !TryReadPointer(state + FocusableStateControlOffset, out var control) || control == 0 ||
                !TryReadExactVtable(control, CustomButtonVtableRva))
            {
                scope.Errors.Add(
                    $"Extras binder correlation for manager key {managerKey} does not match the audited manager/FocusableState/CustomButton layout.");
                return;
            }
            if (scope.Bindings.Any(binding => binding.Control == control ||
                    (binding.Manager == managerPointer && binding.Key == managerKey)))
            {
                scope.Errors.Add("Extras binder supplied a duplicate control or manager key.");
                return;
            }
            scope.Bindings.Add(new Binding(managerPointer, control, managerKey));
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Extras binder observation failed: {FormatException(exception)}");
        }
    }

    public void AfterFocusSet(nint manager, int managerKey)
    {
        try
        {
            if (!TryCaptureActiveEpoch(out var epoch))
            {
                return;
            }
            var managerPointer = (nuint)manager;
            if (!TryValidateAuthoritativeFocus(managerPointer, managerKey, out var error))
            {
                if (GetOwnedBuildScope() is { } invalidBuild)
                {
                    invalidBuild.Errors.Add(error);
                    return;
                }
                if (GetOwnedCallbackScope() is { } invalidCallback)
                {
                    invalidCallback.Errors.Add(error);
                    return;
                }
                FailCoverage(error);
                return;
            }
            if (GetOwnedBuildScope() is { } build)
            {
                build.Focus.Add(new FocusObservation(managerPointer, managerKey));
                return;
            }
            if (GetOwnedCallbackScope() is { } callback)
            {
                callback.Focus.Add(new FocusObservation(managerPointer, managerKey));
                return;
            }

            RunInstrumentationSafely(epoch, "Extras runtime focus capture failed", () =>
            {
                var context = GetActiveContext();
                if (context is null || context.Manager != managerPointer)
                {
                    return;
                }
                if (!TryValidateActiveContext(context, out var diagnostic))
                {
                    FailCoverage(diagnostic);
                    return;
                }
                PublishFocus(context, managerKey, Array.Empty<TextObservation>());
            });
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Extras focus observation failed: {FormatException(exception)}");
        }
    }

    public void BeforeTouchTopMenuDeletingDestructor(nint node, uint deletingFlags)
    {
        _ = deletingFlags;
        try
        {
            if (!TryCaptureActiveEpoch(out _))
            {
                return;
            }
            ClearMatchingNode((nuint)node, EndingDetailVtableRva, "Ending Detail deleting destructor");
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Ending Detail lifetime observation failed: {FormatException(exception)}");
        }
    }

    private IPreparedHook PrepareSwitch(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<GallerySceneSwitchNodeDelegate>(HookId.GallerySceneSwitchNode, build, original =>
            (galleryScene, action, rawStackWord) => boundary.Run(
                "GalleryScene::switchNode",
                () => HandleSwitch(original, galleryScene, action, rawStackWord),
                0));

    private nint HandleSwitch(
        Func<GallerySceneSwitchNodeDelegate> original,
        nint galleryScene,
        int action,
        uint rawStackWord)
    {
        var instrument = TryCaptureActiveEpoch(out var epoch);
        SwitchScope? scope = null;
        string? setupError = null;
        if (instrument)
        {
            try
            {
                if (threadSwitchScope is not null)
                {
                    throw new InvalidOperationException("A nested Gallery switch observation was attempted.");
                }
                if (GetOwnedTransitionScope() is { } transition)
                {
                    if (!IsPlannedNestedSwitch(transition, (nuint)galleryScene, action, rawStackWord) ||
                        transition.NestedSwitchStarted)
                    {
                        throw new InvalidOperationException(
                            $"The enclosing {transition.Source} transition did not invoke exactly its one planned Gallery switch for its scene.");
                    }
                    transition.NestedSwitchStarted = true;
                }
                else
                {
                    // Outside a dispatcher only GalleryScene::init (0x2A51A0) switches: a new scene
                    // has no scheduled dispatcher, even at a reused address.
                    ClearPendingNode();
                    ClearActiveForTransition();
                    ResetDispatchQueue((nuint)galleryScene);
                }
                scope = new SwitchScope(this, epoch, (nuint)galleryScene, action, rawStackWord);
                threadSwitchScope = scope;
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        nint returned;
        try
        {
            returned = original()(galleryScene, action, rawStackWord);
        }
        finally
        {
            if (ReferenceEquals(threadSwitchScope, scope))
            {
                threadSwitchScope = null;
            }
        }

        if (instrument)
        {
            RunInstrumentationSafely(epoch, "Gallery switch post-capture failed", () =>
            {
                if (scope is null)
                {
                    FailCoverage($"Gallery switch observation could not start: {setupError}");
                    return;
                }
                FinalizeSwitch(scope, (nuint)returned);
            });
        }
        return returned;
    }

    private IPreparedHook PrepareHubOnEnter(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasHubOnEnterDelegate>(HookId.ExtrasHubOnEnter, build, original => node =>
            boundary.Run("Extras Hub onEnter", () => HandleOnEnter(Surface.Hub, node, original)));

    private IPreparedHook PrepareLogOnEnter(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingLogOnEnterDelegate>(HookId.EndingLogOnEnter, build, original => node =>
            boundary.Run("Ending Log onEnter", () => HandleOnEnter(Surface.EndingLog, node, original)));

    private IPreparedHook PrepareDetailOnEnter(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingDetailOnEnterDelegate>(HookId.EndingDetailOnEnter, build, original => node =>
            boundary.Run("Ending Detail onEnter", () => HandleOnEnter(Surface.EndingDetail, node, original)));

    private IPreparedHook PrepareMoviesOnEnter(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasMoviesOnEnterDelegate>(HookId.ExtrasMoviesOnEnter, build, original => node =>
            boundary.Run("Extras Movies onEnter", () => HandleOnEnter(Surface.Movies, node, original)));

    private IPreparedHook PrepareSoundOnEnter(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasSoundOnEnterDelegate>(HookId.ExtrasSoundOnEnter, build, original => node =>
            boundary.Run("Extras Sound onEnter", () => HandleOnEnter(Surface.Sound, node, original)));

    private IPreparedHook PrepareIllustrationsOnEnter(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasIllustrationsOnEnterDelegate>(HookId.ExtrasIllustrationsOnEnter, build, original => node =>
            boundary.Run("Extras Illustrations onEnter", () => HandleOnEnter(Surface.Illustrations, node, original)));

    private void HandleOnEnter<TDelegate>(Surface surface, nint node, Func<TDelegate> original)
        where TDelegate : Delegate
    {
        var typedOriginal = (Delegate)original();
        var instrument = TryCaptureActiveEpoch(out var epoch);
        BuildScope? scope = null;
        string? setupError = null;
        PendingNode? pending = null;
        if (instrument)
        {
            try
            {
                pending = ResolvePendingForOnEnter(surface, (nuint)node, epoch);
                if (threadBuildScope is not null)
                {
                    throw new InvalidOperationException("A nested Extras builder observation was attempted.");
                }
                scope = new BuildScope(this, epoch, surface, pending);
                threadBuildScope = scope;
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        try
        {
            switch (typedOriginal)
            {
                case ExtrasHubOnEnterDelegate hub:
                    hub(node);
                    break;
                case EndingLogOnEnterDelegate log:
                    log(node);
                    break;
                case EndingDetailOnEnterDelegate detail:
                    detail(node);
                    break;
                case ExtrasMoviesOnEnterDelegate movies:
                    movies(node);
                    break;
                case ExtrasSoundOnEnterDelegate sound:
                    sound(node);
                    break;
                case ExtrasIllustrationsOnEnterDelegate illustrations:
                    illustrations(node);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Extras onEnter original {typedOriginal.GetType().FullName}.");
            }
        }
        finally
        {
            if (ReferenceEquals(threadBuildScope, scope))
            {
                threadBuildScope = null;
            }
            if (pending is not null && ReferenceEquals(threadPendingNode, pending))
            {
                threadPendingNode = null;
            }
        }

        if (!instrument)
        {
            return;
        }
        RunInstrumentationSafely(epoch, $"{surface} post-builder capture failed", () =>
        {
            if (scope is null)
            {
                FailCoverage($"{surface} builder observation could not start: {setupError}");
                return;
            }
            FinalizeBuild(scope);
        });
    }

    private PendingNode ResolvePendingForOnEnter(Surface surface, nuint node, int epoch)
    {
        var pending = GetOwnedPendingNode();
        var transition = GetOwnedTransitionScope();
        if (pending is null && transition is not null)
        {
            if (transition.Epoch != epoch || transition.Target != surface || transition.Pending is not null ||
                transition.Completed is not null || transition.NestedSwitchStarted ||
                !TryReadPointer(transition.Scene + GalleryCurrentNodeOffset, out var attached) || attached != 0 ||
                !TryReadExactVtable(node, ExpectedVtable(surface)))
            {
                throw new InvalidOperationException(
                    $"{surface} direct onEnter does not match the exact transition target, empty scene slot, and audited vtable.");
            }
            if (!TryResolveDirectTransitionTitle(transition, out var title, out var error))
            {
                throw new InvalidOperationException(error);
            }
            pending = new PendingNode(
                this,
                epoch,
                transition.Scene,
                transition.Action,
                surface,
                node,
                title);
            transition.Pending = pending;
            threadPendingNode = pending;
        }
        if (pending is null && transition is null && TryResumeSuspended(surface, node, epoch, out var resumed))
        {
            pending = resumed;
            threadPendingNode = resumed;
        }
        if (pending is null || pending.Epoch != epoch || pending.Surface != surface ||
            pending.Node != node || !TryReadExactVtable(node, ExpectedVtable(surface)))
        {
            throw new InvalidOperationException(
                $"{surface} onEnter does not match the exact pending action, epoch, node pointer, and vtable.");
        }
        if (transition is not null &&
            (!ReferenceEquals(transition.Pending, pending) ||
             !TryReadPointer(transition.Scene + GalleryCurrentNodeOffset, out var transitionAttached) ||
             transitionAttached != 0))
        {
            throw new InvalidOperationException(
                $"{surface} transition onEnter did not occur while the exact Gallery scene slot was empty.");
        }
        return pending;
    }

    /// <summary>
    /// A pushed scene (the movie player 0x1C, or the ending replay through scene 0x13) calls
    /// onExit on the Gallery page, and popping back calls the same node's onEnter, which rebuilds
    /// the page. Only the last page the running scene exited, still attached to its scene, may
    /// re-enter this way, and only with no switch or transition pending; its title is the one it
    /// had, because nothing re-resolves it. Transitions clear the page before removing it, so a
    /// removed page is never remembered.
    /// </summary>
    private bool TryResumeSuspended(Surface surface, nuint node, int epoch, out PendingNode pending)
    {
        pending = null!;
        ActiveContext? candidate;
        lock (stateGate)
        {
            candidate = suspended;
        }
        if (candidate is null || candidate.Node != node || candidate.Surface != surface ||
            !TryReadExactVtable(node, ExpectedVtable(surface)) ||
            !TryReadPointer(candidate.Scene + GalleryCurrentNodeOffset, out var attached) || attached != node)
        {
            return false;
        }
        pending = new PendingNode(this, epoch, candidate.Scene, ResumeAction, surface, node, candidate.Title);
        return true;
    }

    private bool TryResolveDirectTransitionTitle(
        TransitionScope scope,
        out string title,
        out string error)
    {
        title = string.Empty;
        if (scope.ExpectedTitle is null)
        {
            error = "The direct Extras transition has no exact audited title key.";
            return false;
        }
        if (scope.Source == Surface.EndingLog && scope.Action == 3 &&
            (!TryReadInt32(imageBase + SelectedEndingGlobalRva, out var selected) ||
             selected != scope.SelectedEnding))
        {
            error = "Ending Detail title no longer belongs to the exact selected Ending Log row.";
            return false;
        }
        return TryRequireOnly(scope.Text, [scope.ExpectedTitle.Value], out _, out title, out error);
    }

    private IPreparedHook PrepareOnExit(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasNodeOnExitDelegate>(HookId.ExtrasNodeOnExit, build, original => node =>
            boundary.Run("Extras node onExit", () =>
            {
                var captureFailure = CaptureTeardownFailure(
                    () => ClearMatchingAnyNode((nuint)node, "Extras node onExit"));
                original()(node);
                ReportTeardownFailure("Extras node onExit", captureFailure);
            }));

    private IPreparedHook PrepareHubDeletingDestructor(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasHubDeletingDestructorDelegate>(HookId.ExtrasHubDeletingDestructor, build, original =>
            (node, deletingFlags) => boundary.Run(
                "Extras Hub deleting destructor",
                () =>
                {
                    var captureFailure = CaptureTeardownFailure(
                        () => ClearMatchingNode(
                            (nuint)node,
                            ExtrasHubVtableRva,
                            "Extras Hub deleting destructor"));
                    var returned = original()(node, deletingFlags);
                    ReportTeardownFailure("Extras Hub deleting destructor", captureFailure);
                    return returned;
                },
                node));

    /// <summary>0x1D38E0 is a shared base-class deleting destructor (it reinstalls vtable 0x3A6388):
    /// the Ending Log, Movies and Illustrations pages all use it as vtable slot 0.</summary>
    private IPreparedHook PrepareLogDeletingDestructor(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingLogDeletingDestructorDelegate>(HookId.EndingLogDeletingDestructor, build, original =>
            (node, deletingFlags) => boundary.Run(
                "Ending Log deleting destructor",
                () =>
                {
                    var captureFailure = CaptureTeardownFailure(
                        () => ClearMatchingNodeOfAny(
                            (nuint)node,
                            [EndingLogVtableRva, MoviesVtableRva, IllustrationsVtableRva],
                            "Ending Log deleting destructor"));
                    var returned = original()(node, deletingFlags);
                    ReportTeardownFailure("Ending Log deleting destructor", captureFailure);
                    return returned;
                },
                node));

    private IPreparedHook PrepareHubCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasHubCallbackDelegate>(HookId.ExtrasHubCallback, build, original =>
            (closure, eventType, action) => boundary.Run(
                "Extras Hub callback",
                () => HandleCallback(Surface.Hub, closure, eventType, action, () => original()(closure, eventType, action))));

    private IPreparedHook PrepareLogCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingLogCallbackDelegate>(HookId.EndingLogCallback, build, original =>
            (closure, eventType, action) => boundary.Run(
                "Ending Log callback",
                () => HandleCallback(Surface.EndingLog, closure, eventType, action, () => original()(closure, eventType, action))));

    private IPreparedHook PrepareDetailCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingDetailCallbackDelegate>(HookId.EndingDetailCallback, build, original =>
            (closure, eventType, action) => boundary.Run(
                "Ending Detail callback",
                () => HandleCallback(Surface.EndingDetail, closure, eventType, action, () => original()(closure, eventType, action))));

    private IPreparedHook PrepareMoviesCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasMoviesCallbackDelegate>(HookId.ExtrasMoviesCallback, build, original =>
            (closure, eventType, action) => boundary.Run(
                "Extras Movies callback",
                () => HandleCallback(Surface.Movies, closure, eventType, action, () => original()(closure, eventType, action))));

    private IPreparedHook PrepareSoundCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasSoundCallbackDelegate>(HookId.ExtrasSoundCallback, build, original =>
            (closure, eventType, action) => boundary.Run(
                "Extras Sound callback",
                () => HandleCallback(Surface.Sound, closure, eventType, action, () => original()(closure, eventType, action))));

    private IPreparedHook PrepareIllustrationsCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasIllustrationsCallbackDelegate>(HookId.ExtrasIllustrationsCallback, build, original =>
            (closure, eventType, action) => boundary.Run(
                "Extras Illustrations callback",
                () => HandleCallback(Surface.Illustrations, closure, eventType, action, () => original()(closure, eventType, action))));

    private IPreparedHook PrepareViewerCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasIllustrationViewerCallbackDelegate>(HookId.ExtrasIllustrationViewerCallback, build, original =>
            (closure, eventType, action) => boundary.Run(
                "Extras Illustration viewer callback",
                () => HandleViewerCallback(closure, eventType, () => original()(closure, eventType, action))));

    private IPreparedHook PrepareSoundIdle(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasSoundIdleDelegate>(HookId.ExtrasSoundIdle, build, original => node =>
            boundary.Run("Extras Sound idle state", () => HandleSoundIdle(node, () => original()(node))));

    private IPreparedHook PrepareLogTransition(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasLogTransitionDelegate>(HookId.ExtrasLogTransition, build, original => payload =>
            boundary.Run(
                "Extras Ending Log transition",
                () => HandleTransition(Surface.EndingLog, payload, payloadHasAction: true, () => original()(payload))));

    private IPreparedHook PrepareDetailTransition(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasDetailTransitionDelegate>(HookId.ExtrasDetailTransition, build, original => payload =>
            boundary.Run(
                "Extras Ending Detail transition",
                () => HandleTransition(Surface.EndingDetail, payload, payloadHasAction: true, () => original()(payload))));

    private IPreparedHook PrepareHubDispatch(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<GalleryHubDispatchDelegate>(HookId.GalleryHubDispatch, build, original => payload =>
            boundary.Run(
                "GalleryScene deferred Hub action",
                () => HandleTransition(Surface.Hub, payload, payloadHasAction: true, () => original()(payload))));

    private IPreparedHook PrepareMoviesDispatch(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<GalleryMoviesDispatchDelegate>(HookId.GalleryMoviesDispatch, build, original => payload =>
            boundary.Run(
                "GalleryScene deferred Movies action",
                () => HandleTransition(Surface.Movies, payload, payloadHasAction: true, () => original()(payload))));

    private IPreparedHook PrepareSoundBack(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<GallerySoundBackDelegate>(HookId.GallerySoundBack, build, original => payload =>
            boundary.Run(
                "GalleryScene deferred Sound Back",
                () => HandleTransition(Surface.Sound, payload, payloadHasAction: false, () => original()(payload))));

    private IPreparedHook PrepareIllustrationsBack(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<GalleryIllustrationsBackDelegate>(HookId.GalleryIllustrationsBack, build, original => payload =>
            boundary.Run(
                "GalleryScene deferred Illustrations Back",
                () => HandleTransition(Surface.Illustrations, payload, payloadHasAction: false, () => original()(payload))));

    /// <summary>
    /// Every Gallery page asks for its next page through its own std::function, which only
    /// schedules the dispatcher: a Sequence of DelayTime(1/120) and CallFunc (Hub 0x2A5590,
    /// Ending Log 0x2A5D60, Ending Detail 0x2A6000, Movies 0x2A59C0, Sound 0x2A5B10, Illustrations
    /// 0x2A6C70). The dispatcher therefore runs on a later frame, after the page callback has
    /// returned, and it constructs, attaches and enters its target synchronously.
    /// </summary>
    private void HandleTransition(Surface source, nint payload, bool payloadHasAction, Action invokeOriginal)
    {
        var instrument = TryCaptureActiveEpoch(out var epoch);
        TransitionScope? scope = null;
        string? setupError = null;
        if (instrument)
        {
            try
            {
                var (action, scene) = ReadTransitionPayload(source, (nuint)payload, payloadHasAction);
                scope = BeginTransition(source, action, scene, epoch);
                if (scope.Plan.Kind is TransitionKind.Build or TransitionKind.NestedSwitch)
                {
                    threadTransitionScope = scope;
                    ClearPendingNode();
                    ClearActiveForTransition();
                }
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        try
        {
            invokeOriginal();
        }
        finally
        {
            if (scope is not null && ReferenceEquals(threadTransitionScope, scope))
            {
                threadTransitionScope = null;
            }
            if (scope is not null && ReferenceEquals(threadPendingNode, scope.Pending))
            {
                threadPendingNode = null;
            }
        }

        if (!instrument)
        {
            return;
        }
        RunInstrumentationSafely(epoch, $"{source} transition post-capture failed", () =>
        {
            if (scope is null)
            {
                FailCoverage($"{source} transition validation failed: {setupError}");
                return;
            }
            FinalizeTransition(scope);
        });
    }

    /// <summary>The Hub, Ending Log, Ending Detail and Movies dispatchers receive {action, scene};
    /// the Sound and Illustrations Back dispatchers receive only {scene}.</summary>
    private (int Action, nuint Scene) ReadTransitionPayload(Surface source, nuint payload, bool payloadHasAction)
    {
        if (!payloadHasAction)
        {
            if (!TryReadPointer(payload, out var onlyScene) || onlyScene == 0)
            {
                throw new InvalidOperationException($"{source} Back payload 0x{payload:X} has no readable Gallery scene.");
            }
            return (BackAction, onlyScene);
        }
        if (!TryReadInt32(payload, out var action))
        {
            throw new InvalidOperationException($"{source} transition payload 0x{payload:X} has no readable action.");
        }
        if (!TryReadPointer(payload + 4, out var scene) || scene == 0)
        {
            throw new InvalidOperationException($"{source} transition payload has no readable Gallery scene identity.");
        }
        return (action, scene);
    }

    private TransitionScope BeginTransition(Surface source, int action, nuint scene, int epoch)
    {
        if (threadTransitionScope is not null || threadSwitchScope is not null || threadBuildScope is not null)
        {
            throw new InvalidOperationException("A nested or overlapping Extras transition observation was attempted.");
        }
        var active = GetActiveContext();
        if (active is not null && active.Scene != scene)
        {
            throw new InvalidOperationException(
                $"{source} transition does not belong to the active page's Gallery scene.");
        }

        // Natively the page callback has returned before its dispatcher runs and the request
        // waits in its scene's queue; an enclosing callback scope only exists when a dispatcher is
        // replayed synchronously. Either way one decide or cancel answers for exactly one dispatcher.
        var callback = GetOwnedCallbackScope();
        TransitionRequest? request;
        QueuedDispatch? queued = null;
        if (callback is not null && ReferenceEquals(callback.Context, active))
        {
            request = callback.RequestConsumed ? null : callback.Request;
            callback.RequestConsumed = true;
        }
        else
        {
            queued = DequeueDispatch(scene);
            request = queued?.Request;
        }
        if (request is null || request.Epoch != epoch || request.Context.Surface != source ||
            request.Context.Scene != scene ||
            (queued is not null && queued.DispatchAction != action) ||
            (queued is null && ScheduledDispatchAction(request) != action))
        {
            throw new InvalidOperationException(
                $"{source} transition action {action} has no preceding decide or cancel on the same page.");
        }

        // The dispatcher operates the scene's current node (+0x290), so an earlier dispatcher may
        // already have replaced the page that asked for this one; that page is then retired.
        var sourceActive = ReferenceEquals(active, request.Context);
        if (active is not null && !TryValidateActiveContext(active, out var error))
        {
            throw new InvalidOperationException(error);
        }
        if (!TryValidateTransitionRequest(request, queued, source, action, out var selectedEnding, out error) ||
            !TryPlanTransition(source, action, selectedEnding, out var plan, out error))
        {
            throw new InvalidOperationException(error);
        }
        if (plan.Kind == TransitionKind.Suspending && sourceActive)
        {
            request.Context.SuspendRequested = true;
        }
        return new TransitionScope(this, epoch, source, plan, action, scene, selectedEnding)
        {
            SourceWasActive = sourceActive,
        };
    }

    /// <summary>
    /// What each page callback schedules, read from its body: Hub 0x1DC610, Ending Log 0x1D4850,
    /// Ending Detail 0x1D35A0, Movies 0x1D83C0, Sound 0x1D9F70 and Illustrations 0x1D6160. A decide
    /// on an unfocused control only moves focus except in Ending Log; a locked Hub tile or ending only plays a buzzer,
    /// a track decide plays it, an illustration decide opens the viewer, and a Cancel while a
    /// track plays only stops it; none of those schedules a dispatcher.
    /// </summary>
    private static int? ScheduledDispatchAction(TransitionRequest request)
    {
        var context = request.Context;
        RuntimeControl? control = null;
        var decided = request.EventType == 0 && context.Controls.TryGetValue(request.Action, out control);
        var focusedDecide = decided && request.EntryFocusKey == request.Action;
        var cancel = request.EventType == 2;
        switch (context.Surface)
        {
            case Surface.Hub:
                if (cancel) return 4;
                return focusedDecide && !control!.Focus.Disabled && request.Action is >= 0 and <= 4
                    ? request.Action
                    : null;
            case Surface.EndingLog:
                // 0x1D4850 checks the unlock mask, not the focus, before scheduling 3.
                if (cancel) return 4;
                if (!decided) return null;
                if (control!.Focus.Position == EndingCount + 1) return 4;
                return request.Action is >= 0 and <= MaximumSelectedEnding && !control.Focus.Disabled ? 3 : null;
            case Surface.EndingDetail:
                if (cancel) return 4;
                return focusedDecide ? (control!.Focus.Position == 1 ? 3 : 4) : null;
            case Surface.Movies:
                if (cancel) return 4;
                return focusedDecide ? (request.Action < MovieCount ? 0 : 4) : null;
            case Surface.Sound:
                if (cancel) return request.SoundWasPlaying ? null : BackAction;
                return focusedDecide && request.Action == TrackCount ? BackAction : null;
            case Surface.Illustrations:
                if (cancel) return BackAction;
                return focusedDecide && request.Action == IllustrationCount ? BackAction : null;
            default:
                return null;
        }
    }

    private void EnqueueDispatch(QueuedDispatch dispatch)
    {
        lock (stateGate)
        {
            var scene = dispatch.Request.Context.Scene;
            if (!dispatchQueues.TryGetValue(scene, out var queue))
            {
                queue = [];
                dispatchQueues[scene] = queue;
            }
            queue.Add(dispatch);
        }
    }

    private QueuedDispatch? DequeueDispatch(nuint scene)
    {
        lock (stateGate)
        {
            if (!dispatchQueues.TryGetValue(scene, out var queue) || queue.Count == 0)
            {
                return null;
            }
            var head = queue[0];
            queue.RemoveAt(0);
            return head;
        }
    }

    private void ResetDispatchQueue(nuint scene)
    {
        lock (stateGate)
        {
            dispatchQueues.Remove(scene);
        }
    }

    /// <summary>
    /// What each dispatcher does, read from its body: Hub 0x2A5650, Ending Log 0x2A5E20, Ending
    /// Detail 0x2A60C0, Movies 0x2A5A80, Sound Back 0x2A5BD0 and Illustrations Back 0x2A6D30.
    /// </summary>
    private bool TryPlanTransition(
        Surface source,
        int action,
        int selectedEnding,
        out TransitionPlan plan,
        out string error)
    {
        plan = null!;
        error = string.Empty;
        switch (source, action)
        {
            case (Surface.Hub, 0):
                plan = TransitionPlan.Switch(Surface.Movies, switchAction: 1, raw: 0, finalFocusKey: null);
                return true;
            case (Surface.Hub, 1):
                plan = TransitionPlan.Build(Surface.Illustrations, new LocalizedKey(0x1A, 0x45), finalFocusKey: null);
                return true;
            case (Surface.Hub, 2):
                plan = TransitionPlan.Build(Surface.Sound, new LocalizedKey(0x1A, 0x44), finalFocusKey: null);
                return true;
            case (Surface.Hub, 3):
                plan = TransitionPlan.Build(Surface.EndingLog, new LocalizedKey(0x1A, 0x0F), finalFocusKey: null);
                return true;
            case (Surface.Hub, 4):
                plan = TransitionPlan.Leaving;
                return true;
            case (Surface.EndingLog, 3):
                if (!TryReadInt32(
                        imageBase + EndingRecordTableRva + (nuint)(selectedEnding * EndingRecordStride),
                        out var messageId))
                {
                    error = "Ending Log transition cannot read the exact selected ending title message ID.";
                    return false;
                }
                plan = TransitionPlan.Build(Surface.EndingDetail, new LocalizedKey(0x0F, messageId), finalFocusKey: null);
                return true;
            case (Surface.EndingLog, 4):
                plan = TransitionPlan.Switch(Surface.Hub, switchAction: 0, raw: 3, finalFocusKey: 3);
                return true;
            case (Surface.EndingDetail, 3):
                plan = TransitionPlan.Suspending;
                return true;
            case (Surface.EndingDetail, 4):
                plan = TransitionPlan.Build(Surface.EndingLog, new LocalizedKey(0x1A, 0x0F), finalFocusKey: null);
                return true;
            case (Surface.Movies, 0):
                plan = TransitionPlan.Suspending;
                return true;
            case (Surface.Movies, 4):
                plan = TransitionPlan.Switch(Surface.Hub, switchAction: 0, raw: 0, finalFocusKey: 0);
                return true;
            case (Surface.Sound, BackAction):
                plan = TransitionPlan.Build(Surface.Hub, new LocalizedKey(0x41, 0x06), finalFocusKey: 2);
                return true;
            case (Surface.Illustrations, BackAction):
                plan = TransitionPlan.Build(Surface.Hub, new LocalizedKey(0x41, 0x06), finalFocusKey: 1);
                return true;
            default:
                error = $"{source} transition action {action} is not an audited Gallery transition.";
                return false;
        }
    }

    /// <summary>The dispatcher must follow the decide or cancel that asks for it natively.</summary>
    private bool TryValidateTransitionRequest(
        TransitionRequest request,
        QueuedDispatch? queued,
        Surface source,
        int action,
        out int selectedEnding,
        out string error)
    {
        selectedEnding = -1;
        error = string.Empty;
        var context = request.Context;
        bool Decides(int key) => request.EventType == 0 && request.Action == key && request.EntryFocusKey == key &&
            context.Controls.TryGetValue(key, out var control) && !control.Focus.Disabled;
        var related = (source, action) switch
        {
            (Surface.Hub, >= 0 and <= 3) => Decides(action),
            (Surface.Hub, 4) => IsExactBackRequest(request, expectedPosition: 5),
            // 0x1D4850 stores the callback row; 0x2A5E20 uses the live global at dispatch.
            // Later choices and Log initialization (0x1D39D3) can both change that global.
            (Surface.EndingLog, 3) =>
                request.EventType == 0 && request.Action is >= 0 and <= MaximumSelectedEnding &&
                context.Controls.TryGetValue(request.Action, out var endingRow) && !endingRow.Focus.Disabled &&
                TryReadInt32(imageBase + SelectedEndingGlobalRva, out selectedEnding) &&
                selectedEnding is >= 0 and <= MaximumSelectedEnding &&
                (queued is null
                    ? selectedEnding == request.Action
                    : queued.SelectedEnding == request.Action),
            // Ending Log 0x1D4850 activates Back immediately too, without a focus-first branch.
            (Surface.EndingLog, 4) => IsExactBackRequest(request, expectedPosition: 20, requireEntryFocus: false),
            (Surface.EndingDetail, 3) =>
                Decides(0) && context.Controls[0].Focus.Position == 1,
            (Surface.EndingDetail, 4) =>
                IsExactBackRequest(request, expectedPosition: 2) &&
                TryReadInt32(imageBase + SelectedEndingGlobalRva, out selectedEnding) &&
                selectedEnding is >= 0 and <= MaximumSelectedEnding,
            // The stored row is read when the callback returns; the page may be retired by now.
            (Surface.Movies, 0) =>
                request.EventType == 0 && request.Action is >= 0 and < MovieCount && Decides(request.Action) &&
                (queued is not null
                    ? queued.MovieRow == request.Action
                    : TryReadInt32(context.Node + MoviesSelectedRowOffset, out var storedRow) && storedRow == request.Action),
            (Surface.Movies, 4) => IsExactBackRequest(request, expectedPosition: MovieCount + 1),
            (Surface.Sound, BackAction) =>
                IsExactBackRequest(request, expectedPosition: TrackCount + 1) &&
                (request.EventType == 0 || !request.SoundWasPlaying),
            (Surface.Illustrations, BackAction) => IsExactBackRequest(request, expectedPosition: IllustrationCount + 1),
            _ => false,
        };
        if (!related)
        {
            error = $"{source} transition action {action} is not related to the exact decide or cancel that preceded it.";
        }
        return related;
    }

    private static bool IsExactBackRequest(TransitionRequest request, int expectedPosition, bool requireEntryFocus = true)
    {
        if (request.EventType == 2)
        {
            return request.Context.Controls.Values.Count(control => control.Focus.Position == expectedPosition) == 1;
        }
        return request.EventType == 0 && (!requireEntryFocus || request.EntryFocusKey == request.Action) &&
            request.Context.Controls.TryGetValue(request.Action, out var control) &&
            control.Focus.Position == expectedPosition && !control.Focus.Disabled;
    }

    private void HandleCallback(
        Surface surface,
        nint closure,
        int eventType,
        int action,
        Action invokeOriginal)
    {
        var instrument = TryCaptureActiveEpoch(out var epoch);
        CallbackScope? scope = null;
        string? setupError = null;
        if (instrument)
        {
            try
            {
                if (threadCallbackScope is not null)
                {
                    throw new InvalidOperationException("A nested Extras callback observation was attempted.");
                }
                var context = GetActiveContext();
                if (context is not null && context.Surface == surface)
                {
                    if (!TryValidateActiveContext(context, out var error) ||
                        !TryValidateCallbackClosure(context, (nuint)closure, action, out error))
                    {
                        throw new InvalidOperationException(error);
                    }
                    scope = new CallbackScope(this, epoch, context, eventType, action);
                    threadCallbackScope = scope;
                }
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        try
        {
            invokeOriginal();
        }
        finally
        {
            if (ReferenceEquals(threadCallbackScope, scope))
            {
                threadCallbackScope = null;
            }
        }

        if (!instrument)
        {
            return;
        }
        RunInstrumentationSafely(epoch, $"{surface} callback post-capture failed", () =>
        {
            if (setupError is not null)
            {
                FailCoverage($"{surface} callback validation failed: {setupError}");
                return;
            }
            if (scope is not null)
            {
                FinalizeCallback(scope);
            }
        });
    }

    private void FinalizeSwitch(SwitchScope scope, nuint returnedNode)
    {
        if (scope.Errors.Count > 0)
        {
            FailCoverage(scope.Errors[0]);
            return;
        }
        if (!TryMapAction(scope.Action, out var surface))
        {
            return;
        }
        if (returnedNode == 0 || !TryReadExactVtable(returnedNode, ExpectedVtable(surface)))
        {
            FailCoverage(
                $"Gallery switch action {scope.Action} returned node 0x{returnedNode:X} with the wrong audited vtable.");
            return;
        }
        if (!TryResolveSwitchTitle(scope, surface, out var title, out var error))
        {
            FailCoverage(error);
            return;
        }
        var pending = new PendingNode(
            this, scope.Epoch, scope.Scene, scope.Action, surface, returnedNode, title);
        if (GetOwnedTransitionScope() is { } transition)
        {
            if (!IsPlannedNestedSwitch(transition, scope.Scene, scope.Action, scope.RawStackWord) ||
                transition.Target != surface || transition.Pending is not null)
            {
                FailCoverage($"{transition.Source} nested switch does not match its exact planned transition target.");
                return;
            }
            transition.Pending = pending;
        }
        threadPendingNode = pending;
    }

    private static bool IsPlannedNestedSwitch(TransitionScope transition, nuint scene, int action, uint raw) =>
        transition.Plan.Kind == TransitionKind.NestedSwitch && transition.Scene == scene &&
        transition.Plan.SwitchAction == action && transition.Plan.SwitchRaw == raw;

    private bool TryResolveSwitchTitle(
        SwitchScope scope,
        Surface surface,
        out string title,
        out string error)
    {
        LocalizedKey expected;
        if (surface == Surface.Hub)
        {
            expected = new(0x41, 0x06);
        }
        else if (surface == Surface.EndingLog)
        {
            expected = new(0x1A, 0x0F);
        }
        else if (PageTitleKey(surface) is { } pageTitle)
        {
            expected = pageTitle;
        }
        else
        {
            if (!TryReadInt32(imageBase + SelectedEndingGlobalRva, out var selected) ||
                selected < 0 || selected > MaximumSelectedEnding ||
                !TryReadInt32(
                    imageBase + EndingRecordTableRva + (nuint)(selected * EndingRecordStride),
                    out var messageId))
            {
                title = string.Empty;
                error = "Ending Detail switch cannot read the selected ending or exact title message ID.";
                return false;
            }
            expected = new(0x0F, messageId);
        }
        return TryRequireOnly(scope.Text, [expected], out var values, out title, out error);
    }

    private void FinalizeBuild(BuildScope scope)
    {
        if (scope.Errors.Count > 0)
        {
            FailCoverage(scope.Errors[0]);
            return;
        }
        if (!TryValidateBuildCommon(scope, out var manager, out var focusedKey, out var error))
        {
            FailCoverage(error);
            return;
        }
        switch (scope.Surface)
        {
            case Surface.Hub:
                FinalizeHub(scope, manager, focusedKey);
                break;
            case Surface.EndingLog:
                FinalizeEndingLog(scope, manager, focusedKey);
                break;
            case Surface.EndingDetail:
                FinalizeEndingDetail(scope, manager, focusedKey);
                break;
            case Surface.Movies:
                FinalizeMediaPage(scope, manager, focusedKey, MoviesPage);
                break;
            case Surface.Sound:
                FinalizeMediaPage(scope, manager, focusedKey, SoundPage);
                break;
            case Surface.Illustrations:
                FinalizeMediaPage(scope, manager, focusedKey, IllustrationsPage);
                break;
        }
    }

    /// <summary>
    /// Movies (builder 0x1D7390 → 0x1D7570), Sound (0x1D9070 → 0x1D9200, then the idle state
    /// 0x1DA820) and Illustrations (0x1D51A0 → 0x1D5380) share one shape: Back is constructed
    /// first (0x1D85F0 or 0x1D6910, (0x23, 0xD8)), then one CustomButton per row whose title the
    /// row builder localizes in table order; the rows bind keys 0..N-1 and Back binds key N. No
    /// builder reads an unlock flag: locking exists only at the Hub category.
    /// </summary>
    private void FinalizeMediaPage(BuildScope scope, nuint manager, int focusedKey, MediaPage page)
    {
        var controls = scope.ConstructedControls.ToArray();
        var keys = Enumerable.Range(0, page.Rows).Append(page.Rows).ToArray();
        var ordered = controls.Length == page.Rows + 1 ? controls.Skip(1).Append(controls[0]).ToArray() : controls;
        if (controls.Length != page.Rows + 1 ||
            !TryRequireExactBindings(scope, manager, ordered, keys, out var error))
        {
            FailCoverage($"{page.Surface} did not construct Back and exactly {page.Rows} rows bound to keys 0..{page.Rows}.");
            return;
        }
        var titleKeys = new LocalizedKey[page.Rows];
        for (var row = 0; row < page.Rows; row++)
        {
            if (!TryReadTitleId(page, row, out var titleId))
            {
                FailCoverage($"{page.Surface} title table row {row} is unreadable.");
                return;
            }
            titleKeys[row] = new LocalizedKey(page.TitleBank, titleId);
        }
        var back = new LocalizedKey(0x23, 0xD8);
        var required = new List<LocalizedKey>(titleKeys) { back, page.Status };
        if (!TryRequireOnly(scope.Text, required, out var values, out _, out error))
        {
            FailCoverage($"{page.Surface} localized text is incomplete: {error}");
            return;
        }
        var observedTitles = scope.Text.Where(item => item.Key.File == page.TitleBank).Select(item => item.Key).ToArray();
        if (!observedTitles.SequenceEqual(titleKeys))
        {
            FailCoverage($"{page.Surface} row titles were not localized in native row order.");
            return;
        }
        if (page.Surface == Surface.Sound &&
            (!TryReadByte(scope.Pending.Node + SoundPlayingOffset, out var playing) || playing != 0))
        {
            FailCoverage("Sound did not finish its build in the idle state.");
            return;
        }

        var count = page.Rows + 1;
        var controlMap = new Dictionary<int, RuntimeControl>();
        for (var row = 0; row < page.Rows; row++)
        {
            controlMap[row] = new RuntimeControl(
                ordered[row],
                new MenuFocus(values[titleKeys[row]], null, row + 1, count, null, false));
        }
        controlMap[page.Rows] = new RuntimeControl(
            controls[0],
            new MenuFocus(values[back], null, count, count, null, false));
        if (!controlMap.TryGetValue(focusedKey, out var focused))
        {
            FailCoverage($"{page.Surface} focused key {focusedKey} has no captured row or Back control.");
            return;
        }
        var context = new ActiveContext(
            page.Surface,
            scope.Pending.Scene,
            scope.Pending.Node,
            manager,
            controlMap,
            scope.Pending.Title)
        {
            StatusText = values[page.Status],
        };
        CompleteBuild(scope, context, ToPresented(context, focused.Focus, [context.StatusText]));
    }

    private bool TryReadTitleId(MediaPage page, int row, out int titleId)
    {
        titleId = -1;
        return row >= 0 && row < page.Rows &&
            TryReadInt32(imageBase + page.TitleTableRva + (nuint)(row * page.TitleStride), out titleId) &&
            titleId >= 0;
    }

    private bool TryValidateBuildCommon(
        BuildScope scope,
        out nuint manager,
        out int focusedKey,
        out string error)
    {
        manager = 0;
        focusedKey = -1;
        error = string.Empty;
        var transition = GetOwnedTransitionScope();
        var deferred = transition is not null && ReferenceEquals(transition.Pending, scope.Pending);
        if (!TryReadPointer(scope.Pending.Scene + GalleryCurrentNodeOffset, out var attached) ||
            attached != (deferred ? 0u : scope.Pending.Node) ||
            !TryReadExactVtable(scope.Pending.Node, ExpectedVtable(scope.Surface)))
        {
            error = deferred
                ? $"{scope.Surface} transition builder did not complete while the exact scene slot remained empty."
                : $"{scope.Surface} completed without exact scene attachment and node identity.";
            return false;
        }
        if (scope.ConstructedControls.Count == 0 ||
            !scope.ConstructedControls.SetEquals(scope.Bindings.Select(binding => binding.Control)))
        {
            error = $"{scope.Surface} constructed controls and binder controls do not have the same exact pointer set.";
            return false;
        }
        var managers = scope.Bindings.Select(binding => binding.Manager).Distinct().ToArray();
        if (managers.Length != 1 || !TryReadExactVtable(managers[0], ManagerVtableRva))
        {
            error = $"{scope.Surface} did not correlate exactly one audited input manager.";
            return false;
        }
        var correlatedManager = managers[0];
        manager = correlatedManager;
        var focus = scope.Focus.Where(item => item.Manager == correlatedManager).ToArray();
        if (focus.Length != 1 ||
            !TryValidateAuthoritativeFocus(correlatedManager, focus[0].Key, out error) ||
            !scope.Bindings.Any(binding => binding.Manager == correlatedManager && binding.Key == focus[0].Key))
        {
            error = string.IsNullOrWhiteSpace(error)
                ? $"{scope.Surface} did not capture one exact authoritative bound focus key."
                : error;
            return false;
        }
        focusedKey = focus[0].Key;
        error = string.Empty;
        return true;
    }

    private void FinalizeHub(BuildScope scope, nuint manager, int focusedKey)
    {
        var controls = scope.ConstructedControls.ToArray();
        var error = string.Empty;
        if (controls.Length != 5 || !TryRequireExactBindings(scope, manager, controls, [0, 1, 2, 3, 4], out error))
        {
            FailCoverage(string.IsNullOrWhiteSpace(error)
                ? "Extras Hub did not construct exactly five controls."
                : error);
            return;
        }
        var required = HubLabelKeys.ToList();
        var enabled = new bool[5];
        for (var index = 0; index < 5; index++)
        {
            if (!TryReadByte(scope.Pending.Node + 0x2E4u + (nuint)(index * 8), out var available) ||
                available is not 0 and not 1)
            {
                FailCoverage($"Extras Hub availability byte for position {index} is unreadable or invalid.");
                return;
            }
            enabled[index] = available != 0;
        }
        LocalizedKey? helpKey = focusedKey is >= 0 and < 4
            ? new LocalizedKey(0x1A, enabled[focusedKey] ? 0x4A + focusedKey : 0x49)
            : null;
        if (helpKey is not null)
        {
            required.Add(helpKey.Value);
        }
        if (!TryRequireOnly(scope.Text, required, out var values, out _, out error))
        {
            FailCoverage(error);
            return;
        }
        var inputs = new ExtrasHubControlInput[5];
        for (var index = 0; index < 5; index++)
        {
            inputs[index] = new ExtrasHubControlInput(
                index,
                index,
                values[HubLabelKeys[index]],
                helpKey is not null && index == focusedKey ? values[helpKey.Value] : null,
                enabled[index],
                Visible: true);
        }
        if (!ExtrasCapture.TryCaptureHub(
                imageBase,
                imageBase + ExtrasHubVtableRva,
                focusedKey,
                inputs,
                out var snapshot,
                out error))
        {
            FailCoverage($"Extras Hub capture is incomplete: {error}");
            return;
        }

        var context = new ActiveContext(
            Surface.Hub,
            scope.Pending.Scene,
            scope.Pending.Node,
            manager,
            BuildControlMap(scope, snapshot.Controls),
            scope.Pending.Title)
        {
            Hub = snapshot,
        };
        if (helpKey is not null)
        {
            context.HelpCache[helpKey.Value] = values[helpKey.Value];
        }
        CompleteBuild(
            scope,
            context,
            ToPresented(context, snapshot.TryGetFocusedControl(out var focus) ? focus : null, []));
    }

    private void FinalizeEndingLog(BuildScope scope, nuint manager, int focusedKey)
    {
        var controls = scope.ConstructedControls.ToArray();
        if (controls.Length != 20)
        {
            FailCoverage("Ending Log did not construct exactly one Back control and nineteen ending rows.");
            return;
        }
        var backBinding = scope.Bindings.SingleOrDefault(binding => binding.Control == controls[0]);
        if (backBinding is null || backBinding.Manager != manager || backBinding.Key <= 18)
        {
            FailCoverage("Ending Log Back was not independently correlated by its exact control identity and noncolliding manager key.");
            return;
        }
        for (var row = 0; row < EndingCount; row++)
        {
            if (!scope.Bindings.Any(binding => binding.Manager == manager &&
                    binding.Control == controls[row + 1] && binding.Key == row))
            {
                FailCoverage($"Ending Log row {row} does not have its exact control/key identity.");
                return;
            }
        }
        if (!TryReadPointer(imageBase + SaveDataGlobalRva, out var saveData) || saveData == 0 ||
            !TryReadInt32(saveData + EndingUnlockFlagsOffset, out var flags))
        {
            FailCoverage("Ending Log unlock flags are unreadable.");
            return;
        }

        var expectedText = new List<LocalizedKey> { new(0x1A, 0x40), new(0x23, 0xD8) };
        var rowInputs = new EndingLogRowInput[EndingCount];
        var unlockedKeys = new List<LocalizedKey>();
        var locked = new bool[EndingCount];
        for (var row = 0; row < EndingCount; row++)
        {
            var record = imageBase + EndingRecordTableRva + (nuint)(row * EndingRecordStride);
            if (!TryReadInt32(record, out var messageId) || !TryReadInt32(record + 0x0C, out var mask))
            {
                FailCoverage($"Ending Log record {row} is unreadable.");
                return;
            }
            locked[row] = (flags & mask) == 0;
            if (!locked[row])
            {
                var key = new LocalizedKey(0x0F, messageId);
                expectedText.Add(key);
                unlockedKeys.Add(key);
            }
        }
        if (!TryRequireOnly(scope.Text, expectedText, out var values, out _, out var error))
        {
            FailCoverage(error);
            return;
        }
        var observedUnlocked = scope.Text.Where(item => item.Key.File == 0x0F).Select(item => item.Key).ToArray();
        if (!observedUnlocked.SequenceEqual(unlockedKeys))
        {
            FailCoverage("Ending Log unlocked title observations do not match native row order and exact message IDs.");
            return;
        }
        var unlockedIndex = 0;
        for (var row = 0; row < EndingCount; row++)
        {
            rowInputs[row] = new EndingLogRowInput(
                row,
                locked[row] ? "???" : values[unlockedKeys[unlockedIndex++]],
                locked[row]);
        }
        if (!ExtrasCapture.TryCaptureEndingLog(
                imageBase,
                imageBase + EndingLogVtableRva,
                rowInputs,
                new ExtrasBackControlInput(backBinding.Key, values[new(0x23, 0xD8)], true, true),
                out var snapshot,
                out error))
        {
            FailCoverage($"Ending Log capture is incomplete: {error}");
            return;
        }
        var controlMap = new Dictionary<int, RuntimeControl>();
        for (var row = 0; row < EndingCount; row++)
        {
            controlMap[row] = new RuntimeControl(
                controls[row + 1],
                new MenuFocus(rowInputs[row].VisibleLabel, null, row + 1, 20, null, rowInputs[row].Locked));
        }
        controlMap[backBinding.Key] = new RuntimeControl(
            controls[0],
            ToFocus(snapshot.Back));
        if (!controlMap.TryGetValue(focusedKey, out var focused))
        {
            FailCoverage($"Ending Log focused key {focusedKey} has no captured row or Back control.");
            return;
        }
        var context = new ActiveContext(
            Surface.EndingLog,
            scope.Pending.Scene,
            scope.Pending.Node,
            manager,
            controlMap,
            scope.Pending.Title)
        {
            EndingLog = snapshot,
            BuilderTitle = values[new(0x1A, 0x40)],
        };
        CompleteBuild(scope, context, ToPresented(context, focused.Focus, [context.BuilderTitle]));
    }

    private void FinalizeEndingDetail(BuildScope scope, nuint manager, int focusedKey)
    {
        var controls = scope.ConstructedControls.ToArray();
        var error = string.Empty;
        if (controls.Length != 2 || !TryRequireExactBindings(scope, manager, controls, [0, 1], out error))
        {
            FailCoverage(string.IsNullOrWhiteSpace(error)
                ? "Ending Detail did not construct exactly Review and Back."
                : error);
            return;
        }
        if (!TryReadInt32(imageBase + SelectedEndingGlobalRva, out var selected) ||
            selected < 0 || selected > MaximumSelectedEnding ||
            !TryReadInt32(
                imageBase + EndingRequirementTableRva + (nuint)(selected * EndingRecordStride),
                out var requirementMessageId))
        {
            FailCoverage("Ending Detail cannot read the selected ending and requirement message ID.");
            return;
        }
        LocalizedKey[] required =
        [
            new(0x1A, 0x4F),
            new(0x1A, 0x47),
            new(0x0F, requirementMessageId),
            new(0x1A, 0x48),
            new(0x23, 0xD8),
        ];
        if (!TryRequireOnly(scope.Text, required, out var values, out _, out error))
        {
            FailCoverage(error);
            return;
        }
        var input = new EndingDetailInput(
            scope.Pending.Title,
            values[new(0x1A, 0x4F)],
            values[new(0x1A, 0x47)],
            values[new(0x0F, requirementMessageId)],
            [
                new EndingDetailControlInput(0, values[new(0x1A, 0x48)], true, true),
                new EndingDetailControlInput(1, values[new(0x23, 0xD8)], true, true),
            ]);
        if (!ExtrasCapture.TryCaptureEndingDetail(
                imageBase,
                imageBase + EndingDetailVtableRva,
                input,
                out var snapshot,
                out error))
        {
            FailCoverage($"Ending Detail capture is incomplete: {error}");
            return;
        }
        var context = new ActiveContext(
            Surface.EndingDetail,
            scope.Pending.Scene,
            scope.Pending.Node,
            manager,
            BuildControlMap(scope, snapshot.Controls),
            scope.Pending.Title)
        {
            EndingDetail = snapshot,
        };
        if (!context.Controls.TryGetValue(focusedKey, out var focused))
        {
            FailCoverage($"Ending Detail focused key {focusedKey} has no captured control.");
            return;
        }
        CompleteBuild(
            scope,
            context,
            ToPresented(
                context,
                focused.Focus,
                [snapshot.VisibleHeader, $"{snapshot.VisibleRequirementsLabel}: {snapshot.VisibleRequirementText}"]));
    }

    private void FinalizeCallback(CallbackScope scope)
    {
        if (scope.Errors.Count > 0)
        {
            FailCoverage(scope.Errors[0]);
            return;
        }
        AccessibilityEvent? primary = scope.Context.Surface switch
        {
            Surface.Hub => FinalizeHubCallback(scope),
            Surface.EndingLog => FinalizeLogCallback(scope),
            Surface.EndingDetail => FinalizeDetailCallback(scope),
            Surface.Movies => FinalizeMoviesCallback(scope),
            Surface.Sound => FinalizeSoundCallback(scope),
            Surface.Illustrations => FinalizeIllustrationsCallback(scope),
            _ => null,
        };
        if (Volatile.Read(ref faulted) != 0)
        {
            return;
        }
        // A decide or cancel that schedules a dispatcher queues exactly one request behind any
        // already scheduled on this scene; the dispatcher that runs later consumes it.
        if (!scope.RequestConsumed && ReferenceEquals(GetActiveContext(), scope.Context) &&
            ScheduledDispatchAction(scope.Request) is { } dispatchAction)
        {
            int? movieRow = null;
            if (scope.Context.Surface == Surface.Movies && dispatchAction == 0 &&
                TryReadInt32(scope.Context.Node + MoviesSelectedRowOffset, out var storedRow))
            {
                movieRow = storedRow;
            }
            int? selected = null;
            if (scope.Context.Surface == Surface.EndingLog && dispatchAction == 3 &&
                TryReadInt32(imageBase + SelectedEndingGlobalRva, out var selectedEnding))
            {
                selected = selectedEnding;
            }
            EnqueueDispatch(new QueuedDispatch(scope.Request, dispatchAction, movieRow, selected));
        }
        if (primary is not null)
        {
            dispatcher.Publish(primary);
        }
        if (scope.ExitOwner is { } exitOwner)
        {
            dispatcher.Publish(new MenuExited(exitOwner));
        }
        foreach (var deferred in scope.DeferredEvents)
        {
            dispatcher.Publish(deferred);
        }
    }

    private AccessibilityEvent? FinalizeHubCallback(CallbackScope scope)
    {
        var context = scope.Context;
        if (scope.EventType == 0)
        {
            if (!TryReadContextFocusAtEntry(scope, out var entryKey) ||
                !context.Controls.TryGetValue(scope.Action, out var control))
            {
                FailCoverage("Extras Hub callback has no exact entry focus/action control correlation.");
                return null;
            }
            if (entryKey != scope.Action)
            {
                return FinalizeCallbackFocus(scope);
            }
            if (control.Focus.Disabled)
            {
                if (!TryResolveHubHelp(context, scope.Action, scope.Text, out var help, out var error))
                {
                    FailCoverage(error);
                    return null;
                }
                return new MenuUnsupported(control.Focus.Label, help, "Choose another Extras item or Back.");
            }
            return new MenuActivated(control.Focus.Label);
        }
        if (scope.EventType == 2 && context.Controls.TryGetValue(4, out var back))
        {
            return new MenuActivated(back.Focus.Label);
        }
        return scope.Focus.Count > 0 ? FinalizeCallbackFocus(scope) : null;
    }

    private AccessibilityEvent? FinalizeLogCallback(CallbackScope scope)
    {
        var context = scope.Context;
        if (scope.EventType == 0 && context.Controls.TryGetValue(scope.Action, out var control))
        {
            return control.Focus.Disabled
                ? new MenuUnsupported(control.Focus.Label, "This ending is unavailable.", "Choose another ending or Back.")
                : new MenuActivated(control.Focus.Label);
        }
        if (scope.EventType == 2)
        {
            var back = context.Controls.Values.SingleOrDefault(control => control.Focus.Position == 20);
            return back is null ? null : new MenuActivated(back.Focus.Label);
        }
        return scope.Focus.Count > 0 ? FinalizeCallbackFocus(scope) : null;
    }

    private AccessibilityEvent? FinalizeDetailCallback(CallbackScope scope)
    {
        var context = scope.Context;
        if (scope.EventType == 0 && context.Controls.TryGetValue(scope.Action, out var control))
        {
            if (!TryReadContextFocusAtEntry(scope, out var entryKey))
            {
                FailCoverage("Ending Detail callback entry focus is unavailable.");
                return null;
            }
            if (entryKey != scope.Action)
            {
                return FinalizeCallbackFocus(scope);
            }
            // Review replays the ending through the field engine (0x2A60C0 → NextScene(1) → scene
            // 0x13) and pops back here afterwards; the replay's visuals have no description.
            return scope.Action == 0
                ? new MenuUnsupported(control.Focus.Label, ReviewDescription, ReviewReturn)
                : new MenuActivated(control.Focus.Label);
        }
        if (scope.EventType == 2 && context.Controls.TryGetValue(1, out var back))
        {
            return new MenuActivated(back.Focus.Label);
        }
        return scope.Focus.Count > 0 ? FinalizeCallbackFocus(scope) : null;
    }

    /// <summary>Movies input 0x1D83C0: decide on the focused row stores it at node + 0x2C8 and asks
    /// for playback (PlayMovieScene 0x1C shows only the video); key 8 and Cancel ask for Back.</summary>
    private AccessibilityEvent? FinalizeMoviesCallback(CallbackScope scope)
    {
        var context = scope.Context;
        if (scope.EventType == 0 && context.Controls.TryGetValue(scope.Action, out var control))
        {
            if (!TryReadContextFocusAtEntry(scope, out var entryKey))
            {
                FailCoverage("Movies callback entry focus is unavailable.");
                return null;
            }
            if (entryKey != scope.Action)
            {
                return FinalizeCallbackFocus(scope);
            }
            return scope.Action < MovieCount
                ? new MenuUnsupported(control.Focus.Label, NoVideoDescription, MoviesReturn)
                : new MenuActivated(control.Focus.Label);
        }
        if (scope.EventType == 2 && TryGetControlAtPosition(context, MovieCount + 1, out var back))
        {
            return new MenuActivated(back.Focus.Label);
        }
        return scope.Focus.Count > 0 ? FinalizeCallbackFocus(scope) : null;
    }

    /// <summary>Sound input 0x1D9F70: decide on a track plays it and 0x1DA600 localizes "Now
    /// Playing" and the title into the two labels it shows; Cancel while playing stops the track
    /// through 0x1DA820, which restores the StatusBar instruction; otherwise Cancel and key 65
    /// ask for Back.</summary>
    private AccessibilityEvent? FinalizeSoundCallback(CallbackScope scope)
    {
        var context = scope.Context;
        if (scope.EventType == 0 && context.Controls.TryGetValue(scope.Action, out var control))
        {
            if (!TryReadContextFocusAtEntry(scope, out var entryKey))
            {
                FailCoverage("Sound callback entry focus is unavailable.");
                return null;
            }
            if (entryKey != scope.Action)
            {
                return FinalizeCallbackFocus(scope);
            }
            if (scope.Action >= TrackCount)
            {
                return new MenuActivated(control.Focus.Label);
            }
            if (!TryReadTitleId(SoundPage, scope.Action, out var titleId))
            {
                FailCoverage($"Sound title table row {scope.Action} is unreadable.");
                return null;
            }
            // 0x1DA600 pads "Now Playing" with leading spaces (0xAFCB0) and the update 0x1DA9B0
            // animates trailing dots; the words are the localized message itself.
            var nowPlaying = new LocalizedKey(0x1A, 0x46);
            var title = new LocalizedKey(SoundPage.TitleBank, titleId);
            if (!TryRequireOnly(scope.Text, [nowPlaying, title], out var values, out _, out var error))
            {
                FailCoverage($"Sound did not show exactly Now Playing and track {scope.Action + 1}'s title: {error}");
                return null;
            }
            if (!TryReadByte(context.Node + SoundPlayingOffset, out var playing) || playing != 1)
            {
                FailCoverage($"Sound track {scope.Action + 1} did not enter the playing state.");
                return null;
            }
            context.SoundPlaying = true;
            return new MenuNoticePresented(OwnerOf(context), [values[nowPlaying], values[title]]);
        }
        if (scope.EventType == 2)
        {
            if (scope.Request.SoundWasPlaying)
            {
                return FinishSoundIdle(context, scope.Text, "Sound Cancel while playing");
            }
            return TryGetControlAtPosition(context, TrackCount + 1, out var back)
                ? new MenuActivated(back.Focus.Label)
                : null;
        }
        return scope.Focus.Count > 0 ? FinalizeCallbackFocus(scope) : null;
    }

    /// <summary>Illustrations input 0x1D6160: decide on the focused row opens the viewer 0x1D6390,
    /// which draws only the picture and gives it one control focused at key 0 on its own
    /// manager; key 16 and Cancel ask for Back.</summary>
    private AccessibilityEvent? FinalizeIllustrationsCallback(CallbackScope scope)
    {
        var context = scope.Context;
        if (scope.EventType == 0 && context.Controls.TryGetValue(scope.Action, out var control))
        {
            if (!TryReadContextFocusAtEntry(scope, out var entryKey))
            {
                FailCoverage("Illustrations callback entry focus is unavailable.");
                return null;
            }
            if (entryKey != scope.Action)
            {
                return FinalizeCallbackFocus(scope);
            }
            if (scope.Action >= IllustrationCount)
            {
                return new MenuActivated(control.Focus.Label);
            }
            var viewerFocus = scope.Focus.Where(item => item.Manager != context.Manager).ToArray();
            var viewerBindings = scope.Bindings.Where(item => item.Manager != context.Manager).ToArray();
            if (viewerFocus.Length != 1 || viewerFocus[0].Key != 0 ||
                viewerBindings.Length != 1 || viewerBindings[0].Manager != viewerFocus[0].Manager ||
                viewerBindings[0].Key != 0 || scope.ConstructedControls.Count != 1 ||
                !scope.ConstructedControls.Contains(viewerBindings[0].Control) ||
                scope.Focus.Any(item => item.Manager == context.Manager) ||
                scope.Bindings.Any(item => item.Manager == context.Manager))
            {
                FailCoverage("The illustration viewer did not open as one control focused at key 0 on its own manager.");
                return null;
            }
            context.Viewer = new ViewerState(viewerFocus[0].Manager, scope.Action);
            return new MenuUnsupported(control.Focus.Label, NoImageDescription, IllustrationReturn);
        }
        if (scope.EventType == 2 && TryGetControlAtPosition(context, IllustrationCount + 1, out var back))
        {
            return new MenuActivated(back.Focus.Label);
        }
        return scope.Focus.Count > 0 ? FinalizeCallbackFocus(scope) : null;
    }

    private static bool TryGetControlAtPosition(ActiveContext context, int position, out RuntimeControl control)
    {
        var matches = context.Controls.Values.Where(item => item.Focus.Position == position).ToArray();
        control = matches.Length == 1 ? matches[0] : null!;
        return matches.Length == 1;
    }

    /// <summary>0x1DA820 restores the StatusBar instruction (0x1A, 0x3D) and clears both Now
    /// Playing labels and the playing byte.</summary>
    private AccessibilityEvent? FinishSoundIdle(ActiveContext context, IReadOnlyList<TextObservation> text, string what)
    {
        if (!TryRequireOnly(text, [new LocalizedKey(0x1A, 0x3D)], out _, out var instruction, out var error) ||
            !TryReadByte(context.Node + SoundPlayingOffset, out var playing) || playing != 0)
        {
            FailCoverage($"{what} did not return to the idle Sound page: {error}");
            return null;
        }
        context.SoundPlaying = false;
        return new MenuNoticePresented(OwnerOf(context), [instruction]);
    }

    /// <summary>
    /// The illustration viewer's own input 0x1D6890 closes it on decide or cancel: it removes the
    /// picture and its control, restores the list focus (0x1DD5F0) and re-enables the list.
    /// </summary>
    private void HandleViewerCallback(nint closure, int eventType, Action invokeOriginal)
    {
        var instrument = TryCaptureActiveEpoch(out var epoch);
        ActiveContext? viewerContext = null;
        CallbackScope? scope = null;
        string? setupError = null;
        if (instrument)
        {
            try
            {
                if (threadCallbackScope is not null)
                {
                    throw new InvalidOperationException("A nested Extras callback observation was attempted.");
                }
                var context = GetActiveContext();
                if (context is { Surface: Surface.Illustrations, Viewer: { } viewer })
                {
                    if (!TryReadPointer((nuint)closure + 4, out var node) || node != context.Node ||
                        !TryReadPointer((nuint)closure + 0x0C, out var manager) || manager != viewer.Manager)
                    {
                        throw new InvalidOperationException(
                            "Illustration viewer callback closure does not name the open viewer and its page.");
                    }
                    viewerContext = context;
                    scope = new CallbackScope(this, epoch, context, eventType, -1);
                    threadCallbackScope = scope;
                }
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        try
        {
            invokeOriginal();
        }
        finally
        {
            if (ReferenceEquals(threadCallbackScope, scope))
            {
                threadCallbackScope = null;
            }
        }

        if (!instrument)
        {
            return;
        }
        RunInstrumentationSafely(epoch, "Illustration viewer post-capture failed", () =>
        {
            if (setupError is not null)
            {
                FailCoverage($"Illustration viewer callback validation failed: {setupError}");
                return;
            }
            if (viewerContext is null || eventType is not (0 or 2))
            {
                return;
            }
            if (!ReferenceEquals(GetActiveContext(), viewerContext) || scope!.Errors.Count > 0 ||
                !TryReadInt32(viewerContext.Manager + ManagerFocusKeyOffset, out var focusedKey) ||
                !viewerContext.Controls.TryGetValue(focusedKey, out var focused))
            {
                FailCoverage(scope?.Errors.FirstOrDefault() ??
                    "The illustration list did not regain one captured focused row when its viewer closed.");
                return;
            }
            viewerContext.Viewer = null;
            dispatcher.Publish(ToPresented(viewerContext, focused.Focus, [viewerContext.StatusText]));
        });
    }

    /// <summary>
    /// The Sound page's update 0x1DA9B0 calls the idle state 0x1DA820 when the track ends. Calls
    /// from the builder or from a Cancel callback are read by those scopes instead.
    /// </summary>
    private void HandleSoundIdle(nint node, Action invokeOriginal)
    {
        var instrument = TryCaptureActiveEpoch(out var epoch);
        IdleScope? scope = null;
        string? setupError = null;
        if (instrument && GetActiveTextScope() is null)
        {
            try
            {
                var context = GetActiveContext();
                if (context is { Surface: Surface.Sound, SoundPlaying: true } && context.Node == (nuint)node)
                {
                    if (!TryValidateActiveContext(context, out var error))
                    {
                        throw new InvalidOperationException(error);
                    }
                    scope = new IdleScope(this, epoch, context);
                    threadIdleScope = scope;
                }
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        try
        {
            invokeOriginal();
        }
        finally
        {
            if (scope is not null && ReferenceEquals(threadIdleScope, scope))
            {
                threadIdleScope = null;
            }
        }

        if (!instrument || (scope is null && setupError is null))
        {
            return;
        }
        RunInstrumentationSafely(epoch, "Sound idle post-capture failed", () =>
        {
            if (scope is null)
            {
                FailCoverage($"Sound idle observation could not start: {setupError}");
                return;
            }
            if (scope.Errors.Count > 0)
            {
                FailCoverage(scope.Errors[0]);
                return;
            }
            if (!ReferenceEquals(GetActiveContext(), scope.Context))
            {
                return;
            }
            if (FinishSoundIdle(scope.Context, scope.Text, "The finished Sound track") is { } idle)
            {
                dispatcher.Publish(idle);
            }
        });
    }

    private AccessibilityEvent? FinalizeCallbackFocus(CallbackScope scope)
    {
        if (scope.Focus.Count != 1 || scope.Focus[0].Manager != scope.Context.Manager)
        {
            FailCoverage($"{scope.Context.Surface} callback did not capture one exact authoritative focus transition.");
            return null;
        }
        if (scope.Context.Surface == Surface.Hub)
        {
            if (!TryUpdateHubFocus(scope.Context, scope.Focus[0].Key, scope.Text, out var focus, out var error))
            {
                FailCoverage(error);
                return null;
            }
            return new MenuFocusChanged(focus);
        }
        if (!scope.Context.Controls.TryGetValue(scope.Focus[0].Key, out var control))
        {
            FailCoverage($"{scope.Context.Surface} focus key {scope.Focus[0].Key} has no captured control.");
            return null;
        }
        return new MenuFocusChanged(control.Focus);
    }

    private bool TryReadContextFocusAtEntry(CallbackScope scope, out int key)
    {
        key = scope.EntryFocusKey;
        return key >= 0 && scope.Context.Controls.ContainsKey(key);
    }

    private void PublishFocus(ActiveContext context, int managerKey, IReadOnlyList<TextObservation> text)
    {
        if (context.Surface == Surface.Hub)
        {
            if (!TryUpdateHubFocus(context, managerKey, text, out var focus, out var error))
            {
                FailCoverage(error);
                return;
            }
            dispatcher.Publish(new MenuFocusChanged(focus));
            return;
        }
        if (!context.Controls.TryGetValue(managerKey, out var control))
        {
            FailCoverage($"{context.Surface} focus key {managerKey} has no captured control.");
            return;
        }
        dispatcher.Publish(new MenuFocusChanged(control.Focus));
    }

    private bool TryUpdateHubFocus(
        ActiveContext context,
        int key,
        IReadOnlyList<TextObservation> observed,
        out MenuFocus focus,
        out string error)
    {
        focus = null!;
        if (context.Hub is null || !context.Controls.TryGetValue(key, out var runtime))
        {
            error = $"Extras Hub focus key {key} has no captured control.";
            return false;
        }
        string? help = null;
        if (key < 4)
        {
            if (!TryResolveHubHelp(context, key, observed, out help, out error))
            {
                return false;
            }
        }
        if (!context.Hub.TryMoveFocus(key, help, out var updated, out error) ||
            !updated.TryGetFocusedControl(out var control))
        {
            return false;
        }
        context.Hub = updated;
        focus = ToFocus(control);
        context.Controls[key] = runtime with { Focus = focus };
        return true;
    }

    private bool TryResolveHubHelp(
        ActiveContext context,
        int key,
        IReadOnlyList<TextObservation> observed,
        out string help,
        out string error)
    {
        help = string.Empty;
        if (!context.Controls.TryGetValue(key, out var control) || key < 0 || key > 3)
        {
            error = $"Extras Hub help key {key} has no captured tile.";
            return false;
        }
        var expected = new LocalizedKey(0x1A, control.Focus.Disabled ? 0x49 : 0x4A + key);
        var matches = observed.Where(item => item.Key == expected).ToArray();
        var unknown = observed.Where(item => item.Key != expected).ToArray();
        if (unknown.Length > 0 || matches.Length > 1)
        {
            error = $"Extras Hub help observation for key {key} is ambiguous or has an unexpected native message ID.";
            return false;
        }
        if (matches.Length == 1)
        {
            context.HelpCache[expected] = matches[0].Text;
        }
        if (!context.HelpCache.TryGetValue(expected, out help!) || string.IsNullOrWhiteSpace(help))
        {
            error = $"Extras Hub focus key {key} has no native-observed visible help text.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private bool TryValidateCallbackClosure(
        ActiveContext context,
        nuint closure,
        int action,
        out string error)
    {
        if (closure == 0)
        {
            error = $"{context.Surface} callback closure is null.";
            return false;
        }
        if (context.Surface == Surface.EndingLog)
        {
            if (!TryReadPointer(closure, out var node) || node != context.Node)
            {
                error = "Ending Log callback closure does not retain the active node identity.";
                return false;
            }
            error = string.Empty;
            return true;
        }
        // The Illustrations closure keeps its container at +8 for the viewer (0x1D623A) and its
        // control vector at +0xC; every other page keeps {manager, node, vector}.
        var vectorOffset = context.Surface == Surface.Illustrations ? 0x0Cu : 0x08u;
        if (!TryReadPointer(closure, out var manager) || manager != context.Manager ||
            !TryReadPointer(closure + 4, out var nodePointer) || nodePointer != context.Node ||
            !TryReadPointer(closure + vectorOffset, out var vector) || vector == 0)
        {
            error = $"{context.Surface} callback closure manager/node/vector does not match the active capture.";
            return false;
        }
        if (action >= 0 && context.Controls.TryGetValue(action, out var control))
        {
            if (!TryReadPointer(vector + (nuint)(action * 4), out var vectorControl) ||
                vectorControl != control.Pointer)
            {
                error = $"{context.Surface} callback action {action} does not match its captured control vector identity.";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private bool TryValidateActiveContext(ActiveContext context, out string error)
    {
        if (!TryReadExactVtable(context.Node, ExpectedVtable(context.Surface)) ||
            !TryReadExactVtable(context.Manager, ManagerVtableRva) ||
            !TryReadPointer(context.Scene + GalleryCurrentNodeOffset, out var attached) || attached != context.Node)
        {
            error = $"{context.Surface} retained node/manager/scene identity is stale or has the wrong vtable.";
            return false;
        }
        foreach (var control in context.Controls.Values)
        {
            if (!TryReadExactVtable(control.Pointer, CustomButtonVtableRva))
            {
                error = $"{context.Surface} retained control 0x{control.Pointer:X} has a stale or wrong vtable.";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private bool TryValidateAuthoritativeFocus(nuint manager, int requestedKey, out string error)
    {
        if (!TryReadExactVtable(manager, ManagerVtableRva) ||
            !TryReadInt32(manager + ManagerFocusKeyOffset, out var authoritative) ||
            authoritative != requestedKey)
        {
            error = $"Extras manager 0x{manager:X} requested key {requestedKey}, but its exact vtable or authoritative +0x2C4 key is different/unreadable.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private void CompleteBuild(BuildScope scope, ActiveContext context, MenuPresented presented)
    {
        if (GetOwnedTransitionScope() is not { } transition)
        {
            ActivateContext(context, presented);
            return;
        }
        if (transition.Epoch != scope.Epoch || !ReferenceEquals(transition.Pending, scope.Pending) ||
            transition.Target != scope.Surface || transition.Completed is not null ||
            context.Surface != scope.Surface || context.Scene != transition.Scene ||
            context.Node != scope.Pending.Node)
        {
            FailCoverage($"{scope.Surface} completed outside its one exact enclosing transition target.");
            return;
        }
        transition.Completed = new CompletedBuild(scope, context, presented);
    }

    private void FinalizeTransition(TransitionScope scope)
    {
        if (scope.Errors.Count > 0)
        {
            FailCoverage(scope.Errors[0]);
            return;
        }
        switch (scope.Plan.Kind)
        {
            case TransitionKind.Leaving:
                // Hub Back: NextScene(-1) replaces the Gallery with the title; its teardown
                // clears the page through onExit and the destructors.
                return;
            case TransitionKind.Suspending:
                // Movie playback or the ending replay: the page stays until the pushed scene
                // calls onExit, and the same node's onEnter rebuilds it when the scene pops.
                // A retired source means an earlier dispatcher replaced the page first; the push
                // then exits whichever page is attached, and that page is the one that re-enters.
                var source = GetActiveContext();
                if (scope.SourceWasActive &&
                    (source is null || source.Surface != scope.Source || source.Scene != scope.Scene ||
                     !source.SuspendRequested))
                {
                    FailCoverage($"{scope.Source} did not keep its exact page while the game started its playback.");
                }
                return;
            case TransitionKind.NestedSwitch:
                if (!scope.NestedSwitchStarted || scope.Text.Count != 0)
                {
                    FailCoverage(
                        $"{scope.Source} did not complete through its one planned nested Gallery switch without outer title observations.");
                    return;
                }
                break;
            default:
                if (!TryResolveDirectTransitionTitle(scope, out var title, out var titleError) ||
                    scope.Pending is null || !string.Equals(scope.Pending.Title, title, StringComparison.Ordinal))
                {
                    FailCoverage(string.IsNullOrWhiteSpace(titleError)
                        ? $"{scope.Target} transition title no longer matches its exact pending target."
                        : titleError);
                    return;
                }
                break;
        }
        var pending = scope.Pending;
        var completed = scope.Completed;
        var expectedPendingAction = scope.Plan.Kind == TransitionKind.NestedSwitch ? scope.Plan.SwitchAction : scope.Action;
        if (pending is null || completed is null || pending.Epoch != scope.Epoch ||
            pending.Scene != scope.Scene || pending.Surface != scope.Target ||
            pending.Action != expectedPendingAction ||
            !ReferenceEquals(completed.Build.Pending, pending) || completed.Build.Epoch != scope.Epoch ||
            completed.Context.Surface != scope.Target || completed.Context.Scene != scope.Scene ||
            completed.Context.Node != pending.Node ||
            !string.Equals(completed.Context.Title, pending.Title, StringComparison.Ordinal) ||
            !string.Equals(completed.Presented.Title, pending.Title, StringComparison.Ordinal) ||
            GetActiveContext() is not null)
        {
            FailCoverage($"{scope.Source} transition did not produce one complete exact {scope.Target} target.");
            return;
        }
        if (!TryValidateActiveContext(completed.Context, out var error))
        {
            FailCoverage(error);
            return;
        }
        if (!TryValidateFinalTransitionFocus(scope, completed, out error))
        {
            FailCoverage(error);
            return;
        }
        if (scope.Source == Surface.EndingDetail && scope.Action == 4 &&
            (!TryReadInt32(imageBase + SelectedEndingGlobalRva, out var selected) ||
             selected != scope.SelectedEnding ||
             !completed.Context.Controls.TryGetValue(selected, out var restored) ||
             !Equals(completed.Presented.Focus, restored.Focus)))
        {
            FailCoverage("Ending Detail Back did not restore the exact selected Ending Log row and focus.");
            return;
        }
        ActivateContext(completed.Context, completed.Presented);
    }

    private bool TryValidateFinalTransitionFocus(
        TransitionScope scope,
        CompletedBuild completed,
        out string error)
    {
        if (!TryReadInt32(completed.Context.Manager + ManagerFocusKeyOffset, out var focusedKey) ||
            !completed.Context.Controls.TryGetValue(focusedKey, out var focused) ||
            completed.Presented.Focus is null || !Equals(completed.Presented.Focus, focused.Focus))
        {
            error = $"{scope.Target} transition final manager focus does not match the exact published target focus.";
            return false;
        }
        // The Hub constructor 0x1DB370 takes its first focus in ECX: 3 from the Ending Log's
        // switchNode(0, 3), 0 from Movies' switchNode(0, 0), 2 and 1 from Sound and Illustrations.
        if (scope.Plan.FinalFocusKey is { } expected && focusedKey != expected)
        {
            error = $"{scope.Source} Back did not restore Hub manager key {expected}.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private void ActivateContext(ActiveContext context, MenuPresented presented)
    {
        lock (stateGate)
        {
            active = context;
            suspended = null;
        }
        PublishOrDefer(presented);
    }

    private void PublishOrDefer(AccessibilityEvent accessibilityEvent)
    {
        if (GetOwnedCallbackScope() is { } callback)
        {
            callback.DeferredEvents.Add(accessibilityEvent);
        }
        else
        {
            dispatcher.Publish(accessibilityEvent);
        }
    }

    private void ClearActiveForTransition()
    {
        ActiveContext? departed;
        lock (stateGate)
        {
            departed = active;
            active = null;
        }
        if (departed is not null)
        {
            QueueExit(departed);
        }
    }

    private static Exception? CaptureTeardownFailure(Action capture)
    {
        try
        {
            capture();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void ReportTeardownFailure(string boundary, Exception? exception)
    {
        if (exception is not null)
        {
            FailCoverage($"{boundary} accessibility cleanup failed after the native original was preserved: {FormatException(exception)}");
        }
    }

    private void ClearMatchingAnyNode(nuint node, string boundary)
    {
        var context = GetActiveContext();
        if (context is null || context.Node != node)
        {
            return;
        }
        if (!TryReadExactVtable(node, ExpectedVtable(context.Surface)))
        {
            FailCoverage($"{boundary} matched the active pointer but not its audited pre-original vtable.");
            return;
        }
        ClearMatchingContext(context, fromExit: true);
    }

    private void ClearMatchingNodeOfAny(nuint node, IReadOnlyList<uint> sharedVtableRvas, string boundary)
    {
        ForgetSuspended(node);
        var context = GetActiveContext();
        if (context is null || context.Node != node)
        {
            return;
        }
        var expected = ExpectedVtable(context.Surface);
        if (!sharedVtableRvas.Contains(expected) || !TryReadExactVtable(node, expected))
        {
            FailCoverage($"{boundary} matched the active pointer but not one of its exact shared surface vtables.");
            return;
        }
        ClearMatchingContext(context, fromExit: false);
    }

    private void ForgetSuspended(nuint node)
    {
        lock (stateGate)
        {
            if (suspended?.Node == node)
            {
                suspended = null;
            }
        }
    }

    private void ClearMatchingNode(nuint node, uint expectedVtableRva, string boundary)
    {
        ForgetSuspended(node);
        var context = GetActiveContext();
        if (context is null || context.Node != node)
        {
            return;
        }
        if (ExpectedVtable(context.Surface) != expectedVtableRva ||
            !TryReadExactVtable(node, expectedVtableRva))
        {
            FailCoverage($"{boundary} matched the active pointer but not its exact surface/vtable identity.");
            return;
        }
        ClearMatchingContext(context, fromExit: false);
    }

    private void ClearMatchingContext(ActiveContext context, bool fromExit)
    {
        var cleared = false;
        lock (stateGate)
        {
            if (ReferenceEquals(active, context))
            {
                active = null;
                cleared = true;
                if (fromExit)
                {
                    // A pushed scene (movie or ending replay) calls onExit on whichever page is
                    // attached, even one a later dispatcher built first, and popping re-enters
                    // that same node. A removed page is destroyed instead and never re-enters.
                    context.SuspendRequested = false;
                    suspended = context;
                }
            }
        }
        if (cleared)
        {
            QueueExit(context);
        }
    }

    private void QueueExit(ActiveContext departed)
    {
        if (GetOwnedCallbackScope() is { } callback)
        {
            callback.ExitOwner = OwnerOf(departed);
        }
        else
        {
            dispatcher.Publish(new MenuExited(OwnerOf(departed)));
        }
    }

    private void ClearActiveStateWithoutEvent()
    {
        lock (stateGate)
        {
            active = null;
            suspended = null;
            dispatchQueues.Clear();
        }
    }

    private ActiveContext? GetActiveContext()
    {
        lock (stateGate)
        {
            return active;
        }
    }

    private void ClearPendingNode()
    {
        if (ReferenceEquals(threadPendingNode?.Owner, this))
        {
            threadPendingNode = null;
        }
    }

    private PendingNode? GetOwnedPendingNode() =>
        ReferenceEquals(threadPendingNode?.Owner, this) ? threadPendingNode : null;

    private void ClearOwnedThreadState()
    {
        if (ReferenceEquals(threadSwitchScope?.Owner, this)) threadSwitchScope = null;
        if (ReferenceEquals(threadBuildScope?.Owner, this)) threadBuildScope = null;
        if (ReferenceEquals(threadCallbackScope?.Owner, this)) threadCallbackScope = null;
        if (ReferenceEquals(threadTransitionScope?.Owner, this)) threadTransitionScope = null;
        if (ReferenceEquals(threadPendingNode?.Owner, this)) threadPendingNode = null;
        if (ReferenceEquals(threadIdleScope?.Owner, this)) threadIdleScope = null;
    }

    private TextScope? GetActiveTextScope()
    {
        if (GetOwnedBuildScope() is { } build) return build;
        if (ReferenceEquals(threadSwitchScope?.Owner, this)) return threadSwitchScope;
        if (GetOwnedTransitionScope() is { } transition) return transition;
        if (GetOwnedCallbackScope() is { } callback) return callback;
        if (ReferenceEquals(threadIdleScope?.Owner, this)) return threadIdleScope;
        return null;
    }

    private ControlScope? GetOwnedControlSink() =>
        GetOwnedBuildScope() is { } build ? build : GetOwnedCallbackScope();

    private BuildScope? GetOwnedBuildScope() =>
        ReferenceEquals(threadBuildScope?.Owner, this) ? threadBuildScope : null;

    private CallbackScope? GetOwnedCallbackScope() =>
        ReferenceEquals(threadCallbackScope?.Owner, this) ? threadCallbackScope : null;

    private TransitionScope? GetOwnedTransitionScope() =>
        ReferenceEquals(threadTransitionScope?.Owner, this) ? threadTransitionScope : null;

    /// <summary>GalleryScene::switchNode 0x2A52B0 builds one page per action.</summary>
    private static bool TryMapAction(int action, out Surface surface)
    {
        surface = action switch
        {
            0 => Surface.Hub,
            1 => Surface.Movies,
            2 => Surface.Sound,
            3 => Surface.EndingLog,
            4 => Surface.EndingDetail,
            5 => Surface.Illustrations,
            _ => default,
        };
        return action is >= 0 and <= 5;
    }

    private static uint ExpectedVtable(Surface surface) => surface switch
    {
        Surface.Hub => ExtrasHubVtableRva,
        Surface.EndingLog => EndingLogVtableRva,
        Surface.EndingDetail => EndingDetailVtableRva,
        Surface.Movies => MoviesVtableRva,
        Surface.Sound => SoundVtableRva,
        Surface.Illustrations => IllustrationsVtableRva,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>The Gallery title switchNode shows for a media page (extra.txt).</summary>
    private static LocalizedKey? PageTitleKey(Surface surface) => surface switch
    {
        Surface.Movies => new LocalizedKey(0x1A, 0x43),
        Surface.Sound => new LocalizedKey(0x1A, 0x44),
        Surface.Illustrations => new LocalizedKey(0x1A, 0x45),
        _ => null,
    };

    private bool TryRequireExactBindings(
        BuildScope scope,
        nuint manager,
        IReadOnlyList<nuint> controls,
        IReadOnlyList<int> keys,
        out string error)
    {
        if (controls.Count != keys.Count)
        {
            error = "Extras control/key correlation has different counts.";
            return false;
        }
        for (var index = 0; index < controls.Count; index++)
        {
            if (!scope.Bindings.Any(binding => binding.Manager == manager &&
                    binding.Control == controls[index] && binding.Key == keys[index]))
            {
                error = $"Extras control position {index} does not match exact manager key {keys[index]}.";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private Dictionary<int, RuntimeControl> BuildControlMap(
        BuildScope scope,
        IReadOnlyList<MenuControlSnapshot> snapshots)
    {
        var byKey = scope.Bindings.ToDictionary(binding => binding.Key);
        return snapshots.ToDictionary(
            snapshot => snapshot.Key,
            snapshot => new RuntimeControl(byKey[snapshot.Key].Control, ToFocus(snapshot)));
    }

    private static MenuPresented ToPresented(
        ActiveContext context,
        MenuControlSnapshot? focus,
        IReadOnlyList<string> status) =>
        ToPresented(context, focus is null ? null : ToFocus(focus), status);

    private static MenuPresented ToPresented(
        ActiveContext context,
        MenuFocus? focus,
        IReadOnlyList<string> status) =>
        new(OwnerOf(context), context.Title, focus, status);

    private static MenuFocus ToFocus(MenuControlSnapshot snapshot) =>
        new(snapshot.Label, snapshot.Value, snapshot.Position, snapshot.Count, snapshot.Help, !snapshot.Enabled);

    private static bool TryRequireOnly(
        IReadOnlyList<TextObservation> observed,
        IReadOnlyList<LocalizedKey> required,
        out Dictionary<LocalizedKey, string> values,
        out string singleValue,
        out string error)
    {
        values = [];
        singleValue = string.Empty;
        if (observed.Count != required.Count)
        {
            error = $"Expected {required.Count} exact localized Extras observations but captured {observed.Count}.";
            return false;
        }
        var remaining = observed.ToList();
        foreach (var key in required)
        {
            var matches = remaining.Where(item => item.Key == key).ToArray();
            if (matches.Length != 1)
            {
                error = $"Localized Extras text ({key.File:X},{key.Message:X}) was missing or ambiguous.";
                return false;
            }
            values[key] = matches[0].Text;
            remaining.Remove(matches[0]);
        }
        if (remaining.Count != 0)
        {
            error = "Extras capture observed an unexpected localized message.";
            return false;
        }
        if (required.Count == 1)
        {
            singleValue = values[required[0]];
        }
        error = string.Empty;
        return true;
    }

    private bool TryReadExactVtable(nuint instance, uint expectedRva) =>
        instance != 0 && TryReadPointer(instance, out var vtable) && vtable == imageBase + expectedRva;

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

    private bool TryReadByte(nuint address, out byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = bytes[0];
        return true;
    }

    private void InitializeBuild(IVerifiedGameBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.ImageBaseAddress == 0)
        {
            throw new InvalidOperationException("Verified image base is unavailable for Extras hooks.");
        }
        if (imageBase != 0 && imageBase != build.ImageBaseAddress)
        {
            throw new InvalidOperationException("Extras hooks received conflicting verified image bases.");
        }
        imageBase = build.ImageBaseAddress;
    }

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

    private static IHookRegistration CreateRegistration(
        HookId id,
        Func<IVerifiedGameBuild, UnmanagedBoundaryGuard, IPreparedHook> prepare) =>
        new HookRegistration(GameVersionCatalog.Get(id).Symbol, prepare);

    private bool TryCaptureActiveEpoch(out int epoch)
    {
        lock (lifecycleGate)
        {
            epoch = activeEpoch;
            return hooksActive && Volatile.Read(ref faulted) == 0;
        }
    }

    private bool RunIfActive(int epoch, Action action)
    {
        lock (lifecycleGate)
        {
            if (!hooksActive || activeEpoch != epoch || Volatile.Read(ref faulted) != 0)
            {
                return false;
            }
            action();
            return true;
        }
    }

    private void RunInstrumentationSafely(int epoch, string context, Action action)
    {
        try
        {
            RunIfActive(epoch, action);
        }
        catch (Exception exception)
        {
            FailCoverage($"{context}: {FormatException(exception)}");
        }
    }

    private void FailCoverage(string diagnostic)
    {
        lock (lifecycleGate)
        {
            if (!hooksActive || Interlocked.Exchange(ref faulted, 1) != 0)
            {
                return;
            }
            ClearActiveStateWithoutEvent();
            ClearOwnedThreadState();
            try
            {
                dispatcher.ReportCoverageFailure(
                    string.IsNullOrWhiteSpace(diagnostic)
                        ? "Extras accessibility coverage failed without a diagnostic."
                        : diagnostic);
            }
            catch (Exception)
            {
                // Shared fanout and the unmanaged boundary provide independent containment.
            }
        }
    }

    private static string FormatException(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";

    private sealed class HookRegistration(
        string name,
        Func<IVerifiedGameBuild, UnmanagedBoundaryGuard, IPreparedHook> prepare) : IHookRegistration
    {
        public string Name { get; } = name;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
            prepare(build, boundary);
    }

    private abstract class TextScope(ExtrasHookSet owner)
    {
        public ExtrasHookSet Owner { get; } = owner;
        public List<TextObservation> Text { get; } = [];
        public List<string> Errors { get; } = [];
    }

    /// <summary>A scope that also records CustomButton construction and manager bindings.</summary>
    private abstract class ControlScope(ExtrasHookSet owner) : TextScope(owner)
    {
        public OrderedSet<nuint> ConstructedControls { get; } = new();
        public List<Binding> Bindings { get; } = [];
    }

    private sealed class SwitchScope(
        ExtrasHookSet owner,
        int epoch,
        nuint scene,
        int action,
        uint rawStackWord) : TextScope(owner)
    {
        public int Epoch { get; } = epoch;
        public nuint Scene { get; } = scene;
        public int Action { get; } = action;
        public uint RawStackWord { get; } = rawStackWord;
    }

    private sealed class TransitionScope(
        ExtrasHookSet owner,
        int epoch,
        Surface source,
        TransitionPlan plan,
        int action,
        nuint scene,
        int selectedEnding) : TextScope(owner)
    {
        public int Epoch { get; } = epoch;
        public Surface Source { get; } = source;
        public TransitionPlan Plan { get; } = plan;
        public Surface? Target => Plan.Target;
        public int Action { get; } = action;
        public nuint Scene { get; } = scene;
        public int SelectedEnding { get; } = selectedEnding;
        public LocalizedKey? ExpectedTitle => Plan.ExpectedTitle;
        public bool NestedSwitchStarted { get; set; }
        public bool SourceWasActive { get; init; }
        public PendingNode? Pending { get; set; }
        public CompletedBuild? Completed { get; set; }
    }

    private sealed class BuildScope(
        ExtrasHookSet owner,
        int epoch,
        Surface surface,
        PendingNode pending) : ControlScope(owner)
    {
        public int Epoch { get; } = epoch;
        public Surface Surface { get; } = surface;
        public PendingNode Pending { get; } = pending;
        public List<FocusObservation> Focus { get; } = [];
    }

    private sealed class CallbackScope : ControlScope
    {
        public CallbackScope(
            ExtrasHookSet owner,
            int epoch,
            ActiveContext context,
            int eventType,
            int action) : base(owner)
        {
            Epoch = epoch;
            Context = context;
            EventType = eventType;
            Action = action;
            if (!owner.TryReadInt32(context.Manager + ManagerFocusKeyOffset, out var entryFocus))
            {
                Errors.Add($"{context.Surface} callback entry focus is unreadable.");
                EntryFocusKey = -1;
            }
            else
            {
                EntryFocusKey = entryFocus;
            }
            // 0x1D9F70 decides Cancel by the playing byte at entry: stop the track, or go Back.
            var wasPlaying = context.Surface == Surface.Sound &&
                owner.TryReadByte(context.Node + SoundPlayingOffset, out var playing) && playing != 0;
            Request = new TransitionRequest(context, epoch, eventType, action, EntryFocusKey, wasPlaying);
        }

        public int Epoch { get; }
        public ActiveContext Context { get; }
        public int EventType { get; }
        public int Action { get; }
        public int EntryFocusKey { get; }
        public TransitionRequest Request { get; }
        public bool RequestConsumed { get; set; }
        public List<FocusObservation> Focus { get; } = [];
        public List<AccessibilityEvent> DeferredEvents { get; } = [];
        public MenuOwner? ExitOwner { get; set; }
    }

    private sealed class IdleScope(ExtrasHookSet owner, int epoch, ActiveContext context) : TextScope(owner)
    {
        public int Epoch { get; } = epoch;
        public ActiveContext Context { get; } = context;
    }

    private sealed record PendingNode(
        ExtrasHookSet Owner,
        int Epoch,
        nuint Scene,
        int Action,
        Surface Surface,
        nuint Node,
        string Title);

    private sealed record CompletedBuild(
        BuildScope Build,
        ActiveContext Context,
        MenuPresented Presented);

    /// <summary>The decide or cancel a page callback saw, which its deferred dispatcher must follow.</summary>
    private sealed record TransitionRequest(
        ActiveContext Context,
        int Epoch,
        int EventType,
        int Action,
        int EntryFocusKey,
        bool SoundWasPlaying);

    /// <summary>One scheduled dispatcher: the request, the action its payload carries, and what
    /// the callback stored natively when it scheduled (Movies row +0x2C8, selected ending).</summary>
    private sealed record QueuedDispatch(
        TransitionRequest Request,
        int DispatchAction,
        int? MovieRow,
        int? SelectedEnding);

    private enum TransitionKind
    {
        Build,
        NestedSwitch,
        Suspending,
        Leaving,
    }

    private sealed record TransitionPlan(
        TransitionKind Kind,
        Surface? Target,
        LocalizedKey? ExpectedTitle,
        int SwitchAction,
        uint SwitchRaw,
        int? FinalFocusKey)
    {
        public static TransitionPlan Leaving { get; } = new(TransitionKind.Leaving, null, null, -1, 0, null);
        public static TransitionPlan Suspending { get; } = new(TransitionKind.Suspending, null, null, -1, 0, null);

        public static TransitionPlan Build(Surface target, LocalizedKey title, int? finalFocusKey) =>
            new(TransitionKind.Build, target, title, -1, 0, finalFocusKey);

        public static TransitionPlan Switch(Surface target, int switchAction, uint raw, int? finalFocusKey) =>
            new(TransitionKind.NestedSwitch, target, null, switchAction, raw, finalFocusKey);
    }

    private sealed record ViewerState(nuint Manager, int Row);

    private sealed record MediaPage(
        Surface Surface,
        int Rows,
        int TitleBank,
        uint TitleTableRva,
        int TitleStride,
        LocalizedKey Status);

    private static readonly MediaPage MoviesPage = new(
        Surface.Movies, MovieCount, 0x10, MoviesTitleTableRva, MediaTableStride, new LocalizedKey(0x1A, 0x3B));

    private static readonly MediaPage SoundPage = new(
        Surface.Sound, TrackCount, 0x01, SoundTitleTableRva, SoundTableStride, new LocalizedKey(0x1A, 0x3D));

    private static readonly MediaPage IllustrationsPage = new(
        Surface.Illustrations, IllustrationCount, 0x10, IllustrationsTitleTableRva, MediaTableStride, new LocalizedKey(0x1A, 0x4E));

    private static MenuOwner OwnerOf(ActiveContext context) => new("Extras", (ulong)context.Node);

    private sealed class ActiveContext(
        Surface surface,
        nuint scene,
        nuint node,
        nuint manager,
        Dictionary<int, RuntimeControl> controls,
        string title)
    {
        public Surface Surface { get; } = surface;
        public nuint Scene { get; } = scene;
        public nuint Node { get; } = node;
        public nuint Manager { get; } = manager;
        public Dictionary<int, RuntimeControl> Controls { get; } = controls;
        public string Title { get; } = new string(title.AsSpan());
        public Dictionary<LocalizedKey, string> HelpCache { get; } = [];
        public ExtrasHubSnapshot? Hub { get; set; }
        public EndingLogSnapshot? EndingLog { get; init; }
        public EndingDetailSnapshot? EndingDetail { get; init; }
        public string BuilderTitle { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        public bool SuspendRequested { get; set; }
        public bool SoundPlaying { get; set; }
        public ViewerState? Viewer { get; set; }
    }

    private sealed class OrderedSet<T> : IReadOnlyCollection<T>
        where T : notnull
    {
        private readonly List<T> ordered = [];
        private readonly HashSet<T> set = [];
        public int Count => ordered.Count;
        public bool Add(T value)
        {
            if (!set.Add(value)) return false;
            ordered.Add(value);
            return true;
        }
        public bool Contains(T value) => set.Contains(value);
        public bool SetEquals(IEnumerable<T> values) => set.SetEquals(values);
        public T[] ToArray() => ordered.ToArray();
        public IEnumerator<T> GetEnumerator() => ordered.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed record Binding(nuint Manager, nuint Control, int Key);
    private sealed record FocusObservation(nuint Manager, int Key);
    private sealed record RuntimeControl(nuint Pointer, MenuFocus Focus);
    private sealed record TextObservation(LocalizedKey Key, string Text);
    private readonly record struct LocalizedKey(int File, int Message);

    private enum Surface
    {
        Hub,
        EndingLog,
        EndingDetail,
        Movies,
        Sound,
        Illustrations,
    }
}
