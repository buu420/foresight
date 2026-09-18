using System.Linq.Expressions;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Menus;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Menus;

public sealed class ShopHookSetTests
{
    [Fact] public void HooksForwardNativeArgumentsAndDestructorReturnWhileActiveAndDisabled()
    {
        var calls = new List<string>(); var factory = new Factory(calls); var errors = new Errors();
        var hooks = new ShopHookSet(factory, new EmptyMemory(), new Dispatcher(), () => true);
        var build = new BuildData(0x400000, GameVersionCatalog.Hooks.ToDictionary(x => x.Id, x => (nuint)(0x400000u + x.Rva)));
        foreach (var registration in hooks.Registrations) registration.Prepare(build, new(errors, errors));
        hooks.AfterHooksActivated();
        ((ShopSceneUpdateDelegate)factory.Detours[HookId.ShopSceneUpdate])(100, 2.0f);
        Assert.Equal((nint)12345, ((ShopSceneDestructorDelegate)factory.Detours[HookId.ShopSceneDestructor])(100, 1));
        hooks.AfterHooksDisabled();
        ((ShopSceneUpdateDelegate)factory.Detours[HookId.ShopSceneUpdate])(101, 3.0f);
        Assert.Equal((nint)12345, ((ShopSceneDestructorDelegate)factory.Detours[HookId.ShopSceneDestructor])(101, 0));
        Assert.Equal(["ShopSceneUpdate:100,2", "ShopSceneDestructor:100,1", "ShopSceneUpdate:101,3", "ShopSceneDestructor:101,0"], calls);
        Assert.Empty(errors.Messages);
    }
    [Fact] public void RequiresBothVerifiedBoundariesBeforeActivation()
    {
        var hooks = new ShopHookSet(new Factory([]), new EmptyMemory(), new Dispatcher(), () => true);
        Assert.Throws<InvalidOperationException>(hooks.AfterHooksActivated);
    }
    private sealed class Dispatcher : ISemanticEventDispatcher
    {
        public int Generation => 0;
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent value) { }
        public void ReportCoverageFailure(string message) { }
    }
    private sealed record BuildData(nuint ImageBaseAddress, IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;
    private sealed class EmptyMemory : IReadableMemory { public bool TryRead(nuint p, Span<byte> b) => false; }
    private sealed class Errors : IModLog, IAccessibleFatalError
    {
        public List<string> Messages { get; } = [];
        public void Info(string m) { }
        public void Error(string m) => Messages.Add(m);
        public void Show(string m) => Messages.Add(m);
    }
    private sealed class Factory(List<string> calls) : IRuntimeNativeHookFactory
    {
        public Dictionary<HookId, Delegate> Detours { get; } = [];
        public IHook<T> CreateHook<T>(HookId id, T detour, nuint address) where T : Delegate
        {
            Detours.Add(id, detour);
            var invoke = typeof(T).GetMethod("Invoke")!;
            var p = invoke.GetParameters().Select(x => Expression.Parameter(x.ParameterType)).ToArray();
            var record = new Action<long[]>(v => calls.Add($"{id}:{string.Join(',', v)}"));
            Expression body = Expression.Invoke(Expression.Constant(record), Expression.NewArrayInit(typeof(long),
                p.Select(x => Expression.Convert(x, typeof(long)))));
            if (invoke.ReturnType == typeof(nint)) body = Expression.Block(body, Expression.Constant((nint)12345));
            return new Hook<T>(Expression.Lambda<T>(body, p).Compile());
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
