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

namespace ChronoTriggerAccessibility.Mod.Extras;

/// <summary>
/// Speaks every window <c>SaveEndingResultScene</c> (scene 0x1B) shows after an ending: the
/// result messages, the clear-data Yes/No questions and the saving notices. Evidence:
/// artifacts/research/engine-0332 (claude-ui-report.md and the saveending/ending-dialogs files).
/// <para>The ending script's opcode handler at 0x15C800 calls <c>SceneManager::NextScene(4)</c>
/// from <c>WorldScene</c>, which creates scene 0x1B (factory 0x297860, creator 0x2B0980). Init
/// 0x2B0B90 marks the game cleared and 0x2B0C80 picks the first message. Every message goes
/// through 0x2B1390, which renders one font-0x0C label per line (0x2B06B0 → 0x2400B0, lines split
/// on 0x5C by 0x40FC80) and then the shared one-control window 0x23D520: the first-clear message
/// 0x2B0CF0, the ending result 0x2B0F30, the Dreamseeker message 0x2B1150 and "Save failed."
/// from 0x2B1B40. After the result, 0x2B1620 asks "Save game completion data?", 0x2B1A70
/// "Overwrite existing save data?" and 0x2B19A0 "Return to the title screen?", each through the
/// Yes/No builder 0x2B2200 (prompt through 0x2B06B0, two buttons through 0x2B25C0, focus key 1).
/// A successful overwrite opens the gate 0x2B1E20, whose callback draws "Saving data." (0x2B1F10)
/// and half a second later "Save complete." (0x2B2020) before <c>NextScene(1)</c>.</para>
/// <para>None of these is a MsgWindow or an nsMenu page, and the scene has no other owner.
/// Every spoken string is text the game itself just rendered; the message it was given is used
/// only to prove the rendered labels are the whole message, in order.</para>
/// </summary>
public sealed class EndingResultHookSet : IHookActivationObserver, ISharedNativeHookObserver
{
    public const uint SceneVtableRva = 0x3B34D8;
    public const uint ManagerVtableRva = 0x3A5D0C;
    public const uint CustomButtonVtableRva = 0x3A4364;
    public const uint FocusableStateVtableRva = 0x3AC3F4;
    public const uint ManagerFocusKeyOffset = 0x2C4;
    public const uint FocusableStateControlOffset = 0x14;

    /// <summary>The content node init creates and stores at scene + 0x29C (0x2B0BE3); the Yes/No
    /// windows and the save gate are its children.</summary>
    public const uint SceneContainerOffset = 0x29C;

    /// <summary>cocos2d <c>Node::_parent</c>, audited in docs/field-save-slots-native-audit.md.</summary>
    public const uint NodeParentOffset = 0x16C;

    /// <summary>The font size 0x2B06B0 (0x2B07CD) and 0x2B25C0 (0x2B29A3) pass for every line and caption.</summary>
    public const int LineFontSize = 0x0C;

    /// <summary>The byte the native splitter 0x40FC80 breaks lines on.</summary>
    public const char LineSeparator = NativeTextLines.Separator;

    /// <summary>A sanity bound for the 400x135 message area 0x2B06B0 lays lines out in.</summary>
    public const int MaximumLineCount = 8;

    /// <summary>Localize/en/msg/start.txt: Yes and No for the Yes/No window, and the notices.</summary>
    public const int StartTextFileId = 0x41;
    public const int FirstChoiceMessageId = 0x11;
    public const int ChoiceCount = 2;
    public const int SavingMessageId = 0x43;
    public const int SaveCompleteMessageId = 0x44;

    public const string OwnerSource = "EndingResult";

    private static readonly HookId[] DedicatedHookIds =
    [
        HookId.EndingResultDialogBuilder,
        HookId.SaveEndingResultSceneDestructor,
        HookId.EndingConfirmationBuilder,
        HookId.EndingSavingNotice,
        HookId.EndingSaveCompleteNotice,
    ];

    [ThreadStatic]
    private static CaptureScope? threadScope;

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly MsvcStringReader stringReader;
    private readonly object lifecycleGate = new();
    private readonly object stateGate = new();
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private PresentedScene? active;
    private ActiveConfirmation? confirmation;
    private int activeEpoch = 1;
    private bool hooksActive = true;

    public EndingResultHookSet(
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
            CreateRegistration(HookId.EndingResultDialogBuilder, PrepareMessageBuilder),
            CreateRegistration(HookId.SaveEndingResultSceneDestructor, PrepareSceneDestructor),
            CreateRegistration(HookId.EndingConfirmationBuilder, PrepareConfirmationBuilder),
            CreateRegistration(HookId.EndingSavingNotice, PrepareSavingNotice),
            CreateRegistration(HookId.EndingSaveCompleteNotice, PrepareSaveCompleteNotice),
        ]);
    }

    public IReadOnlyList<HookId> RequiredHookIds { get; }

    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    /// <summary>The scene whose windows currently own narration, if any.</summary>
    public nuint ActiveScene
    {
        get
        {
            lock (stateGate)
            {
                return active?.Scene ?? 0;
            }
        }
    }

    public void AfterHooksActivated()
    {
        lock (lifecycleGate)
        {
            if (!hooksActive)
            {
                throw new InvalidOperationException(
                    "Ending result hooks were disabled before activation completed.");
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
            confirmation = null;
        }
    }

    /// <summary>Yes and No for the Yes/No window, and the text of the saving notices, arrive
    /// through TextManager::getMsg on file 0x41 inside the hooked functions.</summary>
    public void AfterTextManagerGetMsg(nint textManager, nint result, int fileId, int messageId, nint returned)
    {
        _ = textManager;
        try
        {
            if (!TryCaptureActiveEpoch(out _) || GetOwnedScope() is not { } scope || fileId != StartTextFileId)
            {
                return;
            }

            var choice = messageId - FirstChoiceMessageId;
            if (scope.Kind == CaptureKind.Confirmation && choice is >= 0 and < ChoiceCount)
            {
                if (!TryReadText(returned != 0 ? (nuint)returned : (nuint)result, out var text, out var error))
                {
                    scope.Errors.Add($"Ending Yes/No choice {choice} text is unreadable: {error}");
                }
                else if (!scope.LocalizedChoices.TryAdd(choice, text))
                {
                    scope.Errors.Add($"Ending Yes/No choice {choice} was localized more than once.");
                }
            }
            else if (scope.Kind == CaptureKind.Notice && messageId == scope.NoticeMessageId)
            {
                if (!TryReadText(returned != 0 ? (nuint)returned : (nuint)result, out var text, out var error))
                {
                    scope.Errors.Add($"Ending notice ({fileId:X},{messageId:X}) is unreadable: {error}");
                }
                else if (scope.Expected is not null)
                {
                    scope.Errors.Add("Ending notice localized its message more than once.");
                }
                else
                {
                    scope.Expected = text;
                }
            }
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Ending localized-text capture failed: {FormatException(exception)}");
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
                    $"Ending window label used font size {fontSize} or returned no node; the audited labels use {LineFontSize}.");
                return;
            }

            if (!TryReadText((nuint)text, out var line, out var error))
            {
                scope.Errors.Add($"Ending window label text is unreadable: {error}");
                return;
            }

            // Labels drawn before the first control are message lines; the Yes/No builder
            // draws each button's caption right after constructing that button.
            scope.Labels.Add(new CapturedLabel(line, scope.Controls.Count - 1));
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Ending window label capture failed: {FormatException(exception)}");
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
                scope.Errors.Add("Ending window CustomButton did not return exact typed ECX storage.");
            }
            else if (scope.Controls.Contains(control))
            {
                scope.Errors.Add("Ending window constructed the same control more than once.");
            }
            else
            {
                scope.Controls.Add(control);
            }
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Ending window control capture failed: {FormatException(exception)}");
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
                    $"Ending window binder key {managerKey} does not match the audited manager/control layout.");
                return;
            }

            scope.Bindings.Add(new WindowBinding(managerPointer, control, managerKey));
        }
        catch (Exception exception)
        {
            dispatcher.ReportCoverageFailure(
                $"Ending window binder capture failed: {FormatException(exception)}");
        }
    }

    public void AfterFocusSet(nint manager, int managerKey)
    {
        if (!TryCaptureActiveEpoch(out var epoch)) return;
        try
        {
            var managerPointer = (nuint)manager;
            if (GetOwnedScope() is { } scope)
            {
                if (!TryReadExactVtable(managerPointer, ManagerVtableRva) ||
                    !TryReadInt32(managerPointer + ManagerFocusKeyOffset, out var committedKey) ||
                    committedKey != managerKey)
                {
                    scope.Errors.Add("Ending window focus did not commit to the audited manager state.");
                    return;
                }

                scope.Focus.Add(new WindowFocus(managerPointer, managerKey));
                return;
            }

            ObserveLiveFocus(managerPointer, managerKey, epoch);
        }
        catch (Exception exception)
        {
            RunIfActive(epoch, () => dispatcher.ReportCoverageFailure(
                $"Ending window focus capture failed: {FormatException(exception)}"));
        }
    }

    /// <summary>Splits text exactly as 0x40FC80 does before 0x2B06B0 draws it.</summary>
    public static IReadOnlyList<string> SplitLines(string text) => NativeTextLines.Split(text);

    /// <summary>The drawn lines that are not blank; blank ones are spacing.</summary>
    public static IReadOnlyList<string> VisibleLines(IEnumerable<string> lines) => NativeTextLines.Visible(lines);

    private void ObserveLiveFocus(nuint manager, int managerKey, int epoch)
    {
        ActiveConfirmation current;
        lock (stateGate)
        {
            if (confirmation is not { } open || open.Manager != manager)
            {
                return;
            }

            current = open;
        }

        // Choosing removes the window (0x2B3060 and 0x2B2EE0 call removeFromParent before the
        // callback), and a freed manager address can be reused by the next screen's manager.
        // Only a manager still inside this window, inside this scene's container, is followed.
        if (!IsLive(current))
        {
            lock (stateGate)
            {
                if (ReferenceEquals(confirmation, current))
                {
                    confirmation = null;
                }
            }

            return;
        }

        RunIfActive(epoch, () =>
        {
            // Unload may retire the reader while the native liveness reads run.
            // Serialize publication with disable, just as scoped builders do.
            lock (stateGate)
            {
                if (!ReferenceEquals(confirmation, current)) return;
            }

            if (!TryReadInt32(manager + ManagerFocusKeyOffset, out var committedKey) || committedKey != managerKey)
            {
                dispatcher.ReportCoverageFailure("Ending Yes/No focus did not commit to the audited manager state.");
                return;
            }

            if (!current.IndexByKey.TryGetValue(managerKey, out var index))
            {
                dispatcher.ReportCoverageFailure(
                    $"Ending Yes/No focus key {managerKey} has no captured rendered choice.");
                return;
            }

            dispatcher.Publish(new MenuConfirmationFocused(current.Choices[index], index, current.Choices.Count));
        });
    }

    private bool IsLive(ActiveConfirmation open) =>
        TryReadExactVtable(open.Manager, ManagerVtableRva) &&
        TryReadPointer(open.Manager + NodeParentOffset, out var window) && window == open.Window &&
        TryReadPointer(open.Window + NodeParentOffset, out var container) && container == open.Container &&
        TryReadExactVtable(open.Scene, SceneVtableRva) &&
        TryReadPointer(open.Scene + SceneContainerOffset, out var sceneContainer) && sceneContainer == open.Container;

    private IPreparedHook PrepareMessageBuilder(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingResultDialogBuilderDelegate>(
            HookId.EndingResultDialogBuilder,
            build,
            original => (scene, text, continuation) => RunScoped(
                boundary,
                "SaveEndingResultScene message window",
                epoch => BeginMessageScope((nuint)scene, (nuint)text, epoch),
                () => original()(scene, text, continuation),
                CompleteMessage));

    private IPreparedHook PrepareConfirmationBuilder(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingConfirmationBuilderDelegate>(
            HookId.EndingConfirmationBuilder,
            build,
            original => (scene, container, prompt, callback) => RunScoped(
                boundary,
                "SaveEndingResultScene Yes/No window",
                epoch => BeginConfirmationScope((nuint)scene, (nuint)container, (nuint)prompt, epoch),
                () => original()(scene, container, prompt, callback),
                CompleteConfirmation));

    private IPreparedHook PrepareSavingNotice(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingSavingNoticeDelegate>(
            HookId.EndingSavingNotice,
            build,
            original => (capture, manager, close) => RunScoped(
                boundary,
                "SaveEndingResultScene saving notice",
                epoch => BeginNoticeScope((nuint)capture, gateOffset: 0, sceneOffset: 4, SavingMessageId, epoch),
                () => original()(capture, manager, close),
                CompleteNotice));

    private IPreparedHook PrepareSaveCompleteNotice(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<EndingSaveCompleteNoticeDelegate>(
            HookId.EndingSaveCompleteNotice,
            build,
            original => capture => RunScoped(
                boundary,
                "SaveEndingResultScene save complete notice",
                epoch => BeginNoticeScope((nuint)capture, gateOffset: 4, sceneOffset: 8, SaveCompleteMessageId, epoch),
                () => original()(capture),
                CompleteNotice));

    private IPreparedHook PrepareSceneDestructor(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary) =>
        PrepareHook<SaveEndingResultSceneDestructorDelegate>(
            HookId.SaveEndingResultSceneDestructor,
            build,
            original => (scene, deletingFlags) => boundary.Run(
                "SaveEndingResultScene deleting destructor",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch);
                    if (instrument)
                    {
                        RunIfActive(epoch, () => boundary.Run(
                            "SaveEndingResultScene destructor capture",
                            () => CloseScene((nuint)scene)));
                    }

                    return original()(scene, deletingFlags);
                },
                fallback: (nint)0));

    /// <summary>Opens one capture scope around one native call, always runs the game's
    /// function, and completes or reports the capture only while this hook lifecycle lasts.</summary>
    private void RunScoped(
        UnmanagedBoundaryGuard boundary,
        string name,
        Func<int, CaptureScope> begin,
        Action runOriginal,
        Action<CaptureScope> complete) => boundary.Run(
        name,
        () =>
        {
            var instrument = TryCaptureActiveEpoch(out var epoch);
            CaptureScope? scope = null;
            Exception? captureFailure = null;
            if (instrument)
            {
                try
                {
                    instrument = RunIfActive(epoch, () => scope = begin(epoch));
                }
                catch (Exception exception)
                {
                    captureFailure = exception;
                }
            }

            try
            {
                runOriginal();
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
                RunIfActive(epoch, () => complete(scope));
            }

            if (captureFailure is not null && instrument)
            {
                RunIfActive(epoch, () => boundary.Run($"{name} capture", () => throw captureFailure));
            }
        });

    private CaptureScope BeginMessageScope(nuint scene, nuint text, int epoch)
    {
        var scope = OpenScope(CaptureKind.Message, scene, epoch);
        if (!TryReadExactVtable(scene, SceneVtableRva))
        {
            scope.Errors.Add("Ending message window ran on an object that is not the audited SaveEndingResultScene.");
        }
        else if (!TryReadText(text, out var composed, out var error))
        {
            scope.Errors.Add($"Ending message text is unreadable: {error}");
        }
        else
        {
            scope.Expected = composed;
        }

        return scope;
    }

    private CaptureScope BeginConfirmationScope(nuint scene, nuint container, nuint prompt, int epoch)
    {
        var scope = OpenScope(CaptureKind.Confirmation, scene, epoch);
        scope.Container = container;
        if (!TryReadExactVtable(scene, SceneVtableRva))
        {
            scope.Errors.Add("Ending Yes/No window ran on an object that is not the audited SaveEndingResultScene.");
        }
        else if (!TryReadPointer(scene + SceneContainerOffset, out var sceneContainer) ||
                 sceneContainer == 0 || sceneContainer != container)
        {
            scope.Errors.Add("Ending Yes/No window was not parented to its scene's audited container.");
        }
        else if (!TryReadText(prompt, out var text, out var error))
        {
            scope.Errors.Add($"Ending Yes/No prompt is unreadable: {error}");
        }
        else
        {
            scope.Expected = text;
        }

        return scope;
    }

    private CaptureScope BeginNoticeScope(nuint capture, uint gateOffset, uint sceneOffset, int messageId, int epoch)
    {
        // The scene is read from the lambda capture before the scope opens, so a capture that
        // is not the audited layout is reported against no scene rather than a guessed one.
        var hasScene = TryReadPointer(capture + sceneOffset, out var scene);
        var scope = OpenScope(CaptureKind.Notice, hasScene ? scene : 0, epoch);
        scope.NoticeMessageId = messageId;
        if (!hasScene || !TryReadExactVtable(scene, SceneVtableRva))
        {
            scope.Errors.Add("Ending notice capture does not name the audited SaveEndingResultScene.");
        }
        else if (!TryReadPointer(capture + gateOffset, out var gate) || gate == 0 ||
                 !TryReadPointer(gate + NodeParentOffset, out var gateParent) ||
                 !TryReadPointer(scene + SceneContainerOffset, out var container) ||
                 container == 0 || gateParent != container)
        {
            scope.Errors.Add("Ending notice gate is not a child of its scene's audited container.");
        }

        return scope;
    }

    private CaptureScope OpenScope(CaptureKind kind, nuint scene, int epoch)
    {
        if (threadScope is not null)
        {
            throw new InvalidOperationException("An ending window capture is already active on this thread.");
        }

        var scope = new CaptureScope(this, kind, scene, epoch);
        threadScope = scope;
        return scope;
    }

    private CaptureScope? GetOwnedScope() =>
        threadScope is { } scope && ReferenceEquals(scope.Owner, this) ? scope : null;

    private void CompleteMessage(CaptureScope scope)
    {
        if (!TryValidateRenderedText(scope, "Ending message", out var visible))
        {
            return;
        }

        if (scope.Labels.Any(label => label.Control >= 0))
        {
            dispatcher.ReportCoverageFailure("Ending message window drew a label after its control.");
            return;
        }

        if (scope.Controls.Count != 1)
        {
            dispatcher.ReportCoverageFailure(
                $"Ending message window constructed {scope.Controls.Count} controls, not exactly one.");
            return;
        }

        if (scope.Focus.Count == 0)
        {
            dispatcher.ReportCoverageFailure("Ending message window did not capture an authoritative focus.");
            return;
        }

        // The window factory binds its one control and only then focuses the manager, so the
        // final audited focus assignment names the manager that owns the window.
        var focus = scope.Focus[^1];
        var bindings = scope.Bindings.Where(binding => binding.Manager == focus.Manager).ToArray();
        if (bindings.Length != 1 || bindings[0].Key != 0 || focus.Key != 0 ||
            !scope.Controls.Contains(bindings[0].Control))
        {
            dispatcher.ReportCoverageFailure(
                $"Ending message window correlated {bindings.Length} binding(s) and focus key {focus.Key}, " +
                "not its one control at key 0.");
            return;
        }

        Present(scope, null, owner => new MenuNoticePresented(owner, visible));
    }

    private void CompleteConfirmation(CaptureScope scope)
    {
        if (!TryValidateRenderedText(scope, "Ending Yes/No prompt", out var visible))
        {
            return;
        }

        if (scope.Controls.Count != ChoiceCount)
        {
            dispatcher.ReportCoverageFailure(
                $"Ending Yes/No window constructed {scope.Controls.Count} controls, not {ChoiceCount}.");
            return;
        }

        var choices = new string[ChoiceCount];
        for (var index = 0; index < ChoiceCount; index++)
        {
            var captions = scope.Labels.Where(label => label.Control == index).ToArray();
            if (captions.Length != 1 || !scope.LocalizedChoices.TryGetValue(index, out var localized) ||
                !string.Equals(captions[0].Text, localized, StringComparison.Ordinal) ||
                captions[0].Text.Trim().Length == 0)
            {
                dispatcher.ReportCoverageFailure(
                    $"Ending Yes/No button {index} did not draw exactly its one localized caption.");
                return;
            }

            choices[index] = captions[0].Text.Trim();
        }

        if (scope.Focus.Count == 0)
        {
            dispatcher.ReportCoverageFailure("Ending Yes/No window did not capture an authoritative focus.");
            return;
        }

        var focus = scope.Focus[^1];
        var bindings = scope.Bindings.Where(binding => binding.Manager == focus.Manager).ToArray();
        if (bindings.Length != ChoiceCount ||
            bindings.Select(binding => binding.Key).Distinct().Count() != ChoiceCount ||
            bindings.Select(binding => binding.Control).Distinct().Count() != ChoiceCount ||
            bindings.Any(binding => !scope.Controls.Contains(binding.Control)))
        {
            dispatcher.ReportCoverageFailure(
                $"Ending Yes/No manager correlated {bindings.Length} binding(s), not one per button.");
            return;
        }

        var indexByKey = new ReadOnlyDictionary<int, int>(bindings.ToDictionary(
            binding => binding.Key,
            binding => scope.Controls.IndexOf(binding.Control)));
        if (!indexByKey.TryGetValue(focus.Key, out var selected))
        {
            dispatcher.ReportCoverageFailure("Ending Yes/No window focused a key none of its buttons is bound to.");
            return;
        }

        // 0x2B2200 adds the manager to the window it created last (0x2B239F), and the window to
        // the container it was given (0x2B224B): the pair live focus is later checked against.
        if (!TryReadPointer(focus.Manager + NodeParentOffset, out var window) || window == 0 ||
            !TryReadPointer(window + NodeParentOffset, out var windowParent) || windowParent != scope.Container)
        {
            dispatcher.ReportCoverageFailure("Ending Yes/No manager is not inside a window of its scene's container.");
            return;
        }

        var prompt = string.Join(" ", visible);
        var readOnlyChoices = new ReadOnlyCollection<string>(choices);
        var open = new ActiveConfirmation(scope.Scene, scope.Container, window, focus.Manager, readOnlyChoices, indexByKey);
        Present(scope, open, owner => new MenuConfirmationPresented(owner, prompt, readOnlyChoices, selected));
    }

    private void CompleteNotice(CaptureScope scope)
    {
        if (scope.Errors.Count == 0 && scope.Expected is null)
        {
            dispatcher.ReportCoverageFailure(
                $"Ending notice did not localize its message ({StartTextFileId:X},{scope.NoticeMessageId:X}).");
            return;
        }

        if (!TryValidateRenderedText(scope, "Ending notice", out var visible))
        {
            return;
        }

        if (scope.Controls.Count != 0 || scope.Bindings.Count != 0)
        {
            dispatcher.ReportCoverageFailure("Ending notice constructed controls; the audited notice only draws text.");
            return;
        }

        Present(scope, null, owner => new MenuNoticePresented(owner, visible));
    }

    /// <summary>Checks the captured labels drawn before any control against the text the
    /// window was given, split exactly as the native splitter splits it.</summary>
    private bool TryValidateRenderedText(CaptureScope scope, string what, out IReadOnlyList<string> visible)
    {
        visible = [];
        if (scope.Errors.Count != 0)
        {
            dispatcher.ReportCoverageFailure(scope.Errors[0]);
            return false;
        }

        if (scope.Expected is not { } expectedText)
        {
            dispatcher.ReportCoverageFailure($"{what} has no text to validate its labels against.");
            return false;
        }

        var expected = SplitLines(expectedText);
        if (expected.Count > MaximumLineCount)
        {
            dispatcher.ReportCoverageFailure(
                $"{what} has {expected.Count} lines; at most {MaximumLineCount} fit the audited window.");
            return false;
        }

        var rendered = scope.Labels.Where(label => label.Control < 0).Select(label => label.Text).ToArray();
        if (!rendered.SequenceEqual(expected, StringComparer.Ordinal))
        {
            dispatcher.ReportCoverageFailure(
                $"{what} drew {rendered.Length} label(s) that do not match its {expected.Count} line(s) in order.");
            return false;
        }

        visible = VisibleLines(rendered);
        if (visible.Count == 0)
        {
            dispatcher.ReportCoverageFailure($"{what} drew no visible text.");
            return false;
        }

        return true;
    }

    private void Present(
        CaptureScope scope,
        ActiveConfirmation? openConfirmation,
        Func<MenuOwner, MenuAccessibilityEvent> presentation)
    {
        var owner = new MenuOwner(OwnerSource, (ulong)scope.Scene);
        lock (stateGate)
        {
            if (scope.Epoch != activeEpoch)
            {
                dispatcher.ReportCoverageFailure("Ending window completed outside the active hook lifecycle.");
                return;
            }

            active = new PresentedScene(scope.Scene, owner);
            confirmation = openConfirmation;
        }

        dispatcher.Publish(presentation(owner));
    }

    private void CloseScene(nuint scene)
    {
        MenuOwner owner;
        lock (stateGate)
        {
            if (confirmation is { } open && open.Scene == scene)
            {
                confirmation = null;
            }

            if (active is not { } current || current.Scene != scene)
            {
                return;
            }

            owner = current.Owner;
            active = null;
        }

        dispatcher.Publish(new MenuExited(owner));
    }

    private bool TryReadText(nuint address, out string text, out string error)
    {
        if (!stringReader.TryRead(address, out var read, out error))
        {
            text = string.Empty;
            return false;
        }

        text = new string(read.AsSpan());
        return true;
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

    private void InitializeBuild(IVerifiedGameBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.ImageBaseAddress == 0)
        {
            throw new InvalidOperationException(
                "Verified image base is unavailable for ending result hooks.");
        }

        if (imageBase == 0)
        {
            imageBase = build.ImageBaseAddress;
        }
        else if (imageBase != build.ImageBaseAddress)
        {
            throw new InvalidOperationException(
                "Ending result hooks received conflicting image bases.");
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

    private enum CaptureKind
    {
        Message,
        Confirmation,
        Notice,
    }

    /// <summary>A label and the index of the last control constructed before it (-1: none).</summary>
    private sealed record CapturedLabel(string Text, int Control);

    private sealed record WindowBinding(nuint Manager, nuint Control, int Key);

    private sealed record WindowFocus(nuint Manager, int Key);

    private sealed record PresentedScene(nuint Scene, MenuOwner Owner);

    private sealed record ActiveConfirmation(
        nuint Scene,
        nuint Container,
        nuint Window,
        nuint Manager,
        IReadOnlyList<string> Choices,
        IReadOnlyDictionary<int, int> IndexByKey);

    private sealed class CaptureScope(EndingResultHookSet owner, CaptureKind kind, nuint scene, int epoch)
    {
        public EndingResultHookSet Owner { get; } = owner;
        public CaptureKind Kind { get; } = kind;
        public nuint Scene { get; } = scene;
        public int Epoch { get; } = epoch;
        public nuint Container { get; set; }
        public int NoticeMessageId { get; set; } = -1;
        public string? Expected { get; set; }
        public List<CapturedLabel> Labels { get; } = [];
        public List<nuint> Controls { get; } = [];
        public Dictionary<int, string> LocalizedChoices { get; } = [];
        public List<WindowBinding> Bindings { get; } = [];
        public List<WindowFocus> Focus { get; } = [];
        public List<string> Errors { get; } = [];
    }
}
