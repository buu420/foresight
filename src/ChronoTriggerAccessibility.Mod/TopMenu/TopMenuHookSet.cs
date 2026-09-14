using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
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

namespace ChronoTriggerAccessibility.Mod.TopMenu;

public interface ITopMenuCaptureFactory
{
    bool TryBegin(
        IReadableMemory memory,
        nuint imageBase,
        nuint root,
        TopMenuStyle style,
        out ITopMenuCapture capture,
        out string diagnostic);
}

public interface ITopMenuCapture : IDisposable
{
    bool TryRecordUtf8(nuint absoluteReturnAddress, string text, out string diagnostic);
    bool TryRecordConstructedControl(nuint control, int position, bool enabled, bool visible, out string diagnostic);
    bool TryRecordManagerKeyBinding(nuint manager, nuint control, int key, out string diagnostic);
    bool TryBeginStatusBar(nuint statusBar, out ITopMenuStatusCapture status, out string diagnostic);
    bool TryCreateSnapshot(out TopMenuSnapshot snapshot, out string diagnostic);
}

public interface ITopMenuStatusCapture : IDisposable
{
    bool TryRecordRenderedLine(nuint absoluteReturnAddress, nuint wideStringAddress, out string diagnostic);
    bool TryComplete(out string diagnostic);
}

public sealed class NativeTopMenuCaptureFactory : ITopMenuCaptureFactory
{
    public bool TryBegin(
        IReadableMemory memory,
        nuint imageBase,
        nuint root,
        TopMenuStyle style,
        out ITopMenuCapture capture,
        out string diagnostic)
    {
        if (!TopMenuCaptureScope.TryBegin(memory, imageBase, root, style, out var native, out diagnostic))
        {
            capture = null!;
            return false;
        }
        capture = new NativeCapture(native);
        return true;
    }

    private sealed class NativeCapture(TopMenuCaptureScope inner) : ITopMenuCapture
    {
        public bool TryRecordUtf8(nuint absoluteReturnAddress, string text, out string diagnostic) =>
            inner.TryRecordUtf8(absoluteReturnAddress, text, out diagnostic);

        public bool TryRecordConstructedControl(
            nuint control,
            int position,
            bool enabled,
            bool visible,
            out string diagnostic) =>
            inner.TryRecordConstructedControl(control, position, enabled, visible, out diagnostic);

        public bool TryRecordManagerKeyBinding(
            nuint manager,
            nuint control,
            int key,
            out string diagnostic) =>
            inner.TryRecordManagerKeyBinding(manager, control, key, out diagnostic);

        public bool TryBeginStatusBar(
            nuint statusBar,
            out ITopMenuStatusCapture status,
            out string diagnostic)
        {
            if (!inner.TryBeginStatusBar(statusBar, out var native, out diagnostic))
            {
                status = null!;
                return false;
            }
            status = new NativeStatusCapture(native);
            return true;
        }

        public bool TryCreateSnapshot(out TopMenuSnapshot snapshot, out string diagnostic) =>
            inner.TryCreateSnapshot(out snapshot, out diagnostic);

        public void Dispose() => inner.Dispose();
    }

    private sealed class NativeStatusCapture(TopMenuStatusCaptureScope inner) : ITopMenuStatusCapture
    {
        public bool TryRecordRenderedLine(
            nuint absoluteReturnAddress,
            nuint wideStringAddress,
            out string diagnostic) =>
            inner.TryRecordRenderedLine(absoluteReturnAddress, wideStringAddress, out diagnostic);

        public bool TryComplete(out string diagnostic) => inner.TryComplete(out diagnostic);
        public void Dispose() => inner.Dispose();
    }
}

/// <summary>
/// Captures the exact ordinary field top menu. Every rendered string is paired
/// with a one-shot audited call-site marker before it can enter the native
/// capture grammar.
/// </summary>
public sealed class TopMenuHookSet : IHookActivationObserver, ISharedNativeHookObserver
{
    public const uint ManagerFocusKeyOffset = 0x2C4;
    public const uint FocusableStateControlOffset = 0x14;
    public const uint WidgetVisibleOffset = 0x1AD;
    public const uint WidgetEnabledOffset = 0x2E2;
    public const uint ClassicParentTopMenuOffset = 0x290;
    public const uint TouchParentTopMenuOffset = 0x294;
    public const uint ManagerVtableRva = 0x3A5D0C;
    public const uint CustomButtonVtableRva = 0x3A4364;
    public const uint FocusableStateVtableRva = 0x3AC3F4;

    private const string UnsupportedBoundary = "This top-menu subpage is not accessible yet.";
    private const string UnsupportedReturnInstruction = "Press Cancel to return to the accessible top menu.";

    private static readonly HookId[] FunctionHookIds =
    [
        HookId.ClassicTopMenuBuilder,
        HookId.TouchTopMenuBuilder,
        HookId.MenuTextLabelFactory,
        HookId.StatusBarFormatScope,
        HookId.StatusBarGlyphRenderer,
        HookId.StatusBarDestructor,
        HookId.ClassicTopMenuDeletingDestructor,
        HookId.TouchTopMenuDeletingDestructor,
        HookId.ClassicTopMenuActionDispatcher,
        HookId.TouchTopMenuActionDispatcher,
    ];

    private static readonly ProbeDefinition[] ProbeDefinitions =
    [
        Label(HookId.ClassicTopMenuTimeLabelCallSite, StyleMask.Classic),
        Label(HookId.ClassicTopMenuCurrencyLabelCallSite, StyleMask.Classic),
        Label(HookId.ClassicTopMenuSingleFooterLabelCallSite, StyleMask.Classic),
        Label(HookId.ClassicTopMenuFirstFooterLabelCallSite, StyleMask.Classic),
        Label(HookId.ClassicTopMenuSecondFooterLabelCallSite, StyleMask.Classic),
        Label(HookId.ClassicTopMenuContextLabelCallSite, StyleMask.Classic),
        new(HookId.StatusBarHiddenLabelCallSite, ProbeTarget.HiddenLabel, StyleMask.Both),
        new(HookId.StatusBarEmptyLineLabelCallSite, ProbeTarget.EmptyStatusLineLabel, StyleMask.Both),
        new(HookId.TouchStatusBarInitialLabelCallSite, ProbeTarget.OptionalLabel, StyleMask.Touch),
        Label(HookId.ClassicTopMenuCaptionLabelCallSite, StyleMask.Classic),
        Label(HookId.ClassicTopMenuMemberNameLabelCallSite, StyleMask.Classic),
        Label(HookId.ClassicStatusRowLabelCallSite, StyleMask.Classic),
        Label(HookId.ClassicStatusRowZeroValueCallSite, StyleMask.Classic),
        Label(HookId.ClassicStatusUnavailableValueCallSite, StyleMask.Classic),
        Label(HookId.ClassicStatusCurrentValueCallSite, StyleMask.Classic),
        Label(HookId.ClassicStatusMaximumValueCallSite, StyleMask.Classic),
        Label(HookId.ClassicStatusExtraLabelCallSite, StyleMask.Classic),
        Label(HookId.TouchTopMenuTimeLabelCallSite, StyleMask.Touch),
        Label(HookId.TouchTopMenuCurrencyLabelCallSite, StyleMask.Touch),
        Label(HookId.TouchTopMenuCaptionLabelCallSite, StyleMask.Touch),
        Label(HookId.TouchTopMenuMemberNameLabelCallSite, StyleMask.Touch),
        new(HookId.TouchTopMenuReserveNameLabelCallSite, ProbeTarget.Rejected, StyleMask.Touch),
        Label(HookId.CompactStatusRowLabelCallSite, StyleMask.Both),
        Label(HookId.CompactStatusRowZeroValueCallSite, StyleMask.Both),
        Label(HookId.CompactStatusCurrentValueCallSite, StyleMask.Both),
        Label(HookId.CompactStatusMaximumValueCallSite, StyleMask.Both),
        Label(HookId.CompactStatusExtraLabelCallSite, StyleMask.Both),
        new(HookId.StatusBarGlyphRendererCallSite, ProbeTarget.Renderer, StyleMask.Both),
    ];

    private static readonly IReadOnlyDictionary<HookId, ProbeDefinition> ProbesById =
        ProbeDefinitions.ToDictionary(definition => definition.Id);

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IRuntimeNativeAsmHookFactory asmHookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly ITopMenuCaptureFactory captureFactory;
    private readonly MsvcStringReader stringReader;
    private readonly object gate = new();
    private readonly HashSet<HookId> preparedIds = [];
    private readonly ThreadLocal<PendingProbe?> pendingProbe = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private BuildContext? buildContext;
    private ActiveContext? activeContext;
    private ActionTransaction? actionTransaction;
    private int epoch;
    private int transitionActive;
    private int faulted;
    private bool hooksActive;

    public TopMenuHookSet(
        IRuntimeNativeHookFactory hookFactory,
        IRuntimeNativeAsmHookFactory asmHookFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher)
        : this(hookFactory, asmHookFactory, memory, dispatcher, new NativeTopMenuCaptureFactory())
    {
    }

    public TopMenuHookSet(
        IRuntimeNativeHookFactory hookFactory,
        IRuntimeNativeAsmHookFactory asmHookFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher,
        ITopMenuCaptureFactory captureFactory)
    {
        this.hookFactory = hookFactory ?? throw new ArgumentNullException(nameof(hookFactory));
        this.asmHookFactory = asmHookFactory ?? throw new ArgumentNullException(nameof(asmHookFactory));
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.captureFactory = captureFactory ?? throw new ArgumentNullException(nameof(captureFactory));
        stringReader = new MsvcStringReader(memory);

        RequiredHookIds = new ReadOnlyCollection<HookId>(
            FunctionHookIds.Concat(ProbeDefinitions.Select(definition => definition.Id)).ToArray());
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            CreateRegistration(HookId.ClassicTopMenuBuilder, PrepareClassicBuilder),
            CreateRegistration(HookId.TouchTopMenuBuilder, PrepareTouchBuilder),
            CreateRegistration(HookId.MenuTextLabelFactory, PrepareLabelFactory),
            CreateRegistration(HookId.StatusBarFormatScope, PrepareStatusFormat),
            CreateRegistration(HookId.StatusBarGlyphRenderer, PrepareStatusRenderer),
            CreateRegistration(HookId.StatusBarDestructor, PrepareStatusDestructor),
            CreateRegistration(HookId.ClassicTopMenuDeletingDestructor, PrepareClassicDestructor),
            CreateRegistration(HookId.TouchTopMenuDeletingDestructor, PrepareTouchDestructor),
            CreateRegistration(HookId.ClassicTopMenuActionDispatcher, PrepareClassicDispatcher),
            CreateRegistration(HookId.TouchTopMenuActionDispatcher, PrepareTouchDispatcher),
            .. ProbeDefinitions.Select(definition => CreateRegistration(
                definition.Id,
                (build, boundary) => PrepareProbe(definition, build, boundary))),
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
                throw new InvalidOperationException("Top-menu hooks were not fully prepared before activation.");
            }
            if (Volatile.Read(ref faulted) != 0)
            {
                throw new InvalidOperationException("Top-menu accessibility faulted before activation completed.");
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
            activeContext = null;
            actionTransaction = null;
            if (buildContext is { } build)
            {
                build.Cancelled = true;
            }
            buildContext = null;
        }
        pendingProbe.Value = null;
        Interlocked.Exchange(ref transitionActive, 0);
    }

    public void AfterCustomButtonConstructed(nint storage, nint returned)
    {
        try
        {
            if (!TryGetOwnedBuild(out var build))
            {
                return;
            }
            var control = (nuint)returned;
            if (storage == 0 || returned != storage || !TryReadExactVtable(control, CustomButtonVtableRva))
            {
                build.AddError(
                    $"Top-menu CustomButton did not return exact typed ECX storage (storage 0x{(nuint)storage:X}, returned 0x{control:X}).");
                return;
            }
            if (!build.AddConstructed(control))
            {
                build.AddError($"Top-menu CustomButton 0x{control:X} was constructed more than once.");
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared top-menu CustomButton observation failed: {FormatException(exception)}");
        }
    }

    public void AfterControlBound(nint manager, nint focusableState, int managerKey)
    {
        try
        {
            if (!TryGetOwnedBuild(out var build))
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
                build.AddError(
                    $"Top-menu binder key {managerKey} does not match the audited manager/FocusableState/CustomButton layout.");
                return;
            }
            if (!build.ContainsConstructed(control))
            {
                build.AddError("Top-menu binder referenced a control that was not constructed in this exact builder scope.");
                return;
            }
            if (!build.AddBinding(new Binding(managerPointer, control, managerKey)))
            {
                build.AddError("Top-menu binder supplied a duplicate manager key or control pointer.");
                return;
            }
            if (!build.Capture.TryRecordManagerKeyBinding(managerPointer, control, managerKey, out var diagnostic))
            {
                build.AddError(diagnostic);
            }
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared top-menu binder observation failed: {FormatException(exception)}");
        }
    }

    public void AfterFocusSet(nint manager, int managerKey)
    {
        try
        {
            if (!TryCaptureEpoch(out var currentEpoch))
            {
                return;
            }
            if (TryGetBuild(out var build))
            {
                if (build.OwnerThreadId != Environment.CurrentManagedThreadId)
                {
                    build.AddError("Top-menu focus changed on a different thread during its builder scope.");
                }
                else if (managerKey < 0 || !TryReadExactVtable((nuint)manager, ManagerVtableRva) ||
                    !TryReadInt32((nuint)manager + ManagerFocusKeyOffset, out var buildCommittedKey) ||
                    buildCommittedKey != managerKey)
                {
                    build.AddError(
                        "Top-menu builder focus does not match an exact manager vtable and committed native key.");
                }
                else
                {
                    build.AddFocus(new FocusObservation((nuint)manager, managerKey));
                }
                return;
            }
            if (Volatile.Read(ref transitionActive) != 0)
            {
                return;
            }

            ActiveContext? active;
            lock (gate)
            {
                active = hooksActive && epoch == currentEpoch ? activeContext : null;
            }
            if (active is null || active.Manager != (nuint)manager)
            {
                return;
            }
            if (!TryValidateActive(active, out var diagnostic) ||
                !TryReadInt32(active.Manager + ManagerFocusKeyOffset, out var committedKey) ||
                committedKey != managerKey || !active.Controls.TryGetValue(managerKey, out var control) ||
                !TryReadExactVtable(control.Pointer, CustomButtonVtableRva) ||
                !TryReadControlState(control.Pointer, out var enabled, out var visible) || !visible)
            {
                FailCoverage(string.IsNullOrWhiteSpace(diagnostic)
                    ? "Top-menu focus does not match its exact committed manager key and visible control."
                    : diagnostic);
                return;
            }
            PublishIfCurrent(currentEpoch, new MenuFocusChanged(ToFocus(control.Focus, enabled)));
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared top-menu focus observation failed: {FormatException(exception)}");
        }
    }

    public void BeforeTouchTopMenuDeletingDestructor(nint node, uint deletingFlags)
    {
        _ = node;
        _ = deletingFlags;
        // The root detour owns top-menu teardown. This observer intentionally
        // remains empty so Extras can run before the shared root original.
    }

    private IPreparedHook PrepareClassicBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ClassicTopMenuBuilderDelegate>(HookId.ClassicTopMenuBuilder, build, original =>
            (topMenu, rawStackWord) => boundary.Run(
                "Classic top-menu builder",
                () => HandleBuilder(TopMenuStyle.Classic, (nuint)topMenu, rawStackWord,
                    () => original()(topMenu, rawStackWord))));

    private IPreparedHook PrepareTouchBuilder(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TouchTopMenuBuilderDelegate>(HookId.TouchTopMenuBuilder, build, original =>
            (topMenu, rawStackWord) => boundary.Run(
                "Touch top-menu builder",
                () => HandleBuilder(TopMenuStyle.Touch, (nuint)topMenu, rawStackWord,
                    () => original()(topMenu, rawStackWord))));

    private IPreparedHook PrepareLabelFactory(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<MenuTextLabelFactoryDelegate>(HookId.MenuTextLabelFactory, build, original =>
            (position, text, anchor, fontSize) => boundary.Run(
                "menu UTF-8 text-label factory",
                () => HandleLabelFactory(original, position, text, anchor, fontSize),
                0));

    private IPreparedHook PrepareStatusFormat(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<StatusBarFormatScopeDelegate>(HookId.StatusBarFormatScope, build, original =>
            (statusBar, text, rawMode) => boundary.Run(
                "StatusBar formatting scope",
                () => HandleStatusFormat(original, statusBar, text, rawMode)));

    private IPreparedHook PrepareStatusRenderer(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<StatusBarGlyphRendererDelegate>(HookId.StatusBarGlyphRenderer, build, original =>
            (glyphOutput, text, outputArgument) => boundary.Run(
                "StatusBar glyph renderer",
                () => HandleStatusRenderer(original, glyphOutput, text, outputArgument),
                0));

    private IPreparedHook PrepareStatusDestructor(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<StatusBarDestructorDelegate>(HookId.StatusBarDestructor, build, original => statusBar =>
            boundary.Run("StatusBar destructor", () => HandleStatusDestructor(original, statusBar)));

    private IPreparedHook PrepareClassicDestructor(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ClassicTopMenuDeletingDestructorDelegate>(HookId.ClassicTopMenuDeletingDestructor, build, original =>
            (topMenu, deletingFlags) => boundary.Run(
                "Classic top-menu deleting destructor",
                () => HandleDeletingDestructor(
                    TopMenuStyle.Classic,
                    (nuint)topMenu,
                    () => original()(topMenu, deletingFlags)),
                topMenu));

    private IPreparedHook PrepareTouchDestructor(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TouchTopMenuDeletingDestructorDelegate>(HookId.TouchTopMenuDeletingDestructor, build, original =>
            (topMenu, deletingFlags) => boundary.Run(
                "Touch top-menu deleting destructor",
                () => HandleDeletingDestructor(
                    TopMenuStyle.Touch,
                    (nuint)topMenu,
                    () => original()(topMenu, deletingFlags)),
                topMenu));

    private IPreparedHook PrepareClassicDispatcher(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TopMenuActionDispatcherDelegate>(HookId.ClassicTopMenuActionDispatcher, build, original => context =>
            boundary.Run("Classic top-menu action dispatcher", () =>
                HandleActionDispatcher(TopMenuStyle.Classic, (nuint)context, () => original()(context))));

    private IPreparedHook PrepareTouchDispatcher(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
        PrepareHook<TopMenuActionDispatcherDelegate>(HookId.TouchTopMenuActionDispatcher, build, original => context =>
            boundary.Run("Touch top-menu action dispatcher", () =>
                HandleActionDispatcher(TopMenuStyle.Touch, (nuint)context, () => original()(context))));

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

    private void HandleBuilder(
        TopMenuStyle style,
        nuint root,
        uint rawStackWord,
        Action callOriginal)
    {
        _ = rawStackWord;
        if (!TryCaptureEpoch(out var capturedEpoch))
        {
            callOriginal();
            return;
        }

        BuildContext? build = null;
        string? setupError = null;
        try
        {
            lock (gate)
            {
                if (buildContext is not null)
                {
                    throw new InvalidOperationException("A nested top-menu builder scope was attempted.");
                }
            }
            if (!captureFactory.TryBegin(memory, imageBase, root, style, out var capture, out var diagnostic))
            {
                throw new InvalidOperationException(diagnostic);
            }
            build = new BuildContext(capturedEpoch, Environment.CurrentManagedThreadId, root, style, capture);
            lock (gate)
            {
                if (!hooksActive || epoch != capturedEpoch || Volatile.Read(ref faulted) != 0 || buildContext is not null)
                {
                    throw new InvalidOperationException("Top-menu lifecycle changed while the builder scope was starting.");
                }
                buildContext = build;
            }
        }
        catch (Exception exception)
        {
            setupError = FormatException(exception);
            build?.AddError($"Top-menu builder setup failed: {setupError}");
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

        string? captureFailure = null;
        try
        {
            if (build is null)
            {
                captureFailure = $"Top-menu builder observation could not start: {setupError}";
            }
            else
            {
                lock (gate)
                {
                    if (ReferenceEquals(buildContext, build))
                    {
                        buildContext = null;
                    }
                }
                if (pendingProbe.Value is { } stale && stale.Epoch == build.Epoch)
                {
                    build.AddError($"Top-menu call-site marker {stale.Id} was not consumed by its immediate target.");
                    pendingProbe.Value = null;
                }
                if (originalFailure is not null)
                {
                    build.AddError($"Native top-menu builder failed: {FormatException(originalFailure)}");
                }
                captureFailure = FinalizeBuild(build);
            }
        }
        catch (Exception exception)
        {
            captureFailure = $"Top-menu post-builder capture failed: {FormatException(exception)}";
        }
        finally
        {
            if (build is not null)
            {
                lock (gate)
                {
                    if (ReferenceEquals(buildContext, build))
                    {
                        buildContext = null;
                    }
                }
                try
                {
                    build.Dispose();
                }
                catch (Exception exception)
                {
                    captureFailure ??= $"Top-menu capture disposal failed: {FormatException(exception)}";
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(captureFailure))
        {
            FailCoverage(captureFailure);
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private nint HandleLabelFactory(
        Func<MenuTextLabelFactoryDelegate> original,
        nint position,
        nint text,
        nint anchor,
        int fontSize)
    {
        BuildContext? build = null;
        PendingProbe? marker = null;
        if (TryGetOwnedBuild(out var owned))
        {
            build = owned;
            marker = ConsumeProbe(owned, ProbeTarget.Label);
        }

        nint returned;
        try
        {
            returned = original()(position, text, anchor, fontSize);
        }
        catch (Exception exception)
        {
            build?.AddError($"Native top-menu text-label factory failed: {FormatException(exception)}");
            throw;
        }
        if (build is not null && marker is null)
        {
            RecordUnmarkedLabelDiagnostic(build, text, fontSize, returned);
        }
        if (build is null || marker is null || marker.Value.Target is not
            (ProbeTarget.Label or ProbeTarget.HiddenLabel or ProbeTarget.OptionalLabel or ProbeTarget.EmptyStatusLineLabel))
        {
            return returned;
        }
        try
        {
            if (returned == 0)
            {
                build.AddError("The top-menu text-label factory returned null for an audited visible label.");
                return returned;
            }
            // RVA 0x22EFA1 creates a label whose owner and label are immediately
            // hidden by unconditional setVisible(false) calls in StatusBar init.
            if (marker.Value.Target == ProbeTarget.HiddenLabel)
            {
                return returned;
            }
            if (marker.Value.Target == ProbeTarget.EmptyStatusLineLabel &&
                (build.Status is null || build.Status.Disposed))
            {
                build.AddError("An empty StatusBar line label was allocated outside its owned formatting scope.");
                return returned;
            }
            if (!stringReader.TryRead((nuint)text, out var value, out var diagnostic))
            {
                build.AddError($"An audited top-menu UTF-8 label is unreadable or blank: {diagnostic}");
                return returned;
            }
            if (marker.Value.Target == ProbeTarget.EmptyStatusLineLabel)
            {
                // RVA 0x22F2D7 allocates an empty label before the actual line is
                // formatted. Its text is captured separately at RVA 0x22F3B8.
                if (value.Length != 0)
                {
                    build.AddError("A newly allocated StatusBar line label must be empty before its rendered text is assigned.");
                }
                return returned;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                if (marker.Value.Target != ProbeTarget.OptionalLabel)
                {
                    build.AddError("An audited top-menu UTF-8 label is blank.");
                }
                return returned;
            }
            if (!build.Capture.TryRecordUtf8(imageBase + marker.Value.ReturnRva, value, out diagnostic))
            {
                build.AddError(diagnostic);
            }
        }
        catch (Exception exception)
        {
            build.AddError($"Top-menu UTF-8 capture failed after the native label call: {FormatException(exception)}");
        }
        return returned;
    }

    private void RecordUnmarkedLabelDiagnostic(BuildContext build, nint text, int fontSize, nint returned)
    {
        // Keep unknown text out of speech, but retain bounded evidence for the
        // next native audit instead of repeating an error with no identifying data.
        if (!build.TryTakeUnmarkedLabelDiagnostic()) return;
        try
        {
            var readable = stringReader.TryRead((nuint)text, out var value, out _);
            var preview = readable ? value[..Math.Min(value.Length, 160)] : "<unreadable>";
            dispatcher.RecordDiagnostic($"Top-menu unmarked label: style={build.Style}; " +
                $"root=0x{build.Root:X8}; textAddress=0x{(nuint)text:X8}; " +
                $"label=0x{(nuint)returned:X8}; fontSize={fontSize}; " +
                $"text={System.Text.Json.JsonSerializer.Serialize(preview)}");
        }
        catch (Exception) { /* Diagnostic failures cannot affect a completed native call. */ }
    }

    private void HandleStatusFormat(
        Func<StatusBarFormatScopeDelegate> original,
        nint statusBar,
        nint text,
        uint rawMode)
    {
        if (!TryGetOwnedBuild(out var build))
        {
            original()(statusBar, text, rawMode);
            return;
        }

        StatusContext? status = null;
        try
        {
            if (build.Status is not null)
            {
                build.AddError("A nested StatusBar formatting scope was observed in the top-menu builder.");
            }
            else if (build.CompletedStatusBar != 0)
            {
                build.AddError("More than one completed StatusBar formatting scope was observed in the top-menu builder.");
            }
            else if (!build.Capture.TryBeginStatusBar((nuint)statusBar, out var capture, out var diagnostic))
            {
                build.AddError(diagnostic);
            }
            else
            {
                status = new StatusContext((nuint)statusBar, capture);
                build.Status = status;
            }
        }
        catch (Exception exception)
        {
            build.AddError($"StatusBar capture setup failed before the native formatter: {FormatException(exception)}");
        }

        Exception? originalFailure = null;
        var completed = false;
        try
        {
            original()(statusBar, text, rawMode);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
        }
        finally
        {
            if (status is not null)
            {
                if (pendingProbe.Value is { } stale && stale.Epoch == build.Epoch)
                {
                    build.AddError($"StatusBar renderer marker {stale.Id} was not consumed by its immediate target.");
                    pendingProbe.Value = null;
                }
                try
                {
                    if (originalFailure is null && !status.Capture.TryComplete(out var diagnostic))
                    {
                        build.AddError(diagnostic);
                    }
                    else if (originalFailure is null)
                    {
                        completed = true;
                    }
                }
                catch (Exception exception)
                {
                    build.AddError($"StatusBar capture completion failed: {FormatException(exception)}");
                }
                try
                {
                    status.Capture.Dispose();
                }
                catch (Exception exception)
                {
                    build.AddError($"StatusBar capture disposal failed: {FormatException(exception)}");
                }
                status.Disposed = true;
                if (ReferenceEquals(build.Status, status))
                {
                    build.Status = null;
                }
                if (completed)
                {
                    build.CompletedStatusBar = status.Pointer;
                }
            }
        }
        if (originalFailure is not null)
        {
            build.AddError($"Native StatusBar formatter failed: {FormatException(originalFailure)}");
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
    }

    private nint HandleStatusRenderer(
        Func<StatusBarGlyphRendererDelegate> original,
        nint glyphOutput,
        nint text,
        nint outputArgument)
    {
        BuildContext? build = null;
        StatusContext? status = null;
        PendingProbe? marker = null;
        if (TryGetOwnedBuild(out var owned))
        {
            build = owned;
            status = owned.Status;
            marker = ConsumeProbe(owned, ProbeTarget.Renderer);
            if (status is null)
            {
                owned.AddError("StatusBar glyph renderer ran outside its owned formatting scope.");
            }
        }

        nint returned;
        try
        {
            returned = original()(glyphOutput, text, outputArgument);
        }
        catch (Exception exception)
        {
            build?.AddError($"Native StatusBar glyph renderer failed: {FormatException(exception)}");
            throw;
        }
        if (build is null || status is null || marker is null || marker.Value.Target != ProbeTarget.Renderer)
        {
            return returned;
        }
        try
        {
            if (!status.Capture.TryRecordRenderedLine(
                    imageBase + marker.Value.ReturnRva,
                    (nuint)text,
                    out var diagnostic))
            {
                build.AddError(diagnostic);
            }
        }
        catch (Exception exception)
        {
            build.AddError($"StatusBar line capture failed after the native renderer: {FormatException(exception)}");
        }
        return returned;
    }

    private void HandleStatusDestructor(Func<StatusBarDestructorDelegate> original, nint statusBar)
    {
        string? failure = null;
        MenuOwner? publishExit = null;
        try
        {
            if (TryGetBuild(out var build) && build.Status?.Pointer == (nuint)statusBar)
            {
                build.AddError("The owned StatusBar was destroyed during its top-menu capture scope.");
            }
            lock (gate)
            {
                if (hooksActive && activeContext is { } active && active.StatusBar == (nuint)statusBar)
                {
                    activeContext = null;
                    if (!TryReadExactVtable((nuint)statusBar, TopMenuCaptureScope.StatusBarVtableRva))
                    {
                        failure = "StatusBar destructor did not match the active exact StatusBar vtable.";
                    }
                    else if (!TryDeferExitLocked(active))
                    {
                        publishExit = OwnerOf(active);
                    }
                }
            }
            if (publishExit is not null)
            {
                dispatcher.Publish(new MenuExited(publishExit));
            }
        }
        catch (Exception exception)
        {
            failure = $"StatusBar teardown publication failed: {FormatException(exception)}";
        }
        Exception? originalFailure = null;
        try
        {
            original()(statusBar);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
            failure ??= $"Native StatusBar destructor failed: {FormatException(exception)}";
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

    private nint HandleDeletingDestructor(TopMenuStyle style, nuint root, Func<nint> callOriginal)
    {
        string? failure = null;
        MenuOwner? publishExit = null;
        try
        {
            lock (gate)
            {
                if (buildContext is { } build && build.Root == root)
                {
                    build.AddError("The top-menu root was destroyed during its builder capture scope.");
                }
                if (hooksActive && activeContext is { } active && active.Root == root)
                {
                    activeContext = null;
                    if (active.Style != style || !TryReadExactVtable(root, RootVtableRva(style)))
                    {
                        failure = "Top-menu deleting destructor did not match the active root style and exact vtable.";
                    }
                    else if (!TryDeferExitLocked(active))
                    {
                        publishExit = OwnerOf(active);
                    }
                }
            }
            if (publishExit is not null)
            {
                dispatcher.Publish(new MenuExited(publishExit));
            }
        }
        catch (Exception exception)
        {
            failure = $"Top-menu teardown publication failed: {FormatException(exception)}";
        }

        nint returned = (nint)root;
        Exception? originalFailure = null;
        try
        {
            returned = callOriginal();
        }
        catch (Exception exception)
        {
            originalFailure = exception;
            failure ??= $"Native top-menu deleting destructor failed: {FormatException(exception)}";
        }
        if (failure is not null)
        {
            FailCoverage(failure);
        }
        if (originalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }
        return returned;
    }

    private void HandleActionDispatcher(TopMenuStyle style, nuint context, Action callOriginal)
    {
        string? failure = null;
        ActionTransaction? transaction = null;
        var transitionOwner = Interlocked.CompareExchange(ref transitionActive, 1, 0) == 0;
        try
        {
            try
            {
                if (!transitionOwner)
                {
                    failure = "A reentrant top-menu action transition was attempted.";
                }
                else if (TryCaptureEpoch(out var capturedEpoch) &&
                    !TryBeginActionTransaction(
                        style,
                        context,
                        capturedEpoch,
                        out transaction,
                        out failure))
                {
                    transaction = null;
                }
            }
            catch (Exception exception)
            {
                failure = $"Top-menu action capture failed: {FormatException(exception)}";
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

            if (originalFailure is null && string.IsNullOrWhiteSpace(failure) && transaction is not null)
            {
                try
                {
                    PublishActionTransaction(transaction);
                }
                catch (Exception exception)
                {
                    failure = $"Top-menu action publication failed: {FormatException(exception)}";
                }
            }
            if (!string.IsNullOrWhiteSpace(failure))
            {
                FailCoverage(failure);
            }
            if (originalFailure is not null)
            {
                FailCoverage($"Native top-menu action dispatcher failed: {FormatException(originalFailure)}");
                ExceptionDispatchInfo.Capture(originalFailure).Throw();
            }
        }
        finally
        {
            CancelActionTransaction(transaction);
            if (transitionOwner)
            {
                Interlocked.Exchange(ref transitionActive, 0);
            }
        }
    }

    /// <summary>The submenu reader has already announced the child during native dispatch.</summary>
    public Func<bool>? SubmenuOwnsSpeech { get; set; }

    private bool TryBeginActionTransaction(
        TopMenuStyle style,
        nuint context,
        int capturedEpoch,
        out ActionTransaction? transaction,
        out string diagnostic)
    {
        transaction = null;
        diagnostic = string.Empty;
        ActiveContext? active;
        lock (gate)
        {
            active = hooksActive && epoch == capturedEpoch && Volatile.Read(ref faulted) == 0
                ? activeContext
                : null;
        }
        if (active is null)
        {
            diagnostic = string.Empty;
            return true;
        }
        if (active.Style != style || !TryValidateActive(active, out diagnostic) ||
            !TryReadInt32(context, out var action) || action is < 0 or > 7 ||
            !TryReadPointer(context + 4, out var parent) || parent == 0 ||
            !TryReadPointer(parent + (style == TopMenuStyle.Classic
                ? ClassicParentTopMenuOffset
                : TouchParentTopMenuOffset), out var parentRoot) || parentRoot != active.Root)
        {
            diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                ? "Top-menu dispatcher context does not match its exact action, parent, and active root layout."
                : diagnostic;
            return false;
        }
        MenuAccessibilityEvent? pendingEvent = null;
        if (action != 7)
        {
            if (!active.Controls.TryGetValue(action, out var control) ||
                !TryReadExactVtable(control.Pointer, CustomButtonVtableRva) ||
                !TryReadControlState(control.Pointer, out var enabled, out var visible) || !enabled || !visible)
            {
                diagnostic = $"Top-menu action {action} does not identify one currently enabled visible native-key control.";
                return false;
            }
            pendingEvent = action == 4
                ? new MenuActivated(control.Focus.Label)
                : new MenuUnsupported(
                    control.Focus.Label,
                    UnsupportedBoundary,
                    UnsupportedReturnInstruction);
        }
        var candidate = new ActionTransaction(
            capturedEpoch,
            Environment.CurrentManagedThreadId,
            style,
            context,
            active,
            pendingEvent);
        lock (gate)
        {
            if (!hooksActive || epoch != capturedEpoch || Volatile.Read(ref faulted) != 0 ||
                !ReferenceEquals(activeContext, active))
            {
                return true;
            }
            if (actionTransaction is not null)
            {
                diagnostic = "A second owned top-menu action transaction was already active.";
                return false;
            }
            actionTransaction = candidate;
            transaction = candidate;
        }
        diagnostic = string.Empty;
        return true;
    }

    private void PublishActionTransaction(ActionTransaction transaction)
    {
        lock (gate)
        {
            if (!IsCurrentActionTransactionLocked(transaction))
            {
                return;
            }
            try
            {
                if (transaction.PendingEvent is not null &&
                    !(transaction.PendingEvent is MenuUnsupported && SubmenuOwnsSpeech?.Invoke() == true))
                {
                    dispatcher.Publish(transaction.PendingEvent);
                }
                if (IsCurrentActionTransactionLocked(transaction) && transaction.ExitDeferred)
                {
                    dispatcher.Publish(new MenuExited(OwnerOf(transaction.Owner)));
                }
            }
            finally
            {
                if (ReferenceEquals(actionTransaction, transaction))
                {
                    actionTransaction = null;
                }
            }
        }
    }

    private bool IsCurrentActionTransactionLocked(ActionTransaction transaction) =>
        hooksActive && epoch == transaction.Epoch && Volatile.Read(ref faulted) == 0 &&
        transaction.OwnerThreadId == Environment.CurrentManagedThreadId &&
        ReferenceEquals(actionTransaction, transaction) &&
        (transaction.ExitDeferred
            ? activeContext is null
            : ReferenceEquals(activeContext, transaction.Owner));

    private bool TryDeferExitLocked(ActiveContext active)
    {
        if (actionTransaction is not { } transaction ||
            transaction.Epoch != epoch ||
            !ReferenceEquals(transaction.Owner, active))
        {
            return false;
        }
        transaction.ExitDeferred = true;
        return true;
    }

    private void CancelActionTransaction(ActionTransaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }
        lock (gate)
        {
            if (ReferenceEquals(actionTransaction, transaction))
            {
                actionTransaction = null;
            }
        }
    }

    private string? FinalizeBuild(BuildContext build)
    {
        if (build.OwnerThreadId != Environment.CurrentManagedThreadId)
        {
            return "Top-menu capture finalization ran on a different thread.";
        }
        if (build.Cancelled)
        {
            return null;
        }
        if (build.Constructed.Count != 7 || build.Bindings.Count != 7)
        {
            build.AddError(
                $"Top-menu builder requires exactly seven controls and seven bindings; observed {build.Constructed.Count} and {build.Bindings.Count}.");
        }
        for (var index = 0; index < build.Constructed.Count; index++)
        {
            var control = build.Constructed[index];
            if (!TryReadExactVtable(control, CustomButtonVtableRva) ||
                !TryReadControlState(control, out var enabled, out var visible))
            {
                build.AddError($"Top-menu control position {index} is not a readable exact CustomButton.");
                continue;
            }
            if (!build.Capture.TryRecordConstructedControl(control, index, enabled, visible, out var diagnostic))
            {
                build.AddError(diagnostic);
            }
        }
        if (build.Errors.Count > 0)
        {
            return string.Join(" ", build.Errors);
        }
        if (!build.Capture.TryCreateSnapshot(out var snapshot, out var captureDiagnostic))
        {
            return captureDiagnostic;
        }
        if (snapshot.Style != build.Style || snapshot.Controls.Count != 7 ||
            snapshot.Controls.Select(control => control.Key).Distinct().Count() != 7 ||
            snapshot.Controls.Count(control => control.Key == snapshot.FocusedKey) != 1 ||
            !snapshot.Controls.Select(control => control.Position).Order().SequenceEqual(Enumerable.Range(1, 7)) ||
            snapshot.Controls.Any(control => control.Count != 7 || string.IsNullOrWhiteSpace(control.Label) || !control.Visible))
        {
            return "Top-menu capture returned a style-mismatched or incomplete seven-control snapshot.";
        }
        var managers = build.Bindings.Select(binding => binding.Manager).Distinct().ToArray();
        if (managers.Length != 1)
        {
            return "Top-menu builder did not bind all controls to one exact manager.";
        }
        if (build.Focus.Any(focus => focus.Manager != managers[0]))
        {
            return "Top-menu builder focus observations did not belong to its exact bound manager.";
        }
        var bindingsByKey = build.Bindings.ToDictionary(binding => binding.Key);
        if (!snapshot.Controls.All(control => bindingsByKey.ContainsKey(control.Key)))
        {
            return "Top-menu snapshot keys do not match the exact observed manager bindings.";
        }
        if (build.CompletedStatusBar == 0 ||
            !TryReadExactVtable(build.CompletedStatusBar, TopMenuCaptureScope.StatusBarVtableRva))
        {
            return "Top-menu builder did not retain one completed exact StatusBar instance.";
        }
        var controls = snapshot.Controls.ToDictionary(
            control => control.Key,
            control => new RuntimeControl(bindingsByKey[control.Key].Control, ToFocus(control)));
        var active = new ActiveContext(build.Root, build.Style, build.CompletedStatusBar, managers[0], controls);
        var focused = controls[snapshot.FocusedKey].Focus;

        lock (gate)
        {
            if (!hooksActive || epoch != build.Epoch || Volatile.Read(ref faulted) != 0 || build.Cancelled)
            {
                return null;
            }
            activeContext = active;
            try
            {
                dispatcher.Publish(new MenuPresented(OwnerOf(active), "Menu", focused, snapshot.FlattenedStatus));
            }
            catch
            {
                activeContext = null;
                throw;
            }
        }
        return null;
    }

    private void ObserveProbe(ProbeDefinition definition)
    {
        try
        {
            if (!TryGetBuild(out var build))
            {
                pendingProbe.Value = null;
                return;
            }
            if (build.OwnerThreadId != Environment.CurrentManagedThreadId)
            {
                build.AddError($"Top-menu call-site marker {definition.Id} ran on a different thread.");
                return;
            }
            if (pendingProbe.Value is not null)
            {
                build.AddError("More than one top-menu call-site marker was pending before an immediate target call.");
                return;
            }
            if (!Includes(definition.Styles, build.Style))
            {
                build.AddError($"Call-site marker {definition.Id} is invalid for the active {build.Style} top-menu style.");
            }
            if (definition.Target == ProbeTarget.Rejected)
            {
                build.AddError(
                    "The unreachable touch reserve-name label branch executed inside the exact top-menu builder.");
            }
            var contract = GameVersionCatalog.Get(definition.Id);
            pendingProbe.Value = new PendingProbe(
                build.Epoch,
                definition.Id,
                checked(contract.Rva + (uint)contract.ExpectedBytes.Length),
                definition.Target);
        }
        catch (Exception exception)
        {
            FailCoverage($"Top-menu call-site observation failed: {FormatException(exception)}");
        }
    }

    private PendingProbe? ConsumeProbe(BuildContext build, ProbeTarget expectedTarget)
    {
        var marker = pendingProbe.Value;
        pendingProbe.Value = null;
        if (marker is null)
        {
            build.AddError($"An audited top-menu {expectedTarget} call arrived without its immediate call-site marker.");
            return null;
        }
        var compatibleLabel = expectedTarget == ProbeTarget.Label &&
            marker.Value.Target is ProbeTarget.HiddenLabel or ProbeTarget.OptionalLabel or ProbeTarget.EmptyStatusLineLabel;
        if (marker.Value.Epoch != build.Epoch || (marker.Value.Target != expectedTarget && !compatibleLabel))
        {
            build.AddError(
                $"Top-menu marker {marker.Value.Id} is stale or targets {marker.Value.Target} instead of {expectedTarget}.");
            return marker;
        }
        return marker;
    }

    private bool TryValidateActive(ActiveContext active, out string diagnostic)
    {
        if (!TryReadExactVtable(active.Root, RootVtableRva(active.Style)) ||
            !TryReadExactVtable(active.Manager, ManagerVtableRva))
        {
            diagnostic = "Active top-menu root or manager no longer has its exact audited vtable.";
            return false;
        }
        diagnostic = string.Empty;
        return true;
    }

    private bool TryReadControlState(nuint control, out bool enabled, out bool visible)
    {
        enabled = false;
        visible = false;
        return TryReadByte(control + WidgetEnabledOffset, out var enabledByte) && enabledByte is 0 or 1 &&
            TryReadByte(control + WidgetVisibleOffset, out var visibleByte) && visibleByte is 0 or 1 &&
            SetBooleans(enabledByte, visibleByte, out enabled, out visible);
    }

    private static bool SetBooleans(byte enabledByte, byte visibleByte, out bool enabled, out bool visible)
    {
        enabled = enabledByte != 0;
        visible = visibleByte != 0;
        return true;
    }

    private bool TryReadExactVtable(nuint instance, uint expectedRva) =>
        instance != 0 && TryReadPointer(instance, out var vtable) && vtable == imageBase + expectedRva;

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

    private bool TryGetOwnedBuild(out BuildContext build)
    {
        if (!TryGetBuild(out build))
        {
            return false;
        }
        if (build.OwnerThreadId != Environment.CurrentManagedThreadId)
        {
            build.AddError("Top-menu builder observation crossed native threads.");
            return false;
        }
        return true;
    }

    private bool TryGetBuild(out BuildContext build)
    {
        lock (gate)
        {
            if (hooksActive && Volatile.Read(ref faulted) == 0 && buildContext is { } current)
            {
                build = current;
                return true;
            }
        }
        build = null!;
        return false;
    }

    private bool TryCaptureEpoch(out int capturedEpoch)
    {
        lock (gate)
        {
            capturedEpoch = epoch;
            return hooksActive && Volatile.Read(ref faulted) == 0;
        }
    }

    private void PublishIfCurrent(int capturedEpoch, MenuFocusChanged accessibilityEvent)
    {
        lock (gate)
        {
            if (hooksActive && epoch == capturedEpoch && Volatile.Read(ref faulted) == 0 && activeContext is not null)
            {
                dispatcher.Publish(accessibilityEvent);
            }
        }
    }

    private void FailCoverage(string diagnostic)
    {
        lock (gate)
        {
            if (!hooksActive || Interlocked.Exchange(ref faulted, 1) != 0)
            {
                return;
            }
            activeContext = null;
            actionTransaction = null;
            if (buildContext is { } build)
            {
                build.Cancelled = true;
            }
            try
            {
                dispatcher.ReportCoverageFailure(string.IsNullOrWhiteSpace(diagnostic)
                    ? "Top-menu accessibility coverage failed without a diagnostic."
                    : diagnostic);
            }
            catch (Exception)
            {
                // Coverage reporting is best-effort at a shared unmanaged boundary.
            }
        }
        pendingProbe.Value = null;
    }

    private void InitializeBuild(IVerifiedGameBuild build, HookId id)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.ImageBaseAddress == 0 || (ulong)build.ImageBaseAddress > uint.MaxValue)
        {
            throw new InvalidOperationException("Verified x86 image base is unavailable for top-menu hooks.");
        }
        lock (gate)
        {
            if (imageBase != 0 && imageBase != build.ImageBaseAddress)
            {
                throw new InvalidOperationException("Top-menu hooks received conflicting verified image bases.");
            }
            imageBase = build.ImageBaseAddress;
            if (!preparedIds.Add(id))
            {
                throw new InvalidOperationException($"Top-menu hook '{id}' was prepared more than once.");
            }
        }
    }

    private static nuint RequireAddress(IVerifiedGameBuild build, HookId id)
    {
        if (!build.HookAddresses.TryGetValue(id, out var address) || address == 0 || (ulong)address > uint.MaxValue)
        {
            throw new InvalidOperationException($"Verified x86 address for required top-menu hook '{id}' is missing.");
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

    private static MenuFocus ToFocus(MenuControlSnapshot control) =>
        new(control.Label, control.Value, control.Position, control.Count, control.Help, !control.Enabled);

    private static MenuFocus ToFocus(MenuFocus focus, bool enabled) =>
        new(focus.Label, focus.Value, focus.Position, focus.Count, focus.Help, !enabled);

    private static uint RootVtableRva(TopMenuStyle style) => style == TopMenuStyle.Classic
        ? TopMenuCaptureScope.ClassicRootVtableRva
        : TopMenuCaptureScope.TouchRootVtableRva;

    private static ProbeDefinition Label(HookId id, StyleMask styles) =>
        new(id, ProbeTarget.Label, styles);

    private static bool Includes(StyleMask mask, TopMenuStyle style) =>
        (mask & (style == TopMenuStyle.Classic ? StyleMask.Classic : StyleMask.Touch)) != 0;

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

    private sealed class BuildContext(
        int epoch,
        int ownerThreadId,
        nuint root,
        TopMenuStyle style,
        ITopMenuCapture capture) : IDisposable
    {
        private readonly object gate = new();
        private readonly List<nuint> constructed = [];
        private readonly List<Binding> bindings = [];
        private readonly List<FocusObservation> focus = [];
        private readonly List<string> errors = [];
        private int unmarkedLabelDiagnostics;

        public int Epoch { get; } = epoch;
        public int OwnerThreadId { get; } = ownerThreadId;
        public nuint Root { get; } = root;
        public TopMenuStyle Style { get; } = style;
        public ITopMenuCapture Capture { get; } = capture;
        public StatusContext? Status { get; set; }
        public nuint CompletedStatusBar { get; set; }
        public bool Cancelled { get; set; }
        public IReadOnlyList<nuint> Constructed { get { lock (gate) return constructed.ToArray(); } }
        public IReadOnlyList<Binding> Bindings { get { lock (gate) return bindings.ToArray(); } }
        public IReadOnlyList<FocusObservation> Focus { get { lock (gate) return focus.ToArray(); } }
        public IReadOnlyList<string> Errors { get { lock (gate) return errors.ToArray(); } }
        public bool TryTakeUnmarkedLabelDiagnostic() => Interlocked.Increment(ref unmarkedLabelDiagnostics) <= 8;

        public bool AddConstructed(nuint control)
        {
            lock (gate)
            {
                if (constructed.Contains(control)) return false;
                constructed.Add(control);
                return true;
            }
        }

        public bool ContainsConstructed(nuint control) { lock (gate) return constructed.Contains(control); }

        public bool AddBinding(Binding binding)
        {
            lock (gate)
            {
                if (bindings.Any(existing => existing.Control == binding.Control || existing.Key == binding.Key))
                {
                    return false;
                }
                bindings.Add(binding);
                return true;
            }
        }

        public void AddFocus(FocusObservation observation) { lock (gate) focus.Add(observation); }

        public void AddError(string diagnostic)
        {
            lock (gate)
            {
                errors.Add(string.IsNullOrWhiteSpace(diagnostic)
                    ? "Top-menu capture failed without a diagnostic."
                    : diagnostic);
            }
        }

        public void Dispose() => Capture.Dispose();
    }

    private sealed class StatusContext(nuint pointer, ITopMenuStatusCapture capture)
    {
        public nuint Pointer { get; } = pointer;
        public ITopMenuStatusCapture Capture { get; } = capture;
        public bool Disposed { get; set; }
    }

    private static MenuOwner OwnerOf(ActiveContext active) => new("TopMenu", (ulong)active.Root);

    private sealed record ActiveContext(
        nuint Root,
        TopMenuStyle Style,
        nuint StatusBar,
        nuint Manager,
        IReadOnlyDictionary<int, RuntimeControl> Controls);

    private sealed class ActionTransaction(
        int epoch,
        int ownerThreadId,
        TopMenuStyle style,
        nuint context,
        ActiveContext owner,
        MenuAccessibilityEvent? pendingEvent)
    {
        public int Epoch { get; } = epoch;
        public int OwnerThreadId { get; } = ownerThreadId;
        public TopMenuStyle Style { get; } = style;
        public nuint Context { get; } = context;
        public ActiveContext Owner { get; } = owner;
        public MenuAccessibilityEvent? PendingEvent { get; } = pendingEvent;
        public bool ExitDeferred { get; set; }
    }

    private sealed record RuntimeControl(nuint Pointer, MenuFocus Focus);
    private sealed record Binding(nuint Manager, nuint Control, int Key);
    private sealed record FocusObservation(nuint Manager, int Key);
    private readonly record struct PendingProbe(int Epoch, HookId Id, uint ReturnRva, ProbeTarget Target);
    private readonly record struct ProbeDefinition(HookId Id, ProbeTarget Target, StyleMask Styles);

    private enum ProbeTarget
    {
        Label,
        HiddenLabel,
        OptionalLabel,
        EmptyStatusLineLabel,
        Renderer,
        Rejected,
    }

    [Flags]
    private enum StyleMask
    {
        Classic = 1,
        Touch = 2,
        Both = Classic | Touch,
    }
}
