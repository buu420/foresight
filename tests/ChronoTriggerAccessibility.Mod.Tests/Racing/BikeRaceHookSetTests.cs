using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Racing;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Racing;

public sealed class BikeRaceHookSetTests
{
    [Fact] public void PreservesByteReturnAndOriginalOrderAndClosesBeforeNativeDestruction()
    {
        var calls = new List<string>(); var factory = new Factory(calls); var errors = new Errors();
        var hooks = new BikeRaceHookSet(factory, p => calls.Add($"capture:{p}"), p => calls.Add($"close:{p}"),
            p => Assert.Equal((nuint)0x400000, p), () => calls.Add("disable"));
        var build = new BuildData(0x400000, GameVersionCatalog.Hooks.ToDictionary(x => x.Id, x => (nuint)(0x400000 + x.Rva)));
        foreach (var registration in hooks.Registrations) registration.Prepare(build, new(errors, errors));
        hooks.AfterHooksActivated();
        var update = (BikeRaceUpdateDelegate)factory.Detours[HookId.BikeRaceUpdate];
        var destroy = (BikeRaceDestructorDelegate)factory.Detours[HookId.BikeRaceDestructor];
        Assert.Equal((byte)0, update(42));
        factory.Result = 0xA5; Assert.Equal((byte)0xA5, update(42));
        destroy(42); hooks.AfterHooksDisabled(); update(43); destroy(43);
        Assert.Equal(["update:42", "capture:42", "update:42", "close:42", "close:42", "destroy:42", "disable", "update:43", "destroy:43"], calls);
        Assert.Empty(errors.Messages);
    }
    [Fact] public void RefusesActivationWithoutBothVerifiedHooks()
    {
        var hooks = new BikeRaceHookSet(new Factory([]), _ => { }, _ => { }, _ => { }, () => { });
        Assert.Throws<InvalidOperationException>(hooks.AfterHooksActivated);
    }
    private sealed record BuildData(nuint ImageBaseAddress, IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;
    private sealed class Errors : IModLog, IAccessibleFatalError
    {
        public List<string> Messages { get; } = [];
        public void Info(string m) { }
        public void Error(string m) => Messages.Add(m);
        public void Show(string m) => Messages.Add(m);
    }
    private sealed class Factory(List<string> calls) : IRuntimeNativeHookFactory
    {
        public byte Result;
        public Dictionary<HookId, Delegate> Detours { get; } = [];
        public IHook<T> CreateHook<T>(HookId id, T detour, nuint address) where T : Delegate
        {
            Detours.Add(id, detour);
            Delegate original = id == HookId.BikeRaceUpdate
                ? new BikeRaceUpdateDelegate(p => { calls.Add($"update:{p}"); return Result; })
                : new BikeRaceDestructorDelegate(p => calls.Add($"destroy:{p}"));
            return new Hook<T>((T)original);
        }
    }
    private sealed class Hook<T>(T original) : IHook<T> where T : Delegate
    {
        public T OriginalFunction => original;
        public IReverseWrapper<T> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 1;
        public nint OriginalFunctionWrapperAddress => 2;
        public IHook<T> Activate() { IsHookActivated = IsHookEnabled = true; return this; }
        IHook IHook.Activate() => Activate();
        public void Enable() => IsHookEnabled = true;
        public void Disable() => IsHookEnabled = false;
    }
}
