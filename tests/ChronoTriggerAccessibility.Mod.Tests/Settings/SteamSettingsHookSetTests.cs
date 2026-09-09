using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Settings;

/// <summary>
/// Behavioural regressions for the Steam Settings resolution selector lifecycle.
///
/// The native contract these encode is <c>ResolutionCallback</c> at RVA 0x1FB100:
///   * event 0 with the key equal to the LIVE manager focus key at [[context]+0x2C4]
///     applies the selection, calls the page builder (RVA 0x1F0310) re-entrantly,
///     and then closes the selector through the owning control's vtable + 0x134;
///   * event 0 with any other key only moves focus;
///   * event 1 moves focus;
///   * event 2 does nothing at all, so the selector stays open.
/// </summary>
public sealed class SteamSettingsHookSetTests
{
    [Fact]
    public void TitlePresentationDoesNotRaceASlowNativeCaller()
    {
        var harness = SteamSettingsHarness.Create();
        harness.PrepareAndActivate();
        harness.Construct();
        harness.BuildPage();

        Assert.False(harness.Dispatcher.FirstEvent.Wait(TimeSpan.FromSeconds(1)));
        harness.Set.FlushDeferredPresentation();
        Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Snapshot()));
    }

    [Fact]
    public void FirstPresentationWaitsForItsCallerAndIsPublishedExactlyOnce()
    {
        var harness = Open(out _);

        Assert.Empty(harness.Dispatcher.Snapshot());

        harness.Set.FlushDeferredPresentation();

        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Snapshot()));
        Assert.Equal("Display Settings", presented.Title);

        // Repeated completion notifications must not repeat the presentation.
        harness.Set.FlushDeferredPresentation();
        Assert.Single(harness.Dispatcher.Snapshot());
        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    [Fact]
    public void InGamePresentationStillReachesThePlayerWhenNoCallerFlushesIt()
    {
        var harness = SteamSettingsHarness.Create(context: (int)SteamSettingsContext.InGame);
        harness.PrepareAndActivate();
        harness.InstallResolutionCallbackContext();
        harness.Construct((int)SteamSettingsContext.InGame);
        harness.BuildPage();

        // Losing the page description entirely is worse than announcing it late.
        WaitUntil(() => harness.Dispatcher.Snapshot().Length == 1);

        var presented = Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Snapshot()));
        Assert.Equal("Display Settings", presented.Title);
        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    [Fact]
    public void ApplyingAResolutionResumesWithExactlyOneRebuiltPageAnnouncement()
    {
        var harness = Open(out _);
        harness.Set.FlushDeferredPresentation();
        harness.BuildResolutionSelector("Screen Size", "1280 x 720", "1920 x 1080");

        var beforeApply = harness.Dispatcher.Snapshot().Length;
        harness.Fixture.SetSelectorFocusKey(0);
        harness.ResolutionCallback(eventType: 0, key: 0, rebuildsPage: true);

        AssertNoCoverageFailures(harness);
        var published = harness.Dispatcher.Snapshot().Skip(beforeApply).ToArray();
        var resumed = Assert.IsType<MenuPresented>(Assert.Single(published));
        Assert.Equal("Display Settings", resumed.Title);
        Assert.Equal("Screen Mode", resumed.Focus!.Label);
        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    [Fact]
    public void ApplyingAResolutionFollowsTheLiveManagerFocusKeyRatherThanACachedOne()
    {
        var harness = Open(out _);
        harness.Set.FlushDeferredPresentation();
        harness.BuildResolutionSelector("Screen Size", "1280 x 720", "1920 x 1080", "2560 x 1440");

        // The selector opens focused on the resolution already in use. Nothing in the
        // builder reports that, so the cached focus key and the live manager key differ.
        harness.Fixture.SetSelectorFocusKey(2);
        var beforeApply = harness.Dispatcher.Snapshot().Length;

        harness.ResolutionCallback(eventType: 0, key: 2, rebuildsPage: true);

        AssertNoCoverageFailures(harness);
        var published = harness.Dispatcher.Snapshot().Skip(beforeApply).ToArray();
        var resumed = Assert.IsType<MenuPresented>(Assert.Single(published));
        Assert.Equal("Display Settings", resumed.Title);
        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    [Fact]
    public void MovingSelectorFocusIsNarratedWithoutClosingTheSelector()
    {
        var harness = Open(out _);
        harness.Set.FlushDeferredPresentation();
        harness.BuildResolutionSelector("Screen Size", "1280 x 720", "1920 x 1080");
        harness.Fixture.SetSelectorFocusKey(0);

        var beforeMove = harness.Dispatcher.Snapshot().Length;
        harness.ResolutionCallback(eventType: 1, key: 1);

        AssertNoCoverageFailures(harness);
        var moved = Assert.IsType<MenuFocusChanged>(
            Assert.Single(harness.Dispatcher.Snapshot().Skip(beforeMove)));
        Assert.Equal("1920 x 1080", moved.Focus!.Label);
        Assert.Equal(2, moved.Focus.Count);
        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    [Fact]
    public void RejectedResolutionDoesNotAnnounceAnUnchangedPageOrCloseTheSelector()
    {
        var harness = Open(out _);
        harness.Set.FlushDeferredPresentation();
        harness.BuildResolutionSelector("Screen Size", "1280 x 720", "1920 x 1080");
        harness.Fixture.SetSelectorFocusKey(0);
        var beforeApply = harness.Dispatcher.Snapshot().Length;

        // The native mode/size compatibility check can return before rebuilding
        // the page, even when the requested key is the live selected entry.
        harness.ResolutionCallback(eventType: 0, key: 0, rebuildsPage: false);

        Assert.Empty(harness.Dispatcher.Snapshot().Skip(beforeApply));
        harness.ResolutionCallback(eventType: 1, key: 1);
        Assert.IsType<MenuFocusChanged>(Assert.Single(harness.Dispatcher.Snapshot().Skip(beforeApply)));
        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    [Fact]
    public void ObservedPageRebuildResumesSpeechEvenWhenTheOldSelectorCannotBeRead()
    {
        var harness = Open(out _);
        harness.Set.FlushDeferredPresentation();
        harness.BuildResolutionSelector("Screen Size", "1280 x 720", "1920 x 1080");
        harness.Fixture.Memory.Add(SteamSettingsFixture.SelectorManager, []);
        var beforeApply = harness.Dispatcher.Snapshot().Length;

        harness.ResolutionCallback(eventType: 0, key: 1, rebuildsPage: true);

        Assert.IsType<MenuPresented>(Assert.Single(harness.Dispatcher.Snapshot().Skip(beforeApply)));
        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    [Fact]
    public void CancellingTheSelectorLeavesItOpenBecauseTheGameDoesNothing()
    {
        var harness = Open(out _);
        harness.Set.FlushDeferredPresentation();
        harness.BuildResolutionSelector("Screen Size", "1280 x 720", "1920 x 1080");
        harness.Fixture.SetSelectorFocusKey(0);

        // ResolutionCallback event 2 returns without touching any state, so the
        // selector is still the visible surface afterwards.
        var beforeCancel = harness.Dispatcher.Snapshot().Length;
        harness.ResolutionCallback(eventType: 2, key: 0);
        Assert.Empty(harness.Dispatcher.Snapshot().Skip(beforeCancel));

        var beforeMove = harness.Dispatcher.Snapshot().Length;
        harness.ResolutionCallback(eventType: 1, key: 1);

        AssertNoCoverageFailures(harness);
        var moved = Assert.IsType<MenuFocusChanged>(
            Assert.Single(harness.Dispatcher.Snapshot().Skip(beforeMove)));
        Assert.Equal("1920 x 1080", moved.Focus!.Label);
        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    [Fact]
    public void RepeatedResolutionChangesKeepNarratingEveryRebuiltPage()
    {
        var harness = Open(out _);
        harness.Set.FlushDeferredPresentation();

        for (var round = 0; round < 3; round++)
        {
            harness.BuildResolutionSelector("Screen Size", "1280 x 720", "1920 x 1080");
            harness.Fixture.SetSelectorFocusKey(1);

            var beforeApply = harness.Dispatcher.Snapshot().Length;
            harness.ResolutionCallback(eventType: 0, key: 1, rebuildsPage: true);

            AssertNoCoverageFailures(harness);
            var resumed = Assert.IsType<MenuPresented>(
                Assert.Single(harness.Dispatcher.Snapshot().Skip(beforeApply)));
            Assert.Equal("Display Settings", resumed.Title);
        }

        Assert.Empty(harness.Dispatcher.FailureSnapshot());
    }

    private static SteamSettingsHarness Open(out SteamSettingsFixture fixture)
    {
        var harness = SteamSettingsHarness.Create();
        harness.PrepareAndActivate();
        harness.InstallResolutionCallbackContext();
        harness.Construct((int)SteamSettingsContext.Title);
        harness.BuildPage();
        AssertNoCoverageFailures(harness);
        fixture = harness.Fixture;
        return harness;
    }

    private static void AssertNoCoverageFailures(SteamSettingsHarness harness)
    {
        var failures = harness.Dispatcher.FailureSnapshot();
        Assert.True(
            failures.Length == 0,
            "Steam Settings reported coverage failures:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            Thread.Sleep(5);
        }
        Assert.True(condition(), "The deferred Settings presentation never reached the dispatcher.");
    }
}
