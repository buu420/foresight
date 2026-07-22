using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.NewGame;

public sealed class SharedNativeHookFanoutFactoryTests
{
    [Fact]
    public void SharedTextHookCallsStartupDetourOnceThenObserverAndReturnsStartupResult()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var observer = new RecordingObserver(calls);
        var factory = new SharedNativeHookFanoutFactory(inner, observer, _ => calls.Add("failure"));
        TextManagerGetMsgDelegate startup = (_, result, _, _) =>
        {
            calls.Add("startup");
            return result + 4;
        };

        var hook = factory.CreateHook(HookId.TextManagerGetMsg, startup, 0x5B9110);
        var returned = inner.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
            0x1000, 0x2000, 0x23, 0xD6);

        Assert.Equal((nint)0x2004, returned);
        Assert.Equal(["startup", "text"], calls);
        Assert.Single(inner.Created);
        Assert.Same(inner.GetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg), hook.OriginalFunction);
    }

    [Fact]
    public void SharedFocusHookContainsObserverAndFailureSinkExceptionsAfterStartupDetour()
    {
        var calls = new List<string>();
        var inner = new RecordingFactory();
        var observer = new ThrowingObserver(calls);
        var factory = new SharedNativeHookFanoutFactory(
            inner,
            observer,
            _ =>
            {
                calls.Add("failure");
                throw new InvalidOperationException("failure sink fault");
            });
        NsMenuFocusSetterDelegate startup = (_, _) => calls.Add("startup");

        _ = factory.CreateHook(HookId.NsMenuFocusSetter, startup, 0x5DD3E0);
        var exception = Record.Exception(() =>
            inner.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(0x3000, 1));

        Assert.Null(exception);
        Assert.Equal(["startup", "focus", "failure"], calls);
        Assert.Single(inner.Created);
    }

    [Fact]
    public void NonSharedHookPassesThroughWithoutWrapping()
    {
        var inner = new RecordingFactory();
        var factory = new SharedNativeHookFanoutFactory(
            inner,
            new RecordingObserver([]),
            _ => { });
        ModeSelectSteamInitDelegate detour = _ => 1;

        _ = factory.CreateHook(HookId.ModeSelectSteamInit, detour, 0x6A9C60);

        Assert.Same(detour, inner.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit));
    }

    [Fact]
    public void ReturnedHookRootsFanoutDelegateForNativeLifetime()
    {
        var inner = new RecordingFactory(retainDetours: false);
        var factory = new SharedNativeHookFanoutFactory(
            inner,
            new RecordingObserver([]),
            _ => { });
        TextManagerGetMsgDelegate startup = (_, result, _, _) => result;

        var hook = factory.CreateHook(HookId.TextManagerGetMsg, startup, 0x5B9110);
        var weak = inner.LastDetourReference!;
        ForceCollection();

        Assert.True(weak.IsAlive);
        GC.KeepAlive(hook);
    }

    private static void ForceCollection()
    {
        for (var index = 0; index < 3; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class RecordingObserver(List<string> calls) : INewGameSharedNativeObserver
    {
        public void AfterTextManagerGetMsg(
            nint textManager,
            nint result,
            int fileId,
            int messageId,
            nint returned) => calls.Add("text");

        public void AfterFocusSet(nint manager, int managerKey) => calls.Add("focus");
    }

    private sealed class ThrowingObserver(List<string> calls) : INewGameSharedNativeObserver
    {
        public void AfterTextManagerGetMsg(
            nint textManager,
            nint result,
            int fileId,
            int messageId,
            nint returned) => throw new InvalidOperationException("text fault");

        public void AfterFocusSet(nint manager, int managerKey)
        {
            calls.Add("focus");
            throw new InvalidOperationException("focus fault");
        }
    }

    private sealed class RecordingFactory(bool retainDetours = true) : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals = new()
        {
            [HookId.TextManagerGetMsg] = (TextManagerGetMsgDelegate)((_, result, _, _) => result),
            [HookId.NsMenuFocusSetter] = (NsMenuFocusSetterDelegate)((_, _) => { }),
            [HookId.ModeSelectSteamInit] = (ModeSelectSteamInitDelegate)(_ => 1),
        };
        private readonly Dictionary<HookId, Delegate> detours = [];

        public List<(HookId Id, nuint Address)> Created { get; } = [];
        public WeakReference? LastDetourReference { get; private set; }

        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate =>
            (TDelegate)detours[id];

        public TDelegate GetOriginal<TDelegate>(HookId id) where TDelegate : Delegate =>
            (TDelegate)originals[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            Created.Add((id, address));
            LastDetourReference = new WeakReference(detour);
            if (retainDetours)
            {
                detours[id] = detour;
            }
            else
            {
                // Invocation is not needed by the root-only test.
                detours.Remove(id);
            }
            return new FakeHook<TDelegate>((TDelegate)originals[id]);
        }
    }

    private sealed class FakeHook<TDelegate>(TDelegate original) : IHook<TDelegate>
        where TDelegate : Delegate
    {
        public TDelegate OriginalFunction { get; } = original;
        public IReverseWrapper<TDelegate> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 1;
        public nint OriginalFunctionWrapperAddress => 1;
        public IHook<TDelegate> Activate() { IsHookEnabled = true; IsHookActivated = true; return this; }
        IHook IHook.Activate() => Activate();
        public void Disable() => IsHookEnabled = false;
        public void Enable() => IsHookEnabled = true;
    }
}
