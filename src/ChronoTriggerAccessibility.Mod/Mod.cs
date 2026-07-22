using System.Diagnostics;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Template;
using Reloaded.Hooks.ReloadedII.Interfaces;

namespace ChronoTriggerAccessibility.Mod;

public sealed class Mod : ModBase
{
    private readonly IReloadedHooks? hooks;
    private readonly AccessibilityRuntime? runtime;
    private readonly Task? initializationTask;

    public Mod(ModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        hooks = context.Hooks;

        var log = new ModLog(context.Logger, context.ModConfig.ModId);
        var fatalError = new AccessibleFatalError();
        var hookInstaller = new ReloadedHookInstaller(Array.Empty<IHookRegistration>());
        runtime = new AccessibilityRuntime(
            new CurrentProcessExecutableVerifier(),
            new GameWindowWaiter(),
            new PrismRuntimeFactory(),
            hookInstaller,
            log,
            fatalError,
            Process.GetCurrentProcess().Id);

        // Reloaded calls the mod entry point on the game's startup thread. Window discovery must
        // execute on the thread pool so waiting for a visible window cannot deadlock that thread.
        initializationTask = runtime.StartInBackground();
        log.Info("Accessibility initialization scheduled off the game startup thread.");
    }

    public Mod() { }
}
