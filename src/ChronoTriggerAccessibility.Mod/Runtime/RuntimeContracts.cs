using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Mod.Runtime;

public interface IVerifiedGameBuild
{
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
