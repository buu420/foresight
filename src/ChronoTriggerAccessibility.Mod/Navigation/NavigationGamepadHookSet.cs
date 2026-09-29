using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.Enums;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Filters only physical joystick input, before the game's event table,
/// face-button remapping and keyboard merge. No extra poll or OS input injection.</summary>
public sealed class NavigationGamepadHookSet(IRuntimeNativeAsmHookFactory factory,
    NavigationJoystick joystick, Action suspend) : IHookRegistration, IHookActivationObserver
{
    private bool prepared;
    private volatile bool active;
    public string Name => GameVersionCatalog.Get(HookId.GameJoystickStateFilter).Symbol;
    public IReadOnlyList<IHookRegistration> Registrations => [this];

    public unsafe IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        var contract = GameVersionCatalog.Get(HookId.GameJoystickStateFilter);
        if (prepared || build.ImageBaseAddress == 0 ||
            !build.HookAddresses.TryGetValue(contract.Id, out var address) ||
            address != checked(build.ImageBaseAddress + contract.Rva))
            throw new InvalidOperationException("Controller filtering requires the verified native snapshot boundary.");
        GameJoystickStateProbeDelegate callback = (device, state, result) => boundary.Run(Name, () =>
        {
            if (!active || boundary.IsFaulted) return;
            // WINMM does not initialize the buffer on failure. Do not dereference it.
            if (result != 0) joystick.Filter(device, [], result);
            else joystick.Filter(device, new Span<uint>((void*)state, 13), result);
        });
        var hook = factory.CreateAsmHook(contract.Id, Name, callback, address, BuildAssembly,
            new(AsmHookBehaviour.ExecuteFirst, 7, true, 7));
        prepared = true;
        return hook;
    }

    public void AfterHooksActivated()
    {
        if (!prepared) throw new InvalidOperationException("Controller filter is not prepared.");
        active = true;
    }

    public void AfterHooksDisabled()
    {
        active = false;
        suspend();
    }

    public static IReadOnlyList<string> BuildAssembly(RuntimeAsmHookAssemblyContext context) =>
    [
        "use32", "pushfd", "pushad", "push eax", "lea eax, [ebp-0x138]", "push eax", "push ebx",
        context.AbsoluteCallMnemonic, "add esp, 12", "popad", "popfd",
    ];
}
