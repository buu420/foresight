using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.SaveLoad;

/// <summary>
/// Speaks the confirmations that <c>nsMenu::MenuNodeSaveLoadSteam</c> raises, including the
/// "Resume bookmarked game?" prompt the title screen's Resume row opens directly into.
/// <para>Native path, proven in artifacts/research/resume-confirmation-0314: title row 0
/// dispatches <c>SceneManager::NextScene(3)</c> at RVA 0x2D0078, which leaves the title scene
/// for scene 0x0D; <c>SaveLoadGameSteamScene::init(1)</c> at 0x2B41B0 maps that to node mode 3,
/// and <c>MenuNodeSaveLoadSteam::open</c> at 0x218A20 immediately calls the confirmation builder
/// at 0x21A1D0 for modes 2 and 3. Nothing spoke because the title hook set opens its capture
/// scope only for title action 6 (Quit) and had already published ScreenExited for the title
/// menu, which leaves the semantic state with no active screen.</para>
/// <para>The builder localizes one prompt and then <c>(0x41, 0x11)</c> "Yes" and
/// <c>(0x41, 0x12)</c> "No", constructs both choices through the already-shared nsMenu
/// CustomButton constructor and control binder, and finishes on the shared focus setter with
/// key 1, so "No" is preselected. Every spoken string is the string the game itself just
/// produced; the node mode only decides which message id is <em>expected</em>.</para>
/// </summary>
public sealed class SaveLoadConfirmationHookSet : IHookActivationObserver, ISharedNativeHookObserver
{
    /// <summary>nsMenu manager, CustomButton and FocusableState layout. Identical to the title
    /// menu's, because the confirmation builder calls the same 0x1DCCA0, 0x1D2160 and 0x1DD260.</summary>
    public const uint ManagerFocusKeyOffset = 0x2C4;
    public const uint FocusableStateControlOffset = 0x14;
    public const uint ManagerVtableRva = 0x3A5D0C;
    public const uint CustomButtonVtableRva = 0x3A4364;
    public const uint FocusableStateVtableRva = 0x3AC3F4;

    /// <summary>nsMenu::MenuNodeSaveLoadSteam, whose vtable is written at RVA 0x218888.</summary>
    public const uint SaveLoadNodeVtableRva = 0x3A980C;

    /// <summary>Node mode, written by open() at RVA 0x218A60.</summary>
    public const uint NodeModeOffset = 0x2CC;

    /// <summary>Set to 1 by the confirmation builder at RVA 0x21A209, cleared when it closes.</summary>
    public const uint NodeConfirmationActiveOffset = 0x2EC;

    /// <summary>Localize/en/msg/start.txt.</summary>
    public const int StartTextFileId = 0x41;

    /// <summary>The Ope bank the save modes can take their prompt from instead.</summary>
    public const int OpeBankId = 0x23;
    public const int OpeSavePromptId = 0x8E;

    public const int FirstChoiceMessageId = 0x11;
    public const int ChoiceCount = 2;

    public const int SaveMode = 0;
    public const int LoadMode = 1;
    public const int BookmarkAndQuitMode = 2;
    public const int ResumeMode = 3;
    public const int SaveOverwriteModeA = 4;
    public const int SaveOverwriteModeB = 5;

    private static readonly HookId[] DedicatedHookIds =
    [
        HookId.SaveLoadConfirmationBuilder,
        HookId.SaveLoadNodeDestructor,
    ];

    [ThreadStatic]
    private static ConfirmationScope? threadScope;

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly MsvcStringReader stringReader;
    private readonly object lifecycleGate = new();
    private readonly object stateGate = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private ActiveConfirmation? active;
    private bool screenEntered;
    private int activeEpoch = 1;
    private bool hooksActive = true;

    public SaveLoadConfirmationHookSet(
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
            CreateRegistration(HookId.SaveLoadConfirmationBuilder, PrepareConfirmationBuilder),
            CreateRegistration(HookId.SaveLoadNodeDestructor, PrepareNodeDestructor),
        ]);
    }

    public IReadOnlyList<HookId> RequiredHookIds { get; }

    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    public void AfterHooksActivated()
    {
        lock (lifecycleGate)
        {
            if (!hooksActive)
            {
                throw new InvalidOperationException(
                    "Save/load confirmation hooks were disabled before activation completed.");
            }
        }
    }

    public void AfterHooksDisabled()
    {
        lock (lifecycleGate)
        {
            if (!hooksActive)
            {
                return;
            }

            hooksActive = false;
            activeEpoch = unchecked(activeEpoch + 1);
            if (ReferenceEquals(threadScope?.Owner, this))
            {
                threadScope = null;
            }
        }

        lock (stateGate)
        {
            active = null;
            screenEntered = false;
        }
    }

    /// <summary>The prompt and both choices arrive through TextManager::getMsg on file 0x41.</summary>
    public void AfterTextManagerGetMsg(nint textManager, nint result, int fileId, int messageId, nint returned)
    {
        _ = textManager;
        try
        {
            if (!TryCaptureActiveEpoch(out _) || GetOwnedScope() is not { } scope ||
                fileId != StartTextFileId)
            {
                return;
            }

            if (messageId >= FirstChoiceMessageId && messageId < FirstChoiceMessageId + ChoiceCount)
            {
                StoreChoice(scope, messageId - FirstChoiceMessageId, result, returned);
                return;
            }

            if (IsExpectedPromptMessage(scope.Mode, fileId, messageId))
            {
                StorePrompt(scope, fileId, messageId, result, returned);
            }
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Save/load confirmation localized-text capture failed: {FormatException(exception)}");
        }
    }

    /// <summary>The save modes can take their prompt from the Ope bank instead. getMsg itself
    /// re-enters this resolver at RVA 0x1B9150 with the same file and message, so the nested
    /// raw text has to be skipped or the processed getMsg result would look like a second
    /// prompt and fail the capture closed.</summary>
    public void AfterOpeTextResolver(nint resolver, nint result, int bank, int messageId, nint returned)
    {
        _ = resolver;
        try
        {
            if (SharedNativeHookFanoutFactory.IsTextManagerGetMsgActive ||
                !TryCaptureActiveEpoch(out _) || GetOwnedScope() is not { } scope ||
                !IsExpectedPromptMessage(scope.Mode, bank, messageId))
            {
                return;
            }

            StorePrompt(scope, bank, messageId, result, returned);
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Save/load confirmation Ope localized-text capture failed: {FormatException(exception)}");
        }
    }

    public void AfterCustomButtonConstructed(nint storage, nint returned)
    {
        try
        {
            if (!TryCaptureActiveEpoch(out _) || GetOwnedScope() is not { } scope)
            {
                return;
            }

            var control = (nuint)returned;
            if (storage == 0 || returned != storage || !TryReadExactVtable(control, CustomButtonVtableRva))
            {
                scope.Errors.Add("Save/load confirmation CustomButton did not return exact typed ECX storage.");
            }
            else if (!scope.Controls.Add(control))
            {
                scope.Errors.Add("Save/load confirmation constructed the same choice control more than once.");
            }
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Save/load confirmation control capture failed: {FormatException(exception)}");
        }
    }

    public void AfterControlBound(nint manager, nint focusableState, int managerKey)
    {
        try
        {
            if (!TryCaptureActiveEpoch(out _) || GetOwnedScope() is not { } scope)
            {
                return;
            }

            var managerPointer = (nuint)manager;
            var state = (nuint)focusableState;
            if (managerKey < 0 || !TryReadExactVtable(managerPointer, ManagerVtableRva) ||
                !TryReadExactVtable(state, FocusableStateVtableRva) ||
                !TryReadPointer(state + FocusableStateControlOffset, out var control) ||
                !TryReadExactVtable(control, CustomButtonVtableRva))
            {
                scope.Errors.Add(
                    $"Save/load confirmation binder key {managerKey} does not match the audited manager/control layout.");
                return;
            }

            scope.Bindings.Add(new ChoiceBinding(managerPointer, control, managerKey));
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Save/load confirmation binder capture failed: {FormatException(exception)}");
        }
    }

    public void AfterFocusSet(nint manager, int managerKey)
    {
        try
        {
            if (!TryCaptureActiveEpoch(out _))
            {
                return;
            }

            var managerPointer = (nuint)manager;
            var owned = GetOwnedScope();
            if (!TryReadExactVtable(managerPointer, ManagerVtableRva) ||
                !TryReadInt32(managerPointer + ManagerFocusKeyOffset, out var authoritativeKey) ||
                authoritativeKey != managerKey)
            {
                owned?.Errors.Add("Save/load confirmation focus did not commit to the audited manager state.");
                return;
            }

            if (owned is not null)
            {
                owned.Focus.Add(new ChoiceFocus(managerPointer, managerKey));
                return;
            }

            ObserveLiveFocus(managerPointer, managerKey);
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Save/load confirmation focus capture failed: {FormatException(exception)}");
        }
    }

    private void ObserveLiveFocus(nuint manager, int managerKey)
    {
        ActiveConfirmation confirmation;
        lock (stateGate)
        {
            if (active is not { } current || current.Manager != manager)
            {
                return;
            }

            confirmation = current;
        }

        // The builder is the only thing that opens a confirmation, so a manager that outlived
        // its node would otherwise keep answering focus moves. Re-read the node every time.
        if (!TryReadExactVtable(confirmation.Node, SaveLoadNodeVtableRva) ||
            !TryReadByte(confirmation.Node + NodeConfirmationActiveOffset, out var stillOpen) ||
            stillOpen == 0)
        {
            CloseConfirmation(confirmation.Node);
            return;
        }

        if (!confirmation.IndexByKey.TryGetValue(managerKey, out var selectedIndex))
        {
            dispatcher.ReportCoverageFailure(
                $"Save/load confirmation focus key {managerKey} has no captured localized choice.");
            return;
        }

        dispatcher.Publish(new ConfirmationOpened(
            confirmation.Prompt,
            confirmation.Choices,
            selectedIndex));
    }

    private IPreparedHook PrepareConfirmationBuilder(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SaveLoadConfirmationBuilderDelegate>(
            HookId.SaveLoadConfirmationBuilder,
            build,
            original => (node, slot) => boundary.Run(
                "MenuNodeSaveLoadSteam confirmation builder",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch);
                    ConfirmationScope? scope = null;
                    Exception? captureFailure = null;
                    if (instrument)
                    {
                        try
                        {
                            instrument = RunIfActive(epoch, () => scope = BeginScope((nuint)node, epoch));
                        }
                        catch (Exception exception)
                        {
                            captureFailure = exception;
                        }
                    }

                    try
                    {
                        original()(node, slot);
                    }
                    finally
                    {
                        if (scope is not null && ReferenceEquals(threadScope, scope))
                        {
                            threadScope = null;
                        }
                    }

                    if (scope is not null && captureFailure is null && instrument)
                    {
                        RunIfActive(epoch, () => CompleteCapture(scope));
                    }

                    if (captureFailure is not null && instrument)
                    {
                        RunIfActive(epoch, () => boundary.Run(
                            "MenuNodeSaveLoadSteam confirmation capture",
                            () => throw captureFailure));
                    }
                }));

    private IPreparedHook PrepareNodeDestructor(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SaveLoadNodeDestructorDelegate>(
            HookId.SaveLoadNodeDestructor,
            build,
            original => node => boundary.Run(
                "MenuNodeSaveLoadSteam destructor",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch);
                    if (instrument)
                    {
                        RunIfActive(epoch, () => boundary.Run(
                            "MenuNodeSaveLoadSteam destructor capture",
                            () => CloseConfirmation((nuint)node)));
                    }

                    original()(node);
                }));

    private ConfirmationScope BeginScope(nuint node, int epoch)
    {
        if (threadScope is not null)
        {
            throw new InvalidOperationException(
                "A save/load confirmation capture is already active on this thread.");
        }

        var scope = new ConfirmationScope(this, node, epoch);
        if (!TryReadExactVtable(node, SaveLoadNodeVtableRva))
        {
            scope.Errors.Add("Save/load confirmation builder ran on a node that is not the audited class.");
        }
        else if (!TryReadInt32(node + NodeModeOffset, out var mode))
        {
            scope.Errors.Add("Save/load confirmation node mode is unreadable.");
        }
        else if (!IsAuditedMode(mode))
        {
            scope.Errors.Add($"Save/load confirmation node mode {mode} has no audited prompt.");
        }
        else
        {
            scope.Mode = mode;
        }

        threadScope = scope;
        return scope;
    }

    private ConfirmationScope? GetOwnedScope() =>
        threadScope is { } scope && ReferenceEquals(scope.Owner, this) ? scope : null;

    private void StorePrompt(ConfirmationScope scope, int bank, int messageId, nint result, nint returned)
    {
        if (!TryReadLocalizedText(result, returned, out var text, out var error))
        {
            scope.Errors.Add(
                $"Save/load confirmation prompt ({bank:X},{messageId:X}) is unreadable or blank: {error}");
            return;
        }

        if (scope.Prompt is not null)
        {
            scope.Errors.Add("Save/load confirmation localized more than one prompt.");
            return;
        }

        scope.Prompt = text;
    }

    private void StoreChoice(ConfirmationScope scope, int choiceKey, nint result, nint returned)
    {
        if (!TryReadLocalizedText(result, returned, out var text, out var error))
        {
            scope.Errors.Add($"Save/load confirmation choice {choiceKey} is unreadable or blank: {error}");
            return;
        }

        if (!scope.ChoiceLabels.TryAdd(choiceKey, text))
        {
            scope.Errors.Add($"Save/load confirmation choice key {choiceKey} was localized more than once.");
        }
    }

    private bool TryReadLocalizedText(nint result, nint returned, out string text, out string error)
    {
        var address = returned != 0 ? (nuint)returned : (nuint)result;
        if (!stringReader.TryRead(address, out var read, out error) || string.IsNullOrWhiteSpace(read))
        {
            text = string.Empty;
            return false;
        }

        text = new string(read.AsSpan());
        error = string.Empty;
        return true;
    }

    private void CompleteCapture(ConfirmationScope scope)
    {
        if (scope.Errors.Count != 0)
        {
            dispatcher.ReportCoverageFailure(scope.Errors[0]);
            return;
        }

        if (scope.Prompt is null || scope.ChoiceLabels.Count != ChoiceCount ||
            !scope.ChoiceLabels.Keys.Order().SequenceEqual([0, 1]))
        {
            dispatcher.ReportCoverageFailure(
                "Save/load confirmation did not capture one prompt and localized choice keys 0 and 1.");
            return;
        }

        if (scope.Focus.Count == 0)
        {
            dispatcher.ReportCoverageFailure(
                "Save/load confirmation did not capture an authoritative focused manager.");
            return;
        }

        // The builder creates both choices and only then focuses their manager, so the final
        // audited focus assignment is the one that owns the transaction.
        var initialFocus = scope.Focus[^1];
        var manager = initialFocus.Manager;
        var bindings = scope.Bindings.Where(binding => binding.Manager == manager).ToArray();
        if (bindings.Length != ChoiceCount ||
            bindings.Select(binding => binding.Key).Distinct().Order().SequenceEqual([0, 1]) is false ||
            bindings.Any(binding => !scope.Controls.Contains(binding.Control)))
        {
            dispatcher.ReportCoverageFailure(
                $"Save/load confirmation manager correlated {bindings.Length} choice bindings with keys " +
                $"[{string.Join(",", bindings.Select(binding => binding.Key).Order())}], not exact keys 0 and 1.");
            return;
        }

        if (bindings.All(binding => binding.Key != initialFocus.Key))
        {
            dispatcher.ReportCoverageFailure(
                "Save/load confirmation did not capture one authoritative initial choice.");
            return;
        }

        var ordered = bindings.OrderBy(binding => binding.Key).ToArray();
        var choices = new ReadOnlyCollection<string>(ordered
            .Select(binding => new string(scope.ChoiceLabels[binding.Key].AsSpan()))
            .ToArray());
        var indexByKey = new ReadOnlyDictionary<int, int>(ordered
            .Select((binding, index) => (binding.Key, index))
            .ToDictionary(item => item.Key, item => item.index));
        var confirmation = new ActiveConfirmation(
            scope.Node,
            manager,
            new string(scope.Prompt.AsSpan()),
            choices,
            indexByKey);

        bool announceScreen;
        lock (stateGate)
        {
            if (scope.Epoch != activeEpoch)
            {
                dispatcher.ReportCoverageFailure(
                    "Save/load confirmation completed outside the active hook lifecycle.");
                return;
            }

            active = confirmation;
            announceScreen = !screenEntered;
            screenEntered = true;
        }

        if (announceScreen)
        {
            // The node owns the screen while it confirms. Without this the semantic state has no
            // active screen and silently drops every confirmation announcement, which is exactly
            // why Resume said nothing after the title menu exited.
            dispatcher.Publish(new ScreenEntered(ScreenKind.SaveLoad));
        }

        dispatcher.Publish(new ConfirmationOpened(
            confirmation.Prompt,
            confirmation.Choices,
            indexByKey[initialFocus.Key]));
    }

    private void CloseConfirmation(nuint node)
    {
        bool announceExit;
        lock (stateGate)
        {
            if (active is { } current && current.Node != node)
            {
                return;
            }

            active = null;
            announceExit = screenEntered;
            screenEntered = false;
        }

        if (announceExit)
        {
            dispatcher.Publish(new ScreenExited(ScreenKind.SaveLoad));
        }
    }

    /// <summary>The mode-to-prompt map read from the builder's own switch at RVA 0x21A23A.</summary>
    public static bool IsExpectedPromptMessage(int mode, int bank, int messageId) => mode switch
    {
        SaveMode or SaveOverwriteModeA or SaveOverwriteModeB =>
            (bank == StartTextFileId && messageId is 0x14 or 0x15) ||
            (bank == OpeBankId && messageId == OpeSavePromptId),
        LoadMode => bank == StartTextFileId && messageId == 0x1B,
        BookmarkAndQuitMode => bank == StartTextFileId && messageId == 0x26,
        ResumeMode => bank == StartTextFileId && messageId == 0x2B,
        _ => false,
    };

    public static bool IsAuditedMode(int mode) => mode is SaveMode or LoadMode or
        BookmarkAndQuitMode or ResumeMode or SaveOverwriteModeA or SaveOverwriteModeB;

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
            throw new InvalidOperationException(
                "Verified image base is unavailable for save/load confirmation hooks.");
        }

        if (imageBase == 0)
        {
            imageBase = build.ImageBaseAddress;
        }
        else if (imageBase != build.ImageBaseAddress)
        {
            throw new InvalidOperationException(
                "Save/load confirmation hooks received conflicting image bases.");
        }
    }

    private bool TryCaptureActiveEpoch(out int epoch)
    {
        lock (lifecycleGate)
        {
            epoch = activeEpoch;
            return hooksActive;
        }
    }

    private bool RunIfActive(int epoch, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (lifecycleGate)
        {
            if (!hooksActive || activeEpoch != epoch)
            {
                return false;
            }

            action();
            return true;
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

    private bool TryReadExactVtable(nuint instance, uint expectedRva) =>
        instance != 0 && TryReadPointer(instance, out var vtable) && vtable == imageBase + expectedRva;

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

    private static string FormatException(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";

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

    private sealed record ChoiceBinding(nuint Manager, nuint Control, int Key);

    private sealed record ChoiceFocus(nuint Manager, int Key);

    private sealed class ConfirmationScope(SaveLoadConfirmationHookSet owner, nuint node, int epoch)
    {
        public SaveLoadConfirmationHookSet Owner { get; } = owner;
        public nuint Node { get; } = node;
        public int Epoch { get; } = epoch;
        public int Mode { get; set; } = -1;
        public string? Prompt { get; set; }
        public HashSet<nuint> Controls { get; } = [];
        public Dictionary<int, string> ChoiceLabels { get; } = [];
        public List<ChoiceBinding> Bindings { get; } = [];
        public List<ChoiceFocus> Focus { get; } = [];
        public List<string> Errors { get; } = [];
    }

    private sealed record ActiveConfirmation(
        nuint Node,
        nuint Manager,
        string Prompt,
        IReadOnlyList<string> Choices,
        IReadOnlyDictionary<int, int> IndexByKey);
}
