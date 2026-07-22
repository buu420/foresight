using ChronoTriggerAccessibility.Mod.Diagnostics;

namespace ChronoTriggerAccessibility.Mod.Runtime;

public sealed class AccessibilityRuntime
{
    private readonly IRuntimeExecutableVerifier executableVerifier;
    private readonly IGameWindowWaiter windowWaiter;
    private readonly IRuntimePrismFactory prismFactory;
    private readonly IRuntimeHookInstaller hookInstaller;
    private readonly IModLog log;
    private readonly IAccessibleFatalError fatalError;
    private readonly int processId;
    private IRuntimePrismSession? prismSession;
    private int started;

    public AccessibilityRuntime(
        IRuntimeExecutableVerifier executableVerifier,
        IGameWindowWaiter windowWaiter,
        IRuntimePrismFactory prismFactory,
        IRuntimeHookInstaller hookInstaller,
        IModLog log,
        IAccessibleFatalError fatalError,
        int processId)
    {
        this.executableVerifier = executableVerifier ?? throw new ArgumentNullException(nameof(executableVerifier));
        this.windowWaiter = windowWaiter ?? throw new ArgumentNullException(nameof(windowWaiter));
        this.prismFactory = prismFactory ?? throw new ArgumentNullException(nameof(prismFactory));
        this.hookInstaller = hookInstaller ?? throw new ArgumentNullException(nameof(hookInstaller));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.fatalError = fatalError ?? throw new ArgumentNullException(nameof(fatalError));
        this.processId = processId > 0 ? processId : throw new ArgumentOutOfRangeException(nameof(processId));
    }

    public AccessibilityRuntimeState State { get; private set; } = AccessibilityRuntimeState.Created;

    public Task StartInBackground(CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(
            () => Initialize(cancellationToken),
            cancellationToken,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);

    public void Initialize() => Initialize(CancellationToken.None);

    public void Initialize(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("Accessibility runtime initialization may only run once.");
        }

        State = AccessibilityRuntimeState.Initializing;
        try
        {
            var build = executableVerifier.VerifyCurrentProcess();
            _ = windowWaiter.WaitForSoleVisibleWindow(processId, cancellationToken);
            prismSession = prismFactory.Create();
            log.Info($"Prism backend active: {prismSession.BackendName}");

            var boundary = new UnmanagedBoundaryGuard(log, fatalError);
            hookInstaller.PrepareAll(build, boundary);
            if (hookInstaller.PreparedHooks.Any(hook => hook.IsActive))
            {
                throw new InvalidOperationException("A prepared hook was active before the atomic activation phase.");
            }

            hookInstaller.ActivateAll();
            State = AccessibilityRuntimeState.Active;
            log.Info("Accessibility runtime active.");
        }
        catch (Exception exception)
        {
            hookInstaller.DisableAll();
            prismSession?.Dispose();
            prismSession = null;
            State = AccessibilityRuntimeState.Faulted;
            ReportFatal($"Chrono Trigger accessibility initialization failed: {exception}");
        }
    }

    private void ReportFatal(string message)
    {
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
            // Initialization is already faulted; never fault the background task on diagnostics.
        }
    }
}
