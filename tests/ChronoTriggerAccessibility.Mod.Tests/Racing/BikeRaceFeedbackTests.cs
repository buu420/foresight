using ChronoTriggerAccessibility.Mod.Racing;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Racing;

public sealed class BikeRaceFeedbackTests
{
    private static BikeRaceSnapshot Running => new(BikeRacePhase.Racing, BikeRaceResult.Unknown,
        800, 12, true, 3, true, -1, BikeRaceLane.Above);

    [Fact] public void DescribesEntryControlsAndReportsNativeStart()
    {
        var speech = new List<string>();
        var feedback = new BikeRaceFeedback(speech.Add, _ => { }, () => { });
        feedback.Update(Running with { Phase = BikeRacePhase.Preparing }, 0, true);
        Assert.Contains("Up and down", string.Join(" ", speech));
        Assert.Contains("Preparing", speech.Last());
        feedback.Update(Running, 3000, true);
        Assert.Contains("Go", speech.Last());
    }
    [Fact] public void PauseStopsTonesAndAnnouncesFreshStatusOnResume()
    {
        var speech = new List<string>(); var tones = new List<BikeRaceLane>(); var stops = 0;
        var feedback = new BikeRaceFeedback(speech.Add, tones.Add, () => stops++);
        feedback.Update(Running, 0, true);
        feedback.Update(Running with { Paused = true }, 1000, true);
        feedback.Update(Running with { Paused = true }, 2000, true);
        Assert.Single(tones); Assert.Contains("paused", speech.Last()); Assert.Equal(2, speech.Count);
        feedback.Update(Running with { Distance = 200 }, 3000, true);
        Assert.Equal(2, tones.Count); Assert.Contains("200", speech.Last()); Assert.True(stops > 0);
    }
    [Fact] public void AudioFailureWaitsForFocusedRaceAndIsAnnouncedOnce()
    {
        var speech = new List<string>(); var tones = new List<BikeRaceLane>(); var audioWorks = true;
        var feedback = new BikeRaceFeedback(speech.Add, tones.Add, () => { }, () => audioWorks);
        feedback.Update(Running, 0, true); audioWorks = false;
        feedback.Update(Running, 1000, false); Assert.Single(speech);
        feedback.Update(Running, 2000, true); Assert.Contains("tones are unavailable", speech.Last());
        Assert.Single(tones); var count = speech.Count;
        feedback.Update(Running, 4000, true); Assert.Equal(count, speech.Count);
    }

    [Fact] public void TonesContinueWithoutSpeechFloodAndFollowJohnny()
    {
        var speech = new List<string>(); var tones = new List<BikeRaceLane>();
        var feedback = new BikeRaceFeedback(speech.Add, tones.Add, () => { });
        feedback.Update(Running, 0, true);
        for (var n = 1; n <= 20; n++) feedback.Update(Running, n * 50, true);
        Assert.Single(speech);
        Assert.InRange(tones.Count, 2, 4);
        feedback.Update(Running with { JohnnyLane = BikeRaceLane.Below }, 1400, true);
        Assert.Equal(BikeRaceLane.Below, tones.Last());
    }

    [Fact] public void AnnouncesBoostUseRechargeAndDistanceMilestonesWithoutInventingUnits()
    {
        var speech = new List<string>();
        var feedback = new BikeRaceFeedback(speech.Add, _ => { }, () => { });
        feedback.Update(Running, 0, true);
        feedback.Update(Running with { Boosts = 2, BoostReady = false, Distance = 590 }, 2000, true);
        Assert.Contains("2 boosts", speech.Last());
        Assert.Contains("600", speech.Last());
        feedback.Update(Running with { Boosts = 2, BoostReady = true, Distance = 500 }, 4200, true);
        Assert.Contains("Boost ready", speech.Last());
    }

    [Fact] public void DiscardsTransientLeadChangesAndAnnouncesStableLead()
    {
        var speech = new List<string>();
        var feedback = new BikeRaceFeedback(speech.Add, _ => { }, () => { });
        feedback.Update(Running, 0, true);
        feedback.Update(Running with { Lead = 1 }, 2000, true);
        feedback.Update(Running, 2200, true);
        Assert.Single(speech);
        feedback.Update(Running with { Lead = 1 }, 3000, true);
        feedback.Update(Running with { Lead = 1 }, 3700, true);
        Assert.Contains("You lead", speech.Last());
    }
    [Fact] public void BoostAnnouncementDoesNotSilentlyConsumeAPendingLeadChange()
    {
        var speech = new List<string>();
        var feedback = new BikeRaceFeedback(speech.Add, _ => { }, () => { });
        feedback.Update(Running, 0, true);
        var leading = Running with { Lead = 1, Boosts = 2, BoostReady = false };
        feedback.Update(leading, 2000, true);
        Assert.DoesNotContain("You lead", speech.Last());
        feedback.Update(leading, 3700, true);
        Assert.Contains("You lead", speech.Last());
    }

    [Fact] public void LossIsOnlyAnnouncedFromNativeResultAndStopsTones()
    {
        var speech = new List<string>(); var tones = new List<BikeRaceLane>(); var stops = 0;
        var feedback = new BikeRaceFeedback(speech.Add, tones.Add, () => stops++);
        feedback.Update(Running with { Distance = 0 }, 0, true);
        Assert.DoesNotContain(speech, text => text.Contains("lost", StringComparison.OrdinalIgnoreCase));
        var done = Running with { Phase = BikeRacePhase.Finished, Result = BikeRaceResult.Lost };
        feedback.Update(done, 2000, true); feedback.Update(done, 3000, true);
        Assert.Contains("Johnny wins", speech.Last());
        Assert.Equal(2, speech.Count); Assert.Single(tones); Assert.True(stops > 0);
    }

    [Fact] public void FocusLossAndMissingCaptureStopTonesAndNeverReplayStaleStatus()
    {
        var speech = new List<string>(); var tones = new List<BikeRaceLane>(); var stops = 0;
        var feedback = new BikeRaceFeedback(speech.Add, tones.Add, () => stops++);
        feedback.Update(Running, 0, true);
        feedback.Update(Running, 1000, false, true);
        feedback.Update(null, 2000, true, true);
        Assert.Single(tones); Assert.True(stops >= 2);
        Assert.Contains("unavailable", speech.Last());
        feedback.Update(Running with { Distance = 500, BoostsEnabled = false }, 4000, true, true);
        Assert.Contains("500", speech.Last()); Assert.Contains("No boosts", speech.Last());
    }

    [Fact] public void CloseResetsRetryAndRepeatReadsStatusEvenWithoutAChange()
    {
        var speech = new List<string>();
        var feedback = new BikeRaceFeedback(speech.Add, _ => { }, () => { });
        feedback.Update(Running, 0, true);
        feedback.Update(Running, 100, true, true);
        Assert.Contains("Johnny above", speech.Last()); Assert.Equal(2, speech.Count);
        Assert.Contains("Score 12", speech.Last());
        feedback.Close(); feedback.Update(Running, 500, true);
        Assert.Equal(3, speech.Count); Assert.Contains("Bike race", speech.Last());
    }
}
