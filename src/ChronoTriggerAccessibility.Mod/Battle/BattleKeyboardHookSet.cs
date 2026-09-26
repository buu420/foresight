using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.Enums;

namespace ChronoTriggerAccessibility.Mod.Battle;

public sealed class BattleKeyboardHookSet(IRuntimeNativeAsmHookFactory factory,
    BattleKeyboard keyboard, Func<bool> battleActive) : IHookRegistration, IHookActivationObserver
{
    private bool prepared;
    private volatile bool active;
    public string Name => GameVersionCatalog.Get(HookId.GameKeyboardStateFilter).Symbol;
    public IReadOnlyList<IHookRegistration> Registrations => [this];

    public unsafe IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        var contract = GameVersionCatalog.Get(HookId.GameKeyboardStateFilter);
        if (prepared || build.ImageBaseAddress == 0 ||
            !build.HookAddresses.TryGetValue(contract.Id, out var address) ||
            address != checked(build.ImageBaseAddress + contract.Rva))
            throw new InvalidOperationException("Keyboard filtering requires the verified native snapshot boundary.");
        GameKeyboardStateProbeDelegate callback = state => boundary.Run(Name, () =>
        {
            if (active && !boundary.IsFaulted)
                keyboard.FilterGameKeyboardState(new Span<byte>((void*)state, 256), battleActive());
        });
        var hook = factory.CreateAsmHook(contract.Id, Name, callback, address, BuildAssembly,
            new(AsmHookBehaviour.ExecuteFirst, 5, true, 5));
        prepared = true;
        return hook;
    }

    public void AfterHooksActivated()
    {
        if (!prepared) throw new InvalidOperationException("Keyboard filter is not prepared.");
        keyboard.ResetGameInputSuppression();
        active = true;
    }

    public void AfterHooksDisabled()
    {
        active = false;
        keyboard.ResetGameInputSuppression();
    }

    public static IReadOnlyList<string> BuildAssembly(RuntimeAsmHookAssemblyContext context) =>
    [
        // 18F585 has just filled the 256-byte native stack array at EBP-110.
        // Preserve all registers/flags; the original XOR ECX,ECX and NOP run afterward.
        "use32", "pushfd", "pushad", "lea eax, [ebp-0x110]", "push eax",
        context.AbsoluteCallMnemonic, "add esp, 4", "popad", "popfd",
    ];
}
