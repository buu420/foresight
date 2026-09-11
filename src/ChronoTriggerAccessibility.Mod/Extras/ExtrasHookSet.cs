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

    private const int EndingCount = 19;
    private const int EndingRecordStride = 0x2C;
    private const int MaximumSelectedEnding = EndingCount - 1;

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

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly MsvcStringReader stringReader;
    private readonly object lifecycleGate = new();
    private readonly object stateGate = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private ActiveContext? active;
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
            if (!TryCaptureActiveEpoch(out _) || GetOwnedBuildScope() is not { } scope)
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
            if (!TryCaptureActiveEpoch(out _) || GetOwnedBuildScope() is not { } scope)
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
                    if (transition.Source != Surface.EndingLog || transition.Action != 4 ||
                        transition.Scene != (nuint)galleryScene || action != 0 || rawStackWord != 3 ||
                        transition.NestedSwitchStarted)
                    {
                        throw new InvalidOperationException(
                            "The enclosing Ending Log Back transition did not invoke exactly one Gallery switch action 0/raw word 3 for its scene.");
                    }
                    transition.NestedSwitchStarted = true;
                }
                else
                {
                    ClearPendingNode();
                    ClearActiveForTransition();
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

    private IPreparedHook PrepareLogDeletingDestructor(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingLogDeletingDestructorDelegate>(HookId.EndingLogDeletingDestructor, build, original =>
            (node, deletingFlags) => boundary.Run(
                "Ending Log deleting destructor",
                () =>
                {
                    var captureFailure = CaptureTeardownFailure(
                        () => ClearMatchingNode(
                            (nuint)node,
                            EndingLogVtableRva,
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

    private IPreparedHook PrepareLogTransition(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasLogTransitionDelegate>(HookId.ExtrasLogTransition, build, original => payload =>
            boundary.Run(
                "Extras Ending Log transition",
                () => HandleTransition(Surface.EndingLog, payload, () => original()(payload))));

    private IPreparedHook PrepareDetailTransition(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ExtrasDetailTransitionDelegate>(HookId.ExtrasDetailTransition, build, original => payload =>
            boundary.Run(
                "Extras Ending Detail transition",
                () => HandleTransition(Surface.EndingDetail, payload, () => original()(payload))));

    private void HandleTransition(Surface source, nint payload, Action invokeOriginal)
    {
        var instrument = TryCaptureActiveEpoch(out var epoch);
        TransitionScope? scope = null;
        string? setupError = null;
        var preserveUnsupportedReview = false;
        var unsupportedReviewValidated = false;
        if (instrument)
        {
            try
            {
                if (!TryReadInt32((nuint)payload, out var action))
                {
                    throw new InvalidOperationException(
                        $"{source} transition payload 0x{(nuint)payload:X} has no readable action.");
                }
                preserveUnsupportedReview = source == Surface.EndingDetail && action == 3;
                if (preserveUnsupportedReview)
                {
                    ValidateUnsupportedReviewTransition((nuint)payload, epoch);
                    unsupportedReviewValidated = true;
                }
                else
                {
                    scope = BeginTransition(source, (nuint)payload, action, epoch);
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
            if (ReferenceEquals(threadTransitionScope, scope))
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
            if (preserveUnsupportedReview)
            {
                if (!unsupportedReviewValidated)
                {
                    FailCoverage($"{source} Review transition validation failed: {setupError}");
                }
                return;
            }
            if (scope is null)
            {
                FailCoverage($"{source} transition validation failed: {setupError}");
                return;
            }
            FinalizeTransition(scope);
        });
    }

    private void ValidateUnsupportedReviewTransition(nuint payload, int epoch)
    {
        if (threadTransitionScope is not null || threadSwitchScope is not null ||
            threadBuildScope is not null || threadPendingNode is not null)
        {
            throw new InvalidOperationException(
                "Ending Detail Review was nested inside another Extras transition, switch, or build observation.");
        }
        if (!TryReadPointer(payload + 4, out var scene) || scene == 0)
        {
            throw new InvalidOperationException(
                "Ending Detail Review payload has no readable Gallery scene identity.");
        }

        var callback = GetOwnedCallbackScope();
        var context = GetActiveContext();
        if (callback is null || callback.Epoch != epoch || callback.ReviewTransitionObserved ||
            callback.Context.Surface != Surface.EndingDetail || context is null ||
            !ReferenceEquals(callback.Context, context) || context.Scene != scene)
        {
            throw new InvalidOperationException(
                "Ending Detail Review does not belong to one exact active Detail callback and Gallery scene.");
        }
        if (!TryValidateActiveContext(context, out var error))
        {
            throw new InvalidOperationException(error);
        }
        if (callback.EventType != 0 || callback.Action != 0 || callback.EntryFocusKey != 0 ||
            !context.Controls.TryGetValue(0, out var review) || review.Focus.Position != 1 ||
            review.Focus.Disabled)
        {
            throw new InvalidOperationException(
                "Ending Detail Review is not related to its exact focused, enabled Review control callback.");
        }
        callback.ReviewTransitionObserved = true;
    }

    private TransitionScope BeginTransition(Surface source, nuint payload, int action, int epoch)
    {
        if (threadTransitionScope is not null || threadSwitchScope is not null || threadBuildScope is not null)
        {
            throw new InvalidOperationException("A nested or overlapping Extras transition observation was attempted.");
        }
        if (!TryReadPointer(payload + 4, out var scene) || scene == 0)
        {
            throw new InvalidOperationException($"{source} transition payload has no readable Gallery scene identity.");
        }
        var target = (source, action) switch
        {
            (Surface.EndingLog, 3) => Surface.EndingDetail,
            (Surface.EndingLog, 4) => Surface.Hub,
            (Surface.EndingDetail, 4) => Surface.EndingLog,
            _ => throw new InvalidOperationException(
                $"{source} transition action {action} is not an audited menu transition."),
        };
        var callback = GetOwnedCallbackScope();
        var context = GetActiveContext();
        if (callback is null || callback.Epoch != epoch || callback.Context.Surface != source ||
            context is null || !ReferenceEquals(callback.Context, context) || context.Scene != scene)
        {
            throw new InvalidOperationException(
                $"{source} transition does not belong to the exact active callback and Gallery scene.");
        }
        if (!TryValidateActiveContext(context, out var error))
        {
            throw new InvalidOperationException(error);
        }
        if (!TryValidateTransitionCallbackRelationship(callback, action, out var selectedEnding, out error))
        {
            throw new InvalidOperationException(error);
        }

        LocalizedKey? expectedTitle = null;
        if (source == Surface.EndingLog && action == 3)
        {
            if (!TryReadInt32(
                    imageBase + EndingRecordTableRva + (nuint)(selectedEnding * EndingRecordStride),
                    out var messageId))
            {
                throw new InvalidOperationException(
                    "Ending Log transition cannot read the exact selected ending title message ID.");
            }
            expectedTitle = new LocalizedKey(0x0F, messageId);
        }
        else if (source == Surface.EndingDetail && action == 4)
        {
            expectedTitle = new LocalizedKey(0x1A, 0x0F);
        }
        return new TransitionScope(
            this,
            epoch,
            source,
            target,
            action,
            scene,
            selectedEnding,
            expectedTitle);
    }

    private bool TryValidateTransitionCallbackRelationship(
        CallbackScope callback,
        int transitionAction,
        out int selectedEnding,
        out string error)
    {
        selectedEnding = -1;
        if (callback.Context.Surface == Surface.EndingLog && transitionAction == 3)
        {
            if (callback.EventType != 0 || callback.Action < 0 || callback.Action > MaximumSelectedEnding ||
                callback.EntryFocusKey != callback.Action ||
                !callback.Context.Controls.TryGetValue(callback.Action, out var row) || row.Focus.Disabled ||
                !TryReadInt32(imageBase + SelectedEndingGlobalRva, out selectedEnding) ||
                selectedEnding != callback.Action)
            {
                error = "Ending Log detail transition is not related to one focused, unlocked selected row.";
                return false;
            }
            error = string.Empty;
            return true;
        }
        if (callback.Context.Surface == Surface.EndingLog && transitionAction == 4)
        {
            if (!IsExactBackCallback(callback, expectedPosition: 20))
            {
                error = "Ending Log Hub transition is not related to its exact Back callback.";
                return false;
            }
            error = string.Empty;
            return true;
        }
        if (callback.Context.Surface == Surface.EndingDetail && transitionAction == 4)
        {
            if (!IsExactBackCallback(callback, expectedPosition: 2) ||
                !TryReadInt32(imageBase + SelectedEndingGlobalRva, out selectedEnding) ||
                selectedEnding < 0 || selectedEnding > MaximumSelectedEnding)
            {
                error = "Ending Detail Log transition is not related to its exact Back callback and selected ending.";
                return false;
            }
            error = string.Empty;
            return true;
        }
        error = $"{callback.Context.Surface} transition action {transitionAction} has no audited callback relationship.";
        return false;
    }

    private static bool IsExactBackCallback(CallbackScope callback, int expectedPosition)
    {
        if (callback.EventType == 2)
        {
            return callback.Context.Controls.Values.Count(control => control.Focus.Position == expectedPosition) == 1;
        }
        return callback.EventType == 0 && callback.EntryFocusKey == callback.Action &&
            callback.Context.Controls.TryGetValue(callback.Action, out var control) &&
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
            if (transition.Source != Surface.EndingLog || transition.Action != 4 ||
                transition.Target != Surface.Hub || transition.Scene != scope.Scene ||
                scope.Action != 0 || scope.RawStackWord != 3 || transition.Pending is not null)
            {
                FailCoverage("Ending Log Back nested switch does not match its exact Hub transition relationship.");
                return;
            }
            transition.Pending = pending;
        }
        threadPendingNode = pending;
    }

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
        }
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
            _ => null,
        };
        if (Volatile.Read(ref faulted) != 0)
        {
            return;
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
            if (scope.Action is 0 or 1 or 2)
            {
                return new MenuUnsupported(
                    control.Focus.Label,
                    "Detailed reading is not yet covered on this screen.",
                    "Use Back to return to Extras.");
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
            return scope.Action == 0
                ? new MenuUnsupported(
                    control.Focus.Label,
                    "Ending review playback is not yet covered.",
                    "Use Back to return to the Ending Log.")
                : new MenuActivated(control.Focus.Label);
        }
        if (scope.EventType == 2 && context.Controls.TryGetValue(1, out var back))
        {
            return new MenuActivated(back.Focus.Label);
        }
        return scope.Focus.Count > 0 ? FinalizeCallbackFocus(scope) : null;
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
        if (!TryReadPointer(closure, out var manager) || manager != context.Manager ||
            !TryReadPointer(closure + 4, out var nodePointer) || nodePointer != context.Node ||
            !TryReadPointer(closure + 8, out var vector) || vector == 0)
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
        if (scope.Source == Surface.EndingLog && scope.Action == 4)
        {
            if (!scope.NestedSwitchStarted || scope.Text.Count != 0)
            {
                FailCoverage(
                    "Ending Log Back did not complete through one exact nested Hub switch without outer title observations.");
                return;
            }
        }
        else
        {
            if (!TryResolveDirectTransitionTitle(scope, out var title, out var titleError) ||
                scope.Pending is null || !string.Equals(scope.Pending.Title, title, StringComparison.Ordinal))
            {
                FailCoverage(string.IsNullOrWhiteSpace(titleError)
                    ? $"{scope.Target} transition title no longer matches its exact pending target."
                    : titleError);
                return;
            }
        }
        var pending = scope.Pending;
        var completed = scope.Completed;
        var expectedPendingAction = scope.Source == Surface.EndingLog && scope.Action == 4 ? 0 : scope.Action;
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
        if (scope.Source == Surface.EndingLog && scope.Action == 4 && focusedKey != 3)
        {
            error = "Ending Log Back did not restore Hub manager key 3 from its exact action 0/raw word 3 switch.";
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
        ClearMatchingContext(context);
    }

    private void ClearMatchingNode(nuint node, uint expectedVtableRva, string boundary)
    {
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
        ClearMatchingContext(context);
    }

    private void ClearMatchingContext(ActiveContext context)
    {
        var cleared = false;
        lock (stateGate)
        {
            if (ReferenceEquals(active, context))
            {
                active = null;
                cleared = true;
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
    }

    private TextScope? GetActiveTextScope()
    {
        if (GetOwnedBuildScope() is { } build) return build;
        if (ReferenceEquals(threadSwitchScope?.Owner, this)) return threadSwitchScope;
        if (GetOwnedTransitionScope() is { } transition) return transition;
        if (GetOwnedCallbackScope() is { } callback) return callback;
        return null;
    }

    private BuildScope? GetOwnedBuildScope() =>
        ReferenceEquals(threadBuildScope?.Owner, this) ? threadBuildScope : null;

    private CallbackScope? GetOwnedCallbackScope() =>
        ReferenceEquals(threadCallbackScope?.Owner, this) ? threadCallbackScope : null;

    private TransitionScope? GetOwnedTransitionScope() =>
        ReferenceEquals(threadTransitionScope?.Owner, this) ? threadTransitionScope : null;

    private static bool TryMapAction(int action, out Surface surface)
    {
        surface = action switch
        {
            0 => Surface.Hub,
            3 => Surface.EndingLog,
            4 => Surface.EndingDetail,
            _ => default,
        };
        return action is 0 or 3 or 4;
    }

    private static uint ExpectedVtable(Surface surface) => surface switch
    {
        Surface.Hub => ExtrasHubVtableRva,
        Surface.EndingLog => EndingLogVtableRva,
        Surface.EndingDetail => EndingDetailVtableRva,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
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
        Surface target,
        int action,
        nuint scene,
        int selectedEnding,
        LocalizedKey? expectedTitle) : TextScope(owner)
    {
        public int Epoch { get; } = epoch;
        public Surface Source { get; } = source;
        public Surface Target { get; } = target;
        public int Action { get; } = action;
        public nuint Scene { get; } = scene;
        public int SelectedEnding { get; } = selectedEnding;
        public LocalizedKey? ExpectedTitle { get; } = expectedTitle;
        public bool NestedSwitchStarted { get; set; }
        public PendingNode? Pending { get; set; }
        public CompletedBuild? Completed { get; set; }
    }

    private sealed class BuildScope(
        ExtrasHookSet owner,
        int epoch,
        Surface surface,
        PendingNode pending) : TextScope(owner)
    {
        public int Epoch { get; } = epoch;
        public Surface Surface { get; } = surface;
        public PendingNode Pending { get; } = pending;
        public OrderedSet<nuint> ConstructedControls { get; } = new();
        public List<Binding> Bindings { get; } = [];
        public List<FocusObservation> Focus { get; } = [];
    }

    private sealed class CallbackScope : TextScope
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
        }

        public int Epoch { get; }
        public ActiveContext Context { get; }
        public int EventType { get; }
        public int Action { get; }
        public int EntryFocusKey { get; }
        public List<FocusObservation> Focus { get; } = [];
        public List<AccessibilityEvent> DeferredEvents { get; } = [];
        public MenuOwner? ExitOwner { get; set; }
        public bool ReviewTransitionObserved { get; set; }
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
    }
}
