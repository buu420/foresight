using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class BootstrapReadinessTests
{
    [Fact]
    public void ActiveRuntimeSignalsTheExistingEventForThisProcess()
    {
        var name = ValidEventName();
        using var ready = CreateEvent(name);
        var log = new RecordingLog();

        Assert.True(BootstrapReadiness.SignalWhenActive(AccessibilityRuntimeState.Active, name, log));

        Assert.True(ready.WaitOne(0));
        Assert.Single(log.Infos);
        Assert.Empty(log.Errors);
    }

    [Theory]
    [InlineData(AccessibilityRuntimeState.Created)]
    [InlineData(AccessibilityRuntimeState.Initializing)]
    [InlineData(AccessibilityRuntimeState.Faulted)]
    [InlineData(AccessibilityRuntimeState.Stopped)]
    public void InactiveRuntimeDoesNotSignal(AccessibilityRuntimeState state)
    {
        var name = ValidEventName();
        using var ready = CreateEvent(name);
        var log = new RecordingLog();

        Assert.False(BootstrapReadiness.SignalWhenActive(state, name, log));

        Assert.False(ready.WaitOne(0));
        Assert.Single(log.Errors);
    }

    [Fact]
    public void EventForAnotherProcessIsNotSignaled()
    {
        var name = $@"Local\Foresight.Managed.{Environment.ProcessId + 1}.{Guid.NewGuid():B}";
        AssertRejected(name);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{00000000-0000-0000-0000-000000000000}")]
    [InlineData("{not-a-valid-guid-with-38-characters!}")]
    [InlineData("{11111111-2222-3333-4444-555555555555}suffix")]
    public void InvalidLaunchIdentifierIsNotSignaled(string identifier)
    {
        AssertRejected($@"Local\Foresight.Managed.{Environment.ProcessId}.{identifier}");
    }

    [Fact]
    public void BootstrapInjectionEventCannotSubstituteForManagedReadiness()
    {
        AssertRejected($@"Local\Foresight.Bootstrap.{Environment.ProcessId}.{Guid.NewGuid():B}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LegacyLaunchWithoutAnEventIsANoOp(string? eventName)
    {
        var log = new RecordingLog();

        Assert.False(BootstrapReadiness.SignalWhenActive(AccessibilityRuntimeState.Active, eventName, log));

        Assert.Empty(log.Infos);
        Assert.Empty(log.Errors);
    }

    [Fact]
    public void MissingEventIsReportedWithoutCreatingOne()
    {
        var name = ValidEventName();
        var log = new RecordingLog();

        Assert.False(BootstrapReadiness.SignalWhenActive(AccessibilityRuntimeState.Active, name, log));

        Assert.False(EventWaitHandle.TryOpenExisting(name, out var unexpected));
        unexpected?.Dispose();
        Assert.Single(log.Errors);
    }

    [Fact]
    public async Task AcknowledgementWaitsForInitializationAndThenReadsTheCurrentState()
    {
        var name = ValidEventName();
        using var ready = CreateEvent(name);
        var log = new RecordingLog();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = AccessibilityRuntimeState.Initializing;
        var pending = BootstrapReadiness.SignalAfterInitializationAsync(initialized.Task, () => state, name, log);

        Assert.False(pending.IsCompleted);
        Assert.False(ready.WaitOne(0));
        state = AccessibilityRuntimeState.Active;
        initialized.SetResult();
        await pending;

        Assert.True(ready.WaitOne(0));
        Assert.Empty(log.Errors);
    }

    [Fact]
    public async Task FailedInitializationDoesNotAcknowledgeEvenIfStateSaysActive()
    {
        var name = ValidEventName();
        using var ready = CreateEvent(name);
        var log = new RecordingLog();

        await BootstrapReadiness.SignalAfterInitializationAsync(
            Task.FromException(new InvalidOperationException("Startup failed")),
            () => AccessibilityRuntimeState.Active, name, log);

        Assert.False(ready.WaitOne(0));
        Assert.Contains("Startup failed", Assert.Single(log.Errors), StringComparison.Ordinal);
    }

    private static string ValidEventName() => $@"Local\Foresight.Managed.{Environment.ProcessId}.{Guid.NewGuid():B}";

    private static EventWaitHandle CreateEvent(string name) => new(false, EventResetMode.ManualReset, name);

    private static void AssertRejected(string name)
    {
        using var ready = CreateEvent(name);
        var log = new RecordingLog();
        Assert.False(BootstrapReadiness.SignalWhenActive(AccessibilityRuntimeState.Active, name, log));
        Assert.False(ready.WaitOne(0));
        Assert.Single(log.Errors);
    }

    private sealed class RecordingLog : IModLog
    {
        public List<string> Infos { get; } = [];
        public List<string> Errors { get; } = [];
        public void Info(string message) => Infos.Add(message);
        public void Error(string message) => Errors.Add(message);
    }
}
