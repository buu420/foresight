using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
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
using Reloaded.Hooks.Definitions.Enums;

namespace ChronoTriggerAccessibility.Mod.Settings;

/// <summary>
/// Owns the Touch/mouse Settings hooks. Shared menu primitives are observed through
/// <see cref="ISharedNativeHookObserver"/> and their originals remain owned by the fanout root.
/// </summary>
public sealed class TouchSettingsHookSet : IHookActivationObserver, ISharedNativeHookObserver
{
    public const uint FocusableStateControlOffset = 0x14;
    public const uint ManagerKeyOffset = 0x2C4;
    public const uint Permanent1001Offset = 0x2DC;
    public const uint Permanent1002Offset = 0x2E0;
    public const uint Permanent1000Offset = 0x2E4;
    public const uint ActivePageOffset = 0x2E8;
    public const uint FocusKeyOffset = 0x2F8;
    public const uint CustomButtonVtableRva = 0x3A4364;
    public const uint FocusableStateVtableRva = 0x3AC3F4;

    private const string UnsupportedBoundary =
        "This Settings option opens content that is not yet accessible.";
    private const string UnsupportedReturnInstruction =
        "Use Cancel to return to Settings.";

    private static readonly RuntimeAsmHookOptions ProbeOptions = new(
        AsmHookBehaviour.ExecuteFirst,
        HookLength: 5,
        PreferRelativeJump: true,
        MaxOpcodeSize: 5);

    private static readonly HookId[] FunctionHookIds =
    [
        HookId.MenuNodeConfigConstructor,
        HookId.MenuNodeConfigBuilder,
        HookId.MenuNodeConfigDestructor,
        HookId.TouchSettingsPageTransition,
        HookId.TouchSettingsCallback,
        HookId.TouchSettingsValueMutation,
        HookId.TouchSettingsConfirmationBuilderA,
        HookId.TouchSettingsConfirmationBuilderB,
        HookId.TouchSettingsConfirmationCallbackA,
        HookId.TouchSettingsConfirmationCallbackB,
    ];

    private static readonly HookId[] ProbeHookIds =
    [
        HookId.TouchSettingsFirstPermanentControlLabelCallSite,
        HookId.TouchSettingsSecondPermanentControlLabelCallSite,
        HookId.TouchSettingsHeadingLabelCallSite,
        HookId.TouchSettingsConfirmationAChoiceLabelCallSite,
        HookId.TouchSettingsConfirmationBChoiceLabelCallSite,
    ];

    [ThreadStatic]
    private static OuterBuildScope? threadOuterBuild;
    [ThreadStatic]
    private static TransitionScope? threadTransition;
    [ThreadStatic]
    private static CallbackScope? threadCallback;
    [ThreadStatic]
    private static ConfirmationBuildScope? threadConfirmationBuild;
    [ThreadStatic]
    private static PendingMarker? threadMarker;

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IRuntimeNativeAsmHookFactory asmHookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly MsvcStringReader stringReader;
    private readonly object gate = new();
    private readonly HashSet<HookId> preparedIds = [];
    private readonly HashSet<nuint> constructedRoots = [];
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private int epoch;
    private int captureGeneration;
    private int faulted;
    private bool hooksActive;
    private ActiveContext? active;
    private ConfirmationContext? confirmation;

    public TouchSettingsHookSet(
        IRuntimeNativeHookFactory hookFactory,
        IRuntimeNativeAsmHookFactory asmHookFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher)
    {
        this.hookFactory = hookFactory ?? throw new ArgumentNullException(nameof(hookFactory));
        this.asmHookFactory = asmHookFactory ?? throw new ArgumentNullException(nameof(asmHookFactory));
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        stringReader = new MsvcStringReader(memory);

        RequiredHookIds = new ReadOnlyCollection<HookId>(FunctionHookIds.Concat(ProbeHookIds).ToArray());
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            CreateRegistration(HookId.MenuNodeConfigConstructor, PrepareConstructor),
            CreateRegistration(HookId.MenuNodeConfigBuilder, PrepareBuilder),
            CreateRegistration(HookId.MenuNodeConfigDestructor, PrepareDestructor),
            CreateRegistration(HookId.TouchSettingsPageTransition, PreparePageTransition),
            CreateRegistration(HookId.TouchSettingsCallback, PrepareCallback),
            CreateRegistration(HookId.TouchSettingsValueMutation, PrepareMutation),
            CreateRegistration(HookId.TouchSettingsConfirmationBuilderA,
                (build, boundary) => PrepareConfirmationBuilder(ConfirmationKind.A, HookId.TouchSettingsConfirmationBuilderA, build, boundary)),
            CreateRegistration(HookId.TouchSettingsConfirmationBuilderB,
                (build, boundary) => PrepareConfirmationBuilder(ConfirmationKind.B, HookId.TouchSettingsConfirmationBuilderB, build, boundary)),
            CreateRegistration(HookId.TouchSettingsConfirmationCallbackA,
                (build, boundary) => PrepareConfirmationCallback(ConfirmationKind.A, HookId.TouchSettingsConfirmationCallbackA, build, boundary)),
            CreateRegistration(HookId.TouchSettingsConfirmationCallbackB,
                (build, boundary) => PrepareConfirmationCallback(ConfirmationKind.B, HookId.TouchSettingsConfirmationCallbackB, build, boundary)),
            .. ProbeHookIds.Select(id => CreateRegistration(id, (build, boundary) => PrepareProbe(id, build, boundary))),
        ]);
    }

    public IReadOnlyList<HookId> RequiredHookIds { get; }
    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    public void AfterHooksActivated()
    {
        lock (gate)
        {
            if (imageBase == 0 || preparedIds.Count != RequiredHookIds.Count)
            {
                throw new InvalidOperationException("Touch Settings hooks were not fully prepared before activation.");
            }
            if (Volatile.Read(ref faulted) != 0)
            {
                throw new InvalidOperationException("Touch Settings hooks faulted before activation completed.");
            }
            hooksActive = true;
            epoch = unchecked(epoch + 1);
        }
    }

    public void AfterHooksDisabled()
    {
        lock (gate)
        {
            hooksActive = false;
            epoch = unchecked(epoch + 1);
            active = null;
            confirmation = null;
            constructedRoots.Clear();
        }
        ClearThreadState();
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
            if (!TryCaptureEpoch(out _) || GetOwnedConfirmationBuild() is not { } scope)
            {
                return;
            }
            var expected = scope.Kind == ConfirmationKind.A
                ? new LocalizedKey(0x23, 0xD4)
                : new LocalizedKey(0x3A, 5);
            if (fileId != expected.Bank || messageId != expected.Message)
            {
                return;
            }
            if (scope.Prompt is not null)
            {
                scope.Errors.Add("Touch Settings confirmation prompt was observed more than once.");
                return;
            }
            var address = returned != 0 ? (nuint)returned : (nuint)result;
            if (!stringReader.TryRead(address, out var prompt, out var error) || string.IsNullOrWhiteSpace(prompt))
            {
                scope.Errors.Add($"Touch Settings confirmation prompt is unreadable or blank: {error}");
                return;
            }
            scope.Prompt = new string(prompt.AsSpan());
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Touch Settings prompt observation failed: {FormatException(exception)}");
        }
    }

    public void AfterMenuTextLabelFactory(nint position, nint text, nint anchor, int fontSize, nint returned)
    {
        _ = position;
        _ = anchor;
        _ = fontSize;
        var marker = threadMarker;
        threadMarker = null;
        if (marker is null || !ReferenceEquals(marker.Owner, this))
        {
            return;
        }
        try
        {
            var error = "The marked label did not reach the audited UTF-8 read.";
            if (!TryCaptureEpoch(out var currentEpoch) || marker.Epoch != currentEpoch || returned == 0 || text == 0 ||
                !stringReader.TryRead((nuint)text, out var value, out error) || string.IsNullOrWhiteSpace(value))
            {
                marker.AddError($"Touch Settings marked label is unreadable, blank, stale, or returned null: {error}");
                return;
            }
            marker.Consume(new string(value.AsSpan()), (nuint)returned);
        }
        catch (Exception exception)
        {
            marker.AddError($"Touch Settings marked-label observation failed: {FormatException(exception)}");
        }
    }

    public void AfterCustomButtonConstructed(nint storage, nint returned)
    {
        try
        {
            if (!TryCaptureEpoch(out _))
            {
                return;
            }
            var target = (IControlObservationScope?)GetOwnedConfirmationBuild() ??
                (IControlObservationScope?)GetOwnedOuterBuild() ??
                (IControlObservationScope?)GetOwnedTransition();
            if (target is null)
            {
                return;
            }
            var control = (nuint)returned;
            if (storage == 0 || returned != storage || !TryReadExactVtable(control, CustomButtonVtableRva))
            {
                target.Errors.Add(
                    $"Touch Settings CustomButton must return exact typed ECX storage (storage 0x{(nuint)storage:X}, returned 0x{control:X}).");
                return;
            }
            if (!target.Constructed.Add(control))
            {
                target.Errors.Add($"Touch Settings CustomButton 0x{control:X} was constructed more than once.");
                return;
            }
            target.ConstructionOrder.Add(control);
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Touch Settings CustomButton observation failed: {FormatException(exception)}");
        }
    }

    public void AfterControlBound(nint manager, nint focusableState, int managerKey)
    {
        try
        {
            if (!TryCaptureEpoch(out _))
            {
                return;
            }
            var target = (IControlObservationScope?)GetOwnedConfirmationBuild() ??
                (IControlObservationScope?)GetOwnedOuterBuild() ??
                (IControlObservationScope?)GetOwnedTransition();
            if (target is null)
            {
                return;
            }
            var managerAddress = (nuint)manager;
            var stateAddress = (nuint)focusableState;
            if (managerKey < 0 || !TryReadExactVtable(managerAddress, TouchSettingsCapture.InputManagerVtableRva) ||
                !TryReadExactVtable(stateAddress, FocusableStateVtableRva) ||
                !TryReadPointer(stateAddress + FocusableStateControlOffset, out var control) || control == 0 ||
                !TryReadExactVtable(control, CustomButtonVtableRva))
            {
                target.Errors.Add(
                    $"Touch Settings binder key {managerKey} does not match the audited manager/FocusableState/CustomButton layout.");
                return;
            }
            if (target.Bindings.Any(binding => binding.Control == control ||
                    (binding.Manager == managerAddress && binding.Key == managerKey)))
            {
                target.Errors.Add("Touch Settings binder supplied a duplicate control or manager key.");
                return;
            }
            target.Bindings.Add(new Binding(managerAddress, control, managerKey));
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Touch Settings binder observation failed: {FormatException(exception)}");
        }
    }

    public void AfterFocusSet(nint manager, int managerKey)
    {
        try
        {
            if (!TryCaptureEpoch(out var capturedEpoch))
            {
                return;
            }
            var managerAddress = (nuint)manager;
            if (!TryValidateManagerFocus(managerAddress, managerKey, out var error))
            {
                var scope = (IFocusObservationScope?)GetOwnedConfirmationBuild() ??
                    (IFocusObservationScope?)GetOwnedOuterBuild() ??
                    (IFocusObservationScope?)GetOwnedTransition() ??
                    (IFocusObservationScope?)GetOwnedCallback();
                if (scope is not null)
                {
                    scope.Errors.Add(error);
                }
                else
                {
                    FailCoverage(error);
                }
                return;
            }
            var target = (IFocusObservationScope?)GetOwnedConfirmationBuild() ??
                (IFocusObservationScope?)GetOwnedOuterBuild() ??
                (IFocusObservationScope?)GetOwnedTransition() ??
                (IFocusObservationScope?)GetOwnedCallback();
            if (target is not null)
            {
                target.Focus.Add(new FocusObservation(managerAddress, managerKey));
                return;
            }

            ActiveContext? context;
            lock (gate)
            {
                context = hooksActive && epoch == capturedEpoch && Volatile.Read(ref faulted) == 0
                    ? active
                    : null;
            }
            if (context is null || context.Manager != managerAddress)
            {
                return;
            }
            RecaptureAndPublishFocus(context, allowTransition: false, presentIfNeeded: true);
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Touch Settings focus observation failed: {FormatException(exception)}");
        }
    }

    private IPreparedHook PrepareConstructor(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<MenuNodeConfigConstructorDelegate>(HookId.MenuNodeConfigConstructor, build, original => root =>
            boundary.Run("MenuNodeConfig constructor", () => HandleConstructor(original, root), 0));

    private nint HandleConstructor(Func<MenuNodeConfigConstructorDelegate> original, nint root)
    {
        var returned = original()(root);
        if (!TryCaptureEpoch(out _))
        {
            return returned;
        }
        try
        {
            var address = (nuint)root;
            if (root == 0 || returned != root || !TryReadExactVtable(address, TouchSettingsCapture.RootVtableRva))
            {
                FailCoverage(
                    $"MenuNodeConfig constructor did not return its exact ECX root with the audited vtable (root 0x{address:X}, returned 0x{(nuint)returned:X}).");
                return returned;
            }
            lock (gate)
            {
                if (!constructedRoots.Add(address))
                {
                    FailCoverage($"Touch Settings root 0x{address:X} was constructed more than once.");
                }
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Touch Settings constructor observation failed: {FormatException(exception)}");
        }
        return returned;
    }

    private IPreparedHook PrepareBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<MenuNodeConfigBuilderDelegate>(HookId.MenuNodeConfigBuilder, build, original => root =>
            boundary.Run("MenuNodeConfig builder", () => HandleBuilder(original, root)));

    private void HandleBuilder(Func<MenuNodeConfigBuilderDelegate> original, nint rootValue)
    {
        var root = (nuint)rootValue;
        var instrument = TryCaptureEpoch(out var capturedEpoch);
        OuterBuildScope? scope = null;
        string? setupError = null;
        if (instrument)
        {
            try
            {
                lock (gate)
                {
                    if (!constructedRoots.Contains(root) || active is not null ||
                        !TryReadExactVtable(root, TouchSettingsCapture.RootVtableRva))
                    {
                        throw new InvalidOperationException(
                            "Touch Settings builder root is not one exact newly constructed MenuNodeConfig.");
                    }
                }
                if (threadOuterBuild is not null || threadTransition is not null || threadConfirmationBuild is not null)
                {
                    throw new InvalidOperationException("A nested or overlapping Touch Settings outer builder was attempted.");
                }
                scope = new OuterBuildScope(this, capturedEpoch, root);
                threadOuterBuild = scope;
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        Exception? originalFailure = null;
        try
        {
            original()(rootValue);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }
        finally
        {
            if (ReferenceEquals(threadOuterBuild, scope))
            {
                threadOuterBuild = null;
            }
            if (threadMarker is { } marker && ReferenceEquals(marker.Scope, scope))
            {
                marker.AddError("Touch Settings builder returned with an unconsumed label marker.");
                threadMarker = null;
            }
        }

        if (originalFailure is null && instrument)
        {
            try
            {
                if (scope is null)
                {
                    FailCoverage($"Touch Settings builder observation could not start: {setupError}");
                }
                else
                {
                    FinalizeOuterBuild(scope);
                }
            }
            catch (Exception exception)
            {
                FailCoverage($"Touch Settings post-builder capture failed: {FormatException(exception)}");
            }
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private IPreparedHook PrepareDestructor(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<MenuNodeConfigDestructorDelegate>(HookId.MenuNodeConfigDestructor, build, original => root =>
            boundary.Run("MenuNodeConfig destructor", () => HandleDestructor(original, root)));

    private void HandleDestructor(Func<MenuNodeConfigDestructorDelegate> original, nint rootValue)
    {
        var root = (nuint)rootValue;
        var publishExit = false;
        string? failure = null;
        try
        {
            lock (gate)
            {
                constructedRoots.Remove(root);
                if (active is { } context && context.Root == root)
                {
                    active = null;
                    confirmation = null;
                    publishExit = true;
                    if (!TryReadExactVtable(root, TouchSettingsCapture.RootVtableRva))
                    {
                        failure = "Touch Settings destructor root no longer has the audited vtable.";
                    }
                }
            }
            if (publishExit)
            {
                dispatcher.Publish(new MenuExited(OwnerOf(root)));
            }
        }
        catch (Exception exception)
        {
            failure ??= $"Touch Settings destructor publication failed: {FormatException(exception)}";
        }

        Exception? originalFailure = null;
        try
        {
            original()(rootValue);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }
        if (failure is not null)
        {
            FailCoverage(failure);
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private IPreparedHook PrepareCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TouchSettingsCallbackDelegate>(HookId.TouchSettingsCallback, build, original =>
            (context, eventType, key) => boundary.Run("Touch Settings callback", () =>
                HandleCallback(original, context, eventType, key)));

    private void HandleCallback(
        Func<TouchSettingsCallbackDelegate> original,
        nint contextValue,
        int eventType,
        int key)
    {
        var instrument = TryCaptureEpoch(out var capturedEpoch);
        CallbackScope? scope = null;
        string? setupError = null;
        if (instrument)
        {
            try
            {
                var error = string.Empty;
                if (eventType is < 0 or > 3 || threadCallback is not null ||
                    !TryGetValidatedCallbackContext((nuint)contextValue, out var context, out error))
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? "Touch Settings callback event or nesting is outside the audited contract."
                        : error);
                }
                if (!TryCopyCallbackCandidate(context, eventType, key, out var candidate, out error))
                {
                    throw new InvalidOperationException(error);
                }
                scope = new CallbackScope(this, capturedEpoch, context, eventType, key, candidate);
                threadCallback = scope;
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        Exception? originalFailure = null;
        try
        {
            original()(contextValue, eventType, key);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }
        finally
        {
            if (ReferenceEquals(threadCallback, scope))
            {
                threadCallback = null;
            }
        }

        if (originalFailure is null && instrument)
        {
            try
            {
                if (scope is null)
                {
                    FailCoverage($"Touch Settings callback observation could not start: {setupError}");
                }
                else
                {
                    FinalizeCallback(scope);
                }
            }
            catch (Exception exception)
            {
                FailCoverage($"Touch Settings callback post-capture failed: {FormatException(exception)}");
            }
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private IPreparedHook PrepareMutation(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TouchSettingsValueMutationDelegate>(HookId.TouchSettingsValueMutation, build, original =>
            (root, page, row, proposedIndex) => boundary.Run("Touch Settings value mutation", () =>
                HandleMutation(original, root, page, row, proposedIndex), (byte)0));

    private byte HandleMutation(
        Func<TouchSettingsValueMutationDelegate> original,
        nint rootValue,
        int page,
        int row,
        int proposedIndex)
    {
        _ = proposedIndex;
        ActiveContext? candidate = null;
        int capturedEpoch = 0;
        if (TryCaptureEpoch(out capturedEpoch))
        {
            lock (gate)
            {
                if (active is { } current && current.Root == (nuint)rootValue &&
                    current.Snapshot.ActivePageIndex == page && row >= 0 && row < current.Snapshot.Rows.Count)
                {
                    candidate = current;
                }
            }
        }

        var changed = original()(rootValue, page, row, proposedIndex);
        if (changed == 0 || candidate is null)
        {
            return changed;
        }
        try
        {
            lock (gate)
            {
                if (!hooksActive || epoch != capturedEpoch || !ReferenceEquals(active, candidate))
                {
                    return changed;
                }
            }
            if (!TryRecapture(candidate, allowTransition: false, null, out var snapshot, out var error) ||
                snapshot.ActivePageIndex != page || row >= snapshot.Rows.Count ||
                snapshot.Rows[row].NativeRowAddress != candidate.Snapshot.Rows[row].NativeRowAddress)
            {
                FailCoverage(string.IsNullOrWhiteSpace(error)
                    ? "Touch Settings mutation no longer identifies the exact retained page and row."
                    : error);
                return changed;
            }
            ReplaceSnapshotAndPublishFocus(candidate, snapshot, presentIfNeeded: true);
        }
        catch (Exception exception)
        {
            FailCoverage($"Touch Settings mutation post-capture failed: {FormatException(exception)}");
        }
        return changed;
    }

    private IPreparedHook PreparePageTransition(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TouchSettingsPageTransitionDelegate>(HookId.TouchSettingsPageTransition, build, original =>
            (closure, delta) => boundary.Run("Touch Settings page transition", () =>
                HandlePageTransition(original, closure, delta), 0));

    private nint HandlePageTransition(
        Func<TouchSettingsPageTransitionDelegate> original,
        nint closureValue,
        int delta)
    {
        var instrument = TryCaptureEpoch(out var capturedEpoch);
        TransitionScope? scope = null;
        string? setupError = null;
        if (instrument)
        {
            try
            {
                ActiveContext? context;
                lock (gate)
                {
                    context = active;
                }
                if (context is null || closureValue == 0 || delta is not (-1 or 1) ||
                    threadTransition is not null || threadOuterBuild is not null ||
                    !TryReadExactVtable(context.Root, TouchSettingsCapture.RootVtableRva))
                {
                    throw new InvalidOperationException(
                        "Touch Settings page-transition closure, delta, root, or nesting is outside the audited contract.");
                }
                scope = new TransitionScope(this, capturedEpoch, (nuint)closureValue, context, delta);
                threadTransition = scope;
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        nint returned = 0;
        Exception? originalFailure = null;
        try
        {
            returned = original()(closureValue, delta);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }
        finally
        {
            if (ReferenceEquals(threadTransition, scope))
            {
                threadTransition = null;
            }
        }

        if (originalFailure is null && instrument)
        {
            try
            {
                if (scope is null)
                {
                    FailCoverage($"Touch Settings page-transition observation could not start: {setupError}");
                }
                else
                {
                    FinalizeTransition(scope, (nuint)returned);
                }
            }
            catch (Exception exception)
            {
                FailCoverage($"Touch Settings transition post-capture failed: {FormatException(exception)}");
            }
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
        return returned;
    }

    private IPreparedHook PrepareConfirmationBuilder(
        ConfirmationKind kind,
        HookId id,
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TouchSettingsNestedBuilderDelegate>(id, build, original => root =>
            boundary.Run(GameVersionCatalog.Get(id).Symbol, () => HandleConfirmationBuilder(kind, original, root)));

    private void HandleConfirmationBuilder(
        ConfirmationKind kind,
        Func<TouchSettingsNestedBuilderDelegate> original,
        nint rootValue)
    {
        var instrument = TryCaptureEpoch(out var capturedEpoch);
        ConfirmationBuildScope? scope = null;
        string? setupError = null;
        if (instrument)
        {
            try
            {
                ActiveContext? context;
                lock (gate)
                {
                    context = active;
                }
                if (context is null || context.Root != (nuint)rootValue || threadConfirmationBuild is not null ||
                    !TryReadExactVtable(context.Root, TouchSettingsCapture.RootVtableRva))
                {
                    throw new InvalidOperationException("Touch Settings confirmation builder does not own the exact active root.");
                }
                scope = new ConfirmationBuildScope(this, capturedEpoch, kind, context);
                threadConfirmationBuild = scope;
            }
            catch (Exception exception)
            {
                setupError = FormatException(exception);
            }
        }

        Exception? originalFailure = null;
        try
        {
            original()(rootValue);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }
        finally
        {
            if (ReferenceEquals(threadConfirmationBuild, scope))
            {
                threadConfirmationBuild = null;
            }
            if (threadMarker is { } marker && ReferenceEquals(marker.Scope, scope))
            {
                marker.AddError("Touch Settings confirmation returned with an unconsumed choice marker.");
                threadMarker = null;
            }
        }

        if (originalFailure is null && instrument)
        {
            try
            {
                if (scope is null)
                {
                    FailCoverage($"Touch Settings confirmation builder observation could not start: {setupError}");
                }
                else
                {
                    FinalizeConfirmationBuild(scope);
                }
            }
            catch (Exception exception)
            {
                FailCoverage($"Touch Settings confirmation post-builder capture failed: {FormatException(exception)}");
            }
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private IPreparedHook PrepareConfirmationCallback(
        ConfirmationKind kind,
        HookId id,
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TouchSettingsCallbackDelegate>(id, build, original => (context, eventType, key) =>
            boundary.Run(GameVersionCatalog.Get(id).Symbol, () =>
                HandleConfirmationCallback(kind, original, context, eventType, key)));

    private void HandleConfirmationCallback(
        ConfirmationKind kind,
        Func<TouchSettingsCallbackDelegate> original,
        nint contextValue,
        int eventType,
        int key)
    {
        ConfirmationContext? candidate = null;
        string? label = null;
        var instrument = TryCaptureEpoch(out var capturedEpoch);
        if (instrument)
        {
            lock (gate)
            {
                if (confirmation is { } current && current.Kind == kind &&
                    current.Owner.Root == active?.Root &&
                    TryReadPointer((nuint)contextValue, out var manager) && manager == current.Manager &&
                    TryReadPointer((nuint)contextValue + 4, out var root) && root == current.Owner.Root &&
                    current.Choices.TryGetValue(key, out var copied))
                {
                    candidate = current;
                    label = new string(copied.AsSpan());
                }
            }
            if (eventType is < 0 or > 2 || candidate is null)
            {
                FailCoverage("Touch Settings confirmation callback does not match its exact overlay/root/manager/key.");
            }
        }

        original()(contextValue, eventType, key);
        if (!instrument || candidate is null || label is null)
        {
            return;
        }
        try
        {
            lock (gate)
            {
                if (!hooksActive || epoch != capturedEpoch || !ReferenceEquals(active, candidate.Owner))
                {
                    return;
                }
            }
            if (eventType == 1)
            {
                dispatcher.Publish(new MenuConfirmationFocused(label, candidate.IndexByKey[key], candidate.Choices.Count));
            }
            else if (eventType == 0)
            {
                dispatcher.Publish(new MenuActivated(label));
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Touch Settings confirmation callback publication failed: {FormatException(exception)}");
        }
    }

    private IPreparedHook PrepareProbe(
        HookId id,
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary)
    {
        InitializeBuild(build, id);
        var address = RequireAddress(build, id);
        NativeCallSiteProbeDelegate callback = () => boundary.Run(
            GameVersionCatalog.Get(id).Symbol,
            () => ObserveProbe(id));
        return asmHookFactory.CreateAsmHook(
            id,
            GameVersionCatalog.Get(id).Symbol,
            callback,
            address,
            NativeCallSiteProbeAssembly.Build,
            ProbeOptions);
    }

    private void ObserveProbe(HookId id)
    {
        try
        {
            if (!TryCaptureEpoch(out var capturedEpoch))
            {
                threadMarker = null;
                return;
            }
            if (threadMarker is not null)
            {
                threadMarker.AddError("A second Touch Settings label marker arrived before the first was consumed.");
                threadMarker = null;
                return;
            }
            if (id is HookId.TouchSettingsConfirmationAChoiceLabelCallSite or
                HookId.TouchSettingsConfirmationBChoiceLabelCallSite)
            {
                var scope = GetOwnedConfirmationBuild();
                var expected = id == HookId.TouchSettingsConfirmationAChoiceLabelCallSite
                    ? ConfirmationKind.A
                    : ConfirmationKind.B;
                if (scope is null || scope.Kind != expected)
                {
                    FailCoverage("Touch Settings confirmation choice marker escaped its matching nested builder scope.");
                    return;
                }
                threadMarker = PendingMarker.ForConfirmation(this, capturedEpoch, scope);
                return;
            }
            var outer = GetOwnedOuterBuild();
            if (outer is null)
            {
                FailCoverage("Touch Settings outer label marker escaped its exact builder scope.");
                return;
            }
            threadMarker = PendingMarker.ForOuter(this, capturedEpoch, outer, id);
        }
        catch (Exception exception)
        {
            FailCoverage($"Touch Settings call-site marker failed: {FormatException(exception)}");
        }
    }

    private void FinalizeOuterBuild(OuterBuildScope scope)
    {
        if (!IsCurrentScope(scope) || scope.Errors.Count > 0)
        {
            FailCoverage(scope.Errors.Count == 0
                ? "Touch Settings builder epoch or owning thread changed before finalization."
                : string.Join(" ", scope.Errors));
            return;
        }
        if (!TryReadPointer(scope.Root + Permanent1000Offset, out var control1000) || control1000 != 0)
        {
            FailCoverage(
                "Touch Settings exposed unproven root+0x2E4/key 1000; no localized label or semantics are audited for it.");
            return;
        }
        if (scope.Heading is null || scope.PermanentLabels.Count != 2 || scope.Focus.Count != 1 ||
            !TryReadInt32(scope.Root + ActivePageOffset, out var page))
        {
            FailCoverage("Touch Settings builder is missing its exact heading, permanent labels, page, or single initial focus.");
            return;
        }
        var expected = new[] { (Key: 1001, Offset: Permanent1001Offset), (Key: 1002, Offset: Permanent1002Offset) };
        var observations = new List<TouchSettingsSpecialControlObservation>(2);
        nuint manager = 0;
        foreach (var item in expected)
        {
            if (!TryReadPointer(scope.Root + item.Offset, out var control) || control == 0 ||
                !TryReadExactVtable(control, CustomButtonVtableRva) ||
                !scope.PermanentLabels.TryGetValue(item.Key, out var label) || label.Control != control)
            {
                FailCoverage($"Touch Settings permanent control key {item.Key} does not match its exact root field, construction, and label source.");
                return;
            }
            var bindings = scope.Bindings.Where(binding => binding.Key == item.Key && binding.Control == control).ToArray();
            if (bindings.Length != 1 || (manager != 0 && manager != bindings[0].Manager))
            {
                FailCoverage($"Touch Settings permanent control key {item.Key} has no unique exact manager binding.");
                return;
            }
            manager = bindings[0].Manager;
            observations.Add(new TouchSettingsSpecialControlObservation(
                NextCaptureGeneration(), scope.Root, page, item.Key, control, label.Text));
        }
        var generation = observations[0].CaptureGeneration;
        if (observations.Any(observation => observation.CaptureGeneration != generation))
        {
            // Re-stamp as one atomic capture generation.
            generation = NextCaptureGeneration();
            observations = observations.Select(observation => observation with { CaptureGeneration = generation }).ToList();
        }
        var focus = scope.Focus[0];
        if (focus.Manager != manager || !TryFocusedControl(scope.Bindings, manager, focus.Key, out var focusedControl))
        {
            FailCoverage("Touch Settings initial focus does not match its exact bound manager/control.");
            return;
        }
        var managerObservation = new TouchSettingsManagerObservation(
            generation, scope.Root, page, manager, focusedControl);
        if (!TouchSettingsCapture.TryCreateSnapshot(
                memory, imageBase, scope.Root, generation, managerObservation, observations,
                out var snapshot, out var diagnostic))
        {
            FailCoverage(diagnostic);
            return;
        }
        var labels = observations.ToDictionary(
            observation => observation.Key,
            observation => new RuntimeSpecial(observation.ControlAddress, observation.Label));
        var bindingsByKey = scope.Bindings
            .Where(binding => binding.Manager == manager)
            .GroupBy(binding => binding.Key)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Control);
        var context = new ActiveContext(
            scope.Epoch, scope.Root, scope.Heading.Text, manager, labels, bindingsByKey, snapshot);
        lock (gate)
        {
            if (!hooksActive || epoch != scope.Epoch || Volatile.Read(ref faulted) != 0 || active is not null)
            {
                return;
            }
            active = context;
            constructedRoots.Remove(scope.Root);
        }
        if (snapshot.FocusKind != TouchSettingsFocusKind.None)
        {
            QueueInitialPresentation(context);
        }
    }

    private void FinalizeCallback(CallbackScope scope)
    {
        if (!IsCurrentScope(scope) || scope.Errors.Count > 0)
        {
            FailCoverage(scope.Errors.Count == 0
                ? "Touch Settings callback epoch or owning thread changed before finalization."
                : string.Join(" ", scope.Errors));
            return;
        }
        lock (gate)
        {
            if (!ReferenceEquals(active, scope.Context))
            {
                return;
            }
        }
        if (scope.EventType == 0 && scope.Candidate is not null)
        {
            dispatcher.Publish(scope.Candidate);
        }
        if (scope.EventType == 1)
        {
            RecaptureAndPublishFocus(scope.Context, allowTransition: false, presentIfNeeded: true);
        }
        foreach (var deferred in scope.DeferredEvents)
        {
            dispatcher.Publish(deferred);
        }
    }

    private void FinalizeTransition(TransitionScope scope, nuint returned)
    {
        if (!IsCurrentScope(scope) || scope.Errors.Count > 0)
        {
            FailCoverage(scope.Errors.Count == 0
                ? "Touch Settings transition epoch or owning thread changed before finalization."
                : string.Join(" ", scope.Errors));
            return;
        }
        if (!TryRecapture(scope.Context, allowTransition: true, scope, out var snapshot, out var diagnostic) ||
            snapshot.ActivePageIndex == scope.Context.Snapshot.ActivePageIndex ||
            returned != snapshot.NativePageRootAddress || snapshot.NativePagerAddress != scope.Context.Snapshot.NativePagerAddress)
        {
            FailCoverage(string.IsNullOrWhiteSpace(diagnostic)
                ? "Touch Settings transition did not return the exact new page owned by the retained root and pager."
                : diagnostic);
            return;
        }
        ReplaceSnapshotAndPublishFocus(scope.Context, snapshot, presentIfNeeded: true);
    }

    private void FinalizeConfirmationBuild(ConfirmationBuildScope scope)
    {
        if (!IsCurrentScope(scope) || scope.Errors.Count > 0 || scope.Prompt is null ||
            scope.Choices.Count == 0 || scope.Focus.Count != 1)
        {
            FailCoverage(scope.Errors.Count == 0
                ? "Touch Settings confirmation is missing its exact prompt, localized choices, or initial focus."
                : string.Join(" ", scope.Errors));
            return;
        }
        var manager = scope.Focus[0].Manager;
        var bindings = scope.Bindings.Where(binding => binding.Manager == manager).ToArray();
        if (bindings.Length != scope.Choices.Count || bindings.Select(binding => binding.Key).Distinct().Count() != bindings.Length)
        {
            FailCoverage("Touch Settings confirmation choices do not match one exact unique manager binding map.");
            return;
        }
        var choices = new Dictionary<int, string>();
        foreach (var binding in bindings.OrderBy(binding => binding.Key))
        {
            var choice = scope.Choices.SingleOrDefault(candidate => candidate.Control == binding.Control);
            if (choice is null || !TryReadExactVtable(binding.Control, CustomButtonVtableRva))
            {
                FailCoverage("Touch Settings confirmation choice label/control/binder correlation is incomplete.");
                return;
            }
            choices.Add(binding.Key, choice.Text);
        }
        var focusedKey = scope.Focus[0].Key;
        if (!choices.ContainsKey(focusedKey))
        {
            FailCoverage("Touch Settings confirmation initial focus does not identify one localized choice.");
            return;
        }
        var ordered = choices.OrderBy(pair => pair.Key).ToArray();
        var indexByKey = ordered.Select((pair, index) => (pair.Key, index)).ToDictionary(item => item.Key, item => item.index);
        var context = new ConfirmationContext(scope.Kind, scope.Context, manager, choices, indexByKey);
        lock (gate)
        {
            if (!hooksActive || epoch != scope.Epoch || !ReferenceEquals(active, scope.Context))
            {
                return;
            }
            confirmation = context;
        }
        var presented = new MenuConfirmationPresented(
            scope.Prompt,
            ordered.Select(pair => pair.Value).ToArray(),
            indexByKey[focusedKey]);
        var callback = GetOwnedCallback();
        if (callback is not null && ReferenceEquals(callback.Context, scope.Context))
        {
            callback.DeferredEvents.Add(presented);
        }
        else
        {
            dispatcher.Publish(presented);
        }
    }

    private bool TryGetValidatedCallbackContext(
        nuint closure,
        out ActiveContext context,
        out string error)
    {
        context = null!;
        error = string.Empty;
        ActiveContext? candidate;
        lock (gate)
        {
            candidate = active;
        }
        if (candidate is null || !TryReadPointer(closure, out var manager) || manager != candidate.Manager ||
            !TryReadPointer(closure + 4, out var root) || root != candidate.Root ||
            !TryReadInt32(closure + 8, out var page) || page != candidate.Snapshot.ActivePageIndex ||
            !TryReadExactVtable(root, TouchSettingsCapture.RootVtableRva))
        {
            error = "Touch Settings callback closure is not the exact [manager, root, page] tuple for the active capture.";
            return false;
        }
        context = candidate;
        return true;
    }

    private bool TryCopyCallbackCandidate(
        ActiveContext context,
        int eventType,
        int key,
        out AccessibilityEvent? candidate,
        out string error)
    {
        candidate = null;
        error = string.Empty;
        if (eventType != 0)
        {
            return true;
        }
        if (key == 1000)
        {
            error = "Touch Settings activated unproven key 1000; its localized label and semantics are unavailable.";
            return false;
        }
        if (key is 1001 or 1002)
        {
            if (!context.Specials.TryGetValue(key, out var special))
            {
                error = $"Touch Settings special activation key {key} has no retained localized control.";
                return false;
            }
            candidate = new MenuActivated(new string(special.Label.AsSpan()));
            return true;
        }
        if (key < 0)
        {
            error = $"Touch Settings activation key {key} is outside the audited row/special ranges.";
            return false;
        }
        var rowIndex = key / 4;
        if (rowIndex >= context.Snapshot.Rows.Count)
        {
            error = $"Touch Settings activation key {key} decodes outside the retained rows.";
            return false;
        }
        var row = context.Snapshot.Rows[rowIndex];
        candidate = row.UiType == 2
            ? new MenuUnsupported(new string(row.Label.AsSpan()), UnsupportedBoundary, UnsupportedReturnInstruction)
            : new MenuActivated(new string(row.Label.AsSpan()));
        return true;
    }

    private bool TryRecapture(
        ActiveContext context,
        bool allowTransition,
        TransitionScope? transition,
        out TouchSettingsSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = null!;
        diagnostic = string.Empty;
        if (!TryReadInt32(context.Root + ActivePageOffset, out var page) ||
            !TryReadInt32(context.Root + FocusKeyOffset, out var focusKey))
        {
            diagnostic = "Touch Settings root page/focus state is unreadable.";
            return false;
        }
        var bindings = transition is not null && transition.Bindings.Count > 0
            ? transition.Bindings.Where(binding => binding.Manager == context.Manager)
                .GroupBy(binding => binding.Key)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single().Control)
            : context.BoundControls;
        if (!TryFocusedControl(bindings, focusKey, out var focusedControl))
        {
            diagnostic = $"Touch Settings focus key {focusKey} has no exact observed bound control.";
            return false;
        }
        var generation = NextCaptureGeneration();
        var specials = context.Specials.Select(pair => new TouchSettingsSpecialControlObservation(
            generation, context.Root, page, pair.Key, pair.Value.Control, pair.Value.Label)).ToArray();
        var manager = new TouchSettingsManagerObservation(
            generation, context.Root, page, context.Manager, focusedControl);
        var captured = allowTransition
            ? TouchSettingsCapture.TryCreateTransitionSnapshot(
                memory, imageBase, context.Root, generation, manager, specials, out snapshot, out diagnostic)
            : TouchSettingsCapture.TryCreateSnapshot(
                memory, imageBase, context.Root, generation, manager, specials, out snapshot, out diagnostic);
        if (captured && transition is not null && transition.Bindings.Count > 0)
        {
            context.BoundControls = bindings;
        }
        return captured;
    }

    private void RecaptureAndPublishFocus(ActiveContext context, bool allowTransition, bool presentIfNeeded)
    {
        if (!TryRecapture(context, allowTransition, null, out var snapshot, out var diagnostic))
        {
            FailCoverage(diagnostic);
            return;
        }
        ReplaceSnapshotAndPublishFocus(context, snapshot, presentIfNeeded);
    }

    private void ReplaceSnapshotAndPublishFocus(
        ActiveContext context,
        TouchSettingsSnapshot snapshot,
        bool presentIfNeeded)
    {
        lock (gate)
        {
            if (!hooksActive || !ReferenceEquals(active, context) || Volatile.Read(ref faulted) != 0)
            {
                return;
            }
            context.Snapshot = snapshot;
            if (!context.Presented)
            {
                if (!presentIfNeeded || snapshot.FocusKind == TouchSettingsFocusKind.None)
                {
                    return;
                }
                context.Presented = true;
                dispatcher.Publish(new MenuPresented(
                    OwnerOf(context.Root), context.Heading, ToFocus(snapshot), []));
                return;
            }
            dispatcher.Publish(new MenuFocusChanged(ToFocus(snapshot)));
        }
    }

    private void QueueInitialPresentation(ActiveContext context)
    {
        // The Touch builder can run inside the top-menu action-4 native stack. Posting
        // avoids presenting Settings before that caller publishes activation and exit.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1).ConfigureAwait(false);
            try
            {
                lock (gate)
                {
                    if (!hooksActive || !ReferenceEquals(active, context) || context.Presented ||
                        context.Snapshot.FocusKind == TouchSettingsFocusKind.None || Volatile.Read(ref faulted) != 0)
                    {
                        return;
                    }
                    context.Presented = true;
                    dispatcher.Publish(new MenuPresented(
                        OwnerOf(context.Root), context.Heading, ToFocus(context.Snapshot), []));
                }
            }
            catch (Exception exception)
            {
                FailCoverage($"Deferred Touch Settings presentation failed: {FormatException(exception)}");
            }
        });
    }

    private static MenuFocus ToFocus(TouchSettingsSnapshot snapshot)
    {
        var count = snapshot.Rows.Count + snapshot.SpecialControls.Count;
        return snapshot.FocusKind switch
        {
            TouchSettingsFocusKind.Row when snapshot.FocusedRowIndex is int rowIndex =>
                new MenuFocus(
                    snapshot.Rows[rowIndex].Label,
                    snapshot.Rows[rowIndex].Value,
                    rowIndex + 1,
                    count,
                    snapshot.Rows[rowIndex].Help,
                    Disabled: false),
            TouchSettingsFocusKind.SpecialControl when snapshot.FocusedSpecialKey is int key =>
                ToSpecialFocus(snapshot, key, count),
            _ => throw new InvalidOperationException("Touch Settings has no accessible focus to publish."),
        };
    }

    private static MenuFocus ToSpecialFocus(TouchSettingsSnapshot snapshot, int key, int count)
    {
        var ordered = snapshot.SpecialControls.OrderBy(control => control.Key).ToArray();
        var index = Array.FindIndex(ordered, control => control.Key == key);
        if (index < 0)
        {
            throw new InvalidOperationException("Touch Settings special focus has no retained localized control.");
        }
        return new MenuFocus(ordered[index].Label, null, snapshot.Rows.Count + index + 1, count, null, Disabled: false);
    }

    private bool TryValidateManagerFocus(nuint manager, int requested, out string error)
    {
        if (!TryReadExactVtable(manager, TouchSettingsCapture.InputManagerVtableRva) ||
            !TryReadInt32(manager + ManagerKeyOffset, out var authoritative) || authoritative != requested)
        {
            error = $"Touch Settings manager requested focus key {requested}, but its exact +0x2C4 state is unreadable or different.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryFocusedControl(
        IEnumerable<Binding> bindings,
        nuint manager,
        int key,
        out nuint control)
    {
        control = 0;
        if (key == -1)
        {
            return true;
        }
        var exact = bindings.Where(binding => binding.Manager == manager && binding.Key == key).ToArray();
        if (exact.Length != 1)
        {
            return false;
        }
        control = exact[0].Control;
        return true;
    }

    private static bool TryFocusedControl(
        IReadOnlyDictionary<int, nuint> bindings,
        int key,
        out nuint control)
    {
        control = 0;
        return key == -1 || bindings.TryGetValue(key, out control);
    }

    private int NextCaptureGeneration() => Interlocked.Increment(ref captureGeneration);

    private bool IsCurrentScope(IOwnedScope scope) =>
        scope.OwnerThreadId == Environment.CurrentManagedThreadId &&
        TryCaptureEpoch(out var capturedEpoch) && capturedEpoch == scope.Epoch;

    private OuterBuildScope? GetOwnedOuterBuild() =>
        threadOuterBuild is { } scope && ReferenceEquals(scope.Owner, this) ? scope : null;

    private TransitionScope? GetOwnedTransition() =>
        threadTransition is { } scope && ReferenceEquals(scope.Owner, this) ? scope : null;

    private CallbackScope? GetOwnedCallback() =>
        threadCallback is { } scope && ReferenceEquals(scope.Owner, this) ? scope : null;

    private ConfirmationBuildScope? GetOwnedConfirmationBuild() =>
        threadConfirmationBuild is { } scope && ReferenceEquals(scope.Owner, this) ? scope : null;

    private bool TryCaptureEpoch(out int capturedEpoch)
    {
        lock (gate)
        {
            capturedEpoch = epoch;
            return hooksActive && imageBase != 0 && Volatile.Read(ref faulted) == 0;
        }
    }

    private void InitializeBuild(IVerifiedGameBuild build, HookId id)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.ImageBaseAddress == 0 || (ulong)build.ImageBaseAddress > uint.MaxValue)
        {
            throw new InvalidOperationException("Verified x86 image base is unavailable for Touch Settings hooks.");
        }
        lock (gate)
        {
            if (imageBase != 0 && imageBase != build.ImageBaseAddress)
            {
                throw new InvalidOperationException("Touch Settings hooks received conflicting verified image bases.");
            }
            imageBase = build.ImageBaseAddress;
            if (!preparedIds.Add(id))
            {
                throw new InvalidOperationException($"Touch Settings hook '{id}' was prepared more than once.");
            }
        }
    }

    private static nuint RequireAddress(IVerifiedGameBuild build, HookId id)
    {
        if (!build.HookAddresses.TryGetValue(id, out var address) || address == 0 || (ulong)address > uint.MaxValue)
        {
            throw new InvalidOperationException($"Verified x86 address for required Touch Settings hook '{id}' is missing.");
        }
        return address;
    }

    private IPreparedHook PrepareHook<TDelegate>(
        HookId id,
        IVerifiedGameBuild build,
        Func<Func<TDelegate>, TDelegate> createDetour)
        where TDelegate : Delegate
    {
        InitializeBuild(build, id);
        var address = RequireAddress(build, id);
        TDelegate? original = null;
        TDelegate GetOriginal() => original ?? throw new InvalidOperationException($"Original function for '{id}' is not bound.");
        var detour = createDetour(GetOriginal);
        var hook = hookFactory.CreateHook(id, detour, address);
        original = hook.OriginalFunction;
        return new ReloadedPreparedHook<TDelegate>(GameVersionCatalog.Get(id).Symbol, hook, detour);
    }

    private static IHookRegistration CreateRegistration(
        HookId id,
        Func<IVerifiedGameBuild, UnmanagedBoundaryGuard, IPreparedHook> prepare) =>
        new HookRegistration(GameVersionCatalog.Get(id).Symbol, prepare);

    private bool TryReadExactVtable(nuint address, uint rva) =>
        imageBase != 0 && (ulong)imageBase + rva <= uint.MaxValue &&
        TryReadPointer(address, out var vtable) && vtable == imageBase + rva;

    private bool TryReadPointer(nuint address, out nuint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        if (!FitsX86Range(address, bytes.Length) || !memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private bool TryReadInt32(nuint address, out int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        if (!FitsX86Range(address, bytes.Length) || !memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private static bool FitsX86Range(nuint address, int size) =>
        address != 0 && address <= uint.MaxValue && size > 0 && (ulong)address + (uint)size - 1 <= uint.MaxValue;

    private void FailCoverage(string message)
    {
        if (Interlocked.Exchange(ref faulted, 1) != 0)
        {
            return;
        }
        lock (gate)
        {
            active = null;
            confirmation = null;
            constructedRoots.Clear();
        }
        try
        {
            dispatcher.ReportCoverageFailure(string.IsNullOrWhiteSpace(message)
                ? "Touch Settings coverage failed without a diagnostic."
                : message);
        }
        catch (Exception)
        {
            // No instrumentation exception may cross the native boundary.
        }
    }

    private static void ClearThreadState()
    {
        threadOuterBuild = null;
        threadTransition = null;
        threadCallback = null;
        threadConfirmationBuild = null;
        threadMarker = null;
    }

    private static string FormatException(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";

    private sealed class HookRegistration(
        string name,
        Func<IVerifiedGameBuild, UnmanagedBoundaryGuard, IPreparedHook> prepare) : IHookRegistration
    {
        public string Name { get; } = name;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) => prepare(build, boundary);
    }

    private interface IOwnedScope
    {
        TouchSettingsHookSet Owner { get; }
        int Epoch { get; }
        int OwnerThreadId { get; }
        List<string> Errors { get; }
    }

    private interface IControlObservationScope : IOwnedScope
    {
        HashSet<nuint> Constructed { get; }
        List<nuint> ConstructionOrder { get; }
        List<Binding> Bindings { get; }
    }

    private interface IFocusObservationScope : IOwnedScope
    {
        List<FocusObservation> Focus { get; }
    }

    private sealed class OuterBuildScope(TouchSettingsHookSet owner, int epoch, nuint root) :
        IControlObservationScope, IFocusObservationScope
    {
        public TouchSettingsHookSet Owner { get; } = owner;
        public int Epoch { get; } = epoch;
        public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;
        public nuint Root { get; } = root;
        public List<string> Errors { get; } = [];
        public HashSet<nuint> Constructed { get; } = [];
        public List<nuint> ConstructionOrder { get; } = [];
        public List<Binding> Bindings { get; } = [];
        public List<FocusObservation> Focus { get; } = [];
        public Dictionary<int, LabeledControl> PermanentLabels { get; } = [];
        public LabelObservation? Heading { get; set; }
    }

    private sealed class TransitionScope(
        TouchSettingsHookSet owner,
        int epoch,
        nuint closure,
        ActiveContext context,
        int delta) : IControlObservationScope, IFocusObservationScope
    {
        public TouchSettingsHookSet Owner { get; } = owner;
        public int Epoch { get; } = epoch;
        public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;
        public nuint Closure { get; } = closure;
        public ActiveContext Context { get; } = context;
        public int Delta { get; } = delta;
        public List<string> Errors { get; } = [];
        public HashSet<nuint> Constructed { get; } = [];
        public List<nuint> ConstructionOrder { get; } = [];
        public List<Binding> Bindings { get; } = [];
        public List<FocusObservation> Focus { get; } = [];
    }

    private sealed class CallbackScope(
        TouchSettingsHookSet owner,
        int epoch,
        ActiveContext context,
        int eventType,
        int key,
        AccessibilityEvent? candidate) : IFocusObservationScope
    {
        public TouchSettingsHookSet Owner { get; } = owner;
        public int Epoch { get; } = epoch;
        public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;
        public ActiveContext Context { get; } = context;
        public int EventType { get; } = eventType;
        public int Key { get; } = key;
        public AccessibilityEvent? Candidate { get; } = candidate;
        public List<string> Errors { get; } = [];
        public List<FocusObservation> Focus { get; } = [];
        public List<AccessibilityEvent> DeferredEvents { get; } = [];
    }

    private sealed class ConfirmationBuildScope(
        TouchSettingsHookSet owner,
        int epoch,
        ConfirmationKind kind,
        ActiveContext context) : IControlObservationScope, IFocusObservationScope
    {
        public TouchSettingsHookSet Owner { get; } = owner;
        public int Epoch { get; } = epoch;
        public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;
        public ConfirmationKind Kind { get; } = kind;
        public ActiveContext Context { get; } = context;
        public List<string> Errors { get; } = [];
        public HashSet<nuint> Constructed { get; } = [];
        public List<nuint> ConstructionOrder { get; } = [];
        public List<Binding> Bindings { get; } = [];
        public List<FocusObservation> Focus { get; } = [];
        public string? Prompt { get; set; }
        public List<LabeledControl> Choices { get; } = [];
    }

    private sealed class PendingMarker
    {
        private readonly Action<string, nuint> consume;
        private readonly Action<string> addError;

        private PendingMarker(
            TouchSettingsHookSet owner,
            int epoch,
            object scope,
            Action<string, nuint> consume,
            Action<string> addError)
        {
            Owner = owner;
            Epoch = epoch;
            Scope = scope;
            this.consume = consume;
            this.addError = addError;
        }

        public TouchSettingsHookSet Owner { get; }
        public int Epoch { get; }
        public object Scope { get; }
        public void Consume(string text, nuint label) => consume(text, label);
        public void AddError(string error) => addError(error);

        public static PendingMarker ForOuter(
            TouchSettingsHookSet owner,
            int epoch,
            OuterBuildScope scope,
            HookId id) => new(owner, epoch, scope, (text, label) =>
            {
                if (id == HookId.TouchSettingsHeadingLabelCallSite)
                {
                    if (scope.Heading is not null)
                    {
                        scope.Errors.Add("Touch Settings heading label was observed more than once.");
                    }
                    else
                    {
                        scope.Heading = new LabelObservation(text, label);
                    }
                    return;
                }
                var key = id == HookId.TouchSettingsFirstPermanentControlLabelCallSite ? 1001 : 1002;
                if (scope.ConstructionOrder.Count == 0)
                {
                    scope.Errors.Add($"Touch Settings permanent label key {key} was not preceded by a CustomButton construction.");
                    return;
                }
                var control = scope.ConstructionOrder[^1];
                if (!scope.PermanentLabels.TryAdd(key, new LabeledControl(control, text, label)))
                {
                    scope.Errors.Add($"Touch Settings permanent label key {key} was observed more than once.");
                }
            }, scope.Errors.Add);

        public static PendingMarker ForConfirmation(
            TouchSettingsHookSet owner,
            int epoch,
            ConfirmationBuildScope scope) => new(owner, epoch, scope, (text, label) =>
            {
                if (scope.ConstructionOrder.Count == 0)
                {
                    scope.Errors.Add("Touch Settings confirmation choice was not preceded by a CustomButton construction.");
                    return;
                }
                var control = scope.ConstructionOrder[^1];
                if (scope.Choices.Any(choice => choice.Control == control))
                {
                    scope.Errors.Add("Touch Settings confirmation choice control received more than one label.");
                    return;
                }
                scope.Choices.Add(new LabeledControl(control, text, label));
            }, scope.Errors.Add);
    }

    private static MenuOwner OwnerOf(nuint root) => new("TouchSettings", (ulong)root);

    private sealed class ActiveContext(
        int epoch,
        nuint root,
        string heading,
        nuint manager,
        Dictionary<int, RuntimeSpecial> specials,
        IReadOnlyDictionary<int, nuint> boundControls,
        TouchSettingsSnapshot snapshot)
    {
        public int Epoch { get; } = epoch;
        public nuint Root { get; } = root;
        public string Heading { get; } = new string(heading.AsSpan());
        public nuint Manager { get; } = manager;
        public Dictionary<int, RuntimeSpecial> Specials { get; } = specials;
        public IReadOnlyDictionary<int, nuint> BoundControls { get; set; } = boundControls;
        public TouchSettingsSnapshot Snapshot { get; set; } = snapshot;
        public bool Presented { get; set; }
    }

    private sealed record ConfirmationContext(
        ConfirmationKind Kind,
        ActiveContext Owner,
        nuint Manager,
        IReadOnlyDictionary<int, string> Choices,
        IReadOnlyDictionary<int, int> IndexByKey);

    private sealed record RuntimeSpecial(nuint Control, string Label);
    private sealed record Binding(nuint Manager, nuint Control, int Key);
    private sealed record FocusObservation(nuint Manager, int Key);
    private sealed record LabelObservation(string Text, nuint Label);
    private sealed record LabeledControl(nuint Control, string Text, nuint Label);
    private readonly record struct LocalizedKey(int Bank, int Message);
    private enum ConfirmationKind { A, B }
}
