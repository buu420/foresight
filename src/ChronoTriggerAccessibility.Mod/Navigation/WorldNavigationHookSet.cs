using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions.Enums;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class WorldNavigationHookSet(IRuntimeNativeAsmHookFactory factory,
    Action<nint, uint> onTick, Func<nint, uint, uint> onPad, Action<nuint> bindImageBase,
    Action<nint, uint, VehicleKind>? onVehicleTick = null, Func<nint, uint, VehicleKind, uint>? onVehiclePad = null)
    : IHookActivationObserver
{
    private bool active;
    private int prepared;
    private readonly IRuntimeNativeAsmHookFactory nativeFactory = factory;
    private readonly Action<nint, uint> tickCallback = onTick;
    private readonly Func<nint, uint, uint> padCallback = onPad;
    private readonly Action<nuint> bindBuild = bindImageBase;
    private readonly Action<nint, uint, VehicleKind>? vehicleTickCallback = onVehicleTick;
    private readonly Func<nint, uint, VehicleKind, uint>? vehiclePadCallback = onVehiclePad;

    /// <summary>
    /// 264C40 rebuilds the combined pad three times. State 0F (265147) gates the whole
    /// remaining chain on the physical pad being non-zero, and states 10 (265247) and 11
    /// (26536F) each re-read the native getters and overwrite the emulated accumulator
    /// that the direction dispatch in states 12-15 reads. A synthetic direction only
    /// reaches movement if it is injected at every one of them.
    /// </summary>
    private static readonly HookId[] PadInstructions =
    [
        HookId.WorldNavigationPadGateInstruction,
        HookId.WorldNavigationPadSecondInstruction,
        HookId.WorldNavigationPadInstruction,
    ];

    /// <summary>
    /// The Epoch task 28E1D0 reads the pad twice while hovering (state 0F gate, state 10
    /// dispatch) and the Dactyl task 28A1E0 in states 4 and 5; the same rule applies. The
    /// vehicle tick call sites 2766EA and 276718 run once per emulated frame for every
    /// frame the vehicle actor exists, parked or flying, so their callback decides.
    /// </summary>
    private static readonly (HookId Id, bool Tick, VehicleKind Kind)[] VehicleBoundaries =
    [
        (HookId.WorldNavigationEpochTickCallSite, true, VehicleKind.Epoch),
        (HookId.WorldNavigationEpochPadGateInstruction, false, VehicleKind.Epoch),
        (HookId.WorldNavigationEpochPadInstruction, false, VehicleKind.Epoch),
        (HookId.WorldNavigationDactylTickCallSite, true, VehicleKind.Dactyl),
        (HookId.WorldNavigationDactylPadGateInstruction, false, VehicleKind.Dactyl),
        (HookId.WorldNavigationDactylPadInstruction, false, VehicleKind.Dactyl),
    ];

    private static readonly (HookId Id, bool Tick, VehicleKind? Kind)[] Boundaries =
    [
        (HookId.WorldNavigationTickCallSite, true, null),
        .. PadInstructions.Select(id => (id, false, (VehicleKind?)null)),
        .. VehicleBoundaries.Select(b => (b.Id, b.Tick, (VehicleKind?)b.Kind)),
    ];

    public static IReadOnlyList<HookId> BoundaryIds => Boundaries.Select(b => b.Id).ToArray();

    public IReadOnlyList<IHookRegistration> Registrations =>
        [.. Boundaries.Select(b => new Registration(this, b.Id, b.Tick, b.Kind))];

    public void AfterHooksActivated()
    {
        if (prepared != Boundaries.Length)
            throw new InvalidOperationException("Every world input boundary must be prepared.");
        active = true;
    }
    public void AfterHooksDisabled() => active = false;

    private sealed class Registration(WorldNavigationHookSet owner, HookId id, bool tick, VehicleKind? kind) : IHookRegistration
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
            FieldNavigationPadProbeDelegate callback = (context, pad) => boundary.Run(Name, () =>
            {
                if (!owner.active || boundary.IsFaulted) return pad;
                if (kind is { } vehicle)
                {
                    if (tick) { owner.vehicleTickCallback?.Invoke(context, pad, vehicle); return pad; }
                    return owner.vehiclePadCallback?.Invoke(context, pad, vehicle) ?? pad;
                }
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
