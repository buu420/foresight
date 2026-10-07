using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Mod.Racing;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Racing;

public sealed class BikeRaceRuntimeTests
{
    private sealed class Rig
    {
        public long Now;
        public bool Focus = true, Key;
        public int Entries, Stops;
        public List<string> Speech = [], Log = [];
        public BikeRaceSnapshot? Frame = new(BikeRacePhase.Racing, BikeRaceResult.Unknown, 800, 0, true, 3, true, -1, BikeRaceLane.Above);
        public BikeRaceRuntime Runtime;
        public Rig() => Runtime = new(_ => Frame, new(Speech.Add, _ => { }, () => Stops++),
            () => Focus, () => Key, () => Now, () => Entries++, Log.Add);
        public void Tick() { Now += 100; Runtime.Update(42); }
    }

    [Fact] public void EnterCancelsFieldNavigationOnceAndKeyboardRepeatsOnFreshPress()
    {
        var r = new Rig { Key = true }; r.Tick(); r.Tick();
        Assert.True(r.Runtime.HasContext); Assert.Equal(1, r.Entries); Assert.Single(r.Speech);
        r.Key = false; r.Tick(); r.Key = true; r.Tick(); r.Tick();
        Assert.Equal(2, r.Speech.Count); Assert.Contains("800", r.Speech.Last());
    }
    [Fact] public void RightStickRepeatsWithoutOpeningFieldMenuOrHoldingAnyGameInput()
    {
        var r = new Rig(); r.Tick();
        r.Runtime.ObserveController(0, NavigationPadButtons.None, true);
        r.Runtime.ObserveController(0, NavigationPadButtons.RightStick, true); r.Tick();
        r.Runtime.ObserveController(0, NavigationPadButtons.RightStick, true); r.Tick();
        Assert.Equal(2, r.Speech.Count);
        r.Runtime.ObserveController(0, NavigationPadButtons.None, false);
        r.Runtime.ObserveController(0, NavigationPadButtons.RightStick, true); r.Tick();
        Assert.Equal(2, r.Speech.Count); // reconnect-held button is fenced until released
    }
    [Fact] public void CaptureFailureIsReportedOnceAfterStartupGraceAndRecovers()
    {
        var r = new Rig { Frame = null }; r.Tick(); Assert.Empty(r.Speech);
        r.Now = 1500; r.Tick(); r.Tick();
        Assert.Single(r.Speech); Assert.Contains("unavailable", r.Speech.Single());
        Assert.Contains(r.Log, text => text.Contains("capture unavailable"));
        r.Frame = new(BikeRacePhase.Racing, BikeRaceResult.Unknown, 500, 10, true, 2, false, 1, BikeRaceLane.Aligned);
        r.Tick(); Assert.Contains("500", r.Speech.Last());
    }
    [Fact] public void FocusReturnDoesNotReplayAButtonHeldWhileUnfocused()
    {
        var r = new Rig(); r.Tick(); r.Focus = false; r.Key = true; r.Tick();
        r.Runtime.ObserveController(0, NavigationPadButtons.RightStick, true);
        r.Focus = true; r.Tick(); var count = r.Speech.Count; r.Tick();
        Assert.Equal(count, r.Speech.Count); Assert.True(r.Stops > 0);
    }
    [Fact] public void DestructorIsScopedAndClosingResetsNextRace()
    {
        var r = new Rig(); r.Tick(); r.Runtime.Close(99); Assert.True(r.Runtime.HasContext);
        r.Runtime.Close(42); Assert.False(r.Runtime.HasContext); Assert.True(r.Stops > 0);
        r.Tick(); Assert.Equal(2, r.Entries); Assert.Contains("Bike race", r.Speech.Last());
    }
}
