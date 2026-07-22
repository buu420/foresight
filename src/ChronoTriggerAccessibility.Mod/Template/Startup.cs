using ChronoTriggerAccessibility.Mod.Diagnostics;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

namespace ChronoTriggerAccessibility.Mod.Template;

public sealed class Startup : IMod
{
    private IModLoader modLoader = null!;
    private ILogger logger = null!;
    private IModConfig modConfig = null!;
    private IReloadedHooks? hooks;
    private ModBase mod = new Mod();

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 config)
    {
        modLoader = (IModLoader)loaderApi;
        modConfig = (IModConfig)config;
        logger = (ILogger)modLoader.GetLogger();
        modLoader.GetController<IReloadedHooks>()?.TryGetTarget(out hooks!);

        if (hooks is null)
        {
            ReportStartupFailure(
                "Reloaded.SharedLib.Hooks was not available. The accessibility mod cannot safely install its hooks.");
            return;
        }

        try
        {
            mod = new Mod(new ModContext
            {
                Logger = logger,
                Hooks = hooks,
                ModLoader = modLoader,
                ModConfig = modConfig,
                Owner = this,
            });
        }
        catch (Exception exception)
        {
            ReportStartupFailure($"The accessibility mod could not start: {exception}");
        }
    }

    public void Suspend() => mod.Suspend();
    public void Resume() => mod.Resume();
    public void Unload() => mod.Unload();
    public bool CanUnload() => mod.CanUnload();
    public bool CanSuspend() => mod.CanSuspend();
    public Action Disposing => () => mod.Disposing();

    private void ReportStartupFailure(string message)
    {
        var log = new ModLog(logger, modConfig.ModId);
        try
        {
            log.Error(message);
        }
        finally
        {
            new AccessibleFatalError().Show(message);
        }
    }
}
