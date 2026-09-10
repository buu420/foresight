using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Mod.Intro;

public sealed class IntroTraceHookSet : IHookActivationObserver, IHookRegistration
{
    private readonly IRuntimeNativeHookFactory factory;
    private readonly IntroTraceRecorder recorder;
    private bool prepared;

    public IntroTraceHookSet(IRuntimeNativeHookFactory factory, IntroTraceRecorder recorder)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        Registrations = Array.AsReadOnly<IHookRegistration>([this]);
    }

    public string Name => GameVersionCatalog.Get(HookId.FieldOpcodeDispatcher).Symbol;
    public IReadOnlyList<IHookRegistration> Registrations { get; }

    public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
    {
        var contract = GameVersionCatalog.Get(HookId.FieldOpcodeDispatcher);
        if (prepared || build.ImageBaseAddress == 0 ||
            !build.HookAddresses.TryGetValue(contract.Id, out var address) ||
            address != checked(build.ImageBaseAddress + contract.Rva))
            throw new InvalidOperationException("Intro trace requires one preparation against the verified opcode dispatcher.");

        FieldOpcodeDispatcherDelegate? original = null;
        FieldOpcodeDispatcherDelegate detour = (context, opcode) => boundary.Run(Name, () =>
            recorder.Dispatch(context, opcode, () =>
                (original ?? throw new InvalidOperationException("Intro trace original is not bound."))(context, opcode)));
        // The factory only prepares an inactive hook. Bind the trampoline before
        // returning it to ReloadedHookInstaller, which owns the later activation.
        var hook = factory.CreateHook(contract.Id, detour, address);
        original = hook.OriginalFunction ?? throw new InvalidOperationException("Intro trace original is unavailable.");
        prepared = true;
        return new ReloadedPreparedHook<FieldOpcodeDispatcherDelegate>(Name, hook, detour);
    }

    public void AfterHooksActivated()
    {
        if (!prepared) throw new InvalidOperationException("Intro trace has not been prepared.");
        recorder.Enable();
    }
    public void AfterHooksDisabled() => recorder.Disable();
}
