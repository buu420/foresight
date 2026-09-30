using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Runtime;

namespace ChronoTriggerAccessibility.Mod.Startup;

/// <summary>Confirms that speech and hooks are active to the game-folder bootstrap monitor.</summary>
public static class BootstrapReadiness
{
    public const string EnvironmentVariable = "FORESIGHT_READY_EVENT";

    public static async Task SignalAfterInitializationAsync(
        Task initialization,
        Func<AccessibilityRuntimeState> currentState,
        string? eventName,
        IModLog log)
    {
        try
        {
            await initialization.ConfigureAwait(false);
            SignalWhenActive(currentState(), eventName, log);
        }
        catch (Exception exception)
        {
            // The native monitor owns the timeout and accessible startup error.
            log.Error($"Foresight startup acknowledgement failed: {exception}");
        }
    }

    public static bool SignalWhenActive(AccessibilityRuntimeState state, string? eventName, IModLog log)
    {
        // Manual/legacy Reloaded launches have no native monitor to acknowledge.
        if (string.IsNullOrEmpty(eventName)) return false;
        if (state != AccessibilityRuntimeState.Active)
        {
            log.Error($"Foresight startup was not acknowledged: accessibility runtime state is {state}.");
            return false;
        }

        var prefix = $@"Local\Foresight.Managed.{Environment.ProcessId}.";
        if (!eventName.StartsWith(prefix, StringComparison.Ordinal) ||
            eventName.Length != prefix.Length + 38 ||
            !Guid.TryParseExact(eventName.AsSpan(prefix.Length), "B", out var launchId) ||
            launchId == Guid.Empty)
        {
            log.Error("Foresight startup event does not identify this process and launch.");
            return false;
        }

        try
        {
            // Open only: a missing native event must not be replaced by a new one.
            using var ready = EventWaitHandle.OpenExisting(eventName);
            if (!ready.Set())
            {
                log.Error("Foresight startup event could not be signaled.");
                return false;
            }

            log.Info("Foresight managed readiness acknowledged: accessibility runtime is active.");
            return true;
        }
        catch (Exception exception)
        {
            log.Error($"Foresight startup event could not be signaled: {exception.Message}");
            return false;
        }
    }
}
