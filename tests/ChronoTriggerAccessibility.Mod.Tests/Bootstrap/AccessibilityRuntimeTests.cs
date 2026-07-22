using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Native.Hooks;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class AccessibilityRuntimeTests
{
    public static IReadOnlyList<HookContract> TestHookCatalog { get; } =
    [
        new(
            HookId.TextManagerGetMsg,
            "first",
            0x10,
            [0x90],
            typeof(Action),
            X86CallingConvention.MicrosoftThiscall),
        new(
            HookId.SceneManagerCreate,
            "second",
            0x20,
            [0x90],
            typeof(Action),
            X86CallingConvention.MicrosoftFastcall),
    ];

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

    [Fact]
    public void SuccessfulInitializationLogsEveryVerifiedPreparedAndActivatedRequiredHookInPhaseOrder()
    {
        var scenario = new RuntimeScenario(useCatalogHooks: true);

        scenario.Runtime.Initialize();

        var identityLog = Assert.Single(
            scenario.Log.Infos,
            message => message.StartsWith("Supported executable verified:", StringComparison.Ordinal));
        Assert.Contains(GameVersionCatalog.Executable.Sha256, identityLog, StringComparison.Ordinal);
        Assert.Contains(GameVersionCatalog.Executable.Machine.ToString(), identityLog, StringComparison.Ordinal);
        Assert.Contains("0x00400000", identityLog, StringComparison.Ordinal);
        Assert.Contains($"{GameVersionCatalog.Hooks.Count} required hook byte contracts", identityLog, StringComparison.Ordinal);

        var verifiedLogs = scenario.Log.Infos
            .Where(message => message.StartsWith("Hook byte verification passed:", StringComparison.Ordinal))
            .ToArray();
        var preparedLogs = scenario.Log.Infos
            .Where(message => message.StartsWith("Hook preparation verified:", StringComparison.Ordinal))
            .ToArray();
        var activatedLogs = scenario.Log.Infos
            .Where(message => message.StartsWith("Hook activation verified:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(GameVersionCatalog.Hooks.Count, verifiedLogs.Length);
        Assert.Equal(GameVersionCatalog.Hooks.Count, preparedLogs.Length);
        Assert.Equal(GameVersionCatalog.Hooks.Count, activatedLogs.Length);
        foreach (var contract in GameVersionCatalog.Hooks)
        {
            var address = checked((nuint)0x00400000 + contract.Rva);
            var expectedIdentity = $"'{contract.Symbol}' at 0x{address:X8}";
            Assert.Contains(verifiedLogs, message => message.Contains(expectedIdentity, StringComparison.Ordinal));
            Assert.Contains(preparedLogs, message => message.Contains(expectedIdentity, StringComparison.Ordinal));
            Assert.Contains(activatedLogs, message => message.Contains(expectedIdentity, StringComparison.Ordinal));
        }

        var lastVerifyLog = scenario.Timeline.FindLastIndex(
            item => item.StartsWith("log:Hook byte verification passed:", StringComparison.Ordinal));
        var identityLogIndex = scenario.Timeline.FindIndex(
            item => item.StartsWith("log:Supported executable verified:", StringComparison.Ordinal));
        var firstPrepare = scenario.Timeline.FindIndex(
            item => item.StartsWith("prepare:", StringComparison.Ordinal));
        var lastPrepare = scenario.Timeline.FindLastIndex(
            item => item.StartsWith("prepare:", StringComparison.Ordinal));
        var firstPreparationLog = scenario.Timeline.FindIndex(
            item => item.StartsWith("log:Hook preparation verified:", StringComparison.Ordinal));
        var lastPreparationLog = scenario.Timeline.FindLastIndex(
            item => item.StartsWith("log:Hook preparation verified:", StringComparison.Ordinal));
        var firstActivation = scenario.Timeline.FindIndex(
            item => item.StartsWith("activate:", StringComparison.Ordinal));
        var lastActivation = scenario.Timeline.FindLastIndex(
            item => item.StartsWith("activate:", StringComparison.Ordinal));
        var firstActivationLog = scenario.Timeline.FindIndex(
            item => item.StartsWith("log:Hook activation verified:", StringComparison.Ordinal));

        Assert.InRange(identityLogIndex, 1, firstPrepare - 1);
        Assert.InRange(lastVerifyLog, identityLogIndex + 1, firstPrepare - 1);
        Assert.InRange(firstPreparationLog, lastPrepare + 1, firstActivation - 1);
        Assert.InRange(lastPreparationLog, firstPreparationLog, firstActivation - 1);
        Assert.True(firstActivationLog > lastActivation);
    }

    [Fact]
    public void ActivationFailureNeverLogsAnActiveHookClaim()
    {
        var scenario = new RuntimeScenario(FailureStage.ActivateSecond, useCatalogHooks: true);

        scenario.Runtime.Initialize();

        Assert.DoesNotContain(
            scenario.Log.Infos,
            message => message.StartsWith("Hook activation verified:", StringComparison.Ordinal));
        Assert.DoesNotContain(
            scenario.Log.Infos,
            message => message.Equals("Accessibility runtime active.", StringComparison.Ordinal));
        Assert.Equal(AccessibilityRuntimeState.Faulted, scenario.Runtime.State);
    }

    [Fact]
    public void InactiveHookAfterActivationNeverLogsAnActiveHookClaim()
    {
        var scenario = new RuntimeScenario(
            useCatalogHooks: true,
            leaveLastHookInactive: true);

        scenario.Runtime.Initialize();

        Assert.DoesNotContain(
            scenario.Log.Infos,
            message => message.StartsWith("Hook activation verified:", StringComparison.Ordinal));
        Assert.DoesNotContain(
            scenario.Log.Infos,
            message => message.Equals("Accessibility runtime active.", StringComparison.Ordinal));
        Assert.Equal(AccessibilityRuntimeState.Faulted, scenario.Runtime.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingRequiredResolvedAddressesFaultsBeforeWindowOrHookPreparation(int addressCount)
    {
        var scenario = new RuntimeScenario(
            useCatalogHooks: true,
            exposedAddressCount: addressCount);

        scenario.Runtime.Initialize();

        Assert.Equal(["verify-executable"], scenario.Events);
        Assert.All(scenario.Hooks, hook => Assert.False(hook.IsActive));
        Assert.Equal(AccessibilityRuntimeState.Faulted, scenario.Runtime.State);
        Assert.Single(scenario.Log.Errors);
        Assert.Single(scenario.Fatal.Messages);
        Assert.Contains("hook addresses", scenario.Log.Errors[0], StringComparison.OrdinalIgnoreCase);
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
            processId: 1234,
            requiredHookContracts: TestHookCatalog);

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

        public RuntimeScenario(
            FailureStage failureStage = FailureStage.None,
            bool useCatalogHooks = false,
            int? exposedAddressCount = null,
            bool leaveLastHookInactive = false)
        {
            this.failureStage = failureStage;
            LeaveLastHookInactive = leaveLastHookInactive;
            var hookCatalog = useCatalogHooks ? GameVersionCatalog.Hooks : TestHookCatalog;
            Hooks = hookCatalog
                .Select(contract => new FakePreparedHook(contract.Symbol))
                .ToArray();
            Build = new FakeVerifiedGameBuild(hookCatalog, exposedAddressCount);
            Log = new RecordingLog(message => Timeline.Add($"log:{message}"));
            Fatal = new RecordingFatalError();
            Runtime = new AccessibilityRuntime(
                new FakeVerifier(this),
                new FakeWindowWaiter(this),
                new FakePrismFactory(this),
                new FakeHookInstaller(this),
                Log,
                Fatal,
                processId: 1234,
                requiredHookContracts: hookCatalog);
        }

        public List<string> Events { get; } = [];
        public List<string> Timeline { get; } = [];
        public FakePreparedHook[] Hooks { get; }
        public FakeVerifiedGameBuild Build { get; }
        public RecordingLog Log { get; }
        public RecordingFatalError Fatal { get; }
        public AccessibilityRuntime Runtime { get; }
        public int VerifierThreadId { get; private set; }
        public bool LeaveLastHookInactive { get; }

        private void Stage(FailureStage stage, string description)
        {
            Events.Add(description);
            Timeline.Add(description);
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
                return scenario.Build;
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
                for (var index = 0; index < scenario.Hooks.Length; index++)
                {
                    var hook = scenario.Hooks[index];
                    var stage = index == 0 ? FailureStage.PrepareFirst : FailureStage.PrepareSecond;
                    scenario.Stage(stage, $"prepare:{hook.Name}");
                    hook.Prepare();
                }
            }

            public void ActivateAll()
            {
                for (var index = 0; index < scenario.Hooks.Length; index++)
                {
                    var hook = scenario.Hooks[index];
                    var stage = index == 0 ? FailureStage.ActivateFirst : FailureStage.ActivateSecond;
                    scenario.Stage(stage, $"activate:{hook.Name}");
                    hook.Activate();
                    if (scenario.LeaveLastHookInactive && index == scenario.Hooks.Length - 1)
                    {
                        hook.Disable();
                    }
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
        public FakeVerifiedGameBuild(
            IReadOnlyList<HookContract>? hookCatalog = null,
            int? exposedAddressCount = null)
        {
            hookCatalog ??= TestHookCatalog;
            ImageBaseAddress = 0x00400000;
            HookAddresses = hookCatalog
                .Take(exposedAddressCount ?? hookCatalog.Count)
                .ToDictionary(
                    contract => contract.Id,
                    contract => checked(ImageBaseAddress + contract.Rva));
        }

        public nuint ImageBaseAddress { get; }
        public IReadOnlyDictionary<HookId, nuint> HookAddresses { get; }
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

    public sealed class RecordingLog(Action<string>? onInfo = null) : IModLog
    {
        public List<string> Infos { get; } = [];
        public List<string> Errors { get; } = [];
        public void Info(string message)
        {
            Infos.Add(message);
            onInfo?.Invoke(message);
        }
        public void Error(string message) => Errors.Add(message);
    }

    public sealed class RecordingFatalError : IAccessibleFatalError
    {
        public List<string> Messages { get; } = [];
        public void Show(string message) => Messages.Add(message);
    }
}
