using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.Enums;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldNavigationHookSet(IRuntimeNativeAsmHookFactory factory,
    Func<nint, uint, uint> onInput, Action onEnable, Action onDisable) : IHookRegistration, IHookActivationObserver
{
    private bool prepared;
    public string Name => GameVersionCatalog.Get(HookId.FieldNavigationPadCallSite).Symbol;
    public IReadOnlyList<IHookRegistration> Registrations => [this];

    public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        var contract = GameVersionCatalog.Get(HookId.FieldNavigationPadCallSite);
        if (prepared || build.ImageBaseAddress == 0 ||
            !build.HookAddresses.TryGetValue(contract.Id, out var address) ||
            address != checked(build.ImageBaseAddress + contract.Rva))
            throw new InvalidOperationException("Navigation requires the verified field input call site.");
        FieldNavigationPadProbeDelegate callback = (engine, pad) => boundary.Run(Name, () =>
        {
            if (boundary.IsFaulted) { onDisable(); return pad; }
            return onInput(engine, pad);
        }, pad);
        var hook = factory.CreateAsmHook(contract.Id, Name, callback, address, BuildAssembly,
            new(AsmHookBehaviour.ExecuteFirst, 5, true, 5));
        prepared = true;
        return hook;
    }

    public void AfterHooksActivated()
    {
        if (!prepared) throw new InvalidOperationException("Navigation hook is not prepared.");
        onEnable();
    }
    public void AfterHooksDisabled() => onDisable();

    public static IReadOnlyList<string> BuildAssembly(RuntimeAsmHookAssemblyContext context) =>
    [
        "use32", "pushfd", "pushad", "push esi", "push edi",
        context.AbsoluteCallMnemonic, "add esp, 8",
        // ESI contains the pad for the directional consumer after the native dash call.
        // The same pad has already been pushed as that call's argument. PUSHAD saves
        // ESI at +4; PUSHFD+PUSHAD leave the existing argument at +36.
        "mov [esp+4], eax", "mov [esp+36], eax", "popad", "popfd",
    ];
}
