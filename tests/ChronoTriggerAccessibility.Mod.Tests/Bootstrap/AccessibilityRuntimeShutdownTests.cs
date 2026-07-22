using System.Collections.Concurrent;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Native.Hooks;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class AccessibilityRuntimeShutdownTests
{
    [Fact]
    public void ShutdownDisablesHooksBeforeDisposingPrismAndIsIdempotent()
    {
        var scenario = new ShutdownScenario();
        scenario.Runtime.Initialize();

        scenario.Runtime.Shutdown();
        scenario.Runtime.Shutdown();

        Assert.Equal(["disable-hooks", "dispose-prism"], scenario.TeardownEvents);
        Assert.Equal(1, scenario.Installer.DisableCount);
        Assert.Equal(1, scenario.PrismSession.DisposeCount);
        Assert.Equal(AccessibilityRuntimeState.Stopped, scenario.Runtime.State);
    }

    [Theory]
    [InlineData(BlockingStage.Wait)]
    [InlineData(BlockingStage.Prism)]
    [InlineData(BlockingStage.Prepare)]
    [InlineData(BlockingStage.Activate)]
    public async Task ShutdownRacingInitializationLeavesNoActiveHookAndNoDoubleDisposal(BlockingStage stage)
    {
        var scenario = new ShutdownScenario(stage);
        var initialization = scenario.Runtime.StartInBackground();
        Assert.True(scenario.StageEntered.Wait(TimeSpan.FromSeconds(5)), $"{stage} was never entered.");

        var shutdown = Task.Run(scenario.Runtime.Shutdown);
        Assert.True(
            SpinWait.SpinUntil(() => scenario.ObservedCancellation.IsCancellationRequested, TimeSpan.FromSeconds(5)),
            "Shutdown did not cancel initialization.");
        scenario.ReleaseStage.Set();

        await Task.WhenAll(initialization, shutdown);

        Assert.All(scenario.Installer.Hooks, hook => Assert.False(hook.IsActive));
        Assert.InRange(scenario.PrismSession.DisposeCount, 0, 1);
        Assert.Equal(AccessibilityRuntimeState.Stopped, scenario.Runtime.State);
        Assert.Empty(scenario.Fatal.Messages);
        if (stage is BlockingStage.Wait or BlockingStage.Prism or BlockingStage.Prepare)
        {
            Assert.Equal(0, scenario.Installer.ActivateCount);
        }
    }

    [Fact]
    public void ReloadedDisposingIsOverriddenByMod()
    {
        var method = typeof(global::ChronoTriggerAccessibility.Mod.Mod).GetMethod(nameof(global::ChronoTriggerAccessibility.Mod.Mod.Disposing));

        Assert.NotNull(method);
        Assert.Equal(typeof(global::ChronoTriggerAccessibility.Mod.Mod), method.DeclaringType);
    }

    [Fact]
    public void ResidualActiveHookKeepsPrismAliveAndReportsFatalIntegrityFailure()
    {
        var hook = new ResidualHook();
        var installer = new ResidualActiveInstaller(hook);
        var prism = new StandalonePrismSession();
        var fatal = new AccessibilityRuntimeTests.RecordingFatalError();
        var runtime = new AccessibilityRuntime(
            new StandaloneVerifier(),
            new StandaloneWaiter(),
            new StandalonePrismFactory(prism),
            installer,
            new AccessibilityRuntimeTests.RecordingLog(),
            fatal,
            processId: 1234,
            requiredHookContracts: [AccessibilityRuntimeTests.TestHookCatalog[0]]);
        runtime.Initialize();

        runtime.Shutdown();
        runtime.Shutdown();

        Assert.True(hook.IsActive);
        Assert.Equal(0, prism.DisposeCount);
        Assert.Equal(AccessibilityRuntimeState.Faulted, runtime.State);
        Assert.Single(fatal.Messages);
        Assert.Contains("residual active hook", fatal.Messages[0], StringComparison.OrdinalIgnoreCase);
    }

    public enum BlockingStage
    {
        None,
        Wait,
        Prism,
        Prepare,
        Activate,
    }

    private sealed class ShutdownScenario
    {
        private readonly BlockingStage blockingStage;

        public ShutdownScenario(BlockingStage blockingStage = BlockingStage.None)
        {
            this.blockingStage = blockingStage;
            Installer = new BlockingInstaller(this);
            PrismSession = new RecordingPrismSession(this);
            Runtime = new AccessibilityRuntime(
                new SuccessfulVerifier(),
                new BlockingWindowWaiter(this),
                new BlockingPrismFactory(this),
                Installer,
                new AccessibilityRuntimeTests.RecordingLog(),
                Fatal,
                processId: 1234,
                requiredHookContracts: AccessibilityRuntimeTests.TestHookCatalog);
        }

        public AccessibilityRuntime Runtime { get; }
        public BlockingInstaller Installer { get; }
        public RecordingPrismSession PrismSession { get; }
        public AccessibilityRuntimeTests.RecordingFatalError Fatal { get; } = new();
        public ManualResetEventSlim StageEntered { get; } = new(false);
        public ManualResetEventSlim ReleaseStage { get; } = new(false);
        public CancellationToken ObservedCancellation { get; private set; }
        public List<string> TeardownEvents { get; } = [];

        private void Block(BlockingStage stage, CancellationToken cancellationToken = default)
        {
            if (blockingStage != stage)
            {
                return;
            }

            StageEntered.Set();
            if (stage == BlockingStage.Wait)
            {
                ReleaseStage.Wait(cancellationToken);
            }
            else if (!ReleaseStage.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException($"Test did not release {stage}.");
            }
        }

        private sealed class SuccessfulVerifier : IRuntimeExecutableVerifier
        {
            public IVerifiedGameBuild VerifyCurrentProcess() => new VerifiedBuild();
        }

        private sealed class BlockingWindowWaiter(ShutdownScenario scenario) : IGameWindowWaiter
        {
            public nint WaitForSoleVisibleWindow(int processId, CancellationToken cancellationToken)
            {
                scenario.ObservedCancellation = cancellationToken;
                scenario.Block(BlockingStage.Wait, cancellationToken);
                return (nint)42;
            }
        }

        private sealed class BlockingPrismFactory(ShutdownScenario scenario) : IRuntimePrismFactory
        {
            public IRuntimePrismSession Create()
            {
                scenario.Block(BlockingStage.Prism);
                return scenario.PrismSession;
            }
        }

        public sealed class BlockingInstaller(ShutdownScenario scenario) : IRuntimeHookInstaller
        {
            public FakeHook[] Hooks { get; } = [new("first"), new("second")];
            public IReadOnlyList<IPreparedHook> PreparedHooks => Hooks;
            public int ActivateCount { get; private set; }
            public int DisableCount { get; private set; }

            public void PrepareAll(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
            {
                scenario.Block(BlockingStage.Prepare);
                foreach (var hook in Hooks)
                {
                    hook.Prepared = true;
                }
            }

            public void ActivateAll()
            {
                scenario.Block(BlockingStage.Activate);
                ActivateCount++;
                foreach (var hook in Hooks)
                {
                    hook.IsActive = true;
                }
            }

            public void DisableAll()
            {
                DisableCount++;
                scenario.TeardownEvents.Add("disable-hooks");
                foreach (var hook in Hooks)
                {
                    hook.IsActive = false;
                }
            }
        }

        public sealed class RecordingPrismSession(ShutdownScenario scenario) : IRuntimePrismSession
        {
            public string BackendName => "Test backend";
            public int DisposeCount { get; private set; }
            public void Dispose()
            {
                DisposeCount++;
                scenario.TeardownEvents.Add("dispose-prism");
            }
        }

        public sealed class FakeHook(string name) : IPreparedHook
        {
            public string Name { get; } = name;
            public bool Prepared { get; set; }
            public bool IsActive { get; set; }
            public IReadOnlyCollection<object> LifetimeRoots { get; } = [];
            public void Activate() => IsActive = true;
            public void Disable() => IsActive = false;
        }

        private sealed class VerifiedBuild : IVerifiedGameBuild
        {
            public nuint ImageBaseAddress => 0x00400000;
            public IReadOnlyDictionary<HookId, nuint> HookAddresses { get; } =
                AccessibilityRuntimeTests.TestHookCatalog.ToDictionary(
                    contract => contract.Id,
                    contract => checked((nuint)0x00400000 + contract.Rva));
        }
    }

    private sealed class StandaloneVerifier : IRuntimeExecutableVerifier
    {
        public IVerifiedGameBuild VerifyCurrentProcess() => new StandaloneBuild();
    }

    private sealed class StandaloneWaiter : IGameWindowWaiter
    {
        public nint WaitForSoleVisibleWindow(int processId, CancellationToken cancellationToken) => (nint)42;
    }

    private sealed class StandalonePrismFactory(StandalonePrismSession session) : IRuntimePrismFactory
    {
        public IRuntimePrismSession Create() => session;
    }

    private sealed class StandalonePrismSession : IRuntimePrismSession
    {
        public string BackendName => "Test backend";
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class ResidualActiveInstaller(ResidualHook hook) : IRuntimeHookInstaller
    {
        public IReadOnlyList<IPreparedHook> PreparedHooks => [hook];
        public void PrepareAll(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary) { }
        public void ActivateAll() => hook.IsActive = true;
        public void DisableAll() => throw new InvalidOperationException("residual active hook: first");
    }

    private sealed class ResidualHook : IPreparedHook
    {
        public string Name => "first";
        public bool IsActive { get; set; }
        public IReadOnlyCollection<object> LifetimeRoots { get; } = [];
        public void Activate() => IsActive = true;
        public void Disable() { }
    }

    private sealed class StandaloneBuild : IVerifiedGameBuild
    {
        public nuint ImageBaseAddress => 0x00400000;
        public IReadOnlyDictionary<HookId, nuint> HookAddresses { get; } =
            new Dictionary<HookId, nuint>
            {
                [AccessibilityRuntimeTests.TestHookCatalog[0].Id] =
                    checked((nuint)0x00400000 + AccessibilityRuntimeTests.TestHookCatalog[0].Rva),
            };
    }
}
