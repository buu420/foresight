using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.NewGame;

public sealed class SharedNativeHookFanoutFactoryTests
{
    [Fact]
    public void ReturningSharedHooksCallRootThenEveryObserverInOrderAndPreserveReturn()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, new RecordingObserver("one", calls), new RecordingObserver("two", calls));
        TextManagerGetMsgDelegate text = (_, result, _, _) => { calls.Add("text-root"); return result + 4; };
        NsMenuCustomButtonConstructorDelegate custom = storage => { calls.Add("custom-root"); return storage + 8; };

        _ = factory.CreateHook(HookId.TextManagerGetMsg, text, 0x5B9110);
        _ = factory.CreateHook(HookId.NsMenuCustomButtonConstructor, custom, 0x5D2160);

        Assert.Equal((nint)0x2004, inner.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(0x10, 0x2000, 3, 4));
        Assert.Equal((nint)0x3008, inner.GetDetour<NsMenuCustomButtonConstructorDelegate>(HookId.NsMenuCustomButtonConstructor)(0x3000));
        Assert.Equal(["text-root", "one-text", "two-text", "custom-root", "one-custom", "two-custom"], calls);
    }

    [Fact]
    public void VoidSharedHooksCallRootThenEveryObserverInOrder()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, new RecordingObserver("one", calls), new RecordingObserver("two", calls));
        NsMenuFocusSetterDelegate focus = (_, _) => calls.Add("focus-root");
        NsMenuControlBinderDelegate binder = (_, _, _) => calls.Add("binder-root");

        _ = factory.CreateHook(HookId.NsMenuFocusSetter, focus, 0x5DD3E0);
        _ = factory.CreateHook(HookId.NsMenuControlBinder, binder, 0x5DD260);
        inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(0x10, 9);
        inner.GetDetour<NsMenuControlBinderDelegate>(HookId.NsMenuControlBinder)(0x20, 0x30, 7);

        Assert.Equal(["focus-root", "one-focus", "two-focus", "binder-root", "one-binder", "two-binder"], calls);
    }

    [Fact]
    public void ObserverAndFailureSinkFaultsAreIsolatedAndLaterObserversRun()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = new SharedNativeHookFanoutFactory(
            inner,
            new ISharedNativeHookObserver[] { new ThrowingObserver(calls), new RecordingObserver("later", calls) },
            message => { calls.Add($"failure:{message}"); throw new InvalidOperationException("sink"); });
        NsMenuFocusSetterDelegate root = (_, _) => calls.Add("root");

        _ = factory.CreateHook(HookId.NsMenuFocusSetter, root, 0x5DD3E0);
        inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(0x10, 5);

        Assert.Equal("root", calls[0]);
        Assert.Equal("throw-focus", calls[1]);
        Assert.Contains("InvalidOperationException: observer fault", calls[2]);
        Assert.Equal("later-focus", calls[3]);
    }

    [Fact]
    public void RootFaultEscapesWithoutObservationOrFailureReporting()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, new RecordingObserver("observer", calls));
        var expected = new InvalidOperationException("root");
        TextManagerGetMsgDelegate root = (_, _, _, _) => throw expected;

        _ = factory.CreateHook(HookId.TextManagerGetMsg, root, 0x5B9110);
        var actual = Assert.Throws<InvalidOperationException>(() =>
            inner.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(0x10, 0x20, 3, 4));

        Assert.Same(expected, actual);
        Assert.Empty(calls);
    }

    [Fact]
    public void SequenceConstructorSnapshotsOnceAndPreservesDuplicates()
    {
        var calls = new List<string>();
        var observer = new RecordingObserver("duplicate", calls);
        var source = new List<ISharedNativeHookObserver> { observer, observer };
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, calls, source);
        source.Clear();
        NsMenuFocusSetterDelegate root = (_, _) => calls.Add("root");

        _ = factory.CreateHook(HookId.NsMenuFocusSetter, root, 0x5DD3E0);
        inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(0x10, 3);

        Assert.Equal(["root", "duplicate-focus", "duplicate-focus"], calls);
    }

    [Fact]
    public void ConstructorsRejectNullInputsAndObserverElements()
    {
        var inner = new RecordingFactory();
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(null!, new RecordingObserver("x", []), _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(inner, (ISharedNativeHookObserver)null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(inner, (IEnumerable<ISharedNativeHookObserver>)null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(inner, new ISharedNativeHookObserver[] { null! }, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SharedNativeHookFanoutFactory(inner, new RecordingObserver("x", []), null!));
    }

    [Fact]
    public void WrongSharedDelegateTypesAreRejectedBeforeInnerFactoryAndNonSharedPassesThrough()
    {
        var inner = new RecordingFactory();
        var factory = CreateFactory(inner, [], new RecordingObserver("observer", []));
        ModeSelectSteamInitDelegate wrong = _ => 1;

        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.TextManagerGetMsg, wrong, 1));
        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.NsMenuFocusSetter, wrong, 1));
        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.NsMenuCustomButtonConstructor, wrong, 1));
        Assert.Throws<InvalidOperationException>(() => factory.CreateHook(HookId.NsMenuControlBinder, wrong, 1));
        Assert.Empty(inner.Created);

        _ = factory.CreateHook(HookId.ModeSelectSteamInit, wrong, 0x6A9C60);
        Assert.Same(wrong, inner.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit));
    }

    [Fact]
    public void ReturnedHookRootsFanoutAndForwardsLifecycleAndOriginalMetadata()
    {
        var inner = new RecordingFactory(retainDetours: false);
        var factory = CreateFactory(inner, [], new RecordingObserver("observer", []));
        TextManagerGetMsgDelegate root = (_, result, _, _) => result;

        var hook = factory.CreateHook(HookId.TextManagerGetMsg, root, 0x5B9110);
        var weak = inner.LastDetourReference!;
        ForceCollection();

        Assert.True(weak.IsAlive);
        Assert.Equal((nint)0x111, hook.OriginalFunctionAddress);
        Assert.Equal((nint)0x222, hook.OriginalFunctionWrapperAddress);
        Assert.False(hook.IsHookActivated);
        Assert.Same(hook, hook.Activate());
        Assert.True(hook.IsHookActivated);
        hook.Disable();
        Assert.False(hook.IsHookEnabled);
        hook.Enable();
        Assert.True(hook.IsHookEnabled);
        GC.KeepAlive(hook);
    }

    private static SharedNativeHookFanoutFactory CreateFactory(
        RecordingFactory inner,
        List<string> calls,
        params ISharedNativeHookObserver[] observers) =>
        new(inner, observers, message => calls.Add($"failure:{message}"));

    private static SharedNativeHookFanoutFactory CreateFactory(
        RecordingFactory inner,
        List<string> calls,
        IEnumerable<ISharedNativeHookObserver> observers) =>
        new(inner, observers, message => calls.Add($"failure:{message}"));

    private static void ForceCollection()
    {
        for (var index = 0; index < 3; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class RecordingObserver(string name, List<string> calls) : ISharedNativeHookObserver
    {
        public void AfterTextManagerGetMsg(nint textManager, nint result, int fileId, int messageId, nint returned) => calls.Add($"{name}-text");
        public void AfterFocusSet(nint manager, int managerKey) => calls.Add($"{name}-focus");
        public void AfterCustomButtonConstructed(nint storage, nint returned) => calls.Add($"{name}-custom");
        public void AfterControlBound(nint manager, nint focusableState, int managerKey) => calls.Add($"{name}-binder");
    }

    private sealed class ThrowingObserver(List<string> calls) : ISharedNativeHookObserver
    {
        public void AfterFocusSet(nint manager, int managerKey)
        {
            calls.Add("throw-focus");
            throw new InvalidOperationException("observer fault");
        }
    }

    private sealed class RecordingFactory(bool retainDetours = true) : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals = new()
        {
            [HookId.TextManagerGetMsg] = (TextManagerGetMsgDelegate)((_, result, _, _) => result),
            [HookId.NsMenuFocusSetter] = (NsMenuFocusSetterDelegate)((_, _) => { }),
            [HookId.NsMenuCustomButtonConstructor] = (NsMenuCustomButtonConstructorDelegate)(storage => storage),
            [HookId.NsMenuControlBinder] = (NsMenuControlBinderDelegate)((_, _, _) => { }),
            [HookId.ModeSelectSteamInit] = (ModeSelectSteamInitDelegate)(_ => 1),
        };
        private readonly Dictionary<HookId, Delegate> detours = [];

        public List<(HookId Id, nuint Address)> Created { get; } = [];
        public WeakReference? LastDetourReference { get; private set; }
        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate => (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address) where TDelegate : Delegate
        {
            Created.Add((id, address));
            LastDetourReference = new WeakReference(detour);
            if (retainDetours) detours[id] = detour;
            return new FakeHook<TDelegate>((TDelegate)originals[id]);
        }
    }

    private sealed class FakeHook<TDelegate>(TDelegate original) : IHook<TDelegate> where TDelegate : Delegate
    {
        public TDelegate OriginalFunction { get; } = original;
        public IReverseWrapper<TDelegate> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 0x111;
        public nint OriginalFunctionWrapperAddress => 0x222;
        public IHook<TDelegate> Activate() { IsHookEnabled = true; IsHookActivated = true; return this; }
        IHook IHook.Activate() => Activate();
        public void Disable() => IsHookEnabled = false;
        public void Enable() => IsHookEnabled = true;
    }
}
