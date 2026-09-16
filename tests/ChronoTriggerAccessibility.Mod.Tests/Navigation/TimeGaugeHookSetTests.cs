using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Mod.Tests.Bootstrap;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TimeGaugeHookSetTests
{
    private const nuint Image = 0x400000, Scene = 0x700000, Data = 0x200000, Manager = 0x600000,
        Banks = 0x610000, Menu = 0x620000, Lines = 0x630000;
    private static readonly string[] Labels =
        ["65 000 000 B.C.", "12 000 B.C.", "600 A.D.", "1000 A.D.", "1999 A.D.", "2300 A.D.", "End of Time"];

    [Fact]
    public void OpeningPresentsTheGaugeWithTheCurrentEraHighlightedAsNotSelectable()
    {
        var h = new Harness(slot: 3, current: 3);
        h.Init();
        var presented = Assert.IsType<MenuPresented>(Assert.Single(h.Events));
        Assert.Equal(TimeGaugeHookSet.Title, presented.Title);
        Assert.Equal(new MenuOwner(TimeGaugeHookSet.OwnerSource, Scene), presented.Owner);
        Assert.Equal("1000 A.D.", presented.Focus!.Label);
        Assert.Equal(4, presented.Focus.Position);
        Assert.Equal(7, presented.Focus.Count);
        Assert.True(presented.Focus.Disabled);
        Assert.Contains("Not selectable", presented.Focus.Help);
        Assert.Equal([TimeGaugeHookSet.KeyHelp], presented.StatusDetails);
        // The same frame's update speaks nothing new.
        h.Update();
        Assert.Single(h.Events);
    }

    [Fact]
    public void EachChangedSlotIsSpokenOnceWithItsLabelAndDescription()
    {
        var h = new Harness(slot: 3, current: 3);
        h.Init();
        h.Update(); h.Update();
        h.Slot(2); h.Update(); h.Update();
        var focus = Assert.IsType<MenuFocusChanged>(Assert.Single(h.Events.Skip(1)));
        Assert.Equal("1999 A.D.", focus.Focus!.Label);
        Assert.Equal(3, focus.Focus.Position);
        Assert.False(focus.Focus.Disabled);
        Assert.Equal("Description 0xA6", focus.Focus.Help);
        h.Slot(0); h.Update();
        Assert.Equal("End of Time", Assert.IsType<MenuFocusChanged>(h.Events[^1]).Focus!.Label);
        Assert.Null(((MenuFocusChanged)h.Events[^1]).Focus!.Help);
    }

    [Fact]
    public void CommitAnnouncesTheDestinationAndClosesTheMenu()
    {
        var h = new Harness(slot: 3, current: 3);
        h.Init();
        h.Slot(1); h.Update();
        h.Close(result: 0x1F2); h.Update(); h.Update();
        Assert.Equal("2300 A.D.", Assert.IsType<MenuActivated>(h.Events[2]).Label);
        Assert.IsType<MenuExited>(h.Events[3]);
        Assert.Equal("Traveling to 2300 A.D.", Assert.IsType<NavigationAnnouncement>(h.Events[4]).Text);
        Assert.Equal(5, h.Events.Count);
    }

    [Fact]
    public void CancelClosesQuietlyAndAForeignSceneIsIgnored()
    {
        var h = new Harness(slot: 3, current: 3);
        h.Init();
        h.Close(result: 0xFFFF); h.Update(Scene + 0x1000); Assert.Single(h.Events);
        h.Update();
        Assert.IsType<MenuExited>(h.Events[1]);
        Assert.Equal("Time gauge closed.", Assert.IsType<NavigationAnnouncement>(h.Events[2]).Text);
        Assert.Equal(3, h.Events.Count);
    }

    [Fact]
    public void UnreadableLabelsAreACoverageFailureNotSilence()
    {
        var h = new Harness(slot: 3, current: 3);
        h.Memory.Word(Scene + TimeGaugeCapture.SlotOffset, 9);
        h.Init();
        Assert.Empty(h.Events);
        Assert.Single(h.Failures);
    }

    [Fact]
    public void HooksUseTheAuditedEntriesAndNeverInjectInput()
    {
        var h = new Harness(slot: 3, current: 3);
        Assert.Equal(2, h.Factory.Created.Count);
        Assert.Contains(HookId.TimeGaugeSceneInit, h.Factory.Created.Keys);
        Assert.Contains(HookId.TimeGaugeSceneUpdate, h.Factory.Created.Keys);
        Assert.Equal(Image + 0x2989B0, h.Factory.Created[HookId.TimeGaugeSceneInit]);
        Assert.Equal(Image + 0x2996D0, h.Factory.Created[HookId.TimeGaugeSceneUpdate]);
        h.Init(); h.Update();
        Assert.Equal(1, h.OriginalInitCalls);
        Assert.Equal(1, h.OriginalUpdateCalls);
    }

    private sealed class Harness
    {
        public NavigationMemory Memory { get; }
        public List<AccessibilityEvent> Events { get; } = [];
        public List<string> Failures { get; } = [];
        public RecordingHookFactory Factory { get; } = new();
        public TimeGaugeHookSet HookSet { get; }
        public int OriginalInitCalls;

        public Harness(int slot, int current)
        {
            Memory = Build(slot, current);
            HookSet = new TimeGaugeHookSet(Factory, Memory, new Dispatcher(Events, Failures));
            var build = new VerifiedBuild(Image, GameVersionCatalog.Hooks.ToDictionary(c => c.Id, c => Image + c.Rva));
            var errors = new AccessibilityRuntimeTests.RecordingLog();
            var boundary = new UnmanagedBoundaryGuard(errors, new AccessibilityRuntimeTests.RecordingFatalError());
            foreach (var registration in HookSet.Registrations) registration.Prepare(build, boundary).Activate();
            HookSet.AfterHooksActivated();
            Factory.Originals[HookId.TimeGaugeSceneUpdate] = (TimeGaugeSceneUpdateDelegate)((_, _) => OriginalUpdateCalls++);
        }
        public int OriginalUpdateCalls;

        public void Init()
        {
            var detour = (TimeGaugeSceneInitDelegate)Factory.Detours[HookId.TimeGaugeSceneInit];
            Factory.Originals[HookId.TimeGaugeSceneInit] = (TimeGaugeSceneInitDelegate)(_ => { OriginalInitCalls++; return 1; });
            detour((nint)Scene);
        }
        public void Update(nuint scene = Scene) =>
            ((TimeGaugeSceneUpdateDelegate)Factory.Detours[HookId.TimeGaugeSceneUpdate])((nint)scene, 0.016f);
        public void Slot(int slot) => Memory.Word(Scene + TimeGaugeCapture.SlotOffset, (uint)slot);
        public void Close(int result) => Memory.Byte(Scene + TimeGaugeCapture.ClosingOffset, 1).Short(Data + 0x2E2AF, (ushort)result);

        private static NavigationMemory Build(int slot, int current)
        {
            var memory = new NavigationMemory()
                .Word(Scene + TimeGaugeCapture.SlotOffset, (uint)slot).Word(Scene + TimeGaugeCapture.CurrentEraOffset, (uint)current)
                .Byte(Scene + TimeGaugeCapture.ClosingOffset, 0)
                .Word(Image + 0x41B4BC, (uint)Data).Short(Data + 0x2E2AF, 0)
                .Word(Image + 0x41C3D8, (uint)Manager)
                .Word(Manager, (uint)Banks).Word(Manager + 4, (uint)Banks + 0x24 * 4)
                .Word(Banks + 0x23 * 4, (uint)Menu)
                .Word(Menu, (uint)Lines).Word(Menu + 4, (uint)Lines + 0xB0 * 24);
            for (var id = 0; id < 0xB0; id++) memory.String(Lines + (nuint)(id * 24), $"Description 0x{id:X2}");
            for (var s = 0; s < Labels.Length; s++) memory.String(Lines + (nuint)(TimeGaugeCapture.LabelId(s) * 24), Labels[6 - s]);
            return memory;
        }
    }

    private sealed record VerifiedBuild(nuint ImageBaseAddress, IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class Dispatcher(List<AccessibilityEvent> events, List<string> failures) : ISemanticEventDispatcher
    {
        public int Generation => 0;
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent accessibilityEvent) => events.Add(accessibilityEvent);
        public void ReportCoverageFailure(string message) => failures.Add(message);
    }

    internal sealed class RecordingHookFactory : IRuntimeNativeHookFactory
    {
        public Dictionary<HookId, nuint> Created { get; } = [];
        public Dictionary<HookId, Delegate> Detours { get; } = [];
        public Dictionary<HookId, Delegate> Originals { get; } = [];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address) where TDelegate : Delegate
        {
            Created[id] = address; Detours[id] = detour;
            return new Hook<TDelegate>(() => (TDelegate)Originals[id]);
        }

        private sealed class Hook<TDelegate>(Func<TDelegate> original) : IHook<TDelegate> where TDelegate : Delegate
        {
            public TDelegate OriginalFunction => original();
            public IReverseWrapper<TDelegate> ReverseWrapper => null!;
            public bool IsHookEnabled { get; private set; }
            public bool IsHookActivated { get; private set; }
            public nint OriginalFunctionAddress => 1;
            public nint OriginalFunctionWrapperAddress => 2;
            public IHook<TDelegate> Activate() { IsHookActivated = IsHookEnabled = true; return this; }
            IHook IHook.Activate() => Activate();
            public void Enable() => IsHookEnabled = true;
            public void Disable() => IsHookEnabled = false;
        }
    }

    /// <summary>Same layout helper as the native tests, local to this project.</summary>
    internal sealed class NavigationMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        private nuint heap = 0x900000;
        public NavigationMemory Add(nuint address, ReadOnlySpan<byte> data)
        {
            for (var i = 0; i < data.Length; i++) bytes[address + (nuint)i] = data[i];
            return this;
        }
        public NavigationMemory Byte(nuint address, byte value) => Add(address, [value]);
        public NavigationMemory Word(nuint address, uint value)
        {
            Span<byte> data = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(data, value); return Add(address, data);
        }
        public NavigationMemory Short(nuint address, ushort value)
        {
            Span<byte> data = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(data, value); return Add(address, data);
        }
        public NavigationMemory String(nuint address, string value)
        {
            var data = Encoding.UTF8.GetBytes(value); var layout = new byte[24];
            BinaryPrimitives.WriteInt32LittleEndian(layout.AsSpan(16), data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(layout.AsSpan(20), Math.Max(15, data.Length));
            if (data.Length <= 15) data.CopyTo(layout, 0);
            else { BinaryPrimitives.WriteUInt32LittleEndian(layout, (uint)heap); Add(heap, data); heap += 8192; }
            return Add(address, layout);
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                if (!bytes.TryGetValue(address + (nuint)i, out var value)) return false;
                destination[i] = value;
            }
            return true;
        }
    }
}
