using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Extras;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Extras;

public sealed class ExtrasHookSetTests
{
    private const nuint ImageBase = 0x00400000;
    private const nuint Scene = 0x00900000;
    private const nuint Node = 0x00910000;
    private const nuint Manager = 0x00920000;
    private const nuint Closure = 0x00930000;
    private const nuint ControlVector = 0x00940000;
    private const nuint SaveData = 0x00B00000;

    private static readonly HookId[] ExpectedHookIds =
    [
        HookId.GallerySceneSwitchNode,
        HookId.ExtrasHubOnEnter,
        HookId.EndingLogOnEnter,
        HookId.EndingDetailOnEnter,
        HookId.ExtrasNodeOnExit,
        HookId.ExtrasHubDeletingDestructor,
        HookId.EndingLogDeletingDestructor,
        HookId.ExtrasHubCallback,
        HookId.EndingLogCallback,
        HookId.EndingDetailCallback,
    ];

    [Fact]
    public void PreparationOwnsEveryExactDedicatedBoundaryAndStartsInactive()
    {
        var harness = CreateHarness();

        Assert.Equal(ExpectedHookIds, harness.Set.RequiredHookIds);
        Assert.Equal(
            ExpectedHookIds.Select(id => GameVersionCatalog.Get(id).Symbol),
            harness.Set.Registrations.Select(registration => registration.Name));

        harness.Installer.PrepareAll(CreateBuild(), harness.Boundary);

        Assert.Equal(ExpectedHookIds, harness.Factory.Created.Select(item => item.Id));
        Assert.Equal(
            ExpectedHookIds.Select(id => ImageBase + GameVersionCatalog.Get(id).Rva),
            harness.Factory.Created.Select(item => item.Address));
        Assert.All(harness.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));

        harness.Installer.ActivateAll();
        Assert.All(harness.Installer.PreparedHooks, hook => Assert.True(hook.IsActive));

        harness.Installer.DisableAll();
        Assert.All(harness.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));
    }

    [Fact]
    public void HubUsesSwitchReturnTwoPhaseBuildAndLazilyNarratesNativeHelp()
    {
        var harness = CreateHarness();
        var controls = Controls(5);
        ConfigureNode(harness.Memory, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureManagerAndControls(harness.Memory, controls, focusedKey: 3);
        harness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddInt32(Node + 0x2E4, 1)
            .AddInt32(Node + 0x2EC, 1)
            .AddInt32(Node + 0x2F4, 1)
            .AddInt32(Node + 0x2FC, 1)
            .AddInt32(Node + 0x304, 1);

        harness.Factory.SetOriginal<GallerySceneSwitchNodeDelegate>(
            HookId.GallerySceneSwitchNode,
            (scene, action, raw) =>
            {
                Assert.Equal((nint)Scene, scene);
                Assert.Equal(0, action);
                Assert.Equal(0x12345678u, raw);
                harness.ObserveText(0x41, 0x06, "Extras");
                return (nint)Node;
            });
        harness.Factory.SetOriginal<ExtrasHubOnEnterDelegate>(HookId.ExtrasHubOnEnter, node =>
        {
            Assert.Equal((nint)Node, node);
            ObserveHubLabelsAndControls(harness, controls);
            Bind(harness, controls, [0, 1, 2, 3, 4]);
            harness.Set.AfterFocusSet((nint)Manager, 3);
            harness.ObserveText(0x1A, 0x4D, "Ending help");
        });
        var callbackCalls = 0;
        harness.Factory.SetOriginal<ExtrasHubCallbackDelegate>(HookId.ExtrasHubCallback, (_, eventType, action) =>
        {
            if (callbackCalls++ != 0)
            {
                return;
            }
            Assert.Equal(0, eventType);
            Assert.Equal(0, action);
            harness.ObserveText(0x1A, 0x4A, "Movie help");
            harness.Memory.AddInt32(Manager + ExtrasHookSet.ManagerFocusKeyOffset, 0);
            harness.Set.AfterFocusSet((nint)Manager, 0);
        });
        harness.PrepareAndActivate();

        var returned = harness.Switch(0, 0x12345678);
        harness.HubEnter();

        Assert.Equal((nint)Node, returned);
        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal("Extras", presented.Title);
        Assert.Equal(new MenuFocus("Ending Log", null, 4, 5, "Ending help", false), presented.Focus);
        Assert.Empty(presented.StatusDetails!);

        ConfigureHubClosure(harness.Memory, controls);
        harness.HubCallback(0, 0);

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Equal(
            new MenuFocusChanged(new MenuFocus("Movies", null, 1, 5, "Movie help", false)),
            harness.Dispatcher.Events[1]);

        harness.HubCallback(0, 0);
        var unsupported = Assert.IsType<MenuUnsupported>(harness.Dispatcher.Events[2]);
        Assert.Equal("Movies", unsupported.SelectedLabel);
        Assert.Contains("not yet covered", unsupported.BoundaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Back", unsupported.ReturnInstruction, StringComparison.Ordinal);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void HubLockedActivationUsesOnlyTheObservedNativeLockedHelp()
    {
        var harness = CreateHarness();
        var controls = Controls(5);
        ConfigureNode(harness.Memory, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureManagerAndControls(harness.Memory, controls, focusedKey: 0);
        ConfigureHubClosure(harness.Memory, controls);
        harness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddInt32(Node + 0x2E4, 0)
            .AddInt32(Node + 0x2EC, 0)
            .AddInt32(Node + 0x2F4, 0)
            .AddInt32(Node + 0x2FC, 0)
            .AddInt32(Node + 0x304, 1);
        ConfigureHubBuild(harness, controls, focusedKey: 0, helpMessage: 0x49, help: "Locked help");
        harness.Factory.SetOriginal<ExtrasHubCallbackDelegate>(HookId.ExtrasHubCallback, (_, _, _) =>
            harness.ObserveText(0x1A, 0x49, "Locked help"));
        harness.PrepareAndActivate();
        harness.Switch();
        harness.HubEnter();

        harness.HubCallback(0, 0);

        var unsupported = Assert.IsType<MenuUnsupported>(harness.Dispatcher.Events.Last());
        Assert.Equal("Movies", unsupported.SelectedLabel);
        Assert.Equal("Locked help", unsupported.BoundaryText);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void EndingLogExposesNineteenVisibleRowsNeverHiddenLockedTitlesAndIdentityCorrelatedBack()
    {
        var harness = CreateHarness();
        var controls = Controls(20);
        ConfigureNode(harness.Memory, ExtrasHookSet.EndingLogVtableRva);
        ConfigureManagerAndControls(harness.Memory, controls, focusedKey: 1);
        harness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddPointer(ImageBase + ExtrasHookSet.SaveDataGlobalRva, SaveData)
            .AddInt32(SaveData + ExtrasHookSet.EndingUnlockFlagsOffset, 1);
        for (var row = 0; row < 19; row++)
        {
            harness.Memory
                .AddInt32(ImageBase + ExtrasHookSet.EndingRecordTableRva + (nuint)(row * 0x2C), 0x100 + row)
                .AddInt32(ImageBase + ExtrasHookSet.EndingRecordTableRva + (nuint)(row * 0x2C) + 0x0C, 1 << row);
        }
        ConfigureSwitch(harness, action: 3, file: 0x1A, message: 0x0F, title: "Ending Log");
        harness.Factory.SetOriginal<EndingLogOnEnterDelegate>(HookId.EndingLogOnEnter, _ =>
        {
            harness.ObserveText(0x1A, 0x40, "Endings");
            harness.Set.AfterCustomButtonConstructed((nint)controls[0], (nint)controls[0]);
            harness.ObserveText(0x23, 0xD8, "Back");
            for (var row = 0; row < 19; row++)
            {
                harness.Set.AfterCustomButtonConstructed((nint)controls[row + 1], (nint)controls[row + 1]);
                if (row == 0)
                {
                    harness.ObserveText(0x0F, 0x100, "Ending One");
                }
            }
            var states = FocusStates(20);
            for (var row = 0; row < 19; row++)
            {
                BindOne(harness, states[row + 1], controls[row + 1], row);
            }
            BindOne(harness, states[0], controls[0], 77);
            harness.Set.AfterFocusSet((nint)Manager, 1);
        });
        harness.PrepareAndActivate();

        harness.Switch(3);
        harness.LogEnter();

        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal("Ending Log", presented.Title);
        Assert.Equal(new MenuFocus("???", null, 2, 20, null, true), presented.Focus);
        Assert.Equal(["Endings"], presented.StatusDetails);
        Assert.DoesNotContain(
            harness.Dispatcher.Events.OfType<MenuPresented>().SelectMany(item => item.StatusDetails!),
            text => text.Contains("Ending Two", StringComparison.Ordinal));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void EndingDetailNarratesDynamicTitleDistinctHeaderRequirementsAndControls()
    {
        var harness = CreateHarness();
        var controls = Controls(2);
        ConfigureNode(harness.Memory, ExtrasHookSet.EndingDetailVtableRva);
        ConfigureManagerAndControls(harness.Memory, controls, focusedKey: 0);
        harness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddInt32(ImageBase + ExtrasHookSet.SelectedEndingGlobalRva, 0)
            .AddInt32(ImageBase + ExtrasHookSet.EndingRecordTableRva, 0x100)
            .AddInt32(ImageBase + ExtrasHookSet.EndingRequirementTableRva, 0x200);
        ConfigureSwitch(harness, action: 4, file: 0x0F, message: 0x100, title: "Ending One");
        harness.Factory.SetOriginal<EndingDetailOnEnterDelegate>(HookId.EndingDetailOnEnter, _ =>
        {
            harness.ObserveText(0x1A, 0x4F, "Details");
            harness.ObserveText(0x1A, 0x47, "Requirements");
            harness.ObserveText(0x0F, 0x200, "Win condition");
            harness.ObserveText(0x1A, 0x48, "Review");
            harness.Set.AfterCustomButtonConstructed((nint)controls[0], (nint)controls[0]);
            harness.ObserveText(0x23, 0xD8, "Back");
            harness.Set.AfterCustomButtonConstructed((nint)controls[1], (nint)controls[1]);
            Bind(harness, controls, [0, 1]);
            harness.Set.AfterFocusSet((nint)Manager, 0);
        });
        harness.PrepareAndActivate();

        harness.Switch(4);
        harness.DetailEnter();

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal("Ending One", presented.Title);
        Assert.Equal(new MenuFocus("Review", null, 1, 2, null, false), presented.Focus);
        Assert.Equal(["Details", "Requirements: Win condition"], presented.StatusDetails);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void ExitAndEveryDeletingDestructorClearBeforeOriginalAndRemainIdempotent()
    {
        var harness = CreateHarness();
        var controls = Controls(5);
        ConfigureNode(harness.Memory, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureManagerAndControls(harness.Memory, controls, focusedKey: 4);
        harness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddInt32(Node + 0x2E4, 1).AddInt32(Node + 0x2EC, 1)
            .AddInt32(Node + 0x2F4, 1).AddInt32(Node + 0x2FC, 1)
            .AddInt32(Node + 0x304, 1);
        ConfigureHubBuild(harness, controls, focusedKey: 4, helpMessage: null, help: null);
        var exitSawCleared = false;
        var destructorSawCleared = false;
        harness.Factory.SetOriginal<ExtrasNodeOnExitDelegate>(HookId.ExtrasNodeOnExit, _ =>
        {
            var before = harness.Dispatcher.Events.Count(item => item is MenuFocusChanged);
            harness.Set.AfterFocusSet((nint)Manager, 4);
            exitSawCleared = harness.Dispatcher.Events.Count(item => item is MenuFocusChanged) == before;
        });
        harness.Factory.SetOriginal<ExtrasHubDeletingDestructorDelegate>(
            HookId.ExtrasHubDeletingDestructor,
            (node, flags) =>
            {
                var before = harness.Dispatcher.Events.Count(item => item is MenuFocusChanged);
                harness.Set.AfterFocusSet((nint)Manager, 4);
                destructorSawCleared = harness.Dispatcher.Events.Count(item => item is MenuFocusChanged) == before;
                return 0x1234;
            });
        harness.PrepareAndActivate();
        harness.Switch();
        harness.HubEnter();

        harness.Exit();
        harness.Switch();
        harness.HubEnter();
        var returned = harness.HubDelete();
        harness.Set.BeforeTouchTopMenuDeletingDestructor((nint)Node, 1);

        Assert.True(exitSawCleared);
        Assert.True(destructorSawCleared);
        Assert.Equal((nint)0x1234, returned);
        Assert.Equal(2, harness.Dispatcher.Events.Count(item => item is MenuExited));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void TeardownPublicationFailureCannotSkipOnExitOrDeletingDestructorOriginal()
    {
        var exitHarness = CreateHarness();
        var exitControls = Controls(5);
        ConfigureNode(exitHarness.Memory, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureManagerAndControls(exitHarness.Memory, exitControls, focusedKey: 4);
        exitHarness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddInt32(Node + 0x2E4, 1).AddInt32(Node + 0x2EC, 1)
            .AddInt32(Node + 0x2F4, 1).AddInt32(Node + 0x2FC, 1)
            .AddInt32(Node + 0x304, 1);
        ConfigureHubBuild(exitHarness, exitControls, focusedKey: 4, helpMessage: null, help: null);
        var exitOriginalCalls = 0;
        exitHarness.Factory.SetOriginal<ExtrasNodeOnExitDelegate>(
            HookId.ExtrasNodeOnExit,
            _ => exitOriginalCalls++);
        exitHarness.PrepareAndActivate();
        exitHarness.Switch();
        exitHarness.HubEnter();
        exitHarness.Dispatcher.ThrowOnPublish = true;

        exitHarness.Exit();

        Assert.Equal(1, exitOriginalCalls);
        Assert.Single(exitHarness.Dispatcher.Failures);

        var deleteHarness = CreateHarness();
        var deleteControls = Controls(5);
        ConfigureNode(deleteHarness.Memory, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureManagerAndControls(deleteHarness.Memory, deleteControls, focusedKey: 4);
        deleteHarness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddInt32(Node + 0x2E4, 1).AddInt32(Node + 0x2EC, 1)
            .AddInt32(Node + 0x2F4, 1).AddInt32(Node + 0x2FC, 1)
            .AddInt32(Node + 0x304, 1);
        ConfigureHubBuild(deleteHarness, deleteControls, focusedKey: 4, helpMessage: null, help: null);
        var deleteOriginalCalls = 0;
        deleteHarness.Factory.SetOriginal<ExtrasHubDeletingDestructorDelegate>(
            HookId.ExtrasHubDeletingDestructor,
            (_, _) =>
            {
                deleteOriginalCalls++;
                return 0x1234;
            });
        deleteHarness.PrepareAndActivate();
        deleteHarness.Switch();
        deleteHarness.HubEnter();
        deleteHarness.Dispatcher.ThrowOnPublish = true;

        var returned = deleteHarness.HubDelete();

        Assert.Equal(1, deleteOriginalCalls);
        Assert.Equal((nint)0x1234, returned);
        Assert.Single(deleteHarness.Dispatcher.Failures);
    }

    [Fact]
    public void WrongSwitchVtableFailsClosedWithoutChangingReturnOrCallingOriginalTwice()
    {
        var harness = CreateHarness();
        var calls = 0;
        harness.Memory.AddPointer(Node, ImageBase + ExtrasHookSet.EndingLogVtableRva);
        harness.Factory.SetOriginal<GallerySceneSwitchNodeDelegate>(
            HookId.GallerySceneSwitchNode,
            (_, _, _) =>
            {
                calls++;
                harness.ObserveText(0x41, 0x06, "Extras");
                return (nint)Node;
            });
        harness.PrepareAndActivate();

        var returned = harness.Switch();

        Assert.Equal((nint)Node, returned);
        Assert.Equal(1, calls);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void SwitchNeverDiscoversNodeFromSceneFieldAndOnEnterRequiresLaterExactAttachment()
    {
        var harness = CreateHarness();
        var controls = Controls(5);
        ConfigureNode(harness.Memory, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureManagerAndControls(harness.Memory, controls, focusedKey: 4);
        harness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, 0x00EEEEEE)
            .AddInt32(Node + 0x2E4, 1).AddInt32(Node + 0x2EC, 1)
            .AddInt32(Node + 0x2F4, 1).AddInt32(Node + 0x2FC, 1)
            .AddInt32(Node + 0x304, 1);
        ConfigureHubBuild(harness, controls, focusedKey: 4, helpMessage: null, help: null);
        harness.PrepareAndActivate();

        Assert.Equal((nint)Node, harness.Switch());
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);

        harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node);
        harness.HubEnter();

        Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void MismatchedPendingOnEnterCallsOriginalOnceThenFailsWithoutPartialPresentation()
    {
        var harness = CreateHarness();
        var calls = 0;
        ConfigureNode(harness.Memory, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureSwitch(harness, 0, 0x41, 0x06, "Extras");
        harness.Factory.SetOriginal<ExtrasHubOnEnterDelegate>(HookId.ExtrasHubOnEnter, _ => calls++);
        harness.PrepareAndActivate();
        harness.Switch();
        harness.Memory.AddPointer(Node, ImageBase + ExtrasHookSet.EndingLogVtableRva);

        harness.HubEnter();

        Assert.Equal(1, calls);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    [Fact]
    public void DisableDiscardsPendingSwitchAndSuppressesLateOnEnterInstrumentation()
    {
        var harness = CreateHarness();
        var calls = 0;
        ConfigureNode(harness.Memory, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureSwitch(harness, 0, 0x41, 0x06, "Extras");
        harness.Factory.SetOriginal<ExtrasHubOnEnterDelegate>(HookId.ExtrasHubOnEnter, _ => calls++);
        harness.PrepareAndActivate();
        harness.Switch();

        harness.Installer.DisableAll();
        harness.HubEnter();

        Assert.Equal(1, calls);
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Events);
    }

    private static Harness CreateHarness()
    {
        var memory = new TestMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var set = new ExtrasHookSet(factory, memory, dispatcher);
        var boundary = new UnmanagedBoundaryGuard(new RecordingLog(), new RecordingFatal());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        return new(set, memory, dispatcher, factory, boundary, installer);
    }

    private static void ConfigureSwitch(
        Harness harness,
        int action,
        int file,
        int message,
        string title) =>
        harness.Factory.SetOriginal<GallerySceneSwitchNodeDelegate>(
            HookId.GallerySceneSwitchNode,
            (_, actualAction, _) =>
            {
                Assert.Equal(action, actualAction);
                harness.ObserveText(file, message, title);
                return (nint)Node;
            });

    private static void ConfigureHubBuild(
        Harness harness,
        nuint[] controls,
        int focusedKey,
        int? helpMessage,
        string? help)
    {
        ConfigureSwitch(harness, 0, 0x41, 0x06, "Extras");
        harness.Factory.SetOriginal<ExtrasHubOnEnterDelegate>(HookId.ExtrasHubOnEnter, _ =>
        {
            ObserveHubLabelsAndControls(harness, controls);
            Bind(harness, controls, [0, 1, 2, 3, 4]);
            harness.Set.AfterFocusSet((nint)Manager, focusedKey);
            if (helpMessage is not null)
            {
                harness.ObserveText(0x1A, helpMessage.Value, help!);
            }
        });
    }

    private static void ObserveHubLabelsAndControls(Harness harness, nuint[] controls)
    {
        var labels = new (int File, int Message, string Text)[]
        {
            (0x1A, 0x43, "Movies"),
            (0x1A, 0x45, "Illustrations"),
            (0x1A, 0x44, "Sound"),
            (0x1A, 0x0F, "Ending Log"),
            (0x23, 0xD8, "Back"),
        };
        for (var index = 0; index < controls.Length; index++)
        {
            harness.ObserveText(labels[index].File, labels[index].Message, labels[index].Text);
            harness.Set.AfterCustomButtonConstructed((nint)controls[index], (nint)controls[index]);
        }
    }

    private static void Bind(Harness harness, nuint[] controls, int[] keys)
    {
        var states = FocusStates(controls.Length);
        for (var index = 0; index < controls.Length; index++)
        {
            BindOne(harness, states[index], controls[index], keys[index]);
        }
    }

    private static void BindOne(Harness harness, nuint state, nuint control, int key)
    {
        harness.Memory
            .AddPointer(state, ImageBase + ExtrasHookSet.FocusableStateVtableRva)
            .AddPointer(state + ExtrasHookSet.FocusableStateControlOffset, control);
        harness.Set.AfterControlBound((nint)Manager, (nint)state, key);
    }

    private static void ConfigureNode(TestMemory memory, uint vtableRva) =>
        memory.AddPointer(Node, ImageBase + vtableRva);

    private static void ConfigureManagerAndControls(TestMemory memory, nuint[] controls, int focusedKey)
    {
        memory
            .AddPointer(Manager, ImageBase + ExtrasHookSet.ManagerVtableRva)
            .AddInt32(Manager + ExtrasHookSet.ManagerFocusKeyOffset, focusedKey);
        foreach (var control in controls)
        {
            memory.AddPointer(control, ImageBase + ExtrasHookSet.CustomButtonVtableRva);
        }
    }

    private static void ConfigureHubClosure(TestMemory memory, nuint[] controls)
    {
        memory
            .AddPointer(Closure, Manager)
            .AddPointer(Closure + 4, Node)
            .AddPointer(Closure + 8, ControlVector);
        for (var index = 0; index < controls.Length; index++)
        {
            memory.AddPointer(ControlVector + (nuint)(index * 4), controls[index]);
        }
    }

    private static nuint[] Controls(int count) =>
        Enumerable.Range(0, count).Select(index => 0x00A00000u + (nuint)(index * 0x1000)).ToArray();

    private static nuint[] FocusStates(int count) =>
        Enumerable.Range(0, count).Select(index => 0x00C00000u + (nuint)(index * 0x1000)).ToArray();

    private static VerifiedBuild CreateBuild() => new(
        ImageBase,
        ExpectedHookIds.ToDictionary(id => id, id => ImageBase + GameVersionCatalog.Get(id).Rva));

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed record Harness(
        ExtrasHookSet Set,
        TestMemory Memory,
        RecordingDispatcher Dispatcher,
        RecordingHookFactory Factory,
        UnmanagedBoundaryGuard Boundary,
        ReloadedHookInstaller Installer)
    {
        private int nextTextIndex;

        public void PrepareAndActivate()
        {
            Installer.PrepareAll(CreateBuild(), Boundary);
            Installer.ActivateAll();
        }

        public void ObserveText(int file, int message, string text)
        {
            var address = 0x00D00000u + (nuint)(nextTextIndex++ * 0x100);
            Memory.AddString(address, text);
            Set.AfterTextManagerGetMsg(0x1111, (nint)address, file, message, (nint)address);
        }

        public nint Switch(int action = 0, uint raw = 0) =>
            Factory.GetDetour<GallerySceneSwitchNodeDelegate>(HookId.GallerySceneSwitchNode)((nint)Scene, action, raw);
        public void HubEnter() => Factory.GetDetour<ExtrasHubOnEnterDelegate>(HookId.ExtrasHubOnEnter)((nint)Node);
        public void LogEnter() => Factory.GetDetour<EndingLogOnEnterDelegate>(HookId.EndingLogOnEnter)((nint)Node);
        public void DetailEnter() => Factory.GetDetour<EndingDetailOnEnterDelegate>(HookId.EndingDetailOnEnter)((nint)Node);
        public void HubCallback(int eventType, int action) =>
            Factory.GetDetour<ExtrasHubCallbackDelegate>(HookId.ExtrasHubCallback)((nint)Closure, eventType, action);
        public void Exit() => Factory.GetDetour<ExtrasNodeOnExitDelegate>(HookId.ExtrasNodeOnExit)((nint)Node);
        public nint HubDelete() => Factory.GetDetour<ExtrasHubDeletingDestructorDelegate>(
            HookId.ExtrasHubDeletingDestructor)((nint)Node, 1);
    }

    private sealed class RecordingHookFactory : IRuntimeNativeHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals = new()
        {
            [HookId.GallerySceneSwitchNode] = (GallerySceneSwitchNodeDelegate)((_, _, _) => (nint)Node),
            [HookId.ExtrasHubOnEnter] = (ExtrasHubOnEnterDelegate)(_ => { }),
            [HookId.EndingLogOnEnter] = (EndingLogOnEnterDelegate)(_ => { }),
            [HookId.EndingDetailOnEnter] = (EndingDetailOnEnterDelegate)(_ => { }),
            [HookId.ExtrasNodeOnExit] = (ExtrasNodeOnExitDelegate)(_ => { }),
            [HookId.ExtrasHubDeletingDestructor] = (ExtrasHubDeletingDestructorDelegate)((node, _) => node),
            [HookId.EndingLogDeletingDestructor] = (EndingLogDeletingDestructorDelegate)((node, _) => node),
            [HookId.ExtrasHubCallback] = (ExtrasHubCallbackDelegate)((_, _, _) => { }),
            [HookId.EndingLogCallback] = (EndingLogCallbackDelegate)((_, _, _) => { }),
            [HookId.EndingDetailCallback] = (EndingDetailCallbackDelegate)((_, _, _) => { }),
        };
        private readonly Dictionary<HookId, Delegate> detours = [];
        public List<(HookId Id, nuint Address)> Created { get; } = [];

        public void SetOriginal<TDelegate>(HookId id, TDelegate original) where TDelegate : Delegate =>
            originals[id] = original;
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
        public bool ThrowOnPublish { get; set; }
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent accessibilityEvent)
        {
            if (ThrowOnPublish)
            {
                throw new InvalidOperationException("simulated teardown publication failure");
            }
            Events.Add(accessibilityEvent);
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
