using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Intro;
using ChronoTriggerAccessibility.Mod.Minigames;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;
using ChronoTriggerAccessibility.Mod.Menus;

namespace ChronoTriggerAccessibility.Mod.Tests.Intro;

public sealed class IntroTraceHookSetTests
{
    [Fact]
    public void PasswordAndFlashbackReadersShareTheOpcodeHookWithTheNativeCallInBetween()
    {
        var factory=new Factory();var errors=new Errors();var speech=new List<string>();
        var recorder=new IntroTraceRecorder(new NoMemory(),_=>{});
        var order=new List<string>();
        var password=new PasswordPanelRuntime((_,_)=>
        {
            Assert.True(factory.Originals>0);order.Add("password-after");
            return new(PasswordPanelState.Shown,0x1000,0x5000,4,0x7000,0x8000,0,1,false);
        },()=>true,speech.Add,_=>{});
        var flashback=new LavosFlashbackRuntime((_,_)=>
        {
            order.Add("flashback-before");
            return new(LavosFlashbackKind.Flashback,0x1000,0x2000,0x3000,0x5000,347,311,LavosFlashbackSubject.DragonTank);
        },()=>true,_=>"Dragon Tank",_=>null,speech.Add,_=>{});
        var hooks=new IntroTraceHookSet(factory,recorder,passwordPanel:password,lavosFlashbacks:flashback);
        var build=new Build(0x400000,GameVersionCatalog.Hooks.ToDictionary(x=>x.Id,x=>(nuint)(0x400000+x.Rva)));
        hooks.Prepare(build,new(errors,errors));hooks.AfterHooksActivated();
        factory.Detour!(0x1000,0x2D); // Native pad handler opens the panel.
        Assert.Equal(["password-after"],order);Assert.Single(speech);order.Clear();
        factory.Detour(0x1000,0xAD);
        Assert.Equal(["flashback-before","password-after"],order);
        Assert.Equal(2,speech.Count);Assert.Equal(2,factory.Originals);Assert.Equal(1,factory.Created);
        hooks.AfterHooksDisabled();factory.Detour(0x1000,0xAD);
        Assert.Equal(3,factory.Originals);Assert.Equal(2,speech.Count);Assert.Equal(2,order.Count);
        hooks.AfterHooksActivated();factory.Detour(0x1000,0x2D);factory.Detour(0x1000,0xAD);
        Assert.Equal(5,factory.Originals);Assert.Equal(4,speech.Count);Assert.Empty(errors.Messages);
    }
    [Fact]
    public void CopyGameUsesTheExistingOpcodeHookAndResetsWhenHooksAreDisabled()
    {
        var factory = new Factory(); var errors = new Errors(); var speech = new List<string>();
        var recorder = new IntroTraceRecorder(new NoMemory(), _ => { });
        var action = BekklerMimicAction.Window;
        var mimic = new BekklerMimicRuntime((_, _) => new(0x1000, 0xA0000, 0xD0000, 0x5000,
            0x382, 1, 0, action), before => factory.Originals > 0 ? new(before, "Y") : null,
            () => true, speech.Add, _ => { });
        var hooks = new IntroTraceHookSet(factory, recorder, mimic: mimic);
        var build = new Build(0x400000, GameVersionCatalog.Hooks.ToDictionary(x => x.Id, x => (nuint)(0x400000 + x.Rva)));
        hooks.Prepare(build, new(errors, errors)); hooks.AfterHooksActivated();
        Assert.False(recorder.IsRecording);
        factory.Detour!(0x1000, 0x4F);
        action = BekklerMimicAction.Laugh; factory.Detour(0x1000, 0xAA);
        Assert.Equal(["Y. Laugh. Copy game started."], speech);
        hooks.AfterHooksDisabled(); factory.Detour(0x1000, 0xAA);
        hooks.AfterHooksActivated(); factory.Detour(0x1000, 0xAA);
        Assert.Single(speech); Assert.Equal(4, factory.Originals);
        Assert.Equal(1, factory.Created); Assert.Empty(errors.Messages);
    }

    [Fact]
    public void OneOpcodeHookRunsTheNativeGameOnceAndEnablesContestFeedbackIndependentlyOfIntroTracing()
    {
        var factory = new Factory(); var errors = new Errors(); var speech = new List<string>();
        var recorder = new IntroTraceRecorder(new NoMemory(), _ => { });
        var contest = new IokaContestRuntime((_, _) => new(0x1000, 0xA0000, 0xD0000, 0x5000,
            0x7BD, 6, 0, IokaContestAction.Started), _ => factory.Originals > 0,
            () => true, () => 0, () => "Ayla", speech.Add, _ => { });
        var hooks = new IntroTraceHookSet(factory, recorder, contest);
        var build = new Build(0x400000, GameVersionCatalog.Hooks.ToDictionary(x => x.Id, x => (nuint)(0x400000 + x.Rva)));
        var prepared = hooks.Prepare(build, new(errors, errors));
        Assert.Single(hooks.Registrations); Assert.Equal(1, factory.Created); Assert.False(prepared.IsActive);
        hooks.AfterHooksActivated();
        Assert.False(recorder.IsRecording);
        factory.Detour!(0x1000, 0x75);
        Assert.Equal(1, factory.Originals);
        Assert.StartsWith("Drinking contest started.", Assert.Single(speech));
        hooks.AfterHooksDisabled(); factory.Detour(0x1000, 0x75);
        Assert.Equal(2, factory.Originals); Assert.Single(speech); Assert.Empty(errors.Messages);
    }

    private sealed record Build(nuint ImageBaseAddress, IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    [Fact]
    public void StoryActionsShareTheOpcodeHookAndWorkWhenIntroTracingIsNotRecording()
    {
        var factory = new Factory(); var errors = new Errors(); var speech = new List<string>();
        var recorder = new IntroTraceRecorder(new NoMemory(), _ => { });
        var story = new StoryActionRuntime((_, _) => StoryActionRuntimeTests.Frame(),
            _ => factory.Originals > 0 ? new(StoryAnimationKind.Looping, 0x16) : null,
            () => true, _ => "Cinder", speech.Add, _ => { });
        var contest = new IokaContestRuntime((_, _) => null, _ => false,
            () => true, () => 0, () => "Ayla", speech.Add, _ => { });
        var hooks = new IntroTraceHookSet(factory, recorder, contest, story);
        var build = new Build(0x400000, GameVersionCatalog.Hooks.ToDictionary(x => x.Id, x => (nuint)(0x400000 + x.Rva)));
        hooks.Prepare(build, new(errors, errors)); hooks.AfterHooksActivated();
        Assert.False(recorder.IsRecording);
        factory.Detour!(0x1000, 0xAA);
        Assert.Equal(["Cinder nods."], speech); Assert.Equal(1, factory.Created); Assert.Equal(1, factory.Originals);
        hooks.AfterHooksDisabled(); factory.Detour(0x1000, 0xAA);
        Assert.Equal(2, factory.Originals); Assert.Single(speech); Assert.Empty(errors.Messages);
    }
    private sealed class NoMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
    private sealed class Errors : IModLog, IAccessibleFatalError
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) { }
        public void Error(string message) => Messages.Add(message);
        public void Show(string message) => Messages.Add(message);
    }
    private sealed class Factory : IRuntimeNativeHookFactory
    {
        public int Created, Originals;
        public FieldOpcodeDispatcherDelegate? Detour;
        public IHook<T> CreateHook<T>(HookId id, T detour, nuint address) where T : Delegate
        {
            Assert.Equal(HookId.FieldOpcodeDispatcher, id); Assert.Equal((nuint)0x5619E0, address);
            Created++; Detour = (FieldOpcodeDispatcherDelegate)(Delegate)detour;
            return new Hook<T>((T)(Delegate)new FieldOpcodeDispatcherDelegate((_, _) => Originals++));
        }
    }
    private sealed class Hook<T>(T original) : IHook<T> where T : Delegate
    {
        public T OriginalFunction => original;
        public IReverseWrapper<T> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 1;
        public nint OriginalFunctionWrapperAddress => 2;
        public IHook<T> Activate() { IsHookActivated = IsHookEnabled = true; return this; }
        IHook IHook.Activate() => Activate();
        public void Enable() => IsHookEnabled = true;
        public void Disable() => IsHookEnabled = false;
    }
}
