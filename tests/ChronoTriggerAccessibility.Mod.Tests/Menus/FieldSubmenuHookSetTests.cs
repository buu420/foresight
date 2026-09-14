using System.Linq.Expressions;
using System.Buffers.Binary;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Menus;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Menus;

public sealed class FieldSubmenuHookSetTests
{
    [Fact] public void EveryOriginalReceivesItsArgumentsExactlyOnceAndOpenKeepsItsReturnValue()
    {
        var calls = new List<string>(); var f = new Factory(calls); var errors = new Errors();
        var session = new FieldSubmenuSession(_ => null, _ => null, _ => false, () => true, _ => { }, _ => { });
        var hooks = new FieldSubmenuHookSet(f, new(new EmptyMemory()), session);
        Prepare(hooks, errors); hooks.AfterHooksActivated();
        ((SubmenuNodeWordDelegate)f.Detours[HookId.ClassicFieldMenuReplace])(100, 200);
        ((SubmenuNodeWordDelegate)f.Detours[HookId.TouchFieldMenuReplace])(101, 201);
        Assert.Equal((byte)1, ((SaveSlotOpenDelegate)f.Detours[HookId.SaveSlotOpen])(102, 5, 1));
        ((SubmenuManagerDispatchDelegate)f.Detours[HookId.MenuManagerDispatch])(103, 2, 19);
        ((SubmenuNodeWordDelegate)f.Detours[HookId.InventoryHelpRefresh])(104, 0x8001);
        ((SubmenuNodeWordDelegate)f.Detours[HookId.SaveSlotDetailsRefresh])(105, 0x12345678);
        ((SubmenuNodeWordDelegate)f.Detours[HookId.MenuManagerUpdate])(106, 0x3C888889);
        Assert.Equal(["ClassicFieldMenuReplace:100,200", "TouchFieldMenuReplace:101,201", "SaveSlotOpen:102,5,1",
            "MenuManagerDispatch:103,2,19", "InventoryHelpRefresh:104,32769", "SaveSlotDetailsRefresh:105,305419896",
            "MenuManagerUpdate:106,1015580809"], calls);
        Assert.Empty(errors.Messages);
    }
    [Fact] public void AFailedCloseCannotSkipNativeReplacementAndDisabledHooksKeepForwarding()
    {
        var calls = new List<string>(); var f = new Factory(calls); var errors = new Errors();
        var session = new FieldSubmenuSession(_ => new FieldSubmenuSnapshot("Inventory", "Inventory", "1", "Potion"),
            _ => "Inventory", _ => false, () => true,
            value => { if (value is ChronoTriggerAccessibility.Core.Menus.MenuExited) throw new InvalidOperationException("close failed"); }, _ => { });
        var hooks = new FieldSubmenuHookSet(f, new(new EmptyMemory()), session);
        Prepare(hooks, errors); hooks.AfterHooksActivated(); session.Enter(50);
        ((SubmenuNodeWordDelegate)f.Detours[HookId.ClassicFieldMenuReplace])(100, 200);
        Assert.Equal("ClassicFieldMenuReplace:100,200", Assert.Single(calls));
        Assert.NotEmpty(errors.Messages);
        hooks.AfterHooksDisabled();
        ((SubmenuNodeWordDelegate)f.Detours[HookId.InventoryHelpRefresh])(104, 0x8001);
        Assert.Equal("InventoryHelpRefresh:104,32769", calls[1]);
    }
    [Fact] public void UnrelatedManagerCallbacksCannotRefreshAnOpenSubmenu()
    {
        var calls = new List<string>(); var f = new Factory(calls); var errors = new Errors(); var captures = 0;
        var session = new FieldSubmenuSession(_ => { captures++; return new("Inventory", "Inventory", "potion", "Potion"); },
            _ => "Inventory", _ => false, () => true, _ => { }, _ => { });
        var memory = new WordMemory(new Dictionary<nuint, uint>
        {
            [1000] = 0x400000 + FieldSubmenuCapture.ClassicItemNodeVtableRva,
            [1000 + 0x2C0] = 2000, [2000] = 0x400000 + 0x3A5D04,
            [2004] = 3000, [2008] = 3004, [3000] = 4000,
            [4000] = 0x400000 + 0x3A5D0C, [5000] = 0x400000 + 0x3A5D0C,
        });
        var hooks = new FieldSubmenuHookSet(f, new(memory), session);
        Prepare(hooks, errors); hooks.AfterHooksActivated(); session.Enter(1000); Assert.Equal(1, captures);
        var callback = (SubmenuManagerDispatchDelegate)f.Detours[HookId.MenuManagerDispatch];
        callback(5000, 2, 1); Assert.Equal(1, captures);
        callback(4000, 2, 1); Assert.Equal(2, captures);
        Assert.Equal(2, calls.Count); Assert.Empty(errors.Messages);
    }
    private sealed class WordMemory(Dictionary<nuint, uint> words) : IReadableMemory
    {
        public bool TryRead(nuint p, Span<byte> b)
        {
            if (b.Length != 4 || !words.TryGetValue(p, out var value)) return false;
            BinaryPrimitives.WriteUInt32LittleEndian(b, value); return true;
        }
    }
    private static void Prepare(FieldSubmenuHookSet hooks, Errors errors)
    {
        var build = new BuildData(0x400000, GameVersionCatalog.Hooks.ToDictionary(x => x.Id, x => (nuint)(0x400000u + x.Rva)));
        var guard = new UnmanagedBoundaryGuard(errors, errors);
        foreach (var r in hooks.Registrations) r.Prepare(build, guard);
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
            if (invoke.ReturnType == typeof(byte)) body = Expression.Block(body, Expression.Constant((byte)1));
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
