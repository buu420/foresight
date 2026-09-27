using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Mod.Runtime;

public sealed class AccessibilityRuntime
{
    private const int ShutdownRequestedSignal = 1;
    private const int ActivationCommittedSignal = 2;
    private readonly IRuntimeExecutableVerifier executableVerifier;
    private readonly IGameWindowWaiter windowWaiter;
    private readonly IRuntimePrismFactory prismFactory;
    private readonly IRuntimeHookInstaller hookInstaller;
    private readonly IModLog log;
    private readonly IAccessibleFatalError fatalError;
    private readonly int processId;
    private readonly ISemanticEventDispatcher? semanticDispatcher;
    private readonly IReadOnlyList<HookContract> requiredHookContracts;
    private readonly object lifecycleGate = new();
    private readonly CancellationTokenSource shutdownCancellation = new();
    private IRuntimePrismSession? prismSession;
    private CleanupOutcome? cleanupOutcome;
    private int state = (int)AccessibilityRuntimeState.Created;
    private int started;
    private int lifecycleSignals;
    private int cleanupCompleted;
    private int fatalReported;

    public AccessibilityRuntime(
        IRuntimeExecutableVerifier executableVerifier,
        IGameWindowWaiter windowWaiter,
        IRuntimePrismFactory prismFactory,
        IRuntimeHookInstaller hookInstaller,
        IModLog log,
        IAccessibleFatalError fatalError,
        int processId,
        ISemanticEventDispatcher? semanticDispatcher = null,
        IReadOnlyList<HookContract>? requiredHookContracts = null)
    {
        this.executableVerifier = executableVerifier ?? throw new ArgumentNullException(nameof(executableVerifier));
        this.windowWaiter = windowWaiter ?? throw new ArgumentNullException(nameof(windowWaiter));
        this.prismFactory = prismFactory ?? throw new ArgumentNullException(nameof(prismFactory));
        this.hookInstaller = hookInstaller ?? throw new ArgumentNullException(nameof(hookInstaller));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.fatalError = fatalError ?? throw new ArgumentNullException(nameof(fatalError));
        this.processId = processId > 0 ? processId : throw new ArgumentOutOfRangeException(nameof(processId));
        this.semanticDispatcher = semanticDispatcher;
        this.requiredHookContracts = (requiredHookContracts ?? GameVersionCatalog.Hooks).ToArray();
        if (this.requiredHookContracts.Count == 0)
        {
            throw new ArgumentException(
                "At least one required hook contract must be supplied.",
                nameof(requiredHookContracts));
        }
    }

    public AccessibilityRuntimeState State => (AccessibilityRuntimeState)Volatile.Read(ref state);

    public Task StartInBackground(CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(
            () => Initialize(cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);

    public void Initialize() => Initialize(CancellationToken.None);

    public void Initialize(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("Accessibility runtime initialization may only run once.");
        }

        if (IsShutdownRequested)
        {
            SetState(AccessibilityRuntimeState.Stopped);
            return;
        }

        SetState(AccessibilityRuntimeState.Initializing);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            shutdownCancellation.Token);

        try
        {
            linkedCancellation.Token.ThrowIfCancellationRequested();
            var build = executableVerifier.VerifyCurrentProcess();
            var resolvedHooks = SnapshotResolvedHooks(build);
            LogVerifiedBuild(build, resolvedHooks);
            linkedCancellation.Token.ThrowIfCancellationRequested();
            _ = windowWaiter.WaitForSoleVisibleWindow(processId, linkedCancellation.Token);
            linkedCancellation.Token.ThrowIfCancellationRequested();

            string? failureMessage = null;
            lock (lifecycleGate)
            {
                if (IsShutdownRequested)
                {
                    CompleteShutdownUnderLock();
                    return;
                }

                try
                {
                    prismSession = prismFactory.Create();
                    semanticDispatcher?.Attach(prismSession);
                    log.Info($"Prism backend active: {prismSession.BackendName}");
                    linkedCancellation.Token.ThrowIfCancellationRequested();

                    var boundary = new UnmanagedBoundaryGuard(log, fatalError);
                    hookInstaller.PrepareAll(build, boundary);
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    if (hookInstaller.PreparedHooks.Any(hook => hook.IsActive))
                    {
                        throw new InvalidOperationException(
                            "A prepared hook was active before the atomic activation phase.");
                    }

                    VerifyPreparedHookState(resolvedHooks, expectedActive: false);
                    foreach (var hook in resolvedHooks)
                    {
                        log.Info(
                            $"Hook preparation verified: '{hook.Contract.Symbol}' at " +
                            $"0x{hook.Address:X8} is prepared and inactive.");
                    }

                    if (!TryCommitActivation())
                    {
                        throw new OperationCanceledException(shutdownCancellation.Token);
                    }

                    log.Info(
                        $"Atomic hook activation starting for {hookInstaller.PreparedHooks.Count} prepared hooks.");
                    hookInstaller.ActivateAll();
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    VerifyPreparedHookState(resolvedHooks, expectedActive: true);
                    foreach (var hook in resolvedHooks)
                    {
                        log.Info(
                            $"Hook activation verified: '{hook.Contract.Symbol}' at " +
                            $"0x{hook.Address:X8} is active.");
                    }

                    SetState(AccessibilityRuntimeState.Active);
                    log.Info("Accessibility runtime active.");
                }
                catch (Exception exception)
                {
                    if (IsShutdownCancellation(exception))
                    {
                        CompleteShutdownUnderLock();
                    }
                    else
                    {
                        failureMessage = CompleteInitializationFailureUnderLock(exception);
                    }
                }
            }

            if (failureMessage is not null)
            {
                ReportFatalOnce(failureMessage);
            }
        }
        catch (Exception exception)
        {
            if (IsShutdownCancellation(exception))
            {
                Shutdown();
                return;
            }

            string failureMessage;
            lock (lifecycleGate)
            {
                failureMessage = CompleteInitializationFailureUnderLock(exception);
            }

            ReportFatalOnce(failureMessage);
        }
    }

    public void Shutdown()
    {
        Interlocked.Or(ref lifecycleSignals, ShutdownRequestedSignal);
        try
        {
            shutdownCancellation.Cancel();
        }
        catch (Exception exception)
        {
            ReportFatalOnce($"Accessibility shutdown cancellation failed: {exception}");
        }

        string? failureMessage = null;
        lock (lifecycleGate)
        {
            var outcome = CleanupResourcesUnderLock();
            if (outcome.Failures.Count == 0)
            {
                SetState(AccessibilityRuntimeState.Stopped);
            }
            else
            {
                SetState(AccessibilityRuntimeState.Faulted);
                failureMessage = FormatCleanupFailure("Accessibility shutdown integrity failure", outcome);
            }
        }

        if (failureMessage is not null)
        {
            ReportFatalOnce(failureMessage);
        }
    }

    private bool IsShutdownRequested =>
        (Volatile.Read(ref lifecycleSignals) & ShutdownRequestedSignal) != 0;

    private IReadOnlyList<ResolvedRequiredHook> SnapshotResolvedHooks(IVerifiedGameBuild build)
    {
        if (build.HookAddresses.Count != requiredHookContracts.Count)
        {
            throw new InvalidOperationException(
                $"Verified build exposed {build.HookAddresses.Count} hook addresses; " +
                $"expected {requiredHookContracts.Count}.");
        }

        var resolvedHooks = new List<ResolvedRequiredHook>(requiredHookContracts.Count);
        foreach (var contract in requiredHookContracts)
        {
            if (!build.HookAddresses.TryGetValue(contract.Id, out var address) || address == 0)
            {
                throw new InvalidOperationException(
                    $"Verified build did not expose a resolved address for '{contract.Symbol}'.");
            }

            if (build.ImageBaseAddress != 0 &&
                address != checked(build.ImageBaseAddress + contract.Rva))
            {
                throw new InvalidOperationException(
                    $"Resolved address for '{contract.Symbol}' did not match its verified RVA.");
            }

            resolvedHooks.Add(new ResolvedRequiredHook(contract, address));
        }

        return resolvedHooks.AsReadOnly();
    }

    private void LogVerifiedBuild(
        IVerifiedGameBuild build,
        IReadOnlyList<ResolvedRequiredHook> resolvedHooks)
    {
        log.Info(
            $"Supported executable verified: SHA-256 {GameVersionCatalog.Executable.Sha256}; " +
            $"machine {GameVersionCatalog.Executable.Machine}; preferred image base " +
            $"0x{GameVersionCatalog.Executable.ImageBase:X8}; loaded image base " +
            $"0x{build.ImageBaseAddress:X8}; {requiredHookContracts.Count} required hook byte contracts verified.");

        foreach (var hook in resolvedHooks)
        {
            log.Info(
                $"Hook byte verification passed: '{hook.Contract.Symbol}' at 0x{hook.Address:X8}.");
        }
    }

    private void VerifyPreparedHookState(
        IReadOnlyList<ResolvedRequiredHook> resolvedHooks,
        bool expectedActive)
    {
        if (resolvedHooks.Count == 0)
        {
            return;
        }

        var requiredPreparedHooks = hookInstaller.PreparedHooks.Where(hook => hook is not IOptionalPreparedHook).ToArray();
        if (requiredPreparedHooks.Length != resolvedHooks.Count)
        {
            throw new InvalidOperationException(
                $"Hook installer exposed {requiredPreparedHooks.Length} prepared required hooks; " +
                $"expected {resolvedHooks.Count}.");
        }

        foreach (var resolvedHook in resolvedHooks)
        {
            var matches = requiredPreparedHooks
                .Where(hook => string.Equals(
                    hook.Name,
                    resolvedHook.Contract.Symbol,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one prepared hook named '{resolvedHook.Contract.Symbol}', " +
                    $"but found {matches.Length}.");
            }

            if (matches[0].IsActive != expectedActive)
            {
                var expectedState = expectedActive ? "active" : "inactive";
                throw new InvalidOperationException(
                    $"Hook '{resolvedHook.Contract.Symbol}' was not {expectedState} at phase verification.");
            }
        }
    }

    private bool TryCommitActivation()
    {
        while (true)
        {
            var signals = Volatile.Read(ref lifecycleSignals);
            if ((signals & ShutdownRequestedSignal) != 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref lifecycleSignals,
                    signals | ActivationCommittedSignal,
                    signals) == signals)
            {
                return true;
            }
        }
    }

    private bool IsShutdownCancellation(Exception exception) =>
        IsShutdownRequested && exception is OperationCanceledException;

    private void CompleteShutdownUnderLock()
    {
        var outcome = CleanupResourcesUnderLock();
        if (outcome.Failures.Count == 0)
        {
            SetState(AccessibilityRuntimeState.Stopped);
            return;
        }

        SetState(AccessibilityRuntimeState.Faulted);
        ReportFatalOnce(FormatCleanupFailure("Accessibility shutdown integrity failure", outcome));
    }

    private string CompleteInitializationFailureUnderLock(Exception originalFailure)
    {
        var outcome = CleanupResourcesUnderLock();
        SetState(AccessibilityRuntimeState.Faulted);
        var message = $"Chrono Trigger accessibility initialization failed: {originalFailure}";
        return outcome.Failures.Count == 0
            ? message
            : $"{message}{Environment.NewLine}{FormatCleanupFailure("Rollback/integrity failure", outcome)}";
    }

    private CleanupOutcome CleanupResourcesUnderLock()
    {
        if (Volatile.Read(ref cleanupCompleted) != 0)
        {
            return cleanupOutcome ?? CleanupOutcome.Success;
        }

        var failures = new List<Exception>();
        var hooksConfirmedInactive = true;
        try
        {
            hookInstaller.DisableAll();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            if (exception is HookRollbackException { HooksConfirmedInactive: false })
            {
                hooksConfirmedInactive = false;
            }
        }

        foreach (var hook in hookInstaller.PreparedHooks)
        {
            try
            {
                if (hook.IsActive)
                {
                    hooksConfirmedInactive = false;
                    failures.Add(new InvalidOperationException(
                        $"Residual active hook '{hook.Name}' prevents Prism shutdown."));
                }
            }
            catch (Exception exception)
            {
                hooksConfirmedInactive = false;
                failures.Add(new InvalidOperationException(
                    $"Could not confirm hook '{hook.Name}' inactive; Prism remains loaded.", exception));
            }
        }

        if (hooksConfirmedInactive && prismSession is not null)
        {
            try
            {
                semanticDispatcher?.Detach(prismSession);
                prismSession.Dispose();
                prismSession = null;
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException("Prism disposal failed.", exception));
            }
        }

        cleanupOutcome = new CleanupOutcome(failures.AsReadOnly(), hooksConfirmedInactive);
        Volatile.Write(ref cleanupCompleted, 1);
        return cleanupOutcome;
    }

    private static string FormatCleanupFailure(string heading, CleanupOutcome outcome)
    {
        var details = string.Join(" | ", outcome.Failures.Select(failure => failure.ToString()));
        if (!outcome.HooksConfirmedInactive)
        {
            details += " | Prism was intentionally retained because every hook could not be confirmed inactive.";
        }

        return $"{heading}: {details}";
    }

    private void ReportFatalOnce(string message)
    {
        if (Interlocked.Exchange(ref fatalReported, 1) != 0)
        {
            return;
        }

        try
        {
            log.Error(message);
        }
        catch (Exception)
        {
            // Keep trying the independent native accessible sink.
        }

        try
        {
            fatalError.Show(message);
        }
        catch (Exception)
        {
            // Runtime integrity is already faulted; diagnostics cannot escape to the game.
        }
    }

    private void SetState(AccessibilityRuntimeState value) =>
        Volatile.Write(ref state, (int)value);

    private sealed record CleanupOutcome(
        IReadOnlyList<Exception> Failures,
        bool HooksConfirmedInactive)
    {
        public static CleanupOutcome Success { get; } = new(Array.Empty<Exception>(), true);
    }

    private readonly record struct ResolvedRequiredHook(HookContract Contract, nuint Address);
}
