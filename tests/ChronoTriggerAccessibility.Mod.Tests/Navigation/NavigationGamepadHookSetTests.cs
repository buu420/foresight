using System.Runtime.InteropServices;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Mod.Tests.Bootstrap;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.Enums;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NavigationGamepadHookSetTests
{
    [Fact]
    public void VerifiedPostCallHookFiltersBeforeMappingAndNeverReadsAFailedOrRetiredBuffer()
    {
        var factory = new Factory();
        var calls = new List<(uint Id, bool Connected)>();
        var suspended = false;
        var set = new NavigationGamepadHookSet(factory,
            new NavigationJoystick((id, _, _, connected) => { calls.Add((id, connected)); return true; }),
            () => suspended = true);
        var log = new AccessibilityRuntimeTests.RecordingLog();
        var fatal = new AccessibilityRuntimeTests.RecordingFatalError();
        var contract = GameVersionCatalog.Get(HookId.GameJoystickStateFilter);
        set.Prepare(new Build(0x400000, new Dictionary<HookId, nuint> { [contract.Id] = 0x400000 + contract.Rva }),
            new UnmanagedBoundaryGuard(log, fatal));
        Assert.Equal(0x58F0D5u, factory.Address);
        Assert.Equal(AsmHookBehaviour.ExecuteFirst, factory.Options!.Value.Behaviour);
        Assert.Equal(7, factory.Options.Value.HookLength);
        Assert.Equal(new[] { "use32", "pushfd", "pushad", "push eax", "lea eax, [ebp-0x138]", "push eax",
            "push ebx", "call callback", "add esp, 12", "popad", "popfd" }, factory.Code);
        factory.Callback!(0, 0, 0); // not active; no pointer use
        set.AfterHooksActivated();
        factory.Callback(2, 0, 167); // invalid snapshot on failed joyGetPosEx
        Assert.Equal([(2u, false)], calls);
        var native = Marshal.AllocHGlobal(52);
        try
        {
            Marshal.Copy(new[] { 52, 255, 65535, 32768, 0, 0, 0, 0, 1, 1, 18000, 0, 0 }, 0, native, 13);
            factory.Callback(2, native, 0);
            Assert.Equal(0, Marshal.ReadInt32(native, 0x20));
            Assert.Equal(32768, Marshal.ReadInt32(native, 8));
            Assert.Equal(65535, Marshal.ReadInt32(native, 0x28));
        }
        finally { Marshal.FreeHGlobal(native); }
        set.AfterHooksDisabled();
        factory.Callback(0, 0, 0);
        Assert.True(suspended);
        Assert.Equal(2, calls.Count);
        Assert.Empty(log.Errors); Assert.Empty(fatal.Messages);
    }

    [Fact]
    public void ManagedFailureCannotUnwindThroughTheNativeJoystickPoll()
    {
        var factory = new Factory();
        var set = new NavigationGamepadHookSet(factory,
            new NavigationJoystick((_, _, _, _) => throw new InvalidOperationException("test failure")), () => { });
        var log = new AccessibilityRuntimeTests.RecordingLog();
        var fatal = new AccessibilityRuntimeTests.RecordingFatalError();
        var contract = GameVersionCatalog.Get(HookId.GameJoystickStateFilter);
        set.Prepare(new Build(0x400000, new Dictionary<HookId, nuint> { [contract.Id] = 0x400000 + contract.Rva }),
            new UnmanagedBoundaryGuard(log, fatal));
        set.AfterHooksActivated();
        factory.Callback!(0, 0, 167);
        factory.Callback(0, 0, 0); // faulted callbacks must skip pointers too
        Assert.Single(log.Errors); Assert.Single(fatal.Messages);
    }

    private sealed record Build(nuint ImageBaseAddress, IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;
    private sealed class Factory : IRuntimeNativeAsmHookFactory
    {
        public GameJoystickStateProbeDelegate? Callback;
        public nuint Address;
        public RuntimeAsmHookOptions? Options;
        public IReadOnlyList<string>? Code;
        public IPreparedHook CreateAsmHook<T>(HookId id, string name, T callback, nuint address,
            Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly, RuntimeAsmHookOptions options) where T : Delegate
        {
            Assert.Equal(HookId.GameJoystickStateFilter, id);
            Callback = Assert.IsType<GameJoystickStateProbeDelegate>(callback); Address = address; Options = options;
            Code = buildAssembly(new RuntimeAsmHookAssemblyContext("call callback", "push eax", "pop eax"));
            return new Prepared(name, [callback, Code]);
        }
    }
    private sealed class Prepared(string name, IReadOnlyCollection<object> roots) : IPreparedHook
    {
        public string Name => name;
        public bool IsActive { get; private set; }
        public IReadOnlyCollection<object> LifetimeRoots => roots;
        public void Activate() => IsActive = true;
        public void Disable() => IsActive = false;
    }
}
