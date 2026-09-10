using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.NewGame;
using ChronoTriggerAccessibility.Core.Startup;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.NewGame;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.NewGame;

public sealed class NewGameHookSetTests
{
    private const nuint ImageBase = 0x400000;
    private const nuint ControlScene = 0x90000;
    private const nuint ControlManager = 0x91000;
    private const nuint ModeScene = 0x92000;
    private const nuint ModeRecords = 0x93000;
    private const nuint NameScene = 0xC0000;
    private const nuint NameManager = 0xC1000;
    private const nuint NameState0 = 0xC2000;
    private const nuint NameState1 = 0xC2100;
    private const nuint NameState2 = 0xC2200;
    private const nuint NameState3 = 0xC2300;
    private const nuint NameControl0 = 0xC3000;
    private const nuint NameControl1 = 0xC3100;
    private const nuint NameControl2 = 0xC3200;
    private const nuint NameControl3 = 0xC3300;
    private const nuint ConfirmationManager = 0xC4000;
    private const nuint ConfirmationControl0 = 0xC5000;
    private const nuint ConfirmationControl1 = 0xC5100;
    private const nuint ConfirmationState0 = 0xC6000;
    private const nuint ConfirmationState1 = 0xC6100;

    private static readonly HookId[] ExpectedDedicatedHooks =
    [
        HookId.OpeTextResolver,
        HookId.OpeManualSceneInit,
        HookId.ModeSelectSteamInit,
        HookId.ModeSelectCallback,
        HookId.NameInputSceneInit,
        HookId.NameInputSceneUpdate,
        HookId.NameConfirmationBuilder,
        HookId.NsMenuCustomButtonConstructor,
        HookId.NsMenuControlBinder,
        HookId.ControlNextCallback,
        HookId.NameActionCallback,
        HookId.NameDirectEntryActivation,
        HookId.NameDirectEntryClose,
        HookId.NameGridRefresh,
    ];

    [Fact]
    public void ProductionSetRegistersOnlyDedicatedHooksAndRootsThreeExactGetterWrappers()
    {
        var factory = new RecordingHookFactory();
        var wrappers = new RecordingWrapperFactory();
        var set = new NewGameHookSet(factory, wrappers, new TestMemory(), new RecordingDispatcher());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);

        installer.PrepareAll(CreateBuild(), CreateBoundary());

        Assert.Equal(ExpectedDedicatedHooks, set.RequiredHookIds);
        Assert.Equal(ExpectedDedicatedHooks, factory.Created.Select(item => item.Id));
        Assert.DoesNotContain(factory.Created, item => item.Id == HookId.TextManagerGetMsg);
        Assert.DoesNotContain(factory.Created, item => item.Id == HookId.NsMenuFocusSetter);
        Assert.Equal(
            new nuint[] { ImageBase + 0x1EC320, ImageBase + 0x1EBEB0, ImageBase + 0x2AC370 },
            wrappers.Created);
        Assert.All(installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.True(installer.LifetimeRootCount >= ExpectedDedicatedHooks.Length * 2);

        installer.ActivateAll();
        Assert.All(installer.PreparedHooks, hook => Assert.True(hook.IsActive));
    }

    [Fact]
    public void CombinedProductionCompositionPreparesExactlyTwentyTwoHooksWithSingleSharedRegistrations()
    {
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        var wrappers = new RecordingWrapperFactory();
        var memory = CreateNameMemory().AddInt32(
                ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva,
                StartupTitleHookSet.SquareEnixSceneId);
        var dispatcher = new RecordingDispatcher();
        ConfigureNameInit(factory);
        var textOriginalCalls = 0;
        var focusOriginalCalls = 0;
        var updateOriginalCalls = 0;
        factory.SetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (_, result, _, _) =>
            {
                textOriginalCalls++;
                return result;
            });
        factory.SetOriginal<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter,
            (_, _) => focusOriginalCalls++);
        factory.SetOriginal<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate,
            (_, _) => updateOriginalCalls++);

        var composition = ChronoTriggerAccessibility.Mod.Mod.CreateAccessibilityComposition(
            factory,
            wrappers,
            memory,
            dispatcher,
            new OpeningMovieTimeline());
        var allIds = new[]
        {
            HookId.TextManagerGetMsg, HookId.SceneManagerCreate, HookId.SceneManagerNextScene,
            HookId.TitleMenuModeEnter, HookId.TitleRowFactory, HookId.TitleSceneUpdate,
            HookId.NsMenuFocusSetter, HookId.TitleMenuCallback,
        }.Concat(ExpectedDedicatedHooks).ToArray();
        var build = new VerifiedBuild(ImageBase, allIds.ToDictionary(
            id => id, id => ImageBase + GameVersionCatalog.Get(id).Rva));

        composition.Installer.PrepareAll(build, CreateBoundary());

        Assert.Equal(22, composition.Installer.PreparedHooks.Count);
        Assert.Equal(22, factory.Created.Count);
        Assert.Single(factory.Created, item => item.Id == HookId.TextManagerGetMsg);
        Assert.Single(factory.Created, item => item.Id == HookId.NsMenuFocusSetter);
        Assert.Equal(22, factory.Created.Select(item => item.Id).Distinct().Count());

        composition.Installer.ActivateAll();
        Assert.All(composition.Installer.PreparedHooks, hook => Assert.True(hook.IsActive));
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        Assert.Contains(Flatten(dispatcher.Events), item => item is NameEntryPresented);
        Assert.Empty(dispatcher.Failures);

        dispatcher.Events.Clear();
        composition.Installer.DisableAll();
        Assert.All(composition.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        memory.AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 1);
        factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
            0x100, (nint)NameTextAddress(0), 0x42, 0x08);
        factory.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)(
            (nint)NameManager, 1);
        factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate)(
            (nint)NameScene, 0.016f);

        Assert.Equal(1, textOriginalCalls);
        Assert.Equal(1, focusOriginalCalls);
        Assert.Equal(1, updateOriginalCalls);
        Assert.Empty(dispatcher.Events);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void ControlNextCopiesBothPointerArgumentsAndPublishesBeforeSynchronousNextScreenConstruction()
    {
        var memory = CreateControlMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var sequence = new List<string>();
        var nextCalls = 0;
        nint forwardedEvent = 0;
        nint forwardedValue = 0;
        factory.SetOriginal<ControlNextCallbackDelegate>(HookId.ControlNextCallback,
            (_, eventPointer, valuePointer) =>
            {
                nextCalls++;
                forwardedEvent = eventPointer;
                forwardedValue = valuePointer;
                sequence.Add("original");
                dispatcher.Publish(new ModeSelectPresented(
                    [
                        new("Battle", "Active", "Battle help"),
                        new("Graphics", "Original", "Graphics help"),
                        new("Interface", "Gamepad", "Interface help"),
                    ],
                    "Start",
                    0));
                memory.Remove((nuint)eventPointer).Remove((nuint)valuePointer);
            });

        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        ConfigureControlInit(factory, set, memory);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)((nint)ControlScene);
        dispatcher.AfterPublish = item =>
        {
            if (item is ControlDescriptionNextActivated)
            {
                sequence.Add("next");
            }
            else if (item is ModeSelectPresented)
            {
                sequence.Add("mode");
            }
        };
        const nuint eventPointer = 0xA0000;
        const nuint valuePointer = 0xA0010;
        memory.AddInt32(eventPointer, 0);

        factory.GetDetour<ControlNextCallbackDelegate>(HookId.ControlNextCallback)(
            0xB0000, (nint)eventPointer, (nint)valuePointer);

        Assert.Equal(1, nextCalls);
        Assert.Equal((nint)eventPointer, forwardedEvent);
        Assert.Equal((nint)valuePointer, forwardedValue);
        Assert.Equal(["next", "original", "mode"], sequence);
        Assert.Contains(dispatcher.Events, item => item == new ControlDescriptionNextActivated("Next"));
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void ControlNextDoesNotDereferenceItsAbiPreservedSecondPointer()
    {
        var memory = CreateControlMemory().AddInt32(0xA0000, 2);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        factory.SetOriginal<ControlNextCallbackDelegate>(HookId.ControlNextCallback,
            (_, eventPointer, valuePointer) =>
            {
                calls++;
                Assert.Equal((nint)0xA0000, eventPointer);
                Assert.Equal(0, valuePointer);
            });
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        ConfigureControlInit(factory, set, memory);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)((nint)ControlScene);
        dispatcher.Events.Clear();

        factory.GetDetour<ControlNextCallbackDelegate>(HookId.ControlNextCallback)(
            0xB0000, 0xA0000, 0);

        Assert.Equal(1, calls);
        Assert.Equal(
            new ControlDescriptionNextActivated("Next"),
            Assert.IsType<ControlDescriptionNextActivated>(Assert.Single(dispatcher.Events)));
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void ControlNextMirrorsClosureStateGateAndRetainsActiveScreenWhenNativeDoesNotTransition()
    {
        var memory = CreateControlMemory()
            .AddInt32(0xA0000, 0)
            .AddInt32(0xB0000 + NewGameHookSet.ControlNextClosureStateOffset, 2);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        factory.SetOriginal<ControlNextCallbackDelegate>(HookId.ControlNextCallback,
            (_, _, _) => calls++);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        ConfigureControlInit(factory, set, memory);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)((nint)ControlScene);
        dispatcher.Events.Clear();
        var next = factory.GetDetour<ControlNextCallbackDelegate>(HookId.ControlNextCallback);

        next(0xB0000, 0xA0000, 0);

        Assert.Empty(dispatcher.Events);
        Assert.Empty(dispatcher.Failures);
        memory.AddInt32(0xB0000 + NewGameHookSet.ControlNextClosureStateOffset, 1);
        next(0xB0000, 0xA0000, 0);
        Assert.IsType<ControlDescriptionNextActivated>(Assert.Single(dispatcher.Events));
        Assert.Equal(2, calls);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void UnreadableControlNextClosureGateFailsOnceButStillForwardsOriginal()
    {
        var memory = CreateControlMemory().AddInt32(0xA0000, 2);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        factory.SetOriginal<ControlNextCallbackDelegate>(HookId.ControlNextCallback,
            (_, _, _) => calls++);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        ConfigureControlInit(factory, set, memory);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)((nint)ControlScene);
        dispatcher.Events.Clear();

        factory.GetDetour<ControlNextCallbackDelegate>(HookId.ControlNextCallback)(
            0xDEAD0, 0xA0000, 0);

        Assert.Equal(1, calls);
        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("+0x8", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedModeVectorFailsBeforeAnyNativeGetterWrapperInvocation()
    {
        var memory = CreateModeMemory()
            .AddPointer(
                ModeScene + ModeSelectCapture.RecordsEndOffset,
                ModeRecords + 2 * ModeSelectCapture.RecordStride);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var wrappers = new RecordingWrapperFactory();
        var originalCalls = 0;
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, scene =>
        {
            originalCalls++;
            EmitModeLocalizedText(factory);
            return 1;
        });
        var set = new NewGameHookSet(factory, wrappers, memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        var result = factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
            (nint)ModeScene);

        Assert.Equal(1, result);
        Assert.Equal(1, originalCalls);
        Assert.Empty(wrappers.InvokedTargets);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("three", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(dispatcher.Events, item => item is ModeSelectPresented);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ModeInitPublishesAllThreeRowsAndChangeRecapturesAuthoritativePostOriginalState(
        bool useTextManager, bool postprocessText)
    {
        var memory = CreateModeMemory()
            .AddInt32(ModeScene + ModeSelectCapture.CompositeFocusOffset, 11)
            .AddPointer(0xB8000, 0xB8100)
            .AddPointer(0xB8000 + NewGameHookSet.ModeCallbackSceneOffset, ModeScene)
            .AddInt32(0xB8100 + NewGameHookSet.ManagerFocusKeyOffset, 11);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        var wrappers = new RecordingWrapperFactory { Values = [1, 0, 1] };
        var initCalls = 0;
        var callbackCalls = 0;
        var textCalls = 0;
        factory.SetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (manager, result, bank, messageId) =>
            {
                textCalls++;
                var index = Array.IndexOf(EnumerateModeTextKeys().ToArray(),
                    new LocalizedMessageKey(bank, messageId));
                Assert.True(index >= 0);
                var rawAddress = (nuint)result + 0x10000;
                memory.AddInlineString(rawAddress, postprocessText ? $"key,Mode {index}" : $"Mode {index}");
                factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver)(
                    manager, (nint)rawAddress, bank, messageId);
                return result;
            });
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
        {
            initCalls++;
            EmitModeLocalizedText(factory, useTextManager);
            return 1;
        });
        factory.SetOriginal<ModeSelectCallbackDelegate>(HookId.ModeSelectCallback, (_, eventType, value) =>
        {
            callbackCalls++;
            Assert.Equal(3, eventType);
            Assert.Equal(4, value);
            wrappers.Values[1] = 1;
            memory.AddInt32(ModeScene + ModeSelectCapture.CompositeFocusOffset, 20);
        });
        ActivateCombinedHooks(factory, wrappers, memory, dispatcher);

        Assert.Equal(1, factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
            (nint)ModeScene));

        Assert.Empty(dispatcher.Failures);
        var presented = Assert.IsType<ModeSelectPresented>(Assert.Single(dispatcher.Events));
        Assert.Equal(3, presented.Rows.Count);
        Assert.Equal(11, presented.CompositeFocus);
        Assert.Equal("Mode 15", presented.StartLabel);
        Assert.Equal(new[] { "Mode 0", "Mode 5", "Mode 10" }, presented.Rows.Select(row => row.Label));
        Assert.Equal(new[] { "Mode 4", "Mode 8", "Mode 14" }, presented.Rows.Select(row => row.Help));
        Assert.Equal("Mode 2", presented.Rows[0].Value);
        Assert.Equal("Mode 6", presented.Rows[1].Value);
        Assert.Equal("Mode 12", presented.Rows[2].Value);

        dispatcher.Events.Clear();
        factory.GetDetour<ModeSelectCallbackDelegate>(HookId.ModeSelectCallback)(0xB8000, 3, 4);

        var changed = Assert.IsType<ModeSelectChanged>(Assert.Single(dispatcher.Events));
        Assert.Equal(20, changed.CompositeFocus);
        Assert.Equal("Mode 7", changed.Rows[1].Value);
        Assert.Equal(1, initCalls);
        Assert.Equal(1, callbackCalls);
        Assert.Equal(useTextManager ? 11 : 0, textCalls);
        Assert.Equal(6, wrappers.InvokedTargets.Count);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void SeparateDuplicateModeTextRequestsStillFailBeforePublishingOrCallingGetters()
    {
        var memory = CreateModeMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var wrappers = new RecordingWrapperFactory();
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
        {
            EmitModeLocalizedText(factory);
            factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver)(
                0x100, (nint)ModeTextAddress(1), 0x3F, 5);
            return 1;
        });
        var set = new NewGameHookSet(factory, wrappers, memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        Assert.Equal(1, factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
            (nint)ModeScene));

        Assert.Contains("(3F,5) was captured more than once", Assert.Single(dispatcher.Failures));
        Assert.Empty(dispatcher.Events);
        Assert.Empty(wrappers.InvokedTargets);
    }

    [Theory]
    [InlineData(0, "Battle Mode: ACTIVE. Time flows constantly while in battle. Left control, 1 of 4")]
    [InlineData(10, "Graphics: Original. Play with graphics similar to the original pixel art. Left control, 2 of 4")]
    [InlineData(20, "Interface: Gamepad/Keyboard. Easy gamepad/keyboard controls. Left control, 3 of 4")]
    [InlineData(30, "Start Game, 4 of 4")]
    public void ModeSelectNarratesNativeValueAndHelpWithoutTheConstructionPlaceholder(
        int focus, string expected)
    {
        // Exact-build requests and visible English results, independent of TextContracts.
        // The constructor's Equipment text is replaced by current-row help before presentation.
        (int Bank, int Id, string Text)[] requests =
        [
            (0x23, 0x5A, "Battle Mode"),
            (0x23, 0xC0, "Time flows constantly while in battle."),
            (0x23, 0xC1, "Time freezes while you select techs or items."),
            (0x3F, 0x05, "ACTIVE"),
            (0x3F, 0x06, "WAIT"),
            (0x3F, 0x31, "Graphics"),
            (0x23, 0xC7, "Play with graphics supported by high-resolution displays."),
            (0x23, 0xC6, "Play with graphics similar to the original pixel art."),
            (0x3F, 0x32, "High Resolution"),
            (0x3F, 0x33, "Original"),
            (0x42, 0x1A, "Interface"),
            (0x41, 0x55, "Easy touch pad/mouse controls."),
            (0x41, 0x54, "Easy gamepad/keyboard controls."),
            (0x42, 0x1C, "Touch Pad/Mouse"),
            (0x42, 0x1D, "Gamepad/Keyboard"),
            (0x23, 0xD7, "Start Game"),
            (0x23, 0x20, "Equipment"),
        ];
        var memory = CreateModeMemory().AddInt32(ModeScene + ModeSelectCapture.CompositeFocusOffset, focus);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        var wrappers = new RecordingWrapperFactory { Values = [0, 1, 1] };
        factory.SetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (manager, result, bank, messageId) =>
            {
                factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver)(
                    manager, result, bank, messageId);
                return result;
            });
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
        {
            for (var index = 0; index < requests.Length; index++)
            {
                var request = requests[index];
                var address = 0xE0000u + (nuint)(index * 0x100);
                memory.AddHeapString(address, address + 0x10000, request.Text);
                if (request.Bank != 0x23 || request.Id == 0xD7)
                {
                    factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
                        0x100, (nint)address, request.Bank, request.Id);
                }
                else
                {
                    factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver)(
                        0x100, (nint)address, request.Bank, request.Id);
                }
            }
            return 1;
        });
        ActivateCombinedHooks(factory, wrappers, memory, dispatcher);

        Assert.Equal(1, factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
            (nint)ModeScene));

        Assert.Empty(dispatcher.Failures);
        var presented = Assert.IsType<ModeSelectPresented>(Assert.Single(dispatcher.Events));
        var announcements = new NewGameNarrator().Apply(presented);
        Assert.Equal(new[] { "New Game settings.", expected }, announcements.Select(item => item.Text));
    }

    [Fact]
    public void DirectModeTextCaptureResumesAfterTextManagerThrows()
    {
        var memory = CreateModeMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        var shared = new SharedNativeHookFanoutFactory(factory, dispatcher.Failures.Add);
        var set = new NewGameHookSet(shared, new RecordingWrapperFactory(), memory, dispatcher);
        shared.ConfigureObservers([set]);
        var failure = new InvalidOperationException("simulated text failure");
        shared.CreateHook<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (_, _, _, _) => throw failure, ImageBase + GameVersionCatalog.Get(HookId.TextManagerGetMsg).Rva);
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(0x100, 0, 0, 0)));
            EmitModeLocalizedText(factory);
            return 1;
        });
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        Assert.Equal(1, factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
            (nint)ModeScene));

        Assert.Empty(dispatcher.Failures);
        Assert.Equal(3, Assert.IsType<ModeSelectPresented>(Assert.Single(dispatcher.Events)).Rows.Count);
    }

    [Fact]
    public void TextManagerOnAnotherThreadDoesNotSuppressDirectModeTextCapture()
    {
        var memory = CreateModeMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        var shared = new SharedNativeHookFanoutFactory(factory, dispatcher.Failures.Add);
        var set = new NewGameHookSet(shared, new RecordingWrapperFactory(), memory, dispatcher);
        shared.ConfigureObservers([set]);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        shared.CreateHook<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (_, result, _, _) =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)), "Text worker was not released.");
                return result;
            }, ImageBase + GameVersionCatalog.Get(HookId.TextManagerGetMsg).Rva);
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
        {
            EmitModeLocalizedText(factory);
            return 1;
        });
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        Exception? workerFailure = null;
        var worker = new Thread(() => workerFailure = Record.Exception(() =>
            factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(0x100, 0, 0, 0)))
        {
            IsBackground = true,
        };
        worker.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "Text worker did not enter the native call.");
            Assert.Equal(1, factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
                (nint)ModeScene));
        }
        finally
        {
            release.Set();
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "Text worker did not finish.");
        }

        Assert.Null(workerFailure);
        Assert.Empty(dispatcher.Failures);
        Assert.Equal(3, Assert.IsType<ModeSelectPresented>(Assert.Single(dispatcher.Events)).Rows.Count);
    }

    [Fact]
    public void ModeStartAndCancelPublishBeforeTheirSynchronousDestinationConstruction()
    {
        var memory = CreateModeMemory()
            .AddPointer(0xB8000, 0xB8100)
            .AddPointer(0xB8000 + NewGameHookSet.ModeCallbackSceneOffset, ModeScene)
            .AddInt32(0xB8100 + NewGameHookSet.ManagerFocusKeyOffset, 30);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var wrappers = new RecordingWrapperFactory();
        var sequence = new List<string>();
        var callbackCalls = 0;
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
        {
            EmitModeLocalizedText(factory);
            return 1;
        });
        factory.SetOriginal<ModeSelectCallbackDelegate>(HookId.ModeSelectCallback, (_, eventType, _) =>
        {
            callbackCalls++;
            if (eventType == 0)
            {
                sequence.Add("start original");
                dispatcher.Publish(new NameEntryPresented("Enter", "Crono", "Shown only"));
            }
            else
            {
                Assert.Equal(2, eventType);
                sequence.Add("cancel original");
                dispatcher.Publish(new ScreenEntered(ScreenKind.TitleMenu));
            }
        });
        dispatcher.AfterPublish = item =>
        {
            if (item is ModeSelectActivated)
            {
                sequence.Add("start");
            }
            else if (item is NameEntryPresented)
            {
                sequence.Add("name");
            }
            else if (item is ModeSelectCancelled)
            {
                sequence.Add("cancel");
            }
            else if (item is ScreenEntered { Screen: ScreenKind.TitleMenu })
            {
                sequence.Add("title");
            }
        };
        var set = new NewGameHookSet(factory, wrappers, memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        var init = factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit);
        var callback = factory.GetDetour<ModeSelectCallbackDelegate>(HookId.ModeSelectCallback);

        init((nint)ModeScene);
        callback(0xB8000, 0, 30);
        Assert.Equal(["start", "start original", "name"], sequence);

        sequence.Clear();
        dispatcher.Events.Clear();
        init((nint)ModeScene);
        callback(0xB8000, 2, 0);
        Assert.Equal(["cancel", "cancel original", "title"], sequence);
        Assert.Equal(2, callbackCalls);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void ModeCallbackCommonTailRecapturesUnknownEventsAndInvalidDirectionsWithoutFalseFailure()
    {
        var memory = CreateModeMemory()
            .AddPointer(0xB8000, 0xB8100)
            .AddPointer(0xB8000 + NewGameHookSet.ModeCallbackSceneOffset, ModeScene)
            .AddInt32(0xB8100 + NewGameHookSet.ManagerFocusKeyOffset, 0);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
        {
            EmitModeLocalizedText(factory);
            return 1;
        });
        factory.SetOriginal<ModeSelectCallbackDelegate>(HookId.ModeSelectCallback,
            (_, _, _) => calls++);
        var set = new NewGameHookSet(
            factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)((nint)ModeScene);
        var presented = Assert.IsType<ModeSelectPresented>(Assert.Single(dispatcher.Events));
        dispatcher.Events.Clear();
        var callback = factory.GetDetour<ModeSelectCallbackDelegate>(HookId.ModeSelectCallback);

        callback(0xDEAD0, 99, int.MinValue);
        callback(0xB8000, 3, 99);

        Assert.Equal(2, calls);
        var changes = dispatcher.Events.Select(Assert.IsType<ModeSelectChanged>).ToArray();
        Assert.Equal(2, changes.Length);
        var narrator = new NewGameNarrator();
        narrator.Apply(presented);
        Assert.All(changes, changed => Assert.Empty(narrator.Apply(changed)));
        Assert.Empty(dispatcher.Failures);
    }

    [Theory]
    [InlineData("target", 0)]
    [InlineData("vtable", 0)]
    [InlineData("method", 0)]
    [InlineData("return", 1)]
    [InlineData("throw", 1)]
    public void CorruptModeGetterStateFailsAtomicallyAndStillReturnsNativeInitResult(
        string corruption,
        int expectedInvocations)
    {
        var memory = CreateModeMemory();
        var target = ModeGetterTarget(0);
        var vtable = ModeGetterVtable(0);
        var wrappers = new RecordingWrapperFactory();
        switch (corruption)
        {
            case "target":
                memory.AddPointer(
                    ModeRecords + ModeSelectCapture.CurrentValueGetterTargetOffset,
                    0);
                break;
            case "vtable":
                memory.AddPointer(target, 0);
                break;
            case "method":
                memory.AddPointer(vtable + 8, ImageBase + 0x1EC330);
                break;
            case "return":
                wrappers.Values[0] = 2;
                break;
            case "throw":
                wrappers.ThrowOnRow = 0;
                break;
        }
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var originalCalls = 0;
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
        {
            originalCalls++;
            EmitModeLocalizedText(factory);
            return 1;
        });
        var set = new NewGameHookSet(factory, wrappers, memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        var result = factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
            (nint)ModeScene);

        Assert.Equal(1, result);
        Assert.Equal(1, originalCalls);
        Assert.Equal(expectedInvocations, wrappers.InvokedTargets.Count);
        Assert.Single(dispatcher.Failures);
        Assert.DoesNotContain(dispatcher.Events, item => item is ModeSelectPresented);
    }

    [Fact]
    public void NameInitCapturesTheNativeFourControlsAndNarratesInitialNameFieldAndGridButton()
    {
        var memory = CreateNameMemory(active: 0, page: -1)
            .AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 2);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        // Native builder: localized buttons 0/1, the name-field overlay 2,
        // and the image button 3; every state is bound before focus is set to 2.
        factory.SetOriginal<NameInputSceneInitDelegate>(HookId.NameInputSceneInit, _ =>
        {
            var text = factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg);
            text(0x100, (nint)NameTextAddress(1), 0x41, 0x35);
            text(0x100, (nint)NameTextAddress(2), 0x41, 0x08);
            var bind = factory.GetDetour<NsMenuControlBinderDelegate>(HookId.NsMenuControlBinder);
            bind((nint)NameManager, (nint)NameState0, 0);
            bind((nint)NameManager, (nint)NameState1, 1);
            bind((nint)NameManager, (nint)NameState2, 2);
            bind((nint)NameManager, 0xC2300, 3);
            factory.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)((nint)NameManager, 2);
            return 1;
        });
        ActivateCombinedHooks(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var result = factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        Assert.Equal(1, result);
        Assert.Empty(dispatcher.Failures);
        var narrator = new NewGameNarrator();
        var initial = dispatcher.Events.Cast<NewGameAccessibilityEvent>()
            .SelectMany(narrator.Apply).Select(item => item.Text).ToArray();
        Assert.Contains(initial, text => text.Contains("Current name: Crono.", StringComparison.Ordinal));
        Assert.Contains("Name text field, 3 of 4", initial);
        dispatcher.Events.Clear();

        memory.AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 3);
        factory.GetDetour<NsMenuFocusSetterDelegate>(HookId.NsMenuFocusSetter)((nint)NameManager, 3);
        var gridFocus = Assert.IsType<NameActionFocused>(Assert.Single(dispatcher.Events));
        Assert.Equal(new NameActionFocused("Character grid", 3, 4), gridFocus);
        Assert.Equal("Character grid, 4 of 4", Assert.Single(narrator.Apply(gridFocus)).Text);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void NameInitUsesNativeInactiveGridAndDeferredLabels()
    {
        // The constructor creates only Defaults and Accept. The character-grid
        // refresh runs later, after action 3 opens it, via the real _Do_call thunks.
        var memory = CreateNameMemory(active: 0, page: -1, row: -1, column: -1)
            .AddPointer(0xE1000 + 8, ImageBase + 0x2C5D90)
            .AddPointer(0xE1100 + 8, ImageBase + 0x2C5D20)
            .AddPointer(0xE1200 + 8, ImageBase + 0x2C5CE0);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory, includeGridAction: false);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        var result = factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)(
            (nint)NameScene);
        factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate)(
            (nint)NameScene, 0.016f);

        Assert.Equal(1, result);
        Assert.Empty(dispatcher.Failures);
        Assert.Collection(Flatten(dispatcher.Events),
            item => Assert.IsType<NameEntryPresented>(item),
            item => Assert.Equal(new NameActionFocused("Defaults", 0, 4), item));
    }

    [Fact]
    public void NameGridRefreshCapturesLaterAcceptThroughSharedTextAndReplacesItOnRedraw()
    {
        const nuint closure = 0xE0204;
        const nuint actionClosure = 0xF0000;
        var memory = CreateNameMemory(active: 0, page: -1)
            .AddPointer(closure, 0xF1000)
            .AddPointer(closure + 4, NameScene)
            .AddPointer(actionClosure + NewGameHookSet.NameCallbackSceneOffset, NameScene)
            .AddPointer(ImageBase + NameInputCapture.GridPointerTableRva + 87 * 4, 0xE2000)
            .AddCString(0xE2000, string.Empty)
            .AddInlineString(NameTextAddress(0), "Accept");
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        ConfigureNameInit(factory, includeGridAction: false);
        var refreshCalls = 0;
        var wrapperCalls = 0;
        factory.SetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (manager, result, bank, id) =>
            {
                wrapperCalls++;
                factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver)(
                    manager, result, bank, id);
                return result;
            });
        factory.SetOriginal<NameGridRefreshDelegate>(HookId.NameGridRefresh, actualClosure =>
        {
            Assert.Equal((nint)closure, actualClosure);
            refreshCalls++;
            factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
                0x100, (nint)NameTextAddress(0), 0x42, 0x08);
        });
        factory.SetOriginal<NameActionCallbackDelegate>(HookId.NameActionCallback, (_, _, _) =>
        {
            memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 1)
                .AddInt32(NameScene + NameInputCapture.PageOffset, 0);
            factory.GetDetour<NameGridRefreshDelegate>(HookId.NameGridRefresh)((nint)closure);
            memory.AddInt32(NameScene + NameInputCapture.RowOffset, 7)
                .AddInt32(NameScene + NameInputCapture.ColumnOffset, 10);
        });
        ActivateCombinedHooks(factory, new RecordingWrapperFactory(), memory, dispatcher);
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback)(
            (nint)actionClosure, 0, 3);

        Assert.Collection(Flatten(dispatcher.Events),
            item => Assert.Equal(new NameGridVisibilityChanged(true), item),
            item => Assert.Equal(new NameGridFocused("Accept", "Latin", 7, 10), item));
        dispatcher.Events.Clear();
        memory.AddInlineString(NameTextAddress(0), "Accepter");
        factory.GetDetour<NameGridRefreshDelegate>(HookId.NameGridRefresh)((nint)closure);
        // Copies must survive the native string's destruction or reuse.
        memory.AddInlineString(NameTextAddress(0), "overwritten");
        factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate)((nint)NameScene, 0.016f);
        Assert.Equal(new NameGridFocused("Accepter", "Latin", 7, 10), Assert.Single(Flatten(dispatcher.Events)));
        Assert.Equal(2, refreshCalls);
        Assert.Equal(2, wrapperCalls);
        Assert.Empty(dispatcher.Failures);
    }

    [Theory]
    [InlineData("missing label")]
    [InlineData("wrong target")]
    [InlineData("duplicate label")]
    public void NameGridRefreshPreservesNativeCallAndRejectsIncompleteOwnershipOrText(string fault)
    {
        const nuint closure = 0xE0204;
        var memory = CreateNameMemory(active: 0, page: -1)
            .AddPointer(closure + 4, NameScene);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory, includeGridAction: false);
        var nativeCalls = 0;
        factory.SetOriginal<NameGridRefreshDelegate>(HookId.NameGridRefresh, _ =>
        {
            nativeCalls++;
            if (fault == "duplicate label")
            {
                var resolver = factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
                resolver(0x100, (nint)NameTextAddress(0), 0x42, 0x08);
                resolver(0x100, (nint)NameTextAddress(0), 0x42, 0x08);
            }
        });
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        if (fault == "wrong target")
        {
            memory.AddPointer(NameScene + NameInputCapture.RefreshTargetOffset, 0xFFFF0);
        }

        factory.GetDetour<NameGridRefreshDelegate>(HookId.NameGridRefresh)((nint)closure);

        Assert.Equal(1, nativeCalls);
        Assert.Single(dispatcher.Failures);
        Assert.Empty(dispatcher.Events);
    }

    [Fact]
    public void NameGridRefreshDuringInitRestoresParentCaptureAndIgnoresForeignScenes()
    {
        const nuint closure = 0xE0204;
        var memory = CreateNameMemory(active: 0, page: -1)
            .AddPointer(closure + 4, NameScene)
            .AddPointer(ImageBase + NameInputCapture.GridPointerTableRva + 87 * 4, 0xE2000)
            .AddCString(0xE2000, string.Empty)
            .AddInlineString(NameTextAddress(0), "Accept");
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        var calls = 0;
        factory.SetOriginal<NameGridRefreshDelegate>(HookId.NameGridRefresh, _ =>
        {
            calls++;
            factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
                0x100, (nint)NameTextAddress(0), 0x42, 0x08);
        });
        ConfigureNameInit(factory, includeGridAction: false, onOriginal: () =>
            factory.GetDetour<NameGridRefreshDelegate>(HookId.NameGridRefresh)((nint)closure));
        ActivateCombinedHooks(factory, new RecordingWrapperFactory(), memory, dispatcher);
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        Assert.Contains(Flatten(dispatcher.Events), item => item is NameEntryPresented);
        Assert.Contains(Flatten(dispatcher.Events), item => item is NameActionFocused { Label: "Defaults" });
        dispatcher.Events.Clear();

        memory.AddPointer(closure + 4, 0xFFFF0).AddInlineString(NameTextAddress(0), "foreign");
        factory.GetDetour<NameGridRefreshDelegate>(HookId.NameGridRefresh)((nint)closure);
        memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 1)
            .AddInt32(NameScene + NameInputCapture.PageOffset, 0)
            .AddInt32(NameScene + NameInputCapture.RowOffset, 7)
            .AddInt32(NameScene + NameInputCapture.ColumnOffset, 10);
        factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate)((nint)NameScene, 0.016f);

        Assert.Equal(2, calls);
        Assert.Contains(Flatten(dispatcher.Events), item => item is NameGridFocused { Label: "Accept" });
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void NameInitResolvesFocusableStateControlAtPlus14AndPublishesGridFocus()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var binderArguments = new List<(nint Manager, nint State, int Key)>();
        factory.SetOriginal<NsMenuControlBinderDelegate>(HookId.NsMenuControlBinder,
            (manager, state, key) => binderArguments.Add((manager, state, key)));
        ConfigureNameInit(factory);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        var result = factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)(
            (nint)NameScene);

        Assert.Equal(1, result);
        Assert.Equal(
            new[]
            {
                ((nint)NameManager, (nint)NameState0, 0),
                ((nint)NameManager, (nint)NameState1, 1),
                ((nint)NameManager, (nint)NameState2, 2),
                ((nint)NameManager, (nint)NameState3, 3),
            },
            binderArguments);
        Assert.NotEqual(NameState0, NameControl0);
        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.IsType<NameEntryPresented>(item),
            item => Assert.Equal(
                new NameGridFocused("A", "Latin", 0, 0),
                Assert.IsType<NameGridFocused>(item)));
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void NameActionEventOneIgnoresActionIdAndProducesNoNarration()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var calls = new List<(nint Closure, int EventType, int ActionId)>();
        factory.SetOriginal<NameActionCallbackDelegate>(HookId.NameActionCallback,
            (closure, eventType, actionId) => calls.Add((closure, eventType, actionId)));
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback)(
            0xDEAD0, 1, int.MaxValue);

        Assert.Equal([((nint)0xDEAD0, 1, int.MaxValue)], calls);
        Assert.Empty(dispatcher.Events);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void NameActionMirrorsNativeSilentDefaultsOutsideTheSwitchTables()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var calls = new List<(int EventType, int ActionId)>();
        factory.SetOriginal<NameActionCallbackDelegate>(HookId.NameActionCallback,
            (_, eventType, actionId) => calls.Add((eventType, actionId)));
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var action = factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback);

        action(0xDEAD0, 99, 0);
        action(0xDEAD0, 0, 4);

        Assert.Equal([(99, 0), (0, 4)], calls);
        Assert.Empty(dispatcher.Events);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void NameActionsPublishDefaultsGridToggleAndEmptyAcceptFromPostOriginalState()
    {
        var memory = CreateNameMemory(name: "Lucca")
            .AddPointer(0xF0000 + NewGameHookSet.NameCallbackSceneOffset, NameScene);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var calls = 0;
        factory.SetOriginal<NameActionCallbackDelegate>(HookId.NameActionCallback,
            (_, eventType, actionId) =>
            {
                calls++;
                Assert.Equal(0, eventType);
                if (actionId == 0)
                {
                    memory.AddInlineString(NameScene + NameInputCapture.NameOffset, "Crono");
                }
                else if (actionId == 3)
                {
                    memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 0);
                }
            });
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var action = factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback);

        action(0xF0000, 0, 0);
        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.Equal(new NameActionActivated("Defaults"), item),
            item => Assert.Equal(new NewGameNameChanged("Crono"), item));

        dispatcher.Events.Clear();
        action(0xF0000, 0, 3);
        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.Equal(new NameGridVisibilityChanged(false), item),
            item => Assert.Equal(new NameActionFocused("Defaults", 0, 4), item));

        dispatcher.Events.Clear();
        memory.AddInlineString(NameScene + NameInputCapture.NameOffset, string.Empty);
        action(0xF0000, 0, 1);
        Assert.IsType<EmptyNameRejected>(Assert.Single(dispatcher.Events));
        Assert.Equal(3, calls);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void NameUpdatePublishesPostOriginalNameAndCellChangesAndDedupesUnchangedFrames()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var calls = new List<(nint Scene, float Delta)>();
        factory.SetOriginal<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate,
            (scene, delta) =>
            {
                calls.Add((scene, delta));
                if (calls.Count == 1)
                {
                    memory
                        .AddInlineString(NameScene + NameInputCapture.NameOffset, "Crona")
                        .AddInt32(NameScene + NameInputCapture.ColumnOffset, 1)
                        .AddPointer(
                            ImageBase + NameInputCapture.GridPointerTableRva + 4,
                            0xE2100)
                        .AddCString(0xE2100, "B");
                }
            });
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var update = factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate);

        update((nint)NameScene, 0.25f);

        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.Equal(new NewGameNameChanged("Crona"), item),
            item => Assert.Equal(new NameGridFocused("B", "Latin", 0, 1), item));
        dispatcher.Events.Clear();

        update((nint)NameScene, 0.5f);

        Assert.Empty(dispatcher.Events);
        Assert.Equal(
            new[] { ((nint)NameScene, 0.25f), ((nint)NameScene, 0.5f) },
            calls);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void KeyboardNameEntryOpensAfterDelayedNativeActivationNarratesTypingAndCloses()
    {
        const nuint closure = 0xF0000;
        const nuint nameEdit = 0xF1000;
        const nuint directTarget = NameManager;
        const nuint delayedCapture = 0xF3000;
        const nuint closeCapture = 0xF4000;
        const nuint closeVector = 0xF5000;
        var memory = CreateNameMemory(name: "Crono")
            .AddPointer(closure + NewGameHookSet.NameCallbackOwnerOffset, nameEdit)
            .AddPointer(closure + NewGameHookSet.NameCallbackSceneOffset, NameScene)
            .AddPointer(closure + NewGameHookSet.NameCallbackDirectTargetOffset, directTarget)
            .AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 0)
            .AddByte(directTarget + NewGameHookSet.DirectEntryTargetStateOffset, 0)
            .AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 2)
            .AddPointer(delayedCapture, directTarget)
            .AddPointer(delayedCapture + 4, nameEdit);
        AddDirectOpenCapture(memory, delayedCapture, 0xF6000, directTarget, nameEdit);
        AddDirectCloseCapture(memory, closeCapture, closeVector, directTarget);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var actionCalls = new List<(nint Closure, int EventType, int ActionId)>();
        factory.SetOriginal<NameActionCallbackDelegate>(HookId.NameActionCallback,
            (nativeClosure, eventType, actionId) =>
            {
                actionCalls.Add((nativeClosure, eventType, actionId));
                if (eventType == 0 && actionId == 2)
                {
                    memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 0);
                }
            });
        var delayedCalls = new List<nint>();
        factory.SetOriginal<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation,
            capture =>
            {
                delayedCalls.Add(capture);
                memory
                    .AddByte(directTarget + NewGameHookSet.DirectEntryTargetStateOffset, 1)
                    .AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 1);
            });
        var closeCalls = new List<nint>();
        factory.SetOriginal<NameDirectEntryCloseDelegate>(
            HookId.NameDirectEntryClose,
            capture =>
            {
                closeCalls.Add(capture);
                memory.AddByte(
                    directTarget + NewGameHookSet.DirectEntryTargetStateOffset, 0);
            });
        factory.SetOriginal<NameInputSceneUpdateDelegate>(
            HookId.NameInputSceneUpdate,
            (_, _) => memory.AddInlineString(
                NameScene + NameInputCapture.NameOffset, "Lucca"));
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var action = factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback);
        var delayed = factory.GetDetour<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation);
        var close = factory.GetDetour<NameDirectEntryCloseDelegate>(
            HookId.NameDirectEntryClose);

        action((nint)closure, 0, 2);
        Assert.Empty(dispatcher.Events);
        Assert.Empty(dispatcher.Failures);

        delayed((nint)delayedCapture);
        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.Equal(new NameGridVisibilityChanged(false), item),
            item => Assert.Equal(new KeyboardNameEntryFocused("Crono"), item));

        dispatcher.Events.Clear();
        factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate)(
            (nint)NameScene, 0.016f);
        Assert.Equal(
            new NewGameNameChanged("Lucca"),
            Assert.Single(Flatten(dispatcher.Events)));

        dispatcher.Events.Clear();
        memory.AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 0);
        close((nint)closeCapture);
        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.Equal(new KeyboardNameEntryClosed("Lucca"), item),
            item => Assert.Equal(new NameActionFocused("Name text field", 2, 4), item));

        Assert.Equal([((nint)closure, 0, 2)], actionCalls);
        Assert.Equal([(nint)delayedCapture], delayedCalls);
        Assert.Equal([(nint)closeCapture], closeCalls);
        Assert.Empty(dispatcher.Failures);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("controls")]
    [InlineData("grid-button-is-name-field")]
    public void InvalidDelayedNameEntryCaptureStillCallsNativeOriginalOnceAndFailsCoverage(
        string corruption)
    {
        const nuint closure = 0xF0000;
        const nuint nameEdit = 0xF1000;
        const nuint directTarget = NameManager;
        const nuint delayedCapture = 0xF3000;
        var memory = CreateNameMemory()
            .AddPointer(closure + NewGameHookSet.NameCallbackOwnerOffset, nameEdit)
            .AddPointer(closure + NewGameHookSet.NameCallbackSceneOffset, NameScene)
            .AddPointer(closure + NewGameHookSet.NameCallbackDirectTargetOffset, directTarget)
            .AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 0)
            .AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 2)
            .AddPointer(delayedCapture, directTarget + 4)
            .AddPointer(delayedCapture + 4, nameEdit);
        AddDirectOpenCapture(memory, delayedCapture, 0xF6000, directTarget, nameEdit);
        if (corruption == "target")
        {
            memory.AddPointer(delayedCapture, directTarget + 4);
        }
        else if (corruption == "grid-button-is-name-field")
        {
            memory.AddPointer(delayedCapture + 0x14, NameControl2);
        }
        else
        {
            memory.AddPointer(delayedCapture + 0x14, 0xBAD00);
        }
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        factory.SetOriginal<NameActionCallbackDelegate>(HookId.NameActionCallback,
            (_, _, _) => memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 0));
        var delayedCalls = 0;
        factory.SetOriginal<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation,
            _ => delayedCalls++);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback)(
            (nint)closure, 0, 2);
        factory.GetDetour<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation)((nint)delayedCapture);

        Assert.Equal(1, delayedCalls);
        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("match", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InactiveGridDirectEntryDoesNotInventACharacterGridClose()
    {
        const nuint closure = 0xF0000;
        const nuint nameEdit = 0xF1000;
        const nuint delayedCapture = 0xF3000;
        var memory = CreateNameMemory(active: 0, row: 99, column: 99)
            .AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 2)
            .AddPointer(closure + NewGameHookSet.NameCallbackOwnerOffset, nameEdit)
            .AddPointer(closure + NewGameHookSet.NameCallbackSceneOffset, NameScene)
            .AddPointer(closure + NewGameHookSet.NameCallbackDirectTargetOffset, NameManager)
            .AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 0)
            .AddByte(NameManager + NewGameHookSet.DirectEntryTargetStateOffset, 0)
            .AddPointer(delayedCapture, NameManager)
            .AddPointer(delayedCapture + 4, nameEdit);
        AddDirectOpenCapture(memory, delayedCapture, 0xF6000, NameManager, nameEdit);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        factory.SetOriginal<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation,
            _ => memory
                .AddByte(NameManager + NewGameHookSet.DirectEntryTargetStateOffset, 1)
                .AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 1));
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback)(
            (nint)closure, 0, 2);
        factory.GetDetour<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation)((nint)delayedCapture);

        Assert.Equal(
            new KeyboardNameEntryFocused("Crono"),
            Assert.Single(Flatten(dispatcher.Events)));
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void ActivationBodyWithoutPendingActionStillCallsNativeOriginalAndFailsLoudly()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var calls = new List<nint>();
        factory.SetOriginal<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation,
            capture => calls.Add(capture));
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        factory.GetDetour<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation)(0xDEAD0);

        Assert.Equal([(nint)0xDEAD0], calls);
        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("without an exact pending", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenignCloseBodyWithoutActiveImeRemainsNativeSilent()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var calls = new List<nint>();
        factory.SetOriginal<NameDirectEntryCloseDelegate>(
            HookId.NameDirectEntryClose,
            capture => calls.Add(capture));
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        factory.GetDetour<NameDirectEntryCloseDelegate>(
            HookId.NameDirectEntryClose)(0xDEAD0);

        Assert.Equal([(nint)0xDEAD0], calls);
        Assert.Empty(dispatcher.Events);
        Assert.Empty(dispatcher.Failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptCloseControlPayloadStillCallsNativeOriginalAndPublishesNoPartialBatch(
        bool gridButtonIsNameField)
    {
        const nuint closure = 0xF0000;
        const nuint nameEdit = 0xF1000;
        const nuint delayedCapture = 0xF3000;
        const nuint closeCapture = 0xF4000;
        const nuint closeVector = 0xF5000;
        var memory = CreateNameMemory()
            .AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 2)
            .AddPointer(closure + NewGameHookSet.NameCallbackOwnerOffset, nameEdit)
            .AddPointer(closure + NewGameHookSet.NameCallbackSceneOffset, NameScene)
            .AddPointer(closure + NewGameHookSet.NameCallbackDirectTargetOffset, NameManager)
            .AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 0)
            .AddByte(NameManager + NewGameHookSet.DirectEntryTargetStateOffset, 0)
            .AddPointer(delayedCapture, NameManager)
            .AddPointer(delayedCapture + 4, nameEdit);
        AddDirectOpenCapture(memory, delayedCapture, 0xF6000, NameManager, nameEdit);
        AddDirectCloseCapture(memory, closeCapture, closeVector, NameManager);
        memory.AddPointer(closeCapture + 0x10, gridButtonIsNameField ? NameControl2 : 0xBAD00);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        factory.SetOriginal<NameActionCallbackDelegate>(HookId.NameActionCallback,
            (_, _, _) => memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 0));
        factory.SetOriginal<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation,
            _ => memory
                .AddByte(NameManager + NewGameHookSet.DirectEntryTargetStateOffset, 1)
                .AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 1));
        var closeCalls = 0;
        factory.SetOriginal<NameDirectEntryCloseDelegate>(
            HookId.NameDirectEntryClose,
            _ =>
            {
                closeCalls++;
                memory.AddByte(NameManager + NewGameHookSet.DirectEntryTargetStateOffset, 0);
            });
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback)(
            (nint)closure, 0, 2);
        factory.GetDetour<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation)((nint)delayedCapture);
        dispatcher.Events.Clear();
        memory.AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 0);

        factory.GetDetour<NameDirectEntryCloseDelegate>(
            HookId.NameDirectEntryClose)((nint)closeCapture);

        Assert.Equal(1, closeCalls);
        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("exact two-element", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShutdownCancelsPendingNameEntrySpeechButDelayedHookStillCallsNativeOriginal()
    {
        const nuint closure = 0xF0000;
        const nuint nameEdit = 0xF1000;
        const nuint directTarget = NameManager;
        const nuint delayedCapture = 0xF3000;
        var memory = CreateNameMemory()
            .AddPointer(closure + NewGameHookSet.NameCallbackOwnerOffset, nameEdit)
            .AddPointer(closure + NewGameHookSet.NameCallbackSceneOffset, NameScene)
            .AddPointer(closure + NewGameHookSet.NameCallbackDirectTargetOffset, directTarget)
            .AddByte(nameEdit + NewGameHookSet.NameKeyboardStateOffset, 0)
            .AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 2)
            .AddPointer(delayedCapture, directTarget)
            .AddPointer(delayedCapture + 4, nameEdit);
        AddDirectOpenCapture(memory, delayedCapture, 0xF6000, directTarget, nameEdit);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        factory.SetOriginal<NameActionCallbackDelegate>(HookId.NameActionCallback,
            (_, _, _) => memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 0));
        var delayedCalls = 0;
        factory.SetOriginal<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation,
            _ => delayedCalls++);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        factory.GetDetour<NameActionCallbackDelegate>(HookId.NameActionCallback)(
            (nint)closure, 0, 2);
        installer.DisableAll();
        factory.GetDetour<NameDirectEntryActivationDelegate>(
            HookId.NameDirectEntryActivation)((nint)delayedCapture);

        Assert.Equal(1, delayedCalls);
        Assert.Empty(dispatcher.Events);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void InactiveInitialNameGridUsesAuthoritativeMainActionManagerFocus()
    {
        var memory = CreateNameMemory(active: 0, row: 99, column: 99)
            .AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 1);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);

        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.IsType<NameEntryPresented>(item),
            item => Assert.Equal(new NameActionFocused("Accept", 1, 4), item));
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void NameGridCloseAndReopenRepublishesTheSameCellAfterValidatedActionFocus()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var updateCalls = 0;
        factory.SetOriginal<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate,
            (_, _) => updateCalls++);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var update = factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate);

        memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 0);
        update((nint)NameScene, 0.016f);

        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.Equal(new NameGridVisibilityChanged(false), item),
            item => Assert.Equal(new NameActionFocused("Defaults", 0, 4), item));

        dispatcher.Events.Clear();
        memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 1);
        update((nint)NameScene, 0.016f);

        Assert.Collection(
            Flatten(dispatcher.Events),
            item => Assert.Equal(new NameGridVisibilityChanged(true), item),
            item => Assert.Equal(new NameGridFocused("A", "Latin", 0, 0), item));
        Assert.Equal(2, updateCalls);
        Assert.Empty(dispatcher.Failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NameGridCloseFocusFailurePublishesNoPartialSemanticBatch(bool unreadable)
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        memory.AddByte(NameScene + NameInputCapture.ActiveOffset, 0);
        if (unreadable)
        {
            memory.Remove(NameManager + NewGameHookSet.ManagerFocusKeyOffset);
        }
        else
        {
            memory.AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 9);
        }

        factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate)(
            (nint)NameScene, 0.016f);

        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("grid closed", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedFocusRequiresRequestedKeyToMatchAuthoritativeManagerState()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        set.AfterFocusSet((nint)NameManager, 1);

        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("authoritative", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NameInitRejectsExtraManagerBindingsBeforePublishing(int extraCount)
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory, extraBindingCount: extraCount);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);

        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("exact distinct keys", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmationForwardsSixWordNameAndCorrelatesConstructedControlsThroughBinderStates()
    {
        var memory = CreateNameMemory();
        AddConfirmationMemory(memory);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var expectedWords = EncodeInlineName("Crono");
        var forwarded = new List<uint[]>();
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        ConfigureConfirmationBuilder(factory, set, forwarded, extraBinding: false);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();

        factory.GetDetour<NameConfirmationBuilderDelegate>(HookId.NameConfirmationBuilder)(
            (nint)NameScene,
            expectedWords[0], expectedWords[1], expectedWords[2], expectedWords[3],
            expectedWords[4], expectedWords[5]);

        Assert.Single(forwarded);
        Assert.Equal(expectedWords, forwarded[0]);
        var confirmation = Assert.IsType<NameConfirmationPresented>(Assert.Single(dispatcher.Events));
        Assert.Equal("Use Crono?", confirmation.Prompt);
        Assert.Equal(new[] { "Yes", "No" }, confirmation.Choices);
        Assert.Equal(1, confirmation.SelectedIndex);
        Assert.Empty(dispatcher.Failures);

        dispatcher.Events.Clear();
        memory.AddInt32(ConfirmationManager + NewGameHookSet.ManagerFocusKeyOffset, 0);
        set.AfterFocusSet((nint)ConfirmationManager, 0);
        Assert.Equal(
            new NameConfirmationFocused("Yes", 0, 2),
            Assert.IsType<NameConfirmationFocused>(Assert.Single(dispatcher.Events)));

        dispatcher.Events.Clear();
        set.AfterFocusSet((nint)ConfirmationManager, 1);
        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("authoritative", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmationCorrelatesEachChoiceOnceAfterTextManagerProcessesNestedResolverText()
    {
        var memory = CreateNameMemory();
        AddConfirmationMemory(memory);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        ConfigureNameInit(factory);
        var textCalls = 0;
        factory.SetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (manager, result, bank, messageId) =>
            {
                textCalls++;
                var raw = messageId == 0x11 ? "key,Yes" : "key,No";
                var rawAddress = (nuint)result + 0x10000;
                memory.AddInlineString(rawAddress, raw);
                factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver)(
                    manager, (nint)rawAddress, bank, messageId);
                return result;
            });
        var forwarded = new List<uint[]>();
        ActivateCombinedHooks(factory, new RecordingWrapperFactory(), memory, dispatcher,
            set => ConfigureConfirmationBuilder(
                factory, set, forwarded, extraBinding: false, useTextManager: true));
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var words = EncodeInlineName("Crono");

        factory.GetDetour<NameConfirmationBuilderDelegate>(HookId.NameConfirmationBuilder)(
            (nint)NameScene, words[0], words[1], words[2], words[3], words[4], words[5]);

        Assert.Empty(dispatcher.Failures);
        var confirmation = Assert.IsType<NameConfirmationPresented>(Assert.Single(dispatcher.Events));
        Assert.Equal("Use Crono?", confirmation.Prompt);
        Assert.Equal(new[] { "Yes", "No" }, confirmation.Choices);
        Assert.Equal(1, confirmation.SelectedIndex);
        Assert.Equal(words, Assert.Single(forwarded));
        Assert.Equal(2, textCalls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("mismatch")]
    public void ConfirmationRejectsInvalidCustomButtonConstructorReturnWithoutChangingNativeReturn(
        string behavior)
    {
        var memory = CreateNameMemory();
        AddConfirmationMemory(memory);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        factory.SetOriginal<NsMenuCustomButtonConstructorDelegate>(HookId.NsMenuCustomButtonConstructor,
            storage => behavior == "null" ? 0 : storage + 4);
        var returned = new List<nint>();
        var builderCalls = 0;
        NewGameHookSet? set = null;
        factory.SetOriginal<NameConfirmationBuilderDelegate>(HookId.NameConfirmationBuilder,
            (scene, _, _, _, _, _, _) =>
            {
                builderCalls++;
                var constructor = factory.GetDetour<NsMenuCustomButtonConstructorDelegate>(
                    HookId.NsMenuCustomButtonConstructor);
                var resolver = factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
                returned.Add(constructor((nint)ConfirmationControl0));
                resolver(0x100, (nint)ConfirmationTextAddress(1), 0x41, 0x11);
                returned.Add(constructor((nint)ConfirmationControl1));
                resolver(0x100, (nint)ConfirmationTextAddress(2), 0x41, 0x12);
                resolver(0x100, (nint)ConfirmationTextAddress(0), 0x23, 0xDA);
                set!.AfterFocusSet((nint)ConfirmationManager, 1);
            });
        set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var words = EncodeInlineName("Crono");

        factory.GetDetour<NameConfirmationBuilderDelegate>(HookId.NameConfirmationBuilder)(
            (nint)NameScene, words[0], words[1], words[2], words[3], words[4], words[5]);

        Assert.Equal(1, builderCalls);
        Assert.Equal(
            behavior == "null"
                ? new nint[] { 0, 0 }
                : [(nint)ConfirmationControl0 + 4, (nint)ConfirmationControl1 + 4],
            returned);
        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("exact non-null ECX", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmationRejectsExtraBindingEvenWhenConstructedControlsHaveKeysZeroAndOne()
    {
        var memory = CreateNameMemory();
        AddConfirmationMemory(memory, includeExtraState: true);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var forwarded = new List<uint[]>();
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        ConfigureConfirmationBuilder(factory, set, forwarded, extraBinding: true);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var words = EncodeInlineName("Crono");

        factory.GetDetour<NameConfirmationBuilderDelegate>(HookId.NameConfirmationBuilder)(
            (nint)NameScene, words[0], words[1], words[2], words[3], words[4], words[5]);

        Assert.Single(forwarded);
        Assert.Empty(dispatcher.Events);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("exact distinct keys", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReentrantControlInitScopeFailureStillCallsBothNativeOriginalFrames()
    {
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        byte nestedResult = 0;
        factory.SetOriginal<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit, scene =>
        {
            calls++;
            if (calls == 1)
            {
                nestedResult = factory.GetDetour<OpeManualSceneInitDelegate>(
                    HookId.OpeManualSceneInit)(scene);
            }
            return 1;
        });
        var set = new NewGameHookSet(
            factory, new RecordingWrapperFactory(), CreateControlMemory(), dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        var result = factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)(
            (nint)ControlScene);

        Assert.Equal(1, result);
        Assert.Equal(1, nestedResult);
        Assert.Equal(2, calls);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("scope", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReentrantModeInitScopeFailureStillCallsBothNativeOriginalFrames()
    {
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        byte nestedResult = 0;
        factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, scene =>
        {
            calls++;
            if (calls == 1)
            {
                nestedResult = factory.GetDetour<ModeSelectSteamInitDelegate>(
                    HookId.ModeSelectSteamInit)(scene);
            }
            return 1;
        });
        var set = new NewGameHookSet(
            factory, new RecordingWrapperFactory(), CreateModeMemory(), dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        var result = factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
            (nint)ModeScene);

        Assert.Equal(1, result);
        Assert.Equal(1, nestedResult);
        Assert.Equal(2, calls);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("scope", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReentrantNameInitScopeFailureStillCallsBothNativeOriginalFrames()
    {
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        byte nestedResult = 0;
        factory.SetOriginal<NameInputSceneInitDelegate>(HookId.NameInputSceneInit, scene =>
        {
            calls++;
            if (calls == 1)
            {
                nestedResult = factory.GetDetour<NameInputSceneInitDelegate>(
                    HookId.NameInputSceneInit)(scene);
            }
            return 1;
        });
        var set = new NewGameHookSet(
            factory, new RecordingWrapperFactory(), CreateNameMemory(), dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        var result = factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)(
            (nint)NameScene);

        Assert.Equal(1, result);
        Assert.Equal(1, nestedResult);
        Assert.Equal(2, calls);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("scope", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReentrantConfirmationScopeFailureStillCallsBothNativeOriginalFrames()
    {
        var memory = CreateNameMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        ConfigureNameInit(factory);
        var calls = 0;
        factory.SetOriginal<NameConfirmationBuilderDelegate>(HookId.NameConfirmationBuilder,
            (scene, word0, word1, word2, word3, length, capacity) =>
            {
                calls++;
                if (calls == 1)
                {
                    factory.GetDetour<NameConfirmationBuilderDelegate>(
                        HookId.NameConfirmationBuilder)(
                        scene, word0, word1, word2, word3, length, capacity);
                }
            });
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)((nint)NameScene);
        dispatcher.Events.Clear();
        var words = EncodeInlineName("Crono");

        factory.GetDetour<NameConfirmationBuilderDelegate>(HookId.NameConfirmationBuilder)(
            (nint)NameScene, words[0], words[1], words[2], words[3], words[4], words[5]);

        Assert.Equal(2, calls);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("scope", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dispatcher.Events);
    }

    [Fact]
    public void ThrowingSemanticPublicationCannotReplaceSuccessfulNativeInitReturnValues()
    {
        AssertControlReturn();
        AssertModeReturn();
        AssertNameReturn();

        static void AssertControlReturn()
        {
            var memory = CreateControlMemory();
            var dispatcher = new RecordingDispatcher { ThrowOnPublish = true };
            var factory = new RecordingHookFactory();
            var calls = 0;
            var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
            ConfigureControlInit(factory, set, memory, () => calls++);
            var installer = new ReloadedHookInstaller(set.Registrations, [set]);
            installer.PrepareAll(CreateBuild(), CreateBoundary());
            installer.ActivateAll();

            var exception = Record.Exception(() => Assert.Equal(
                1,
                factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)(
                    (nint)ControlScene)));

            Assert.Null(exception);
            Assert.Equal(1, calls);
            Assert.Single(dispatcher.Failures);
            factory.GetDetour<ControlNextCallbackDelegate>(HookId.ControlNextCallback)(
                0xB0000, 0xDEAD0, 0);
            Assert.Equal(1, dispatcher.PublishAttempts);
        }

        static void AssertModeReturn()
        {
            var memory = CreateModeMemory();
            var dispatcher = new RecordingDispatcher { ThrowOnPublish = true };
            var factory = new RecordingHookFactory();
            var calls = 0;
            factory.SetOriginal<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit, _ =>
            {
                calls++;
                EmitModeLocalizedText(factory);
                return 1;
            });
            var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
            var installer = new ReloadedHookInstaller(set.Registrations, [set]);
            installer.PrepareAll(CreateBuild(), CreateBoundary());
            installer.ActivateAll();

            var exception = Record.Exception(() => Assert.Equal(
                1,
                factory.GetDetour<ModeSelectSteamInitDelegate>(HookId.ModeSelectSteamInit)(
                    (nint)ModeScene)));

            Assert.Null(exception);
            Assert.Equal(1, calls);
            Assert.Single(dispatcher.Failures);
            factory.GetDetour<ModeSelectCallbackDelegate>(HookId.ModeSelectCallback)(
                0xDEAD0, 99, 0);
            Assert.Equal(1, dispatcher.PublishAttempts);
        }

        static void AssertNameReturn()
        {
            var memory = CreateNameMemory();
            var dispatcher = new RecordingDispatcher { ThrowOnPublish = true };
            var factory = new RecordingHookFactory();
            var calls = 0;
            ConfigureNameInit(factory, onOriginal: () => calls++);
            var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
            var installer = new ReloadedHookInstaller(set.Registrations, [set]);
            installer.PrepareAll(CreateBuild(), CreateBoundary());
            installer.ActivateAll();

            var exception = Record.Exception(() => Assert.Equal(
                1,
                factory.GetDetour<NameInputSceneInitDelegate>(HookId.NameInputSceneInit)(
                    (nint)NameScene)));

            Assert.Null(exception);
            Assert.Equal(1, calls);
            Assert.Single(dispatcher.Failures);
            factory.GetDetour<NameInputSceneUpdateDelegate>(HookId.NameInputSceneUpdate)(
                (nint)NameScene, 0.016f);
            Assert.Equal(1, dispatcher.PublishAttempts);
        }
    }

    [Fact]
    public void UnreadableControlNextArgumentStillCallsOriginalOnceAndFaultsExactlyOnce()
    {
        var memory = CreateControlMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        factory.SetOriginal<ControlNextCallbackDelegate>(HookId.ControlNextCallback,
            (_, _, _) => calls++);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        ConfigureControlInit(factory, set, memory);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)((nint)ControlScene);
        dispatcher.Events.Clear();

        var next = factory.GetDetour<ControlNextCallbackDelegate>(HookId.ControlNextCallback);
        next(0xB0000, 0xDEAD0, 0xDEAD4);
        next(0xB0000, 0xDEAD0, 0xDEAD4);

        Assert.Equal(2, calls);
        Assert.Single(dispatcher.Failures);
        Assert.Contains("Control Descriptions", dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dispatcher.Events);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void ControlInitPreservesNativeResultAndNeverPublishesPartialLocalizedRecords(
        byte nativeResult,
        bool expectFailure)
    {
        var memory = CreateControlMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var originalCalls = 0;
        factory.SetOriginal<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit, _ =>
        {
            originalCalls++;
            var resolver = factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
            resolver(0x100, 0x80000, 0x23, 0xD6);
            resolver(0x100, 0x80020, 0x23, 0xD5);
            for (var index = 0; index < ControlDescriptionCapture.RequiredMessageIds.Count - 1; index++)
            {
                resolver(
                    0x100,
                    (nint)(0x80100 + index * 0x20),
                    0x37,
                    ControlDescriptionCapture.RequiredMessageIds[index]);
            }
            return nativeResult;
        });
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();

        var result = factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)(
            (nint)ControlScene);

        Assert.Equal(nativeResult, result);
        Assert.Equal(1, originalCalls);
        Assert.Empty(dispatcher.Events);
        Assert.Equal(expectFailure ? 1 : 0, dispatcher.Failures.Count);
    }

    [Fact]
    public void ControlNextMirrorsNativeSilentHandlingForEveryNonZeroNonTwoEvent()
    {
        var memory = CreateControlMemory().AddInt32(0xA0000, 99);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var calls = 0;
        factory.SetOriginal<ControlNextCallbackDelegate>(HookId.ControlNextCallback,
            (_, _, _) => calls++);
        var set = new NewGameHookSet(factory, new RecordingWrapperFactory(), memory, dispatcher);
        ConfigureControlInit(factory, set, memory);
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        installer.PrepareAll(CreateBuild(), CreateBoundary());
        installer.ActivateAll();
        factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)((nint)ControlScene);
        dispatcher.Events.Clear();

        var next = factory.GetDetour<ControlNextCallbackDelegate>(HookId.ControlNextCallback);
        next(0xB0000, 0xA0000, unchecked((nint)0xDEADBEEF));

        Assert.Equal(1, calls);
        Assert.Empty(dispatcher.Events);
        Assert.Empty(dispatcher.Failures);
    }

    [Fact]
    public void ControlDescriptionsCaptureAllTwentyFiveProcessedRecordsOnceThroughTextManager()
    {
        var memory = CreateControlMemory();
        var reader = new MsvcStringReader(memory);
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory(includeStartupOriginals: true);
        var textCalls = 0;
        factory.SetOriginal<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg,
            (manager, result, bank, messageId) =>
            {
                textCalls++;
                Assert.True(reader.TryRead((nuint)result, out var displayed, out var error), error);
                var rawAddress = (nuint)result + 0x100000;
                memory.AddHeapString(rawAddress, rawAddress + 0x10000, $"key,{displayed}");
                factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver)(
                    manager, (nint)rawAddress, bank, messageId);
                return result;
            });
        ActivateCombinedHooks(factory, new RecordingWrapperFactory(), memory, dispatcher,
            set => ConfigureControlInit(factory, set, memory, useTextManager: true));

        Assert.Equal(1, factory.GetDetour<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit)(
            (nint)ControlScene));

        Assert.Empty(dispatcher.Failures);
        var presented = Assert.IsType<ControlDescriptionsPresented>(Assert.Single(dispatcher.Events));
        Assert.Equal("Control Descriptions", presented.Title);
        Assert.Equal("Next", presented.NextLabel);
        Assert.Equal(Enumerable.Range(0, 25).Select(index => $"Text {index}"),
            presented.Lines.SelectMany(line => line.VisibleTexts));
        Assert.Equal(27, textCalls);
    }

    private static void ConfigureControlInit(
        RecordingHookFactory factory,
        NewGameHookSet set,
        TestMemory memory,
        Action? onOriginal = null,
        bool useTextManager = false)
    {
        factory.SetOriginal<OpeManualSceneInitDelegate>(HookId.OpeManualSceneInit, _ =>
        {
            onOriginal?.Invoke();
            OpeTextResolverDelegate resolver = useTextManager
                ? (manager, result, bank, messageId) =>
                    factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
                        manager, result, bank, messageId)
                : factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
            resolver(0x100, 0x80000, 0x23, 0xD6);
            resolver(0x100, 0x80020, 0x23, 0xD5);
            for (var index = 0; index < ControlDescriptionCapture.RequiredMessageIds.Count; index++)
            {
                resolver(
                    0x100,
                    (nint)(0x80100 + index * 0x20),
                    0x37,
                    ControlDescriptionCapture.RequiredMessageIds[index]);
            }
            set.AfterFocusSet((nint)ControlManager, 0);
            return 1;
        });
    }

    private static TestMemory CreateControlMemory()
    {
        var memory = new TestMemory()
            .AddHeapString(0x80000, 0x88000, "Control Descriptions")
            .AddInlineString(0x80020, "Next")
            .AddPointer(ImageBase + NewGameHookSet.ControllerConfigurationRootRva, 0x70000)
            .AddByte(0x70000 + NewGameHookSet.ControllerArtworkSelectorOffset, 1)
            .AddInt32(ControlManager + NewGameHookSet.ManagerFocusKeyOffset, 0)
            .AddInt32(0xB0000 + NewGameHookSet.ControlNextClosureStateOffset, 0);
        for (var index = 0; index < ControlDescriptionCapture.RequiredMessageIds.Count; index++)
        {
            memory.AddInlineString(
                (nuint)(0x80100 + index * 0x20),
                $"Text {index}");
        }
        var records = new (int Id, int X, int Y, int Anchor)[]
        {
            (0x0E,185,-53,2),(0x02,190,-53,0),(0x0E,185,-72,2),(0x03,190,-72,0),
            (0x0E,185,-91,2),(0x04,190,-91,0),(0x0E,185,-110,2),(0x05,190,-110,0),
            (0x0E,185,-129,2),(0x06,190,-129,0),(0x0F,124,-129,1),(0x07,190,-148,0),
            (0x0E,185,-167,2),(0x08,190,-167,0),(0x10,124,-167,1),(0x0E,185,-186,2),
            (0x12,190,-186,0),(0x0E,185,-205,2),(0x09,190,-205,0),(0x0E,185,-224,2),
            (0x0A,190,-224,0),(0x0E,185,-243,2),(0x0B,190,-243,0),(0x0E,185,-262,2),
            (0x0C,190,-262,0),
        };
        for (var index = 0; index < records.Length; index++)
        {
            var address = ImageBase + ControlDescriptionCapture.RecordTableRva + (nuint)(index * 16);
            memory.AddInt32(address, records[index].Id)
                .AddInt32(address + 4, records[index].X)
                .AddInt32(address + 8, records[index].Y)
                .AddInt32(address + 12, records[index].Anchor);
        }
        uint[] names =
        [
            0x3B1E54,0x3B1E60,0x3B1E78,0x3B1E6C,0x3B1E90,0x3B1E84,0x3B1E90,
            0x3B1E84,0x3B1EA4,0x3B1E9C,0x3B1EBC,0x3B1EB0,0x3B1ED4,
        ];
        var sprites = new (int X, int Y)[]
        {
            (100,-53),(100,-72),(100,-91),(100,-110),(100,-129),(132,-129),(100,-167),
            (132,-167),(100,-186),(100,-205),(100,-224),(100,-243),(100,-262),
        };
        foreach (var table in new[]
            {
                ControlDescriptionCapture.NormalSpriteTableRva,
                ControlDescriptionCapture.AlternateSpriteTableRva,
            })
        {
            for (var index = 0; index < sprites.Length; index++)
            {
                var nameIndex = table == ControlDescriptionCapture.AlternateSpriteTableRva && index < 2
                    ? 1 - index
                    : index;
                var address = ImageBase + table + (nuint)(index * 12);
                memory.AddPointer(address, ImageBase + names[nameIndex])
                    .AddInt32(address + 4, sprites[index].X)
                    .AddInt32(address + 8, sprites[index].Y);
            }
        }
        return memory;
    }

    private static TestMemory CreateModeMemory()
    {
        var memory = new TestMemory()
            .AddPointer(ModeScene + ModeSelectCapture.RecordsBeginOffset, ModeRecords)
            .AddPointer(
                ModeScene + ModeSelectCapture.RecordsEndOffset,
                ModeRecords + 3 * ModeSelectCapture.RecordStride)
            .AddPointer(
                ModeScene + ModeSelectCapture.RecordsCapacityOffset,
                ModeRecords + 3 * ModeSelectCapture.RecordStride)
            .AddInt32(ModeScene + ModeSelectCapture.CompositeFocusOffset, 0);

        for (var index = 0; index < ModeSelectCapture.RowCount; index++)
        {
            var record = ModeRecords + (nuint)(index * ModeSelectCapture.RecordStride);
            var values = 0xA0000u + (nuint)(index * 0x100);
            var target = ModeGetterTarget(index);
            var vtable = ModeGetterVtable(index);
            memory
                .AddPointer(record + ModeSelectCapture.ValuesBeginOffset, values)
                .AddPointer(
                    record + ModeSelectCapture.ValuesEndOffset,
                    values + 2 * MsvcStringReader.LayoutSize)
                .AddPointer(
                    record + ModeSelectCapture.ValuesCapacityOffset,
                    values + 2 * MsvcStringReader.LayoutSize)
                .AddPointer(record + ModeSelectCapture.CurrentValueGetterTargetOffset, target)
                .AddPointer(target, vtable)
                .AddPointer(vtable + 8, ImageBase + new uint[] { 0x1EC320, 0x1EBEB0, 0x2AC370 }[index]);
        }

        var keys = EnumerateModeTextKeys().ToArray();
        for (var index = 0; index < keys.Length; index++)
        {
            memory.AddInlineString(ModeTextAddress(index), $"Mode {index}");
        }
        return memory;
    }

    private static void EmitModeLocalizedText(RecordingHookFactory factory, bool useTextManager = false)
    {
        var resolver = factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
        var keys = EnumerateModeTextKeys().ToArray();
        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            if (useTextManager && (key.Bank != 0x23 || key == ModeSelectCapture.StartTextKey))
            {
                factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
                    0x100, (nint)ModeTextAddress(index), key.Bank, key.MessageId);
            }
            else
            {
                resolver(0x100, (nint)ModeTextAddress(index), key.Bank, key.MessageId);
            }
        }
    }

    private static IEnumerable<LocalizedMessageKey> EnumerateModeTextKeys() =>
        ModeSelectCapture.TextContracts
            .SelectMany(contract => new[] { contract.Label }.Concat(contract.Values).Concat(contract.Help))
            .Append(ModeSelectCapture.StartTextKey);

    private static nuint ModeTextAddress(int index) => 0xB0000u + (nuint)(index * 0x20);

    private static nuint ModeGetterTarget(int index) => 0xA1000u + (nuint)(index * 0x100);
    private static nuint ModeGetterVtable(int index) => 0xA2000u + (nuint)(index * 0x100);

    private static TestMemory CreateNameMemory(
        string name = "Crono",
        byte active = 1,
        int page = 0,
        int row = 0,
        int column = 0,
        string gridText = "A")
    {
        var memory = new TestMemory()
            .AddByte(NameScene + NameInputCapture.ActiveOffset, active)
            .AddInt32(NameScene + NameInputCapture.PageOffset, page)
            .AddInt32(NameScene + NameInputCapture.ColumnOffset, column)
            .AddInt32(NameScene + NameInputCapture.RowOffset, row)
            .AddPointer(NameScene + NameInputCapture.GlyphAppendTargetOffset, 0xE0000)
            .AddPointer(NameScene + NameInputCapture.DeleteTargetOffset, 0xE0100)
            .AddPointer(NameScene + NameInputCapture.RefreshTargetOffset, 0xE0200)
            .AddPointer(0xE0000, 0xE1000)
            .AddPointer(0xE1000 + 8, ImageBase + NameInputCapture.GlyphAppendInvokeRva)
            .AddPointer(0xE0100, 0xE1100)
            .AddPointer(0xE1100 + 8, ImageBase + NameInputCapture.DeleteInvokeRva)
            .AddPointer(0xE0200, 0xE1200)
            .AddPointer(0xE1200 + 8, ImageBase + NameInputCapture.RefreshInvokeRva)
            .AddInt32(ImageBase + NameInputCapture.LanguageGlobalRva, 1)
            .AddInlineString(NameScene + NameInputCapture.NameOffset, name)
            .AddInt32(NameManager + NewGameHookSet.ManagerFocusKeyOffset, 0)
            .AddInlineString(NameTextAddress(0), "Grid Action")
            .AddInlineString(NameTextAddress(1), "Defaults")
            .AddInlineString(NameTextAddress(2), "Accept");

        AddFocusableState(memory, NameState0, NameControl0);
        AddFocusableState(memory, NameState1, NameControl1);
        AddFocusableState(memory, NameState2, NameControl2);
        AddFocusableState(memory, NameState3, NameControl3);
        AddFocusableState(memory, 0xC2400, 0xC3400);
        AddFocusableState(memory, 0xC2500, 0xC3500);
        if (active != 0 && page is >= 0 and <= 2 && row is >= 0 and <= 7 && column is >= 0 and <= 10)
        {
            var index = (row + page * 8) * 11 + column;
            memory
                .AddPointer(
                    ImageBase + NameInputCapture.GridPointerTableRva + (nuint)(index * 4),
                    0xE2000)
                .AddCString(0xE2000, gridText);
        }
        return memory;
    }

    private static void ConfigureNameInit(
        RecordingHookFactory factory,
        int extraBindingCount = 0,
        Action? onOriginal = null,
        bool includeGridAction = true)
    {
        factory.SetOriginal<NameInputSceneInitDelegate>(HookId.NameInputSceneInit, _ =>
        {
            onOriginal?.Invoke();
            var resolver = factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
            if (includeGridAction)
            {
                resolver(0x100, (nint)NameTextAddress(0),
                    NameInputCapture.GridActionTextKey.Bank,
                    NameInputCapture.GridActionTextKey.MessageId);
            }
            resolver(0x100, (nint)NameTextAddress(1),
                NameInputCapture.DefaultsTextKey.Bank,
                NameInputCapture.DefaultsTextKey.MessageId);
            resolver(0x100, (nint)NameTextAddress(2),
                NameInputCapture.AcceptTextKey.Bank,
                NameInputCapture.AcceptTextKey.MessageId);
            var binder = factory.GetDetour<NsMenuControlBinderDelegate>(HookId.NsMenuControlBinder);
            binder((nint)NameManager, (nint)NameState0, 0);
            binder((nint)NameManager, (nint)NameState1, 1);
            binder((nint)NameManager, (nint)NameState2, 2);
            binder((nint)NameManager, (nint)NameState3, 3);
            for (var index = 0; index < extraBindingCount; index++)
            {
                binder((nint)NameManager, (nint)(0xC2400 + index * 0x100), 4 + index);
            }
            return 1;
        });
    }

    private static void AddConfirmationMemory(TestMemory memory, bool includeExtraState = false)
    {
        memory
            .AddInt32(ConfirmationManager + NewGameHookSet.ManagerFocusKeyOffset, 1)
            .AddInlineString(ConfirmationTextAddress(0), "Use <NAME>?")
            .AddInlineString(ConfirmationTextAddress(1), "Yes")
            .AddInlineString(ConfirmationTextAddress(2), "No");
        AddFocusableState(memory, ConfirmationState0, ConfirmationControl0);
        AddFocusableState(memory, ConfirmationState1, ConfirmationControl1);
        if (includeExtraState)
        {
            AddFocusableState(memory, 0xC6200, 0xC5200);
        }
    }

    private static void ConfigureConfirmationBuilder(
        RecordingHookFactory factory,
        NewGameHookSet set,
        List<uint[]> forwarded,
        bool extraBinding,
        bool useTextManager = false)
    {
        factory.SetOriginal<NameConfirmationBuilderDelegate>(HookId.NameConfirmationBuilder,
            (scene, word0, word1, word2, word3, length, capacity) =>
            {
                forwarded.Add([word0, word1, word2, word3, length, capacity]);
                var constructor = factory.GetDetour<NsMenuCustomButtonConstructorDelegate>(
                    HookId.NsMenuCustomButtonConstructor);
                var resolver = factory.GetDetour<OpeTextResolverDelegate>(HookId.OpeTextResolver);
                var binder = factory.GetDetour<NsMenuControlBinderDelegate>(HookId.NsMenuControlBinder);

                constructor((nint)ConfirmationControl0);
                if (useTextManager)
                {
                    factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
                        0x100, (nint)ConfirmationTextAddress(1), 0x41, 0x11);
                }
                else
                {
                    resolver(0x100, (nint)ConfirmationTextAddress(1), 0x41, 0x11);
                }
                constructor((nint)ConfirmationControl1);
                if (useTextManager)
                {
                    factory.GetDetour<TextManagerGetMsgDelegate>(HookId.TextManagerGetMsg)(
                        0x100, (nint)ConfirmationTextAddress(2), 0x41, 0x12);
                }
                else
                {
                    resolver(0x100, (nint)ConfirmationTextAddress(2), 0x41, 0x12);
                }
                resolver(0x100, (nint)ConfirmationTextAddress(0), 0x23, 0xDA);
                binder((nint)ConfirmationManager, (nint)ConfirmationState0, 0);
                binder((nint)ConfirmationManager, (nint)ConfirmationState1, 1);
                if (extraBinding)
                {
                    binder((nint)ConfirmationManager, 0xC6200, 2);
                }
                set.AfterFocusSet((nint)ConfirmationManager, 1);
            });
    }

    private static void AddFocusableState(TestMemory memory, nuint state, nuint control)
    {
        memory
            .AddPointer(state, ImageBase + NewGameHookSet.FocusableStateVtableRva)
            .AddPointer(state + NewGameHookSet.FocusableStateControlOffset, control);
    }

    private static void AddDirectCloseCapture(
        TestMemory memory,
        nuint capture,
        nuint vector,
        nuint target)
    {
        memory
            .AddPointer(capture, target)
            .AddPointer(capture + 4, vector)
            .AddPointer(capture + 8, vector + 8)
            .AddPointer(capture + 12, vector + 8)
            .AddPointer(capture + 0x10, NameControl3)
            .AddPointer(vector, NameControl0)
            .AddPointer(vector + 4, NameControl1);
    }

    private static void AddDirectOpenCapture(
        TestMemory memory,
        nuint capture,
        nuint vector,
        nuint target,
        nuint nameEdit)
    {
        memory
            .AddPointer(capture, target)
            .AddPointer(capture + 4, nameEdit)
            .AddPointer(capture + 8, vector)
            .AddPointer(capture + 12, vector + 8)
            .AddPointer(capture + 0x10, vector + 8)
            .AddPointer(capture + 0x14, NameControl3)
            .AddPointer(vector, NameControl0)
            .AddPointer(vector + 4, NameControl1);
    }

    private static uint[] EncodeInlineName(string name)
    {
        var encoded = Encoding.UTF8.GetBytes(name);
        Assert.True(encoded.Length < 16);
        Span<byte> inline = stackalloc byte[16];
        encoded.CopyTo(inline);
        return
        [
            BinaryPrimitives.ReadUInt32LittleEndian(inline),
            BinaryPrimitives.ReadUInt32LittleEndian(inline[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(inline[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(inline[12..]),
            (uint)encoded.Length,
            15,
        ];
    }

    private static nuint NameTextAddress(int index) => 0xD1000u + (nuint)(index * 0x20);
    private static nuint ConfirmationTextAddress(int index) => 0xD2000u + (nuint)(index * 0x20);

    private static IReadOnlyList<AccessibilityEvent> Flatten(
        IEnumerable<AccessibilityEvent> events) =>
        events.SelectMany(item => item is NameAccessibilityBatch batch
            ? batch.Events.Cast<AccessibilityEvent>()
            : [item]).ToArray();

    private static VerifiedBuild CreateBuild() => new(
        ImageBase,
        ExpectedDedicatedHooks.ToDictionary(
            id => id,
            id => ImageBase + GameVersionCatalog.Get(id).Rva));

    private static NewGameHookSet ActivateCombinedHooks(
        RecordingHookFactory factory,
        RecordingWrapperFactory wrappers,
        TestMemory memory,
        RecordingDispatcher dispatcher,
        Action<NewGameHookSet>? configure = null)
    {
        memory.AddInt32(ImageBase + StartupTitleHookSet.CurrentSceneGlobalRva,
            StartupTitleHookSet.SquareEnixSceneId);
        var composition = ChronoTriggerAccessibility.Mod.Mod.CreateAccessibilityComposition(
            factory, wrappers, memory, dispatcher, new OpeningMovieTimeline());
        var ids = composition.StartupTitleHookSet.RequiredHookIds
            .Concat(composition.NewGameHookSet.RequiredHookIds);
        var build = new VerifiedBuild(ImageBase, ids.ToDictionary(
            id => id, id => ImageBase + GameVersionCatalog.Get(id).Rva));
        configure?.Invoke(composition.NewGameHookSet);
        composition.Installer.PrepareAll(build, CreateBoundary());
        composition.Installer.ActivateAll();
        dispatcher.Events.Clear();
        return composition.NewGameHookSet;
    }

    private static UnmanagedBoundaryGuard CreateBoundary() =>
        new(new RecordingLog(), new RecordingFatal());

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class RecordingHookFactory : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals = new()
        {
            [HookId.OpeTextResolver] = (OpeTextResolverDelegate)((_, result, _, _) => result),
            [HookId.OpeManualSceneInit] = (OpeManualSceneInitDelegate)(_ => 1),
            [HookId.ModeSelectSteamInit] = (ModeSelectSteamInitDelegate)(_ => 1),
            [HookId.ModeSelectCallback] = (ModeSelectCallbackDelegate)((_, _, _) => { }),
            [HookId.NameInputSceneInit] = (NameInputSceneInitDelegate)(_ => 1),
            [HookId.NameInputSceneUpdate] = (NameInputSceneUpdateDelegate)((_, _) => { }),
            [HookId.NameConfirmationBuilder] = (NameConfirmationBuilderDelegate)((_, _, _, _, _, _, _) => { }),
            [HookId.NsMenuCustomButtonConstructor] = (NsMenuCustomButtonConstructorDelegate)(storage => storage),
            [HookId.NsMenuControlBinder] = (NsMenuControlBinderDelegate)((_, _, _) => { }),
            [HookId.ControlNextCallback] = (ControlNextCallbackDelegate)((_, _, _) => { }),
            [HookId.NameActionCallback] = (NameActionCallbackDelegate)((_, _, _) => { }),
            [HookId.NameDirectEntryActivation] = (NameDirectEntryActivationDelegate)(_ => { }),
            [HookId.NameDirectEntryClose] = (NameDirectEntryCloseDelegate)(_ => { }),
            [HookId.NameGridRefresh] = (NameGridRefreshDelegate)(_ => { }),
        };
        private readonly Dictionary<HookId, Delegate> detours = [];

        public List<(HookId Id, nuint Address)> Created { get; } = [];

        public RecordingHookFactory(bool includeStartupOriginals = false)
        {
            if (!includeStartupOriginals)
            {
                return;
            }
            originals[HookId.TextManagerGetMsg] = (TextManagerGetMsgDelegate)((_, result, _, _) => result);
            originals[HookId.SceneManagerCreate] = (SceneManagerCreateDelegate)((_, _) => 0);
            originals[HookId.SceneManagerNextScene] = (SceneManagerNextSceneDelegate)(_ => { });
            originals[HookId.TitleMenuModeEnter] = (TitleMenuModeEnterDelegate)(_ => { });
            originals[HookId.TitleRowFactory] = (TitleRowFactoryDelegate)(_ => 1);
            originals[HookId.TitleSceneUpdate] = (TitleSceneUpdateDelegate)((_, _) => { });
            originals[HookId.NsMenuFocusSetter] = (NsMenuFocusSetterDelegate)((_, _) => { });
            originals[HookId.TitleMenuCallback] = (TitleMenuCallbackDelegate)((_, _, _) => { });
        }

        public void SetOriginal<TDelegate>(HookId id, TDelegate original)
            where TDelegate : Delegate => originals[id] = original;

        public TDelegate GetDetour<TDelegate>(HookId id)
            where TDelegate : Delegate => (TDelegate)detours[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            Created.Add((id, address));
            detours[id] = detour;
            return new FakeHook<TDelegate>((TDelegate)originals[id]);
        }
    }

    private sealed class RecordingWrapperFactory : IRuntimeNativeFunctionWrapperFactory
    {
        public List<nuint> Created { get; } = [];
        public List<nuint> InvokedTargets { get; } = [];
        public int[] Values { get; set; } = [0, 0, 0];
        public int? ThrowOnRow { get; set; }
        public TDelegate CreateWrapper<TDelegate>(nuint address) where TDelegate : Delegate
        {
            var row = Created.Count;
            Created.Add(address);
            return (TDelegate)(Delegate)(ModeValueGetterDelegate)(target =>
            {
                InvokedTargets.Add((nuint)target);
                if (ThrowOnRow == row)
                {
                    throw new InvalidOperationException("simulated getter failure");
                }
                return Values[row];
            });
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

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public TestMemory AddByte(nuint address, byte value)
        {
            segments[address] = [value];
            return this;
        }

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

        public TestMemory AddInlineString(nuint address, string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            Assert.True(encoded.Length < 16, $"Test inline string '{value}' is too long.");
            var layout = new byte[MsvcStringReader.LayoutSize];
            encoded.CopyTo(layout, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 15);
            segments[address] = layout;
            return this;
        }

        public TestMemory AddHeapString(nuint address, nuint dataAddress, string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            var layout = new byte[MsvcStringReader.LayoutSize];
            BinaryPrimitives.WriteUInt32LittleEndian(layout, checked((uint)dataAddress));
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), (uint)Math.Max(16, encoded.Length));
            segments[address] = layout;
            segments[dataAddress] = encoded;
            return this;
        }

        public TestMemory AddCString(nuint address, string value)
        {
            segments[address] = [.. Encoding.UTF8.GetBytes(value), 0];
            return this;
        }

        public TestMemory Remove(nuint address)
        {
            segments.Remove(address);
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
        public Action<AccessibilityEvent>? AfterPublish { get; set; }
        public bool ThrowOnPublish { get; set; }
        public int PublishAttempts { get; private set; }
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent accessibilityEvent)
        {
            PublishAttempts++;
            if (ThrowOnPublish)
            {
                throw new InvalidOperationException("simulated semantic publication failure");
            }
            Events.Add(accessibilityEvent);
            AfterPublish?.Invoke(accessibilityEvent);
        }
        public void ReportCoverageFailure(string message) => Failures.Add(message);
    }

    private sealed class RecordingLog : IModLog
    {
        public void Info(string message) { }
        public void Error(string message) { }
    }

    private sealed class RecordingFatal : IAccessibleFatalError
    {
        public void Show(string message) { }
    }
}
