using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using SharedReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ChronoTriggerAccessibility.Mod.Startup;

public interface IRuntimeNativeHookFactory
{
    IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
        where TDelegate : Delegate;
}

public sealed class ReloadedNativeHookFactory(SharedReloadedHooks hooks) : IRuntimeNativeHookFactory
{
    private readonly SharedReloadedHooks hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));

    public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
        where TDelegate : Delegate
    {
        _ = id;
        ArgumentNullException.ThrowIfNull(detour);
        return hooks.CreateHook(detour, checked((long)address));
    }
}
