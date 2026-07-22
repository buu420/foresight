using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Native.Hooks;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class AccessibilityRuntimeTests
{
    [Fact]
    public void InitializeUsesStrictFailClosedCompositionOrder()
    {
        var scenario = new RuntimeScenario();

        scenario.Runtime.Initialize();

        Assert.Equal(
        [
            "verify-executable",
            "wait-for-window",
            "initialize-prism",
            "prepare:first",
            "prepare:second",
            "activate:first",
            "activate:second",
        ], scenario.Events);
        Assert.All(scenario.Hooks, hook => Assert.True(hook.IsActive));
        Assert.Contains(scenario.Log.Infos, message => message.Contains("Test backend", StringComparison.Ordinal));
        Assert.Equal(AccessibilityRuntimeState.Active, scenario.Runtime.State);
    }

    [Theory]
    [InlineData(FailureStage.Verify, 1)]
    [InlineData(FailureStage.Window, 2)]
    [InlineData(FailureStage.Prism, 3)]
    [InlineData(FailureStage.PrepareFirst, 4)]
    [InlineData(FailureStage.PrepareSecond, 5)]
    [InlineData(FailureStage.ActivateFirst, 6)]
    [InlineData(FailureStage.ActivateSecond, 7)]
    public void FailureStopsLaterStagesDisablesEveryPreparedHookAndReportsAccessibly(
        FailureStage failureStage,
        int expectedEventCount)
    {
        var scenario = new RuntimeScenario(failureStage);

        var exception = Record.Exception(scenario.Runtime.Initialize);

        Assert.Null(exception);
        Assert.Equal(expectedEventCount, scenario.Events.Count);
        Assert.All(scenario.Hooks, hook => Assert.False(hook.IsActive));
        Assert.Equal(AccessibilityRuntimeState.Faulted, scenario.Runtime.State);
        Assert.Single(scenario.Log.Errors);
        Assert.Single(scenario.Fatal.Messages);
        Assert.Contains(failureStage.ToString(), scenario.Log.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(failureStage.ToString(), scenario.Fatal.Messages[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartInBackgroundDoesNotRunInitializationOnCallingThread()
    {
        var scenario = new RuntimeScenario();
        var callingThread = Environment.CurrentManagedThreadId;

        await scenario.Runtime.StartInBackground();

        Assert.NotEqual(callingThread, scenario.VerifierThreadId);
        Assert.Equal(AccessibilityRuntimeState.Active, scenario.Runtime.State);
    }

    [Fact]
    public void InitializationFailureReportPreservesOriginalAndRollbackIntegrityFailures()
    {
        var log = new RecordingLog();
        var fatal = new RecordingFatalError();
        var runtime = new AccessibilityRuntime(
            new SuccessfulVerifier(),
            new ImmediateWindowWaiter(),
            new ImmediatePrismFactory(),
            new OriginalAndRollbackFailingInstaller(),
            log,
            fatal,
            processId: 1234);

        var exception = Record.Exception(runtime.Initialize);

        Assert.Null(exception);
        Assert.Single(log.Errors);
        Assert.Single(fatal.Messages);
        Assert.Contains("original prepare failure", log.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback integrity failure", log.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("original prepare failure", fatal.Messages[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback integrity failure", fatal.Messages[0], StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RuntimeScenario
    {
        private readonly FailureStage failureStage;

        public RuntimeScenario(FailureStage failureStage = FailureStage.None)
        {
            this.failureStage = failureStage;
            Hooks = [new FakePreparedHook("first"), new FakePreparedHook("second")];
            Log = new RecordingLog();
            Fatal = new RecordingFatalError();
            Runtime = new AccessibilityRuntime(
                new FakeVerifier(this),
                new FakeWindowWaiter(this),
                new FakePrismFactory(this),
                new FakeHookInstaller(this),
                Log,
                Fatal,
                processId: 1234);
        }

        public List<string> Events { get; } = [];
        public FakePreparedHook[] Hooks { get; }
        public RecordingLog Log { get; }
        public RecordingFatalError Fatal { get; }
        public AccessibilityRuntime Runtime { get; }
        public int VerifierThreadId { get; private set; }

        private void Stage(FailureStage stage, string description)
        {
            Events.Add(description);
            if (failureStage == stage)
            {
                throw new InvalidOperationException($"{stage} failed");
            }
        }

        private sealed class FakeVerifier(RuntimeScenario scenario) : IRuntimeExecutableVerifier
        {
            public IVerifiedGameBuild VerifyCurrentProcess()
            {
                scenario.VerifierThreadId = Environment.CurrentManagedThreadId;
                scenario.Stage(FailureStage.Verify, "verify-executable");
                return new FakeVerifiedGameBuild();
            }
        }

        private sealed class FakeWindowWaiter(RuntimeScenario scenario) : IGameWindowWaiter
        {
            public nint WaitForSoleVisibleWindow(int processId, CancellationToken cancellationToken)
            {
                Assert.Equal(1234, processId);
                scenario.Stage(FailureStage.Window, "wait-for-window");
                return (nint)42;
            }
        }

        private sealed class FakePrismFactory(RuntimeScenario scenario) : IRuntimePrismFactory
        {
            public IRuntimePrismSession Create()
            {
                scenario.Stage(FailureStage.Prism, "initialize-prism");
                return new FakePrismSession();
            }
        }

        private sealed class FakeHookInstaller(RuntimeScenario scenario) : IRuntimeHookInstaller
        {
            public IReadOnlyList<IPreparedHook> PreparedHooks => scenario.Hooks;

            public void PrepareAll(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
            {
                foreach (var hook in scenario.Hooks)
                {
                    var stage = hook.Name == "first" ? FailureStage.PrepareFirst : FailureStage.PrepareSecond;
                    scenario.Stage(stage, $"prepare:{hook.Name}");
                    hook.Prepare();
                }
            }

            public void ActivateAll()
            {
                foreach (var hook in scenario.Hooks)
                {
                    var stage = hook.Name == "first" ? FailureStage.ActivateFirst : FailureStage.ActivateSecond;
                    scenario.Stage(stage, $"activate:{hook.Name}");
                    hook.Activate();
                }
            }

            public void DisableAll()
            {
                foreach (var hook in scenario.Hooks)
                {
                    hook.Disable();
                }
            }
        }
    }

    public enum FailureStage
    {
        None,
        Verify,
        Window,
        Prism,
        PrepareFirst,
        PrepareSecond,
        ActivateFirst,
        ActivateSecond,
    }

    private sealed class FakeVerifiedGameBuild : IVerifiedGameBuild
    {
        public IReadOnlyDictionary<HookId, nuint> HookAddresses { get; } = new Dictionary<HookId, nuint>();
    }

    private sealed class FakePrismSession : IRuntimePrismSession
    {
        public string BackendName => "Test backend";
        public void Dispose() { }
    }

    private sealed class SuccessfulVerifier : IRuntimeExecutableVerifier
    {
        public IVerifiedGameBuild VerifyCurrentProcess() => new FakeVerifiedGameBuild();
    }

    private sealed class ImmediateWindowWaiter : IGameWindowWaiter
    {
        public nint WaitForSoleVisibleWindow(int processId, CancellationToken cancellationToken) => (nint)42;
    }

    private sealed class ImmediatePrismFactory : IRuntimePrismFactory
    {
        public IRuntimePrismSession Create() => new FakePrismSession();
    }

    private sealed class OriginalAndRollbackFailingInstaller : IRuntimeHookInstaller
    {
        public IReadOnlyList<IPreparedHook> PreparedHooks => [];
        public void PrepareAll(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) =>
            throw new InvalidOperationException("original prepare failure");
        public void ActivateAll() => throw new NotSupportedException();
        public void DisableAll() => throw new InvalidOperationException("rollback integrity failure");
    }

    public sealed class FakePreparedHook(string name) : IPreparedHook
    {
        private bool prepared;
        public string Name { get; } = name;
        public bool IsActive { get; private set; }
        public IReadOnlyCollection<object> LifetimeRoots { get; } = [new object()];

        public void Prepare() => prepared = true;

        public void Activate()
        {
            Assert.True(prepared);
            IsActive = true;
        }

        public void Disable() => IsActive = false;
    }

    public sealed class RecordingLog : IModLog
    {
        public List<string> Infos { get; } = [];
        public List<string> Errors { get; } = [];
        public void Info(string message) => Infos.Add(message);
        public void Error(string message) => Errors.Add(message);
    }

    public sealed class RecordingFatalError : IAccessibleFatalError
    {
        public List<string> Messages { get; } = [];
        public void Show(string message) => Messages.Add(message);
    }
}
