using System.Buffers.Binary;
using System.Linq.Expressions;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Startup;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class CompleteCompositionTests
{
    private const nuint ImageBase = 0x400000;

    [Fact]
    public void IntegratedCompositionPreparesEachAuditedHookOnceAndActivatesEveryParticipant()
    {
        var factory = new RecordingFactory();
        var dispatcher = new RecordingDispatcher();
        var composition = ChronoTriggerAccessibility.Mod.Mod.CreateCompleteAccessibilityComposition(
            factory, factory, factory, new StartupMemory(), dispatcher, new OpeningMovieTimeline());
        var build = new VerifiedBuild(ImageBase, GameVersionCatalog.Hooks.ToDictionary(
            contract => contract.Id, contract => ImageBase + contract.Rva));
        var errors = new ErrorRecorder();

        composition.Installer.PrepareAll(build, new UnmanagedBoundaryGuard(errors, errors));

        Assert.Equal(126, factory.Created.Count);
        Assert.Equal(build.HookAddresses.OrderBy(entry => entry.Key), factory.Created.OrderBy(entry => entry.Key));
        Assert.All(composition.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));

        composition.Installer.ActivateAll();

        Assert.All(composition.Installer.PreparedHooks, hook => Assert.True(hook.IsActive));
        Assert.Contains(new StartupSceneEntered(StartupSceneKind.SquareEnixLogo), dispatcher.Events);
        Assert.Empty(dispatcher.Failures);
        Assert.Empty(errors.Errors);

        composition.Installer.DisableAll();

        Assert.All(composition.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.Empty(dispatcher.Failures);
        Assert.Empty(errors.Errors);
    }

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress, IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class RecordingFactory :
        IRuntimeNativeHookFactory, IRuntimeNativeAsmHookFactory, IRuntimeNativeFunctionWrapperFactory
    {
        public Dictionary<HookId, nuint> Created { get; } = [];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            Created.Add(id, address);
            return new NativeHook<TDelegate>(CreateNoOp<TDelegate>());
        }

        public TDelegate CreateWrapper<TDelegate>(nuint address) where TDelegate : Delegate =>
            CreateNoOp<TDelegate>();

        public IPreparedHook CreateAsmHook<TDelegate>(
            HookId id, string name, TDelegate callback, nuint address,
            Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly,
            RuntimeAsmHookOptions options) where TDelegate : Delegate
        {
            Created.Add(id, address);
            var code = buildAssembly(new RuntimeAsmHookAssemblyContext(
                "call managed_callback", "push eax\npush ecx\npush edx", "pop edx\npop ecx\npop eax"));
            return new PreparedProbe(name, [callback, code]);
        }

        private static TDelegate CreateNoOp<TDelegate>() where TDelegate : Delegate
        {
            var invoke = typeof(TDelegate).GetMethod("Invoke")!;
            var parameters = invoke.GetParameters().Select(parameter => Expression.Parameter(parameter.ParameterType));
            return Expression.Lambda<TDelegate>(Expression.Default(invoke.ReturnType), parameters).Compile();
        }
    }

    private sealed class NativeHook<TDelegate>(TDelegate original) : IHook<TDelegate> where TDelegate : Delegate
    {
        public TDelegate OriginalFunction => original;
        public IReverseWrapper<TDelegate> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 1;
        public nint OriginalFunctionWrapperAddress => 2;
        public IHook<TDelegate> Activate()
        {
            IsHookActivated = true;
            IsHookEnabled = true;
            return this;
        }
        IHook IHook.Activate() => Activate();
        public void Enable() => IsHookEnabled = true;
        public void Disable() => IsHookEnabled = false;
    }

    private sealed class PreparedProbe(string name, IReadOnlyCollection<object> roots) : IPreparedHook
    {
        public string Name => name;
        public bool IsActive { get; private set; }
        public IReadOnlyCollection<object> LifetimeRoots => roots;
        public void Activate() => IsActive = true;
        public void Disable() => IsActive = false;
    }

    private sealed class StartupMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address != ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva || destination.Length != 4)
                return false;
            BinaryPrimitives.WriteInt32LittleEndian(destination, StartupTitleHookSet.SquareEnixSceneId);
            return true;
        }
    }

    private sealed class RecordingDispatcher : ISemanticEventDispatcher
    {
        public int Generation => 0;
        public List<AccessibilityEvent> Events { get; } = [];
        public List<string> Failures { get; } = [];
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent accessibilityEvent) => Events.Add(accessibilityEvent);
        public void ReportCoverageFailure(string message) => Failures.Add(message);
    }

    private sealed class ErrorRecorder : IModLog, IAccessibleFatalError
    {
        public List<string> Errors { get; } = [];
        public void Info(string message) { }
        public void Error(string message) => Errors.Add(message);
        public void Show(string message) => Errors.Add(message);
    }
}
