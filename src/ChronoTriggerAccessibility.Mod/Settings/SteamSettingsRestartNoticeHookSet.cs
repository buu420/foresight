using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Menus;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Settings;

/// <summary>
/// Speaks the message Steam Settings shows before it closes the game. When the screen mode or
/// size no longer matches the saved one, the category callback 0x1F0000 and 0x1F0280 disable the
/// settings input and call 0x1F52C0 on <c>MenuNodeConfigSteam</c>. It resolves (0x3A, 1) from
/// msg/resolution.txt (bank map recovered from the text loader 0x1B7A30), draws it one
/// font-0x0C label per 0x5C-separated line through 0x2B06B0 and builds the shared one-control
/// window 0x23D520. That window's callback 0x1FC240 calls <c>Director::end</c> on confirm or
/// cancel, so the next key press quits the game. Nothing spoke it: it is not one of the audited
/// Steam Settings overlays. Evidence: artifacts/research/engine-0332/claude-ui-report.md.
/// </summary>
public sealed class SteamSettingsRestartNoticeHookSet : IHookActivationObserver, ISharedNativeHookObserver
{
    /// <summary>nsMenu::MenuNodeConfigSteam, installed by its constructor at 0x1ECB30.</summary>
    public const uint NodeVtableRva = 0x3A702C;
    public const uint ManagerVtableRva = 0x3A5D0C;
    public const uint CustomButtonVtableRva = 0x3A4364;
    public const uint FocusableStateVtableRva = 0x3AC3F4;
    public const uint ManagerFocusKeyOffset = 0x2C4;
    public const uint FocusableStateControlOffset = 0x14;

    /// <summary>The font size 0x2B06B0 passes for every line (0x2B07CD).</summary>
    public const int LineFontSize = 0x0C;
    public const int MaximumLineCount = 8;

    /// <summary>msg/resolution.txt and the message 0x1F5403 resolves from it.</summary>
    public const int ResolutionTextBank = 0x3A;
    public const int RestartMessageId = 1;

    public const string OwnerSource = "SteamSettingsRestartNotice";

    [ThreadStatic]
    private static NoticeScope? threadScope;

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly MsvcStringReader stringReader;
    private readonly object lifecycleGate = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private int activeEpoch = 1;
    private bool hooksActive = true;

    public SteamSettingsRestartNoticeHookSet(
        IRuntimeNativeHookFactory hookFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher)
    {
        this.hookFactory = hookFactory ?? throw new ArgumentNullException(nameof(hookFactory));
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        stringReader = new MsvcStringReader(memory);
        RequiredHookIds = new ReadOnlyCollection<HookId>([HookId.SteamSettingsRestartNotice]);
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            new HookRegistration(GameVersionCatalog.Get(HookId.SteamSettingsRestartNotice).Symbol, PrepareNotice),
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
                    "Steam Settings restart notice hooks were disabled before activation completed.");
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
    }

    /// <summary>0x1F5403 calls the resolver directly. A resolution nested inside getMsg is some
    /// other text's processing, never this notice.</summary>
    public void AfterOpeTextResolver(nint resolver, nint result, int bank, int messageId, nint returned)
    {
        _ = resolver;
        try
        {
            if (SharedNativeHookFanoutFactory.IsTextManagerGetMsgActive || !TryCaptureActiveEpoch(out _) ||
                GetOwnedScope() is not { } scope || bank != ResolutionTextBank || messageId != RestartMessageId)
            {
                return;
            }

            if (!stringReader.TryRead(returned != 0 ? (nuint)returned : (nuint)result, out var text, out var error))
            {
                scope.Errors.Add($"Steam Settings restart notice text is unreadable: {error}");
            }
            else if (scope.Text is not null)
            {
                scope.Errors.Add("Steam Settings restart notice resolved its message more than once.");
            }
            else
            {
                scope.Text = new string(text.AsSpan());
            }
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice text capture failed: {FormatException(exception)}");
        }
    }

    public void AfterMenuTextLabelFactory(nint position, nint text, nint anchor, int fontSize, nint returned)
    {
        _ = position;
        _ = anchor;
        try
        {
            if (!TryCaptureActiveEpoch(out _) || GetOwnedScope() is not { } scope)
            {
                return;
            }

            if (fontSize != LineFontSize || returned == 0)
            {
                scope.Errors.Add(
                    $"Steam Settings restart notice label used font size {fontSize} or returned no node; the audited lines use {LineFontSize}.");
                return;
            }

            if (!stringReader.TryRead((nuint)text, out var line, out var error))
            {
                scope.Errors.Add($"Steam Settings restart notice label text is unreadable: {error}");
                return;
            }

            if (scope.Controls.Count != 0)
            {
                scope.Errors.Add("Steam Settings restart notice drew a label after its window control.");
                return;
            }

            scope.Lines.Add(new string(line.AsSpan()));
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice label capture failed: {FormatException(exception)}");
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
                scope.Errors.Add("Steam Settings restart notice CustomButton did not return exact typed ECX storage.");
            }
            else if (!scope.Controls.Add(control))
            {
                scope.Errors.Add("Steam Settings restart notice constructed the same control more than once.");
            }
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice control capture failed: {FormatException(exception)}");
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
                    $"Steam Settings restart notice binder key {managerKey} does not match the audited manager/control layout.");
                return;
            }

            scope.Bindings.Add(new WindowBinding(managerPointer, control, managerKey));
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice binder capture failed: {FormatException(exception)}");
        }
    }

    public void AfterFocusSet(nint manager, int managerKey)
    {
        try
        {
            if (!TryCaptureActiveEpoch(out _) || GetOwnedScope() is not { } scope)
            {
                return;
            }

            var managerPointer = (nuint)manager;
            if (!TryReadExactVtable(managerPointer, ManagerVtableRva) ||
                !TryReadInt32(managerPointer + ManagerFocusKeyOffset, out var committedKey) ||
                committedKey != managerKey)
            {
                scope.Errors.Add("Steam Settings restart notice focus did not commit to the audited manager state.");
                return;
            }

            scope.Focus.Add(new WindowFocus(managerPointer, managerKey));
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice focus capture failed: {FormatException(exception)}");
        }
    }

    private IPreparedHook PrepareNotice(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.ImageBaseAddress == 0)
        {
            throw new InvalidOperationException("Verified image base is unavailable for the Steam Settings restart notice.");
        }

        if (imageBase != 0 && imageBase != build.ImageBaseAddress)
        {
            throw new InvalidOperationException("The Steam Settings restart notice received conflicting image bases.");
        }

        imageBase = build.ImageBaseAddress;
        if (!build.HookAddresses.TryGetValue(HookId.SteamSettingsRestartNotice, out var address))
        {
            throw new InvalidOperationException(
                $"Verified address for required hook '{HookId.SteamSettingsRestartNotice}' is missing.");
        }

        SteamSettingsRestartNoticeDelegate? original = null;
        SteamSettingsRestartNoticeDelegate GetOriginal() => original ?? throw new InvalidOperationException(
            "Original function for the Steam Settings restart notice is not bound.");
        SteamSettingsRestartNoticeDelegate detour = (node, backdropParent) => boundary.Run(
            "MenuNodeConfigSteam restart notice",
            () =>
            {
                var instrument = TryCaptureActiveEpoch(out var epoch);
                NoticeScope? scope = null;
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
                    GetOriginal()(node, backdropParent);
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
                        "MenuNodeConfigSteam restart notice capture",
                        () => throw captureFailure));
                }
            });
        var hook = hookFactory.CreateHook(HookId.SteamSettingsRestartNotice, detour, address);
        original = hook.OriginalFunction;
        return new ReloadedPreparedHook<SteamSettingsRestartNoticeDelegate>(
            GameVersionCatalog.Get(HookId.SteamSettingsRestartNotice).Symbol, hook, detour);
    }

    private NoticeScope BeginScope(nuint node, int epoch)
    {
        if (threadScope is not null)
        {
            throw new InvalidOperationException("A Steam Settings restart notice capture is already active on this thread.");
        }

        var scope = new NoticeScope(this, node, epoch);
        if (!TryReadExactVtable(node, NodeVtableRva))
        {
            scope.Errors.Add("Steam Settings restart notice ran on a node that is not the audited MenuNodeConfigSteam.");
        }

        threadScope = scope;
        return scope;
    }

    private NoticeScope? GetOwnedScope() =>
        threadScope is { } scope && ReferenceEquals(scope.Owner, this) ? scope : null;

    private void CompleteCapture(NoticeScope scope)
    {
        if (scope.Errors.Count != 0)
        {
            dispatcher.ReportCoverageFailure(scope.Errors[0]);
            return;
        }

        if (scope.Text is not { } text)
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice did not resolve ({ResolutionTextBank:X},{RestartMessageId:X}).");
            return;
        }

        var expected = NativeTextLines.Split(text);
        if (expected.Count > MaximumLineCount || !scope.Lines.SequenceEqual(expected, StringComparer.Ordinal))
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice drew {scope.Lines.Count} label(s) that do not match its {expected.Count} line(s) in order.");
            return;
        }

        var visible = NativeTextLines.Visible(scope.Lines);
        if (visible.Count == 0)
        {
            dispatcher.ReportCoverageFailure("Steam Settings restart notice drew no visible text.");
            return;
        }

        if (scope.Controls.Count != 1 || scope.Focus.Count == 0)
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice constructed {scope.Controls.Count} controls and " +
                $"{scope.Focus.Count} focus assignments, not its one focused control.");
            return;
        }

        var focus = scope.Focus[^1];
        var bindings = scope.Bindings.Where(binding => binding.Manager == focus.Manager).ToArray();
        if (bindings.Length != 1 || bindings[0].Key != 0 || focus.Key != 0 ||
            !scope.Controls.Contains(bindings[0].Control))
        {
            dispatcher.ReportCoverageFailure(
                $"Steam Settings restart notice correlated {bindings.Length} binding(s) and focus key {focus.Key}, " +
                "not its one control at key 0.");
            return;
        }

        lock (lifecycleGate)
        {
            if (scope.Epoch != activeEpoch)
            {
                dispatcher.ReportCoverageFailure("Steam Settings restart notice completed outside the active hook lifecycle.");
                return;
            }
        }

        dispatcher.Publish(new MenuNoticePresented(new MenuOwner(OwnerSource, (ulong)scope.Node), visible));
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
        if (address == 0 || !memory.TryRead(address, bytes))
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
        if (address == 0 || !memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
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

    private sealed record WindowBinding(nuint Manager, nuint Control, int Key);

    private sealed record WindowFocus(nuint Manager, int Key);

    private sealed class NoticeScope(SteamSettingsRestartNoticeHookSet owner, nuint node, int epoch)
    {
        public SteamSettingsRestartNoticeHookSet Owner { get; } = owner;
        public nuint Node { get; } = node;
        public int Epoch { get; } = epoch;
        public string? Text { get; set; }
        public List<string> Lines { get; } = [];
        public HashSet<nuint> Controls { get; } = [];
        public List<WindowBinding> Bindings { get; } = [];
        public List<WindowFocus> Focus { get; } = [];
        public List<string> Errors { get; } = [];
    }
}
