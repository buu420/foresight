using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Mod.Tests.Bootstrap;
using ChronoTriggerAccessibility.Native.Hooks;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class WorldNavigationHookSetTests
{
    // 0x264C40 combines the pad three times. State 0x0F (0x265147) ORs the physical
    // pad and then GATES the whole chain on it being non-zero:
    //     or ecx, edi / mov [ebx], ecx / jne  ->  else state 0x16, chain over.
    // States 0x10 (0x265247) and 0x11 (0x26536F) each re-read the two native getters
    // and overwrite the emulated accumulator before the direction dispatch at states
    // 0x12-0x15 reads it. A synthetic direction therefore has to be injected at all
    // three sites: only at 0x26536F it is never reached with no physical input, and
    // only at 0x265147 it is overwritten before the direction tests.
    private static readonly uint[] NativeCombineSites = [0x265147, 0x265247, 0x26536F];
    private const string CombineEncoding = "0BBE1C330000";

    [Fact]
    public void CatalogHooksEveryNativeWorldPadCombineSite()
    {
        var hooked = GameVersionCatalog.Hooks
            .Where(contract => contract.Kind == NativeHookKind.AssemblyInstructionSite &&
                contract.ExpectedBytes.AsSpan().SequenceEqual(Convert.FromHexString(CombineEncoding)))
            .Select(contract => contract.Rva)
            .OrderBy(rva => rva)
            .ToArray();

        Assert.Equal(NativeCombineSites, hooked);
    }

    [Fact]
    public void EveryCombineSiteRoutesToThePadCallbackAndTheTickSiteDoesNot()
    {
        var padCalls = new List<(nint Context, uint Pad)>();
        var tickCalls = new List<(nint Context, uint Pad)>();
        var harness = new Harness(
            (context, pad) => tickCalls.Add((context, pad)),
            (context, pad) => { padCalls.Add((context, pad)); return pad | 0x800u; });

        harness.PrepareAll();

        Assert.Equal(NativeCombineSites.Length + 1, harness.Factory.Created.Count);
        foreach (var site in NativeCombineSites)
        {
            var created = Assert.Single(harness.Factory.Created, c => c.Address == Harness.ImageBase + site);
            Assert.Equal(AsmHookBehaviour.ExecuteAfter, created.Options.Behaviour);
            Assert.Equal(6, created.Options.HookLength);
            Assert.Equal(0x800u, harness.Invoke(created, 0x29A6FCC0, 0));
        }

        Assert.Equal(NativeCombineSites.Length, padCalls.Count);
        Assert.All(padCalls, call => Assert.Equal(0x29A6FCC0, call.Context));
        Assert.Empty(tickCalls);
    }

    [Fact]
    public void ActivationRequiresEveryPreparedBoundary()
    {
        var harness = new Harness((_, _) => { }, (_, pad) => pad);
        var registrations = harness.HookSet.Registrations;

        registrations[0].Prepare(harness.Build, harness.Boundary);

        Assert.Throws<InvalidOperationException>(harness.HookSet.AfterHooksActivated);
        for (var i = 1; i < registrations.Count; i++) registrations[i].Prepare(harness.Build, harness.Boundary);
        harness.HookSet.AfterHooksActivated();
    }

    [Fact]
    public void PadAssemblyPreservesFlagsAndWritesTheResultIntoTheSavedEdiSlot()
    {
        var code = WorldNavigationHookSet.BuildPadAssembly(new RuntimeAsmHookAssemblyContext(
            "call dword [managed_callback]", "push eax", "pop eax"));

        // PUSHFD then PUSHAD leaves the saved EDI at [esp]; the two pushed arguments
        // are removed before the result is written back over it.
        Assert.Equal(
            ["use32", "pushfd", "pushad", "push edi", "push esi", "call dword [managed_callback]",
                "add esp, 8", "mov [esp], eax", "popad", "popfd"],
            code);
    }

    private sealed class Harness
    {
        internal const nuint ImageBase = 0x00400000;

        internal Harness(Action<nint, uint> tick, Func<nint, uint, uint> pad)
        {
            Factory = new RecordingAsmHookFactory();
            HookSet = new WorldNavigationHookSet(Factory, tick, pad, _ => { });
            Build = new FakeBuild();
            Boundary = new UnmanagedBoundaryGuard(
                new AccessibilityRuntimeTests.RecordingLog(),
                new AccessibilityRuntimeTests.RecordingFatalError());
        }

        internal RecordingAsmHookFactory Factory { get; }
        internal WorldNavigationHookSet HookSet { get; }
        internal FakeBuild Build { get; }
        internal UnmanagedBoundaryGuard Boundary { get; }

        internal void PrepareAll()
        {
            foreach (var registration in HookSet.Registrations) registration.Prepare(Build, Boundary);
            HookSet.AfterHooksActivated();
        }

        internal uint Invoke(RecordingAsmHookFactory.Site created, nint context, uint pad) =>
            ((FieldNavigationPadProbeDelegate)created.Callback)(context, pad);
    }

    internal sealed class FakeBuild : IVerifiedGameBuild
    {
        public nuint ImageBaseAddress => Harness.ImageBase;
        public IReadOnlyDictionary<HookId, nuint> HookAddresses { get; } =
            new ReadOnlyDictionary<HookId, nuint>(GameVersionCatalog.Hooks
                .ToDictionary(contract => contract.Id, contract => Harness.ImageBase + contract.Rva));
    }

    internal sealed class RecordingAsmHookFactory : IRuntimeNativeAsmHookFactory
    {
        internal sealed record Site(HookId Id, nuint Address, RuntimeAsmHookOptions Options, Delegate Callback);

        internal List<Site> Created { get; } = [];

        public IPreparedHook CreateAsmHook<TDelegate>(
            HookId id,
            string name,
            TDelegate callback,
            nuint address,
            Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly,
            RuntimeAsmHookOptions options)
            where TDelegate : Delegate
        {
            buildAssembly(new RuntimeAsmHookAssemblyContext("call dword [managed_callback]", "push eax", "pop eax"));
            Created.Add(new Site(id, address, options, callback));
            return new StubPreparedHook(name);
        }
    }

    private sealed class StubPreparedHook(string name) : IPreparedHook
    {
        public string Name { get; } = name;
        public bool IsActive { get; private set; }
        public IReadOnlyCollection<object> LifetimeRoots { get; } = [];
        public void Activate() => IsActive = true;
        public void Disable() => IsActive = false;
    }
}
