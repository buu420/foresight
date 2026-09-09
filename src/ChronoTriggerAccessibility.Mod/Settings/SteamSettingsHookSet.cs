using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
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
    private const uint ModeOffset = 0x314;
    private const uint FocusKeyOffset = 0x338;
    private const uint ManagerFocusKeyOffset = 0x2C4;
    private const int DescriptorStride = 0x0C;
    private const int CategoryStride = 0x30;
    private const int RowStride = 0x88;
    private const int RowValueVectorOffset = 0x24;
    private const int RowSetterOffset = 0x58;
    private const int StringStride = MsvcStringReader.LayoutSize;
    private const uint RowCallbackRenderedCountOffset = 0x04;
    private const uint RowCallbackRowVectorOffset = 0x10;
    private const int MaximumLicenseTextByteLength = 64 * 1024;
    private const int LicensePageCount = 17;
    private const uint ControllerButtonIdsRva = 0x3BB320;
    private const uint RuntimeConfigPointerRva = 0x41B4C4;
    private const uint ControllerBindingsOffset = 0x68F4;
    private const uint KeyboardBindingsOffset = 0x6904;
    private const uint KeyboardLookupVectorOffset = 0x3B0;

    private const string MenuTitle = "Settings";

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
        HookId.SteamSettingsLicensePageBuilder,
        HookId.SteamSettingsConfirmationCallbackA,
        HookId.SteamSettingsConfirmationCallbackB,
        HookId.SteamSettingsControllerBuilder,
        HookId.SteamSettingsControllerRowRefresh,
        HookId.SteamSettingsControllerCallback,
        HookId.SteamSettingsKeyboardBuilder,
        HookId.SteamSettingsKeyboardRowRefresh,
        HookId.SteamSettingsKeyboardCallback,
    ];

    private static readonly ProbeDefinition[] ProbeDefinitions =
    [
        new(HookId.SteamSettingsCategoryLabelCallSite, ProbeKind.CategoryLabel, BuildKind.Outer),
        new(HookId.SteamSettingsRowLabelCallSite, ProbeKind.RowLabel, BuildKind.Page),
        new(HookId.SteamSettingsTitleResolutionValueLabelCallSite, ProbeKind.TitleResolutionValue, BuildKind.Page),
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
    private readonly ThreadLocal<List<AccessibilityEvent>> deferredNestedEvents = new(() => []);
    private readonly ThreadLocal<RowSetterScope?> rowSetterScope = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;

    private nuint imageBase;
    private BuildScope? outerBuild;
    private BuildScope? pageBuild;
    private BuildScope? overlayBuild;
    private ActiveState? active;
    private NestedContext? nested;
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
            CreateRegistration(HookId.SteamSettingsLicensePageBuilder, PrepareLicensePageBuilder),
            CreateRegistration(HookId.SteamSettingsConfirmationCallbackA, PrepareConfirmationCallbackA),
            CreateRegistration(HookId.SteamSettingsConfirmationCallbackB, PrepareConfirmationCallbackB),
            CreateRegistration(HookId.SteamSettingsControllerBuilder, PrepareControllerBuilder),
            CreateRegistration(HookId.SteamSettingsControllerRowRefresh, PrepareControllerRowRefresh),
            CreateRegistration(HookId.SteamSettingsControllerCallback, PrepareControllerCallback),
            CreateRegistration(HookId.SteamSettingsKeyboardBuilder, PrepareKeyboardBuilder),
            CreateRegistration(HookId.SteamSettingsKeyboardRowRefresh, PrepareKeyboardRowRefresh),
            CreateRegistration(HookId.SteamSettingsKeyboardCallback, PrepareKeyboardCallback),
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
            nested = null;
            deferredPresentation = null;
            CancelScopesLocked();
        }
        pendingMarker.Value = null;
        callbackDepth.Value = 0;
        deferredNestedEvents.Value?.Clear();
        rowSetterScope.Value = null;
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
        try
        {
            if (marker is not null)
            {
                pendingMarker.Value = null;
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
                return;
            }

            if (!TryGetOwnedOverlay(out var genericScope) || !CapturesEveryRenderedLabel(genericScope.Kind))
            {
                return;
            }
            var genericDiagnostic = string.Empty;
            var maximumByteLength = genericScope.Kind == BuildKind.Licenses
                ? MaximumLicenseTextByteLength
                : MsvcStringReader.MaximumByteLength;
            if (returned == 0 || text == 0 ||
                !stringReader.TryRead(
                    (nuint)text,
                    maximumByteLength,
                    out var genericValue,
                    out genericDiagnostic))
            {
                genericScope.AddError(
                    $"A nested Steam Settings label was not rendered as readable UTF-8: {genericDiagnostic}");
                return;
            }
            if (string.IsNullOrWhiteSpace(genericValue))
            {
                if (genericScope.Kind == BuildKind.Keyboard)
                {
                    genericScope.AddLabel("Unassigned");
                    return;
                }
                genericScope.AddError("A nested Steam Settings visible label was blank.");
                return;
            }
            genericScope.AddLabel(genericValue);
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
        try
        {
            if (!TryGetConfirmationPromptScope(fileId, messageId, out var scope))
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

    public void AfterOpeTextResolver(nint resolver, nint result, int bank, int messageId, nint returned)
    {
        _ = resolver;
        try
        {
            if (!TryGetConfirmationPromptScope(bank, messageId, out var scope))
            {
                return;
            }
            var address = returned != 0 ? (nuint)returned : (nuint)result;
            var diagnostic = "the localized resolver returned null";
            if (address == 0 || !stringReader.TryRead(address, out var prompt, out diagnostic) ||
                string.IsNullOrWhiteSpace(prompt))
            {
                scope.AddError($"A Steam Settings confirmation prompt was unreadable: {diagnostic}");
                return;
            }
            scope.AddPrompt(prompt);
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared Steam Settings localized-prompt observation failed: {FormatException(exception)}");
        }
    }

    public void AfterFocusSet(nint manager, int managerKey)
    {
        try
        {
            if (TryGetOwnedOverlay(out var buildScope))
            {
                buildScope.AddFocus((nuint)manager, managerKey);
                return;
            }

            ActiveState? basis;
            NestedContext? nestedBasis;
            lock (gate)
            {
                basis = hooksActive && Volatile.Read(ref faulted) == 0 ? active : null;
                nestedBasis = hooksActive && Volatile.Read(ref faulted) == 0 ? nested : null;
            }
            if (nestedBasis is not null && nestedBasis.Manager == (nuint)manager &&
                nestedBasis.Choices.ContainsKey(managerKey))
            {
                if (nestedBasis.FocusKey == managerKey)
                {
                    return;
                }
                var refreshedNested = nestedBasis with { FocusKey = managerKey };
                lock (gate)
                {
                    if (ReferenceEquals(nested, nestedBasis))
                    {
                        nested = refreshedNested;
                    }
                }
                if (callbackDepth.Value == 0)
                {
                    PublishSafely(CreateNestedFocusEvent(refreshedNested, managerKey));
                }
                return;
            }
            if (basis is null || IsAnyBuildOwnedByCurrentThread() || !OwnsManager(basis.Snapshot, (nuint)manager))
            {
                return;
            }
            if (basis.Snapshot.Mode == SteamSettingsMode.Page &&
                (!TryReadInt32(basis.Root + FocusKeyOffset, out var committedFocusKey) ||
                 committedFocusKey != managerKey))
            {
                // nsMenu commits the manager key before the row callback writes
                // MenuNodeConfigSteam::focusKey. The post-callback refresh below
                // HandleCallback is the first authoritative complete page state.
                return;
            }
            if (!TryRefreshActive(basis, basis.Observations, out var refreshed, out var diagnostic) ||
                refreshed.Snapshot.FocusKey != managerKey)
            {
                // Core and nested callbacks publish one authoritative post-original
                // transition. A native focus helper can briefly expose an old or
                // not-yet-rebuilt key while that callback is still on the stack;
                // the enclosing callback will reconcile it after the rebuild.
                if (callbackDepth.Value != 0)
                {
                    return;
                }
                FailCoverage(string.IsNullOrWhiteSpace(diagnostic)
                    ? "Steam Settings focus setter did not commit the observed manager key."
                    : diagnostic);
                return;
            }
            if (callbackDepth.Value == 0 &&
                !Equals(ToFocus(basis.Snapshot), ToFocus(refreshed.Snapshot)))
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
                HandleNestedCallback(NestedKind.Licenses, original, context, eventType, key)));

    private IPreparedHook PrepareSetterInvoker(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsSetterInvokerDelegate>(HookId.SteamSettingsSetterInvoker, build, original =>
            (setter, newIndex) => boundary.Run("Steam Settings setter invoker", () =>
                HandleSetter(original, setter, newIndex)));

    private IPreparedHook PrepareResolutionBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsResolutionBuilder, build, original => root =>
            boundary.Run("Steam Settings resolution builder", () =>
                HandleNestedBuild(BuildKind.Resolution, (nuint)root, -1, () => original()(root))));

    private IPreparedHook PrepareResolutionCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsResolutionCallback, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings resolution callback", () =>
                HandleNestedCallback(NestedKind.Resolution, original, context, eventType, key)));

    private IPreparedHook PrepareConfirmationBuilderA(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsConfirmationBuilderA, build, original => root =>
            boundary.Run("Steam Settings confirmation A builder", () =>
                HandleNestedBuild(BuildKind.ConfirmationA, (nuint)root, -1, () => original()(root))));

    private IPreparedHook PrepareConfirmationBuilderB(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsConfirmationBuilderB, build, original => root =>
            boundary.Run("Steam Settings confirmation B builder", () =>
                HandleNestedBuild(BuildKind.ConfirmationB, (nuint)root, -1, () => original()(root))));

    private IPreparedHook PrepareLicensePageBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsLicensePageBuilderDelegate>(HookId.SteamSettingsLicensePageBuilder, build, original =>
            (root, pageIndex) => boundary.Run("Steam Settings Licenses page builder", () =>
                HandleNestedBuild(BuildKind.Licenses, (nuint)root, pageIndex, () => original()(root, pageIndex))));

    private IPreparedHook PrepareConfirmationCallbackA(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsConfirmationCallbackA, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings confirmation A callback", () =>
                HandleNestedCallback(NestedKind.ConfirmationA, original, context, eventType, key)));

    private IPreparedHook PrepareConfirmationCallbackB(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsConfirmationCallbackB, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings confirmation B callback", () =>
                HandleNestedCallback(NestedKind.ConfirmationB, original, context, eventType, key)));

    private IPreparedHook PrepareControllerBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsControllerBuilder, build, original => root =>
            boundary.Run("Steam Settings controller configuration builder", () =>
                HandleNestedBuild(BuildKind.Controller, (nuint)root, -1, () => original()(root))));

    private IPreparedHook PrepareControllerRowRefresh(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsRowRefreshDelegate>(HookId.SteamSettingsControllerRowRefresh, build, original =>
            (context, rowIndex) => boundary.Run("Steam Settings controller binding refresh", () =>
                HandleBindingRefresh(NestedKind.Controller, original, context, rowIndex)));

    private IPreparedHook PrepareControllerCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsControllerCallback, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings controller configuration callback", () =>
                HandleNestedCallback(NestedKind.Controller, original, context, eventType, key)));

    private IPreparedHook PrepareKeyboardBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsKeyboardBuilder, build, original => root =>
            boundary.Run("Steam Settings keyboard configuration builder", () =>
                HandleNestedBuild(BuildKind.Keyboard, (nuint)root, -1, () => original()(root))));

    private IPreparedHook PrepareKeyboardRowRefresh(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsRowRefreshDelegate>(HookId.SteamSettingsKeyboardRowRefresh, build, original =>
            (context, rowIndex) => boundary.Run("Steam Settings keyboard binding refresh", () =>
                HandleBindingRefresh(NestedKind.Keyboard, original, context, rowIndex)));

    private IPreparedHook PrepareKeyboardCallback(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SteamSettingsCallbackDelegate>(HookId.SteamSettingsKeyboardCallback, build, original =>
            (context, eventType, key) => boundary.Run("Steam Settings keyboard configuration callback", () =>
                HandleNestedCallback(NestedKind.Keyboard, original, context, eventType, key)));

    private IPreparedHook PrepareProbe(
        ProbeDefinition definition,
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary)
    {
        InitializeBuild(build, definition.Id);
        var address = RequireAddress(build, definition.Id);
        var options = new RuntimeAsmHookOptions(
            AsmHookBehaviour.ExecuteFirst,
            HookLength: 5,
            PreferRelativeJump: true,
            MaxOpcodeSize: 5);
        if (definition.Kind == ProbeKind.TitleResolutionValue)
        {
            SteamSettingsRenderedValueProbeDelegate directCallback = text => boundary.Run(
                GameVersionCatalog.Get(definition.Id).Symbol,
                () => ObserveRenderedValueProbe(definition, (nuint)text));
            return asmHookFactory.CreateAsmHook(
                definition.Id,
                GameVersionCatalog.Get(definition.Id).Symbol,
                directCallback,
                address,
                EdxCallSiteProbeAssembly.Build,
                options);
        }
        NativeCallSiteProbeDelegate callback = () => boundary.Run(
            GameVersionCatalog.Get(definition.Id).Symbol,
            () => ObserveProbe(definition));
        return asmHookFactory.CreateAsmHook(
            definition.Id,
            GameVersionCatalog.Get(definition.Id).Symbol,
            callback,
            address,
            NativeCallSiteProbeAssembly.Build,
            options);
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
                nested = null;
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
        else if (originalFailure is null && callbackDepth.Value == 0)
        {
            foreach (var nestedEvent in DrainDeferredNestedEvents())
            {
                PublishSafely(nestedEvent);
            }
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private void HandleNestedBuild(BuildKind kind, nuint root, int pageIndex, Action callOriginal)
    {
        if (!TryStartScope(kind, root, pageIndex, out var scope, out var setupDiagnostic))
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
                failure = FinalizeNestedBuild(scope, selected);
            }
        }
        catch (Exception exception)
        {
            failure = $"Steam Settings nested post-builder capture failed: {FormatException(exception)}";
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

    private void HandleBindingRefresh(
        NestedKind kind,
        Func<SteamSettingsRowRefreshDelegate> original,
        nint context,
        int rowIndex)
    {
        original()(context, rowIndex);

        BuildScope? scope = null;
        var expectedBuild = kind == NestedKind.Controller ? BuildKind.Controller : BuildKind.Keyboard;
        var building = TryGetOwnedScope(expectedBuild, out scope);
        NestedContext? activeNested;
        lock (gate)
        {
            activeNested = nested is { Kind: var currentKind } current && currentKind == kind ? current : null;
        }
        if (!building && activeNested is null)
        {
            return;
        }

        var captured = kind == NestedKind.Controller
            ? TryCaptureControllerBinding(rowIndex, out var value, out var diagnostic)
            : TryCaptureKeyboardBinding((nuint)context, rowIndex, out value, out diagnostic);
        if (!captured)
        {
            if (building)
            {
                scope!.AddError(diagnostic);
            }
            else
            {
                FailCoverage(diagnostic);
            }
            return;
        }
        if (building)
        {
            scope!.AddDynamicValue(rowIndex, value);
            return;
        }
        if (activeNested is null || !activeNested.Choices.TryGetValue(rowIndex, out var oldChoice) ||
            string.Equals(oldChoice.Value, value, StringComparison.Ordinal))
        {
            return;
        }
        var choices = activeNested.Choices.ToDictionary(pair => pair.Key, pair => pair.Value);
        choices[rowIndex] = oldChoice with { Value = value };
        var refreshed = activeNested with
        {
            Choices = new ReadOnlyDictionary<int, NestedChoice>(choices),
        };
        lock (gate)
        {
            if (!ReferenceEquals(nested, activeNested))
            {
                return;
            }
            nested = refreshed;
        }
        if (refreshed.FocusKey == rowIndex)
        {
            DeferOrPublishNested(CreateNestedFocusEvent(refreshed, rowIndex));
        }
    }

    private void HandleCallback(
        CallbackKind kind,
        Func<SteamSettingsCallbackDelegate> original,
        nint context,
        int eventType,
        int key)
    {
        ActiveState? before = null;
        if (TryReadPointer((nuint)context, out var root))
        {
            lock (gate)
            {
                if (active?.Root == root)
                {
                    before = active;
                }
            }
        }
        var beforeFocus = before is null ? null : ToFocus(before.Snapshot);
        var targetFocus = before is not null && TryGetCallbackTargetFocus(kind, before.Snapshot, key, out var target)
            ? target
            : beforeFocus;
        var activates = before is not null && eventType == 0 &&
            IsCoreActivation(kind, before.Snapshot, key);
        var previousSetterScope = rowSetterScope.Value;
        // The native row callback owns a deep copy of the descriptor rows. Its
        // setter ECX is copy.begin + row * 0x88 + 0x58, not the descriptor-row
        // address retained by the immutable semantic snapshot.
        rowSetterScope.Value = kind == CallbackKind.Row && before is not null &&
            TryCaptureRowSetterScope((nuint)context, before, out var capturedSetterScope)
                ? capturedSetterScope
                : null;

        callbackDepth.Value++;
        if (callbackDepth.Value == 1)
        {
            deferredNestedEvents.Value!.Clear();
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
            rowSetterScope.Value = previousSetterScope;
        }

        if (originalFailure is not null)
        {
            deferredNestedEvents.Value!.Clear();
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
        if (callbackDepth.Value != 0 || before is null)
        {
            return;
        }

        if (activates && targetFocus is not null)
        {
            PublishSafely(new MenuActivated(targetFocus.Label));
        }
        var nestedEvents = DrainDeferredNestedEvents();
        foreach (var nestedEvent in nestedEvents)
        {
            PublishSafely(nestedEvent);
        }
        if (nestedEvents.Count != 0)
        {
            return;
        }

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

        var changedSurface = before.Snapshot.Mode != refreshed.Snapshot.Mode ||
            before.Snapshot.Mode == SteamSettingsMode.Page &&
            refreshed.Snapshot.Mode == SteamSettingsMode.Page &&
            before.Snapshot.ActivePageIndex != refreshed.Snapshot.ActivePageIndex;
        var refreshedFocus = ToFocus(refreshed.Snapshot);
        if (changedSurface)
        {
            PublishSafely(CreateActivePresentation(refreshed.Snapshot));
        }
        else if (!Equals(beforeFocus, refreshedFocus))
        {
            PublishSafely(new MenuFocusChanged(refreshedFocus));
        }
    }

    private void HandleNestedCallback(
        NestedKind kind,
        Func<SteamSettingsCallbackDelegate> original,
        nint context,
        int eventType,
        int key)
    {
        NestedContext? before;
        ActiveState? pageBefore;
        lock (gate)
        {
            before = nested is { Kind: var currentKind } current && currentKind == kind ? current : null;
            pageBefore = active;
        }
        var rootOffset = GetNestedCallbackRootOffset(kind);
        var hasCallbackRoot = TryReadPointer((nuint)context + rootOffset, out var callbackRoot);

        // ResolutionCallback (RVA 0x1FB100) compares against the live selector key
        // before original. A cached key can misclassify an apply as a focus move.
        int? liveFocusKey = null;
        if (kind == NestedKind.Resolution &&
            TryReadPointer((nuint)context, out var callbackManager) &&
            TryReadInt32(callbackManager + ManagerFocusKeyOffset, out var nativeFocusKey))
        {
            liveFocusKey = nativeFocusKey;
        }
        var hiddenLicensePreview = kind == NestedKind.Licenses && hasCallbackRoot &&
            TryReadByte(callbackRoot + ModeOffset, out var callbackMode) &&
            callbackMode == (byte)SteamSettingsMode.Categories;
        var validContext = before is not null && hasCallbackRoot && callbackRoot == before.Root;
        var label = before is null
            ? kind.ToString()
            : before.Choices.TryGetValue(key, out var keyedChoice)
                ? keyedChoice.Label
                : before.Choices[before.FocusKey].Label;

        callbackDepth.Value++;
        if (callbackDepth.Value == 1)
        {
            deferredNestedEvents.Value!.Clear();
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
            deferredNestedEvents.Value!.Clear();
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
        if (callbackDepth.Value != 0)
        {
            return;
        }
        if (hiddenLicensePreview || before is null)
        {
            // Native state machines retain and broadcast callbacks for hidden
            // nested controls. A builder must install the semantic surface before
            // any callback can represent visible UI, so unmatched callbacks are
            // native-only rather than accessibility coverage failures.
            return;
        }
        if (!validContext)
        {
            FailCoverage($"Steam Settings {kind} callback event {eventType}, key {key} did not match its captured root and menu context.");
            return;
        }

        var activates = kind switch
        {
            NestedKind.ConfirmationA or NestedKind.ConfirmationB => eventType == 0,
            NestedKind.Resolution => false,
            NestedKind.Controller => eventType == 1 || eventType == 0 && key == 4,
            NestedKind.Keyboard or NestedKind.Licenses => eventType == 0,
            _ => false,
        };
        if (activates)
        {
            PublishSafely(new MenuActivated(label));
        }
        var nestedEvents = DrainDeferredNestedEvents();
        foreach (var nestedEvent in nestedEvents)
        {
            PublishSafely(nestedEvent);
        }
        if (nestedEvents.Count != 0)
        {
            return;
        }

        // A valid selection can still be rejected by the native mode/size check.
        // Only the observed reentrant page build proves that the apply succeeded
        // and the selector closed. This also survives an unreadable old manager.
        bool resolutionPageRebuilt;
        lock (gate)
        {
            resolutionPageRebuilt = kind == NestedKind.Resolution && pageBefore is not null &&
                pageBefore.Root == before.Root && active is { } pageAfter &&
                pageAfter.Root == before.Root && pageAfter.Generation != pageBefore.Generation;
        }

        var closes = kind switch
        {
            NestedKind.ConfirmationA or NestedKind.ConfirmationB => eventType is 0 or 2,
            NestedKind.Resolution => eventType == 0 && resolutionPageRebuilt &&
                before.Choices.ContainsKey(key),
            NestedKind.Controller => key == 4 && eventType == 0,
            NestedKind.Keyboard => key == 12 && eventType == 0,
            NestedKind.Licenses => eventType == 2 || key == 2 && eventType == 0,
            _ => false,
        };
        var focuses = kind switch
        {
            NestedKind.Resolution => eventType == 1 ||
                eventType == 0 && liveFocusKey is int nativeKey && key != nativeKey,
            NestedKind.Controller => eventType == 0 && key is >= 0 and < 4,
            NestedKind.ConfirmationA or NestedKind.ConfirmationB or NestedKind.Keyboard or NestedKind.Licenses =>
                eventType == 1,
            _ => false,
        };
        if (focuses && !closes && key != before.FocusKey && before.Choices.ContainsKey(key))
        {
            NestedContext focused;
            lock (gate)
            {
                focused = nested is { Kind: NestedKind.Resolution, Root: var nestedRoot } latest &&
                    nestedRoot == before.Root && latest.Choices.ContainsKey(key)
                    ? latest with { FocusKey = key }
                    : before with { FocusKey = key };
                if (nested?.Root == before.Root && nested.Kind == kind)
                {
                    nested = focused;
                }
            }
            PublishSafely(CreateNestedFocusEvent(focused, key));
            return;
        }
        if (!closes)
        {
            return;
        }
        lock (gate)
        {
            if (nested?.Root == before.Root && nested.Kind == before.Kind)
            {
                nested = null;
            }
        }
        PublishActivePage(before.Root);
    }

    private string? FinalizeNestedBuild(BuildScope scope, string selected)
    {
        if (!TryReadByte(scope.Root + ModeOffset, out var mode))
        {
            return "A nested Steam Settings builder returned with an unreadable page/category mode.";
        }
        if (mode == (byte)SteamSettingsMode.Categories)
        {
            // Category focus previews invoke ordinary page construction. They
            // are not visible submenus until the category is activated.
            return null;
        }
        if (scope.Errors.Count != 0)
        {
            return string.Join(" | ", scope.Errors);
        }

        var labels = scope.Labels.ToArray();
        var prompts = scope.Prompts.Distinct(StringComparer.Ordinal).ToArray();
        var prior = GetNested(scope.Root);
        var manager = scope.Focus.LastOrDefault().Manager;
        if (manager == 0)
        {
            manager = scope.Bindings.Select(binding => binding.Manager).FirstOrDefault(value => value != 0);
        }
        if (manager == 0 && prior is not null)
        {
            manager = prior.Manager;
        }
        var observedFocus = scope.Focus.Count == 0 ? (int?)null : scope.Focus[^1].Key;
        var choices = new Dictionary<int, NestedChoice>();
        IReadOnlyList<string> status = [];
        string title;
        NestedKind kind;
        string? confirmationPrompt = null;
        int defaultFocus;

        switch (scope.Kind)
        {
            case BuildKind.Resolution:
                if (labels.Length < 2)
                {
                    return "The resolution selector did not render its heading and selectable entries.";
                }
                kind = NestedKind.Resolution;
                title = labels[0];
                for (var index = 1; index < labels.Length; index++)
                {
                    choices.Add(index - 1, new NestedChoice(labels[index], null, null, Disabled: false));
                }
                defaultFocus = 0;
                break;

            case BuildKind.ConfirmationA:
            case BuildKind.ConfirmationB:
                if (labels.Length != 2 || prompts.Length != 1)
                {
                    return "A Settings confirmation did not render one prompt and exactly two localized choices.";
                }
                kind = scope.Kind == BuildKind.ConfirmationA
                    ? NestedKind.ConfirmationA
                    : NestedKind.ConfirmationB;
                title = selected;
                confirmationPrompt = prompts[0];
                choices.Add(0, new NestedChoice(labels[0], null, null, Disabled: false));
                choices.Add(1, new NestedChoice(labels[1], null, null, Disabled: false));
                defaultFocus = 1;
                break;

            case BuildKind.Controller:
                if (labels.Length < 6 || scope.DynamicValues.Count != 4)
                {
                    return "Controller configuration did not render its heading, five selectable controls, and four visible button assignments.";
                }
                kind = NestedKind.Controller;
                title = labels[0];
                for (var index = 0; index < 5; index++)
                {
                    choices.Add(index, new NestedChoice(
                        labels[index + 1],
                        index < 4 ? scope.DynamicValues[index] : null,
                        null,
                        Disabled: false));
                }
                status = VisibleDetails(labels.Skip(6));
                defaultFocus = 0;
                break;

            case BuildKind.Keyboard:
                if (labels.Length < 25 || scope.DynamicValues.Count != 11)
                {
                    return $"Keyboard configuration rendered {labels.Length} text labels and {scope.DynamicValues.Count} live assignments; 25 labels and 11 assignments are required.";
                }
                kind = NestedKind.Keyboard;
                title = labels[0];
                for (var index = 0; index < 11; index++)
                {
                    choices.Add(index, new NestedChoice(
                        labels[1 + index * 2],
                        scope.DynamicValues[index],
                        null,
                        Disabled: false));
                }
                choices.Add(11, new NestedChoice(labels[23], null, null, Disabled: false));
                choices.Add(12, new NestedChoice(labels[24], null, null, Disabled: false));
                status = VisibleDetails(labels.Skip(25));
                defaultFocus = 0;
                break;

            case BuildKind.Licenses:
                if (scope.PageIndex is < 0 or >= LicensePageCount || labels.Length is < 1 or > 2)
                {
                    return "Licenses did not render a valid one-of-17 page with its visible heading and body.";
                }
                kind = NestedKind.Licenses;
                string heading;
                string body;
                if (labels.Length == 2)
                {
                    body = labels[0];
                    heading = labels[1];
                    title = TryExtractLicenseTitle(heading, out var localizedTitle)
                        ? localizedTitle
                        : string.Equals(selected, MenuTitle, StringComparison.Ordinal) ? "Licenses" : selected;
                }
                else if (prior is { Kind: NestedKind.Licenses, StatusDetails.Count: >= 2 } &&
                    TryRewriteLicenseHeading(prior.StatusDetails[0], scope.PageIndex, out heading))
                {
                    body = labels[0];
                    title = prior.Title;
                }
                else
                {
                    return "Licenses redrew its body without a reusable localized one-of-17 heading.";
                }
                choices.Add(0, new NestedChoice("Previous license page", null, null, scope.PageIndex == 0));
                choices.Add(1, new NestedChoice(
                    "Next license page",
                    null,
                    null,
                    scope.PageIndex == LicensePageCount - 1));
                choices.Add(2, new NestedChoice("Back", null, null, Disabled: false));
                status = [heading, body];
                defaultFocus = prior is { Kind: NestedKind.Licenses } ? prior.FocusKey : 0;
                break;

            default:
                return $"Unexpected nested Steam Settings builder kind {scope.Kind}.";
        }

        var focusKey = observedFocus ?? defaultFocus;
        if (!choices.ContainsKey(focusKey))
        {
            focusKey = defaultFocus;
        }
        var context = new NestedContext(
            kind,
            scope.Root,
            title,
            manager,
            new ReadOnlyDictionary<int, NestedChoice>(choices),
            focusKey,
            status,
            confirmationPrompt,
            scope.Kind == BuildKind.Licenses ? scope.PageIndex : null);
        lock (gate)
        {
            if (!hooksActive || epoch != scope.Epoch || Volatile.Read(ref faulted) != 0 ||
                !roots.Contains(scope.Root))
            {
                return "Steam Settings lifecycle changed before its nested page could be installed.";
            }
            nested = context;
        }
        DeferOrPublishNested(CreateNestedPresentation(context));
        return null;
    }

    private void HandleSetter(
        Func<SteamSettingsSetterInvokerDelegate> original,
        nint setter,
        int newIndex)
    {
        var callbackScope = rowSetterScope.Value;
        if (callbackScope is null)
        {
            // This std::function invoker is shared by unrelated game systems and
            // by the resolution selector. It is semantic Settings UI only while
            // the audited visible row callback owns an exact copied-row scope.
            original()(setter, newIndex);
            return;
        }

        ActiveState? basis;
        lock (gate)
        {
            basis = active;
        }
        if (basis?.Snapshot.Page is not { } page ||
            !TryResolveCallbackSetterRow(callbackScope.Value, basis, page, (nuint)setter, out var row))
        {
            original()(setter, newIndex);
            FailCoverage("Steam Settings setter ECX did not identify one exact active row in its audited visible callback copy.");
            return;
        }
        if (basis.Snapshot.Context == SteamSettingsContext.Title &&
            basis.Snapshot.Page is { Index: 0 } && row.Index == 1 &&
            row.Kind == SteamSettingsRowKind.Action && !string.IsNullOrWhiteSpace(row.Value) &&
            row.SelectedValueSourceAddress is null)
        {
            // The title Resolution row launches its dedicated selector and shows
            // a rendered resolution string, but it is not an ordinary left/right
            // value row. Its native setter rebuilds the page with a fresh exact
            // rendered-value observation.
            original()(setter, newIndex);
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

    private bool TryCaptureRowSetterScope(
        nuint callbackContext,
        ActiveState basis,
        out RowSetterScope scope)
    {
        scope = default;
        if (basis.Snapshot.Mode != SteamSettingsMode.Page || basis.Snapshot.Page is not { } page ||
            !TryReadPointer(callbackContext, out var root) || root != basis.Root ||
            !TryReadInt32(
                checked(callbackContext + RowCallbackRenderedCountOffset),
                out var renderedRowCount) ||
            !TryReadVector(
                checked(callbackContext + RowCallbackRowVectorOffset),
                out var rowBegin,
                out var rowEnd))
        {
            return false;
        }

        var rowBytes = (ulong)rowEnd - (ulong)rowBegin;
        if (renderedRowCount <= 0 || renderedRowCount != basis.RenderedRowCount ||
            page.Rows.Count != renderedRowCount || rowBytes % RowStride != 0)
        {
            return false;
        }
        var backingRowCount = rowBytes / RowStride;
        if (backingRowCount < (ulong)renderedRowCount ||
            backingRowCount > SteamSettingsCapture.MaximumRowCount)
        {
            return false;
        }

        scope = new RowSetterScope(basis.Root, basis.Generation, rowBegin, renderedRowCount);
        return true;
    }

    private bool TryResolveCallbackSetterRow(
        RowSetterScope scope,
        ActiveState basis,
        SteamSettingsPageSnapshot page,
        nuint setter,
        out SteamSettingsRowSnapshot row)
    {
        row = null!;
        if (setter == 0 || scope.Root != basis.Root || scope.Generation != basis.Generation ||
            scope.RenderedRowCount != basis.RenderedRowCount ||
            page.Rows.Count != scope.RenderedRowCount)
        {
            return false;
        }

        var firstSetter = checked(scope.RowVectorBegin + (nuint)RowSetterOffset);
        if (setter < firstSetter)
        {
            return false;
        }
        var delta = (ulong)setter - firstSetter;
        if (delta % RowStride != 0)
        {
            return false;
        }
        var rowIndex = checked((int)(delta / RowStride));
        if (rowIndex < 0 || rowIndex >= scope.RenderedRowCount)
        {
            return false;
        }

        var candidate = page.Rows[rowIndex];
        if (candidate.SetterAddress is null ||
            !TryReadPointer(checked(setter + 0x24u), out var setterTarget) || setterTarget == 0)
        {
            return false;
        }
        row = candidate;
        return true;
    }

    private string? FinalizeCoreBuild(BuildScope scope)
    {
        var errors = scope.Errors;
        if (errors.Count != 0)
        {
            return string.Join(" | ", errors);
        }
        if (scope.Kind == BuildKind.Page &&
            GetNested(scope.Root) is { Kind: NestedKind.Licenses } &&
            TryReadByte(scope.Root + ModeOffset, out var licenseMode) &&
            licenseMode == (byte)SteamSettingsMode.Page)
        {
            // The Licenses page replaces the ordinary row surface with its own
            // audited three-control reader. Its exact content and focus are
            // installed by the nested Licenses builder above.
            return null;
        }
        if (!TryReadByte(scope.Root + ModeOffset, out var finalMode))
        {
            return "Steam Settings final page/category mode could not be read after its builder returned.";
        }

        IReadOnlyList<SteamSettingsValueObservation> observations;
        var renderedRowCount = finalMode == (byte)SteamSettingsMode.Categories
            ? 0
            : scope.RowSources.Count;
        if (finalMode == (byte)SteamSettingsMode.Categories)
        {
            // The outer builder constructs the selected page before restoring the
            // category list. Those page observations are valid only inside the
            // nested page scope, not in the final category-mode snapshot.
            observations = [];
        }
        else if (!TryMaterializeValueObservations(scope, out observations, out var observationDiagnostic))
        {
            return observationDiagnostic;
        }
        if (!SteamSettingsCapture.TryCreateSnapshot(
                memory,
                imageBase,
                scope.Root,
                scope.Generation,
                observations,
                renderedRowCount,
                out var snapshot,
                out var diagnostic))
        {
            return diagnostic;
        }
        if (scope.Kind == BuildKind.Outer && scope.CategorySources.Count != snapshot.Categories.Count)
        {
            return $"Steam Settings rendered {scope.CategorySources.Count} audited category labels for {snapshot.Categories.Count} live categories.";
        }

        // Page builds also run as hidden category previews, both during the
        // outer build and when category focus changes. If authoritative mode
        // remains the category list, there is deliberately no active page to
        // install or present yet.
        var nestedInOuterBuild = false;
        lock (gate)
        {
            nestedInOuterBuild = scope.Kind == BuildKind.Page && outerBuild is { } outer &&
                outer.Root == scope.Root && outer.OwnerThreadId == scope.OwnerThreadId;
        }
        if (scope.Kind == BuildKind.Page && snapshot.Mode == SteamSettingsMode.Categories)
        {
            return null;
        }
        if (scope.Kind == BuildKind.Page &&
            (snapshot.Page is null || scope.RowSources.Count != snapshot.Page.Rows.Count))
        {
            return $"Steam Settings rendered {scope.RowSources.Count} audited row labels without a matching complete live page.";
        }

        // A page builder is allowed to nest the outer builder. The outer scope
        // owns that transaction's one installed snapshot and presentation.
        if (nestedInOuterBuild)
        {
            return null;
        }

        var state = new ActiveState(
            scope.Root,
            scope.Generation,
            snapshot,
            new ReadOnlyCollection<SteamSettingsValueObservation>(observations.ToArray()),
            renderedRowCount);
        AccessibilityEvent? publish = null;
        DeferredPresentation? schedule = null;
        var hasNestedPresentation = deferredNestedEvents.Value!.Count != 0;
        lock (gate)
        {
            if (!hooksActive || epoch != scope.Epoch || Volatile.Read(ref faulted) != 0 || !roots.Contains(scope.Root))
            {
                return "Steam Settings lifecycle changed before its post-original snapshot could be installed.";
            }
            var firstPresentation = active is null;
            active = state;
            // Selecting a screen size rebuilds Display Settings behind the
            // selector. Keep its context until the owning apply callback returns
            // after closing it and publishes the rebuilt page exactly once.
            var preservesResolutionSelector = scope.Kind == BuildKind.Page && callbackDepth.Value != 0 &&
                nested is { Kind: NestedKind.Resolution, Root: var resolutionRoot } &&
                resolutionRoot == scope.Root;
            if ((scope.Kind == BuildKind.Page || snapshot.Mode == SteamSettingsMode.Categories) &&
                !hasNestedPresentation && !preservesResolutionSelector &&
                nested is { Root: var nestedRoot } && nestedRoot == scope.Root)
            {
                nested = null;
            }
            var presented = CreateActivePresentation(snapshot);
            if (firstPresentation)
            {
                schedule = new DeferredPresentation(scope.Root, scope.Generation, presented);
                deferredPresentation = schedule;
            }
            else if (callbackDepth.Value == 0 && !hasNestedPresentation)
            {
                publish = presented;
            }
        }
        if (publish is not null)
        {
            PublishSafely(publish);
        }
        if (schedule is not null && snapshot.Context != SteamSettingsContext.Title)
        {
            QueueDeferredPresentation(schedule);
        }
        return null;
    }

    private void QueueDeferredPresentation(DeferredPresentation pending)
    {
        // Retain the in-game entry path's existing deferred publication. Title
        // Settings is flushed by its owning action-5 callback in Mod.cs instead;
        // a timer cannot prove that the native caller has finished rebuilding UI.
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
                outer.ReplaceSelections(scope.Selections);
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

    private void ObserveRenderedValueProbe(ProbeDefinition definition, nuint source)
    {
        try
        {
            if (definition.Kind != ProbeKind.TitleResolutionValue ||
                !TryGetOwnedScope(definition.Scope, out var scope))
            {
                return;
            }
            var diagnostic = "the audited EDX text argument was null";
            if (source == 0 || !stringReader.TryRead(source, out var value, out diagnostic) ||
                string.IsNullOrWhiteSpace(value))
            {
                scope.AddError($"The title Resolution value was not readable at its exact render call: {diagnostic}");
                return;
            }
            RecordMarkedLabel(scope, definition.Kind, source, value);
        }
        catch (Exception exception)
        {
            FailCoverage($"Steam Settings direct rendered-value observation failed: {FormatException(exception)}");
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
                var previous = scope.CurrentRowAddress;
                if (source == 0 || (previous != 0 && source != checked(previous + (nuint)RowStride)))
                {
                    scope.AddError("Steam Settings row label sources were not one ordered temporary 0x88-byte row vector.");
                    return;
                }
                scope.AddRowSource(source);
                break;
            }
            case ProbeKind.TitleResolutionValue:
            {
                if (scope.PageIndex != 0 || scope.CurrentRowIndex != 1 || scope.CurrentRowAddress == 0 ||
                    !TryReadInt32(scope.Root + ContextOffset, out var context) ||
                    context != (int)SteamSettingsContext.Title)
                {
                    scope.AddError("The title Resolution value was rendered outside its exact Display Settings row.");
                    return;
                }
                scope.AddSelection(new SteamSettingsValueSelection(
                    scope.PageIndex,
                    scope.CurrentRowIndex,
                    SelectedIndex: null,
                    RenderedSourceAddress: source,
                    RenderedActionValue: value));
                break;
            }
            case ProbeKind.SelectedValue:
            {
                var rowAddress = scope.CurrentRowAddress;
                if (rowAddress == 0 || !TryReadVector(rowAddress + RowValueVectorOffset, out var begin, out var end) ||
                    source < begin || source >= end || ((ulong)source - begin) % StringStride != 0)
                {
                    scope.AddError("Steam Settings selected value was not aligned in the current temporary row value vector.");
                    return;
                }
                scope.AddSelection(new SteamSettingsValueSelection(
                    scope.PageIndex,
                    scope.CurrentRowIndex,
                    checked((int)(((ulong)source - begin) / StringStride)),
                    RenderedSourceAddress: source,
                    RenderedActionValue: null));
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
        if (TryReadByte(basis.Root + ModeOffset, out var mode) && mode == (byte)SteamSettingsMode.Categories)
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
        if (!TryReadByte(basis.Root + ModeOffset, out var mode))
        {
            refreshed = null!;
            diagnostic = "Steam Settings page/category mode could not be read during post-original refresh.";
            return false;
        }
        var renderedRowCount = mode == (byte)SteamSettingsMode.Categories
            ? 0
            : basis.RenderedRowCount;
        if (!SteamSettingsCapture.TryCreateSnapshot(
                memory,
                imageBase,
                basis.Root,
                basis.Generation,
                observations,
                renderedRowCount,
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
            new ReadOnlyCollection<SteamSettingsValueObservation>(observations.ToArray()),
            renderedRowCount);
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

    private static bool OwnsManager(SteamSettingsSnapshot snapshot, nuint manager) =>
        snapshot.Mode == SteamSettingsMode.Categories
            ? snapshot.CategoryManagerAddress == manager
            : snapshot.ActiveManagerAddresses.Contains(manager);

    private static uint GetNestedCallbackRootOffset(NestedKind kind) => kind switch
    {
        NestedKind.Resolution or NestedKind.Controller => 4,
        NestedKind.ConfirmationA or NestedKind.ConfirmationB => 8,
        NestedKind.Licenses or NestedKind.Keyboard => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown nested Settings callback kind."),
    };

    private static bool TryGetCallbackTargetFocus(
        CallbackKind kind,
        SteamSettingsSnapshot snapshot,
        int key,
        out MenuFocus focus)
    {
        if (kind == CallbackKind.Category && snapshot.Mode == SteamSettingsMode.Categories &&
            key >= 0 && key < snapshot.Categories.Count)
        {
            var category = snapshot.Categories[key];
            focus = new MenuFocus(
                category.Label,
                null,
                key + 1,
                snapshot.Categories.Count,
                OptionalText(category.Help),
                Disabled: false);
            return true;
        }
        if (kind == CallbackKind.Row && snapshot.Mode == SteamSettingsMode.Page &&
            snapshot.Page is { } page && key >= 0)
        {
            var rowIndex = key / 10;
            if (rowIndex >= 0 && rowIndex < page.Rows.Count)
            {
                focus = ToRowFocus(page.Rows[rowIndex], rowIndex, page.Rows.Count + 1);
                return true;
            }
            if (key == page.ReturnControl.Key)
            {
                focus = new MenuFocus(
                    page.ReturnControl.Label,
                    page.ReturnControl.Value,
                    page.ReturnControl.Position,
                    page.ReturnControl.Count,
                    OptionalText(page.ReturnControl.Help),
                    Disabled: !page.ReturnControl.Enabled);
                return true;
            }
        }
        focus = null!;
        return false;
    }

    private static bool IsCoreActivation(CallbackKind kind, SteamSettingsSnapshot snapshot, int key)
    {
        if (kind == CallbackKind.Category)
        {
            return snapshot.Mode == SteamSettingsMode.Categories && key == snapshot.FocusKey &&
                key >= 0 && key < snapshot.Categories.Count;
        }
        if (kind != CallbackKind.Row || snapshot.Mode != SteamSettingsMode.Page ||
            snapshot.Page is not { } page || key < 0)
        {
            return false;
        }
        if (key == page.ReturnControl.Key)
        {
            return true;
        }
        var rowIndex = key / 10;
        return rowIndex >= 0 && rowIndex < page.Rows.Count &&
            page.Rows[rowIndex].Kind == SteamSettingsRowKind.Action;
    }

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
                    OptionalText(snapshot.Categories[index].Help),
                    Disabled: false),
            SteamSettingsFocusKind.Row when snapshot.Page is { } page && snapshot.FocusedRowIndex is { } index =>
                ToRowFocus(page.Rows[index], index, page.Rows.Count + 1),
            SteamSettingsFocusKind.ReturnToCategories when snapshot.Page is { } page =>
                new MenuFocus(
                    page.ReturnControl.Label,
                    page.ReturnControl.Value,
                    page.ReturnControl.Position,
                    page.ReturnControl.Count,
                    OptionalText(page.ReturnControl.Help),
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

    private static string? OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static MenuPresented CreateActivePresentation(SteamSettingsSnapshot snapshot)
    {
        var title = MenuTitle;
        if (snapshot.Mode == SteamSettingsMode.Page && snapshot.ActivePageIndex is { } page &&
            page >= 0 && page < snapshot.Categories.Count)
        {
            title = snapshot.Categories[page].Label;
        }
        return new MenuPresented(title, ToFocus(snapshot), []);
    }

    private NestedContext? GetNested(nuint root)
    {
        lock (gate)
        {
            return nested?.Root == root ? nested : null;
        }
    }

    private static IReadOnlyList<string> VisibleDetails(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray());

    private static bool TryRewriteLicenseHeading(string template, int pageIndex, out string heading)
    {
        heading = string.Empty;
        var suffix = $"/{LicensePageCount}";
        if (pageIndex is < 0 or >= LicensePageCount ||
            !template.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }
        var pageEnd = template.Length - suffix.Length;
        var pageStart = pageEnd;
        while (pageStart > 0 && char.IsAsciiDigit(template[pageStart - 1]))
        {
            pageStart--;
        }
        if (pageStart == pageEnd)
        {
            return false;
        }
        heading = template[..pageStart] +
            (pageIndex + 1).ToString(CultureInfo.InvariantCulture) + template[pageEnd..];
        return true;
    }

    private static bool TryExtractLicenseTitle(string heading, out string title)
    {
        title = string.Empty;
        var suffix = $"/{LicensePageCount}";
        if (!heading.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }
        var pageEnd = heading.Length - suffix.Length;
        var pageStart = pageEnd;
        while (pageStart > 0 && char.IsAsciiDigit(heading[pageStart - 1]))
        {
            pageStart--;
        }
        title = heading[..pageStart].TrimEnd();
        return title.Length != 0;
    }

    private static AccessibilityEvent CreateNestedPresentation(NestedContext context)
    {
        var ordered = context.Choices.OrderBy(pair => pair.Key).ToArray();
        var selectedIndex = Array.FindIndex(ordered, pair => pair.Key == context.FocusKey);
        if (selectedIndex < 0)
        {
            throw new InvalidOperationException("Nested Settings focus key is absent from its choices.");
        }
        if (context.Kind is NestedKind.ConfirmationA or NestedKind.ConfirmationB)
        {
            if (string.IsNullOrWhiteSpace(context.ConfirmationPrompt))
            {
                throw new InvalidOperationException("Nested Settings confirmation prompt is blank.");
            }
            return new MenuConfirmationPresented(
                context.ConfirmationPrompt,
                ordered.Select(pair => pair.Value.Label).ToArray(),
                selectedIndex);
        }
        return new MenuPresented(context.Title, ToNestedFocus(context, context.FocusKey), context.StatusDetails);
    }

    private static AccessibilityEvent CreateNestedFocusEvent(NestedContext context, int key)
    {
        var ordered = context.Choices.OrderBy(pair => pair.Key).ToArray();
        var selectedIndex = Array.FindIndex(ordered, pair => pair.Key == key);
        if (selectedIndex < 0)
        {
            throw new InvalidOperationException("Nested Settings focus key is absent from its choices.");
        }
        return context.Kind is NestedKind.ConfirmationA or NestedKind.ConfirmationB
            ? new MenuConfirmationFocused(ordered[selectedIndex].Value.Label, selectedIndex, ordered.Length)
            : new MenuFocusChanged(ToNestedFocus(context, key));
    }

    private static MenuFocus ToNestedFocus(NestedContext context, int key)
    {
        var ordered = context.Choices.OrderBy(pair => pair.Key).ToArray();
        var position = Array.FindIndex(ordered, pair => pair.Key == key);
        if (position < 0)
        {
            throw new InvalidOperationException("Nested Settings focus key is absent from its choices.");
        }
        var choice = ordered[position].Value;
        return new MenuFocus(
            choice.Label,
            OptionalText(choice.Value),
            position + 1,
            ordered.Length,
            OptionalText(choice.Help),
            choice.Disabled);
    }

    private void DeferOrPublishNested(AccessibilityEvent accessibilityEvent)
    {
        if (callbackDepth.Value != 0 || IsCoreBuildOwnedByCurrentThread())
        {
            deferredNestedEvents.Value!.Add(accessibilityEvent);
            return;
        }
        PublishSafely(accessibilityEvent);
    }

    private IReadOnlyList<AccessibilityEvent> DrainDeferredNestedEvents()
    {
        var pending = deferredNestedEvents.Value!;
        if (pending.Count == 0)
        {
            return [];
        }
        var result = pending.ToArray();
        pending.Clear();
        return result;
    }

    private void PublishActivePage(nuint root)
    {
        SteamSettingsSnapshot? snapshot;
        lock (gate)
        {
            snapshot = active?.Root == root ? active.Snapshot : null;
        }
        if (snapshot is null)
        {
            return;
        }
        PublishSafely(CreateActivePresentation(snapshot));
    }

    private bool TryGetConfirmationPromptScope(int bank, int messageId, out BuildScope scope)
    {
        if (bank == 0x23 && messageId == 0xD4 &&
            TryGetOwnedScope(BuildKind.ConfirmationA, out scope))
        {
            return true;
        }
        if (bank == 0x3A && messageId == 5 &&
            TryGetOwnedScope(BuildKind.ConfirmationB, out scope))
        {
            return true;
        }
        scope = null!;
        return false;
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

    private bool IsCoreBuildOwnedByCurrentThread()
    {
        var thread = Environment.CurrentManagedThreadId;
        lock (gate)
        {
            return outerBuild?.OwnerThreadId == thread || pageBuild?.OwnerThreadId == thread;
        }
    }

    private static bool CapturesEveryRenderedLabel(BuildKind kind) =>
        kind is BuildKind.Licenses or BuildKind.Controller or BuildKind.Keyboard;

    private static bool IsOverlay(BuildKind kind) =>
        kind is BuildKind.Resolution or BuildKind.ConfirmationA or BuildKind.ConfirmationB or
            BuildKind.Licenses or BuildKind.Controller or BuildKind.Keyboard;

    private bool TryMaterializeValueObservations(
        BuildScope scope,
        out IReadOnlyList<SteamSettingsValueObservation> observations,
        out string diagnostic)
    {
        var materialized = new List<SteamSettingsValueObservation>();
        var seenRows = new HashSet<(int Page, int Row)>();
        foreach (var selection in scope.Selections)
        {
            if (selection.PageIndex < 0 || selection.RowIndex < 0 ||
                !seenRows.Add((selection.PageIndex, selection.RowIndex)) ||
                !TryGetRowVectorBegin(scope.Root, selection.PageIndex, out var rowBegin))
            {
                observations = [];
                diagnostic = "Steam Settings selected-value observations do not identify unique live page rows.";
                return false;
            }
            var rowAddress = checked(rowBegin + (nuint)(selection.RowIndex * RowStride));
            if (selection.RenderedActionValue is not null)
            {
                if (selection.PageIndex != 0 || selection.RowIndex != 1 || selection.SelectedIndex is not null ||
                    selection.RenderedSourceAddress == 0 ||
                    !TryReadInt32(scope.Root + ContextOffset, out var context) ||
                    context != (int)SteamSettingsContext.Title)
                {
                    observations = [];
                    diagnostic = "Steam Settings title Resolution observation did not identify its exact Display Settings row.";
                    return false;
                }
                materialized.Add(new SteamSettingsValueObservation(
                    scope.Generation,
                    scope.Root,
                    selection.PageIndex,
                    rowAddress,
                    selection.RenderedSourceAddress,
                    new string(selection.RenderedActionValue.AsSpan())));
                continue;
            }
            if (selection.SelectedIndex is not int selectedIndex || selectedIndex < 0)
            {
                observations = [];
                diagnostic = $"Steam Settings row {selection.RowIndex} has no audited selected value index.";
                return false;
            }
            if (!TryReadVector(rowAddress + RowValueVectorOffset, out var valueBegin, out var valueEnd))
            {
                observations = [];
                diagnostic = $"Steam Settings live value vector for row {selection.RowIndex} is unreadable.";
                return false;
            }
            var byteLength = (ulong)valueEnd - (ulong)valueBegin;
            if (byteLength % StringStride != 0 ||
                (ulong)selectedIndex >= byteLength / StringStride)
            {
                observations = [];
                diagnostic = $"Steam Settings selected index for row {selection.RowIndex} is outside its final live value vector.";
                return false;
            }
            var source = checked(valueBegin + (nuint)(selectedIndex * StringStride));
            materialized.Add(new SteamSettingsValueObservation(
                scope.Generation,
                scope.Root,
                selection.PageIndex,
                rowAddress,
                source));
        }
        observations = new ReadOnlyCollection<SteamSettingsValueObservation>(materialized);
        diagnostic = string.Empty;
        return true;
    }

    private bool TryGetRowVectorBegin(nuint root, int pageIndex, out nuint begin)
    {
        begin = 0;
        if (pageIndex < 0 || !TryReadPointer(root + DescriptorVectorOffset, out var descriptors))
        {
            return false;
        }
        var descriptor = checked(descriptors + (nuint)(pageIndex * DescriptorStride));
        return TryReadPointer(descriptor, out begin) && begin != 0;
    }

    private bool TryCaptureControllerBinding(int rowIndex, out string value, out string diagnostic)
    {
        value = string.Empty;
        diagnostic = string.Empty;
        if (rowIndex is < 0 or > 3 ||
            !TryReadInt32(imageBase + ControllerButtonIdsRva + (nuint)(rowIndex * sizeof(int)), out var wanted) ||
            !TryReadPointer(imageBase + RuntimeConfigPointerRva, out var config) || config == 0)
        {
            diagnostic = $"Controller binding row {rowIndex} does not identify the audited button/configuration tables.";
            return false;
        }
        var iconIndex = -1;
        for (var index = 0; index < 4; index++)
        {
            if (!TryReadInt32(config + ControllerBindingsOffset + (nuint)(index * sizeof(int)), out var current))
            {
                diagnostic = "The live controller binding table was unreadable.";
                return false;
            }
            if (current == wanted)
            {
                iconIndex = index;
                break;
            }
        }
        value = iconIndex switch
        {
            0 => "Button A",
            1 => "Button B",
            2 => "Button Y",
            3 => "Button X",
            _ => string.Empty,
        };
        if (value.Length == 0)
        {
            diagnostic = $"Controller binding row {rowIndex} has no matching visible A, B, Y, or X button icon.";
            return false;
        }
        return true;
    }

    private bool TryCaptureKeyboardBinding(
        nuint context,
        int rowIndex,
        out string value,
        out string diagnostic)
    {
        value = string.Empty;
        diagnostic = string.Empty;
        if (rowIndex is < 0 or > 10 ||
            !TryReadPointer(context, out var root) || !TryReadExactVtable(root, SteamSettingsCapture.RootVtableRva) ||
            !TryReadPointer(imageBase + RuntimeConfigPointerRva, out var config) || config == 0 ||
            !TryReadInt32(config + KeyboardBindingsOffset + (nuint)(rowIndex * sizeof(int)), out var virtualKey))
        {
            diagnostic = $"Keyboard binding row {rowIndex} does not identify the audited root and live key table.";
            return false;
        }
        if (virtualKey is 0xDC or 0xE2)
        {
            value = "Backslash";
            return true;
        }
        if (!TryMapKeyboardLookupIndex(virtualKey, out var lookupIndex))
        {
            value = "Unassigned";
            return true;
        }
        if (!TryReadPointer(root + KeyboardLookupVectorOffset, out var begin) || begin == 0 ||
            !stringReader.TryRead(begin + (nuint)(lookupIndex * StringStride), out value, out diagnostic))
        {
            diagnostic = $"Keyboard binding row {rowIndex} could not read lookup label {lookupIndex}: {diagnostic}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "Unassigned";
        }
        return true;
    }

    private static bool TryMapKeyboardLookupIndex(int virtualKey, out int index)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            index = virtualKey - 0x1C;
            return true;
        }
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            index = virtualKey - 0x27;
            return true;
        }
        if (virtualKey is >= 0x60 and <= 0x69)
        {
            index = virtualKey - 0x57;
            return true;
        }
        if (virtualKey is >= 0x25 and <= 0x28)
        {
            index = virtualKey - 4;
            return true;
        }
        index = virtualKey switch
        {
            0x08 => 2,
            0x0D => 3,
            0x1B => 1,
            0x20 => 8,
            0x2E => 4,
            0x6A => 30,
            0x6B => 31,
            0x6D or 0xBD => 19,
            0x6E or 0xBE => 28,
            0x6F or 0xBF => 29,
            0xA0 or 0xA1 => 5,
            0xA2 or 0xA3 => 6,
            0xA4 or 0xA5 => 7,
            0xBA => 25,
            0xBB => 24,
            0xBC => 27,
            0xC0 => 22,
            0xDB => 23,
            0xDD => 26,
            0xDE => 20,
            _ => -1,
        };
        return index >= 0;
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
                nested = null;
                deferredPresentation = null;
                CancelScopesLocked();
                shouldReport = true;
            }
        }
        pendingMarker.Value = null;
        deferredNestedEvents.Value?.Clear();
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
        private readonly List<SteamSettingsValueSelection> selections = [];
        private readonly List<string> labels = [];
        private readonly List<string> prompts = [];
        private readonly List<nuint> constructed = [];
        private readonly List<(nuint Manager, nuint State, int Key)> bindings = [];
        private readonly List<(nuint Manager, int Key)> focus = [];
        private readonly Dictionary<int, string> dynamicValues = [];

        public int Epoch { get; } = epoch;
        public int OwnerThreadId { get; } = ownerThreadId;
        public BuildKind Kind { get; } = kind;
        public nuint Root { get; } = root;
        public int PageIndex { get; } = pageIndex;
        public int Generation { get; } = generation;
        public bool Cancelled { get; set; }
        public nuint CurrentRowAddress { get { lock (gate) return rowSources.Count == 0 ? 0 : rowSources[^1]; } }
        public int CurrentRowIndex { get { lock (gate) return rowSources.Count - 1; } }
        public IReadOnlyList<string> Errors { get { lock (gate) return errors.ToArray(); } }
        public IReadOnlyList<nuint> CategorySources { get { lock (gate) return categorySources.ToArray(); } }
        public IReadOnlyList<nuint> RowSources { get { lock (gate) return rowSources.ToArray(); } }
        public IReadOnlyList<SteamSettingsValueSelection> Selections { get { lock (gate) return selections.ToArray(); } }
        public IReadOnlyList<string> Labels { get { lock (gate) return labels.ToArray(); } }
        public IReadOnlyList<string> Prompts { get { lock (gate) return prompts.ToArray(); } }
        public IReadOnlyList<(nuint Manager, nuint State, int Key)> Bindings { get { lock (gate) return bindings.ToArray(); } }
        public IReadOnlyList<(nuint Manager, int Key)> Focus { get { lock (gate) return focus.ToArray(); } }
        public IReadOnlyDictionary<int, string> DynamicValues
        {
            get
            {
                lock (gate)
                {
                    return new ReadOnlyDictionary<int, string>(dynamicValues.ToDictionary(pair => pair.Key, pair => pair.Value));
                }
            }
        }

        public void AddError(string error) { lock (gate) errors.Add(error); }
        public void AddCategorySource(nuint source) { lock (gate) categorySources.Add(source); }
        public void AddRowSource(nuint source) { lock (gate) rowSources.Add(source); }
        public void AddSelection(SteamSettingsValueSelection selection) { lock (gate) selections.Add(selection); }
        public void AddLabel(string label) { lock (gate) labels.Add(new string(label.AsSpan())); }
        public void AddPrompt(string prompt) { lock (gate) prompts.Add(new string(prompt.AsSpan())); }
        public void AddConstructed(nuint control) { lock (gate) constructed.Add(control); }
        public void AddBinding(nuint manager, nuint state, int key) { lock (gate) bindings.Add((manager, state, key)); }
        public void AddFocus(nuint manager, int key) { lock (gate) focus.Add((manager, key)); }
        public void AddDynamicValue(int row, string value)
        {
            lock (gate)
            {
                if (!dynamicValues.TryAdd(row, new string(value.AsSpan())))
                {
                    errors.Add($"Nested Settings binding row {row} was captured more than once during one build.");
                }
            }
        }
        public void ReplaceSelections(IEnumerable<SteamSettingsValueSelection> values)
        {
            lock (gate)
            {
                selections.Clear();
                selections.AddRange(values);
            }
        }
    }

    private sealed record ActiveState(
        nuint Root,
        int Generation,
        SteamSettingsSnapshot Snapshot,
        IReadOnlyList<SteamSettingsValueObservation> Observations,
        int RenderedRowCount);

    private sealed record DeferredPresentation(nuint Root, int Generation, MenuPresented Event);
    private sealed record NestedChoice(
        string Label,
        string? Value,
        string? Help,
        bool Disabled);
    private sealed record NestedContext(
        NestedKind Kind,
        nuint Root,
        string Title,
        nuint Manager,
        IReadOnlyDictionary<int, NestedChoice> Choices,
        int FocusKey,
        IReadOnlyList<string> StatusDetails,
        string? ConfirmationPrompt,
        int? LicensePageIndex);
    private readonly record struct SteamSettingsValueSelection(
        int PageIndex,
        int RowIndex,
        int? SelectedIndex,
        nuint RenderedSourceAddress,
        string? RenderedActionValue);
    private readonly record struct RowSetterScope(
        nuint Root,
        int Generation,
        nuint RowVectorBegin,
        int RenderedRowCount);
    private readonly record struct ProbeDefinition(HookId Id, ProbeKind Kind, BuildKind Scope);
    private readonly record struct PendingMarker(HookId Id, ProbeKind Kind, BuildKind ExpectedScope, BuildScope Scope);

    private enum BuildKind
    {
        Outer,
        Page,
        Resolution,
        ConfirmationA,
        ConfirmationB,
        Licenses,
        Controller,
        Keyboard,
    }

    private enum ProbeKind
    {
        CategoryLabel,
        RowLabel,
        TitleResolutionValue,
        SelectedValue,
        OverlayLabel,
    }

    private enum CallbackKind
    {
        Category,
        Row,
    }

    private enum NestedKind
    {
        Resolution,
        ConfirmationA,
        ConfirmationB,
        Licenses,
        Controller,
        Keyboard,
    }
}
