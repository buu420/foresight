using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Core.Startup;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

public sealed class StoryVoiceCueCatalogTests
{
    [Fact]
    public void EveryExistingOpeningDescriptionHasAnExactVoiceCue()
    {
        foreach (var (entry, index) in OpeningMovieTimeline.Entries.Select((entry, index) => (entry, index)))
        {
            var cue = StoryVoiceCueCatalog.ForOpening(entry.Text);
            Assert.NotNull(cue); Assert.Equal(entry.Text, cue.Text); Assert.Equal(entry.Text, cue.VoiceText);
            Assert.Equal($"movie-opening-{index:D2}", cue.CueId);
        }
        Assert.Null(StoryVoiceCueCatalog.ForOpening("An unreviewed description."));
    }
    [Fact]
    public void EveryReviewedGestureAndSpecificSceneHasARecordedCue()
    {
        Assert.Equal(146, StoryVoiceCueCatalog.All.Count);
        Assert.Equal(146, StoryVoiceCueCatalog.All.Select(cue => cue.Id).Distinct().Count());
        Assert.Equal(124, StoryVoiceCueCatalog.All.Count(cue => !cue.Id.StartsWith("movie-opening-")));
        Assert.All(StoryVoiceCueCatalog.All, cue =>
        {
            Assert.Matches("^[a-z0-9-]+$", cue.Id);
            Assert.False(string.IsNullOrWhiteSpace(cue.Text));
        });
    }

    [Fact]
    public void ADefaultIntroducedNameHasItsWholePhraseInTheAuthorsVoice()
    {
        var cue = StoryVoiceCueCatalog.ForAnimation(StoryActionRuntimeTests.Frame(), 0x16, "Crono nods.");
        Assert.Equal("party-00-16-named", cue.CueId);
        Assert.Equal("Crono nods.", cue.VoiceText);
    }

    [Fact]
    public void ACustomNameUsesVisibleAppearanceInsteadOfSpeakingTheWrongDefaultName()
    {
        var cue = StoryVoiceCueCatalog.ForAnimation(StoryActionRuntimeTests.Frame(), 0x16, "Cinder nods.");
        Assert.Equal("Cinder nods.", cue.Text);
        Assert.Equal("party-00-16-appearance", cue.CueId);
        Assert.Equal("The red-haired boy nods.", cue.VoiceText);
    }

    [Fact]
    public void TheUnintroducedGirlNeverUsesHerNamedRecording()
    {
        var frame = StoryActionRuntimeTests.Frame() with
        { StoryPoint = 3, ActorState = StoryActionRuntimeTests.Actor() with { Visual = 1 } };
        var text = StoryActionCatalog.Describe(frame, 0x16, _ => "Marle")!;
        var cue = StoryVoiceCueCatalog.ForAnimation(frame, 0x16, text);
        Assert.Equal("party-01-16-appearance", cue.CueId);
        Assert.Equal("The blonde girl nods.", cue.VoiceText);
    }

    [Fact]
    public void TheFairCollisionAlsoHandlesThePlayersCustomBoyName()
    {
        var before = StoryActionRuntimeTests.Frame();
        var candidate = new StorySceneActionCandidate(StorySceneAction.FairCollision,
            before, before.ActorState, before.ActorState with { Visual = 1 }, false);
        var text = StoryActionCatalog.DescribeScene(candidate, _ => "Cinder");
        var cue = StoryVoiceCueCatalog.ForScene(candidate, text);
        Assert.Equal("scene-faircollision-appearance", cue.CueId);
        Assert.Equal("The red-haired boy and a blonde girl bump into each other and fall to the ground.", cue.VoiceText);
    }
}
