using System.Linq.Expressions;
using ChronoTriggerAccessibility.Mod.Battle;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Battle;

public sealed class BattleHookSetTests
{
    [Fact]
    public void EveryBoundaryCallsOriginalOnceAndPreservesEveryRawArgument()
    {
        var calls = new List<string>(); var factory = new Factory(calls);
        var owner = Set(factory, calls); var guard = new UnmanagedBoundaryGuard(new Errors(), new Errors());
        foreach (var registration in owner.Registrations) registration.Prepare(Build(), guard);
        Assert.Equal(6, factory.Detours.Count);
        owner.AfterHooksActivated();
        ((BattleMenuMemberDelegate)factory.Detours[HookId.BattleHudRefresh])(100);
        ((BattleSevenWordDelegate)factory.Detours[HookId.BattleMessageDisplay])(100,1,2,3,4,5,6,7);
        ((BattleSevenWordDelegate)factory.Detours[HookId.BattleDamageNumber])(100,0x45,2,3,4,5,6,7);
        ((BattleMissDelegate)factory.Detours[HookId.BattleMiss])(100,0x46,12,13,14);
        ((BattleRenderDelegate)factory.Detours[HookId.BattleDamageRender])(100,21,22);
        ((BattleMenuMemberDelegate)factory.Detours[HookId.BattleMenuDestructor])(100);
        Assert.Equal([
            "original:BattleHudRefresh:100", "tick:100",
            "original:BattleMessageDisplay:100,1,2,3,4,5,6,7", "message:100",
            "original:BattleDamageNumber:100,69,2,3,4,5,6,7", "number:100:3",
            "original:BattleMiss:100,70,12,13,14", "miss:100:4",
            "original:BattleDamageRender:100,21,22", "render:100",
            "end:100", "original:BattleMenuDestructor:100"], calls);
    }

    [Fact]
    public void FailingTeardownCannotSkipNativeDestructorAndInactiveCallbacksDoNotCapture()
    {
        var calls = new List<string>(); var factory = new Factory(calls); var errors = new Errors();
        var owner = new BattleHookSet(factory, _ => calls.Add("tick"), _ => throw new InvalidOperationException("capture failure"),
            _ => { }, (_,_) => { }, (_,_) => { }, _ => { }, _ => { }, () => { });
        var guard = new UnmanagedBoundaryGuard(errors, errors);
        foreach (var registration in owner.Registrations) registration.Prepare(Build(), guard);
        owner.AfterHooksActivated();
        ((BattleMenuMemberDelegate)factory.Detours[HookId.BattleMenuDestructor])(100);
        Assert.Contains("original:BattleMenuDestructor:100", calls); Assert.True(guard.IsFaulted);
        owner.AfterHooksDisabled();
        ((BattleMenuMemberDelegate)factory.Detours[HookId.BattleHudRefresh])(100);
        Assert.DoesNotContain("tick", calls);
        Assert.Equal(2, calls.Count);
    }

    private static BattleHookSet Set(Factory factory, List<string> calls) => new(factory,
        x => calls.Add($"tick:{x}"), x => calls.Add($"end:{x}"), x => calls.Add($"message:{x}"),
        (x,s) => calls.Add($"number:{x}:{s}"), (x,s) => calls.Add($"miss:{x}:{s}"),
        x => calls.Add($"render:{x}"), _ => { }, () => { });
    private static IVerifiedGameBuild Build() => new BuildData(0x400000,
        GameVersionCatalog.Hooks.ToDictionary(x => x.Id, x => 0x400000u + (nuint)x.Rva));
    private sealed record BuildData(nuint ImageBaseAddress, IReadOnlyDictionary<HookId,nuint> HookAddresses) : IVerifiedGameBuild;
    private sealed class Errors : IModLog, IAccessibleFatalError
    {
        public void Info(string message) { }
        public void Error(string message) { }
        public void Show(string message) { }
    }
    private sealed class Factory(List<string> calls) : IRuntimeNativeHookFactory
    {
        public Dictionary<HookId,Delegate> Detours { get; } = [];
        public IHook<T> CreateHook<T>(HookId id, T detour, nuint address) where T : Delegate
        {
            Detours.Add(id, detour);
            var parameters = typeof(T).GetMethod("Invoke")!.GetParameters().Select(x => Expression.Parameter(x.ParameterType)).ToArray();
            var record = new Action<nint[]>(values => calls.Add($"original:{id}:{string.Join(',',values)}"));
            var body = Expression.Invoke(Expression.Constant(record), Expression.NewArrayInit(typeof(nint), parameters));
            return new Hook<T>(Expression.Lambda<T>(body, parameters).Compile());
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
