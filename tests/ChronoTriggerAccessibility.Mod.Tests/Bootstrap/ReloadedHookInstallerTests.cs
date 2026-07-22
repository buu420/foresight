using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class ReloadedHookInstallerTests
{
    [Fact]
    public void PreparesEveryHookInactiveBeforeActivationAndRetainsLifetimeRoots()
    {
        var events = new List<string>();
        var first = new FakeRegistration("first", events);
        var second = new FakeRegistration("second", events);
        var installer = CreateInstaller(first, second);

        installer.PrepareAll(new FakeVerifiedGameBuild(), CreateBoundary());

        Assert.Equal(["prepare:first", "prepare:second"], events);
        Assert.All(installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.Equal(4, installer.LifetimeRootCount);

        installer.ActivateAll();

        Assert.Equal(["prepare:first", "prepare:second", "activate:first", "activate:second"], events);
        Assert.All(installer.PreparedHooks, hook => Assert.True(hook.IsActive));
    }

    [Fact]
    public void PrepareFailureLeavesEveryCreatedHookInactive()
    {
        var events = new List<string>();
        var first = new FakeRegistration("first", events);
        var second = new FakeRegistration("second", events) { FailPrepare = true };
        var installer = CreateInstaller(first, second);

        Assert.Throws<InvalidOperationException>(() =>
            installer.PrepareAll(new FakeVerifiedGameBuild(), CreateBoundary()));

        Assert.All(installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.Contains("disable:first", events);
    }

    [Fact]
    public void ActivationFailureRollsBackEveryPreparedHook()
    {
        var events = new List<string>();
        var first = new FakeRegistration("first", events);
        var second = new FakeRegistration("second", events) { FailActivate = true };
        var installer = CreateInstaller(first, second);
        installer.PrepareAll(new FakeVerifiedGameBuild(), CreateBoundary());

        Assert.Throws<InvalidOperationException>(installer.ActivateAll);

        Assert.All(installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.Equal("disable:first", events[^1]);
    }

    [Fact]
    public void UnexpectedActivePrepareHookIsRootedBeforeThrowingDisableAndBothFailuresSurface()
    {
        var events = new List<string>();
        var registration = new FakeRegistration("unsafe", events)
        {
            InitialActive = true,
            FailDisable = true,
        };
        var installer = CreateInstaller(registration);

        var exception = Assert.ThrowsAny<Exception>(() =>
            installer.PrepareAll(new FakeVerifiedGameBuild(), CreateBoundary()));

        Assert.Single(installer.PreparedHooks);
        Assert.True(installer.LifetimeRootCount >= 2);
        Assert.Contains("active", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disable unsafe", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActivationRollbackAttemptsEveryHookAndSurfacesEveryDisableFailure()
    {
        var events = new List<string>();
        var first = new FakeRegistration("first", events) { FailDisable = true };
        var second = new FakeRegistration("second", events)
        {
            FailActivate = true,
            FailDisable = true,
        };
        var installer = CreateInstaller(first, second);
        installer.PrepareAll(new FakeVerifiedGameBuild(), CreateBoundary());

        var exception = Assert.ThrowsAny<Exception>(installer.ActivateAll);

        Assert.Contains("disable:second", events);
        Assert.Contains("disable:first", events);
        Assert.Contains("disable second", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disable first", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RollbackDetectsHookThatRemainsActiveAfterDisable()
    {
        var events = new List<string>();
        var first = new FakeRegistration("first", events) { DisableLeavesActive = true };
        var second = new FakeRegistration("second", events) { FailActivate = true };
        var installer = CreateInstaller(first, second);
        installer.PrepareAll(new FakeVerifiedGameBuild(), CreateBoundary());

        var exception = Assert.ThrowsAny<Exception>(installer.ActivateAll);

        Assert.True(installer.PreparedHooks[0].IsActive);
        Assert.Contains("first", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReloadedPreparedHookRootsDetourAndControlsUnderlyingHook()
    {
        TestHookDelegate detour = _ => { };
        var nativeHook = new FakeReloadedHook<TestHookDelegate>(detour);
        var prepared = new ReloadedPreparedHook<TestHookDelegate>("test", nativeHook, detour);

        Assert.False(prepared.IsActive);
        Assert.Contains(detour, prepared.LifetimeRoots);

        prepared.Activate();
        Assert.True(prepared.IsActive);

        prepared.Disable();
        Assert.False(prepared.IsActive);
    }

    [Fact]
    public void ReloadedPreparedAsmHookRootsEveryBridgeObjectAndUsesReloaded432EnableFallbackOnce()
    {
        MsgWindowChoiceConfirmProbeDelegate callback = _ => { };
        var reverseWrapper = new object();
        IReadOnlyList<string> code = new[] { "use32", "pushfd", "popfd" };
        var nativeHook = new FakeReloadedAsmHook();
        var prepared = new ReloadedPreparedAsmHook(
            "choice confirm",
            nativeHook,
            [callback, reverseWrapper, code]);

        Assert.False(prepared.IsActive);
        Assert.Contains(callback, prepared.LifetimeRoots);
        Assert.Contains(reverseWrapper, prepared.LifetimeRoots);
        Assert.Contains(code, prepared.LifetimeRoots);

        prepared.Activate();
        prepared.Activate();
        Assert.True(prepared.IsActive);
        Assert.Equal(1, nativeHook.ActivateCount);
        Assert.Equal(1, nativeHook.EnableCount);

        prepared.Disable();
        prepared.Disable();
        Assert.False(prepared.IsActive);
        Assert.Equal(1, nativeHook.DisableCount);
    }

    [Fact]
    public void ReloadedPreparedAsmHookTreatsThrownActivationAsPotentiallyActiveUntilDisableSucceeds()
    {
        var nativeHook = new FakeReloadedAsmHook { FailActivateAfterPatch = true };
        var prepared = new ReloadedPreparedAsmHook("choice confirm", nativeHook, [new object()]);

        Assert.Throws<InvalidOperationException>(prepared.Activate);

        Assert.True(prepared.IsActive);
        Assert.Equal(1, nativeHook.ActivateCount);
        Assert.Equal(0, nativeHook.EnableCount);

        prepared.Disable();

        Assert.False(prepared.IsActive);
        Assert.Equal(1, nativeHook.DisableCount);
    }

    [Fact]
    public void ReloadedPreparedAsmHookTreatsThrownEnableAsPotentiallyActiveUntilDisableSucceeds()
    {
        var nativeHook = new FakeReloadedAsmHook { FailEnable = true };
        var prepared = new ReloadedPreparedAsmHook("choice confirm", nativeHook, [new object()]);

        Assert.Throws<InvalidOperationException>(prepared.Activate);

        Assert.True(prepared.IsActive);
        Assert.Equal(1, nativeHook.ActivateCount);
        Assert.Equal(1, nativeHook.EnableCount);

        prepared.Disable();

        Assert.False(prepared.IsActive);
        Assert.Equal(1, nativeHook.DisableCount);
    }

    [Fact]
    public void ReloadedPreparedAsmHookRemainsPotentiallyActiveWhenDisableThrows()
    {
        var nativeHook = new FakeReloadedAsmHook { FailDisable = true };
        var prepared = new ReloadedPreparedAsmHook("choice confirm", nativeHook, [new object()]);
        prepared.Activate();

        Assert.Throws<InvalidOperationException>(prepared.Disable);

        Assert.True(prepared.IsActive);
        Assert.Equal(1, nativeHook.DisableCount);

        nativeHook.FailDisable = false;
        prepared.Disable();

        Assert.False(prepared.IsActive);
        Assert.Equal(2, nativeHook.DisableCount);
    }

    [Fact]
    public void AssemblyHookParticipatesInTransactionalReverseRollback()
    {
        var events = new List<string>();
        var nativeHook = new FakeReloadedAsmHook(events);
        MsgWindowChoiceConfirmProbeDelegate callback = _ => { };
        var preparedAsm = new ReloadedPreparedAsmHook(
            "asm",
            nativeHook,
            [callback, new object(), new[] { "use32" }]);
        var first = new StaticRegistration(preparedAsm, events);
        var second = new FakeRegistration("failure", events) { FailActivate = true };
        var installer = CreateInstaller(first, second);
        installer.PrepareAll(new FakeVerifiedGameBuild(), CreateBoundary());

        Assert.Throws<InvalidOperationException>(installer.ActivateAll);

        Assert.False(preparedAsm.IsActive);
        Assert.Equal(1, nativeHook.ActivateCount);
        Assert.Equal(1, nativeHook.DisableCount);
        Assert.True(events.IndexOf("disable:failure") < events.IndexOf("disable:asm"));
    }

    private static ReloadedHookInstaller CreateInstaller(params IHookRegistration[] registrations) =>
        new(registrations);

    private static UnmanagedBoundaryGuard CreateBoundary() =>
        new(new AccessibilityRuntimeTests.RecordingLog(), new AccessibilityRuntimeTests.RecordingFatalError());

    private sealed class FakeRegistration(string name, List<string> events) : IHookRegistration
    {
        public bool FailPrepare { get; init; }
        public bool FailActivate { get; init; }
        public bool FailDisable { get; init; }
        public bool InitialActive { get; init; }
        public bool DisableLeavesActive { get; init; }
        public string Name { get; } = name;

        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
        {
            events.Add($"prepare:{Name}");
            if (FailPrepare)
            {
                throw new InvalidOperationException($"prepare {Name}");
            }

            return new Prepared(Name, events, FailActivate, FailDisable, InitialActive, DisableLeavesActive);
        }

        private sealed class Prepared(
            string name,
            List<string> events,
            bool failActivate,
            bool failDisable,
            bool initialActive,
            bool disableLeavesActive) : IPreparedHook
        {
            private readonly object delegateRoot = new();
            public string Name { get; } = name;
            public bool IsActive { get; private set; } = initialActive;
            public IReadOnlyCollection<object> LifetimeRoots => [delegateRoot];

            public void Activate()
            {
                events.Add($"activate:{Name}");
                if (failActivate)
                {
                    throw new InvalidOperationException($"activate {Name}");
                }

                IsActive = true;
            }

            public void Disable()
            {
                events.Add($"disable:{Name}");
                if (failDisable)
                {
                    throw new InvalidOperationException($"disable {Name}");
                }

                if (!disableLeavesActive)
                {
                    IsActive = false;
                }
            }
        }
    }

    private sealed class FakeVerifiedGameBuild : IVerifiedGameBuild
    {
        public IReadOnlyDictionary<HookId, nuint> HookAddresses { get; } = new Dictionary<HookId, nuint>();
    }

    [Function(CallingConventions.MicrosoftThiscall)]
    private delegate void TestHookDelegate(nint instance);

    private sealed class StaticRegistration(
        IPreparedHook prepared,
        List<string> events) : IHookRegistration
    {
        public string Name => prepared.Name;

        public IPreparedHook Prepare(IVerifiedGameBuild build, UnmanagedBoundaryGuard boundary)
        {
            events.Add($"prepare:{Name}");
            return prepared;
        }
    }

    private sealed class FakeReloadedAsmHook(List<string>? events = null) : IAsmHook
    {
        public bool FailActivateAfterPatch { get; init; }
        public bool FailEnable { get; init; }
        public bool FailDisable { get; set; }
        public bool IsEnabled { get; private set; }
        public int ActivateCount { get; private set; }
        public int EnableCount { get; private set; }
        public int DisableCount { get; private set; }

        public IAsmHook Activate()
        {
            events?.Add("activate:asm");
            ActivateCount++;
            if (FailActivateAfterPatch)
            {
                throw new InvalidOperationException("activate failed after applying a patch");
            }
            return this;
        }

        public void Enable()
        {
            EnableCount++;
            if (FailEnable)
            {
                throw new InvalidOperationException("enable failed after activation");
            }
            IsEnabled = true;
        }

        public void Disable()
        {
            events?.Add("disable:asm");
            DisableCount++;
            if (FailDisable)
            {
                throw new InvalidOperationException("disable failed");
            }
            IsEnabled = false;
        }
    }

    private sealed class FakeReloadedHook<TDelegate>(TDelegate original) : IHook<TDelegate>
    {
        public TDelegate OriginalFunction { get; } = original;
        public IReverseWrapper<TDelegate> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public IntPtr OriginalFunctionAddress => (IntPtr)1;
        public IntPtr OriginalFunctionWrapperAddress => (IntPtr)1;

        public IHook<TDelegate> Activate()
        {
            IsHookActivated = true;
            IsHookEnabled = true;
            return this;
        }

        IHook IHook.Activate() => Activate();

        public void Disable() => IsHookEnabled = false;
        public void Enable() => IsHookEnabled = true;
    }
}
