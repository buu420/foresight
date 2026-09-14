using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.State;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.SaveLoad;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.SaveLoad;

/// <summary>
/// The shipped bug: choosing Resume on the title screen left four seconds of silence
/// (log 2026-09-14 00.32.32, 19:32:39 to 19:32:43) while the native
/// <c>MenuNodeSaveLoadSteam</c> displayed "Resume bookmarked game?" with Yes/No and No
/// preselected. Native evidence is in artifacts/research/resume-confirmation-0314.
/// </summary>
public sealed class SaveLoadConfirmationHookSetTests
{
    private const nuint ImageBase = 0x400000;
    private const nuint Node = 0x02000000;
    private const nuint Manager = 0x02100000;
    private const nuint FirstControl = 0x02200000;
    private const nuint SecondControl = 0x02210000;
    private const nuint FirstState = 0x02300000;
    private const nuint SecondState = 0x02310000;

    private const string ResumePrompt = "Resume bookmarked game?";
    private const string Yes = "Yes";
    private const string No = "No";

    private static readonly HookId[] ExpectedHookIds =
    [
        HookId.SaveLoadConfirmationBuilder,
        HookId.SaveLoadNodeDestructor,
    ];

    [Fact]
    public void PreparesBothAuditedAddressesInactiveThenActivatesThem()
    {
        var harness = Harness.Create();

        harness.Installer.PrepareAll(CreateBuild(), harness.Boundary);

        Assert.Equal(ExpectedHookIds, harness.Set.RequiredHookIds);
        Assert.Equal(ExpectedHookIds, harness.Factory.Created.Select(created => created.Id));
        Assert.Equal(
            new[] { ImageBase + 0x21A1D0, ImageBase + 0x218860 },
            harness.Factory.Created.Select(created => created.Address));
        Assert.All(harness.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));

        harness.Installer.ActivateAll();

        Assert.All(harness.Installer.PreparedHooks, hook => Assert.True(hook.IsActive));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void ResumeConfirmationPublishesTheNativePromptChoicesAndPreselectedNo()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.Equal(new ScreenEntered(ScreenKind.SaveLoad), item),
            item =>
            {
                var confirmation = Assert.IsType<ConfirmationOpened>(item);
                Assert.Equal(ResumePrompt, confirmation.Prompt);
                Assert.Equal(new[] { Yes, No }, confirmation.Choices);
                Assert.Equal(1, confirmation.SelectedIndex);
            });
    }

    /// <summary>
    /// The regression that matters to the player. Without the screen event the semantic state
    /// has no active screen after ScreenExited(TitleMenu) and silently drops the confirmation,
    /// which is exactly the four-second gap the user reported.
    /// </summary>
    [Fact]
    public void ResumeConfirmationIsNarratedAsThePromptThenTheSelectedChoice()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        var state = new AccessibilityState();
        state.Apply(new ScreenEntered(ScreenKind.TitleMenu));
        state.Apply(new ScreenExited(ScreenKind.TitleMenu));

        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);

        var spoken = harness.Dispatcher.Events
            .SelectMany(state.Apply)
            .Select(announcement => announcement.Text)
            .ToArray();
        Assert.Equal(new[] { ResumePrompt, "No, 2 of 2" }, spoken);
    }

    [Fact]
    public void MovingTheNativeFocusToYesRepublishesTheSelectedChoice()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);
        harness.Dispatcher.Events.Clear();

        harness.MoveFocus(0);

        var confirmation = Assert.IsType<ConfirmationOpened>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(ResumePrompt, confirmation.Prompt);
        Assert.Equal(new[] { Yes, No }, confirmation.Choices);
        Assert.Equal(0, confirmation.SelectedIndex);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void FocusOnAnUnrelatedManagerIsIgnored()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);
        harness.Dispatcher.Events.Clear();
        const nuint otherManager = 0x02400000;
        harness.Memory
            .AddPointer(otherManager, ImageBase + SaveLoadConfirmationHookSet.ManagerVtableRva)
            .AddInt32(otherManager + SaveLoadConfirmationHookSet.ManagerFocusKeyOffset, 0);

        harness.Set.AfterFocusSet((nint)otherManager, 0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void FocusAfterTheNativeConfirmationFlagClearsExitsTheScreenInsteadOfSpeaking()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);
        harness.Dispatcher.Events.Clear();
        harness.Memory.AddInt32(Node + SaveLoadConfirmationHookSet.NodeConfirmationActiveOffset, 0);

        harness.MoveFocus(0);

        Assert.Equal(new ScreenExited(ScreenKind.SaveLoad), Assert.Single(harness.Dispatcher.Events));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void DestroyingTheNodeExitsTheScreenExactlyOnce()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);
        harness.Dispatcher.Events.Clear();

        harness.DestroyNode();
        harness.DestroyNode();

        Assert.Equal(new ScreenExited(ScreenKind.SaveLoad), Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(2, harness.NodeDestructions);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void DestroyingAnUnrelatedNodeLeavesTheLiveConfirmationAlone()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);
        harness.Dispatcher.Events.Clear();

        harness.DestroyNode(0x02500000);
        harness.MoveFocus(0);

        var confirmation = Assert.IsType<ConfirmationOpened>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(0, confirmation.SelectedIndex);
    }

    public static TheoryData<int, int, string> AuditedModePrompts => new()
    {
        { SaveLoadConfirmationHookSet.SaveMode, 0x14, "Save data to this file?" },
        { SaveLoadConfirmationHookSet.SaveMode, 0x15, "Overwrite existing data?" },
        { SaveLoadConfirmationHookSet.LoadMode, 0x1B, "Load this file?" },
        { SaveLoadConfirmationHookSet.BookmarkAndQuitMode, 0x26, "Bookmark your progress and exit the game?" },
        { SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt },
    };

    [Theory]
    [MemberData(nameof(AuditedModePrompts))]
    public void EveryAuditedModeSpeaksItsOwnNativePrompt(int mode, int messageId, string prompt)
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.OpenConfirmation(mode, messageId, prompt, focusKey: 1);

        Assert.Empty(harness.Dispatcher.Failures);
        var confirmation = Assert.IsType<ConfirmationOpened>(harness.Dispatcher.Events[^1]);
        Assert.Equal(prompt, confirmation.Prompt);
    }

    [Fact]
    public void APromptTheAuditedModeCannotProduceIsReportedRatherThanSpoken()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        // 0x1B is "Load this file?", which mode 3 never localizes.
        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x1B, "Load this file?", focusKey: 1);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains(
            "did not capture one prompt and localized choice keys 0 and 1",
            Assert.Single(harness.Dispatcher.Failures),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingChoiceIsReportedRatherThanSpoken()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.OpenConfirmation(
            SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1, choiceCount: 1);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains(
            "did not capture one prompt and localized choice keys 0 and 1",
            Assert.Single(harness.Dispatcher.Failures),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnauditedNodeModeIsReportedRatherThanSpoken()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.OpenConfirmation(mode: 9, promptMessageId: 0x2B, prompt: ResumePrompt, focusKey: 1);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains(
            "node mode 9 has no audited prompt",
            Assert.Single(harness.Dispatcher.Failures),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFocusKeyTheNativeManagerDidNotCommitIsReportedRatherThanSpoken()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();

        harness.OpenConfirmation(
            SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1, committedFocusKey: 0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains(
            "focus did not commit to the audited manager state",
            Assert.Single(harness.Dispatcher.Failures),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledHooksStopSpeakingAndForgetTheLiveConfirmation()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);
        harness.Dispatcher.Events.Clear();

        harness.Set.AfterHooksDisabled();
        harness.MoveFocus(0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    /// <summary>
    /// TextManager::getMsg re-enters the Ope resolver at RVA 0x1B9150 with the same file and
    /// message, so routing the confirmation text through the real shared fanout must still
    /// produce exactly one prompt rather than failing the capture closed on a duplicate.
    /// </summary>
    [Fact]
    public void TheNestedOpeResolutionInsideGetMsgIsNotCountedAsASecondPrompt()
    {
        var harness = Harness.Create();
        harness.PrepareAndActivate();
        harness.RouteTextThroughTheSharedFanout();

        harness.OpenConfirmation(SaveLoadConfirmationHookSet.ResumeMode, 0x2B, ResumePrompt, focusKey: 1);

        Assert.Empty(harness.Dispatcher.Failures);
        var confirmation = Assert.IsType<ConfirmationOpened>(harness.Dispatcher.Events[^1]);
        Assert.Equal(ResumePrompt, confirmation.Prompt);
        Assert.Equal(new[] { Yes, No }, confirmation.Choices);
        Assert.Equal(1, confirmation.SelectedIndex);
    }

    /// <summary>The mode-to-prompt map is the whole guard against speaking a guessed label.</summary>
    [Theory]
    [InlineData(SaveLoadConfirmationHookSet.ResumeMode, 0x41, 0x2B, true)]
    [InlineData(SaveLoadConfirmationHookSet.ResumeMode, 0x41, 0x2A, false)]
    [InlineData(SaveLoadConfirmationHookSet.ResumeMode, 0x23, 0x8E, false)]
    [InlineData(SaveLoadConfirmationHookSet.LoadMode, 0x41, 0x1B, true)]
    [InlineData(SaveLoadConfirmationHookSet.BookmarkAndQuitMode, 0x41, 0x26, true)]
    [InlineData(SaveLoadConfirmationHookSet.SaveMode, 0x41, 0x14, true)]
    [InlineData(SaveLoadConfirmationHookSet.SaveMode, 0x41, 0x15, true)]
    [InlineData(SaveLoadConfirmationHookSet.SaveMode, 0x23, 0x8E, true)]
    [InlineData(SaveLoadConfirmationHookSet.SaveOverwriteModeA, 0x41, 0x15, true)]
    [InlineData(SaveLoadConfirmationHookSet.SaveOverwriteModeB, 0x41, 0x14, true)]
    [InlineData(9, 0x41, 0x2B, false)]
    public void PromptExpectationsFollowTheAuditedBuilderSwitch(int mode, int bank, int messageId, bool expected) =>
        Assert.Equal(expected, SaveLoadConfirmationHookSet.IsExpectedPromptMessage(mode, bank, messageId));

    private static VerifiedBuild CreateBuild() => new(
        ImageBase,
        ExpectedHookIds.ToDictionary(id => id, id => ImageBase + GameVersionCatalog.Get(id).Rva));

    private static UnmanagedBoundaryGuard CreateBoundary() =>
        new(new SilentLog(), new SilentFatal());

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class Harness
    {
        private int nextTextAddress;

        private Harness(
            SaveLoadConfirmationHookSet set,
            TestMemory memory,
            RecordingDispatcher dispatcher,
            RecordingHookFactory factory,
            UnmanagedBoundaryGuard boundary,
            ReloadedHookInstaller installer)
        {
            Set = set;
            Memory = memory;
            Dispatcher = dispatcher;
            Factory = factory;
            Boundary = boundary;
            Installer = installer;
        }

        public SaveLoadConfirmationHookSet Set { get; }
        public TestMemory Memory { get; }
        public RecordingDispatcher Dispatcher { get; }
        public RecordingHookFactory Factory { get; }
        public UnmanagedBoundaryGuard Boundary { get; }
        public ReloadedHookInstaller Installer { get; }
        public int NodeDestructions { get; private set; }

        private TextManagerGetMsgDelegate? sharedGetMsg;

        public static Harness Create()
        {
            var memory = new TestMemory();
            memory
                .AddPointer(Node, ImageBase + SaveLoadConfirmationHookSet.SaveLoadNodeVtableRva)
                .AddInt32(Node + SaveLoadConfirmationHookSet.NodeConfirmationActiveOffset, 1)
                .AddPointer(Manager, ImageBase + SaveLoadConfirmationHookSet.ManagerVtableRva)
                .AddPointer(FirstControl, ImageBase + SaveLoadConfirmationHookSet.CustomButtonVtableRva)
                .AddPointer(SecondControl, ImageBase + SaveLoadConfirmationHookSet.CustomButtonVtableRva)
                .AddPointer(FirstState, ImageBase + SaveLoadConfirmationHookSet.FocusableStateVtableRva)
                .AddPointer(
                    FirstState + SaveLoadConfirmationHookSet.FocusableStateControlOffset, FirstControl)
                .AddPointer(SecondState, ImageBase + SaveLoadConfirmationHookSet.FocusableStateVtableRva)
                .AddPointer(
                    SecondState + SaveLoadConfirmationHookSet.FocusableStateControlOffset, SecondControl);
            var dispatcher = new RecordingDispatcher();
            var factory = new RecordingHookFactory();
            var set = new SaveLoadConfirmationHookSet(factory, memory, dispatcher);
            var installer = new ReloadedHookInstaller(set.Registrations, [set]);
            return new Harness(set, memory, dispatcher, factory, CreateBoundary(), installer);
        }

        public void PrepareAndActivate()
        {
            Installer.PrepareAll(CreateBuild(), Boundary);
            Installer.ActivateAll();
        }

        /// <summary>
        /// Replays the audited body of MenuNodeSaveLoadSteam::openConfirm at RVA 0x21A1D0:
        /// the mode-selected prompt, then per choice a CustomButton and its (0x41, 0x11 + i)
        /// label, then the control bindings, then one focus assignment.
        /// </summary>
        public void OpenConfirmation(
            int mode,
            int promptMessageId,
            string prompt,
            int focusKey,
            int choiceCount = 2,
            int? committedFocusKey = null,
            int promptBank = SaveLoadConfirmationHookSet.StartTextFileId)
        {
            Memory.AddInt32(Node + SaveLoadConfirmationHookSet.NodeModeOffset, mode);
            Factory.BuilderBody = (_, _) =>
                {
                    if (promptBank == SaveLoadConfirmationHookSet.StartTextFileId)
                    {
                        Localize(promptBank, promptMessageId, prompt);
                    }
                    else
                    {
                        LocalizeOpe(promptBank, promptMessageId, prompt);
                    }

                    var controls = new[] { FirstControl, SecondControl };
                    var states = new[] { FirstState, SecondState };
                    for (var index = 0; index < choiceCount; index++)
                    {
                        Set.AfterCustomButtonConstructed((nint)controls[index], (nint)controls[index]);
                        Localize(
                            SaveLoadConfirmationHookSet.StartTextFileId,
                            SaveLoadConfirmationHookSet.FirstChoiceMessageId + index,
                            index == 0 ? Yes : No);
                    }

                    for (var index = 0; index < choiceCount; index++)
                    {
                        Set.AfterControlBound((nint)Manager, (nint)states[index], index);
                    }

                    CommitFocus(committedFocusKey ?? focusKey);
                    Set.AfterFocusSet((nint)Manager, focusKey);
                };
            Factory.GetDetour<SaveLoadConfirmationBuilderDelegate>(HookId.SaveLoadConfirmationBuilder)(
                (nint)Node, 0);
        }

        /// <summary>
        /// Rebuilds the production text path: the real shared fanout roots both entry points,
        /// and the getMsg root re-enters the Ope resolver exactly as RVA 0x1B9150 does.
        /// </summary>
        public void RouteTextThroughTheSharedFanout()
        {
            var passthrough = new PassthroughHookFactory();
            var fanout = new SharedNativeHookFanoutFactory(passthrough, [Set], _ => { });
            fanout.CreateHook<OpeTextResolverDelegate>(
                HookId.OpeTextResolver, (_, result, _, _) => result, 0x1B9060);
            var ope = passthrough.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
            fanout.CreateHook<TextManagerGetMsgDelegate>(
                HookId.TextManagerGetMsg,
                (manager, result, fileId, messageId) =>
                {
                    ope(manager, result, fileId, messageId);
                    return result;
                },
                0x1B9110);
            sharedGetMsg = passthrough.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg);
        }

        public void MoveFocus(int key)
        {
            CommitFocus(key);
            Set.AfterFocusSet((nint)Manager, key);
        }

        public void DestroyNode(nuint node = Node)
        {
            NodeDestructions++;
            Factory.GetDetour<SaveLoadNodeDestructorDelegate>(HookId.SaveLoadNodeDestructor)((nint)node);
        }

        private void CommitFocus(int key) =>
            Memory.AddInt32(Manager + SaveLoadConfirmationHookSet.ManagerFocusKeyOffset, key);

        private void Localize(int fileId, int messageId, string text)
        {
            var address = NextText(text);
            if (sharedGetMsg is not null)
            {
                sharedGetMsg(0x1111, (nint)address, fileId, messageId);
                return;
            }

            Set.AfterTextManagerGetMsg(0x1111, (nint)address, fileId, messageId, (nint)address);
        }

        private void LocalizeOpe(int bank, int messageId, string text)
        {
            var address = NextText(text);
            Set.AfterOpeTextResolver(0x2222, (nint)address, bank, messageId, (nint)address);
        }

        private nuint NextText(string text)
        {
            var address = 0x03000000u + (nuint)(nextTextAddress++ * 0x1000);
            Memory.AddString(address, text);
            return address;
        }
    }

    private sealed class RecordingHookFactory : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals;
        private readonly Dictionary<HookId, Delegate> detours = [];

        public RecordingHookFactory() => originals = new Dictionary<HookId, Delegate>
        {
            // The hook set captures OriginalFunction once at preparation, so the stand-in
            // originals stay fixed and forward to a body the test can swap in later.
            [HookId.SaveLoadConfirmationBuilder] =
                (SaveLoadConfirmationBuilderDelegate)((node, slot) => BuilderBody?.Invoke(node, slot)),
            [HookId.SaveLoadNodeDestructor] =
                (SaveLoadNodeDestructorDelegate)(node => DestructorBody?.Invoke(node)),
        };

        public List<(HookId Id, nuint Address)> Created { get; } = [];
        public Action<nint, int>? BuilderBody { get; set; }
        public Action<nint>? DestructorBody { get; set; }

        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate =>
            (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            Created.Add((id, address));
            detours[id] = detour;
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

    /// <summary>Roots shared hooks for the fanout without pretending to own a native original.</summary>
    private sealed class PassthroughHookFactory : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> detours = [];

        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate =>
            (TDelegate)detours[id];

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
