using System.Runtime.InteropServices;
using ChronoTriggerAccessibility.Mod.Battle;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Mod.Tests.Bootstrap;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.Enums;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Battle;

public sealed class BattleKeyboardHookSetTests
{
    [Fact]
    public void NativeBufferIsFilteredOnlyAfterActivationAndRetiresBeforePointerUse()
    {
        var factory = new Factory();
        var keyboard = new BattleKeyboard(_ => false, () => true);
        var inBattle = false;
        var set = new BattleKeyboardHookSet(factory, keyboard, () => inBattle);
        var log = new AccessibilityRuntimeTests.RecordingLog();
        var fatal = new AccessibilityRuntimeTests.RecordingFatalError();
        var contract = GameVersionCatalog.Get(HookId.GameKeyboardStateFilter);
        var build = new Build(0x400000, new Dictionary<HookId, nuint> { [contract.Id] = 0x400000 + contract.Rva });
        set.Prepare(build, new UnmanagedBoundaryGuard(log, fatal));
        Assert.Equal(0x58F58Bu, factory.Address);
        Assert.Equal(AsmHookBehaviour.ExecuteFirst, factory.Options!.Value.Behaviour);
        Assert.Equal(5, factory.Options.Value.HookLength);
        var native = Marshal.AllocHGlobal(256);
        try
        {
            var snapshot = new byte[256]; snapshot['H'] = 0x80; snapshot[0x10] = 0x80; snapshot['X'] = 0x80;
            Marshal.Copy(snapshot, 0, native, 256);
            factory.Callback!(native);
            Assert.Equal(0x80, Marshal.ReadByte(native, 'H'));
            set.AfterHooksActivated();
            factory.Callback(native);
            Assert.Equal(0x80, Marshal.ReadByte(native, 'H'));
            Marshal.Copy(new byte[256], 0, native, 256);
            factory.Callback(native);
            keyboard.Poll();
            inBattle = true;
            Marshal.Copy(snapshot, 0, native, 256);
            factory.Callback(native);
            Assert.Equal(0, Marshal.ReadByte(native, 'H'));
            Assert.Equal(0x80, Marshal.ReadByte(native, 'X'));
            Assert.Equal(0x80, Marshal.ReadByte(native, 0x10));
            set.AfterHooksDisabled();
            Marshal.Copy(snapshot, 0, native, 256);
            factory.Callback(native);
            Assert.Equal(0x80, Marshal.ReadByte(native, 'H'));
            factory.Callback(0); // Teardown must skip a retired native pointer completely.
        }
        finally { Marshal.FreeHGlobal(native); }
        Assert.Empty(log.Errors);
        Assert.Empty(fatal.Messages);
    }

    [Fact]
    public void FailedOwnerCheckLeavesTheNativeBufferIntactAndDoesNotEscape()
    {
        var factory = new Factory();
        var set = new BattleKeyboardHookSet(factory, new BattleKeyboard(_ => false, () => true),
            () => throw new InvalidOperationException("Owner unavailable"));
        var log = new AccessibilityRuntimeTests.RecordingLog();
        var fatal = new AccessibilityRuntimeTests.RecordingFatalError();
        var contract = GameVersionCatalog.Get(HookId.GameKeyboardStateFilter);
        set.Prepare(new Build(0x400000, new Dictionary<HookId, nuint> { [contract.Id] = 0x400000 + contract.Rva }),
            new UnmanagedBoundaryGuard(log, fatal));
        set.AfterHooksActivated();
        var native = Marshal.AllocHGlobal(256);
        try
        {
            var snapshot = new byte[256]; snapshot['H'] = 0x80; snapshot[0x10] = 0x80;
            Marshal.Copy(snapshot, 0, native, 256);
            factory.Callback!(native);
            var actual = new byte[256]; Marshal.Copy(native, actual, 0, 256);
            Assert.Equal(snapshot, actual);
            factory.Callback(0); // Faulted callbacks also skip the native pointer.
        }
        finally { Marshal.FreeHGlobal(native); }
        Assert.Single(fatal.Messages);
        Assert.Single(log.Errors);
    }

    private sealed record Build(nuint ImageBaseAddress, IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;
    private sealed class Factory : IRuntimeNativeAsmHookFactory
    {
        public GameKeyboardStateProbeDelegate? Callback;
        public nuint Address;
        public RuntimeAsmHookOptions? Options;
        public IPreparedHook CreateAsmHook<T>(HookId id, string name, T callback, nuint address,
            Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly, RuntimeAsmHookOptions options) where T : Delegate
        {
            Assert.Equal(HookId.GameKeyboardStateFilter, id);
            Callback = Assert.IsType<GameKeyboardStateProbeDelegate>(callback); Address = address; Options = options;
            var code = buildAssembly(new RuntimeAsmHookAssemblyContext("call callback", "push eax", "pop eax"));
            return new Prepared(name, [callback, code]);
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
