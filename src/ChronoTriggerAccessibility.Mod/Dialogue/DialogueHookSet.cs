using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions.Enums;

namespace ChronoTriggerAccessibility.Mod.Dialogue;

public sealed class DialogueHookSet : IHookActivationObserver
{
    private static readonly RuntimeAsmHookOptions ConfirmProbeOptions = new(
        AsmHookBehaviour.ExecuteFirst,
        HookLength: 5,
        PreferRelativeJump: true,
        MaxOpcodeSize: 5);

    private readonly IRuntimeNativeHookFactory hookFactory;
    private readonly IRuntimeNativeAsmHookFactory asmHookFactory;
    private readonly IReadableMemory memory;
    private readonly ISemanticEventDispatcher dispatcher;
    private readonly object lifecycleGate = new();
    private readonly HashSet<HookId> preparedIds = [];
    private readonly HashSet<DialogueLineIdentity> observedLines = [];
    private readonly ThreadLocal<ConfirmMarker?> confirmMarker = new(() => null);
    private readonly IReadOnlyList<IHookRegistration> registrations;
    private nuint imageBase;
    private nuint pendingWindow;
    private nuint activeWindow;
    private string[]? choiceLabels;
    private int choicePageBase = -1;
    private int choiceFirstLine = -1;
    private int selectedChoice = -1;
    private int activeEpoch;
    private bool hooksActive;
    private bool faulted;

    public DialogueHookSet(
        IRuntimeNativeHookFactory hookFactory,
        IRuntimeNativeAsmHookFactory asmHookFactory,
        IReadableMemory memory,
        ISemanticEventDispatcher dispatcher)
    {
        this.hookFactory = hookFactory ?? throw new ArgumentNullException(nameof(hookFactory));
        this.asmHookFactory = asmHookFactory ?? throw new ArgumentNullException(nameof(asmHookFactory));
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        RequiredHookIds = new ReadOnlyCollection<HookId>(
        [
            HookId.MsgWindowOpen,
            HookId.MsgWindowUpdate,
            HookId.MsgWindowClose,
            HookId.MsgWindowChoiceConfirmCallSite,
        ]);
        registrations = new ReadOnlyCollection<IHookRegistration>(
        [
            CreateRegistration(HookId.MsgWindowOpen, PrepareOpen),
            CreateRegistration(HookId.MsgWindowUpdate, PrepareUpdate),
            CreateRegistration(HookId.MsgWindowClose, PrepareClose),
            CreateRegistration(HookId.MsgWindowChoiceConfirmCallSite, PrepareConfirmProbe),
        ]);
    }

    public IReadOnlyList<HookId> RequiredHookIds { get; }

    public IReadOnlyList<IHookRegistration> Registrations => registrations;

    public void AfterHooksActivated()
    {
        lock (lifecycleGate)
        {
            if (imageBase == 0 || preparedIds.Count != RequiredHookIds.Count ||
                RequiredHookIds.Any(id => !preparedIds.Contains(id)))
            {
                throw new InvalidOperationException(
                    "Dialogue hooks cannot activate until all three function hooks and the confirm probe are prepared against one verified image base.");
            }
            if (hooksActive)
            {
                throw new InvalidOperationException("Dialogue hooks are already active.");
            }

            activeEpoch = unchecked(activeEpoch + 1);
            hooksActive = true;
            faulted = false;
            ClearInteractionLocked();
        }
    }

    public void AfterHooksDisabled()
    {
        lock (lifecycleGate)
        {
            activeEpoch = unchecked(activeEpoch + 1);
            hooksActive = false;
            faulted = false;
            ClearInteractionLocked();
        }
    }

    private IPreparedHook PrepareOpen(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        return PrepareFunctionHook<MsgWindowOpenDelegate>(
            HookId.MsgWindowOpen,
            build,
            original => (window, rawStackWord) => boundary.Run(
                "MsgWindow open/parser",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch);
                    try
                    {
                        original()(window, rawStackWord);
                    }
                    catch
                    {
                        if (instrument)
                        {
                            FaultEpoch(epoch);
                        }
                        throw;
                    }

                    if (instrument)
                    {
                        ObservePostOpen(epoch, unchecked((nuint)window));
                    }
                }));
    }

    private IPreparedHook PrepareUpdate(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        return PrepareFunctionHook<MsgWindowUpdateDelegate>(
            HookId.MsgWindowUpdate,
            build,
            original => (window, deltaSecondsBits) => boundary.Run(
                "MsgWindow update",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch);
                    try
                    {
                        original()(window, deltaSecondsBits);
                    }
                    catch
                    {
                        if (instrument)
                        {
                            FaultEpoch(epoch);
                        }
                        throw;
                    }

                    if (instrument)
                    {
                        ObservePostUpdate(epoch, unchecked((nuint)window));
                    }
                }));
    }

    private IPreparedHook PrepareClose(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        return PrepareFunctionHook<MsgWindowCloseDelegate>(
            HookId.MsgWindowClose,
            build,
            original => (window, dummyStackWord) => boundary.Run(
                "MsgWindow close",
                () =>
                {
                    var instrument = TryCaptureActiveEpoch(out var epoch);
                    var nativeWindow = unchecked((nuint)window);
                    var preparation = PrepareCloseObservation(epoch, nativeWindow, instrument);
                    try
                    {
                        original()(window, dummyStackWord);
                    }
                    catch
                    {
                        if (instrument)
                        {
                            FaultEpoch(epoch);
                        }
                        throw;
                    }

                    if (instrument)
                    {
                        CompleteClose(epoch, nativeWindow, preparation);
                    }
                }));
    }

    private IPreparedHook PrepareConfirmProbe(
        IVerifiedGameBuild build,
        UnmanagedBoundaryGuard boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        var address = ResolveRequiredAddress(HookId.MsgWindowChoiceConfirmCallSite, build);
        MsgWindowChoiceConfirmProbeDelegate callback = window => boundary.Run(
            "MsgWindow choice-confirm call-site probe",
            () => MarkConfirm(unchecked((nuint)window)));
        var prepared = asmHookFactory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite,
            GameVersionCatalog.Get(HookId.MsgWindowChoiceConfirmCallSite).Symbol,
            callback,
            address,
            ChoiceConfirmProbeAssembly.Build,
            ConfirmProbeOptions)
            ?? throw new InvalidOperationException("The dialogue confirm assembly-hook factory returned null.");
        MarkPrepared(HookId.MsgWindowChoiceConfirmCallSite);
        return prepared;
    }

    private IPreparedHook PrepareFunctionHook<TDelegate>(
        HookId id,
        IVerifiedGameBuild build,
        Func<Func<TDelegate>, TDelegate> createDetour)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(createDetour);
        var address = ResolveRequiredAddress(id, build);
        TDelegate? original = null;
        TDelegate GetOriginal() => original ?? throw new InvalidOperationException(
            $"Original function for required dialogue hook '{id}' is not bound.");
        var detour = createDetour(GetOriginal);
        var hook = hookFactory.CreateHook(id, detour, address)
            ?? throw new InvalidOperationException($"The native hook factory returned null for '{id}'.");
        original = hook.OriginalFunction ?? throw new InvalidOperationException(
            $"The native hook for '{id}' returned no original function.");
        var prepared = new ReloadedPreparedHook<TDelegate>(GameVersionCatalog.Get(id).Symbol, hook, detour);
        MarkPrepared(id);
        return prepared;
    }

    private nuint ResolveRequiredAddress(HookId id, IVerifiedGameBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        lock (lifecycleGate)
        {
            if (!RequiredHookIds.Contains(id))
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "The hook is not owned by the dialogue hook set.");
            }
            if (preparedIds.Contains(id))
            {
                throw new InvalidOperationException($"Dialogue hook '{id}' was already prepared.");
            }
            if (hooksActive)
            {
                throw new InvalidOperationException("Dialogue hooks cannot be prepared while active.");
            }
            if (build.ImageBaseAddress == 0 || build.ImageBaseAddress > uint.MaxValue)
            {
                throw new InvalidOperationException("Verified x86 image base is unavailable for dialogue hooks.");
            }

            if (imageBase == 0)
            {
                imageBase = build.ImageBaseAddress;
            }
            else if (imageBase != build.ImageBaseAddress)
            {
                throw new InvalidOperationException("Dialogue hooks received conflicting verified image bases.");
            }

            if (!build.HookAddresses.TryGetValue(id, out var address) || address == 0)
            {
                throw new InvalidOperationException($"Verified address for required dialogue hook '{id}' is missing.");
            }
            var expected = checked((ulong)imageBase + GameVersionCatalog.Get(id).Rva);
            if (expected > uint.MaxValue || address != (nuint)expected)
            {
                throw new InvalidOperationException(
                    $"Verified address for dialogue hook '{id}' does not match image-base-plus-RVA resolution.");
            }

            return address;
        }
    }

    private void MarkPrepared(HookId id)
    {
        lock (lifecycleGate)
        {
            if (!preparedIds.Add(id))
            {
                throw new InvalidOperationException($"Dialogue hook '{id}' was prepared more than once.");
            }
        }
    }

    private void ObservePostOpen(int epoch, nuint window)
    {
        if (!IsEpochObservable(epoch))
        {
            return;
        }
        var status = DialogueCapture.CaptureStatus(
            memory,
            imageBase,
            window,
            out var snapshot,
            out var error);
        if (status == DialogueCaptureStatus.Invalid)
        {
            ReportCaptureFailure(epoch, $"Post-open dialogue snapshot is incomplete: {error}");
            return;
        }

        lock (lifecycleGate)
        {
            if (!IsEpochObservableLocked(epoch))
            {
                return;
            }

            try
            {
                ClearInteractionLocked();
                if (status == DialogueCaptureStatus.Inactive)
                {
                    pendingWindow = window;
                    return;
                }

                activeWindow = window;
                dispatcher.Publish(new DialogueOpened());
                ObserveSnapshotLocked(snapshot);
            }
            catch
            {
                FaultLocked();
                throw;
            }
        }
    }

    private void ObservePostUpdate(int epoch, nuint window)
    {
        bool wasPending;
        lock (lifecycleGate)
        {
            if (!IsEpochObservableLocked(epoch))
            {
                return;
            }

            if (pendingWindow == window)
            {
                wasPending = true;
            }
            else if (activeWindow == window)
            {
                wasPending = false;
            }
            else
            {
                return;
            }
        }

        var status = DialogueCapture.CaptureStatus(
            memory,
            imageBase,
            window,
            out var snapshot,
            out var error);
        if (status == DialogueCaptureStatus.Inactive)
        {
            // +2BD == 0 is a valid native lifecycle state. Scene teardown can
            // destroy a MsgWindow without calling its animated close function;
            // a newly constructed window may later reuse the registered address.
            // Retire an old active interaction, including its confirm marker,
            // without confirming a choice or faulting future dialogue. A pending
            // open still waits for the game's activation callback at 1984F0.
            if (!wasPending)
                CompleteClose(epoch, window, new ClosePreparation(epoch, window, Recognized: true, null, null));
            return;
        }
        if (status != DialogueCaptureStatus.Complete)
        {
            ReportRegisteredCaptureFailure(
                epoch,
                window,
                wasPending,
                $"Registered dialogue update snapshot is incomplete: {error}");
            return;
        }

        lock (lifecycleGate)
        {
            if (!IsEpochObservableLocked(epoch))
            {
                return;
            }

            try
            {
                if (wasPending)
                {
                    if (pendingWindow != window)
                    {
                        return;
                    }

                    pendingWindow = 0;
                    activeWindow = window;
                    dispatcher.Publish(new DialogueOpened());
                }
                else if (activeWindow != window)
                {
                    return;
                }

                ObserveSnapshotLocked(snapshot);
            }
            catch
            {
                FaultLocked();
                throw;
            }
        }
    }

    private void ObserveSnapshotLocked(DialogueSnapshot snapshot)
    {
        if (snapshot.Line is not null)
        {
            var identity = new DialogueLineIdentity(
                snapshot.PageBase,
                snapshot.Line.Index,
                snapshot.Line.Text);
            if (observedLines.Add(identity))
            {
                dispatcher.Publish(new DialogueLinePresented(
                    snapshot.PageBase,
                    snapshot.Line.Index,
                    snapshot.Line.Text));
            }
        }

        if (snapshot.Choices is null)
        {
            ClearChoicesLocked();
            return;
        }

        var choices = snapshot.Choices;
        var sameList = choiceLabels is not null &&
            choicePageBase == snapshot.PageBase &&
            choiceFirstLine == choices.FirstLineIndex &&
            choiceLabels.SequenceEqual(choices.Labels, StringComparer.Ordinal);
        if (!sameList)
        {
            choiceLabels = choices.Labels.Select(label => new string(label.AsSpan())).ToArray();
            choicePageBase = snapshot.PageBase;
            choiceFirstLine = choices.FirstLineIndex;
            selectedChoice = choices.SelectedIndex;
            dispatcher.Publish(new DialogueChoicesPresented(choiceLabels, selectedChoice));
            return;
        }

        if (selectedChoice == choices.SelectedIndex)
        {
            return;
        }

        selectedChoice = choices.SelectedIndex;
        if (selectedChoice >= 0)
        {
            dispatcher.Publish(new DialogueChoiceFocused(
                choiceLabels![selectedChoice],
                selectedChoice,
                choiceLabels.Length));
        }
    }

    private void MarkConfirm(nuint window)
    {
        confirmMarker.Value = null;
        lock (lifecycleGate)
        {
            if (!IsEpochObservableLocked(activeEpoch) || window == 0 || activeWindow != window)
            {
                return;
            }

            confirmMarker.Value = new ConfirmMarker(activeEpoch, window);
        }
    }

    private ClosePreparation PrepareCloseObservation(int epoch, nuint window, bool instrument)
    {
        var marker = confirmMarker.Value;
        confirmMarker.Value = null;
        if (!instrument)
        {
            return default;
        }

        lock (lifecycleGate)
        {
            if (!IsEpochObservableLocked(epoch))
            {
                return default;
            }

            var recognized = activeWindow == window || pendingWindow == window;
            if (!recognized)
            {
                return default;
            }

            if (activeWindow != window || marker is null || marker.Value.Epoch != epoch ||
                marker.Value.Window != window || choiceLabels is null || selectedChoice < -1 ||
                selectedChoice >= choiceLabels.Length)
            {
                return new ClosePreparation(epoch, window, Recognized: true, null, null);
            }

            var status = DialogueCapture.CaptureStatus(
                memory,
                imageBase,
                window,
                out var snapshot,
                out var error);
            if (status != DialogueCaptureStatus.Complete)
            {
                return new ClosePreparation(
                    epoch,
                    window,
                    Recognized: true,
                    $"Pre-close dialogue choice snapshot is incomplete: {error}",
                    null);
            }

            if (snapshot.PageBase != choicePageBase || snapshot.Choices is null ||
                snapshot.Choices.FirstLineIndex != choiceFirstLine ||
                snapshot.Choices.Labels.Count != choiceLabels.Length ||
                !snapshot.Choices.Labels.SequenceEqual(choiceLabels, StringComparer.Ordinal) ||
                snapshot.Choices.SelectedIndex < -1 ||
                snapshot.Choices.SelectedIndex >= snapshot.Choices.Labels.Count)
            {
                return new ClosePreparation(
                    epoch,
                    window,
                    Recognized: true,
                    "Pre-close dialogue choice snapshot does not exactly match the presented choice list and valid selected range.",
                    null);
            }

            var freshSelection = snapshot.Choices.SelectedIndex;
            if (freshSelection < 0)
            {
                return new ClosePreparation(epoch, window, Recognized: true, null, null);
            }

            var activation = new CloseActivation(
                new string(snapshot.Choices.Labels[freshSelection].AsSpan()),
                freshSelection,
                snapshot.Choices.Labels.Count,
                FocusChanged: freshSelection != selectedChoice);
            return new ClosePreparation(epoch, window, Recognized: true, null, activation);
        }
    }

    private void CompleteClose(int epoch, nuint window, ClosePreparation preparation)
    {
        if (preparation.Diagnostic is not null)
        {
            ReportCaptureFailure(epoch, preparation.Diagnostic);
            return;
        }

        lock (lifecycleGate)
        {
            if (!IsEpochObservableLocked(epoch) || !preparation.Recognized ||
                preparation.Epoch != epoch || preparation.Window != window ||
                (activeWindow != window && pendingWindow != window))
            {
                return;
            }

            try
            {
                if (preparation.Activation is { } activation)
                {
                    if (activation.FocusChanged)
                    {
                        dispatcher.Publish(new DialogueChoiceFocused(
                            activation.Label,
                            activation.SelectedIndex,
                            activation.Count));
                    }
                    dispatcher.Publish(new DialogueChoiceActivated(activation.Label));
                }

                ClearInteractionLocked();
                dispatcher.Publish(new DialogueClosed());
            }
            catch
            {
                FaultLocked();
                throw;
            }
        }
    }

    private bool TryCaptureActiveEpoch(out int epoch)
    {
        lock (lifecycleGate)
        {
            epoch = activeEpoch;
            return IsEpochObservableLocked(epoch);
        }
    }

    private bool IsEpochObservable(int epoch)
    {
        lock (lifecycleGate)
        {
            return IsEpochObservableLocked(epoch);
        }
    }

    private bool IsEpochObservableLocked(int epoch) =>
        hooksActive && !faulted && activeEpoch == epoch;

    private void ReportCaptureFailure(int epoch, string diagnostic)
    {
        lock (lifecycleGate)
        {
            if (!IsEpochObservableLocked(epoch))
            {
                return;
            }

            FaultLocked();
            var exact = string.IsNullOrWhiteSpace(diagnostic)
                ? "Dialogue snapshot capture failed without a diagnostic."
                : diagnostic;
            dispatcher.ReportCoverageFailure(exact);
        }
    }

    private void ReportRegisteredCaptureFailure(
        int epoch,
        nuint window,
        bool pending,
        string diagnostic)
    {
        lock (lifecycleGate)
        {
            if (!IsEpochObservableLocked(epoch) ||
                (pending ? pendingWindow != window : activeWindow != window))
            {
                return;
            }

            FaultLocked();
            var exact = string.IsNullOrWhiteSpace(diagnostic)
                ? "Dialogue snapshot capture failed without a diagnostic."
                : diagnostic;
            dispatcher.ReportCoverageFailure(exact);
        }
    }

    private void FaultEpoch(int epoch)
    {
        lock (lifecycleGate)
        {
            if (hooksActive && activeEpoch == epoch)
            {
                FaultLocked();
            }
        }
    }

    private void FaultLocked()
    {
        faulted = true;
        ClearInteractionLocked();
    }

    private void ClearInteractionLocked()
    {
        pendingWindow = 0;
        activeWindow = 0;
        observedLines.Clear();
        ClearChoicesLocked();
        confirmMarker.Value = null;
    }

    private void ClearChoicesLocked()
    {
        choiceLabels = null;
        choicePageBase = -1;
        choiceFirstLine = -1;
        selectedChoice = -1;
    }

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

    private readonly record struct DialogueLineIdentity(int PageBase, int LineIndex, string Text);
    private readonly record struct ConfirmMarker(int Epoch, nuint Window);
    private readonly record struct ClosePreparation(
        int Epoch,
        nuint Window,
        bool Recognized,
        string? Diagnostic,
        CloseActivation? Activation);
    private readonly record struct CloseActivation(
        string Label,
        int SelectedIndex,
        int Count,
        bool FocusChanged);
}
