using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Mod.Runtime;

public interface IVerifiedGameBuild
{
    nuint ImageBaseAddress => 0;
    IReadOnlyDictionary<HookId, nuint> HookAddresses { get; }
}

public interface IRuntimeExecutableVerifier
{
    IVerifiedGameBuild VerifyCurrentProcess();
}

public interface IGameWindowWaiter
{
    nint WaitForSoleVisibleWindow(int processId, CancellationToken cancellationToken);
}

public interface IRuntimePrismFactory
{
    IRuntimePrismSession Create();
}

public interface IRuntimePrismSession : IDisposable
{
    string BackendName { get; }
    void Output(string text, bool interrupt) =>
        throw new NotSupportedException("This Prism session does not expose announcement output.");
    void Stop() => throw new NotSupportedException("This Prism session does not expose speech cancellation.");
}

public interface ISemanticEventDispatcher
{
    int Generation { get; }
    void Attach(IRuntimePrismSession session);
    void Detach(IRuntimePrismSession session);
    void Publish(AccessibilityEvent accessibilityEvent);
    void ReportCoverageFailure(string message);
    void RecordDiagnostic(string message) { }
}

public interface IRuntimeHookInstaller
{
    IReadOnlyList<IPreparedHook> PreparedHooks { get; }
    void PrepareAll(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary);
    void ActivateAll();
    void DisableAll();
}

public enum AccessibilityRuntimeState
{
    Created,
    Initializing,
    Active,
    Faulted,
    Stopped,
}
