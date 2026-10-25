using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Mod.Minigames;
using ChronoTriggerAccessibility.Mod.Navigation;
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
    private readonly BlackbirdGrateViewRuntime? grateViews;
    private readonly BekklerMimicRuntime? mimic;
    private bool prepared;

    public IntroTraceHookSet(IRuntimeNativeHookFactory factory, IntroTraceRecorder recorder,
        IokaContestRuntime? contest = null, StoryActionRuntime? storyActions = null,
        BlackbirdGrateViewRuntime? grateViews = null, BekklerMimicRuntime? mimic = null)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        this.contest = contest;
        this.storyActions = storyActions;
        this.grateViews = grateViews;
        this.mimic = mimic;
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
            void MimicAndCall()
            {
                if (mimic is null) ContestAndCall();
                else mimic.Dispatch(context, opcode, ContestAndCall);
            }
            // Before the original: an E0 return must be read before the scene changes.
            grateViews?.Dispatch(context, opcode);
            if (storyActions is null) MimicAndCall();
            else storyActions.Dispatch(context, opcode, MimicAndCall);
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
        grateViews?.Enable();
        mimic?.Enable();
    }
    public void AfterHooksDisabled() { mimic?.Disable(); grateViews?.Disable(); storyActions?.Disable(); contest?.Disable(); recorder.Disable(); }
}
