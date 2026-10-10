using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Mod.Minigames;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Mod.Intro;

public sealed class IntroTraceHookSet : IHookActivationObserver, IHookRegistration
{
    private readonly IRuntimeNativeHookFactory factory;
    private readonly IntroTraceRecorder recorder;
    private readonly IokaContestRuntime? contest;
    private readonly StoryActionRuntime? storyActions;
    private bool prepared;

    public IntroTraceHookSet(IRuntimeNativeHookFactory factory, IntroTraceRecorder recorder,
        IokaContestRuntime? contest = null, StoryActionRuntime? storyActions = null)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        this.contest = contest;
        this.storyActions = storyActions;
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
        {
            void TraceAndCall() => recorder.Dispatch(context, opcode, () =>
                (original ?? throw new InvalidOperationException("Intro trace original is not bound."))(context, opcode));
            void ContestAndCall()
            {
                if (contest is null) TraceAndCall();
                else contest.Dispatch(context, opcode, TraceAndCall);
            }
            if (storyActions is null) ContestAndCall();
            else storyActions.Dispatch(context, opcode, ContestAndCall);
        });
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
        contest?.Enable();
        storyActions?.Enable();
    }
    public void AfterHooksDisabled() { storyActions?.Disable(); contest?.Disable(); recorder.Disable(); }
}
