using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.Enums;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class WorldNavigationHookSet(IRuntimeNativeAsmHookFactory factory,
    Action<nint, uint> onTick, Func<nint, uint, uint> onPad, Action<nuint> bindImageBase)
    : IHookActivationObserver
{
    private bool active;
    private int prepared;
    private readonly IRuntimeNativeAsmHookFactory nativeFactory = factory;
    private readonly Action<nint, uint> tickCallback = onTick;
    private readonly Func<nint, uint, uint> padCallback = onPad;
    private readonly Action<nuint> bindBuild = bindImageBase;
    public IReadOnlyList<IHookRegistration> Registrations =>
    [
        new Registration(this, HookId.WorldNavigationTickCallSite),
        new Registration(this, HookId.WorldNavigationPadInstruction),
    ];

    public void AfterHooksActivated()
    {
        if (prepared != 2) throw new InvalidOperationException("Both world input boundaries must be prepared.");
        active = true;
    }
    public void AfterHooksDisabled() => active = false;

    private sealed class Registration(WorldNavigationHookSet owner, HookId id) : IHookRegistration
    {
        private bool prepared;
        public string Name => GameVersionCatalog.Get(id).Symbol;
        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
        {
            var contract = GameVersionCatalog.Get(id);
            if (prepared || build.ImageBaseAddress == 0 || !build.HookAddresses.TryGetValue(id, out var address) ||
                address != checked(build.ImageBaseAddress + contract.Rva))
                throw new InvalidOperationException("World navigation requires the verified native input boundaries.");
            owner.bindBuild(build.ImageBaseAddress);
            var tick = id == HookId.WorldNavigationTickCallSite;
            FieldNavigationPadProbeDelegate callback = (context, pad) => boundary.Run(Name, () =>
            {
                if (!owner.active || boundary.IsFaulted) return pad;
                if (tick) { owner.tickCallback(context, pad); return pad; }
                return owner.padCallback(context, pad);
            }, pad);
            var result = owner.nativeFactory.CreateAsmHook(id, Name, callback, address,
                code => tick ? BuildTickAssembly(code, build.ImageBaseAddress) : BuildPadAssembly(code),
                tick ? new(AsmHookBehaviour.ExecuteFirst, 5, true, 5) : new(AsmHookBehaviour.ExecuteAfter, 6, true, 6));
            prepared = true; owner.prepared++;
            return result;
        }
    }

    public static IReadOnlyList<string> BuildTickAssembly(RuntimeAsmHookAssemblyContext context, nuint imageBase) =>
    [
        "use32", "pushfd", "pushad",
        // The original world handler reads this held-pad getter three times. Read
        // it at the walking-task boundary as well, so keys and motion are sampled
        // every tick, including the frames between eight-pixel movement decisions.
        $"mov ecx, [0x{checked(imageBase + 0x41C3DCu):X}]", "test ecx, ecx", "jnz world_pad_ready",
        $"mov ecx, 0x{checked(imageBase + 0x3FB30Cu):X}", "world_pad_ready:",
        "mov eax, [ecx]", "call dword [eax+4]", "push eax",
        // Saved ECX (the world context) is +24 before the pad argument is pushed.
        "push dword [esp+28]", context.AbsoluteCallMnemonic, "add esp, 8", "popad", "popfd",
    ];

    public static IReadOnlyList<string> BuildPadAssembly(RuntimeAsmHookAssemblyContext context) =>
    [
        "use32", "pushfd", "pushad", "push edi", "push esi", context.AbsoluteCallMnemonic,
        "add esp, 8", "mov [esp], eax", "popad", "popfd",
    ];
}
