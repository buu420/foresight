using System.Collections.ObjectModel;
using System.Diagnostics;
using ChronoTriggerAccessibility.Native.Build;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Mod.Runtime;

public sealed class CurrentProcessExecutableVerifier : IRuntimeExecutableVerifier
{
    private readonly ExecutableVerifier verifier = new();

    public IVerifiedGameBuild VerifyCurrentProcess()
    {
        using var process = Process.GetCurrentProcess();
        var module = process.MainModule
            ?? throw new InvalidOperationException("Could not resolve the loaded main module.");
        var executablePath = module.FileName;
        var result = verifier.Verify(executablePath, GameVersionCatalog.Executable, GameVersionCatalog.Hooks);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Unsupported or modified Chrono Trigger executable: " + string.Join(" ", result.Failures));
        }

        var verified = result.VerifiedExecutable
            ?? throw new InvalidOperationException("Executable verification succeeded without a verified image.");
        var addresses = verified.ResolveInProcessAddresses((nuint)module.BaseAddress);
        return new VerifiedGameBuild(new ReadOnlyDictionary<HookId, nuint>(
            new Dictionary<HookId, nuint>(addresses)));
    }

    private sealed record VerifiedGameBuild(
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;
}
