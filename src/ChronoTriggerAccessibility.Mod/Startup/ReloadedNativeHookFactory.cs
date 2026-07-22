using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
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

public readonly record struct RuntimeAsmHookOptions(
    AsmHookBehaviour Behaviour,
    int HookLength,
    bool PreferRelativeJump,
    int MaxOpcodeSize);

public sealed class RuntimeAsmHookAssemblyContext
{
    public RuntimeAsmHookAssemblyContext(
        string absoluteCallMnemonic,
        string pushCdeclCallerSavedRegisters,
        string popCdeclCallerSavedRegisters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteCallMnemonic);
        ArgumentException.ThrowIfNullOrWhiteSpace(pushCdeclCallerSavedRegisters);
        ArgumentException.ThrowIfNullOrWhiteSpace(popCdeclCallerSavedRegisters);
        AbsoluteCallMnemonic = new string(absoluteCallMnemonic.AsSpan());
        PushCdeclCallerSavedRegisters = new string(pushCdeclCallerSavedRegisters.AsSpan());
        PopCdeclCallerSavedRegisters = new string(popCdeclCallerSavedRegisters.AsSpan());
    }

    public string AbsoluteCallMnemonic { get; }
    public string PushCdeclCallerSavedRegisters { get; }
    public string PopCdeclCallerSavedRegisters { get; }
}

public sealed record RuntimeManagedCall(string AbsoluteCallMnemonic, object ReverseWrapper);

public interface IReloadedAsmHookController
{
    RuntimeManagedCall CreateManagedCall<TDelegate>(TDelegate callback)
        where TDelegate : Delegate;

    string GetPushCdeclCallerSavedRegisters();
    string GetPopCdeclCallerSavedRegisters();
    IAsmHook CreateAsmHook(IReadOnlyList<string> code, nuint address, AsmHookOptions options);
}

public interface IRuntimeNativeAsmHookFactory
{
    IPreparedHook CreateAsmHook<TDelegate>(
        HookId id,
        string name,
        TDelegate callback,
        nuint address,
        Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly,
        RuntimeAsmHookOptions options)
        where TDelegate : Delegate;
}

public sealed class ReloadedAsmHookController : IReloadedAsmHookController
{
    private readonly SharedReloadedHooks hooks;

    public ReloadedAsmHookController(SharedReloadedHooks hooks)
    {
        this.hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
    }

    public RuntimeManagedCall CreateManagedCall<TDelegate>(TDelegate callback)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(callback);
        var mnemonic = hooks.Utilities.GetAbsoluteCallMnemonics(callback, out var reverseWrapper);
        return new RuntimeManagedCall(mnemonic, reverseWrapper);
    }

    public string GetPushCdeclCallerSavedRegisters() =>
        hooks.Utilities.PushCdeclCallerSavedRegisters();

    public string GetPopCdeclCallerSavedRegisters() =>
        hooks.Utilities.PopCdeclCallerSavedRegisters();

    public IAsmHook CreateAsmHook(IReadOnlyList<string> code, nuint address, AsmHookOptions options)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(options);
        return hooks.CreateAsmHook(code.ToArray(), checked((long)address), options);
    }
}

public static class ChoiceConfirmProbeAssembly
{
    public static IReadOnlyList<string> Build(RuntimeAsmHookAssemblyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ReadOnlyCollection<string>(
        [
            "use32",
            "pushfd",
            new string(context.PushCdeclCallerSavedRegisters.AsSpan()),
            "push ecx",
            new string(context.AbsoluteCallMnemonic.AsSpan()),
            "add esp, 4",
            new string(context.PopCdeclCallerSavedRegisters.AsSpan()),
            "popfd",
        ]);
    }
}

/// <summary>
/// Builds a no-argument cdecl marker call that leaves the intercepted x86 call
/// site's flags and caller-saved registers unchanged for relocated execution.
/// </summary>
public static class NativeCallSiteProbeAssembly
{
    public static IReadOnlyList<string> Build(RuntimeAsmHookAssemblyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ReadOnlyCollection<string>(
        [
            "use32",
            "pushfd",
            new string(context.PushCdeclCallerSavedRegisters.AsSpan()),
            new string(context.AbsoluteCallMnemonic.AsSpan()),
            new string(context.PopCdeclCallerSavedRegisters.AsSpan()),
            "popfd",
        ]);
    }
}

public sealed class ReloadedNativeHookFactory :
    IRuntimeNativeHookFactory,
    IRuntimeNativeFunctionWrapperFactory,
    IRuntimeNativeAsmHookFactory
{
    private readonly SharedReloadedHooks? hooks;
    private readonly IReloadedAsmHookController asmHooks;

    public ReloadedNativeHookFactory(SharedReloadedHooks hooks)
    {
        this.hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        asmHooks = new ReloadedAsmHookController(hooks);
    }

    public ReloadedNativeHookFactory(IReloadedAsmHookController asmHooks)
    {
        this.asmHooks = asmHooks ?? throw new ArgumentNullException(nameof(asmHooks));
    }

    public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
        where TDelegate : Delegate
    {
        _ = id;
        ArgumentNullException.ThrowIfNull(detour);
        return (hooks ?? throw new InvalidOperationException("Function hooks require the Reloaded controller constructor."))
            .CreateHook(detour, checked((long)address));
    }

    public TDelegate CreateWrapper<TDelegate>(nuint address)
        where TDelegate : Delegate =>
        (hooks ?? throw new InvalidOperationException("Function wrappers require the Reloaded controller constructor."))
            .CreateWrapper<TDelegate>(checked((long)address), out _);

    public IPreparedHook CreateAsmHook<TDelegate>(
        HookId id,
        string name,
        TDelegate callback,
        nuint address,
        Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly,
        RuntimeAsmHookOptions options)
        where TDelegate : Delegate
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(buildAssembly);
        if (address == 0 || (ulong)address > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "Assembly-hook address must be a nonzero x86 address.");
        }

        var contract = GameVersionCatalog.Hooks.SingleOrDefault(candidate => candidate.Id == id)
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "The hook ID is not present in the verified catalog.");
        if (contract.Kind != NativeHookKind.AssemblyCallSite)
        {
            throw new ArgumentException("A function-entry contract cannot be installed as an assembly call-site hook.", nameof(id));
        }
        if (contract.ExpectedBytes.Length != 5 || contract.ExpectedBytes[0] != 0xE8)
        {
            throw new NotSupportedException(
                $"Assembly call-site '{contract.Symbol}' is not an audited exact five-byte direct CALL.");
        }
        if (!Enum.IsDefined(options.Behaviour))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options, "Unsupported assembly-hook behavior.");
        }
        if (options.HookLength <= 0 || options.MaxOpcodeSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options, "Assembly hook length and maximum opcode size must be positive.");
        }

        var minimumJumpLength = options.PreferRelativeJump ? 5 : 6;
        if (options.HookLength < minimumJumpLength)
        {
            throw new ArgumentException(
                $"The requested {(options.PreferRelativeJump ? "relative" : "absolute")} jump needs at least {minimumJumpLength} bytes.",
                nameof(options));
        }
        if (options.Behaviour != AsmHookBehaviour.ExecuteFirst ||
            options.HookLength != contract.ExpectedBytes.Length ||
            !options.PreferRelativeJump ||
            options.MaxOpcodeSize != contract.ExpectedBytes.Length)
        {
            throw new ArgumentException(
                "An exact five-byte direct-call probe requires ExecuteFirst, HookLength=5, PreferRelativeJump=true, and MaxOpcodeSize=5.",
                nameof(options));
        }

        var managedCall = asmHooks.CreateManagedCall(callback);
        if (managedCall.ReverseWrapper is null)
        {
            throw new InvalidOperationException("Reloaded returned no reverse wrapper for the managed assembly callback.");
        }
        var context = new RuntimeAsmHookAssemblyContext(
            managedCall.AbsoluteCallMnemonic,
            asmHooks.GetPushCdeclCallerSavedRegisters(),
            asmHooks.GetPopCdeclCallerSavedRegisters());
        var generated = buildAssembly(context)
            ?? throw new ArgumentException("The assembly builder returned null.", nameof(buildAssembly));
        var copiedCode = generated.Select(line =>
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new ArgumentException("The assembly builder returned blank FASM.", nameof(buildAssembly));
            }
            return new string(line.AsSpan());
        }).ToArray();
        if (copiedCode.Length == 0)
        {
            throw new ArgumentException("The assembly builder returned no FASM.", nameof(buildAssembly));
        }

        var rootedCode = new ReadOnlyCollection<string>(copiedCode);
        var reloadedOptions = new AsmHookOptions
        {
            Behaviour = options.Behaviour,
            hookLength = options.HookLength,
            PreferRelativeJump = options.PreferRelativeJump,
            MaxOpcodeSize = options.MaxOpcodeSize,
        };
        var hook = asmHooks.CreateAsmHook(rootedCode, address, reloadedOptions)
            ?? throw new InvalidOperationException("Reloaded returned no assembly hook.");
        return new ReloadedPreparedAsmHook(
            name,
            hook,
            [callback, managedCall.ReverseWrapper, rootedCode]);
    }
}
