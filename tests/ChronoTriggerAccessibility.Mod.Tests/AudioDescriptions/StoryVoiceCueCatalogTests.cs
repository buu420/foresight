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
        Assert.Equal(172, StoryVoiceCueCatalog.All.Count);
        Assert.Equal(172, StoryVoiceCueCatalog.All.Select(cue => cue.Id).Distinct().Count());
        Assert.Equal(150, StoryVoiceCueCatalog.All.Count(cue => !cue.Id.StartsWith("movie-opening-")));
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

    public static TheoryData<string, string> OceanCues => new()
    {
        { "scene-munespinsandvanishes", "Mune spins around and vanishes." },
        { "scene-masaspinsandvanishes", "Masa spins around and vanishes." },
        { "scene-cronoraiseshandstomachine-named", "Crono raises both hands toward the machine." },
        { "scene-cronoraiseshandstomachine-appearance", "The red-haired boy raises both hands toward the machine." },
        { "scene-kniferises", "A glowing red knife rises." },
        { "scene-knifeshootsintomachine", "The knife shoots up into the machine." },
        { "scene-swordappearsinmachine", "A sword appears in the machine." },
        { "scene-schalaholdsouthands", "Schala holds out both hands." },
        { "scene-partygoneschalakneels", "Schala kneels alone, head bowed." },
        { "scene-magusstandswhereprophetwas", "The hooded Prophet is gone. Magus stands in his place." },
        { "scene-schalaknockedtoknees", "Schala drops to her knees." },
        { "scene-magusthrowndown", "Magus is thrown backward, face down." },
        { "scene-cronoraisesarmsbeforelavos-named", "Crono raises both arms toward Lavos." },
        { "scene-cronoraisesarmsbeforelavos-appearance", "The red-haired boy raises both arms toward Lavos." },
        { "scene-cronocrumbles-named", "Crono turns into a dark silhouette and crumbles away." },
        { "scene-cronocrumbles-appearance", "The red-haired boy turns into a dark silhouette and crumbles away." },
    };

    [Theory]
    [MemberData(nameof(OceanCues))]
    public void EachOceanPalaceActionHasItsExactNewRecordedCueDefinition(string id, string text)
    {
        Assert.Equal(text, Assert.Single(StoryVoiceCueCatalog.All, cue => cue.Id == id).Text);
        Assert.Equal(16, StoryVoiceCueCatalog.All.Count(cue => OceanCues.Any(row => (string)row[0] == cue.Id)));
    }

    [Fact]
    public void EveryShippedRecordingStillHasItsUnchangedCaption()
    {
        using var stream = typeof(StoryVoiceCueCatalog).Assembly.GetManifestResourceStream(
            StoryVoicePack.ResourcePrefix + "manifest.json")!;
        using var manifest = System.Text.Json.JsonDocument.Parse(stream);
        var shipped = manifest.RootElement.GetProperty("cues").EnumerateArray()
            .Select(cue => (cue.GetProperty("id").GetString()!, cue.GetProperty("text").GetString()!)).ToArray();
        Assert.True(shipped.Length >= 156);
        var all = StoryVoiceCueCatalog.All.Select(cue => (cue.Id, cue.Text)).ToHashSet();
        Assert.All(shipped, cue => Assert.Contains(cue, all));
    }

    [Theory]
    [InlineData(StorySceneAction.CronoRaisesHandsToMachine, "scene-cronoraiseshandstomachine")]
    [InlineData(StorySceneAction.CronoRaisesArmsBeforeLavos, "scene-cronoraisesarmsbeforelavos")]
    [InlineData(StorySceneAction.CronoCrumbles, "scene-cronocrumbles")]
    public void CronosOceanPalaceRecordingsNeverSpeakADefaultNameForACustomOne(StorySceneAction action, string id)
    {
        var before = StoryActionRuntimeTests.Frame() with { StoryPoint = 0xC9 };
        var candidate = new StorySceneActionCandidate(action, before, before.ActorState, null, false);
        var custom = StoryVoiceCueCatalog.ForScene(candidate, StoryActionCatalog.DescribeScene(candidate, _ => "Cinder"));
        Assert.Equal(id + "-appearance", custom.CueId);
        Assert.StartsWith("Cinder ", custom.Text);
        Assert.StartsWith("The red-haired boy ", custom.VoiceText);
        var named = StoryVoiceCueCatalog.ForScene(candidate, StoryActionCatalog.DescribeScene(candidate, _ => "Crono"));
        Assert.Equal(id + "-named", named.CueId);
        Assert.Equal(named.Text, named.VoiceText);
        Assert.True(StoryVoiceCueCatalog.IsRecorded(action));
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
