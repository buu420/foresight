using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.Startup;
using ChronoTriggerAccessibility.Mod.TopMenu;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Hooks;
using ChronoTriggerAccessibility.Native.Memory;
using Reloaded.Hooks.Definitions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.TopMenu;

public sealed class TopMenuHookSetTests
{
    private const nuint ImageBase = 0x00400000;
    private const nuint Root = 0x00900000;
    private const nuint Manager = 0x00910000;
    private const nuint StatusBar = 0x00920000;

    [Fact]
    public void RegistersEveryDedicatedFunctionAndExactCallSiteInertlyBeforeActivation()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);

        harness.Installer.PrepareAll(harness.Build, harness.Boundary);

        Assert.Equal(38, harness.Set.RequiredHookIds.Count);
        Assert.Equal(harness.Set.RequiredHookIds, harness.Factory.Created.Select(item => item.Id));
        Assert.Equal(10, harness.Factory.FunctionDetours.Count);
        Assert.Equal(28, harness.Factory.Probes.Count);
        Assert.All(harness.Installer.PreparedHooks, hook => Assert.False(hook.IsActive));
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void ClassicBuilderUsesStrictMarkersAndNativeKeyBindingsThenPublishesCompleteSnapshot()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();

        harness.BuildMenu();

        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal("Menu", presented.Title);
        Assert.Equal("Settings", presented.Focus!.Label);
        Assert.Equal(5, presented.Focus.Position);
        Assert.Equal(["Party status", "12:34", "12345 G"], presented.StatusDetails);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuBuilder]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.StatusBarFormatScope]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.StatusBarGlyphRenderer]);
        Assert.Equal(7, harness.Capture.Session!.Constructed.Count);
        Assert.Equal(7, harness.Capture.Session.Bindings.Count);
        Assert.Single(harness.Capture.Session.Utf8Returns, ImageBase + 0x1D0AAA);
        Assert.Single(harness.Capture.Session.Status!.RenderedReturns, ImageBase + 0x22F3BD);
        Assert.True(harness.Capture.Session.Status.Completed);
        Assert.True(harness.Capture.Session.Status.Disposed);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void MissingLabelMarkerFailsClosedAfterCallingEveryNativeOriginalOnce()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness, omitLabelMarker: true);
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Contains("marker", harness.Dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(harness.Dispatcher.Diagnostics, line =>
            line.Contains("Top-menu unmarked label") && line.Contains("style=Classic") &&
            line.Contains("text=\"12:34\""));
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuBuilder]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
    }

    [Fact]
    public void MissingLabelDiagnosticFailureCannotSkipTheNativeCallOrItsCoverageError()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness, omitLabelMarker: true);
        harness.Dispatcher.ThrowOnDiagnostic = true;
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Contains("marker", harness.Dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
        Assert.False(harness.Boundary.IsFaulted);
    }

    [Fact]
    public void ClassicBuilderAccountsForHiddenStatusLabelAndAllRenderedFooterLines()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness, withAdditionalLabels: true);
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join("; ", harness.Dispatcher.Failures));
        Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(5, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
        Assert.Equal(new nuint[] { ImageBase + 0x1D0AAA, ImageBase + 0x1D0DBC,
            ImageBase + 0x1D0E05, ImageBase + 0x1D0ED3 }, harness.Capture.Session!.Utf8Returns);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Native caption")]
    public void TouchBuilderAccountsForItsOptionalInitialStatusCaption(string caption)
    {
        var harness = CreateHarness(TopMenuStyle.Touch);
        ConfigureSuccessfulBuilder(harness, initialCaption: caption);
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal(caption.Length == 0 ? 1 : 2, harness.Capture.Session!.Utf8Returns.Count);
    }

    [Theory]
    [InlineData(TopMenuStyle.Classic)]
    [InlineData(TopMenuStyle.Touch)]
    public void EmptyStatusLineAllocationsPreserveTheRenderedLinesWithoutAddingBlankSpeech(TopMenuStyle style)
    {
        var harness = CreateHarness(style);
        ConfigureSuccessfulBuilder(harness);
        harness.Factory.SetOriginal<StatusBarFormatScopeDelegate>(HookId.StatusBarFormatScope, (_, _, _) =>
        {
            for (var line = 0; line < 2; line++)
            {
                harness.Probe(HookId.StatusBarEmptyLineLabelCallSite);
                harness.RenderLabel("");
                harness.Probe(HookId.StatusBarGlyphRendererCallSite);
                harness.RenderStatus($"Line {line + 1}");
            }
        });
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Diagnostics);
        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(["Party status", "12:34", "12345 G"], presented.StatusDetails);
        Assert.Single(harness.Capture.Session!.Utf8Returns);
        Assert.Equal(2, harness.Capture.Session.Status!.RenderedReturns.Count);
        Assert.True(harness.Capture.Session.Status.Completed);
        Assert.Equal(3, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Unexpected")]
    public void EmptyStatusLineMarkerCannotSilenceNonemptyNativeText(string text)
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness, allocatedStatusLabel: text);
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains("must be empty", Assert.Single(harness.Dispatcher.Failures));
        Assert.Equal(2, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
    }

    [Fact]
    public void EmptyStatusLineMarkerIsRejectedOutsideTheOwnedFormatter()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness, labelProbe: HookId.StatusBarEmptyLineLabelCallSite);
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains("outside its owned formatting scope", Assert.Single(harness.Dispatcher.Failures));
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
    }

    [Fact]
    public void StatusLineAllocationOutsideAnyTopMenuBuildPassesThroughWithoutAStaleMarker()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();

        harness.Probe(HookId.StatusBarEmptyLineLabelCallSite);
        harness.RenderLabel("Other screen");
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Empty(harness.Dispatcher.Diagnostics);

        harness.BuildMenu();

        Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal(2, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
    }

    [Fact]
    public void ChildMenuSpeechIsNotOverwrittenByTheOldUnsupportedAnnouncement()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        var childPresented = false;
        harness.Set.SubmenuOwnsSpeech = () => childPresented;
        harness.Factory.SetOriginal<TopMenuActionDispatcherDelegate>(HookId.ClassicTopMenuActionDispatcher, _ =>
        {
            childPresented = true;
            harness.Dispatcher.Publish(new MenuContentPresented(new("field-submenu", 42), "Save", "File 1. Empty"));
        });
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();
        harness.DispatchAction(0);
        Assert.IsType<MenuContentPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void FocusAndUnsupportedDispatcherUseNativeKeysRatherThanVisualPositions()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.Memory.AddInt32(Manager + TopMenuHookSet.ManagerFocusKeyOffset, 0);
        harness.Set.AfterFocusSet((nint)Manager, 0);
        harness.DispatchAction(0);

        var focus = Assert.IsType<MenuFocusChanged>(harness.Dispatcher.Events[0]);
        Assert.Equal("Save", focus.Focus!.Label);
        Assert.Equal(7, focus.Focus.Position);
        var unsupported = Assert.IsType<MenuUnsupported>(harness.Dispatcher.Events[1]);
        Assert.Equal("Save", unsupported.SelectedLabel);
        Assert.Equal("This top-menu subpage is not accessible yet.", unsupported.BoundaryText);
        Assert.Equal("Press Cancel to return to the accessible top menu.", unsupported.ReturnInstruction);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Theory]
    [InlineData(0, "Save")]
    [InlineData(1, "Equipment")]
    [InlineData(2, "Load")]
    [InlineData(3, "Items")]
    [InlineData(6, "Formation")]
    public void EveryEnabledDeepPageActionPublishesItsExactVisibleLabelAndExplicitReturnBoundary(
        int action,
        string expectedLabel)
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();
        harness.SetActionEnabled(action, enabled: true);

        harness.DispatchAction(action);

        var unsupported = Assert.IsType<MenuUnsupported>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(expectedLabel, unsupported.SelectedLabel);
        Assert.Equal("This top-menu subpage is not accessible yet.", unsupported.BoundaryText);
        Assert.Equal("Press Cancel to return to the accessible top menu.", unsupported.ReturnInstruction);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void ClassicBookmarkLeavesSpeechToTheSaveNodesOwnConfirmation()
    {
        // 0x2A8630 case 5 opens the save node in mode 2, which raises its Yes/No confirmation
        // inside the dispatch; nothing may follow it and cut the prompt off.
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();
        harness.SetActionEnabled(TopMenuHookSet.ClassicBookmarkAction, enabled: true);

        harness.DispatchAction(TopMenuHookSet.ClassicBookmarkAction);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void TouchBookmarkStillStatesItsSubpageIsNotAccessible()
    {
        // The Touch save node 0x213AB0 builds its own confirmation, which nothing reads yet.
        var harness = CreateHarness(TopMenuStyle.Touch);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();
        harness.SetActionEnabled(TopMenuHookSet.ClassicBookmarkAction, enabled: true);

        harness.DispatchAction(TopMenuHookSet.ClassicBookmarkAction);

        var unsupported = Assert.IsType<MenuUnsupported>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal("This top-menu subpage is not accessible yet.", unsupported.BoundaryText);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.TouchTopMenuActionDispatcher]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void SettingsActivationPublishesOnlyAfterItsClassicNativeOriginalSucceeds()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        var originalSawNoActionEvent = false;
        harness.Factory.SetOriginal<TopMenuActionDispatcherDelegate>(
            HookId.ClassicTopMenuActionDispatcher,
            _ => originalSawNoActionEvent = harness.Dispatcher.Events.Count == 0);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.DispatchAction(4);

        Assert.True(originalSawNoActionEvent);
        Assert.Equal("Settings", Assert.IsType<MenuActivated>(Assert.Single(harness.Dispatcher.Events)).Label);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void TouchDispatcherUsesParentPlus294AndPublishesOnlyAfterItsNativeOriginalSucceeds()
    {
        var harness = CreateHarness(TopMenuStyle.Touch);
        ConfigureSuccessfulBuilder(harness);
        var originalSawNoActionEvent = false;
        harness.Factory.SetOriginal<TopMenuActionDispatcherDelegate>(
            HookId.TouchTopMenuActionDispatcher,
            _ => originalSawNoActionEvent = harness.Dispatcher.Events.Count == 0);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.DispatchAction(4);

        Assert.True(originalSawNoActionEvent);
        Assert.Equal("Settings", Assert.IsType<MenuActivated>(Assert.Single(harness.Dispatcher.Events)).Label);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.TouchTopMenuActionDispatcher]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void ActionOriginalFailureNeverPublishesTheCapturedAction()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.Factory.SetOriginal<TopMenuActionDispatcherDelegate>(
            HookId.ClassicTopMenuActionDispatcher,
            _ =>
            {
                harness.DeleteClassic();
                throw new InvalidOperationException("simulated action failure");
            });
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.DispatchAction(4);

        Assert.True(harness.Boundary.IsFaulted);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Contains("native top-menu action dispatcher failed", harness.Dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuDeletingDestructor]);
    }

    [Fact]
    public void DisableDuringActionOriginalInvalidatesTheTransactionWithoutPublishing()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.Factory.SetOriginal<TopMenuActionDispatcherDelegate>(
            HookId.ClassicTopMenuActionDispatcher,
            _ =>
            {
                harness.DeleteStatusBar();
                harness.Installer.DisableAll();
            });
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.DispatchAction(4);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.StatusBarDestructor]);
    }

    [Fact]
    public void ReentrantRootAndStatusTeardownPublishesUnsupportedThenOneDeferredExit()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        var originalsSawNoEvent = false;
        harness.Factory.SetOriginal<TopMenuActionDispatcherDelegate>(
            HookId.ClassicTopMenuActionDispatcher,
            _ =>
            {
                harness.DeleteStatusBar();
                harness.DeleteClassic();
                originalsSawNoEvent = harness.Dispatcher.Events.Count == 0;
            });
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.DispatchAction(0);
        harness.DeleteStatusBar();
        harness.DeleteClassic();

        Assert.True(originalsSawNoEvent);
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.Equal("Save", Assert.IsType<MenuUnsupported>(item).SelectedLabel),
            item => Assert.IsType<MenuExited>(item));
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Equal(2, harness.Factory.OriginalCalls[HookId.StatusBarDestructor]);
        Assert.Equal(2, harness.Factory.OriginalCalls[HookId.ClassicTopMenuDeletingDestructor]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void ReturnActionDefersItsReentrantExitWithoutInventingAnActivation()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        var originalSawNoEvent = false;
        harness.Factory.SetOriginal<TopMenuActionDispatcherDelegate>(
            HookId.ClassicTopMenuActionDispatcher,
            _ =>
            {
                harness.DeleteClassic();
                originalSawNoEvent = harness.Dispatcher.Events.Count == 0;
            });
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.DispatchAction(7);

        Assert.True(originalSawNoEvent);
        Assert.IsType<MenuExited>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuDeletingDestructor]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryTeardownPathCarriesTheSameOwnerAsThePublishedPresentation(bool viaStatusBar)
    {
        // The narrator only honours a close from the owner that presented, so each teardown
        // path must stamp the identity of its own active context.
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));

        if (viaStatusBar)
        {
            harness.DeleteStatusBar();
        }
        else
        {
            harness.DeleteClassic();
        }

        var exited = Assert.IsType<MenuExited>(harness.Dispatcher.Events[1]);
        Assert.NotNull(presented.Owner);
        Assert.Equal(presented.Owner, exited.Owner);
        Assert.Equal("TopMenu", exited.Owner!.Source);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void DeferredActionExitCarriesTheOwnerOfTheMenuThatWasTornDown()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.Factory.SetOriginal<TopMenuActionDispatcherDelegate>(
            HookId.ClassicTopMenuActionDispatcher,
            _ => harness.DeleteClassic());
        harness.PrepareAndActivate();
        harness.BuildMenu();
        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        harness.Dispatcher.Events.Clear();

        harness.DispatchAction(7);

        var exited = Assert.IsType<MenuExited>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(presented.Owner, exited.Owner);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void IndependentStatusBarDestructorImmediatelyInvalidatesThePublishedMenuAndExitsOnce()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        var originalSawCleared = false;
        harness.Factory.SetOriginal<StatusBarDestructorDelegate>(
            HookId.StatusBarDestructor,
            _ =>
            {
                var eventCount = harness.Dispatcher.Events.Count;
                harness.Set.AfterFocusSet((nint)Manager, 4);
                originalSawCleared = harness.Dispatcher.Events.Count == eventCount;
            });
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.DeleteStatusBar();
        harness.DeleteStatusBar();
        harness.Set.AfterFocusSet((nint)Manager, 4);

        Assert.True(originalSawCleared);
        Assert.IsType<MenuExited>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(2, harness.Factory.OriginalCalls[HookId.StatusBarDestructor]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void RootDestructorClearsBeforeOriginalAndPublishesExitExactlyOnce()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        var originalSawCleared = false;
        harness.Factory.SetOriginal<ClassicTopMenuDeletingDestructorDelegate>(
            HookId.ClassicTopMenuDeletingDestructor,
            (root, _) =>
            {
                var eventCount = harness.Dispatcher.Events.Count;
                harness.Set.AfterFocusSet((nint)Manager, 4);
                originalSawCleared = harness.Dispatcher.Events.Count == eventCount;
                return root;
            });
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        var returned = harness.DeleteClassic();
        harness.DeleteClassic();

        Assert.Equal((nint)Root, returned);
        Assert.True(originalSawCleared);
        Assert.IsType<MenuExited>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(2, harness.Factory.OriginalCalls[HookId.ClassicTopMenuDeletingDestructor]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void DisableClearsStateAndLateCallbacksOnlyCallNativeOriginals()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.Installer.DisableAll();
        harness.DispatchAction(0);
        harness.Set.AfterFocusSet((nint)Manager, 0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Empty(harness.Dispatcher.Failures);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
    }

    [Fact]
    public void TouchBuilderUsesItsOwnMarkersAndRootDestructorWithoutClassCallbackInference()
    {
        var harness = CreateHarness(TopMenuStyle.Touch);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();

        harness.BuildMenu();
        var returned = harness.DeleteTouch(Root);

        Assert.Equal((nint)Root, returned);
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.IsType<MenuPresented>(item),
            item => Assert.IsType<MenuExited>(item));
        Assert.Single(harness.Capture.Session!.Utf8Returns, ImageBase + 0x221A4F);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.TouchTopMenuBuilder]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.TouchTopMenuDeletingDestructor]);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void UnreachableTouchReserveNameProbeIsDetectedAsCoverageDrift()
    {
        var harness = CreateHarness(TopMenuStyle.Touch);
        ConfigureSuccessfulBuilder(harness, labelProbe: HookId.TouchTopMenuReserveNameLabelCallSite);
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Contains("unreachable", harness.Dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.TouchTopMenuBuilder]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
    }

    [Fact]
    public void MissingRendererMarkerFailsClosedWithoutSkippingRendererOrFormatterOriginal()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness, omitRendererMarker: true);
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Contains("marker", harness.Dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.StatusBarFormatScope]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.StatusBarGlyphRenderer]);
    }

    [Fact]
    public void DisabledActionFailsClosedButStillCallsDispatcherOriginalOnce()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();
        harness.BuildMenu();
        harness.Dispatcher.Events.Clear();

        harness.DispatchAction(2);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Contains("enabled visible", harness.Dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuActionDispatcher]);
    }

    [Fact]
    public void BuilderOriginalFailureCannotPublishPartialMenuAndIsContainedByBoundary()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        harness.Factory.SetOriginal<ClassicTopMenuBuilderDelegate>(
            HookId.ClassicTopMenuBuilder,
            (_, _) => throw new InvalidOperationException("simulated native-wrapper failure"));
        harness.PrepareAndActivate();

        harness.BuildMenu();

        Assert.True(harness.Boundary.IsFaulted);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuBuilder]);
    }

    [Fact]
    public void CaptureCallbackFailureAfterLabelOriginalFailsClosedWithoutChangingNativeReturnPath()
    {
        var harness = CreateHarness(TopMenuStyle.Classic);
        ConfigureSuccessfulBuilder(harness);
        harness.PrepareAndActivate();
        harness.Capture.ThrowOnUtf8 = true;

        harness.BuildMenu();

        Assert.False(harness.Boundary.IsFaulted);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Single(harness.Dispatcher.Failures);
        Assert.Contains("capture failed", harness.Dispatcher.Failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.MenuTextLabelFactory]);
        Assert.Equal(1, harness.Factory.OriginalCalls[HookId.ClassicTopMenuBuilder]);
    }

    private static Harness CreateHarness(TopMenuStyle style)
    {
        var memory = new TestMemory();
        var dispatcher = new RecordingDispatcher();
        var factory = new RecordingHookFactory();
        var controls = Enumerable.Range(0, 7).Select(index => 0x00A00000u + (nuint)(index * 0x1000)).ToArray();
        var keys = new[] { 3, 5, 1, 6, 4, 2, 0 };
        var labels = new[] { "Items", "Tech", "Equipment", "Formation", "Settings", "Load", "Save" };
        var snapshotControls = labels.Select((label, index) => new MenuControlSnapshot(
            label, null, null, keys[index], index + 1, 7, index != 5, true)).ToArray();
        var capture = new RecordingCaptureFactory(new TopMenuSnapshot(
            style,
            snapshotControls,
            focusedKey: 4,
            "12:34",
            "12345 G",
            [new TopMenuMemberSnapshot("Crono", TopMenuMemberKind.Active,
                [new TopMenuStatRowSnapshot("LV", ["12"], null)])],
            ["Party status"],
            ["Party status", "12:34", "12345 G"]));
        var set = new TopMenuHookSet(factory, factory, memory, dispatcher, capture);
        var boundary = new UnmanagedBoundaryGuard(new RecordingLog(), new RecordingFatal());
        var installer = new ReloadedHookInstaller(set.Registrations, [set]);
        var addresses = set.RequiredHookIds.ToDictionary(id => id, id => ImageBase + GameVersionCatalog.Get(id).Rva);
        var build = new VerifiedBuild(ImageBase, addresses);

        memory
            .AddPointer(Root, ImageBase + (style == TopMenuStyle.Classic
                ? TopMenuCaptureScope.ClassicRootVtableRva
                : TopMenuCaptureScope.TouchRootVtableRva))
            .AddPointer(Manager, ImageBase + TopMenuHookSet.ManagerVtableRva)
            .AddInt32(Manager + TopMenuHookSet.ManagerFocusKeyOffset, 4)
            .AddPointer(StatusBar, ImageBase + TopMenuCaptureScope.StatusBarVtableRva);
        for (var index = 0; index < controls.Length; index++)
        {
            memory
                .AddPointer(controls[index], ImageBase + TopMenuHookSet.CustomButtonVtableRva)
                .AddByte(controls[index] + TopMenuHookSet.WidgetVisibleOffset, 1)
                .AddByte(controls[index] + TopMenuHookSet.WidgetEnabledOffset, index == 5 ? (byte)0 : (byte)1);
        }

        return new(set, memory, dispatcher, factory, capture, boundary, installer, build, controls, keys, style);
    }

    private static void ConfigureSuccessfulBuilder(
        Harness harness,
        bool omitLabelMarker = false,
        bool omitRendererMarker = false,
        HookId? labelProbe = null,
        bool withAdditionalLabels = false,
        string? initialCaption = null,
        string? allocatedStatusLabel = null)
    {
        void Body()
        {
            if (initialCaption is not null)
            {
                harness.Probe(HookId.TouchStatusBarInitialLabelCallSite);
                harness.RenderLabel(initialCaption);
            }
            if (withAdditionalLabels)
            {
                harness.Probe(HookId.StatusBarHiddenLabelCallSite);
                harness.RenderLabel("Hidden value");
            }
            ObserveControlsAndBindings(harness);
            if (!omitLabelMarker)
            {
                harness.Probe(labelProbe ?? (harness.Style == TopMenuStyle.Classic
                    ? HookId.ClassicTopMenuTimeLabelCallSite
                    : HookId.TouchTopMenuTimeLabelCallSite));
            }
            harness.RenderLabel("12:34");
            harness.FormatStatus();
            if (withAdditionalLabels)
            {
                harness.Probe(HookId.ClassicTopMenuFirstFooterLabelCallSite);
                harness.RenderLabel("First line");
                harness.Probe(HookId.ClassicTopMenuSecondFooterLabelCallSite);
                harness.RenderLabel("Second line");
                harness.Probe(HookId.ClassicTopMenuContextLabelCallSite);
                harness.RenderLabel("Context line");
            }
        }
        if (harness.Style == TopMenuStyle.Classic)
        {
            harness.Factory.SetOriginal<ClassicTopMenuBuilderDelegate>(HookId.ClassicTopMenuBuilder, (_, _) => Body());
        }
        else
        {
            harness.Factory.SetOriginal<TouchTopMenuBuilderDelegate>(HookId.TouchTopMenuBuilder, (_, _) => Body());
        }
        harness.Factory.SetOriginal<StatusBarFormatScopeDelegate>(HookId.StatusBarFormatScope, (_, _, _) =>
        {
            if (allocatedStatusLabel is not null)
            {
                harness.Probe(HookId.StatusBarEmptyLineLabelCallSite);
                harness.RenderLabel(allocatedStatusLabel);
            }
            if (!omitRendererMarker)
            {
                harness.Probe(HookId.StatusBarGlyphRendererCallSite);
            }
            harness.RenderStatus("Party status");
        });
    }

    private static void ObserveControlsAndBindings(Harness harness)
    {
        for (var index = 0; index < harness.Controls.Length; index++)
        {
            var control = harness.Controls[index];
            var state = 0x00B00000u + (nuint)(index * 0x1000);
            harness.Set.AfterCustomButtonConstructed((nint)control, (nint)control);
            harness.Memory
                .AddPointer(state, ImageBase + TopMenuHookSet.FocusableStateVtableRva)
                .AddPointer(state + TopMenuHookSet.FocusableStateControlOffset, control);
            harness.Set.AfterControlBound((nint)Manager, (nint)state, harness.Keys[index]);
        }
        harness.Set.AfterFocusSet((nint)Manager, 4);
    }

    private sealed record Harness(
        TopMenuHookSet Set,
        TestMemory Memory,
        RecordingDispatcher Dispatcher,
        RecordingHookFactory Factory,
        RecordingCaptureFactory Capture,
        UnmanagedBoundaryGuard Boundary,
        ReloadedHookInstaller Installer,
        VerifiedBuild Build,
        nuint[] Controls,
        int[] Keys,
        TopMenuStyle Style)
    {
        private int nextString;

        public void PrepareAndActivate()
        {
            Installer.PrepareAll(Build, Boundary);
            Installer.ActivateAll();
        }

        public void BuildMenu()
        {
            if (Style == TopMenuStyle.Classic)
            {
                Factory.GetDetour<ClassicTopMenuBuilderDelegate>(HookId.ClassicTopMenuBuilder)((nint)Root, 0);
            }
            else
            {
                Factory.GetDetour<TouchTopMenuBuilderDelegate>(HookId.TouchTopMenuBuilder)((nint)Root, 0);
            }
        }

        public void Probe(HookId id) => Factory.GetProbe(id)();

        public nint RenderLabel(string value)
        {
            var address = 0x00D00000u + (nuint)(nextString++ * 0x100);
            Memory.AddString(address, value);
            return Factory.GetDetour<MenuTextLabelFactoryDelegate>(HookId.MenuTextLabelFactory)(0, (nint)address, 0, 12);
        }

        public void FormatStatus() => Factory.GetDetour<StatusBarFormatScopeDelegate>(
            HookId.StatusBarFormatScope)((nint)StatusBar, 0, 0);

        public nint RenderStatus(string value)
        {
            var address = 0x00E00000u + (nuint)(nextString++ * 0x100);
            Memory.AddWideString(address, value);
            return Factory.GetDetour<StatusBarGlyphRendererDelegate>(HookId.StatusBarGlyphRenderer)(1, (nint)address, 2);
        }

        public void DispatchAction(int action)
        {
            const nuint context = 0x00F00000;
            const nuint parent = 0x00F01000;
            Memory
                .AddInt32(context, action)
                .AddPointer(context + 4, parent)
                .AddPointer(parent + (Style == TopMenuStyle.Classic
                    ? TopMenuHookSet.ClassicParentTopMenuOffset
                    : TopMenuHookSet.TouchParentTopMenuOffset), Root);
            Factory.GetDetour<TopMenuActionDispatcherDelegate>(Style == TopMenuStyle.Classic
                ? HookId.ClassicTopMenuActionDispatcher
                : HookId.TouchTopMenuActionDispatcher)((nint)context);
        }

        public void SetActionEnabled(int action, bool enabled)
        {
            var index = Array.IndexOf(Keys, action);
            Assert.InRange(index, 0, Controls.Length - 1);
            Memory.AddByte(
                Controls[index] + TopMenuHookSet.WidgetEnabledOffset,
                enabled ? (byte)1 : (byte)0);
        }

        public void DeleteStatusBar() => Factory.GetDetour<StatusBarDestructorDelegate>(
            HookId.StatusBarDestructor)((nint)StatusBar);

        public nint DeleteClassic() => Factory.GetDetour<ClassicTopMenuDeletingDestructorDelegate>(
            HookId.ClassicTopMenuDeletingDestructor)((nint)Root, 1);

        public nint DeleteTouch(nuint node) => Factory.GetDetour<TouchTopMenuDeletingDestructorDelegate>(
            HookId.TouchTopMenuDeletingDestructor)((nint)node, 1);
    }

    private sealed record VerifiedBuild(
        nuint ImageBaseAddress,
        IReadOnlyDictionary<HookId, nuint> HookAddresses) : IVerifiedGameBuild;

    private sealed class RecordingCaptureFactory(TopMenuSnapshot snapshot) : ITopMenuCaptureFactory
    {
        public RecordingCapture? Session { get; private set; }
        public bool ThrowOnUtf8 { get; set; }

        public bool TryBegin(
            IReadableMemory memory,
            nuint imageBase,
            nuint root,
            TopMenuStyle style,
            out ITopMenuCapture capture,
            out string diagnostic)
        {
            _ = memory;
            Session = new RecordingCapture(snapshot, imageBase, root, style);
            Session.ThrowOnUtf8 = ThrowOnUtf8;
            capture = Session;
            diagnostic = string.Empty;
            return true;
        }
    }

    private sealed class RecordingCapture(
        TopMenuSnapshot snapshot,
        nuint imageBase,
        nuint root,
        TopMenuStyle style) : ITopMenuCapture
    {
        public List<(nuint Control, int Position, bool Enabled, bool Visible)> Constructed { get; } = [];
        public List<(nuint Manager, nuint Control, int Key)> Bindings { get; } = [];
        public List<nuint> Utf8Returns { get; } = [];
        public RecordingStatusCapture? Status { get; private set; }
        public bool ThrowOnUtf8 { get; set; }

        public bool TryRecordUtf8(nuint absoluteReturnAddress, string text, out string diagnostic)
        {
            if (ThrowOnUtf8)
            {
                throw new InvalidOperationException("simulated UTF-8 capture failure");
            }
            Utf8Returns.Add(absoluteReturnAddress);
            diagnostic = string.Empty;
            return true;
        }

        public bool TryRecordConstructedControl(nuint control, int position, bool enabled, bool visible, out string diagnostic)
        {
            Constructed.Add((control, position, enabled, visible));
            diagnostic = string.Empty;
            return true;
        }

        public bool TryRecordManagerKeyBinding(nuint manager, nuint control, int key, out string diagnostic)
        {
            Bindings.Add((manager, control, key));
            diagnostic = string.Empty;
            return true;
        }

        public bool TryBeginStatusBar(nuint statusBar, out ITopMenuStatusCapture status, out string diagnostic)
        {
            Status = new RecordingStatusCapture();
            status = Status;
            diagnostic = string.Empty;
            return true;
        }

        public bool TryCreateSnapshot(out TopMenuSnapshot captured, out string diagnostic)
        {
            Assert.Equal(snapshot.Style, style);
            Assert.Equal(ImageBase, imageBase);
            Assert.Equal(Root, root);
            captured = snapshot;
            diagnostic = string.Empty;
            return true;
        }

        public void Dispose() { }
    }

    private sealed class RecordingStatusCapture : ITopMenuStatusCapture
    {
        public List<nuint> RenderedReturns { get; } = [];
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }

        public bool TryRecordRenderedLine(nuint absoluteReturnAddress, nuint wideStringAddress, out string diagnostic)
        {
            _ = wideStringAddress;
            RenderedReturns.Add(absoluteReturnAddress);
            diagnostic = string.Empty;
            return true;
        }

        public bool TryComplete(out string diagnostic)
        {
            Completed = true;
            diagnostic = string.Empty;
            return true;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingHookFactory : IRuntimeNativeHookFactory, IRuntimeNativeAsmHookFactory
    {
        private readonly Dictionary<HookId, Delegate> originals = [];
        public Dictionary<HookId, Delegate> FunctionDetours { get; } = [];
        public Dictionary<HookId, NativeCallSiteProbeDelegate> Probes { get; } = [];
        public Dictionary<HookId, int> OriginalCalls { get; } = [];
        public List<(HookId Id, nuint Address)> Created { get; } = [];

        public RecordingHookFactory()
        {
            SetOriginal<ClassicTopMenuBuilderDelegate>(HookId.ClassicTopMenuBuilder, (_, _) => { });
            SetOriginal<TouchTopMenuBuilderDelegate>(HookId.TouchTopMenuBuilder, (_, _) => { });
            SetOriginal<MenuTextLabelFactoryDelegate>(HookId.MenuTextLabelFactory, (_, _, _, _) => 1);
            SetOriginal<StatusBarFormatScopeDelegate>(HookId.StatusBarFormatScope, (_, _, _) => { });
            SetOriginal<StatusBarGlyphRendererDelegate>(HookId.StatusBarGlyphRenderer, (output, _, _) => output);
            SetOriginal<StatusBarDestructorDelegate>(HookId.StatusBarDestructor, _ => { });
            SetOriginal<ClassicTopMenuDeletingDestructorDelegate>(HookId.ClassicTopMenuDeletingDestructor, (node, _) => node);
            SetOriginal<TouchTopMenuDeletingDestructorDelegate>(HookId.TouchTopMenuDeletingDestructor, (node, _) => node);
            SetOriginal<TopMenuActionDispatcherDelegate>(HookId.ClassicTopMenuActionDispatcher, _ => { });
            SetOriginal<TopMenuActionDispatcherDelegate>(HookId.TouchTopMenuActionDispatcher, _ => { });
        }

        public void SetOriginal<TDelegate>(HookId id, TDelegate original) where TDelegate : Delegate
        {
            originals[id] = WrapOriginal(id, original);
            OriginalCalls[id] = 0;
        }

        private TDelegate WrapOriginal<TDelegate>(HookId id, TDelegate original) where TDelegate : Delegate
        {
            if (typeof(TDelegate) == typeof(ClassicTopMenuBuilderDelegate))
            {
                ClassicTopMenuBuilderDelegate wrapped = (a, b) => { OriginalCalls[id]++; ((ClassicTopMenuBuilderDelegate)(Delegate)original)(a, b); };
                return (TDelegate)(Delegate)wrapped;
            }
            if (typeof(TDelegate) == typeof(TouchTopMenuBuilderDelegate))
            {
                TouchTopMenuBuilderDelegate wrapped = (a, b) => { OriginalCalls[id]++; ((TouchTopMenuBuilderDelegate)(Delegate)original)(a, b); };
                return (TDelegate)(Delegate)wrapped;
            }
            if (typeof(TDelegate) == typeof(MenuTextLabelFactoryDelegate))
            {
                MenuTextLabelFactoryDelegate wrapped = (a, b, c, d) => { OriginalCalls[id]++; return ((MenuTextLabelFactoryDelegate)(Delegate)original)(a, b, c, d); };
                return (TDelegate)(Delegate)wrapped;
            }
            if (typeof(TDelegate) == typeof(StatusBarFormatScopeDelegate))
            {
                StatusBarFormatScopeDelegate wrapped = (a, b, c) => { OriginalCalls[id]++; ((StatusBarFormatScopeDelegate)(Delegate)original)(a, b, c); };
                return (TDelegate)(Delegate)wrapped;
            }
            if (typeof(TDelegate) == typeof(StatusBarGlyphRendererDelegate))
            {
                StatusBarGlyphRendererDelegate wrapped = (a, b, c) => { OriginalCalls[id]++; return ((StatusBarGlyphRendererDelegate)(Delegate)original)(a, b, c); };
                return (TDelegate)(Delegate)wrapped;
            }
            if (typeof(TDelegate) == typeof(StatusBarDestructorDelegate))
            {
                StatusBarDestructorDelegate wrapped = a => { OriginalCalls[id]++; ((StatusBarDestructorDelegate)(Delegate)original)(a); };
                return (TDelegate)(Delegate)wrapped;
            }
            if (typeof(TDelegate) == typeof(ClassicTopMenuDeletingDestructorDelegate))
            {
                ClassicTopMenuDeletingDestructorDelegate wrapped = (a, b) => { OriginalCalls[id]++; return ((ClassicTopMenuDeletingDestructorDelegate)(Delegate)original)(a, b); };
                return (TDelegate)(Delegate)wrapped;
            }
            if (typeof(TDelegate) == typeof(TouchTopMenuDeletingDestructorDelegate))
            {
                TouchTopMenuDeletingDestructorDelegate wrapped = (a, b) => { OriginalCalls[id]++; return ((TouchTopMenuDeletingDestructorDelegate)(Delegate)original)(a, b); };
                return (TDelegate)(Delegate)wrapped;
            }
            if (typeof(TDelegate) == typeof(TopMenuActionDispatcherDelegate))
            {
                TopMenuActionDispatcherDelegate wrapped = a => { OriginalCalls[id]++; ((TopMenuActionDispatcherDelegate)(Delegate)original)(a); };
                return (TDelegate)(Delegate)wrapped;
            }
            throw new InvalidOperationException($"Unsupported fake original {typeof(TDelegate).Name}.");
        }

        public TDelegate GetDetour<TDelegate>(HookId id) where TDelegate : Delegate =>
            (TDelegate)FunctionDetours[id];
        public NativeCallSiteProbeDelegate GetProbe(HookId id) => Probes[id];

        public IHook<TDelegate> CreateHook<TDelegate>(HookId id, TDelegate detour, nuint address)
            where TDelegate : Delegate
        {
            Created.Add((id, address));
            FunctionDetours[id] = detour;
            return new FakeHook<TDelegate>((TDelegate)originals[id]);
        }

        public IPreparedHook CreateAsmHook<TDelegate>(
            HookId id,
            string name,
            TDelegate callback,
            nuint address,
            Func<RuntimeAsmHookAssemblyContext, IReadOnlyList<string>> buildAssembly,
            RuntimeAsmHookOptions options)
            where TDelegate : Delegate
        {
            Assert.Equal(typeof(NativeCallSiteProbeDelegate), typeof(TDelegate));
            Assert.Equal(5, options.HookLength);
            _ = buildAssembly;
            Created.Add((id, address));
            Probes[id] = (NativeCallSiteProbeDelegate)(Delegate)callback;
            return new FakePrepared(name, callback);
        }
    }

    private sealed class FakePrepared(string name, object callback) : IPreparedHook
    {
        public string Name { get; } = name;
        public bool IsActive { get; private set; }
        public IReadOnlyCollection<object> LifetimeRoots { get; } = [callback];
        public void Activate() => IsActive = true;
        public void Disable() => IsActive = false;
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

        public TestMemory AddByte(nuint address, byte value) { segments[address] = [value]; return this; }
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
            encoded.CopyTo(layout, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 15);
            segments[address] = layout;
            return this;
        }
        public TestMemory AddWideString(nuint address, string value)
        {
            var encoded = Encoding.Unicode.GetBytes(value);
            var layout = new byte[MsvcWideStringReader.LayoutSize];
            encoded.CopyTo(layout, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)value.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 7);
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
        public List<string> Diagnostics { get; } = [];
        public bool ThrowOnDiagnostic { get; set; }
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent accessibilityEvent) => Events.Add(accessibilityEvent);
        public void ReportCoverageFailure(string message) => Failures.Add(message);
        public void RecordDiagnostic(string message)
        {
            if (ThrowOnDiagnostic) throw new InvalidOperationException("test diagnostic failure");
            Diagnostics.Add(message);
        }
    }

    private sealed class RecordingLog : IModLog { public void Info(string message) { } public void Error(string message) { } }
    private sealed class RecordingFatal : IAccessibleFatalError { public void Show(string message) { } }
}
