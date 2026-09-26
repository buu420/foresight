using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Battle;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

public sealed class ReloadedNativeHookFactoryTests
{
    private const nuint VerifiedAddress = 0x0059768A;

    [Fact]
    public void CreateAsmHook_UsesOneManagedCallAndBuilderWithExactSafeOptionsWithoutActivation()
    {
        var controller = new RecordingAsmHookController();
        var factory = new ReloadedNativeHookFactory(controller);
        MsgWindowChoiceConfirmProbeDelegate callback = _ => { };
        var builderCalls = 0;

        var prepared = factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite,
            "Dialogue choice-confirm probe",
            callback,
            VerifiedAddress,
            context =>
            {
                builderCalls++;
                Assert.Equal(controller.AbsoluteCallMnemonic, context.AbsoluteCallMnemonic);
                Assert.Equal(controller.PushCallerSaved, context.PushCdeclCallerSavedRegisters);
                Assert.Equal(controller.PopCallerSaved, context.PopCdeclCallerSavedRegisters);
                return ChoiceConfirmProbeAssembly.Build(context);
            },
            ValidOptions);

        Assert.Equal(1, controller.ManagedCallCount);
        Assert.Equal(1, controller.PushCount);
        Assert.Equal(1, controller.PopCount);
        Assert.Equal(1, builderCalls);
        Assert.Equal(1, controller.AsmCreateCount);
        Assert.Equal(VerifiedAddress, controller.Address);
        Assert.Equal(ExpectedAssembly, controller.Code);
        Assert.NotNull(controller.Options);
        Assert.Equal(AsmHookBehaviour.ExecuteFirst, controller.Options.Behaviour);
        Assert.Equal(5, controller.Options.hookLength);
        Assert.True(controller.Options.PreferRelativeJump);
        Assert.Equal(5, controller.Options.MaxOpcodeSize);
        Assert.False(controller.Hook.IsEnabled);
        Assert.False(prepared.IsActive);
        Assert.Equal(0, controller.Hook.ActivateCount);
        Assert.Contains(callback, prepared.LifetimeRoots);
        Assert.Contains(controller.ReverseWrapper, prepared.LifetimeRoots);
        Assert.Contains(prepared.LifetimeRoots, root =>
            root is IReadOnlyList<string> code && code.SequenceEqual(ExpectedAssembly));
    }

    [Fact]
    public void CreateAsmHook_CreatesFreshReloadedOptionsAndDefensivelyCopiesBuilderCode()
    {
        var controller = new RecordingAsmHookController();
        var factory = new ReloadedNativeHookFactory(controller);
        MsgWindowChoiceConfirmProbeDelegate callback = _ => { };
        var mutableCode = ExpectedAssembly.ToList();

        var first = factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite,
            "first",
            callback,
            VerifiedAddress,
            _ => mutableCode,
            ValidOptions);
        var firstOptions = controller.OptionsHistory[0];
        mutableCode[0] = "use64";
        var rootedCode = Assert.Single(first.LifetimeRoots.OfType<IReadOnlyList<string>>());

        _ = factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite,
            "second",
            callback,
            VerifiedAddress,
            _ => ExpectedAssembly,
            ValidOptions);

        Assert.Equal("use32", rootedCode[0]);
        Assert.Equal(2, controller.OptionsHistory.Count);
        Assert.NotSame(firstOptions, controller.OptionsHistory[1]);
    }

    [Fact]
    public void CreateAsmHook_RejectsInvalidRequestsBeforeNativeAsmCreation()
    {
        var controller = new RecordingAsmHookController();
        var factory = new ReloadedNativeHookFactory(controller);
        MsgWindowChoiceConfirmProbeDelegate callback = _ => { };
        Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> builder =
            context => ChoiceConfirmProbeAssembly.Build(context);

        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, " ", callback, VerifiedAddress, builder, ValidOptions));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook<MsgWindowChoiceConfirmProbeDelegate>(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", null!, VerifiedAddress, builder, ValidOptions));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress, null!, ValidOptions));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, 0, builder, ValidOptions));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, unchecked((nuint)0x1_0000_0000UL), builder, ValidOptions));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            (HookId)999, "probe", callback, VerifiedAddress, builder, ValidOptions));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowClose, "probe", callback, VerifiedAddress, builder, ValidOptions));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress, builder,
            ValidOptions with { Behaviour = (AsmHookBehaviour)999 }));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress, builder,
            ValidOptions with { Behaviour = AsmHookBehaviour.ExecuteAfter }));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress, builder,
            ValidOptions with { HookLength = 0 }));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress, builder,
            ValidOptions with { MaxOpcodeSize = 0 }));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress, builder,
            ValidOptions with { PreferRelativeJump = false }));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress, builder,
            ValidOptions with { HookLength = 6 }));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress, builder,
            ValidOptions with { MaxOpcodeSize = 6 }));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress,
            _ => Array.Empty<string>(), ValidOptions));
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite, "probe", callback, VerifiedAddress,
            _ => [" "], ValidOptions));

        Assert.Equal(0, controller.AsmCreateCount);
    }

    [Fact]
    public void CreateAsmHook_PropagatesBuilderFailureWithoutNativeAsmCreation()
    {
        var controller = new RecordingAsmHookController();
        var factory = new ReloadedNativeHookFactory(controller);
        MsgWindowChoiceConfirmProbeDelegate callback = _ => { };

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateAsmHook(
            HookId.MsgWindowChoiceConfirmCallSite,
            "probe",
            callback,
            VerifiedAddress,
            _ => throw new InvalidOperationException("builder failure"),
            ValidOptions));

        Assert.Contains("builder failure", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, controller.ManagedCallCount);
        Assert.Equal(0, controller.AsmCreateCount);
    }

    [Fact]
    public void CreateAsmHook_AcceptsAnyAuditedFiveByteDirectCallSite()
    {
        var controller = new RecordingAsmHookController();
        var factory = new ReloadedNativeHookFactory(controller);
        NativeCallSiteProbeDelegate callback = () => { };

        var prepared = factory.CreateAsmHook(
            HookId.ClassicTopMenuTimeLabelCallSite,
            "Classic top-menu time-label probe",
            callback,
            0x005D0AA5,
            NativeCallSiteProbeAssembly.Build,
            ValidOptions);

        Assert.Equal(1, controller.AsmCreateCount);
        Assert.Equal(ExpectedNoArgumentAssembly, controller.Code);
        Assert.Contains(callback, prepared.LifetimeRoots);
    }

    [Fact]
    public void AuditedKeyboardSnapshotFiltersBeforeTheFiveOriginalBytes()
    {
        var controller = new RecordingAsmHookController();
        var factory = new ReloadedNativeHookFactory(controller);
        GameKeyboardStateProbeDelegate callback = _ => { };
        var prepared = factory.CreateAsmHook(HookId.GameKeyboardStateFilter, "Keyboard snapshot", callback,
            0x58F58B, BattleKeyboardHookSet.BuildAssembly, ValidOptions);
        Assert.Equal(1, controller.AsmCreateCount);
        Assert.Equal(5, controller.Options!.hookLength);
        Assert.Equal(AsmHookBehaviour.ExecuteFirst, controller.Options.Behaviour);
        Assert.Equal(new[] { "use32", "pushfd", "pushad", "lea eax, [ebp-0x110]", "push eax",
            controller.AbsoluteCallMnemonic, "add esp, 4", "popad", "popfd" }, controller.Code);
        Assert.Contains(callback, prepared.LifetimeRoots);
        Assert.Throws<ArgumentException>(() => factory.CreateAsmHook(HookId.GameKeyboardStateFilter,
            "Keyboard snapshot", callback, 0x58F58B, BattleKeyboardHookSet.BuildAssembly,
            ValidOptions with { Behaviour = AsmHookBehaviour.ExecuteAfter }));
        Assert.Equal(1, controller.AsmCreateCount);
    }

    [Fact]
    public void AuditedWorldInstructionExecutesItsSixBytesBeforeTheCallback()
    {
        var controller = new RecordingAsmHookController();
        var factory = new ReloadedNativeHookFactory(controller);
        NativeCallSiteProbeDelegate callback = () => { };
        factory.CreateAsmHook(HookId.WorldNavigationPadInstruction, "World pad", callback, 0x66536F,
            NativeCallSiteProbeAssembly.Build, new(AsmHookBehaviour.ExecuteAfter, 6, true, 6));
        Assert.Equal(6, controller.Options!.hookLength);
        Assert.Equal(AsmHookBehaviour.ExecuteAfter, controller.Options.Behaviour);
        Assert.Throws<ArgumentException>(() => factory.CreateAsmHook(HookId.WorldNavigationPadInstruction,
            "World pad", callback, 0x66536F, NativeCallSiteProbeAssembly.Build,
            new(AsmHookBehaviour.ExecuteFirst, 6, true, 6)));
    }

    private static RuntimeAsmHookOptions ValidOptions => new(
        AsmHookBehaviour.ExecuteFirst,
        HookLength: 5,
        PreferRelativeJump: true,
        MaxOpcodeSize: 5);

    private static IReadOnlyList<string> ExpectedAssembly =>
    [
        "use32",
        "pushfd",
        "push eax\npush ecx\npush edx",
        "push ecx",
        "call dword [0x12345678]",
        "add esp, 4",
        "pop edx\npop ecx\npop eax",
        "popfd",
    ];

    private static IReadOnlyList<string> ExpectedNoArgumentAssembly =>
    [
        "use32",
        "pushfd",
        "push eax\npush ecx\npush edx",
        "call dword [0x12345678]",
        "pop edx\npop ecx\npop eax",
        "popfd",
    ];

    private sealed class RecordingAsmHookController : IReloadedAsmHookController
    {
        public string AbsoluteCallMnemonic { get; } = "call dword [0x12345678]";
        public string PushCallerSaved { get; } = "push eax\npush ecx\npush edx";
        public string PopCallerSaved { get; } = "pop edx\npop ecx\npop eax";
        public object ReverseWrapper { get; } = new();
        public RecordingAsmHook Hook { get; } = new();
        public int ManagedCallCount { get; private set; }
        public int PushCount { get; private set; }
        public int PopCount { get; private set; }
        public int AsmCreateCount { get; private set; }
        public nuint Address { get; private set; }
        public IReadOnlyList<string> Code { get; private set; } = Array.Empty<string>();
        public AsmHookOptions? Options { get; private set; }
        public List<AsmHookOptions> OptionsHistory { get; } = [];

        public RuntimeManagedCall CreateManagedCall<TDelegate>(TDelegate callback)
            where TDelegate : Delegate
        {
            Assert.NotNull(callback);
            ManagedCallCount++;
            return new RuntimeManagedCall(AbsoluteCallMnemonic, ReverseWrapper);
        }

        public string GetPushCdeclCallerSavedRegisters()
        {
            PushCount++;
            return PushCallerSaved;
        }

        public string GetPopCdeclCallerSavedRegisters()
        {
            PopCount++;
            return PopCallerSaved;
        }

        public IAsmHook CreateAsmHook(IReadOnlyList<string> code, nuint address, AsmHookOptions options)
        {
            AsmCreateCount++;
            Code = code.ToArray();
            Address = address;
            Options = options;
            OptionsHistory.Add(options);
            return Hook;
        }
    }

    private sealed class RecordingAsmHook : IAsmHook
    {
        public bool IsEnabled { get; private set; }
        public int ActivateCount { get; private set; }

        public IAsmHook Activate()
        {
            ActivateCount++;
            IsEnabled = true;
            return this;
        }

        public void Enable() => IsEnabled = true;
        public void Disable() => IsEnabled = false;
    }
}

public sealed class ChoiceConfirmProbeAssemblyTests
{
    [Fact]
    public void Build_PreservesFlagsAndCallerSavedRegistersAroundOriginalEcxCdeclArgument()
    {
        var context = new RuntimeAsmHookAssemblyContext(
            "call dword [managed_callback]",
            "push eax\npush ecx\npush edx",
            "pop edx\npop ecx\npop eax");

        var code = ChoiceConfirmProbeAssembly.Build(context);
        var ordered = code.ToList();

        Assert.Equal(
        [
            "use32",
            "pushfd",
            "push eax\npush ecx\npush edx",
            "push ecx",
            "call dword [managed_callback]",
            "add esp, 4",
            "pop edx\npop ecx\npop eax",
            "popfd",
        ], code);
        Assert.Equal(ordered.IndexOf("pushfd") + 1, ordered.IndexOf(context.PushCdeclCallerSavedRegisters));
        Assert.True(ordered.IndexOf("push ecx") < ordered.IndexOf(context.AbsoluteCallMnemonic));
        Assert.Equal(ordered.IndexOf(context.AbsoluteCallMnemonic) + 1, ordered.IndexOf("add esp, 4"));
        Assert.Equal(ordered.IndexOf(context.PopCdeclCallerSavedRegisters) + 1, ordered.IndexOf("popfd"));
        Assert.DoesNotContain("ret", code);
    }
}

public sealed class NativeCallSiteProbeAssemblyTests
{
    [Fact]
    public void Build_PreservesFlagsAndCallerSavedRegistersAroundNoArgumentCdeclCall()
    {
        var context = new RuntimeAsmHookAssemblyContext(
            "call dword [managed_callback]",
            "push eax\npush ecx\npush edx",
            "pop edx\npop ecx\npop eax");

        var code = NativeCallSiteProbeAssembly.Build(context);

        Assert.Equal(
        [
            "use32",
            "pushfd",
            "push eax\npush ecx\npush edx",
            "call dword [managed_callback]",
            "pop edx\npop ecx\npop eax",
            "popfd",
        ], code);
        Assert.DoesNotContain("push ecx", code);
        Assert.DoesNotContain("add esp, 4", code);
        Assert.DoesNotContain("ret", code);
    }
}
