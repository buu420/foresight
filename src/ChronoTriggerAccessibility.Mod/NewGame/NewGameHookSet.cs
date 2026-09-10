using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.NewGame;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.NewGame;

public sealed class NewGameHookSet : IHookActivationObserver, ISharedNativeHookObserver
{
    public const uint ControllerConfigurationRootRva = 0x41B4C4;
    public const uint ControllerArtworkSelectorOffset = 0x109C4;
    public const uint ManagerFocusKeyOffset = 0x2C4;
    public const uint ModeCallbackSceneOffset = 0x4;
    public const uint NameCallbackSceneOffset = 0x14;
    public const uint NameCallbackOwnerOffset = 0x0;
    public const uint NameCallbackDirectTargetOffset = 0x18;
    public const uint NameKeyboardStateOffset = 0x2A8;
    public const uint DirectEntryTargetStateOffset = 0x290;
    public const uint ControlNextClosureStateOffset = 0x8;
    public const uint FocusableStateVtableRva = 0x3AC3F4;
    public const uint FocusableStateControlOffset = 0x14;

    private static readonly uint[] ModeValueGetterRvas = [0x1EC320, 0x1EBEB0, 0x2AC370];
    private static readonly IReadOnlySet<LocalizedMessageKey> ModeTextKeys =
        new HashSet<LocalizedMessageKey>(
            ModeSelectCapture.TextContracts.SelectMany(contract =>
                new[] { contract.Label }.Concat(contract.Values).Concat(contract.Help))
            .Append(ModeSelectCapture.StartTextKey));
    private static readonly IReadOnlySet<LocalizedMessageKey> NameTextKeys =
        new HashSet<LocalizedMessageKey>(
        [
            NameInputCapture.GridActionTextKey,
            NameInputCapture.DefaultsTextKey,
            NameInputCapture.AcceptTextKey,
        ]);

    [ThreadStatic]
    private static CaptureScope? threadCaptureScope;

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IRuntimeNativeFunctionWrapperFactory wrapperFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly MsvcStringReader stringReader;
    private readonly object lifecycleGate = new();
    private readonly object stateGate = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private ModeValueGetterDelegate[] modeValueGetters = [];
    private bool hooksActive;
    private int activeEpoch;
    private int faulted;
    private string? activeControlNextLabel;
    private nuint activeControlManager;
    private nuint activeModeScene;
    private ModeSelectSnapshot? activeModeSnapshot;
    private nuint activeNameScene;
    private NameInputSnapshot? activeNameSnapshot;
    private string? activeNameGridActionLabel;
    private IReadOnlyList<NameActionSnapshot> activeNameActions = Array.Empty<NameActionSnapshot>();
    private nuint activeNameManager;
    private NameConfirmationSnapshot? activeConfirmation;
    private nuint activeConfirmationManager;
    private nuint pendingDirectEntryScene;
    private nuint pendingDirectEntryOwner;
    private nuint pendingDirectEntryTarget;
    private bool pendingDirectEntryGridClosed;
    private nuint activeDirectEntryOwner;
    private nuint activeDirectEntryTarget;

    public NewGameHookSet(
        IRuntimeNativeHookFactory hookFactory,
        IRuntimeNativeFunctionWrapperFactory wrapperFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher)
    {
        this.hookFactory = hookFactory ?? throw new ArgumentNullException(nameof(hookFactory));
        this.wrapperFactory = wrapperFactory ?? throw new ArgumentNullException(nameof(wrapperFactory));
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        stringReader = new MsvcStringReader(memory);

        RequiredHookIds = new ReadOnlyCollection<HookId>(
        [
            HookId.OpeTextResolver,
            HookId.OpeManualSceneInit,
            HookId.ModeSelectSteamInit,
            HookId.ModeSelectCallback,
            HookId.NameInputSceneInit,
            HookId.NameInputSceneUpdate,
            HookId.NameConfirmationBuilder,
            HookId.NsMenuCustomButtonConstructor,
            HookId.NsMenuControlBinder,
            HookId.ControlNextCallback,
            HookId.NameActionCallback,
            HookId.NameDirectEntryActivation,
            HookId.NameDirectEntryClose,
            HookId.NameGridRefresh,
        ]);
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            CreateRegistration(HookId.OpeTextResolver, PrepareOpeTextResolver),
            CreateRegistration(HookId.OpeManualSceneInit, PrepareControlInit),
            CreateRegistration(HookId.ModeSelectSteamInit, PrepareModeInit),
            CreateRegistration(HookId.ModeSelectCallback, PrepareModeCallback),
            CreateRegistration(HookId.NameInputSceneInit, PrepareNameInit),
            CreateRegistration(HookId.NameInputSceneUpdate, PrepareNameUpdate),
            CreateRegistration(HookId.NameConfirmationBuilder, PrepareConfirmationBuilder),
            CreateRegistration(HookId.NsMenuCustomButtonConstructor, PrepareCustomButtonConstructor),
            CreateRegistration(HookId.NsMenuControlBinder, PrepareControlBinder),
            CreateRegistration(HookId.ControlNextCallback, PrepareControlNext),
            CreateRegistration(HookId.NameActionCallback, PrepareNameAction),
            CreateRegistration(HookId.NameDirectEntryActivation, PrepareDirectEntryActivation),
            CreateRegistration(HookId.NameDirectEntryClose, PrepareDirectEntryClose),
            CreateRegistration(HookId.NameGridRefresh, PrepareNameGridRefresh),
        ]);
    }

    public IReadOnlyList<HookId> RequiredHookIds { get; }
    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    public void AfterHooksActivated()
    {
        lock (lifecycleGate)
        {
            if (imageBase == 0 || modeValueGetters.Length != ModeSelectCapture.RowCount)
            {
                throw new InvalidOperationException("New Game hooks were not fully prepared before activation.");
            }
            if (Volatile.Read(ref faulted) != 0)
            {
                throw new InvalidOperationException("New Game hooks faulted before activation completed.");
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
            ClearRuntimeState();
            if (ReferenceEquals(threadCaptureScope?.Owner, this))
            {
                threadCaptureScope = null;
            }
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
            if (!TryCaptureActiveEpoch(out _) || GetOwnedThreadScope() is not { } scope)
            {
                return;
            }
            ObserveLocalizedText(scope, fileId, messageId, result, returned);
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared localized-text observation failed: {FormatException(exception)}");
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
            if (GetOwnedThreadScope() is { } scope)
            {
                if (!TryReadInt32((nuint)manager + ManagerFocusKeyOffset, out var authoritativeKey))
                {
                    scope.Errors.Add(
                        $"Post-focus manager 0x{(nuint)manager:X} authoritative key is unreadable.");
                }
                else if (authoritativeKey != managerKey)
                {
                    scope.Errors.Add(
                        $"Post-focus manager requested key {managerKey} but authoritative key is {authoritativeKey}.");
                }
                else
                {
                    scope.FocusObservations.Add(((nuint)manager, authoritativeKey));
                }
                return;
            }

            if (!IsTrackedRuntimeManager((nuint)manager))
            {
                return;
            }
            if (!TryReadInt32((nuint)manager + ManagerFocusKeyOffset, out var runtimeKey) ||
                runtimeKey != managerKey)
            {
                FailCoverage(
                    $"Post-focus manager requested key {managerKey}, but its authoritative +0x2C4 state is unreadable or different.");
                return;
            }

            RunIfActive(epoch, () => PublishRuntimeManagerFocus((nuint)manager, runtimeKey));
        }
        catch (Exception exception)
        {
            FailCoverage($"Shared focus observation failed: {FormatException(exception)}");
        }
    }

    public void AfterMenuTextLabelFactory(nint position, nint text, nint anchor, int fontSize, nint returned)
    {
        _ = position;
        _ = anchor;
        if (!TryCaptureActiveEpoch(out _) ||
            GetOwnedThreadScope() is not { Kind: CaptureKind.NameConfirmation } scope)
        {
            return;
        }
        // The name-character labels precede both CustomButtons. Each button then
        // receives exactly one rendered label at RVA 0x2C36F5 before the next ctor.
        if (scope.ConstructedControls.Count == 0)
        {
            return;
        }
        try
        {
            var control = scope.PendingConfirmationControl;
            if (control == 0)
            {
                scope.Errors.Add("Confirmation rendered another label without a pending choice control.");
                return;
            }
            if (returned == 0 || fontSize != 12)
            {
                scope.Errors.Add("Confirmation choice label factory did not return the audited non-null font-12 label.");
                return;
            }
            if (!stringReader.TryRead((nuint)text, out var label, out var error) ||
                string.IsNullOrWhiteSpace(label))
            {
                scope.Errors.Add($"Confirmation rendered choice text is unreadable or blank: {error}");
                return;
            }
            if (scope.ConfirmationLocalizedLabels.TryGetValue(control, out var localized) &&
                !StringComparer.Ordinal.Equals(localized, label))
            {
                scope.Errors.Add("Confirmation rendered choice text differs from its observed localized text.");
                return;
            }
            if (!scope.ConfirmationControlLabels.TryAdd(control, new string(label.AsSpan())))
            {
                scope.Errors.Add("Confirmation choice control received more than one rendered label.");
                return;
            }
            scope.PendingConfirmationControl = 0;
        }
        catch (Exception exception)
        {
            scope.Errors.Add($"Confirmation rendered-label capture failed: {FormatException(exception)}");
        }
    }

    private IPreparedHook PrepareOpeTextResolver(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<OpeTextResolverDelegate>(
            HookId.OpeTextResolver,
            build,
            original => (resolver, result, bank, messageId) => boundary.Run(
                "Ope localized text resolver",
                () =>
                {
                    var returned = original()(resolver, result, bank, messageId);
                    if (!SharedNativeHookFanoutFactory.IsTextManagerGetMsgActive &&
                        TryCaptureActiveEpoch(out _) && GetOwnedThreadScope() is { } scope)
                    {
                        try
                        {
                            ObserveLocalizedText(scope, bank, messageId, result, returned);
                        }
                        catch (Exception exception)
                        {
                            scope.Errors.Add(
                                $"Localized resolver observation failed: {FormatException(exception)}");
                        }
                    }
                    return returned;
                },
                result));

    private IPreparedHook PrepareControlInit(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<OpeManualSceneInitDelegate>(
            HookId.OpeManualSceneInit,
            build,
            original => scene => boundary.Run(
                "OpeManualScene::init",
                () =>
                {
                    if (!TryCaptureActiveEpoch(out var epoch))
                    {
                        return original()(scene);
                    }

                    CaptureScope? scope = null;
                    string? scopeError = null;
                    try
                    {
                        scope = BeginScope(CaptureKind.ControlDescriptions);
                    }
                    catch (Exception exception)
                    {
                        scopeError = $"Control Descriptions capture scope could not start: {FormatException(exception)}";
                    }
                    byte result;
                    try
                    {
                        result = original()(scene);
                    }
                    finally
                    {
                        if (scope is not null)
                        {
                            EndScope(scope);
                        }
                    }

                    if (result != 0)
                    {
                        RunInstrumentationSafely(epoch, "Control Descriptions post-init capture failed", () =>
                        {
                            if (scope is null)
                            {
                                FailCoverage(scopeError ?? "Control Descriptions capture scope is unavailable.");
                            }
                            else
                            {
                                FinalizeControlDescriptions(scope);
                            }
                        });
                    }
                    return result;
                },
                (byte)0));

    private IPreparedHook PrepareModeInit(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ModeSelectSteamInitDelegate>(
            HookId.ModeSelectSteamInit,
            build,
            original => scene => boundary.Run(
                "ModeSelectSteam::init",
                () =>
                {
                    if (!TryCaptureActiveEpoch(out var epoch))
                    {
                        return original()(scene);
                    }

                    CaptureScope? scope = null;
                    string? scopeError = null;
                    try
                    {
                        scope = BeginScope(CaptureKind.ModeSelect);
                    }
                    catch (Exception exception)
                    {
                        scopeError = $"Mode Select capture scope could not start: {FormatException(exception)}";
                    }
                    byte result;
                    try
                    {
                        result = original()(scene);
                    }
                    finally
                    {
                        if (scope is not null)
                        {
                            EndScope(scope);
                        }
                    }

                    if (result != 0)
                    {
                        RunInstrumentationSafely(epoch, "Mode Select post-init capture failed", () =>
                        {
                            if (scope is null)
                            {
                                FailCoverage(scopeError ?? "Mode Select capture scope is unavailable.");
                            }
                            else
                            {
                                FinalizeModeSelect((nuint)scene, scope);
                            }
                        });
                    }
                    return result;
                },
                (byte)0));

    private IPreparedHook PrepareModeCallback(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ModeSelectCallbackDelegate>(
            HookId.ModeSelectCallback,
            build,
            original => (closure, eventType, value) => boundary.Run(
                "ModeSelect callback",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch) && HasActiveMode();
                    string? error = null;
                    nuint scene = 0;
                    nuint manager = 0;
                    string? startLabel = null;
                    var cancel = false;
                    var recapture = false;
                    var commonTailOnly = (uint)eventType > 3;

                    if (instrument && commonTailOnly)
                    {
                        scene = GetActiveModeScene();
                        recapture = scene != 0;
                    }
                    else if (instrument)
                    {
                        try
                        {
                            if (!TryReadPointer((nuint)closure, out manager) || manager == 0 ||
                                !TryReadPointer((nuint)closure + ModeCallbackSceneOffset, out scene) ||
                                scene == 0 || scene != GetActiveModeScene())
                            {
                                error = "Mode Select callback closure manager/scene is unreadable or does not match the active screen.";
                            }
                            else
                            {
                                switch (eventType)
                                {
                                    case 0:
                                        if (!TryReadInt32(manager + ManagerFocusKeyOffset, out var managerKey))
                                        {
                                            error = "Mode Select activation manager focus is unreadable.";
                                        }
                                        else if (value == 30)
                                        {
                                            if (managerKey != value || value / 10 != ModeSelectCapture.RowCount)
                                            {
                                                error = $"Mode Select Start activation key {value} does not match manager focus {managerKey}.";
                                            }
                                            else
                                            {
                                                startLabel = GetActiveModeSnapshot()?.StartLabel;
                                                if (string.IsNullOrWhiteSpace(startLabel))
                                                {
                                                    error = "Mode Select Start activation has no cached localized label.";
                                                }
                                            }
                                        }
                                        else
                                        {
                                            recapture = true;
                                        }
                                        break;
                                    case 1:
                                        recapture = true;
                                        break;
                                    case 2:
                                        cancel = true;
                                        break;
                                    case 3:
                                        recapture = true;
                                        break;
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            error = $"Mode Select callback capture failed: {FormatException(exception)}";
                        }
                    }

                    var publishBeforeOriginal = error is not null || startLabel is not null || cancel;
                    if (instrument && publishBeforeOriginal)
                    {
                        try
                        {
                            RunIfActive(epoch, () =>
                            {
                                if (error is not null)
                                {
                                    FailCoverage(error);
                                }
                                else if (startLabel is not null)
                                {
                                    dispatcher.Publish(new ModeSelectActivated(
                                        new string(startLabel.AsSpan())));
                                    lock (stateGate)
                                    {
                                        activeModeScene = 0;
                                        activeModeSnapshot = null;
                                    }
                                }
                                else if (cancel)
                                {
                                    dispatcher.Publish(new ModeSelectCancelled());
                                    lock (stateGate)
                                    {
                                        activeModeScene = 0;
                                        activeModeSnapshot = null;
                                    }
                                }
                            });
                        }
                        catch (Exception exception)
                        {
                            FailCoverage($"Mode Select transition publication failed: {FormatException(exception)}");
                        }
                    }

                    original()(closure, eventType, value);

                    if (!instrument || publishBeforeOriginal)
                    {
                        return;
                    }
                    if (recapture)
                    {
                        RunInstrumentationSafely(
                            epoch, "Mode Select post-callback capture failed",
                            () => PublishModeChange(scene));
                    }
                }));

    private IPreparedHook PrepareNameInit(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NameInputSceneInitDelegate>(
            HookId.NameInputSceneInit,
            build,
            original => scene => boundary.Run(
                "NameInputScene::init",
                () =>
                {
                    if (!TryCaptureActiveEpoch(out var epoch))
                    {
                        return original()(scene);
                    }

                    CaptureScope? scope = null;
                    string? scopeError = null;
                    try
                    {
                        scope = BeginScope(CaptureKind.NameEntry);
                        scope.NameScene = (nuint)scene;
                    }
                    catch (Exception exception)
                    {
                        scopeError = $"Name Entry capture scope could not start: {FormatException(exception)}";
                    }
                    byte result;
                    try
                    {
                        result = original()(scene);
                    }
                    finally
                    {
                        if (scope is not null)
                        {
                            EndScope(scope);
                        }
                    }

                    if (result != 0)
                    {
                        RunInstrumentationSafely(epoch, "Name Entry post-init capture failed", () =>
                        {
                            if (scope is null)
                            {
                                FailCoverage(scopeError ?? "Name Entry capture scope is unavailable.");
                            }
                            else
                            {
                                FinalizeNameEntry((nuint)scene, scope);
                            }
                        });
                    }
                    return result;
                },
                (byte)0));

    private IPreparedHook PrepareNameUpdate(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NameInputSceneUpdateDelegate>(
            HookId.NameInputSceneUpdate,
            build,
            original => (scene, deltaSeconds) => boundary.Run(
                "NameInputScene::update",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch) &&
                        (nuint)scene == GetActiveNameScene();
                    original()(scene, deltaSeconds);
                    if (instrument)
                    {
                        RunInstrumentationSafely(
                            epoch, "Name Entry post-update capture failed",
                            () => CaptureAndPublishNameState((nuint)scene));
                    }
                }));

    private IPreparedHook PrepareNameGridRefresh(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NameGridRefreshDelegate>(
            HookId.NameGridRefresh,
            build,
            original => closure => boundary.Run(
                "Name character-grid refresh",
                () =>
                {
                    var parent = GetOwnedThreadScope();
                    var instrument = TryCaptureActiveEpoch(out var epoch) &&
                        (HasActiveName() || parent?.Kind == CaptureKind.NameEntry);
                    CaptureScope? scope = null;
                    string? error = null;
                    nuint scene = 0;
                    if (instrument)
                    {
                        try
                        {
                            // _Do_call adds four to the implementation object before
                            // entering this body; the body closure is { grid, scene }.
                            if (!TryReadPointer((nuint)closure + 4, out scene) || scene == 0)
                            {
                                error = "Name grid refresh scene closure is unreadable.";
                            }
                            else if (scene != (parent?.Kind == CaptureKind.NameEntry
                                ? parent.NameScene : GetActiveNameScene()))
                            {
                                instrument = false;
                            }
                            else if (!TryReadPointer(scene + NameInputCapture.RefreshTargetOffset, out var target) ||
                                target == 0 || target + 4 != (nuint)closure ||
                                !TryReadPointer(target, out var vtable) || vtable == 0 ||
                                !TryReadPointer(vtable + 8, out var invoke) ||
                                invoke != imageBase + NameInputCapture.RefreshInvokeRva)
                            {
                                error = "Name grid refresh does not match its scene's audited std::function target.";
                            }
                            else if (threadCaptureScope is not null &&
                                (parent?.Kind != CaptureKind.NameEntry || parent.NameScene != scene))
                            {
                                error = "Name grid refresh entered an unrelated or nested refresh capture scope.";
                            }
                            else
                            {
                                // Only this audited child may nest inside Name init.
                                // Text ownership returns to the constructor afterward.
                                scope = new CaptureScope(this, CaptureKind.NameGridRefresh)
                                {
                                    NameScene = scene,
                                    Parent = parent,
                                };
                                threadCaptureScope = scope;
                            }
                        }
                        catch (Exception exception)
                        {
                            error = $"Name grid refresh capture failed: {FormatException(exception)}";
                        }
                    }

                    try
                    {
                        original()(closure);
                    }
                    finally
                    {
                        if (scope is not null)
                        {
                            EndScope(scope);
                        }
                    }

                    if (instrument)
                    {
                        RunInstrumentationSafely(epoch, "Name grid refresh post-capture failed", () =>
                        {
                            if (error is not null || scope is null)
                            {
                                FailCoverage(error ?? "Name grid refresh capture scope is unavailable.");
                                return;
                            }
                            if (scope.Errors.Count != 0 ||
                                !TryGetScopeText(scope, NameInputCapture.GridActionTextKey, out var label))
                            {
                                FailCoverage(FirstScopeErrorOr(scope,
                                    "Name grid refresh did not capture its localized Accept label."));
                                return;
                            }
                            if (parent is not null)
                            {
                                StoreScopeText(parent, NameInputCapture.GridActionTextKey, label);
                            }
                            else
                            {
                                lock (stateGate)
                                {
                                    if (activeNameScene == scene)
                                    {
                                        activeNameGridActionLabel = new string(label.AsSpan());
                                    }
                                }
                            }
                        });
                    }
                }));

    private IPreparedHook PrepareConfirmationBuilder(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NameConfirmationBuilderDelegate>(
            HookId.NameConfirmationBuilder,
            build,
            original => (scene, word0, word1, word2, word3, length, capacity) => boundary.Run(
                "Name confirmation builder",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch) &&
                        (nuint)scene == GetActiveNameScene();
                    string? decodedName = null;
                    string? decodeError = null;
                    CaptureScope? scope = null;
                    if (instrument)
                    {
                        try
                        {
                            if (!NameInputCapture.TryDecodeByValueName(
                                    memory, word0, word1, word2, word3, length, capacity,
                                    out decodedName, out decodeError))
                            {
                                decodedName = null;
                            }
                        }
                        catch (Exception exception)
                        {
                            decodeError = $"Confirmation by-value name capture failed: {FormatException(exception)}";
                        }
                        try
                        {
                            scope = BeginScope(CaptureKind.NameConfirmation);
                        }
                        catch (Exception exception)
                        {
                            decodeError = $"Name confirmation capture scope could not start: {FormatException(exception)}";
                        }
                    }

                    try
                    {
                        original()(scene, word0, word1, word2, word3, length, capacity);
                    }
                    finally
                    {
                        if (scope is not null)
                        {
                            EndScope(scope);
                        }
                    }

                    if (instrument)
                    {
                        RunInstrumentationSafely(epoch, "Name confirmation post-build capture failed", () =>
                        {
                            if (scope is null || decodedName is null)
                            {
                                FailCoverage($"Name confirmation cannot decode its proposed name: {decodeError}");
                            }
                            else
                            {
                                FinalizeConfirmation(scope, decodedName);
                            }
                        });
                    }
                }));

    private IPreparedHook PrepareCustomButtonConstructor(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NsMenuCustomButtonConstructorDelegate>(
            HookId.NsMenuCustomButtonConstructor,
            build,
            original => storage => boundary.Run(
                "nsMenu CustomButton constructor",
                () =>
                {
                    var returned = original()(storage);
                    if (TryCaptureActiveEpoch(out _) &&
                        GetOwnedThreadScope() is { Kind: CaptureKind.NameConfirmation } scope)
                    {
                        try
                        {
                            var control = (nuint)returned;
                            if (storage == 0 || returned != storage)
                            {
                                scope.Errors.Add(
                                    $"Confirmation CustomButton constructor must return its exact non-null ECX storage (storage 0x{(nuint)storage:X}, returned 0x{control:X}).");
                            }
                            else if (scope.PendingConfirmationControl != 0)
                            {
                                scope.Errors.Add(
                                    "Confirmation constructed another choice control before rendering the preceding control's label. " +
                                    $"Pending=0x{scope.PendingConfirmationControl:X}, new=0x{control:X}, " +
                                    $"constructed={scope.ConstructedControls.Count}, rendered={scope.ConfirmationControlLabels.Count}, " +
                                    $"text keys=[{string.Join(",", scope.Text.Keys.Select(key => $"{key.Bank:X}/{key.MessageId:X}"))}].");
                            }
                            else
                            {
                                scope.ConstructedControls.Add(control);
                                scope.PendingConfirmationControl = control;
                            }
                        }
                        catch (Exception exception)
                        {
                            scope.Errors.Add($"Confirmation control construction capture failed: {FormatException(exception)}");
                        }
                    }
                    return returned;
                },
                storage));

    private IPreparedHook PrepareControlBinder(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NsMenuControlBinderDelegate>(
            HookId.NsMenuControlBinder,
            build,
            original => (manager, focusableState, managerKey) => boundary.Run(
                "nsMenu control binder",
                () =>
                {
                    original()(manager, focusableState, managerKey);
                    if (TryCaptureActiveEpoch(out _) && GetOwnedThreadScope() is { } scope &&
                        scope.Kind is CaptureKind.NameEntry or CaptureKind.NameConfirmation)
                    {
                        try
                        {
                            var state = (nuint)focusableState;
                            if (state == 0 ||
                                !TryReadPointer(state, out var vtable) ||
                                vtable != imageBase + FocusableStateVtableRva ||
                                !TryReadPointer(state + FocusableStateControlOffset, out var control) ||
                                control == 0)
                            {
                                scope.Errors.Add(
                                    $"Menu control binder state 0x{state:X} does not match the exact FocusableState layout or has no control at +0x14.");
                            }
                            else
                            {
                                scope.Bindings.Add(((nuint)manager, control, managerKey));
                            }
                        }
                        catch (Exception exception)
                        {
                            scope.Errors.Add($"Menu control correlation failed: {FormatException(exception)}");
                        }
                    }
                }));

    private IPreparedHook PrepareControlNext(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<ControlNextCallbackDelegate>(
            HookId.ControlNextCallback,
            build,
            original => (closure, eventTypePointer, valuePointer) => boundary.Run(
                "Control Next callback",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch) && HasActiveControl();
                    string? error = null;
                    var transition = false;
                    string? label = null;
                    if (instrument)
                    {
                        try
                        {
                            if (!TryReadInt32((nuint)eventTypePointer, out var eventType))
                            {
                                error = "Control Descriptions Next callback event pointer is unreadable.";
                            }
                            else
                            {
                                if (eventType is 0 or 2)
                                {
                                    if (!TryReadInt32(
                                            (nuint)closure + ControlNextClosureStateOffset,
                                            out var closureState))
                                    {
                                        error = "Control Descriptions Next callback closure state at +0x8 is unreadable.";
                                    }
                                    else
                                    {
                                        transition = closureState is 0 or 1;
                                    }
                                }
                                if (transition)
                                {
                                    label = GetActiveControlNextLabel();
                                    if (string.IsNullOrWhiteSpace(label))
                                    {
                                        error = "Control Descriptions Next callback has no cached localized label.";
                                    }
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            error = $"Control Descriptions Next capture failed: {FormatException(exception)}";
                        }
                    }

                    if (instrument)
                    {
                        try
                        {
                            RunIfActive(epoch, () =>
                            {
                                if (error is not null)
                                {
                                    FailCoverage(error);
                                }
                                else if (transition && label is not null)
                                {
                                    dispatcher.Publish(new ControlDescriptionNextActivated(
                                        new string(label.AsSpan())));
                                    lock (stateGate)
                                    {
                                        activeControlNextLabel = null;
                                        activeControlManager = 0;
                                    }
                                }
                            });
                        }
                        catch (Exception exception)
                        {
                            FailCoverage(
                                $"Control Descriptions Next publication failed: {FormatException(exception)}");
                        }
                    }

                    original()(closure, eventTypePointer, valuePointer);
                }));

    private IPreparedHook PrepareNameAction(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NameActionCallbackDelegate>(
            HookId.NameActionCallback,
            build,
            original => (closure, eventType, actionId) => boundary.Run(
                "Name action callback",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch) && HasActiveName();
                    string? error = null;
                    string? activatedLabel = null;
                    nuint scene = 0;
                    nuint directOwner = 0;
                    nuint directTarget = 0;
                    var emptyAccept = false;
                    var nativeSilent = (uint)eventType > 1 ||
                        (eventType == 0 && (uint)actionId > 3);
                    if (instrument && !nativeSilent && eventType != 1)
                    {
                        try
                        {
                            if (!TryReadPointer((nuint)closure + NameCallbackSceneOffset, out scene) ||
                                scene == 0 || scene != GetActiveNameScene())
                            {
                                error = "Name action callback scene closure is unreadable or does not match the active Name Entry screen.";
                            }
                            else if (eventType == 0 && actionId == 2)
                            {
                                if (!TryReadPointer((nuint)closure + NameCallbackOwnerOffset, out directOwner) ||
                                    directOwner == 0 ||
                                    !TryReadPointer((nuint)closure + NameCallbackDirectTargetOffset, out directTarget) ||
                                    directTarget == 0 ||
                                    !TryReadByte(directOwner + NameKeyboardStateOffset, out var keyboardState) ||
                                    keyboardState != 0 ||
                                    directTarget != GetActiveNameManager() ||
                                    !HasAuthoritativeNameActionFocus(2))
                                {
                                    error = "Keyboard name-entry activation closure, target, inactive IME state, or key-2 focus is unavailable.";
                                }
                                else
                                {
                                    lock (stateGate)
                                    {
                                        if (pendingDirectEntryOwner != 0 || activeDirectEntryOwner != 0)
                                        {
                                            error = "Keyboard name-entry activation overlapped an existing pending or active IME session.";
                                        }
                                    }
                                }
                            }
                            else if (eventType == 0 && actionId == 0 &&
                                !TryGetNameActionLabel(0, out activatedLabel))
                            {
                                error = "Name Defaults activation has no runtime-correlated localized label.";
                            }
                            else if (eventType == 0 && actionId == 1)
                            {
                                var actionLabel = GetActiveNameGridActionLabel();
                                if (!NameInputCapture.TryCreateSnapshot(
                                        memory, imageBase, scene, actionLabel,
                                        out var before, out var snapshotError))
                                {
                                    error = $"Name Accept cannot capture the proposed name: {snapshotError}";
                                }
                                else
                                {
                                    emptyAccept = before.Name.Length == 0;
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            error = $"Name action capture failed: {FormatException(exception)}";
                        }
                    }

                    original()(closure, eventType, actionId);

                    if (!instrument || nativeSilent || eventType == 1)
                    {
                        return;
                    }
                    RunInstrumentationSafely(epoch, "Name action post-callback capture failed", () =>
                    {
                        if (error is not null)
                        {
                            FailCoverage(error);
                            return;
                        }
                        if (eventType == 0 && actionId == 1 && emptyAccept)
                        {
                            dispatcher.Publish(new EmptyNameRejected());
                        }
                        else if (eventType == 0 && actionId == 0)
                        {
                            CaptureAndPublishNameState(
                                scene,
                                new NameActionActivated(activatedLabel!));
                        }
                        else if (eventType == 0 && actionId == 3)
                        {
                            CaptureAndPublishNameState(scene);
                        }
                        else if (eventType == 0 && actionId == 2)
                        {
                            ArmDirectEntry(scene, directOwner, directTarget);
                        }
                    });
                }));

    private IPreparedHook PrepareDirectEntryActivation(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation,
            build,
            original => capture => boundary.Run(
                "Name direct-entry activation",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch) && HasActiveName();
                    nuint expectedScene = 0;
                    nuint expectedOwner = 0;
                    nuint expectedTarget = 0;
                    if (instrument)
                    {
                        lock (stateGate)
                        {
                            expectedScene = pendingDirectEntryScene;
                            expectedOwner = pendingDirectEntryOwner;
                            expectedTarget = pendingDirectEntryTarget;
                        }
                    }

                    string? error = null;
                    nuint captureTarget = 0;
                    nuint captureOwner = 0;
                    if (instrument)
                    {
                        try
                        {
                            if (expectedScene == 0 || expectedOwner == 0 || expectedTarget == 0)
                            {
                                error = "Name direct-entry activation ran without an exact pending action-2 session.";
                            }
                            else if (capture == 0 ||
                                !TryReadPointer((nuint)capture, out captureTarget) ||
                                !TryReadPointer((nuint)capture + 4, out captureOwner))
                            {
                                error = "Name direct-entry activation capture is unreadable.";
                            }
                            else if (captureTarget != expectedTarget || captureOwner != expectedOwner)
                            {
                                error = "Name direct-entry activation capture does not match its pending scene closure.";
                            }
                            else if (!TryValidateDirectEntryActivationControls(
                                (nuint)capture, out var controlError))
                            {
                                error = controlError;
                            }
                        }
                        catch (Exception exception)
                        {
                            error = $"Name direct-entry activation pre-capture failed: {FormatException(exception)}";
                        }
                    }

                    original()(capture);

                    if (!instrument)
                    {
                        return;
                    }
                    RunInstrumentationSafely(epoch, "Name direct-entry post-activation capture failed", () =>
                    {
                        if (error is not null)
                        {
                            FailCoverage(error);
                            return;
                        }
                        CompleteDirectEntryActivation(
                            expectedScene,
                            expectedOwner,
                            expectedTarget,
                            (nuint)capture);
                    });
                }));

    private IPreparedHook PrepareDirectEntryClose(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<NameDirectEntryCloseDelegate>(
            HookId.NameDirectEntryClose,
            build,
            original => capture => boundary.Run(
                "Name direct-entry close",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch);
                    nuint expectedScene = 0;
                    nuint expectedOwner = 0;
                    nuint expectedTarget = 0;
                    if (instrument)
                    {
                        lock (stateGate)
                        {
                            expectedScene = activeNameScene;
                            expectedOwner = activeDirectEntryOwner;
                            expectedTarget = activeDirectEntryTarget;
                        }
                        instrument = expectedScene != 0 && expectedOwner != 0 && expectedTarget != 0;
                    }

                    string? error = null;
                    if (instrument)
                    {
                        try
                        {
                            if (capture == 0 ||
                                !TryReadPointer((nuint)capture, out var captureTarget))
                            {
                                error = "Name direct-entry close capture is unreadable.";
                            }
                            else if (captureTarget != expectedTarget)
                            {
                                error = "Name direct-entry close capture does not match the active target.";
                            }
                            else if (!TryValidateDirectEntryCloseControls(
                                (nuint)capture, out var controlError))
                            {
                                error = controlError;
                            }
                        }
                        catch (Exception exception)
                        {
                            error = $"Name direct-entry close pre-capture failed: {FormatException(exception)}";
                        }
                    }

                    original()(capture);

                    if (!instrument)
                    {
                        return;
                    }
                    RunInstrumentationSafely(epoch, "Name direct-entry post-close capture failed", () =>
                    {
                        if (error is not null)
                        {
                            FailCoverage(error);
                            return;
                        }
                        CompleteDirectEntryClose(
                            expectedScene,
                            expectedOwner,
                            expectedTarget,
                            (nuint)capture);
                    });
                }));

    private void FinalizeControlDescriptions(CaptureScope scope)
    {
        if (!TryGetScopeText(scope, new(0x23, 0xD6), out var title) ||
            !TryGetScopeText(scope, new(0x23, 0xD5), out var next))
        {
            FailCoverage(FirstScopeErrorOr(scope,
                "Control Descriptions is missing its localized title or Next label."));
            return;
        }
        if (scope.Errors.Count != 0)
        {
            FailCoverage(FirstScopeErrorOr(scope, "Control Descriptions construction capture failed."));
            return;
        }
        if (scope.FocusObservations.Count != 1)
        {
            FailCoverage(
                $"Control Descriptions captured {scope.FocusObservations.Count} input-manager focus calls; expected exactly one.");
            return;
        }
        if (!TryReadPointer(imageBase + ControllerConfigurationRootRva, out var controllerConfiguration) ||
            controllerConfiguration == 0 ||
            !TryReadByte(controllerConfiguration + ControllerArtworkSelectorOffset, out var selectorByte))
        {
            FailCoverage("Control Descriptions controller artwork selector is unreadable.");
            return;
        }
        var selector = selectorByte & 1;
        if (!ControlDescriptionCapture.TryValidateRuntimeTables(
                memory, imageBase, selector, out var tableError))
        {
            FailCoverage($"Control Descriptions runtime tables are invalid: {tableError}");
            return;
        }
        var focus = scope.FocusObservations[0];
        if (!ControlDescriptionCapture.TryCreateSnapshot(
                title,
                next,
                scope.ControlRecords,
                selector,
                focus.Manager,
                focus.Key,
                out var snapshot,
                out var error))
        {
            FailCoverage($"Control Descriptions capture is incomplete: {error}");
            return;
        }

        var lines = snapshot.Records
            .GroupBy(record => record.Y)
            .Select(group => new ControlDescriptionLine(
                new ReadOnlyCollection<string>(group.First().AssociatedSprites
                    .Select(sprite => new string(sprite.Name.AsSpan()))
                    .ToArray()),
                new ReadOnlyCollection<string>(group
                    .Select(record => new string(record.Text.AsSpan()))
                    .ToArray())))
            .ToArray();
        lock (stateGate)
        {
            activeControlNextLabel = snapshot.NextLabel;
            activeControlManager = snapshot.Manager;
        }
        dispatcher.Publish(new ControlDescriptionsPresented(
            snapshot.Title,
            new ReadOnlyCollection<ControlDescriptionLine>(lines),
            snapshot.NextLabel));
    }

    private void FinalizeModeSelect(nuint scene, CaptureScope scope)
    {
        if (scope.Errors.Count != 0)
        {
            FailCoverage(FirstScopeErrorOr(scope, "Mode Select localized construction capture failed."));
            return;
        }
        if (!TryCreateModeSnapshotFromScope(scene, scope, out var snapshot, out var error))
        {
            FailCoverage($"Mode Select capture is incomplete: {error}");
            return;
        }
        lock (stateGate)
        {
            activeModeScene = scene;
            activeModeSnapshot = snapshot;
        }
        dispatcher.Publish(ToPresented(snapshot));
    }

    private void FinalizeNameEntry(nuint scene, CaptureScope scope)
    {
        if (scope.Errors.Count != 0)
        {
            FailCoverage(FirstScopeErrorOr(scope, "Name Entry localized construction capture failed."));
            return;
        }
        if (!TryGetScopeText(scope, NameInputCapture.DefaultsTextKey, out var defaults) ||
            !TryGetScopeText(scope, NameInputCapture.AcceptTextKey, out var accept))
        {
            FailCoverage("Name Entry is missing a localized Defaults or Accept label.");
            return;
        }
        // The grid is initially inactive. Its localized label is drawn by a
        // later refresh, so it is not a prerequisite for the main name screen.
        TryGetScopeText(scope, NameInputCapture.GridActionTextKey, out var gridAction);
        if (!NameInputCapture.TryCreateSnapshot(
                memory, imageBase, scene, gridAction,
                out var snapshot, out var snapshotError))
        {
            FailCoverage($"Name Entry capture is incomplete: {snapshotError}");
            return;
        }
        if (!TryResolveExactKeyManager(
                scope.Bindings, 4, out var manager, out var controls, out var managerError))
        {
            FailCoverage($"Name Entry main-action correlation failed: {managerError}");
            return;
        }
        if (!NameInputCapture.TryCorrelateMainActions(
                [
                    new NameActionObservation(0, controls[0], defaults),
                    new NameActionObservation(1, controls[1], accept),
                ],
                out var actions,
                out var actionError))
        {
            FailCoverage($"Name Entry main-action correlation failed: {actionError}");
            return;
        }
        actions = new ReadOnlyCollection<NameActionSnapshot>(
        [
            .. actions,
            new NameActionSnapshot(2, controls[2], "Name text field"),
            // The native softkeyicon.png button has no text label. Its callback
            // opens/closes the character grid; it is a distinct fourth control.
            new NameActionSnapshot(3, controls[3], "Character grid"),
        ]);

        var matchingFocus = scope.FocusObservations
            .Where(item => item.Manager == manager)
            .ToArray();
        if (matchingFocus.Length > 1 ||
            (matchingFocus.Length == 1 && matchingFocus[0].Key is < 0 or > 3))
        {
            FailCoverage("Name Entry main-action manager has ambiguous or invalid construction-time focus.");
            return;
        }
        int? initialActionKey = matchingFocus.Length == 1 ? matchingFocus[0].Key : null;
        if (!snapshot.GridActive && initialActionKey is null)
        {
            if (!TryReadInt32(manager + ManagerFocusKeyOffset, out var managerKey) ||
                managerKey is < 0 or > 3)
            {
                FailCoverage(
                    "Name Entry grid is inactive and its main-action manager has no authoritative initial focus.");
                return;
            }
            initialActionKey = managerKey;
        }

        lock (stateGate)
        {
            activeNameScene = scene;
            activeNameSnapshot = snapshot;
            activeNameGridActionLabel = gridAction;
            activeNameActions = actions;
            activeNameManager = manager;
            activeConfirmation = null;
            activeConfirmationManager = 0;
            pendingDirectEntryScene = 0;
            pendingDirectEntryOwner = 0;
            pendingDirectEntryTarget = 0;
            pendingDirectEntryGridClosed = false;
            activeDirectEntryOwner = 0;
            activeDirectEntryTarget = 0;
        }
        var initialEvents = new List<NewGameAccessibilityEvent>
        {
            new NameEntryPresented(
                "Enter a name",
                snapshot.Name,
                "Use the character grid or keyboard name entry"),
        };
        if (snapshot.GridActive && snapshot.FocusedCell is not null)
        {
            initialEvents.Add(ToGridFocusEvent(snapshot.FocusedCell, snapshot.PageLabel));
        }
        else if (initialActionKey is not null)
        {
            var action = actions.Single(item => item.ActionId == initialActionKey.Value);
            initialEvents.Add(new NameActionFocused(action.Label, action.ActionId, actions.Count));
        }
        dispatcher.Publish(new NameAccessibilityBatch(
            new ReadOnlyCollection<NewGameAccessibilityEvent>(initialEvents)));
    }

    private void FinalizeConfirmation(CaptureScope scope, string name)
    {
        if (scope.Errors.Count != 0)
        {
            FailCoverage(FirstScopeErrorOr(scope, "Name confirmation construction capture failed."));
            return;
        }
        if (scope.PendingConfirmationControl != 0 ||
            scope.ConstructedControls.Count != 2 ||
            scope.ConfirmationControlLabels.Count != 2)
        {
            FailCoverage(
                "Name confirmation did not produce exactly two CustomButton controls with correlated rendered labels.");
            return;
        }
        if (!TryGetScopeText(scope, NameInputCapture.ConfirmationPromptTextKey, out var prompt))
        {
            FailCoverage("Name confirmation localized prompt template is missing.");
            return;
        }
        if (!TryResolveExactKeyManager(
                scope.Bindings,
                2,
                out var manager,
                out var controlsByKey,
                out var managerError))
        {
            FailCoverage($"Name confirmation manager-key correlation failed: {managerError}");
            return;
        }
        if (!controlsByKey.Values.ToHashSet().SetEquals(scope.ConstructedControls))
        {
            FailCoverage(
                "Name confirmation manager keys 0/1 do not exactly match the two constructed localized controls.");
            return;
        }
        var focus = scope.FocusObservations.Where(item => item.Manager == manager).ToArray();
        if (focus.Length != 1)
        {
            FailCoverage("Name confirmation did not capture exactly one focus call for its correlated manager.");
            return;
        }
        var observations = new List<NameConfirmationChoiceObservation>(2);
        for (var key = 0; key < 2; key++)
        {
            if (!scope.ConfirmationControlLabels.TryGetValue(controlsByKey[key], out var label))
            {
                FailCoverage($"Name confirmation manager key {key} has no localized control-pointer label correlation.");
                return;
            }
            observations.Add(new NameConfirmationChoiceObservation(key, controlsByKey[key], label));
        }
        if (!NameInputCapture.TryCreateConfirmation(
                prompt,
                name,
                observations,
                focus[0].Key,
                out var confirmation,
                out var error))
        {
            FailCoverage($"Name confirmation capture is incomplete: {error}");
            return;
        }

        lock (stateGate)
        {
            activeConfirmation = confirmation;
            activeConfirmationManager = manager;
        }
        dispatcher.Publish(new NameConfirmationPresented(
            confirmation.Prompt,
            new ReadOnlyCollection<string>(confirmation.Choices
                .Select(choice => new string(choice.Label.AsSpan()))
                .ToArray()),
            confirmation.SelectedIndex));
    }

    private void PublishModeChange(nuint scene)
    {
        var previous = GetActiveModeSnapshot();
        var error = "the active Mode Select snapshot is unavailable";
        if (previous is null || !TryRecaptureModeSnapshot(scene, previous, out var snapshot, out error))
        {
            FailCoverage($"Mode Select changed but its authoritative state is unavailable: {error}");
            return;
        }
        lock (stateGate)
        {
            activeModeSnapshot = snapshot;
        }
        dispatcher.Publish(ToChanged(snapshot));
    }

    private void CaptureAndPublishNameState(
        nuint scene,
        NewGameAccessibilityEvent? prefix = null)
    {
        var gridAction = GetActiveNameGridActionLabel();
        if (!NameInputCapture.TryCreateSnapshot(
                memory, imageBase, scene, gridAction,
                out var snapshot, out var error))
        {
            FailCoverage($"Name Entry changed but its authoritative state is unavailable: {error}");
            return;
        }

        NameInputSnapshot? previous;
        lock (stateGate)
        {
            previous = activeNameSnapshot;
        }

        NameActionFocused? closingFocus = null;
        if (previous is { GridActive: true } && !snapshot.GridActive)
        {
            nuint manager;
            IReadOnlyList<NameActionSnapshot> actions;
            lock (stateGate)
            {
                manager = activeNameManager;
                actions = activeNameActions;
            }
            if (manager == 0 ||
                !TryReadInt32(manager + ManagerFocusKeyOffset, out var managerKey))
            {
                FailCoverage("Name Entry grid closed but the main-action manager focus is unavailable.");
                return;
            }
            var action = actions.SingleOrDefault(item => item.ActionId == managerKey);
            if (action is null)
            {
                FailCoverage(
                    $"Name Entry grid closed to manager focus key {managerKey}, which has no correlated localized action.");
                return;
            }
            closingFocus = new NameActionFocused(action.Label, managerKey, actions.Count);
        }

        lock (stateGate)
        {
            activeNameSnapshot = snapshot;
        }
        var events = new List<NewGameAccessibilityEvent>();
        if (prefix is not null)
        {
            events.Add(prefix);
        }
        if (previous is not null && previous.GridActive != snapshot.GridActive)
        {
            events.Add(new NameGridVisibilityChanged(snapshot.GridActive));
        }
        if (previous is null || !string.Equals(previous.Name, snapshot.Name, StringComparison.Ordinal))
        {
            events.Add(new NewGameNameChanged(snapshot.Name));
        }
        if (snapshot.GridActive && snapshot.FocusedCell is not null &&
            (previous?.FocusedCell != snapshot.FocusedCell || previous.Page != snapshot.Page))
        {
            events.Add(ToGridFocusEvent(snapshot.FocusedCell, snapshot.PageLabel));
        }
        else if (closingFocus is not null)
        {
            events.Add(closingFocus);
        }
        if (events.Count != 0)
        {
            dispatcher.Publish(new NameAccessibilityBatch(
                new ReadOnlyCollection<NewGameAccessibilityEvent>(events)));
        }
    }

    private void ArmDirectEntry(nuint scene, nuint owner, nuint target)
    {
        var gridAction = GetActiveNameGridActionLabel();
        if (!NameInputCapture.TryCreateSnapshot(
                memory, imageBase, scene, gridAction,
                out var snapshot, out var error))
        {
            FailCoverage(
                $"Keyboard name-entry activation cannot capture its authoritative screen state: {error}");
            return;
        }
        if (snapshot.GridActive)
        {
            FailCoverage(
                "Keyboard name-entry activation did not close the authoritative character grid before scheduling the IME.");
            return;
        }

        lock (stateGate)
        {
            if (activeNameScene != scene || activeNameSnapshot is null ||
                activeNameManager != target ||
                pendingDirectEntryOwner != 0 || activeDirectEntryOwner != 0)
            {
                FailCoverage(
                    "Keyboard name-entry activation no longer matches the active Name Entry screen.");
                return;
            }
            pendingDirectEntryGridClosed = activeNameSnapshot.GridActive && !snapshot.GridActive;
            activeNameSnapshot = snapshot;
            pendingDirectEntryScene = scene;
            pendingDirectEntryOwner = owner;
            pendingDirectEntryTarget = target;
        }
    }

    private void CompleteDirectEntryActivation(
        nuint scene,
        nuint owner,
        nuint target,
        nuint capture)
    {
        var gridClosed = false;
        lock (stateGate)
        {
            if (pendingDirectEntryScene != scene ||
                pendingDirectEntryOwner != owner ||
                pendingDirectEntryTarget != target ||
                activeNameScene != scene || activeNameSnapshot is null)
            {
                FailCoverage(
                    "Name direct-entry activation no longer matches its pending Name Entry screen.");
                return;
            }
            gridClosed = pendingDirectEntryGridClosed;
        }
        if (!TryReadByte(target + DirectEntryTargetStateOffset, out var targetState) ||
            targetState != 1 ||
            !TryReadByte(owner + NameKeyboardStateOffset, out var keyboardState) ||
            keyboardState != 1 ||
            !HasAuthoritativeNameActionFocus(2))
        {
            FailCoverage(
                "Name direct-entry activation did not produce the authoritative target, IME, and key-2 focus states.");
            return;
        }
        if (!TryValidateDirectEntryActivationControls(capture, out var controlError))
        {
            FailCoverage(controlError);
            return;
        }

        var gridAction = GetActiveNameGridActionLabel();
        if (!NameInputCapture.TryCreateSnapshot(
                memory, imageBase, scene, gridAction,
                out var snapshot, out var error))
        {
            FailCoverage(
                $"Name direct-entry activation cannot capture its authoritative screen state: {error}");
            return;
        }
        if (snapshot.GridActive)
        {
            FailCoverage(
                "Name direct-entry activation reopened while the authoritative character grid remained active.");
            return;
        }

        lock (stateGate)
        {
            if (pendingDirectEntryScene != scene ||
                pendingDirectEntryOwner != owner ||
                pendingDirectEntryTarget != target)
            {
                FailCoverage(
                    "Name direct-entry activation changed while its authoritative state was captured.");
                return;
            }
            activeNameSnapshot = snapshot;
            pendingDirectEntryScene = 0;
            pendingDirectEntryOwner = 0;
            pendingDirectEntryTarget = 0;
            pendingDirectEntryGridClosed = false;
            activeDirectEntryOwner = owner;
            activeDirectEntryTarget = target;
        }
        var events = new List<NewGameAccessibilityEvent>();
        if (gridClosed)
        {
            events.Add(new NameGridVisibilityChanged(false));
        }
        events.Add(new KeyboardNameEntryFocused(snapshot.Name));
        dispatcher.Publish(new NameAccessibilityBatch(
            new ReadOnlyCollection<NewGameAccessibilityEvent>(events)));
    }

    private void CompleteDirectEntryClose(
        nuint scene,
        nuint owner,
        nuint target,
        nuint capture)
    {
        if (!TryReadByte(target + DirectEntryTargetStateOffset, out var targetState) ||
            targetState != 0 ||
            !TryReadByte(owner + NameKeyboardStateOffset, out var keyboardState) ||
            keyboardState != 0)
        {
            FailCoverage(
                "Name direct-entry close did not restore its exact inactive manager and IME states.");
            return;
        }
        if (!HasAuthoritativeNameActionFocus(2))
        {
            FailCoverage(
                "Name direct-entry close did not preserve authoritative key-2 manager focus.");
            return;
        }
        if (!TryValidateDirectEntryCloseControls(capture, out var controlError))
        {
            FailCoverage(controlError);
            return;
        }

        var gridAction = GetActiveNameGridActionLabel();
        if (!NameInputCapture.TryCreateSnapshot(
                memory, imageBase, scene, gridAction,
                out var snapshot, out var error))
        {
            FailCoverage(
                $"Name direct-entry close cannot capture its authoritative screen state: {error}");
            return;
        }
        if (snapshot.GridActive)
        {
            FailCoverage(
                "Name direct-entry close unexpectedly reopened the authoritative character grid.");
            return;
        }

        string actionLabel;
        lock (stateGate)
        {
            var action = activeNameActions.SingleOrDefault(item => item.ActionId == 2);
            if (activeNameScene != scene || activeNameManager != target ||
                activeDirectEntryOwner != owner || activeDirectEntryTarget != target ||
                action is null || activeNameActions.Count != 4)
            {
                FailCoverage(
                    "Name direct-entry close no longer matches its active scene and key-2 control.");
                return;
            }
            actionLabel = new string(action.Label.AsSpan());
            activeNameSnapshot = snapshot;
            activeDirectEntryOwner = 0;
            activeDirectEntryTarget = 0;
        }
        dispatcher.Publish(new NameAccessibilityBatch(
            new ReadOnlyCollection<NewGameAccessibilityEvent>(
            [
                new KeyboardNameEntryClosed(snapshot.Name),
                new NameActionFocused(actionLabel, 2, 4),
            ])));
    }

    private bool TryValidateDirectEntryCloseControls(nuint capture, out string error)
    {
        nuint[] expectedControls;
        lock (stateGate)
        {
            if (activeNameActions.Count != 4 ||
                activeNameActions.Where((item, index) => item.ActionId != index).Any())
            {
                error = "Name direct-entry close has no exact four-action correlation.";
                return false;
            }
            // The IME restores buttons 0/1 and the grid button 3. The name
            // field at key 2 keeps focus and is not in this enable/disable set.
            expectedControls = activeNameActions.Where(item => item.ActionId != 2)
                .Select(item => item.Control).ToArray();
        }

        if (!TryReadPointer(capture + 4, out var vectorBegin) || vectorBegin == 0 ||
            !TryReadPointer(capture + 8, out var vectorEnd) ||
            !TryReadPointer(capture + 12, out var vectorCapacity) ||
            (vectorBegin & 3) != 0 || (vectorEnd & 3) != 0 || (vectorCapacity & 3) != 0 ||
            vectorEnd != vectorBegin + 8 || vectorCapacity < vectorEnd ||
            !TryReadPointer(vectorBegin, out var control0) ||
            !TryReadPointer(vectorBegin + 4, out var control1) ||
            !TryReadPointer(capture + 0x10, out var gridButton) ||
            control0 != expectedControls[0] ||
            control1 != expectedControls[1] ||
            gridButton != expectedControls[2])
        {
            error = "Name direct-entry close controls do not match the exact two-element action vector and key-3 grid button.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private bool TryValidateDirectEntryActivationControls(nuint capture, out string error)
    {
        nuint[] expectedControls;
        lock (stateGate)
        {
            if (activeNameActions.Count != 4 ||
                activeNameActions.Where((item, index) => item.ActionId != index).Any())
            {
                error = "Name direct-entry activation has no exact four-action correlation.";
                return false;
            }
            expectedControls = activeNameActions.Where(item => item.ActionId != 2)
                .Select(item => item.Control).ToArray();
        }

        if (!TryReadPointer(capture + 8, out var vectorBegin) || vectorBegin == 0 ||
            !TryReadPointer(capture + 12, out var vectorEnd) ||
            !TryReadPointer(capture + 0x10, out var vectorCapacity) ||
            (vectorBegin & 3) != 0 || (vectorEnd & 3) != 0 || (vectorCapacity & 3) != 0 ||
            vectorEnd != vectorBegin + 8 || vectorCapacity < vectorEnd ||
            !TryReadPointer(vectorBegin, out var control0) ||
            !TryReadPointer(vectorBegin + 4, out var control1) ||
            !TryReadPointer(capture + 0x14, out var gridButton) ||
            control0 != expectedControls[0] ||
            control1 != expectedControls[1] ||
            gridButton != expectedControls[2])
        {
            error = "Name direct-entry activation controls do not match the exact two-element action vector and key-3 grid button.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static NameGridFocused ToGridFocusEvent(
        NameGridCellSnapshot cell,
        string pageLabel) =>
        new(cell.Label, pageLabel, cell.Row, cell.Column);

    private void PublishRuntimeManagerFocus(nuint manager, int managerKey)
    {
        NameConfirmationSnapshot? confirmation;
        nuint confirmationManager;
        nuint nameManager;
        IReadOnlyList<NameActionSnapshot> actions;
        lock (stateGate)
        {
            confirmation = activeConfirmation;
            confirmationManager = activeConfirmationManager;
            nameManager = activeNameManager;
            actions = activeNameActions;
        }
        if (manager == confirmationManager && confirmation is not null)
        {
            var choice = confirmation.Choices.SingleOrDefault(item => item.ManagerKey == managerKey);
            if (choice is null)
            {
                FailCoverage($"Name confirmation focus key {managerKey} has no correlated localized choice.");
                return;
            }
            dispatcher.Publish(new NameConfirmationFocused(
                choice.Label, managerKey, confirmation.Choices.Count));
        }
        else if (manager == nameManager && actions.Count != 0)
        {
            PublishNameActionFocus(managerKey);
        }
    }

    private void PublishNameActionFocus(int actionId)
    {
        IReadOnlyList<NameActionSnapshot> actions;
        lock (stateGate)
        {
            actions = activeNameActions;
        }
        var action = actions.SingleOrDefault(item => item.ActionId == actionId);
        if (action is null)
        {
            FailCoverage($"Name Entry action focus key {actionId} has no correlated localized control.");
            return;
        }
        dispatcher.Publish(new NameActionFocused(action.Label, actionId, actions.Count));
    }

    private bool TryGetNameActionLabel(int actionId, out string label)
    {
        lock (stateGate)
        {
            var action = activeNameActions.SingleOrDefault(item => item.ActionId == actionId);
            if (action is null)
            {
                label = string.Empty;
                return false;
            }
            label = new string(action.Label.AsSpan());
            return true;
        }
    }

    private bool HasAuthoritativeNameActionFocus(int actionId)
    {
        nuint manager;
        IReadOnlyList<NameActionSnapshot> actions;
        lock (stateGate)
        {
            manager = activeNameManager;
            actions = activeNameActions;
        }
        return manager != 0 && actions.Count == 4 &&
            actions.SingleOrDefault(item => item.ActionId == actionId) is not null &&
            TryReadInt32(manager + ManagerFocusKeyOffset, out var managerKey) &&
            managerKey == actionId;
    }

    private nuint GetActiveNameManager()
    {
        lock (stateGate)
        {
            return activeNameManager;
        }
    }

    private bool TryCreateModeSnapshotFromScope(
        nuint scene,
        CaptureScope scope,
        out ModeSelectSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        error = string.Empty;
        if (!ModeSelectCapture.TryValidateRuntimeLayout(
                memory, scene, out var layout, out error))
        {
            return false;
        }
        if (!TryValidateModeGetterChains(layout, out var getterTargets, out error))
        {
            return false;
        }
        var rows = new ModeSelectLocalizedRow[ModeSelectCapture.RowCount];
        for (var index = 0; index < rows.Length; index++)
        {
            var contract = ModeSelectCapture.TextContracts[index];
            if (!TryGetScopeText(scope, contract.Label, out var label) ||
                !TryGetScopeTexts(scope, contract.Values, out var values) ||
                !TryGetScopeTexts(scope, contract.Help, out var help) ||
                !TryInvokeModeValue(index, getterTargets[index], out var value, out error))
            {
                error = string.IsNullOrWhiteSpace(error)
                    ? $"Mode Select row {index} is missing localized label/value/help text."
                    : error;
                return false;
            }
            rows[index] = new ModeSelectLocalizedRow(
                layout.Rows[index].RecordAddress,
                label,
                values,
                help,
                value);
        }
        if (!TryGetScopeText(scope, ModeSelectCapture.StartTextKey, out var start))
        {
            error = "Mode Select localized Start label is missing.";
            return false;
        }
        return ModeSelectCapture.TryCreateSnapshot(
            memory, scene, rows, start, out snapshot, out error);
    }

    private bool TryRecaptureModeSnapshot(
        nuint scene,
        ModeSelectSnapshot previous,
        out ModeSelectSnapshot snapshot,
        out string error)
    {
        if (!ModeSelectCapture.TryValidateRuntimeLayout(
                memory, scene, out var layout, out error))
        {
            snapshot = null!;
            return false;
        }
        if (previous.Rows.Count != layout.Rows.Count ||
            previous.Rows.Where((row, index) => row.RecordAddress != layout.Rows[index].RecordAddress).Any())
        {
            snapshot = null!;
            error = "Mode Select runtime record addresses changed after the validated screen entry.";
            return false;
        }
        if (!TryValidateModeGetterChains(layout, out var getterTargets, out error))
        {
            snapshot = null!;
            return false;
        }
        var rows = new ModeSelectLocalizedRow[ModeSelectCapture.RowCount];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = previous.Rows[index];
            if (!TryInvokeModeValue(index, getterTargets[index], out var value, out error))
            {
                snapshot = null!;
                return false;
            }
            rows[index] = new ModeSelectLocalizedRow(
                row.RecordAddress,
                row.Label,
                row.Values,
                row.Help,
                value);
        }
        return ModeSelectCapture.TryCreateSnapshot(
            memory,
            scene,
            rows,
            previous.StartLabel,
            out snapshot,
            out error);
    }

    private bool TryValidateModeGetterChains(
        ModeSelectRuntimeLayout layout,
        out IReadOnlyList<nuint> targets,
        out string error)
    {
        var validated = new nuint[ModeSelectCapture.RowCount];
        if (layout.Rows.Count != validated.Length || modeValueGetters.Length != validated.Length)
        {
            targets = Array.Empty<nuint>();
            error = "Mode Select getter preflight does not contain exactly three runtime rows/wrappers.";
            return false;
        }
        for (var rowIndex = 0; rowIndex < validated.Length; rowIndex++)
        {
            var target = layout.Rows[rowIndex].GetterTarget;
            if (target == 0 ||
                !TryReadPointer(target, out var vtable) ||
                vtable == 0 ||
                !TryReadPointer(vtable + 8, out var method))
            {
                targets = Array.Empty<nuint>();
                error = $"Mode Select row {rowIndex} getter target/vtable/method is unreadable.";
                return false;
            }
            var expected = imageBase + ModeValueGetterRvas[rowIndex];
            if (method != expected)
            {
                targets = Array.Empty<nuint>();
                error = $"Mode Select row {rowIndex} getter method 0x{method:X} does not match exact-build address 0x{expected:X}.";
                return false;
            }
            validated[rowIndex] = target;
        }
        targets = new ReadOnlyCollection<nuint>(validated);
        error = string.Empty;
        return true;
    }

    private bool TryInvokeModeValue(int rowIndex, nuint target, out int value, out string error)
    {
        value = 0;
        try
        {
            value = modeValueGetters[rowIndex]((nint)target);
        }
        catch (Exception exception)
        {
            error = $"Mode Select row {rowIndex} getter threw: {FormatException(exception)}";
            return false;
        }
        if (value is not 0 and not 1)
        {
            error = $"Mode Select row {rowIndex} getter returned {value}; expected 0 or 1.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static ModeSelectPresented ToPresented(ModeSelectSnapshot snapshot) =>
        new(ToModeRows(snapshot), snapshot.StartLabel, snapshot.CompositeFocus);

    private static ModeSelectChanged ToChanged(ModeSelectSnapshot snapshot) =>
        new(ToModeRows(snapshot), snapshot.StartLabel, snapshot.CompositeFocus);

    private static IReadOnlyList<ModeSelectRowPresentation> ToModeRows(ModeSelectSnapshot snapshot) =>
        new ReadOnlyCollection<ModeSelectRowPresentation>(snapshot.Rows
            .Select(row => new ModeSelectRowPresentation(row.Label, row.CurrentValue, row.CurrentHelp))
            .ToArray());

    private void ObserveLocalizedText(
        CaptureScope scope,
        int bank,
        int messageId,
        nint result,
        nint returned)
    {
        var key = new LocalizedMessageKey(bank, messageId);
        if (!ScopeNeedsText(scope, key))
        {
            return;
        }
        var address = returned != 0 ? (nuint)returned : (nuint)result;
        if (!stringReader.TryRead(address, out var text, out var error) || string.IsNullOrWhiteSpace(text))
        {
            scope.Errors.Add(
                $"Localized text ({bank:X},{messageId:X}) is unreadable or blank: {error}");
            return;
        }
        text = new string(text.AsSpan());

        if (scope.Kind == CaptureKind.ControlDescriptions && bank == 0x37)
        {
            scope.ControlRecords.Add(new ControlDescriptionTextObservation(messageId, text));
            return;
        }
        if (scope.Kind == CaptureKind.NameConfirmation &&
            NameInputCapture.ConfirmationChoiceTextKeys.Contains(key))
        {
            if (scope.PendingConfirmationControl == 0)
            {
                scope.Errors.Add(
                    $"Confirmation choice text ({bank:X},{messageId:X}) was not immediately preceded by a CustomButton constructor.");
                return;
            }
            if (!scope.ConfirmationLocalizedLabels.TryAdd(scope.PendingConfirmationControl, text))
            {
                scope.Errors.Add("Confirmation choice control received more than one localized label.");
            }
            StoreScopeText(scope, key, text);
            return;
        }
        StoreScopeText(scope, key, text);
    }

    private static bool ScopeNeedsText(CaptureScope scope, LocalizedMessageKey key) =>
        scope.Kind switch
        {
            CaptureKind.ControlDescriptions =>
                key is { Bank: 0x23, MessageId: 0xD6 or 0xD5 } ||
                key.Bank == 0x37 && ControlDescriptionCapture.RequiredMessageIds.Contains(key.MessageId),
            CaptureKind.ModeSelect => ModeTextKeys.Contains(key),
            CaptureKind.NameEntry => NameTextKeys.Contains(key),
            CaptureKind.NameGridRefresh => key == NameInputCapture.GridActionTextKey,
            CaptureKind.NameConfirmation =>
                key == NameInputCapture.ConfirmationPromptTextKey ||
                NameInputCapture.ConfirmationChoiceTextKeys.Contains(key),
            _ => false,
        };

    private static void StoreScopeText(CaptureScope scope, LocalizedMessageKey key, string text)
    {
        if (!scope.Text.TryAdd(key, text))
        {
            scope.Errors.Add($"Localized text ({key.Bank:X},{key.MessageId:X}) was captured more than once.");
        }
    }

    private static bool TryGetScopeText(
        CaptureScope scope,
        LocalizedMessageKey key,
        out string text) => scope.Text.TryGetValue(key, out text!);

    private static bool TryGetScopeTexts(
        CaptureScope scope,
        IReadOnlyList<LocalizedMessageKey> keys,
        out IReadOnlyList<string> texts)
    {
        var captured = new string[keys.Count];
        for (var index = 0; index < keys.Count; index++)
        {
            if (!TryGetScopeText(scope, keys[index], out captured[index]))
            {
                texts = Array.Empty<string>();
                return false;
            }
        }
        texts = new ReadOnlyCollection<string>(captured);
        return true;
    }

    private static bool TryResolveExactKeyManager(
        IReadOnlyList<(nuint Manager, nuint Control, int Key)> bindings,
        int keyCount,
        out nuint manager,
        out IReadOnlyDictionary<int, nuint> controls,
        out string error)
    {
        manager = 0;
        controls = new ReadOnlyDictionary<int, nuint>(new Dictionary<int, nuint>());
        var candidates = bindings
            .Where(item => item.Manager != 0 && item.Control != 0)
            .GroupBy(item => item.Manager)
            .Select(group => new
            {
                Manager = group.Key,
                Items = group.ToArray(),
            })
            .Where(candidate =>
                candidate.Items.Length == keyCount &&
                candidate.Items.Select(item => item.Key).Order().SequenceEqual(
                    Enumerable.Range(0, keyCount)) &&
                candidate.Items.Select(item => item.Control).Distinct().Count() == keyCount)
            .ToArray();
        if (candidates.Length != 1)
        {
            error = $"Expected one manager with exact distinct keys 0 through {keyCount - 1}; found {candidates.Length}.";
            return false;
        }
        manager = candidates[0].Manager;
        controls = new ReadOnlyDictionary<int, nuint>(
            candidates[0].Items.ToDictionary(item => item.Key, item => item.Control));
        error = string.Empty;
        return true;
    }

    private CaptureScope BeginScope(CaptureKind kind)
    {
        if (threadCaptureScope is not null)
        {
            throw new InvalidOperationException(
                $"Nested New Game capture scope {kind} entered while {threadCaptureScope.Kind} is active.");
        }
        var scope = new CaptureScope(this, kind);
        threadCaptureScope = scope;
        return scope;
    }

    private static void EndScope(CaptureScope scope)
    {
        if (ReferenceEquals(threadCaptureScope, scope))
        {
            threadCaptureScope = scope.Parent;
        }
    }

    private CaptureScope? GetOwnedThreadScope() =>
        ReferenceEquals(threadCaptureScope?.Owner, this) ? threadCaptureScope : null;

    private void InitializeBuild(IVerifiedGameBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.ImageBaseAddress == 0)
        {
            throw new InvalidOperationException("Verified image base is unavailable for New Game hooks.");
        }
        if (imageBase != 0)
        {
            if (imageBase != build.ImageBaseAddress)
            {
                throw new InvalidOperationException("New Game hooks received conflicting image bases.");
            }
            return;
        }

        imageBase = build.ImageBaseAddress;
        var wrappers = new ModeValueGetterDelegate[ModeValueGetterRvas.Length];
        for (var index = 0; index < wrappers.Length; index++)
        {
            wrappers[index] = wrapperFactory.CreateWrapper<ModeValueGetterDelegate>(
                imageBase + ModeValueGetterRvas[index]) ??
                throw new InvalidOperationException(
                    $"Reloaded returned a null Mode Select getter wrapper for row {index}.");
        }
        modeValueGetters = wrappers;
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
            ClearRuntimeState();
            try
            {
                dispatcher.ReportCoverageFailure(
                    string.IsNullOrWhiteSpace(diagnostic)
                        ? "New Game accessibility coverage failed without a diagnostic."
                        : diagnostic);
            }
            catch (Exception)
            {
                // The shared fanout/boundary has an independent final containment layer.
            }
        }
    }

    private void ClearRuntimeState()
    {
        lock (stateGate)
        {
            activeControlNextLabel = null;
            activeControlManager = 0;
            activeModeScene = 0;
            activeModeSnapshot = null;
            activeNameScene = 0;
            activeNameSnapshot = null;
            activeNameGridActionLabel = null;
            activeNameActions = Array.Empty<NameActionSnapshot>();
            activeNameManager = 0;
            activeConfirmation = null;
            activeConfirmationManager = 0;
            pendingDirectEntryScene = 0;
            pendingDirectEntryOwner = 0;
            pendingDirectEntryTarget = 0;
            pendingDirectEntryGridClosed = false;
            activeDirectEntryOwner = 0;
            activeDirectEntryTarget = 0;
        }
    }

    private bool HasActiveControl()
    {
        lock (stateGate)
        {
            return activeControlNextLabel is not null && activeControlManager != 0;
        }
    }

    private string? GetActiveControlNextLabel()
    {
        lock (stateGate)
        {
            return activeControlNextLabel is null ? null : new string(activeControlNextLabel.AsSpan());
        }
    }

    private bool HasActiveMode()
    {
        lock (stateGate)
        {
            return activeModeScene != 0 && activeModeSnapshot is not null;
        }
    }

    private nuint GetActiveModeScene()
    {
        lock (stateGate)
        {
            return activeModeScene;
        }
    }

    private ModeSelectSnapshot? GetActiveModeSnapshot()
    {
        lock (stateGate)
        {
            return activeModeSnapshot;
        }
    }

    private bool HasActiveName()
    {
        lock (stateGate)
        {
            return activeNameScene != 0 && activeNameSnapshot is not null;
        }
    }

    private nuint GetActiveNameScene()
    {
        lock (stateGate)
        {
            return activeNameScene;
        }
    }

    private string? GetActiveNameGridActionLabel()
    {
        lock (stateGate)
        {
            return activeNameGridActionLabel is null
                ? null
                : new string(activeNameGridActionLabel.AsSpan());
        }
    }

    private bool IsTrackedRuntimeManager(nuint manager)
    {
        lock (stateGate)
        {
            return manager != 0 &&
                (manager == activeNameManager || manager == activeConfirmationManager);
        }
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

    private static string FirstScopeErrorOr(CaptureScope scope, string fallback) =>
        scope.Errors.FirstOrDefault() ?? fallback;

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

    private sealed class CaptureScope(NewGameHookSet owner, CaptureKind kind)
    {
        public NewGameHookSet Owner { get; } = owner;
        public CaptureKind Kind { get; } = kind;
        public CaptureScope? Parent { get; init; }
        public nuint NameScene { get; set; }
        public Dictionary<LocalizedMessageKey, string> Text { get; } = [];
        public List<ControlDescriptionTextObservation> ControlRecords { get; } = [];
        public List<(nuint Manager, int Key)> FocusObservations { get; } = [];
        public List<(nuint Manager, nuint Control, int Key)> Bindings { get; } = [];
        public List<nuint> ConstructedControls { get; } = [];
        public Dictionary<nuint, string> ConfirmationControlLabels { get; } = [];
        public Dictionary<nuint, string> ConfirmationLocalizedLabels { get; } = [];
        public List<string> Errors { get; } = [];
        public nuint PendingConfirmationControl { get; set; }
    }

    private enum CaptureKind
    {
        ControlDescriptions,
        ModeSelect,
        NameEntry,
        NameGridRefresh,
        NameConfirmation,
    }
}
