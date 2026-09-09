using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Settings;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Settings;

/// <summary>
/// Builds one live <c>MenuNodeConfigSteam</c> instance in fake memory and drives
/// the audited native boundaries in the same order the game does.
///
/// The layout mirrors the shape proven by
/// <c>ChronoTriggerAccessibility.Native.Tests.SteamSettingsCaptureTests</c>: a
/// 0x340 root, a descriptor vector of 0x0C triples, one 0x30 category element per
/// page, and one 0x88 row element per visible row. Rows here deliberately carry no
/// value vector, so no selected-value observation is required and the fixture
/// exercises the lifecycle rather than the value-capture rules.
/// </summary>
internal sealed class SteamSettingsFixture
{
    internal const nuint ImageBase = 0x00400000;
    internal const nuint Root = 0x00A00000;
    internal const nuint Descriptors = 0x00A10000;
    internal const nuint Categories = 0x00A20000;
    internal const nuint Rows = 0x00A30000;
    internal const nuint CategoryManager = 0x00A40000;
    internal const nuint ActiveManagerPointers = 0x00A50000;
    internal const nuint ActiveManager = 0x00A60000;
    internal const nuint ActiveRoot = 0x00A70000;

    /// <summary>
    /// The resolution selector owns its own input manager, separate from the page's.
    /// The native callback reads its focus key at [[context] + 0x2C4].
    /// </summary>
    internal const nuint SelectorManager = 0x00A90000;
    internal const nuint HelpBase = 0x00A80000;
    internal const nuint LabelBase = 0x00B00000;
    internal const nuint StringDataBase = 0x00B80000;

    private const int RootSize = 0x340;
    private const int ContextOffset = 0x2F8;
    private const int DescriptorVectorOffset = 0x2FC;
    private const int CategoryVectorOffset = 0x308;
    private const int ModeOffset = 0x314;
    private const int CategoryManagerOffset = 0x324;
    private const int ActiveRootOffset = 0x328;
    private const int ActiveManagerVectorOffset = 0x32C;
    private const int FocusKeyOffset = 0x338;
    private const int ActivePageOffset = 0x33C;
    private const int DescriptorStride = 0x0C;
    private const int CategoryStride = 0x30;
    private const int RowStride = 0x88;
    private const int RowHelpVectorOffset = 0x18;
    private const int RowValueVectorOffset = 0x24;
    private const int ManagerSize = 0x2C8;
    private const int ManagerKeyOffset = 0x2C4;

    private int nextLabelSlot;
    private int nextStringDataSlot;

    internal SteamSettingsFixture(
        IReadOnlyList<string> rowLabels,
        int context = (int)SteamSettingsContext.Title,
        int activePage = 0,
        int focusKey = 0)
    {
        // SteamSettingsCapture fails closed on the audited shape of each context:
        // Title has 4 categories and 3 ordinary descriptors, in-game has 6 and 4.
        var categoryCount = context == (int)SteamSettingsContext.InGame ? 6 : 4;
        var pageCount = context == (int)SteamSettingsContext.InGame ? 4 : 3;

        RowLabels = rowLabels;
        Memory = new SegmentMemory();

        var root = new byte[RootSize];
        WritePointer(root, 0, ImageBase + SteamSettingsCapture.RootVtableRva);
        WriteInt32(root, ContextOffset, context);
        WriteVector(root, DescriptorVectorOffset, Descriptors, pageCount * DescriptorStride);
        WriteVector(root, CategoryVectorOffset, Categories, categoryCount * CategoryStride);
        root[ModeOffset] = (byte)SteamSettingsMode.Page;
        WritePointer(root, CategoryManagerOffset, CategoryManager);
        WritePointer(root, ActiveRootOffset, ActiveRoot);
        WriteVector(root, ActiveManagerVectorOffset, ActiveManagerPointers, 4);
        WriteInt32(root, FocusKeyOffset, focusKey);
        WriteInt32(root, ActivePageOffset, activePage);

        var descriptors = new byte[pageCount * DescriptorStride];
        for (var page = 0; page < pageCount; page++)
        {
            if (page == activePage)
            {
                WriteVector(descriptors, page * DescriptorStride, Rows, rowLabels.Count * RowStride);
            }
            else
            {
                WriteVector(descriptors, page * DescriptorStride, 0, 0);
            }
        }

        var categories = new byte[categoryCount * CategoryStride];
        for (var page = 0; page < categoryCount; page++)
        {
            WriteInlineString(categories, page * CategoryStride, page == 0 ? "Display Settings" : $"Category {page}");
            WriteInlineString(categories, page * CategoryStride + 0x18, $"Category help {page}");
        }

        var rows = new byte[rowLabels.Count * RowStride];
        for (var index = 0; index < rowLabels.Count; index++)
        {
            var offset = index * RowStride;
            WriteInlineString(rows, offset, rowLabels[index]);
            var helpAddress = HelpBase + (nuint)(index * 0x1000);
            var helpBlock = new byte[MsvcStringReader.LayoutSize];
            WriteInlineString(helpBlock, 0, $"{rowLabels[index]} help");
            Memory.Add(helpAddress, helpBlock);
            WriteVector(rows, offset + RowHelpVectorOffset, helpAddress, helpBlock.Length);
            // No value vector: every row is a value-less action row, so the page
            // capture needs no audited selected-value observation.
            WriteVector(rows, offset + RowValueVectorOffset, 0, 0);
        }

        var categoryManager = new byte[ManagerSize];
        WritePointer(categoryManager, 0, ImageBase + SteamSettingsCapture.InputManagerVtableRva);
        WriteInt32(categoryManager, ManagerKeyOffset, activePage);

        var activeManager = new byte[ManagerSize];
        WritePointer(activeManager, 0, ImageBase + SteamSettingsCapture.InputManagerVtableRva);
        WriteInt32(activeManager, ManagerKeyOffset, focusKey);

        var activeManagerPointers = new byte[4];
        WritePointer(activeManagerPointers, 0, ActiveManager);

        var selectorManager = new byte[ManagerSize];
        WritePointer(selectorManager, 0, ImageBase + SteamSettingsCapture.InputManagerVtableRva);
        WriteInt32(selectorManager, ManagerKeyOffset, 0);

        Memory
            .Add(Root, root)
            .Add(Descriptors, descriptors)
            .Add(Categories, categories)
            .Add(Rows, rows)
            .Add(CategoryManager, categoryManager)
            .Add(ActiveManagerPointers, activeManagerPointers)
            .Add(ActiveManager, activeManager)
            .Add(SelectorManager, selectorManager)
            .Add(ActiveRoot, [0, 0, 0, 0]);
    }

    internal SegmentMemory Memory { get; }
    internal IReadOnlyList<string> RowLabels { get; }

    /// <summary>
    /// Moves the selector's live focus, which is the value the native resolution
    /// callback compares its key against before deciding to apply.
    /// </summary>
    internal void SetSelectorFocusKey(int key) =>
        Memory.WriteInt32(SelectorManager + ManagerKeyOffset, key);

    internal nuint RowAddress(int index) => Root == 0 ? 0 : Rows + (nuint)(index * RowStride);

    /// <summary>Interns one UTF-8 string and returns the address a label factory would receive.</summary>
    internal nuint InternLabel(string value)
    {
        var address = LabelBase + (nuint)(nextLabelSlot++ * 0x100);
        var layout = new byte[MsvcStringReader.LayoutSize];
        WriteInlineString(layout, 0, value);
        Memory.Add(address, layout);
        return address;
    }

    private static void WriteInt32(Span<byte> target, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(target[offset..], value);

    private static void WritePointer(Span<byte> target, int offset, nuint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(target[offset..], checked((uint)value));

    private static void WriteVector(Span<byte> target, int offset, nuint address, int byteLength)
    {
        WritePointer(target, offset, address);
        WritePointer(target, offset + 4, address == 0 ? 0 : address + (nuint)byteLength);
        WritePointer(target, offset + 8, address == 0 ? 0 : address + (nuint)byteLength);
    }

    /// <summary>
    /// Writes one MSVC <c>std::string</c> at <paramref name="offset"/>. Values of 15
    /// bytes or fewer use the small-string buffer; longer ones spill to a separate
    /// heap segment and store a pointer, exactly as the game's strings do.
    /// </summary>
    private void WriteInlineString(Span<byte> target, int offset, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        if (encoded.Length < 16)
        {
            encoded.CopyTo(target[offset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(target[(offset + 0x14)..], 15);
        }
        else
        {
            var dataAddress = StringDataBase + (nuint)(nextStringDataSlot++ * 0x200);
            var data = new byte[encoded.Length + 1];
            encoded.CopyTo(data, 0);
            Memory.Add(dataAddress, data);
            BinaryPrimitives.WriteUInt32LittleEndian(target[offset..], checked((uint)dataAddress));
            BinaryPrimitives.WriteUInt32LittleEndian(target[(offset + 0x14)..], (uint)encoded.Length);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(target[(offset + 0x10)..], (uint)encoded.Length);
    }
}

internal sealed class SegmentMemory : IReadableMemory
{
    private readonly List<(nuint Start, byte[] Bytes)> segments = [];

    internal SegmentMemory Add(nuint address, byte[] bytes)
    {
        segments.RemoveAll(segment => segment.Start == address);
        segments.Add((address, bytes));
        return this;
    }

    internal void WriteInt32(nuint address, int value)
    {
        foreach (var (start, bytes) in segments)
        {
            if (address >= start && address + 4 <= start + (nuint)bytes.Length)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(checked((int)(address - start))), value);
                return;
            }
        }
        throw new InvalidOperationException($"No fixture segment covers 0x{address:X}.");
    }

    public bool TryRead(nuint address, Span<byte> destination)
    {
        foreach (var (start, bytes) in segments)
        {
            if (address >= start && address + (nuint)destination.Length <= start + (nuint)bytes.Length)
            {
                bytes.AsSpan(checked((int)(address - start)), destination.Length).CopyTo(destination);
                return true;
            }
        }
        return false;
    }
}

internal sealed class SteamSettingsHarness
{
    private SteamSettingsHarness(
        SteamSettingsHookSet set,
        SteamSettingsFixture fixture,
        RecordingSettingsDispatcher dispatcher,
        RecordingSettingsHookFactory factory,
        UnmanagedBoundaryGuard boundary,
        ReloadedHookInstaller installer)
    {
        Set = set;
        Fixture = fixture;
        Dispatcher = dispatcher;
        Factory = factory;
        Boundary = boundary;
        Installer = installer;
    }

    internal SteamSettingsHookSet Set { get; }
    internal SteamSettingsFixture Fixture { get; }
    internal RecordingSettingsDispatcher Dispatcher { get; }
    internal RecordingSettingsHookFactory Factory { get; }
    internal UnmanagedBoundaryGuard Boundary { get; }
    internal ReloadedHookInstaller Installer { get; }

    /// <summary>
    /// The fallback that publishes the first Settings presentation when no caller
    /// flushes it. Tests set this long enough that a synchronous flush always wins,
    /// or short enough to assert the safety net still fires.
    /// </summary>
    internal static SteamSettingsHarness Create(
        IReadOnlyList<string>? rowLabels = null,
        int context = (int)SteamSettingsContext.Title)
    {
        var fixture = new SteamSettingsFixture(rowLabels ?? ["Screen Mode", "Screen Size", "Brightness"], context);
        var dispatcher = new RecordingSettingsDispatcher();
        var factory = new RecordingSettingsHookFactory();
        var set = new SteamSettingsHookSet(factory, factory, fixture.Memory, dispatcher);
        var boundary = new UnmanagedBoundaryGuard(new SilentLog(), new SilentFatal());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        return new SteamSettingsHarness(set, fixture, dispatcher, factory, boundary, installer);
    }

    internal void PrepareAndActivate()
    {
        Installer.PrepareAll(CreateBuild(Set.RequiredHookIds), Boundary);
        Installer.ActivateAll();
    }

    /// <summary>Runs the audited constructor so the root becomes one live instance.</summary>
    internal void Construct(int context = (int)SteamSettingsContext.Title) =>
        Factory.GetDetour<MenuNodeConfigSteamConstructorDelegate>(HookId.MenuNodeConfigSteamConstructor)(
            (nint)SteamSettingsFixture.Root,
            context);

    /// <summary>
    /// Runs the audited page builder, rendering one audited row label per live row
    /// in the exact ascending 0x88 order the native builder uses.
    /// </summary>
    internal void BuildPage(uint pageIndex = 0)
    {
        Factory.SetOriginal<MenuNodeConfigSteamPageBuilderDelegate>(
            HookId.MenuNodeConfigSteamPageBuilder,
            (_, _) => RenderRowLabels());
        Factory.GetDetour<MenuNodeConfigSteamPageBuilderDelegate>(HookId.MenuNodeConfigSteamPageBuilder)(
            (nint)SteamSettingsFixture.Root,
            pageIndex);
    }

    /// <summary>
    /// Runs the audited resolution selector builder. The heading and each entry are
    /// rendered through their own audited call site, which is how the selector's
    /// labels are attributed; unlike Licenses/Controller/Keyboard, the Resolution
    /// scope does not capture every rendered label generically.
    /// </summary>
    internal void BuildResolutionSelector(string heading, params string[] entries)
    {
        Factory.SetOriginal<SteamSettingsNestedBuilderDelegate>(
            HookId.SteamSettingsResolutionBuilder,
            _ =>
            {
                RenderProbedLabel(HookId.SteamSettingsResolutionHeadingLabelCallSite, heading);
                foreach (var entry in entries)
                {
                    RenderProbedLabel(HookId.SteamSettingsResolutionEntryLabelCallSite, entry);
                }
            });
        Factory.GetDetour<SteamSettingsNestedBuilderDelegate>(HookId.SteamSettingsResolutionBuilder)(
            (nint)SteamSettingsFixture.Root);
    }

    /// <summary>
    /// Runs the audited resolution callback. <paramref name="rebuildsPage"/> replays
    /// the native apply path, which calls the page builder re-entrantly before the
    /// callback returns (ResolutionCallback RVA 0x1FB100 calls RVA 0x1F0310).
    /// </summary>
    internal void ResolutionCallback(int eventType, int key, bool rebuildsPage = false)
    {
        Factory.SetOriginal<SteamSettingsCallbackDelegate>(
            HookId.SteamSettingsResolutionCallback,
            (_, _, _) =>
            {
                if (rebuildsPage)
                {
                    BuildPage();
                }
            });
        Factory.GetDetour<SteamSettingsCallbackDelegate>(HookId.SteamSettingsResolutionCallback)(
            (nint)ResolutionCallbackContext,
            eventType,
            key);
    }

    /// <summary>
    /// The resolution callback context. The native callback reads the manager at
    /// [context + 0] and the settings root at [context + 4].
    /// </summary>
    internal const nuint ResolutionCallbackContext = 0x00C00000;

    internal void InstallResolutionCallbackContext()
    {
        var block = new byte[0x10];
        BinaryPrimitives.WriteUInt32LittleEndian(block, checked((uint)SteamSettingsFixture.SelectorManager));
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), checked((uint)SteamSettingsFixture.Root));
        Fixture.Memory.Add(ResolutionCallbackContext, block);
    }

    private void RenderRowLabels()
    {
        var probe = Factory.GetProbe(HookId.SteamSettingsRowLabelCallSite);
        for (var index = 0; index < Fixture.RowLabels.Count; index++)
        {
            probe();
            var rowAddress = Fixture.RowAddress(index);
            Set.AfterMenuTextLabelFactory(0, (nint)rowAddress, 0, 24, 1);
        }
    }

    private void RenderProbedLabel(HookId probeId, string value)
    {
        Factory.GetProbe(probeId)();
        var address = Fixture.InternLabel(value);
        Set.AfterMenuTextLabelFactory(0, (nint)address, 0, 24, 1);
    }

    private static VerifiedSettingsBuild CreateBuild(IReadOnlyList<HookId> ids) => new(
        SteamSettingsFixture.ImageBase,
        ids.ToDictionary(id => id, id => SteamSettingsFixture.ImageBase + GameVersionCatalog.Get(id).Rva));

    private sealed record VerifiedSettingsBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class SilentLog : IModLog
    {
        public void Info(string message) { }
        public void Error(string message) { }
    }

    private sealed class SilentFatal : IAccessibleFatalError
    {
        public void Show(string message) { }
    }
}

internal sealed class RecordingSettingsDispatcher : ISemanticEventDispatcher
{
    private readonly object gate = new();
    internal ManualResetEventSlim FirstEvent { get; } = new(false);

    public int Generation => 0;
    public List<AccessibilityEvent> Events { get; } = [];
    public List<string> Failures { get; } = [];

    public void Attach(IRuntimePrismSession session) { }
    public void Detach(IRuntimePrismSession session) { }

    public void Publish(AccessibilityEvent accessibilityEvent)
    {
        lock (gate)
        {
            Events.Add(accessibilityEvent);
            FirstEvent.Set();
        }
    }

    public void ReportCoverageFailure(string message)
    {
        lock (gate)
        {
            Failures.Add(message);
        }
    }

    public AccessibilityEvent[] Snapshot()
    {
        lock (gate)
        {
            return Events.ToArray();
        }
    }

    public string[] FailureSnapshot()
    {
        lock (gate)
        {
            return Failures.ToArray();
        }
    }
}

internal sealed class RecordingSettingsHookFactory : IRuntimeNativeHookFactory, IRuntimeNativeAsmHookFactory
{
    // SteamSettingsHookSet binds IHook.OriginalFunction once, at prepare time, so the
    // delegate handed to the hook must be a stable forwarder. Tests swap the native
    // behaviour behind it by replacing the entry in this dictionary.
    private readonly Dictionary<HookId, Delegate> behaviours = [];

    internal Dictionary<HookId, Delegate> FunctionDetours { get; } = [];
    internal Dictionary<HookId, NativeCallSiteProbeDelegate> Probes { get; } = [];
    internal Dictionary<HookId, SteamSettingsRenderedValueProbeDelegate> ValueProbes { get; } = [];
    internal List<(HookId Id, nuint Address)> Created { get; } = [];

    internal RecordingSettingsHookFactory()
    {
        SetOriginal<MenuNodeConfigSteamConstructorDelegate>(
            HookId.MenuNodeConfigSteamConstructor,
            (instance, _) => instance);
        SetOriginal<MenuNodeConfigSteamBuilderDelegate>(HookId.MenuNodeConfigSteamBuilder, _ => { });
        SetOriginal<MenuNodeConfigSteamDestructorDelegate>(HookId.MenuNodeConfigSteamDestructor, _ => { });
        SetOriginal<MenuNodeConfigSteamPageBuilderDelegate>(HookId.MenuNodeConfigSteamPageBuilder, (_, _) => { });
        SetOriginal<SteamSettingsSetterInvokerDelegate>(HookId.SteamSettingsSetterInvoker, (_, _) => { });
        SetOriginal<SteamSettingsLicensePageBuilderDelegate>(HookId.SteamSettingsLicensePageBuilder, (_, _) => { });
        SetOriginal<SteamSettingsRowRefreshDelegate>(HookId.SteamSettingsControllerRowRefresh, (_, _) => { });
        SetOriginal<SteamSettingsRowRefreshDelegate>(HookId.SteamSettingsKeyboardRowRefresh, (_, _) => { });
        foreach (var id in new[]
                 {
                     HookId.SteamSettingsResolutionBuilder,
                     HookId.SteamSettingsConfirmationBuilderA,
                     HookId.SteamSettingsConfirmationBuilderB,
                     HookId.SteamSettingsControllerBuilder,
                     HookId.SteamSettingsKeyboardBuilder,
                 })
        {
            SetOriginal<SteamSettingsNestedBuilderDelegate>(id, _ => { });
        }
        foreach (var id in new[]
                 {
                     HookId.SteamSettingsCategoryCallback,
                     HookId.SteamSettingsRowCallback,
                     HookId.SteamSettingsLicenseCallback,
                     HookId.SteamSettingsResolutionCallback,
                     HookId.SteamSettingsConfirmationCallbackA,
                     HookId.SteamSettingsConfirmationCallbackB,
                     HookId.SteamSettingsControllerCallback,
                     HookId.SteamSettingsKeyboardCallback,
                 })
        {
            SetOriginal<SteamSettingsCallbackDelegate>(id, (_, _, _) => { });
        }
    }

    internal void SetOriginal<TDelegate>(HookId id, TDelegate original)
        where TDelegate : Delegate =>
        behaviours[id] = original;

    internal TDelegate GetDetour<TDelegate>(HookId id)
        where TDelegate : Delegate =>
        (TDelegate)FunctionDetours[id];

    internal NativeCallSiteProbeDelegate GetProbe(HookId id) => Probes[id];

    public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
        where TDelegate : Delegate
    {
        Created.Add((id, address));
        FunctionDetours[id] = detour;
        return new FixedHook<TDelegate>(CreateForwarder<TDelegate>(id));
    }

    private TDelegate CreateForwarder<TDelegate>(HookId id)
        where TDelegate : Delegate
    {
        Delegate forwarder = typeof(TDelegate) switch
        {
            var type when type == typeof(MenuNodeConfigSteamConstructorDelegate) =>
                new MenuNodeConfigSteamConstructorDelegate((instance, context) =>
                    Behaviour<MenuNodeConfigSteamConstructorDelegate>(id) is { } behaviour
                        ? behaviour(instance, context)
                        : instance),
            var type when type == typeof(MenuNodeConfigSteamBuilderDelegate) =>
                new MenuNodeConfigSteamBuilderDelegate(instance =>
                    Behaviour<MenuNodeConfigSteamBuilderDelegate>(id)?.Invoke(instance)),
            var type when type == typeof(MenuNodeConfigSteamDestructorDelegate) =>
                new MenuNodeConfigSteamDestructorDelegate(instance =>
                    Behaviour<MenuNodeConfigSteamDestructorDelegate>(id)?.Invoke(instance)),
            var type when type == typeof(MenuNodeConfigSteamPageBuilderDelegate) =>
                new MenuNodeConfigSteamPageBuilderDelegate((root, page) =>
                    Behaviour<MenuNodeConfigSteamPageBuilderDelegate>(id)?.Invoke(root, page)),
            var type when type == typeof(SteamSettingsNestedBuilderDelegate) =>
                new SteamSettingsNestedBuilderDelegate(root =>
                    Behaviour<SteamSettingsNestedBuilderDelegate>(id)?.Invoke(root)),
            var type when type == typeof(SteamSettingsLicensePageBuilderDelegate) =>
                new SteamSettingsLicensePageBuilderDelegate((root, page) =>
                    Behaviour<SteamSettingsLicensePageBuilderDelegate>(id)?.Invoke(root, page)),
            var type when type == typeof(SteamSettingsCallbackDelegate) =>
                new SteamSettingsCallbackDelegate((context, eventType, key) =>
                    Behaviour<SteamSettingsCallbackDelegate>(id)?.Invoke(context, eventType, key)),
            var type when type == typeof(SteamSettingsSetterInvokerDelegate) =>
                new SteamSettingsSetterInvokerDelegate((setter, index) =>
                    Behaviour<SteamSettingsSetterInvokerDelegate>(id)?.Invoke(setter, index)),
            var type when type == typeof(SteamSettingsRowRefreshDelegate) =>
                new SteamSettingsRowRefreshDelegate((context, row) =>
                    Behaviour<SteamSettingsRowRefreshDelegate>(id)?.Invoke(context, row)),
            _ => throw new InvalidOperationException($"Unsupported fake original {typeof(TDelegate).Name} for {id}."),
        };
        return (TDelegate)forwarder;
    }

    private TDelegate? Behaviour<TDelegate>(HookId id)
        where TDelegate : Delegate =>
        behaviours.TryGetValue(id, out var behaviour) ? (TDelegate)behaviour : null;

    public IPreparedHook CreateAsmHook<TDelegate>(
        HookId id,
        string name,
        TDelegate callback,
        nuint address,
        Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly,
        RuntimeAsmHookOptions options)
        where TDelegate : Delegate
    {
        _ = buildAssembly;
        Assert.Equal(5, options.HookLength);
        Created.Add((id, address));
        if (callback is NativeCallSiteProbeDelegate probe)
        {
            Probes[id] = probe;
        }
        else if (callback is SteamSettingsRenderedValueProbeDelegate valueProbe)
        {
            ValueProbes[id] = valueProbe;
        }
        else
        {
            Assert.Fail($"Unsupported probe delegate {typeof(TDelegate).Name} for {id}.");
        }
        return new FakePreparedHook(name, callback);
    }

    private sealed class FixedHook<TDelegate>(TDelegate original) : IHook<TDelegate>
        where TDelegate : Delegate
    {
        public TDelegate OriginalFunction { get; } = original;
        public IReverseWrapper<TDelegate> ReverseWrapper => null!;
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 1;
        public nint OriginalFunctionWrapperAddress => 1;
        public IHook<TDelegate> Activate()
        {
            IsHookEnabled = true;
            IsHookActivated = true;
            return this;
        }
        IHook IHook.Activate() => Activate();
        public void Disable() => IsHookEnabled = false;
        public void Enable() => IsHookEnabled = true;
    }

    private sealed class FakePreparedHook(string name, object callback) : IPreparedHook
    {
        public string Name { get; } = name;
        public bool IsActive { get; private set; }
        public IReadOnlyCollection<object> LifetimeRoots { get; } = [callback];
        public void Activate() => IsActive = true;
        public void Disable() => IsActive = false;
    }
}
