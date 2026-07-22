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
/// Runtime narration for the original Steam/classic Settings implementation.
/// Audited call-site probes prove which live native strings were rendered; the
/// hardened native capture then clones and revalidates the complete menu state.
/// </summary>
public sealed class SteamSettingsHookSet : IHookActivationObserver, ISharedNativeHookObserver
{
    private const uint ContextOffset = 0x2F8;
    private const uint DescriptorVectorOffset = 0x2FC;
    private const uint CategoryVectorOffset = 0x308;
    private const uint ManagerFocusKeyOffset = 0x2C4;
    private const int DescriptorStride = 0x0C;
    private const int CategoryStride = 0x30;
    private const int RowStride = 0x88;
    private const int RowValueVectorOffset = 0x24;
    private const int StringStride = MsvcStringReader.LayoutSize;

    private const string MenuTitle = "Settings";
    private const string UnsupportedBoundary = "This Settings page is not accessible yet.";

    private static readonly HookId[] FunctionHookIds =
    [
        HookId.MenuNodeConfigSteamConstructor,
        HookId.MenuNodeConfigSteamBuilder,
        HookId.MenuNodeConfigSteamDestructor,
        HookId.MenuNodeConfigSteamPageBuilder,
        HookId.SteamSettingsCategoryCallback,
        HookId.SteamSettingsRowCallback,
        HookId.SteamSettingsLicenseCallback,
        HookId.SteamSettingsSetterInvoker,
        HookId.SteamSettingsResolutionBuilder,
        HookId.SteamSettingsResolutionCallback,
        HookId.SteamSettingsConfirmationBuilderA,
        HookId.SteamSettingsConfirmationBuilderB,
    ];

    private static readonly ProbeDefinition[] ProbeDefinitions =
    [
        new(HookId.SteamSettingsCategoryLabelCallSite, ProbeKind.CategoryLabel, BuildKind.Outer),
        new(HookId.SteamSettingsRowLabelCallSite, ProbeKind.RowLabel, BuildKind.Page),
        new(HookId.SteamSettingsSelectedValueLabelCallSite, ProbeKind.SelectedValue, BuildKind.Page),
        new(HookId.SteamSettingsResolutionHeadingLabelCallSite, ProbeKind.OverlayLabel, BuildKind.Resolution),
        new(HookId.SteamSettingsResolutionEntryLabelCallSite, ProbeKind.OverlayLabel, BuildKind.Resolution),
        new(HookId.SteamSettingsConfirmationAChoiceLabelCallSite, ProbeKind.OverlayLabel, BuildKind.ConfirmationA),
        new(HookId.SteamSettingsConfirmationBChoiceLabelCallSite, ProbeKind.OverlayLabel, BuildKind.ConfirmationB),
    ];

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IRuntimeNativeAsmHookFactory asmHookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly MsvcStringReader stringReader;
    private readonly object gate = new();
    private readonly HashSet<HookId> preparedIds = [];
    private readonly HashSet<nuint> roots = [];
    private readonly ThreadLocal<PendingMarker?> pendingMarker = new();
    private readonly ThreadLocal<int> callbackDepth = new(() => 0);
    private readonly ThreadLocal<bool> nestedUnsupported = new(() => false);
    private readonly IReadOnlyList<IHookRegistration> registrations;

    private nuint imageBase;
    private BuildScope? outerBuild;
    private BuildScope? pageBuild;
    private BuildScope? overlayBuild;
    private ActiveState? active;
    private DeferredPresentation? deferredPresentation;
    private int epoch;
    private int captureGeneration;
    private int faulted;
    private bool hooksActive;

    public SteamSettingsHookSet(
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

        RequiredHookIds = new ReadOnlyCollection<HookId>(
            FunctionHookIds.Concat(ProbeDefinitions.Select(probe => probe.Id)).ToArray());
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            CreateRegistration(HookId.MenuNodeConfigSteamConstructor, PrepareConstructor),
            CreateRegistration(HookId.MenuNodeConfigSteamBuilder, PrepareBuilder),
            CreateRegistration(HookId.MenuNodeConfigSteamDestructor, PrepareDestructor),
            CreateRegistration(HookId.MenuNodeConfigSteamPageBuilder, PreparePageBuilder),
            CreateRegistration(HookId.SteamSettingsCategoryCallback, PrepareCategoryCallback),
            CreateRegistration(HookId.SteamSettingsRowCallback, PrepareRowCallback),
            CreateRegistration(HookId.SteamSettingsLicenseCallback, PrepareLicenseCallback),
            CreateRegistration(HookId.SteamSettingsSetterInvoker, PrepareSetterInvoker),
            CreateRegistration(HookId.SteamSettingsResolutionBuilder, PrepareResolutionBuilder),
            CreateRegistration(HookId.SteamSettingsResolutionCallback, PrepareResolutionCallback),
            CreateRegistration(HookId.SteamSettingsConfirmationBuilderA, PrepareConfirmationBuilderA),
            CreateRegistration(HookId.SteamSettingsConfirmationBuilderB, PrepareConfirmationBuilderB),
            .. ProbeDefinitions.Select(probe => CreateRegistration(
                probe.Id,
                (build, boundary) => PrepareProbe(probe, build, boundary))),
        ]);
    }

    public IReadOnlyList<HookId> RequiredHookIds { get; }
    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    public void AfterHooksActivated()
    {
        lock (gate)
        {
            if (imageBase == 0 || !preparedIds.SetEquals(RequiredHookIds))
            {
                throw new InvalidOperationException("Steam Settings hooks were not fully prepared before activation.");
            }
            if (Volatile.Read(ref faulted) != 0)
            {
                throw new InvalidOperationException("Steam Settings accessibility faulted before activation.");
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
            roots.Clear();
            active = null;
            deferredPresentation = null;
            CancelScopesLocked();
        }
        pendingMarker.Value = null;
        callbackDepth.Value = 0;
        nestedUnsupported.Value = false;
    }

    /// <summary>
    /// Publishes the first Settings presentation after its caller has finished
    /// its own native action transaction. This is intentionally idempotent so
    /// TopMenu can call it after action 4 has emitted activation and exit.
    /// </summary>
    public void FlushDeferredPresentation()
    {
        DeferredPresentation? pending;
        lock (gate)
        {
            pending = deferredPresentation;
            if (pending is null || !hooksActive || Volatile.Read(ref faulted) != 0 ||
                active is null || active.Root != pending.Root || active.Generation != pending.Generation)
            {
                return;
            }
            deferredPresentation = null;
        }
        PublishSafely(pending.Event);
    }

    public void AfterMenuTextLabelFactory(nint position, nint text, nint anchor, int fontSize, nint returned)
    {
        _ = position;
        _ = anchor;
        _ = fontSize;
        var marker = pendingMarker.Value;
        if (marker is null)
        {
            return;
        }
        pendingMarker.Value = null;
        try
        {
            var scope = marker.Value.Scope;
            if (!IsOwnedLiveScope(scope) || scope.Kind != marker.Value.ExpectedScope)
            {
                scope.AddError("A Steam Settings label marker escaped its exact native builder scope.");
                return;
            }
            var diagnostic = string.Empty;
            if (returned == 0 || text == 0 ||
                !stringReader.TryRead((nuint)text, out var value, out diagnostic) ||
                string.IsNullOrWhiteSpace(value))
            {
                scope.AddError($"An audited Steam Settings label was not rendered as readable UTF-8: {diagnostic}");
                return;
            }
            RecordMarkedLabel(scope, marker.Value.Kind, (nuint)text, value);
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Steam Settings label observation failed: {FormatException(exception)}");
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
        _ = fileId;
        _ = messageId;
        try
        {
            if (!TryGetOwnedScope(BuildKind.Resolution, out var scope) &&
                !TryGetOwnedScope(BuildKind.ConfirmationA, out scope) &&
                !TryGetOwnedScope(BuildKind.ConfirmationB, out scope))
            {
                return;
            }
            var address = returned != 0 ? (nuint)returned : (nuint)result;
            if (address != 0 && stringReader.TryRead(address, out var prompt, out _) &&
                !string.IsNullOrWhiteSpace(prompt))
            {
                scope.AddPrompt(prompt);
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Steam Settings message observation failed: {FormatException(exception)}");
        }
    }

    public void AfterFocusSet(nint manager, int managerKey)
    {
        try
        {
            ActiveState? basis;
            lock (gate)
            {
                basis = hooksActive && Volatile.Read(ref faulted) == 0 ? active : null;
            }
            if (basis is null || IsAnyBuildOwnedByCurrentThread() || !OwnsManager(basis.Snapshot, (nuint)manager))
            {
                return;
            }
            if (!TryRefreshActive(basis, basis.Observations, out var refreshed, out var diagnostic) ||
                refreshed.Snapshot.FocusKey != managerKey)
            {
                FailCoverage(string.IsNullOrWhiteSpace(diagnostic)
                    ? "Steam Settings focus setter did not commit the observed manager key."
                    : diagnostic);
                return;
            }
            if (callbackDepth.Value == 0)
            {
                PublishSafely(new MenuFocusChanged(ToFocus(refreshed.Snapshot)));
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Steam Settings focus observation failed: {FormatException(exception)}");
        }
    }

    public void AfterCustomButtonConstructed(nint storage, nint returned)
    {
        try
        {
            if (TryGetOwnedOverlay(out var scope))
            {
                if (storage == 0 || returned != storage)
                {
                    scope.AddError("A Steam Settings overlay button did not return its exact ECX storage.");
                }
                else
                {
                    scope.AddConstructed((nuint)returned);
                }
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Steam Settings button observation failed: {FormatException(exception)}");
        }
    }

    public void AfterControlBound(nint manager, nint focusableState, int managerKey)
    {
        try
        {
            if (TryGetOwnedOverlay(out var scope))
            {
                scope.AddBinding((nuint)manager, (nuint)focusableState, managerKey);
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Steam Settings binder observation failed: {FormatException(exception)}");
        }
    }

    private IPreparedHook PrepareConstructor(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<MenuNodeConfigSteamConstructorDelegate>(HookId.MenuNodeConfigSteamConstructor, build, original =>
            (instance, context) => boundary.Run(
                "MenuNodeConfigSteam constructor",
                () => HandleConstructor(original, instance, context),
                0));

    private IPreparedHook PrepareBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<MenuNodeConfigSteamBuilderDelegate>(HookId.MenuNodeConfigSteamBuilder, build, original => instance =>
            boundary.Run("MenuNodeConfigSteam builder", () =>
                HandleBuild(BuildKind.Outer, (nuint)instance, -1, () => original()(instance))));

    private IPreparedHook PrepareDestructor(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<MenuNodeConfigSteamDestructorDelegate>(HookId.MenuNodeConfigSteamDestructor, build, original => instance =>
            boundary.Run("MenuNodeConfigSteam destructor", () => HandleDestructor(original, instance)));

    private IPreparedHook PreparePageBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<MenuNodeConfigSteamPageBuilderDelegate>(HookId.MenuNodeConfigSteamPageBuilder, build, original =>
            (root, pageIndex) => boundary.Run("MenuNodeConfigSteam page builder", () =>
                HandleBuild(BuildKind.Page, (nuint)root, checked((int)pageIndex), () => original()(root, pageIndex))));

    private IPreparedHook PrepareCategoryCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsCategoryCallback, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings category callback", () =>
                HandleCallback(CallbackKind.Category, original, context, eventType, key)));

    private IPreparedHook PrepareRowCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsRowCallback, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings row callback", () =>
                HandleCallback(CallbackKind.Row, original, context, eventType, key)));

    private IPreparedHook PrepareLicenseCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsLicenseCallback, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings license callback", () =>
                HandleCallback(CallbackKind.License, original, context, eventType, key)));

    private IPreparedHook PrepareSetterInvoker(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsSetterInvokerDelegate>(HookId.SteamSettingsSetterInvoker, build, original =>
            (setter, newIndex) => boundary.Run("Steam Settings setter invoker", () =>
                HandleSetter(original, setter, newIndex)));

    private IPreparedHook PrepareResolutionBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsResolutionBuilder, build, original => root =>
            boundary.Run("Steam Settings resolution builder", () =>
                HandleOverlayBuild(BuildKind.Resolution, (nuint)root, () => original()(root))));

    private IPreparedHook PrepareResolutionCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsResolutionCallback, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings resolution callback", () =>
                HandleCallback(CallbackKind.Resolution, original, context, eventType, key)));

    private IPreparedHook PrepareConfirmationBuilderA(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsConfirmationBuilderA, build, original => root =>
            boundary.Run("Steam Settings confirmation A builder", () =>
                HandleOverlayBuild(BuildKind.ConfirmationA, (nuint)root, () => original()(root))));

    private IPreparedHook PrepareConfirmationBuilderB(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsConfirmationBuilderB, build, original => root =>
            boundary.Run("Steam Settings confirmation B builder", () =>
                HandleOverlayBuild(BuildKind.ConfirmationB, (nuint)root, () => original()(root))));

    private IPreparedHook PrepareProbe(
        ProbeDefinition definition,
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary)
    {
        InitializeBuild(build, definition.Id);
        var address = RequireAddress(build, definition.Id);
        NativeCallSiteProbeDelegate callback = () => boundary.Run(
            GameVersionCatalog.Get(definition.Id).Symbol,
            () => ObserveProbe(definition));
        return asmHookFactory.CreateAsmHook(
            definition.Id,
            GameVersionCatalog.Get(definition.Id).Symbol,
            callback,
            address,
            NativeCallSiteProbeAssembly.Build,
            new RuntimeAsmHookOptions(
                AsmHookBehaviour.ExecuteFirst,
                HookLength: 5,
                PreferRelativeJump: true,
                MaxOpcodeSize: 5));
    }

    private nint HandleConstructor(
        Func<MenuNodeConfigSteamConstructorDelegate> original,
        nint instance,
        int context)
    {
        var returned = original()(instance, context);
        var root = (nuint)returned;
        if (instance == 0 || returned != instance || context is not 0 and not 1 ||
            !TryReadExactVtable(root, SteamSettingsCapture.RootVtableRva) ||
            !TryReadInt32(root + ContextOffset, out var committedContext) || committedContext != context)
        {
            FailCoverage("MenuNodeConfigSteam constructor did not return exact ECX with its audited vtable and context.");
            return returned;
        }
        lock (gate)
        {
            if (hooksActive && Volatile.Read(ref faulted) == 0 && !roots.Add(root))
            {
                FailCoverage("MenuNodeConfigSteam constructed the same live root more than once.");
            }
        }
        return returned;
    }

    private void HandleDestructor(Func<MenuNodeConfigSteamDestructorDelegate> original, nint instance)
    {
        var root = (nuint)instance;
        var announceExit = false;
        lock (gate)
        {
            roots.Remove(root);
            if (active?.Root == root)
            {
                active = null;
                announceExit = true;
            }
            if (deferredPresentation?.Root == root)
            {
                deferredPresentation = null;
            }
            if (outerBuild?.Root == root || pageBuild?.Root == root || overlayBuild?.Root == root)
            {
                CancelScopesLocked();
            }
        }
        original()(instance);
        if (announceExit)
        {
            PublishSafely(new MenuExited());
        }
    }

    private void HandleBuild(BuildKind kind, nuint root, int pageIndex, Action callOriginal)
    {
        if (!TryStartScope(kind, root, pageIndex, out var scope, out var setupDiagnostic))
        {
            callOriginal();
            FailCoverage(setupDiagnostic);
            return;
        }

        Exception? originalFailure = null;
        try
        {
            callOriginal();
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }

        string? failure = null;
        try
        {
            EndScope(scope);
            if (originalFailure is null)
            {
                failure = FinalizeCoreBuild(scope);
            }
        }
        catch (Exception exception)
        {
            failure = $"Steam Settings post-builder capture failed: {FormatException(exception)}";
        }
        finally
        {
            EnsureScopeRemoved(scope);
        }
        if (!string.IsNullOrWhiteSpace(failure))
        {
            FailCoverage(failure);
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private void HandleOverlayBuild(BuildKind kind, nuint root, Action callOriginal)
    {
        if (!TryStartScope(kind, root, -1, out var scope, out var setupDiagnostic))
        {
            callOriginal();
            FailCoverage(setupDiagnostic);
            return;
        }
        var selected = GetSelectedLabel();
        Exception? originalFailure = null;
        try
        {
            callOriginal();
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }

        string? failure = null;
        try
        {
            EndScope(scope);
            if (originalFailure is null)
            {
                var errors = scope.Errors;
                if (errors.Count != 0)
                {
                    failure = string.Join(" | ", errors);
                }
                else if (scope.Labels.Count == 0)
                {
                    failure = "A Steam Settings overlay completed without its audited localized label probe.";
                }
            }
        }
        finally
        {
            EnsureScopeRemoved(scope);
        }
        if (!string.IsNullOrWhiteSpace(failure))
        {
            FailCoverage(failure);
        }
        else if (originalFailure is null)
        {
            if (callbackDepth.Value != 0)
            {
                nestedUnsupported.Value = true;
            }
            else
            {
                PublishSafely(CreateUnsupported(selected));
            }
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private void HandleCallback(
        CallbackKind kind,
        Func<SteamSettingsCallbackDelegate> original,
        nint context,
        int eventType,
        int key)
    {
        _ = key;
        ActiveState? before = null;
        var rootOffset = kind == CallbackKind.Resolution ? 4u : 0u;
        if (TryReadPointer((nuint)context + rootOffset, out var root))
        {
            lock (gate)
            {
                if (active?.Root == root)
                {
                    before = active;
                }
            }
        }
        var selected = before is null ? "Settings" : GetSelectedLabel(before.Snapshot);
        var beforeFocus = before is null ? null : ToFocus(before.Snapshot);
        var specialActivation = before is not null && eventType == 0 &&
            IsUnsupportedActivation(kind, before.Snapshot);

        callbackDepth.Value++;
        if (callbackDepth.Value == 1)
        {
            nestedUnsupported.Value = false;
        }
        Exception? originalFailure = null;
        try
        {
            original()(context, eventType, key);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }
        finally
        {
            callbackDepth.Value--;
        }

        if (originalFailure is not null)
        {
            nestedUnsupported.Value = false;
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
        if (callbackDepth.Value != 0 || before is null)
        {
            return;
        }

        var overlayUnsupported = nestedUnsupported.Value;
        nestedUnsupported.Value = false;
        ActiveState? current;
        lock (gate)
        {
            current = active;
        }
        if (current is null || current.Root != before.Root)
        {
            return;
        }
        if (!TryRefreshForCurrentMode(current, out var refreshed, out var diagnostic))
        {
            FailCoverage(diagnostic);
            return;
        }

        if (eventType == 0)
        {
            PublishSafely(new MenuActivated(selected));
        }
        if (kind == CallbackKind.License || overlayUnsupported || specialActivation)
        {
            PublishSafely(CreateUnsupported(selected, before.Snapshot));
            return;
        }

        var changedPage = before.Snapshot.Mode != refreshed.Snapshot.Mode ||
            before.Snapshot.ActivePageIndex != refreshed.Snapshot.ActivePageIndex;
        if (changedPage || eventType == 2)
        {
            PublishSafely(new MenuPresented(MenuTitle, ToFocus(refreshed.Snapshot), null));
        }
        else if (eventType != 0 || !Equals(beforeFocus, ToFocus(refreshed.Snapshot)))
        {
            PublishSafely(new MenuFocusChanged(ToFocus(refreshed.Snapshot)));
        }
    }

    private void HandleSetter(
        Func<SteamSettingsSetterInvokerDelegate> original,
        nint setter,
        int newIndex)
    {
        ActiveState? basis;
        SteamSettingsRowSnapshot? row = null;
        lock (gate)
        {
            basis = active;
            if (basis?.Snapshot.Page is { } page)
            {
                row = page.Rows.SingleOrDefault(candidate => candidate.SetterAddress == (nuint)setter);
            }
        }
        if (basis is null || row is null)
        {
            original()(setter, newIndex);
            if (basis is not null)
            {
                FailCoverage("Steam Settings setter ECX did not equal one copied active-row setter address.");
            }
            return;
        }
        if (row.SelectedIndex is null || row.SelectedValueSourceAddress is null ||
            newIndex < 0 || newIndex >= row.Values.Count)
        {
            original()(setter, newIndex);
            FailCoverage("Steam Settings setter received an index outside the copied active-row values.");
            return;
        }

        var oldIndex = row.SelectedIndex.Value;
        var vectorBegin = checked(row.SelectedValueSourceAddress.Value - (nuint)(oldIndex * StringStride));
        var newSource = checked(vectorBegin + (nuint)(newIndex * StringStride));
        var observations = basis.Observations.Select(observation =>
            observation.RowAddress == row.NativeRowAddress
                ? observation with { ValueSourceAddress = newSource }
                : observation).ToArray();

        original()(setter, newIndex);
        if (!TryRefreshActive(basis, observations, out var refreshed, out var diagnostic))
        {
            FailCoverage(diagnostic);
            return;
        }
        if (callbackDepth.Value == 0)
        {
            PublishSafely(new MenuFocusChanged(ToFocus(refreshed.Snapshot)));
        }
    }

    private string? FinalizeCoreBuild(BuildScope scope)
    {
        var errors = scope.Errors;
        if (errors.Count != 0)
        {
            return string.Join(" | ", errors);
        }
        if (!SteamSettingsCapture.TryCreateSnapshot(
                memory,
                imageBase,
                scope.Root,
                scope.Generation,
                scope.Observations,
                out var snapshot,
                out var diagnostic))
        {
            return diagnostic;
        }
        if (scope.Kind == BuildKind.Outer && scope.CategorySources.Count != snapshot.Categories.Count)
        {
            return $"Steam Settings rendered {scope.CategorySources.Count} audited category labels for {snapshot.Categories.Count} live categories.";
        }
        if (scope.Kind == BuildKind.Page &&
            (snapshot.Page is null || scope.RowSources.Count != snapshot.Page.Rows.Count))
        {
            return $"Steam Settings rendered {scope.RowSources.Count} audited row labels without a matching complete live page.";
        }

        // A page builder is allowed to nest the outer builder. The outer scope
        // owns that transaction's one installed snapshot and presentation.
        lock (gate)
        {
            if (scope.Kind == BuildKind.Page && outerBuild is { } outer &&
                outer.Root == scope.Root && outer.OwnerThreadId == scope.OwnerThreadId)
            {
                return null;
            }
        }

        var state = new ActiveState(
            scope.Root,
            scope.Generation,
            snapshot,
            new ReadOnlyCollection<SteamSettingsValueObservation>(scope.Observations.ToArray()));
        AccessibilityEvent? publish = null;
        DeferredPresentation? schedule = null;
        lock (gate)
        {
            if (!hooksActive || epoch != scope.Epoch || Volatile.Read(ref faulted) != 0 || !roots.Contains(scope.Root))
            {
                return "Steam Settings lifecycle changed before its post-original snapshot could be installed.";
            }
            var firstPresentation = active is null;
            active = state;
            var presented = new MenuPresented(MenuTitle, ToFocus(snapshot), null);
            if (firstPresentation)
            {
                schedule = new DeferredPresentation(scope.Root, scope.Generation, presented);
                deferredPresentation = schedule;
            }
            else if (callbackDepth.Value == 0)
            {
                publish = presented;
            }
        }
        if (publish is not null)
        {
            PublishSafely(publish);
        }
        if (schedule is not null)
        {
            QueueDeferredPresentation(schedule);
        }
        return null;
    }

    private void QueueDeferredPresentation(DeferredPresentation pending)
    {
        // The Steam builder can run inside the top-menu action-4 native stack.
        // Posting lets that caller publish activation and exit before Settings.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1).ConfigureAwait(false);
            try
            {
                AccessibilityEvent? publish = null;
                lock (gate)
                {
                    if (hooksActive && Volatile.Read(ref faulted) == 0 &&
                        ReferenceEquals(deferredPresentation, pending) && active is { } current &&
                        current.Root == pending.Root && current.Generation == pending.Generation)
                    {
                        deferredPresentation = null;
                        publish = pending.Event;
                    }
                }
                if (publish is not null)
                {
                    PublishSafely(publish);
                }
            }
            catch (Exception exception)
            {
                FailCoverage($"Deferred Steam Settings presentation failed: {FormatException(exception)}");
            }
        });
    }

    private bool TryStartScope(
        BuildKind kind,
        nuint root,
        int pageIndex,
        out BuildScope scope,
        out string diagnostic)
    {
        lock (gate)
        {
            scope = null!;
            if (!hooksActive || Volatile.Read(ref faulted) != 0 || !roots.Contains(root) ||
                !TryReadExactVtable(root, SteamSettingsCapture.RootVtableRva))
            {
                diagnostic = "Steam Settings builder root is not one exact live constructed MenuNodeConfigSteam instance.";
                return false;
            }
            if (kind == BuildKind.Outer && outerBuild is not null ||
                kind == BuildKind.Page && pageBuild is not null ||
                IsOverlay(kind) && overlayBuild is not null)
            {
                diagnostic = $"A nested {kind} Steam Settings builder scope was attempted.";
                return false;
            }
            if (kind == BuildKind.Page && pageIndex < 0)
            {
                diagnostic = "Steam Settings page builder received a negative page index.";
                return false;
            }
            var generation = unchecked(++captureGeneration);
            scope = new BuildScope(epoch, Environment.CurrentManagedThreadId, kind, root, pageIndex, generation);
            if (kind == BuildKind.Outer)
            {
                outerBuild = scope;
            }
            else if (kind == BuildKind.Page)
            {
                pageBuild = scope;
            }
            else
            {
                overlayBuild = scope;
            }
            diagnostic = string.Empty;
            return true;
        }
    }

    private void EndScope(BuildScope scope)
    {
        if (pendingMarker.Value is { } marker && ReferenceEquals(marker.Scope, scope))
        {
            scope.AddError($"Steam Settings marker {marker.Id} was not consumed by its immediate label factory call.");
            pendingMarker.Value = null;
        }
        lock (gate)
        {
            if (scope.Kind == BuildKind.Page && outerBuild is { } outer &&
                outer.OwnerThreadId == scope.OwnerThreadId && outer.Root == scope.Root)
            {
                outer.ReplaceObservations(scope.Observations);
            }
        }
    }

    private void EnsureScopeRemoved(BuildScope scope)
    {
        lock (gate)
        {
            if (ReferenceEquals(outerBuild, scope))
            {
                outerBuild = null;
            }
            if (ReferenceEquals(pageBuild, scope))
            {
                pageBuild = null;
            }
            if (ReferenceEquals(overlayBuild, scope))
            {
                overlayBuild = null;
            }
        }
    }

    private void ObserveProbe(ProbeDefinition definition)
    {
        try
        {
            if (!TryGetOwnedScope(definition.Scope, out var scope))
            {
                pendingMarker.Value = null;
                return;
            }
            if (pendingMarker.Value is { } pending)
            {
                scope.AddError($"Steam Settings marker {pending.Id} was replaced before its immediate target call.");
            }
            pendingMarker.Value = new PendingMarker(definition.Id, definition.Kind, definition.Scope, scope);
        }
        catch (Exception exception)
        {
            FailCoverage($"Steam Settings call-site observation failed: {FormatException(exception)}");
        }
    }

    private void RecordMarkedLabel(BuildScope scope, ProbeKind kind, nuint source, string value)
    {
        switch (kind)
        {
            case ProbeKind.CategoryLabel:
            {
                if (!TryReadPointer(scope.Root + CategoryVectorOffset, out var begin))
                {
                    scope.AddError("Steam Settings category vector was unreadable at its audited label call.");
                    return;
                }
                var expected = checked(begin + (nuint)(scope.CategorySources.Count * CategoryStride));
                if (source != expected)
                {
                    scope.AddError("Steam Settings category label source was not the next live 0x30-byte category element.");
                    return;
                }
                scope.AddCategorySource(source);
                break;
            }
            case ProbeKind.RowLabel:
            {
                if (!TryGetRowVectorBegin(scope, out var begin))
                {
                    scope.AddError("Steam Settings row vector was unreadable at its audited label call.");
                    return;
                }
                var expected = checked(begin + (nuint)(scope.RowSources.Count * RowStride));
                if (source != expected)
                {
                    scope.AddError("Steam Settings row label source was not the next live 0x88-byte row element.");
                    return;
                }
                scope.AddRowSource(source);
                break;
            }
            case ProbeKind.SelectedValue:
            {
                var rowAddress = scope.CurrentRowAddress;
                if (rowAddress == 0 || !TryReadVector(rowAddress + RowValueVectorOffset, out var begin, out var end) ||
                    source < begin || source >= end || ((ulong)source - begin) % StringStride != 0)
                {
                    scope.AddError("Steam Settings selected value was not aligned in the current live row value vector.");
                    return;
                }
                scope.AddObservation(new SteamSettingsValueObservation(
                    scope.Generation,
                    scope.Root,
                    scope.PageIndex,
                    rowAddress,
                    source));
                break;
            }
            case ProbeKind.OverlayLabel:
                scope.AddLabel(value);
                break;
            default:
                scope.AddError("Steam Settings received an unknown audited label marker.");
                break;
        }
    }

    private bool TryRefreshForCurrentMode(ActiveState basis, out ActiveState refreshed, out string diagnostic)
    {
        if (TryRefreshActive(basis, basis.Observations, out refreshed, out diagnostic))
        {
            return true;
        }
        if (TryReadByte(basis.Root + 0x314, out var mode) && mode == (byte)SteamSettingsMode.Categories)
        {
            return TryRefreshActive(basis, [], out refreshed, out diagnostic);
        }
        return false;
    }

    private bool TryRefreshActive(
        ActiveState basis,
        IReadOnlyList<SteamSettingsValueObservation> observations,
        out ActiveState refreshed,
        out string diagnostic)
    {
        if (!SteamSettingsCapture.TryCreateSnapshot(
                memory,
                imageBase,
                basis.Root,
                basis.Generation,
                observations,
                out var snapshot,
                out diagnostic))
        {
            refreshed = null!;
            return false;
        }
        refreshed = new ActiveState(
            basis.Root,
            basis.Generation,
            snapshot,
            new ReadOnlyCollection<SteamSettingsValueObservation>(observations.ToArray()));
        lock (gate)
        {
            if (!hooksActive || Volatile.Read(ref faulted) != 0 || active is null ||
                active.Root != basis.Root || active.Generation != basis.Generation)
            {
                diagnostic = "Steam Settings lifecycle changed during post-original refresh.";
                return false;
            }
            active = refreshed;
        }
        diagnostic = string.Empty;
        return true;
    }

    private static bool IsUnsupportedActivation(CallbackKind kind, SteamSettingsSnapshot snapshot)
    {
        if (kind == CallbackKind.License)
        {
            return true;
        }
        if (kind == CallbackKind.Category && snapshot.FocusedCategoryIndex is { } category)
        {
            return snapshot.Categories[category].Kind == SteamSettingsCategoryKind.SpecialAction;
        }
        if (kind == CallbackKind.Row && snapshot.Page is { } page && snapshot.FocusedRowIndex is { } row)
        {
            return page.Rows[row].Kind == SteamSettingsRowKind.Action;
        }
        return false;
    }

    private static bool OwnsManager(SteamSettingsSnapshot snapshot, nuint manager) =>
        snapshot.Mode == SteamSettingsMode.Categories
            ? snapshot.CategoryManagerAddress == manager
            : snapshot.ActiveManagerAddresses.Contains(manager);

    private static MenuFocus ToFocus(SteamSettingsSnapshot snapshot)
    {
        return snapshot.FocusKind switch
        {
            SteamSettingsFocusKind.Category when snapshot.FocusedCategoryIndex is { } index =>
                new MenuFocus(
                    snapshot.Categories[index].Label,
                    null,
                    index + 1,
                    snapshot.Categories.Count,
                    snapshot.Categories[index].Help,
                    Disabled: false),
            SteamSettingsFocusKind.Row when snapshot.Page is { } page && snapshot.FocusedRowIndex is { } index =>
                ToRowFocus(page.Rows[index], index, page.Rows.Count + 1),
            SteamSettingsFocusKind.ReturnToCategories when snapshot.Page is { } page =>
                new MenuFocus(
                    page.ReturnControl.Label,
                    page.ReturnControl.Value,
                    page.ReturnControl.Position,
                    page.ReturnControl.Count,
                    page.ReturnControl.Help,
                    Disabled: !page.ReturnControl.Enabled),
            _ => throw new InvalidOperationException("Steam Settings snapshot has no supported focus target."),
        };
    }

    private static MenuFocus ToRowFocus(SteamSettingsRowSnapshot row, int index, int count)
    {
        string? help = null;
        if (row.HelpTexts.Count == 1)
        {
            help = row.HelpTexts[0];
        }
        else if (row.SelectedIndex is { } selected && selected >= 0 && selected < row.HelpTexts.Count)
        {
            help = row.HelpTexts[selected];
        }
        return new MenuFocus(row.Label, row.Value, index + 1, count, help, Disabled: false);
    }

    private string GetSelectedLabel()
    {
        lock (gate)
        {
            return active is null ? "Settings" : GetSelectedLabel(active.Snapshot);
        }
    }

    private static string GetSelectedLabel(SteamSettingsSnapshot snapshot) => ToFocus(snapshot).Label;

    private static MenuUnsupported CreateUnsupported(string selected, SteamSettingsSnapshot? snapshot = null)
    {
        var returnLabel = snapshot?.Page?.ReturnControl.Label;
        var instruction = string.IsNullOrWhiteSpace(returnLabel)
            ? "Press Cancel to return to Settings."
            : $"Choose {returnLabel} to return to Settings.";
        return new MenuUnsupported(selected, UnsupportedBoundary, instruction);
    }

    private bool TryGetOwnedOverlay(out BuildScope scope)
    {
        lock (gate)
        {
            if (overlayBuild is { } current && current.OwnerThreadId == Environment.CurrentManagedThreadId &&
                !current.Cancelled && hooksActive && epoch == current.Epoch)
            {
                scope = current;
                return true;
            }
        }
        scope = null!;
        return false;
    }

    private bool TryGetOwnedScope(BuildKind kind, out BuildScope scope)
    {
        lock (gate)
        {
            var current = kind switch
            {
                BuildKind.Outer => outerBuild,
                BuildKind.Page => pageBuild,
                _ => overlayBuild,
            };
            if (current is not null && current.Kind == kind && !current.Cancelled && hooksActive &&
                current.Epoch == epoch && current.OwnerThreadId == Environment.CurrentManagedThreadId)
            {
                scope = current;
                return true;
            }
        }
        scope = null!;
        return false;
    }

    private bool IsOwnedLiveScope(BuildScope scope)
    {
        lock (gate)
        {
            return hooksActive && !scope.Cancelled && scope.Epoch == epoch &&
                scope.OwnerThreadId == Environment.CurrentManagedThreadId &&
                (ReferenceEquals(scope, outerBuild) || ReferenceEquals(scope, pageBuild) ||
                 ReferenceEquals(scope, overlayBuild));
        }
    }

    private bool IsAnyBuildOwnedByCurrentThread()
    {
        var thread = Environment.CurrentManagedThreadId;
        lock (gate)
        {
            return outerBuild?.OwnerThreadId == thread || pageBuild?.OwnerThreadId == thread ||
                overlayBuild?.OwnerThreadId == thread;
        }
    }

    private static bool IsOverlay(BuildKind kind) =>
        kind is BuildKind.Resolution or BuildKind.ConfirmationA or BuildKind.ConfirmationB;

    private bool TryGetRowVectorBegin(BuildScope scope, out nuint begin)
    {
        begin = 0;
        if (scope.PageIndex < 0 || !TryReadPointer(scope.Root + DescriptorVectorOffset, out var descriptors))
        {
            return false;
        }
        var descriptor = checked(descriptors + (nuint)(scope.PageIndex * DescriptorStride));
        return TryReadPointer(descriptor, out begin) && begin != 0;
    }

    private bool TryReadVector(nuint header, out nuint begin, out nuint end)
    {
        begin = 0;
        end = 0;
        return TryReadPointer(header, out begin) && TryReadPointer(header + 4, out end) &&
            begin != 0 && end >= begin;
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

    private void PublishSafely(AccessibilityEvent accessibilityEvent)
    {
        try
        {
            dispatcher.Publish(accessibilityEvent);
        }
        catch (Exception exception)
        {
            FailCoverage($"Steam Settings semantic publication failed: {FormatException(exception)}");
        }
    }

    private void FailCoverage(string diagnostic)
    {
        var shouldReport = false;
        lock (gate)
        {
            if (hooksActive && Interlocked.Exchange(ref faulted, 1) == 0)
            {
                active = null;
                deferredPresentation = null;
                CancelScopesLocked();
                shouldReport = true;
            }
        }
        pendingMarker.Value = null;
        if (!shouldReport)
        {
            return;
        }
        try
        {
            dispatcher.ReportCoverageFailure(string.IsNullOrWhiteSpace(diagnostic)
                ? "Steam Settings accessibility coverage failed without a diagnostic."
                : diagnostic);
        }
        catch (Exception)
        {
            // Coverage reporting is best-effort at an unmanaged boundary.
        }
    }

    private void CancelScopesLocked()
    {
        if (outerBuild is not null) outerBuild.Cancelled = true;
        if (pageBuild is not null) pageBuild.Cancelled = true;
        if (overlayBuild is not null) overlayBuild.Cancelled = true;
        outerBuild = null;
        pageBuild = null;
        overlayBuild = null;
    }

    private void InitializeBuild(IVerifiedGameBuild build, HookId id)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.ImageBaseAddress == 0 || (ulong)build.ImageBaseAddress > uint.MaxValue)
        {
            throw new InvalidOperationException("Verified x86 image base is unavailable for Steam Settings hooks.");
        }
        lock (gate)
        {
            if (imageBase != 0 && imageBase != build.ImageBaseAddress)
            {
                throw new InvalidOperationException("Steam Settings hooks received conflicting image bases.");
            }
            imageBase = build.ImageBaseAddress;
            if (!preparedIds.Add(id))
            {
                throw new InvalidOperationException($"Steam Settings hook '{id}' was prepared more than once.");
            }
        }
    }

    private static nuint RequireAddress(IVerifiedGameBuild build, HookId id)
    {
        if (!build.HookAddresses.TryGetValue(id, out var address) || address == 0 || (ulong)address > uint.MaxValue)
        {
            throw new InvalidOperationException($"Verified x86 address for Steam Settings hook '{id}' is missing.");
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
        TDelegate GetOriginal() => original ?? throw new InvalidOperationException($"Original '{id}' is not bound.");
        var detour = createDetour(GetOriginal);
        var hook = hookFactory.CreateHook(id, detour, address);
        original = hook.OriginalFunction;
        return new ReloadedPreparedHook<TDelegate>(GameVersionCatalog.Get(id).Symbol, hook, detour);
    }

    private static IHookRegistration CreateRegistration(
        HookId id,
        Func<IVerifiedGameBuild, UnmanagedBoundaryGuard, IPreparedHook> prepare) =>
        new HookRegistration(GameVersionCatalog.Get(id).Symbol, prepare);

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

    private sealed class BuildScope(
        int epoch,
        int ownerThreadId,
        BuildKind kind,
        nuint root,
        int pageIndex,
        int generation)
    {
        private readonly object gate = new();
        private readonly List<string> errors = [];
        private readonly List<nuint> categorySources = [];
        private readonly List<nuint> rowSources = [];
        private readonly List<SteamSettingsValueObservation> observations = [];
        private readonly List<string> labels = [];
        private readonly List<string> prompts = [];
        private readonly List<nuint> constructed = [];
        private readonly List<(nuint Manager, nuint State, int Key)> bindings = [];

        public int Epoch { get; } = epoch;
        public int OwnerThreadId { get; } = ownerThreadId;
        public BuildKind Kind { get; } = kind;
        public nuint Root { get; } = root;
        public int PageIndex { get; } = pageIndex;
        public int Generation { get; } = generation;
        public bool Cancelled { get; set; }
        public nuint CurrentRowAddress { get { lock (gate) return rowSources.Count == 0 ? 0 : rowSources[^1]; } }
        public IReadOnlyList<string> Errors { get { lock (gate) return errors.ToArray(); } }
        public IReadOnlyList<nuint> CategorySources { get { lock (gate) return categorySources.ToArray(); } }
        public IReadOnlyList<nuint> RowSources { get { lock (gate) return rowSources.ToArray(); } }
        public IReadOnlyList<SteamSettingsValueObservation> Observations { get { lock (gate) return observations.ToArray(); } }
        public IReadOnlyList<string> Labels { get { lock (gate) return labels.ToArray(); } }

        public void AddError(string error) { lock (gate) errors.Add(error); }
        public void AddCategorySource(nuint source) { lock (gate) categorySources.Add(source); }
        public void AddRowSource(nuint source) { lock (gate) rowSources.Add(source); }
        public void AddObservation(SteamSettingsValueObservation observation) { lock (gate) observations.Add(observation); }
        public void AddLabel(string label) { lock (gate) labels.Add(new string(label.AsSpan())); }
        public void AddPrompt(string prompt) { lock (gate) prompts.Add(new string(prompt.AsSpan())); }
        public void AddConstructed(nuint control) { lock (gate) constructed.Add(control); }
        public void AddBinding(nuint manager, nuint state, int key) { lock (gate) bindings.Add((manager, state, key)); }
        public void ReplaceObservations(IEnumerable<SteamSettingsValueObservation> values)
        {
            lock (gate)
            {
                observations.Clear();
                observations.AddRange(values.Select(value => value with { CaptureGeneration = Generation }));
            }
        }
    }

    private sealed record ActiveState(
        nuint Root,
        int Generation,
        SteamSettingsSnapshot Snapshot,
        IReadOnlyList<SteamSettingsValueObservation> Observations);

    private sealed record DeferredPresentation(nuint Root, int Generation, MenuPresented Event);
    private readonly record struct ProbeDefinition(HookId Id, ProbeKind Kind, BuildKind Scope);
    private readonly record struct PendingMarker(HookId Id, ProbeKind Kind, BuildKind ExpectedScope, BuildScope Scope);

    private enum BuildKind
    {
        Outer,
        Page,
        Resolution,
        ConfirmationA,
        ConfirmationB,
    }

    private enum ProbeKind
    {
        CategoryLabel,
        RowLabel,
        SelectedValue,
        OverlayLabel,
    }

    private enum CallbackKind
    {
        Category,
        Row,
        License,
        Resolution,
    }
}
