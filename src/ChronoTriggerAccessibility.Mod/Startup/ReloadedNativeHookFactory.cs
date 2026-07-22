using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using SharedReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ChronoTriggerAccessibility.Mod.Startup;

public interface IRuntimeNativeHookFactory
{
    IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
        where TDelegate : Delegate;
}

public interface IRuntimeNativeFunctionWrapperFactory
{
    TDelegate CreateWrapper<TDelegate>(nuint address)
        where TDelegate : Delegate;
}

public sealed class ReloadedNativeHookFactory(SharedReloadedHooks hooks) :
    IRuntimeNativeHookFactory,
    IRuntimeNativeFunctionWrapperFactory
{
    private readonly SharedReloadedHooks hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));

    public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
        where TDelegate : Delegate
    {
        _ = id;
        ArgumentNullException.ThrowIfNull(detour);
        return hooks.CreateHook(detour, checked((long)address));
    }

    public TDelegate CreateWrapper<TDelegate>(nuint address)
        where TDelegate : Delegate =>
        hooks.CreateWrapper<TDelegate>(checked((long)address), out _);
}
