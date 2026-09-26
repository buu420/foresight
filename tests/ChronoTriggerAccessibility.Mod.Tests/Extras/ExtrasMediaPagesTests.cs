using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Mod.Extras;
using ChronoTriggerAccessibility.Native.Hooks;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Extras;

/// <summary>
/// The Movies, Sound and Illustrations pages, and the one-frame deferred Gallery dispatchers:
/// every page callback returns before the dispatcher it asks for runs.
/// </summary>
public sealed partial class ExtrasHookSetTests
{
    private const nuint ViewerContainer = 0x00990000;
    private const nuint ViewerFocusState = 0x00C80000;

    private static readonly MediaSpec MoviesSpec = new(
        MediaKind.Movies,
        ExtrasHookSet.MoviesVtableRva,
        ExtrasHookSet.MovieCount,
        TitleBank: 0x10,
        ExtrasHookSet.MoviesTitleTableRva,
        TitleStride: 0x20,
        TitleBase: 0x300,
        StatusMessage: 0x3B,
        Status: "Please select a movie.",
        SwitchAction: 1,
        TitleMessage: 0x43,
        Title: "Movies",
        RowName: "Movie");

    private static readonly MediaSpec SoundSpec = new(
        MediaKind.Sound,
        ExtrasHookSet.SoundVtableRva,
        ExtrasHookSet.TrackCount,
        TitleBank: 0x01,
        ExtrasHookSet.SoundTitleTableRva,
        TitleStride: 0x0C,
        TitleBase: 0x500,
        StatusMessage: 0x3D,
        Status: "Please select music.",
        SwitchAction: 2,
        TitleMessage: 0x44,
        Title: "Sound",
        RowName: "Track");

    private static readonly MediaSpec IllustrationsSpec = new(
        MediaKind.Illustrations,
        ExtrasHookSet.IllustrationsVtableRva,
        ExtrasHookSet.IllustrationCount,
        TitleBank: 0x10,
        ExtrasHookSet.IllustrationsTitleTableRva,
        TitleStride: 0x20,
        TitleBase: 0x400,
        StatusMessage: 0x4E,
        Status: "Please select an illustration.",
        SwitchAction: 5,
        TitleMessage: 0x45,
        Title: "Illustrations",
        RowName: "Picture");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void HubDispatcherBuildsItsDirectPageOnlyAfterTheDecidingCallbackReturned(int hubKey)
    {
        var harness = CreateHarness();
        var hubControls = Controls(5);
        ConfigureEnabledHub(harness, hubControls, focusedKey: hubKey, Node);
        ConfigurePayload(harness, hubKey);
        var spec = hubKey == 1 ? IllustrationsSpec : SoundSpec;
        string expectedTitle;
        if (hubKey == 3)
        {
            ConfigureEndingRecords(harness.Memory, unlockedFlags: 1);
            ConfigureNode(harness.Memory, TargetNode, ExtrasHookSet.EndingLogVtableRva);
            ConfigureEndingLogOnEnter(harness, Controls(20, 0x00A50000), focusedKey: 0, expectedNode: TargetNode);
            expectedTitle = "Ending Log";
        }
        else
        {
            ConfigureMediaPage(
                harness,
                spec,
                TargetNode,
                Controls(spec.Rows + 1, 0x00A50000),
                () => 0,
                configureSwitch: false);
            expectedTitle = spec.Title;
        }
        var dispatches = 0;
        harness.Factory.SetOriginal<GalleryHubDispatchDelegate>(HookId.GalleryHubDispatch, payload =>
        {
            dispatches++;
            Assert.Equal((nint)TransitionPayload, payload);
            // 0x2A5650 constructs the page, localizes its Gallery title, then attaches it through
            // 0x2A5270: the Hub leaves, the slot empties, and the page's onEnter runs nested.
            harness.ObserveText(0x1A, hubKey == 3 ? 0x0F : spec.TitleMessage, expectedTitle);
            harness.Exit(Node);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, 0);
            if (hubKey == 3)
            {
                harness.LogEnter(TargetNode);
            }
            else
            {
                Enter(harness, spec, TargetNode);
            }
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, TargetNode);
        });
        harness.PrepareAndActivate();
        harness.Switch();
        harness.HubEnter();
        harness.Dispatcher.Events.Clear();

        harness.HubCallback(0, hubKey);

        var label = hubKey switch { 1 => "Illustrations", 2 => "Sound", _ => "Ending Log" };
        Assert.Equal([new MenuActivated(label)], harness.Dispatcher.Events);

        harness.HubDispatch();

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Equal(1, dispatches);
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.Equal(new MenuActivated(label), item),
            item => Assert.IsType<MenuExited>(item),
            item =>
            {
                var presented = Assert.IsType<MenuPresented>(item);
                Assert.Equal(expectedTitle, presented.Title);
                if (hubKey == 3)
                {
                    Assert.Equal(new MenuFocus("Ending One", null, 1, 20, null, false), presented.Focus);
                    return;
                }
                Assert.Equal(
                    new MenuFocus(RowTitle(spec, 0), null, 1, spec.Rows + 1, null, false),
                    presented.Focus);
                Assert.Equal([spec.Status], presented.StatusDetails);
            });
    }

    [Fact]
    public void HubMoviesDispatcherUsesItsOneNestedSwitchAndReadsEveryRowInNativeOrder()
    {
        var harness = CreateHarness();
        var hubControls = Controls(5);
        var movieControls = Controls(MoviesSpec.Rows + 1, 0x00A50000);
        ConfigureEnabledHub(harness, hubControls, focusedKey: 0, Node);
        ConfigurePayload(harness, 0);
        ConfigureMediaPage(harness, MoviesSpec, TargetNode, movieControls, () => 0, configureSwitch: false);
        harness.Factory.SetOriginal<GallerySceneSwitchNodeDelegate>(HookId.GallerySceneSwitchNode, (_, action, raw) =>
        {
            if (action == 0)
            {
                harness.ObserveText(0x41, 0x06, "Extras");
                return (nint)Node;
            }
            Assert.Equal(1, action);
            Assert.Equal(0u, raw);
            harness.ObserveText(0x1A, 0x43, "Movies");
            return (nint)TargetNode;
        });
        harness.Factory.SetOriginal<GalleryHubDispatchDelegate>(HookId.GalleryHubDispatch, _ =>
        {
            // 0x2A5650 key 0: switchNode(1, 0), then attach through 0x2A5270.
            Assert.Equal((nint)TargetNode, harness.Switch(1, 0));
            harness.Exit(Node);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, 0);
            harness.MoviesEnter(TargetNode);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, TargetNode);
        });
        harness.PrepareAndActivate();
        harness.Switch();
        harness.HubEnter();
        harness.Dispatcher.Events.Clear();

        harness.HubCallback(0, 0);
        harness.HubDispatch();

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.Equal(new MenuActivated("Movies"), item),
            item => Assert.IsType<MenuExited>(item),
            item =>
            {
                var presented = Assert.IsType<MenuPresented>(item);
                Assert.Equal("Movies", presented.Title);
                Assert.Equal(new MenuFocus("Movie 1", null, 1, 9, null, false), presented.Focus);
                Assert.Equal(["Please select a movie."], presented.StatusDetails);
            });

        harness.Memory.AddInt32(Manager + ExtrasHookSet.ManagerFocusKeyOffset, 8);
        harness.Set.AfterFocusSet((nint)Manager, 8);
        Assert.Equal(new MenuFocusChanged(new MenuFocus("Back", null, 9, 9, null, false)), harness.Dispatcher.Events.Last());
    }

    [Fact]
    public void MovieDecideStatesTheVideoIsNotDescribedAndTheSameNodeResumesAfterPlayback()
    {
        var harness = CreateHarness();
        var controls = Controls(MoviesSpec.Rows + 1);
        var focusAtEnter = 0;
        var builds = 0;
        ConfigureMediaPage(harness, MoviesSpec, Node, controls, () => focusAtEnter, () => builds++);
        ConfigureMediaClosure(harness.Memory, MoviesSpec, Node, controls);
        ConfigurePayload(harness, 0);
        harness.Factory.SetOriginal<ExtrasMoviesCallbackDelegate>(HookId.ExtrasMoviesCallback, (_, eventType, action) =>
        {
            if (eventType == 0 && action < ExtrasHookSet.MovieCount)
            {
                // 0x1D83C0 stores the decided row, then only schedules the playback dispatcher.
                harness.Memory.AddInt32(Node + ExtrasHookSet.MoviesSelectedRowOffset, action);
            }
        });
        var dispatches = 0;
        harness.Factory.SetOriginal<GalleryMoviesDispatchDelegate>(HookId.GalleryMoviesDispatch, _ => dispatches++);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, MoviesSpec);
        FocusKey(harness, 2);
        harness.Dispatcher.Events.Clear();

        harness.MoviesCallback(0, 2);

        Assert.Equal(
            [new MenuUnsupported("Movie 3", ExtrasHookSet.NoVideoDescription, ExtrasHookSet.MoviesReturn)],
            harness.Dispatcher.Events);

        harness.MoviesDispatch();
        Assert.Equal(1, dispatches);
        Assert.Single(harness.Dispatcher.Events);

        // The movie scene's push calls onExit on the page; popping back re-enters the same node,
        // which rebuilds itself focused on the row it stored.
        harness.Exit(Node);
        Assert.IsType<MenuExited>(harness.Dispatcher.Events.Last());
        focusAtEnter = 2;
        harness.MoviesEnter(Node);

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Equal(2, builds);
        var resumed = Assert.IsType<MenuPresented>(harness.Dispatcher.Events.Last());
        Assert.Equal("Movies", resumed.Title);
        Assert.Equal(new MenuFocus("Movie 3", null, 3, 9, null, false), resumed.Focus);
        Assert.Equal(["Please select a movie."], resumed.StatusDetails);
    }

    [Fact]
    public void MovieDispatcherWithoutTheStoredDecidedRowFailsClosedAfterItsOriginal()
    {
        var harness = CreateHarness();
        var controls = Controls(MoviesSpec.Rows + 1);
        ConfigureMediaPage(harness, MoviesSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, MoviesSpec, Node, controls);
        ConfigurePayload(harness, 0);
        harness.Memory.AddInt32(Node + ExtrasHookSet.MoviesSelectedRowOffset, 5);
        var dispatches = 0;
        harness.Factory.SetOriginal<GalleryMoviesDispatchDelegate>(HookId.GalleryMoviesDispatch, _ => dispatches++);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, MoviesSpec);
        FocusKey(harness, 2);

        harness.MoviesCallback(0, 2);
        harness.MoviesDispatch();

        Assert.Equal(1, dispatches);
        Assert.Contains("not related to the exact decide", Assert.Single(harness.Dispatcher.Failures));
    }

    [Fact]
    public void MoviesBackUsesTheNestedHubSwitchAndRestoresTheMoviesTile()
    {
        var harness = CreateHarness();
        var controls = Controls(MoviesSpec.Rows + 1);
        var hubControls = Controls(5, 0x00A50000);
        ConfigureMediaPage(harness, MoviesSpec, Node, controls, () => 0, configureSwitch: false);
        ConfigureMediaClosure(harness.Memory, MoviesSpec, Node, controls);
        ConfigurePayload(harness, 4);
        ConfigureNode(harness.Memory, TargetNode, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureHubAvailability(harness.Memory, TargetNode);
        ConfigureHubOnEnter(
            harness,
            hubControls,
            focusedKey: 0,
            expectedNode: TargetNode,
            helpMessage: 0x4A,
            help: "View in-game movies.");
        harness.Factory.SetOriginal<GallerySceneSwitchNodeDelegate>(HookId.GallerySceneSwitchNode, (_, action, raw) =>
        {
            if (action == 1)
            {
                harness.ObserveText(0x1A, 0x43, "Movies");
                return (nint)Node;
            }
            Assert.Equal(0, action);
            Assert.Equal(0u, raw);
            harness.ObserveText(0x41, 0x06, "Extras");
            return (nint)TargetNode;
        });
        harness.Factory.SetOriginal<GalleryMoviesDispatchDelegate>(HookId.GalleryMoviesDispatch, _ =>
        {
            // 0x2A5A80 action 4: remove the page, switchNode(0, 0), then attach the Hub.
            harness.Exit(Node);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, 0);
            Assert.Equal((nint)TargetNode, harness.Switch(0, 0));
            harness.HubEnter(TargetNode);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, TargetNode);
        });
        harness.PrepareAndActivate();
        EnterMediaPage(harness, MoviesSpec);
        harness.Dispatcher.Events.Clear();

        harness.MoviesCallback(2, -1);
        Assert.Equal([new MenuActivated("Back")], harness.Dispatcher.Events);
        harness.MoviesDispatch();

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.Equal(new MenuActivated("Back"), item),
            item => Assert.IsType<MenuExited>(item),
            item =>
            {
                var presented = Assert.IsType<MenuPresented>(item);
                Assert.Equal("Extras", presented.Title);
                Assert.Equal(new MenuFocus("Movies", null, 1, 5, "View in-game movies.", false), presented.Focus);
            });
    }

    /// <summary>How the two dispatchers of a Movies Decide and Cancel meet the movie push.</summary>
    public enum MovieRace
    {
        /// <summary>The playback dispatcher runs before the Cancel input.</summary>
        DispatchBetweenInputs,

        /// <summary>Both inputs, then both dispatchers in one ActionManager update, then the
        /// Director's deferred push exits the Hub that Back already built.</summary>
        BothInputsThenBothDispatchersThenPush,

        /// <summary>Both inputs, the playback dispatcher and the push; the paused Back
        /// dispatcher runs only after the movie pops and the Movies page re-entered.</summary>
        BothInputsThenPushBeforeBack,
    }

    [Theory]
    [InlineData(MovieRace.DispatchBetweenInputs)]
    [InlineData(MovieRace.BothInputsThenBothDispatchersThenPush)]
    [InlineData(MovieRace.BothInputsThenPushBeforeBack)]
    public void MovieDecideAndCancelReachTheHubInEveryNativeDispatchOrder(MovieRace race)
    {
        var harness = CreateHarness();
        var controls = Controls(MoviesSpec.Rows + 1);
        var hubControls = Controls(5, 0x00A50000);
        var movieBuilds = 0;
        ConfigureMediaPage(harness, MoviesSpec, Node, controls, () => 0, () => movieBuilds++, configureSwitch: false);
        ConfigureMediaClosure(harness.Memory, MoviesSpec, Node, controls);
        ConfigureNode(harness.Memory, TargetNode, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureHubAvailability(harness.Memory, TargetNode);
        var hubBuilds = 0;
        ConfigureHubOnEnter(
            harness,
            hubControls,
            focusedKey: 0,
            expectedNode: TargetNode,
            onEnterObserved: () => hubBuilds++,
            helpMessage: 0x4A,
            help: "View in-game movies.");
        harness.Factory.SetOriginal<GallerySceneSwitchNodeDelegate>(HookId.GallerySceneSwitchNode, (_, action, _) =>
        {
            if (action == 1)
            {
                harness.ObserveText(0x1A, 0x43, "Movies");
                return (nint)Node;
            }
            harness.ObserveText(0x41, 0x06, "Extras");
            return (nint)TargetNode;
        });
        harness.Factory.SetOriginal<ExtrasMoviesCallbackDelegate>(HookId.ExtrasMoviesCallback, (_, eventType, action) =>
        {
            if (eventType == 0 && action < ExtrasHookSet.MovieCount)
            {
                harness.Memory.AddInt32(Node + ExtrasHookSet.MoviesSelectedRowOffset, action);
            }
        });
        var playbackDispatches = 0;
        harness.Factory.SetOriginal<GalleryMoviesDispatchDelegate>(HookId.GalleryMoviesDispatch, _ =>
        {
            if (ReadByte(harness.Memory, TransitionPayload) != 4)
            {
                // NextScene(0) only asks the Director to push the movie at the end of the frame.
                playbackDispatches++;
                return;
            }
            // 0x2A5A80 action 4 removes whatever node the scene currently shows.
            harness.Exit(Node);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, 0);
            harness.Switch(0, 0);
            harness.HubEnter(TargetNode);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, TargetNode);
        });
        harness.PrepareAndActivate();
        EnterMediaPage(harness, MoviesSpec);

        harness.MoviesCallback(0, 0);
        if (race == MovieRace.DispatchBetweenInputs)
        {
            ConfigurePayload(harness, 0);
            harness.MoviesDispatch();
        }
        harness.MoviesCallback(2, -1);
        if (race != MovieRace.DispatchBetweenInputs)
        {
            ConfigurePayload(harness, 0);
            harness.MoviesDispatch();
        }
        if (race == MovieRace.BothInputsThenPushBeforeBack)
        {
            // The push exits the Movies page and pauses the scene's actions; the pop re-enters it.
            harness.Exit(Node);
            harness.MoviesEnter(Node);
        }
        ConfigurePayload(harness, 4);
        harness.MoviesDispatch();
        if (race != MovieRace.BothInputsThenPushBeforeBack)
        {
            // The deferred push now exits the Hub, and the pop re-enters that same Hub.
            harness.Exit(TargetNode);
            harness.HubEnter(TargetNode);
        }

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Equal(1, playbackDispatches);
        Assert.Equal(race == MovieRace.BothInputsThenPushBeforeBack ? 2 : 1, movieBuilds);
        Assert.Equal(race == MovieRace.BothInputsThenPushBeforeBack ? 1 : 2, hubBuilds);
        var last = Assert.IsType<MenuPresented>(harness.Dispatcher.Events.Last());
        Assert.Equal("Extras", last.Title);
        Assert.Equal(new MenuFocus("Movies", null, 1, 5, "View in-game movies.", false), last.Focus);
    }

    [Fact]
    public void RepeatedSoundCancelBeforeEitherDispatcherRebuildsTheHubTwice()
    {
        // Sound Back 0x2A5BD0 removes whatever node the scene shows and builds a new Hub, so the
        // second queued Back replaces the Hub the first one built.
        const nuint SecondHub = 0x00BF0000;
        var harness = CreateHarness();
        var controls = Controls(SoundSpec.Rows + 1);
        var hubControls = Controls(5, 0x00A50000);
        ConfigureMediaPage(harness, SoundSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, SoundSpec, Node, controls);
        ConfigureBackPayload(harness);
        foreach (var hub in new[] { TargetNode, SecondHub })
        {
            ConfigureNode(harness.Memory, hub, ExtrasHookSet.ExtrasHubVtableRva);
            ConfigureHubAvailability(harness.Memory, hub);
        }
        var hubs = new[] { TargetNode, SecondHub };
        var built = 0;
        harness.Factory.SetOriginal<ExtrasHubOnEnterDelegate>(HookId.ExtrasHubOnEnter, node =>
        {
            Assert.Equal((nint)hubs[built], node);
            ConfigureManagerAndControls(harness.Memory, hubControls, 2);
            ObserveHubLabelsAndControls(harness, hubControls);
            Bind(harness, hubControls, [0, 1, 2, 3, 4]);
            harness.Set.AfterFocusSet((nint)Manager, 2);
            harness.ObserveText(0x1A, 0x4C, "Listen to music tracks.");
        });
        harness.Factory.SetOriginal<GallerySoundBackDelegate>(HookId.GallerySoundBack, _ =>
        {
            if (built == hubs.Length)
            {
                return;
            }
            var current = built == 0 ? Node : hubs[built - 1];
            harness.Exit(current);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, 0);
            harness.ObserveText(0x41, 0x06, "Extras");
            harness.HubEnter(hubs[built]);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, hubs[built]);
            built++;
        });
        harness.PrepareAndActivate();
        EnterMediaPage(harness, SoundSpec);
        harness.Dispatcher.Events.Clear();

        harness.SoundCallback(2, -1);
        harness.SoundCallback(2, -1);
        harness.SoundBack();
        harness.SoundBack();

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Equal(2, built);
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.Equal(new MenuActivated("Back"), item),
            item => Assert.Equal(new MenuActivated("Back"), item),
            item => Assert.Equal(new MenuExited(new("Extras", (ulong)Node)), item),
            item => Assert.Equal(new MenuOwner("Extras", (ulong)TargetNode), Assert.IsType<MenuPresented>(item).Owner),
            item => Assert.Equal(new MenuExited(new("Extras", (ulong)TargetNode)), item),
            item =>
            {
                var presented = Assert.IsType<MenuPresented>(item);
                Assert.Equal(new MenuOwner("Extras", (ulong)SecondHub), presented.Owner);
                Assert.Equal(new MenuFocus("Sound", null, 3, 5, "Listen to music tracks.", false), presented.Focus);
            });

        // Each Cancel answered exactly one dispatcher.
        harness.SoundBack();
        Assert.Contains("no preceding decide or cancel", Assert.Single(harness.Dispatcher.Failures));
    }

    [Fact]
    public void HubDecideThenCancelBeforeEitherDispatcherBuildsThePageThenLeaves()
    {
        var harness = CreateHarness();
        var hubControls = Controls(5);
        ConfigureEnabledHub(harness, hubControls, focusedKey: 1, Node);
        ConfigureMediaPage(
            harness,
            IllustrationsSpec,
            TargetNode,
            Controls(IllustrationsSpec.Rows + 1, 0x00A50000),
            () => 0,
            configureSwitch: false);
        var leaves = 0;
        harness.Factory.SetOriginal<GalleryHubDispatchDelegate>(HookId.GalleryHubDispatch, _ =>
        {
            if (ReadByte(harness.Memory, TransitionPayload) == 4)
            {
                // NextScene(-1): the Director replaces the Gallery at the end of the frame.
                leaves++;
                return;
            }
            harness.ObserveText(0x1A, 0x45, "Illustrations");
            harness.Exit(Node);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, 0);
            harness.IllustrationsEnter(TargetNode);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, TargetNode);
        });
        harness.PrepareAndActivate();
        harness.Switch();
        harness.HubEnter();
        harness.Dispatcher.Events.Clear();

        harness.HubCallback(0, 1);
        harness.HubCallback(2, -1);
        ConfigurePayload(harness, 1);
        harness.HubDispatch();
        ConfigurePayload(harness, 4);
        harness.HubDispatch();
        // The replaced Gallery exits the page the first dispatcher built.
        harness.Exit(TargetNode);

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Equal(1, leaves);
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.Equal(new MenuActivated("Illustrations"), item),
            item => Assert.Equal(new MenuActivated("Back"), item),
            item => Assert.IsType<MenuExited>(item),
            item => Assert.Equal("Illustrations", Assert.IsType<MenuPresented>(item).Title),
            item => Assert.Equal(new MenuExited(new("Extras", (ulong)TargetNode)), item));
    }

    [Fact]
    public void QueuedDispatchersOutOfNativeOrderFailClosed()
    {
        // The scene's ActionManager runs scheduled dispatchers in the order they were added.
        var harness = CreateHarness();
        var controls = Controls(MoviesSpec.Rows + 1);
        ConfigureMediaPage(harness, MoviesSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, MoviesSpec, Node, controls);
        harness.Factory.SetOriginal<ExtrasMoviesCallbackDelegate>(HookId.ExtrasMoviesCallback, (_, eventType, action) =>
        {
            if (eventType == 0 && action < ExtrasHookSet.MovieCount)
            {
                harness.Memory.AddInt32(Node + ExtrasHookSet.MoviesSelectedRowOffset, action);
            }
        });
        var dispatches = 0;
        harness.Factory.SetOriginal<GalleryMoviesDispatchDelegate>(HookId.GalleryMoviesDispatch, _ => dispatches++);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, MoviesSpec);

        harness.MoviesCallback(0, 0);
        harness.MoviesCallback(2, -1);
        ConfigurePayload(harness, 4);
        harness.MoviesDispatch();

        Assert.Equal(1, dispatches);
        Assert.Contains("no preceding decide or cancel", Assert.Single(harness.Dispatcher.Failures));
    }

    [Fact]
    public void DisableDiscardsQueuedRequestsSoNoLaterDispatcherCanConsumeThem()
    {
        var harness = CreateHarness();
        var controls = Controls(MoviesSpec.Rows + 1);
        ConfigureMediaPage(harness, MoviesSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, MoviesSpec, Node, controls);
        ConfigurePayload(harness, 4);
        var dispatches = 0;
        harness.Factory.SetOriginal<GalleryMoviesDispatchDelegate>(HookId.GalleryMoviesDispatch, _ => dispatches++);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, MoviesSpec);
        harness.MoviesCallback(2, -1);

        harness.Installer.DisableAll();
        harness.MoviesDispatch();
        Assert.Empty(harness.Dispatcher.Failures);

        harness.Installer.ActivateAll();
        harness.MoviesDispatch();

        Assert.Equal(2, dispatches);
        Assert.Contains("no preceding decide or cancel", Assert.Single(harness.Dispatcher.Failures));
    }

    [Fact]
    public void SoundTrackShowsNowPlayingAndCancelWhilePlayingStopsWithoutLeaving()
    {
        var harness = CreateHarness();
        var controls = Controls(SoundSpec.Rows + 1);
        ConfigureMediaPage(harness, SoundSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, SoundSpec, Node, controls);
        ConfigureSoundCallback(harness);
        var backDispatches = 0;
        harness.Factory.SetOriginal<GallerySoundBackDelegate>(HookId.GallerySoundBack, _ => backDispatches++);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, SoundSpec);

        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal("Sound", presented.Title);
        Assert.Equal(new MenuFocus("Track 1", null, 1, 66, null, false), presented.Focus);
        Assert.Equal(["Please select music."], presented.StatusDetails);

        FocusKey(harness, 3);
        harness.Dispatcher.Events.Clear();
        harness.SoundCallback(0, 3);

        var playing = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(presented.Owner, playing.Owner);
        Assert.Equal(["Now Playing", "Track 4"], playing.Lines);

        harness.SoundCallback(2, -1);

        var stopped = Assert.IsType<MenuNoticePresented>(harness.Dispatcher.Events.Last());
        Assert.Equal(["Please select music."], stopped.Lines);
        Assert.Equal(0, backDispatches);
        Assert.DoesNotContain(harness.Dispatcher.Events, item => item is MenuExited or MenuActivated);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void SoundTrackEndingOnItsOwnRestoresTheStatusInstruction()
    {
        var harness = CreateHarness();
        var controls = Controls(SoundSpec.Rows + 1);
        ConfigureMediaPage(harness, SoundSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, SoundSpec, Node, controls);
        ConfigureSoundCallback(harness);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, SoundSpec);
        harness.SoundCallback(0, 0);
        harness.Dispatcher.Events.Clear();

        // The page's update 0x1DA9B0 calls the idle state 0x1DA820 once the track stops.
        harness.SoundIdle(Node);
        harness.SoundIdle(Node);

        var idle = Assert.IsType<MenuNoticePresented>(Assert.Single(harness.Dispatcher.Events));
        Assert.Equal(["Please select music."], idle.Lines);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void SoundTrackWithoutItsNativeTitleFailsClosed()
    {
        var harness = CreateHarness();
        var controls = Controls(SoundSpec.Rows + 1);
        ConfigureMediaPage(harness, SoundSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, SoundSpec, Node, controls);
        harness.Factory.SetOriginal<ExtrasSoundCallbackDelegate>(HookId.ExtrasSoundCallback, (_, _, _) =>
        {
            harness.ObserveText(0x1A, 0x46, "Now Playing");
            harness.Memory.AddInt32(Node + ExtrasHookSet.SoundPlayingOffset, 1);
        });
        harness.PrepareAndActivate();
        EnterMediaPage(harness, SoundSpec);
        harness.Dispatcher.Events.Clear();

        harness.SoundCallback(0, 0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains("Now Playing", Assert.Single(harness.Dispatcher.Failures));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoundAndIllustrationsBackBuildTheHubDirectlyOnTheirOwnTile(bool illustrations)
    {
        var harness = CreateHarness();
        var spec = illustrations ? IllustrationsSpec : SoundSpec;
        var controls = Controls(spec.Rows + 1);
        var hubControls = Controls(5, 0x00A50000);
        var hubKey = illustrations ? 1 : 2;
        ConfigureMediaPage(harness, spec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, spec, Node, controls);
        ConfigureBackPayload(harness);
        ConfigureNode(harness.Memory, TargetNode, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureHubAvailability(harness.Memory, TargetNode);
        var help = illustrations ? "View artwork for the game." : "Listen to music tracks.";
        ConfigureHubOnEnter(
            harness,
            hubControls,
            focusedKey: hubKey,
            expectedNode: TargetNode,
            helpMessage: 0x4A + hubKey,
            help: help);
        void BuildHub(nint payload)
        {
            // 0x2A5BD0 / 0x2A6D30: remove the page, construct the Hub with its focus in ECX,
            // localize the Gallery title, then attach it.
            Assert.Equal((nint)BackPayload, payload);
            harness.Exit(Node);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, 0);
            harness.ObserveText(0x41, 0x06, "Extras");
            harness.HubEnter(TargetNode);
            harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, TargetNode);
        }
        harness.Factory.SetOriginal<GallerySoundBackDelegate>(HookId.GallerySoundBack, BuildHub);
        harness.Factory.SetOriginal<GalleryIllustrationsBackDelegate>(HookId.GalleryIllustrationsBack, BuildHub);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, spec);
        harness.Dispatcher.Events.Clear();

        if (illustrations)
        {
            harness.IllustrationsCallback(2, -1);
            harness.IllustrationsBack();
        }
        else
        {
            harness.SoundCallback(2, -1);
            harness.SoundBack();
        }

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Collection(
            harness.Dispatcher.Events,
            item => Assert.Equal(new MenuActivated("Back"), item),
            item => Assert.IsType<MenuExited>(item),
            item =>
            {
                var presented = Assert.IsType<MenuPresented>(item);
                Assert.Equal("Extras", presented.Title);
                Assert.Equal(
                    new MenuFocus(illustrations ? "Illustrations" : "Sound", null, hubKey + 1, 5, help, false),
                    presented.Focus);
            });
    }

    [Fact]
    public void SoundBackAfterCancelStoppedATrackFailsClosed()
    {
        var harness = CreateHarness();
        var controls = Controls(SoundSpec.Rows + 1);
        ConfigureMediaPage(harness, SoundSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, SoundSpec, Node, controls);
        ConfigureSoundCallback(harness);
        ConfigureBackPayload(harness);
        var backDispatches = 0;
        harness.Factory.SetOriginal<GallerySoundBackDelegate>(HookId.GallerySoundBack, _ => backDispatches++);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, SoundSpec);
        harness.SoundCallback(0, 0);
        harness.SoundCallback(2, -1);

        // That Cancel only stopped the track; natively it asked for no dispatcher.
        harness.SoundBack();

        Assert.Equal(1, backDispatches);
        Assert.Contains("no preceding decide or cancel", Assert.Single(harness.Dispatcher.Failures));
    }

    [Fact]
    public void IllustrationViewerIsNamedWithoutAnInventedDescriptionAndClosingReturnsToTheRow()
    {
        var harness = CreateHarness();
        var controls = Controls(IllustrationsSpec.Rows + 1);
        ConfigureMediaPage(harness, IllustrationsSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, IllustrationsSpec, Node, controls);
        ConfigureViewer(harness, extraControl: false);
        var viewerCloses = 0;
        harness.Factory.SetOriginal<ExtrasIllustrationViewerCallbackDelegate>(
            HookId.ExtrasIllustrationViewerCallback,
            (_, _, _) => viewerCloses++);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, IllustrationsSpec);
        FocusKey(harness, 4);
        harness.Dispatcher.Events.Clear();

        harness.IllustrationsCallback(0, 4);

        Assert.Equal(
            [new MenuUnsupported("Picture 5", ExtrasHookSet.NoImageDescription, ExtrasHookSet.IllustrationReturn)],
            harness.Dispatcher.Events);

        harness.ViewerCallback(1);
        Assert.Single(harness.Dispatcher.Events);
        harness.ViewerCallback(2);

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Equal(2, viewerCloses);
        var restored = Assert.IsType<MenuPresented>(harness.Dispatcher.Events.Last());
        Assert.Equal("Illustrations", restored.Title);
        Assert.Equal(new MenuFocus("Picture 5", null, 5, 17, null, false), restored.Focus);
        Assert.Equal(["Please select an illustration."], restored.StatusDetails);

        // After closing, the viewer's closure no longer names an open viewer.
        harness.ViewerCallback(0);
        Assert.Equal(3, viewerCloses);
        Assert.Equal(2, harness.Dispatcher.Events.Count);
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Fact]
    public void IllustrationViewerWithAnyExtraControlFailsClosed()
    {
        var harness = CreateHarness();
        var controls = Controls(IllustrationsSpec.Rows + 1);
        ConfigureMediaPage(harness, IllustrationsSpec, Node, controls, () => 0);
        ConfigureMediaClosure(harness.Memory, IllustrationsSpec, Node, controls);
        ConfigureViewer(harness, extraControl: true);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, IllustrationsSpec);
        harness.Dispatcher.Events.Clear();

        harness.IllustrationsCallback(0, 0);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains("one control focused at key 0", Assert.Single(harness.Dispatcher.Failures));
    }

    [Fact]
    public void DetailReviewSuspendsAfterTheCallbackAndTheSameDetailPageResumesAfterTheReplay()
    {
        var harness = CreateHarness();
        var controls = Controls(2);
        ConfigureEndingRecords(harness.Memory, unlockedFlags: 1);
        ConfigureNode(harness.Memory, Node, ExtrasHookSet.EndingDetailVtableRva);
        harness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddInt32(ImageBase + ExtrasHookSet.SelectedEndingGlobalRva, 0)
            .AddInt32(ImageBase + ExtrasHookSet.EndingRequirementTableRva, 0x200);
        ConfigurePayload(harness, 3);
        ConfigureSwitch(harness, 4, 0x0F, 0x100, "Ending One", Node);
        var builds = 0;
        ConfigureEndingDetailOnEnter(harness, controls, focusedKey: 0, expectedNode: Node, onEnterObserved: () => builds++);
        ConfigureStandardClosure(harness.Memory, Node, controls);
        var transitions = 0;
        harness.Factory.SetOriginal<ExtrasDetailTransitionDelegate>(HookId.ExtrasDetailTransition, _ => transitions++);
        harness.PrepareAndActivate();
        harness.Switch(4);
        harness.DetailEnter();
        harness.Dispatcher.Events.Clear();

        harness.DetailCallback(0, 0);
        harness.DetailTransition();

        Assert.Equal(
            [new MenuUnsupported("Review", ExtrasHookSet.ReviewDescription, ExtrasHookSet.ReviewReturn)],
            harness.Dispatcher.Events);

        // The replay's pushed scene calls onExit on the page; popping back re-enters it.
        harness.Exit(Node);
        harness.DetailEnter();

        Assert.True(harness.Dispatcher.Failures.Count == 0, string.Join(" | ", harness.Dispatcher.Failures));
        Assert.Equal(1, transitions);
        Assert.Equal(2, builds);
        Assert.IsType<MenuExited>(harness.Dispatcher.Events[1]);
        var resumed = Assert.IsType<MenuPresented>(harness.Dispatcher.Events[2]);
        Assert.Equal("Ending One", resumed.Title);
        Assert.Equal(new MenuFocus("Review", null, 1, 2, null, false), resumed.Focus);
    }

    [Fact]
    public void OneDecideCannotAnswerTwoDeferredDispatchers()
    {
        var harness = CreateHarness();
        var controls = Controls(2);
        ConfigureEndingRecords(harness.Memory, unlockedFlags: 1);
        ConfigureNode(harness.Memory, Node, ExtrasHookSet.EndingDetailVtableRva);
        harness.Memory
            .AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, Node)
            .AddInt32(ImageBase + ExtrasHookSet.SelectedEndingGlobalRva, 0)
            .AddInt32(ImageBase + ExtrasHookSet.EndingRequirementTableRva, 0x200);
        ConfigurePayload(harness, 3);
        ConfigureSwitch(harness, 4, 0x0F, 0x100, "Ending One", Node);
        ConfigureEndingDetailOnEnter(harness, controls, focusedKey: 0, expectedNode: Node);
        ConfigureStandardClosure(harness.Memory, Node, controls);
        var transitions = 0;
        harness.Factory.SetOriginal<ExtrasDetailTransitionDelegate>(HookId.ExtrasDetailTransition, _ => transitions++);
        harness.PrepareAndActivate();
        harness.Switch(4);
        harness.DetailEnter();

        harness.DetailCallback(0, 0);
        harness.DetailTransition();
        harness.DetailTransition();

        Assert.Equal(2, transitions);
        Assert.Contains("no preceding decide or cancel", Assert.Single(harness.Dispatcher.Failures));
    }

    [Fact]
    public void DispatcherWithNoPrecedingDecideOrCancelFailsClosedAfterItsOriginal()
    {
        var harness = CreateHarness();
        var controls = Controls(MoviesSpec.Rows + 1);
        ConfigureMediaPage(harness, MoviesSpec, Node, controls, () => 0);
        ConfigurePayload(harness, 4);
        var dispatches = 0;
        harness.Factory.SetOriginal<GalleryMoviesDispatchDelegate>(HookId.GalleryMoviesDispatch, _ => dispatches++);
        harness.PrepareAndActivate();
        EnterMediaPage(harness, MoviesSpec);
        harness.Dispatcher.Events.Clear();

        harness.MoviesDispatch();

        Assert.Equal(1, dispatches);
        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains("no preceding decide or cancel", Assert.Single(harness.Dispatcher.Failures));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedDeletingDestructorClearsTheActiveMoviesOrIllustrationsPage(bool illustrations)
    {
        var harness = CreateHarness();
        var spec = illustrations ? IllustrationsSpec : MoviesSpec;
        var controls = Controls(spec.Rows + 1);
        ConfigureMediaPage(harness, spec, Node, controls, () => 0);
        var originals = 0;
        harness.Factory.SetOriginal<EndingLogDeletingDestructorDelegate>(
            HookId.EndingLogDeletingDestructor,
            (node, _) =>
            {
                originals++;
                return node;
            });
        harness.PrepareAndActivate();
        EnterMediaPage(harness, spec);
        harness.Dispatcher.Events.Clear();

        Assert.Equal((nint)Node, harness.SharedDelete(Node));
        FocusKey(harness, 1);

        Assert.Equal(1, originals);
        Assert.IsType<MenuExited>(Assert.Single(harness.Dispatcher.Events));
        Assert.Empty(harness.Dispatcher.Failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MediaPageMissingAnyNativeRowTitleFailsClosedWithoutPresenting(bool sound)
    {
        var harness = CreateHarness();
        var spec = sound ? SoundSpec : MoviesSpec;
        var controls = Controls(spec.Rows + 1);
        ConfigureMediaPage(harness, spec, Node, controls, () => 0, omitTitleRow: 3);
        harness.PrepareAndActivate();

        EnterMediaPage(harness, spec);

        Assert.Empty(harness.Dispatcher.Events);
        Assert.Contains("localized text is incomplete", Assert.Single(harness.Dispatcher.Failures));
    }

    private static void ConfigureEnabledHub(Harness harness, nuint[] controls, int focusedKey, nuint node)
    {
        ConfigureNode(harness.Memory, node, ExtrasHookSet.ExtrasHubVtableRva);
        ConfigureManagerAndControls(harness.Memory, controls, focusedKey);
        ConfigureHubClosure(harness.Memory, controls);
        ConfigureHubAvailability(harness.Memory, node);
        harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, node);
        var helps = new[] { "View in-game movies.", "View artwork for the game.", "Listen to music tracks.", "Review endings." };
        ConfigureHubBuild(
            harness,
            controls,
            focusedKey,
            focusedKey < 4 ? 0x4A + focusedKey : null,
            focusedKey < 4 ? helps[focusedKey] : null);
    }

    private static void ConfigureHubAvailability(TestMemory memory, nuint node)
    {
        for (var index = 0; index < 5; index++)
        {
            memory.AddInt32(node + 0x2E4 + (nuint)(index * 8), 1);
        }
    }

    private static void ConfigurePayload(Harness harness, int action) =>
        harness.Memory.AddInt32(TransitionPayload, action).AddPointer(TransitionPayload + 4, Scene);

    private static void ConfigureBackPayload(Harness harness) =>
        harness.Memory.AddPointer(BackPayload, Scene);

    private static void FocusKey(Harness harness, int key)
    {
        harness.Memory.AddInt32(Manager + ExtrasHookSet.ManagerFocusKeyOffset, key);
        harness.Set.AfterFocusSet((nint)Manager, key);
    }

    private static string RowTitle(MediaSpec spec, int row) => $"{spec.RowName} {row + 1}";

    /// <summary>The builders: Movies 0x1D7390 and Illustrations 0x1D51A0 localize the status,
    /// then construct Back (0x1D85F0 / 0x1D6910), then one row per table entry; Sound 0x1D9070
    /// constructs Back and its rows, then enters its idle state 0x1DA820, which shows the status.</summary>
    private static void ConfigureMediaPage(
        Harness harness,
        MediaSpec spec,
        nuint node,
        nuint[] controls,
        Func<int> focusedKey,
        Action? onEnterObserved = null,
        int? omitTitleRow = null,
        bool configureSwitch = true)
    {
        ConfigureNode(harness.Memory, node, spec.Vtable);
        if (configureSwitch)
        {
            ConfigureSwitch(harness, spec.SwitchAction, 0x1A, spec.TitleMessage, spec.Title, node);
        }
        for (var row = 0; row < spec.Rows; row++)
        {
            harness.Memory.AddInt32(ImageBase + spec.TitleTable + (nuint)(row * spec.TitleStride), spec.TitleBase + row);
        }
        void Build(nint entered)
        {
            Assert.Equal((nint)node, entered);
            onEnterObserved?.Invoke();
            var focus = focusedKey();
            ConfigureManagerAndControls(harness.Memory, controls, focus);
            if (spec.Kind != MediaKind.Sound)
            {
                harness.ObserveText(0x1A, spec.StatusMessage, spec.Status);
            }
            harness.Set.AfterCustomButtonConstructed((nint)controls[0], (nint)controls[0]);
            harness.ObserveText(0x23, 0xD8, "Back");
            for (var row = 0; row < spec.Rows; row++)
            {
                harness.Set.AfterCustomButtonConstructed((nint)controls[row + 1], (nint)controls[row + 1]);
                if (row != omitTitleRow)
                {
                    harness.ObserveText(spec.TitleBank, spec.TitleBase + row, RowTitle(spec, row));
                }
            }
            var states = FocusStates(spec.Rows + 1);
            for (var row = 0; row < spec.Rows; row++)
            {
                BindOne(harness, states[row + 1], controls[row + 1], row);
            }
            BindOne(harness, states[0], controls[0], spec.Rows);
            harness.Set.AfterFocusSet((nint)Manager, focus);
            if (spec.Kind == MediaKind.Sound)
            {
                harness.SoundIdle(node);
            }
        }
        switch (spec.Kind)
        {
            case MediaKind.Movies:
                harness.Factory.SetOriginal<ExtrasMoviesOnEnterDelegate>(HookId.ExtrasMoviesOnEnter, Build);
                break;
            case MediaKind.Sound:
                harness.Factory.SetOriginal<ExtrasSoundOnEnterDelegate>(HookId.ExtrasSoundOnEnter, Build);
                harness.Factory.SetOriginal<ExtrasSoundIdleDelegate>(HookId.ExtrasSoundIdle, idle =>
                {
                    harness.ObserveText(0x1A, 0x3D, "Please select music.");
                    harness.Memory.AddInt32((nuint)idle + ExtrasHookSet.SoundPlayingOffset, 0);
                });
                break;
            default:
                harness.Factory.SetOriginal<ExtrasIllustrationsOnEnterDelegate>(HookId.ExtrasIllustrationsOnEnter, Build);
                break;
        }
    }

    /// <summary>Movies and Sound closures hold {manager, node, control vector}; Illustrations
    /// holds {manager, node, container, control vector}. The vector is indexed by manager key.</summary>
    private static void ConfigureMediaClosure(TestMemory memory, MediaSpec spec, nuint node, nuint[] controls)
    {
        var keyed = controls.Skip(1).Append(controls[0]).ToArray();
        memory.AddPointer(Closure, Manager).AddPointer(Closure + 4, node);
        if (spec.Kind == MediaKind.Illustrations)
        {
            memory.AddPointer(Closure + 8, ViewerContainer).AddPointer(Closure + 0x0C, ControlVector);
        }
        else
        {
            memory.AddPointer(Closure + 8, ControlVector);
        }
        for (var index = 0; index < keyed.Length; index++)
        {
            memory.AddPointer(ControlVector + (nuint)(index * 4), keyed[index]);
        }
    }

    /// <summary>0x1D9F70: decide plays the track and 0x1DA600 shows Now Playing and its title;
    /// Cancel while playing calls the idle state 0x1DA820.</summary>
    private static void ConfigureSoundCallback(Harness harness) =>
        harness.Factory.SetOriginal<ExtrasSoundCallbackDelegate>(HookId.ExtrasSoundCallback, (_, eventType, action) =>
        {
            if (eventType == 0 && action < ExtrasHookSet.TrackCount)
            {
                harness.ObserveText(0x1A, 0x46, "Now Playing");
                harness.ObserveText(0x01, SoundSpec.TitleBase + action, RowTitle(SoundSpec, action));
                harness.Memory.AddInt32(Node + ExtrasHookSet.SoundPlayingOffset, 1);
                return;
            }
            if (eventType == 2 && ReadByte(harness.Memory, Node + ExtrasHookSet.SoundPlayingOffset) != 0)
            {
                harness.SoundIdle(Node);
            }
        });

    /// <summary>0x1D6160 opens the viewer 0x1D6390: one CustomButton bound at key 0 on its own
    /// manager and focused; its closure 0x1D6890 holds {container, page, ..., manager}.</summary>
    private static void ConfigureViewer(Harness harness, bool extraControl)
    {
        harness.Memory
            .AddPointer(ViewerManager, ImageBase + ExtrasHookSet.ManagerVtableRva)
            .AddInt32(ViewerManager + ExtrasHookSet.ManagerFocusKeyOffset, 0)
            .AddPointer(ViewerControl, ImageBase + ExtrasHookSet.CustomButtonVtableRva)
            .AddPointer(ViewerControl + 0x1000, ImageBase + ExtrasHookSet.CustomButtonVtableRva)
            .AddPointer(ViewerFocusState, ImageBase + ExtrasHookSet.FocusableStateVtableRva)
            .AddPointer(ViewerFocusState + ExtrasHookSet.FocusableStateControlOffset, ViewerControl)
            .AddPointer(ViewerClosure, ViewerContainer)
            .AddPointer(ViewerClosure + 4, Node)
            .AddPointer(ViewerClosure + 8, ViewerContainer)
            .AddPointer(ViewerClosure + 0x0C, ViewerManager);
        harness.Factory.SetOriginal<ExtrasIllustrationsCallbackDelegate>(HookId.ExtrasIllustrationsCallback, (_, eventType, action) =>
        {
            if (eventType != 0 || action >= ExtrasHookSet.IllustrationCount)
            {
                return;
            }
            harness.Set.AfterCustomButtonConstructed((nint)ViewerControl, (nint)ViewerControl);
            if (extraControl)
            {
                harness.Set.AfterCustomButtonConstructed((nint)(ViewerControl + 0x1000), (nint)(ViewerControl + 0x1000));
            }
            harness.Set.AfterControlBound((nint)ViewerManager, (nint)ViewerFocusState, 0);
            harness.Set.AfterFocusSet((nint)ViewerManager, 0);
        });
    }

    /// <summary>Enters a page through switchNode outside any transition; its switch original
    /// must already be configured, because hooks capture their originals at activation.</summary>
    private static void EnterMediaPage(Harness harness, MediaSpec spec, nuint node = Node)
    {
        harness.Memory.AddPointer(Scene + ExtrasHookSet.GalleryCurrentNodeOffset, node);
        harness.Switch(spec.SwitchAction);
        Enter(harness, spec, node);
    }

    private static void Enter(Harness harness, MediaSpec spec, nuint node)
    {
        switch (spec.Kind)
        {
            case MediaKind.Movies:
                harness.MoviesEnter(node);
                break;
            case MediaKind.Sound:
                harness.SoundEnter(node);
                break;
            default:
                harness.IllustrationsEnter(node);
                break;
        }
    }

    private static byte ReadByte(TestMemory memory, nuint address)
    {
        var value = new byte[1];
        Assert.True(memory.TryRead(address, value));
        return value[0];
    }

    private enum MediaKind
    {
        Movies,
        Sound,
        Illustrations,
    }

    private sealed record MediaSpec(
        MediaKind Kind,
        uint Vtable,
        int Rows,
        int TitleBank,
        uint TitleTable,
        int TitleStride,
        int TitleBase,
        int StatusMessage,
        string Status,
        int SwitchAction,
        int TitleMessage,
        string Title,
        string RowName);
}
