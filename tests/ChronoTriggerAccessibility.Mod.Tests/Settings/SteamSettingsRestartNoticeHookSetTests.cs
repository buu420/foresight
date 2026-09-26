using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Core.State;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Menus;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Settings;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Settings;

/// <summary>
/// After the screen mode or size changes, leaving Steam Settings shows a message and the next
/// key press closes the game (0x1F52C0, callback 0x1FC240 → Director::end). Nothing spoke it.
/// Native evidence: artifacts/research/engine-0332/claude-ui-report.md. The strings are
/// synthetic; the native shape is real: (0x3A, 1) resolved directly, lines separated by an empty
/// line (a doubled 0x5C), one label per split line, then one window control at key 0.
/// </summary>
public sealed class SteamSettingsRestartNoticeHookSetTests
{
    private const nuint ImageBase = 0x400000;
    private const nuint Node = 0x02000000;
    private const nuint OtherNode = 0x02010000;
    private const nuint Backdrop = 0x02020000;
    private const nuint Manager = 0x02100000;
    private const nuint Control = 0x02200000;
    private const nuint SecondControl = 0x02210000;
    private const nuint State = 0x02300000;
    private const nuint SecondState = 0x02310000;
    private const nuint LabelNode = 0x02400000;
    private const nuint UnmappedText = 0x07F00000;

    private const string First = "Sentence one.";
    private const string Second = "Sentence two.";
    private const string Third = "Sentence three.";

    private static readonly string Message = string.Join(NativeTextLines.Separator,
        [First, string.Empty, Second, string.Empty, Third]);

    [Fact]
    public void PreparesTheAuditedAddressInactiveThenActivatesIt()
    {
        var harness = Harness.Create();

        harness.Installer.PrepareAll(CreateBuild(), harness.Boundary);

        Assert.Equal([HookId.SteamSettingsRestartNotice], harness.Set.RequiredHookIds);
        Assert.Equal([(HookId.SteamSettingsRestartNotice, ImageBase + 0x1F52C0)], harness.Factory.Created);
        Assert.All(harness.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));

        harness.Installer.ActivateAll();

        Assert.All(harness.Installer.PreparedHooks, hook => Assert.True(hook.IsActive));
    }

    [Fact]
    public void TheClosingMessageIsSpokenAsItsVisibleLines()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowNotice(Message);

        Assert.Empty(harness.Dispatcher.Failures);
        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(new MenuOwner(SteamSettingsRestartNoticeHookSet.OwnerSource, Node), notice.Owner);
        Assert.Equal([First, Second, Third], notice.Lines!);
        Assert.Equal(1, harness.NoticeCalls);
    }

    [Fact]
    public void ThePlayerHearsEveryLineBeforeTheKeyThatClosesTheGame()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        var state = new AccessibilityState();

        harness.ShowNotice(Message);
        var spoken = state.Apply(Assert.Single(harness.Dispatcher.Events));

        Assert.Equal([First, Second, Third], spoken.Select(item => item.Text));
        Assert.Equal([true, false, false], spoken.Select(item => item.Interrupt));
    }

    [Fact]
    public void TextAndLabelsThroughTheRealSharedFanoutAreCaptured()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.RouteThroughTheSharedFanout();

        harness.ShowNotice(Message);

        Assert.Empty(harness.Dispatcher.Failures);
        var notice = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal([First, Second, Third], notice.Lines!);
    }

    [Fact]
    public void ANoticeOnAnotherClassFailsClosedButStillRunsTheGame()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Memory.AddPointer(OtherNode, ImageBase + 0x3A7030);

        harness.ShowNotice(Message, node: OtherNode);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(1, harness.NoticeCalls);
    }

    [Theory]
    [InlineData(SteamSettingsRestartNoticeHookSet.ResolutionTextBank, 2)]
    [InlineData(0x41, SteamSettingsRestartNoticeHookSet.RestartMessageId)]
    public void TextResolvedFromAnyOtherMessageIsNotTheNotice(int bank, int messageId)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowNotice(Message, bank: bank, messageId: messageId);

        Assert.Contains("did not resolve", Assert.Single(harness.Dispatcher.Failures), StringComparison.Ordinal);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AResolutionNestedInsideGetMsgIsNotTheNotice()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowNotice(Message, nestedInGetMsg: true);

        Assert.Contains("did not resolve", Assert.Single(harness.Dispatcher.Failures), StringComparison.Ordinal);
        Assert.Empty(harness.Dispatcher.Events);
    }

    public static TheoryData<string[]?, int, bool> BrokenRenders => new()
    {
        { [First, Second, Third], SteamSettingsRestartNoticeHookSet.LineFontSize, false },
        { [First, string.Empty, Second, string.Empty], SteamSettingsRestartNoticeHookSet.LineFontSize, false },
        { null, 0x10, false },
        { null, SteamSettingsRestartNoticeHookSet.LineFontSize, true },
    };

    [Theory]
    [MemberData(nameof(BrokenRenders))]
    public void LabelsThatAreNotExactlyTheSplitMessageFailClosed(string[]? rendered, int fontSize, bool unreadable)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowNotice(Message, rendered, fontSize, textAddress: unreadable ? UnmappedText : null);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void AMessageWithNothingVisibleFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowNotice(" " + NativeTextLines.Separator + " ");

        Assert.Contains("no visible text", Assert.Single(harness.Dispatcher.Failures), StringComparison.Ordinal);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(2, 0, 0, true)]
    [InlineData(1, 1, 1, true)]
    [InlineData(1, 0, 1, true)]
    [InlineData(1, 0, 0, false)]
    public void AWindowWithoutExactlyItsOneKeyZeroControlFailsClosed(int controls, int bindKey, int focusKey, bool focus)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowNotice(Message, controls: controls, bindKey: bindKey, focusKey: focusKey, focus: focus);

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void ALabelDrawnAfterTheWindowControlFailsClosed()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowNotice(Message, afterControl: () => harness.Label(Third));

        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void SharedObservationsOutsideTheNoticeBelongToSomeoneElse()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.Label(First);
        harness.Resolve(SteamSettingsRestartNoticeHookSet.ResolutionTextBank,
            SteamSettingsRestartNoticeHookSet.RestartMessageId, Message);
        harness.Set.AfterCustomButtonConstructed((nint)Control, (nint)Control);
        harness.Set.AfterControlBound((nint)Manager, (nint)State, 0);
        harness.Set.AfterFocusSet((nint)Manager, 0);

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void LosingTheHookLifecycleMidBuildPublishesNothing()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.ShowNotice(Message, duringBuild: harness.Set.AfterHooksDisabled);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal(1, harness.NoticeCalls);
    }

    [Fact]
    public void AfterHooksAreDisabledTheNoticeOnlyRunsTheGame()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.Set.AfterHooksDisabled();

        harness.ShowNotice(Message);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal(1, harness.NoticeCalls);
    }

    private static VerifiedBuild CreateBuild() => new(
        ImageBase,
        new Dictionary<HookId, nuint>
        {
            [HookId.SteamSettingsRestartNotice] =
                ImageBase + GameVersionCatalog.Get(HookId.SteamSettingsRestartNotice).Rva,
        });

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class Harness
    {
        private int nextTextAddress;
        private MenuTextLabelFactoryDelegate? sharedLabelFactory;
        private OpeTextResolverDelegate? sharedResolver;
        private TextManagerGetMsgDelegate? sharedGetMsg;

        private Harness(
            SteamSettingsRestartNoticeHookSet set,
            TestMemory memory,
            RecordingDispatcher dispatcher,
            RecordingHookFactory factory,
            ReloadedHookInstaller installer)
        {
            Set = set;
            Memory = memory;
            Dispatcher = dispatcher;
            Factory = factory;
            Installer = installer;
        }

        public SteamSettingsRestartNoticeHookSet Set { get; }
        public TestMemory Memory { get; }
        public RecordingDispatcher Dispatcher { get; }
        public RecordingHookFactory Factory { get; }
        public ReloadedHookInstaller Installer { get; }
        public UnmanagedBoundaryGuard Boundary { get; } = new(new SilentLog(), new SilentFatal());
        public int NoticeCalls { get; private set; }

        public static Harness Create()
        {
            var memory = new TestMemory();
            memory
                .AddPointer(Node, ImageBase + SteamSettingsRestartNoticeHookSet.NodeVtableRva)
                .AddPointer(Manager, ImageBase + SteamSettingsRestartNoticeHookSet.ManagerVtableRva)
                .AddPointer(Control, ImageBase + SteamSettingsRestartNoticeHookSet.CustomButtonVtableRva)
                .AddPointer(SecondControl, ImageBase + SteamSettingsRestartNoticeHookSet.CustomButtonVtableRva)
                .AddPointer(State, ImageBase + SteamSettingsRestartNoticeHookSet.FocusableStateVtableRva)
                .AddPointer(State + SteamSettingsRestartNoticeHookSet.FocusableStateControlOffset, Control)
                .AddPointer(SecondState, ImageBase + SteamSettingsRestartNoticeHookSet.FocusableStateVtableRva)
                .AddPointer(SecondState + SteamSettingsRestartNoticeHookSet.FocusableStateControlOffset, SecondControl);
            var dispatcher = new RecordingDispatcher();
            var factory = new RecordingHookFactory();
            var set = new SteamSettingsRestartNoticeHookSet(factory, memory, dispatcher);
            return new Harness(set, memory, dispatcher, factory, new ReloadedHookInstaller(set.Registrations, [set]));
        }

        public void PrepareAndActivate()
        {
            Installer.PrepareAll(CreateBuild(), Boundary);
            Installer.ActivateAll();
        }

        /// <summary>Roots the resolver, getMsg and label factory exactly as production composes them.</summary>
        public void RouteThroughTheSharedFanout()
        {
            var passthrough = new PassthroughHookFactory();
            var fanout = new SharedNativeHookFanoutFactory(passthrough, [Set], _ => { });
            fanout.CreateHook<OpeTextResolverDelegate>(HookId.OpeTextResolver, (_, result, _, _) => result, 0x1B9060);
            sharedResolver = passthrough.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
            var resolver = sharedResolver;
            fanout.CreateHook<TextManagerGetMsgDelegate>(
                HookId.TextManagerGetMsg,
                (manager, result, fileId, messageId) =>
                {
                    resolver(manager, result, fileId, messageId);
                    return result;
                },
                0x1B9110);
            sharedGetMsg = passthrough.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg);
            fanout.CreateHook<MenuTextLabelFactoryDelegate>(
                HookId.MenuTextLabelFactory, (_, _, _, _) => (nint)LabelNode, 0x2400B0);
            sharedLabelFactory = passthrough.GetDetour<MenuTextLabelFactoryDelegate>(HookId.MenuTextLabelFactory);
        }

        public nuint AddText(string text)
        {
            var address = 0x03000000u + (nuint)(nextTextAddress++ * 0x1000);
            Memory.AddString(address, text);
            return address;
        }

        public void Label(string text, int fontSize = SteamSettingsRestartNoticeHookSet.LineFontSize)
        {
            var address = AddText(text);
            if (sharedLabelFactory is not null)
            {
                sharedLabelFactory(0x1234, (nint)address, 0x5678, fontSize);
            }
            else
            {
                Set.AfterMenuTextLabelFactory(0x1234, (nint)address, 0x5678, fontSize, (nint)LabelNode);
            }
        }

        /// <summary>One direct call of the raw resolver 0x1B9060, as 0x1F5403 makes it.</summary>
        public void Resolve(int bank, int messageId, string text, nuint? address = null)
        {
            var result = address ?? AddText(text);
            if (sharedResolver is not null)
            {
                sharedResolver(0x1111, (nint)result, bank, messageId);
            }
            else
            {
                Set.AfterOpeTextResolver(0x1111, (nint)result, bank, messageId, (nint)result);
            }
        }

        /// <summary>
        /// Replays 0x1F52C0: the backdrop joins +0x290, (0x3A, 1) is resolved directly
        /// (0x1F5403), 0x2B06B0 draws one font-0x0C label per split line (0x1F5410), then the shared
        /// window 0x23D520 constructs, binds and focuses its one control at key 0 (0x1F5455).
        /// </summary>
        public void ShowNotice(
            string text,
            IReadOnlyList<string>? rendered = null,
            int fontSize = SteamSettingsRestartNoticeHookSet.LineFontSize,
            nuint node = Node,
            nuint? textAddress = null,
            int bank = SteamSettingsRestartNoticeHookSet.ResolutionTextBank,
            int messageId = SteamSettingsRestartNoticeHookSet.RestartMessageId,
            bool nestedInGetMsg = false,
            int controls = 1,
            int bindKey = 0,
            int focusKey = 0,
            bool focus = true,
            Action? duringBuild = null,
            Action? afterControl = null)
        {
            if (nestedInGetMsg && sharedGetMsg is null)
            {
                RouteThroughTheSharedFanout();
            }

            Factory.NoticeBody = (_, _) =>
            {
                NoticeCalls++;
                if (nestedInGetMsg)
                {
                    sharedGetMsg!(0x1111, (nint)AddText(text), bank, messageId);
                }
                else
                {
                    Resolve(bank, messageId, text, textAddress);
                }

                foreach (var line in rendered ?? NativeTextLines.Split(text))
                {
                    Label(line, fontSize);
                }

                duringBuild?.Invoke();
                var constructed = new[] { Control, SecondControl };
                var states = new[] { State, SecondState };
                for (var index = 0; index < controls; index++)
                {
                    Set.AfterCustomButtonConstructed((nint)constructed[index], (nint)constructed[index]);
                }

                afterControl?.Invoke();
                for (var index = 0; index < Math.Max(controls, 1); index++)
                {
                    Set.AfterControlBound((nint)Manager, (nint)states[index], bindKey + index);
                }

                if (focus)
                {
                    Memory.AddInt32(Manager + SteamSettingsRestartNoticeHookSet.ManagerFocusKeyOffset, focusKey);
                    Set.AfterFocusSet((nint)Manager, focusKey);
                }
            };
            Factory.GetDetour<SteamSettingsRestartNoticeDelegate>(HookId.SteamSettingsRestartNotice)(
                (nint)node, (nint)Backdrop);
        }
    }

    private sealed class RecordingHookFactory : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> detours = [];

        public List<(HookId Id, nuint Address)> Created { get; } = [];
        public Action<nint, nint>? NoticeBody { get; set; }

        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate => (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            Created.Add((id, address));
            detours[id] = detour;
            // The set captures OriginalFunction once, so the stand-in forwards to a swappable body.
            Delegate original = (SteamSettingsRestartNoticeDelegate)((node, parent) => NoticeBody?.Invoke(node, parent));
            return new FakeHook<TDelegate>((TDelegate)original);
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

    /// <summary>Roots shared hooks for the fanout without pretending to own a native original.</summary>
    private sealed class PassthroughHookFactory : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> detours = [];

        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate => (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            _ = address;
            detours[id] = detour;
            return new FakeHook<TDelegate>(detour);
        }
    }

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public TestMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            segments[address] = bytes;
            return this;
        }

        public TestMemory AddPointer(nuint address, nuint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value));
            segments[address] = bytes;
            return this;
        }

        public TestMemory AddString(nuint address, string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            var layout = new byte[MsvcStringReader.LayoutSize];
            if (encoded.Length < 16)
            {
                encoded.CopyTo(layout, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 15);
            }
            else
            {
                var dataAddress = address + 0x40;
                BinaryPrimitives.WriteUInt32LittleEndian(layout, checked((uint)dataAddress));
                BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), (uint)encoded.Length);
                segments[dataAddress] = encoded;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            segments[address] = layout;
            return this;
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
