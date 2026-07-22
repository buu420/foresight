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
