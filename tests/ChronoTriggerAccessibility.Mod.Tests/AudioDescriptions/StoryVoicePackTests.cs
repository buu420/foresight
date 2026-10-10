using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

public sealed class StoryVoicePackTests
{
    [Fact]
    public void EveryNativeDescriptionHasItsVerifiedRecordedPcmInTheBuiltMod()
    {
        var pack = StoryVoicePack.Load(); Assert.Equal(124, pack.Count);
        foreach (var cue in StoryVoiceCueCatalog.All)
        {
            var clip = pack.Get(new(cue.Text, cue.Id, cue.Text));
            Assert.Equal(cue.Text, clip.Text); Assert.InRange(clip.Duration, 0.1, 20);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(clip.Wave, 0, 4));
        }
    }

    [Fact]
    public void AnAlteredRecordingIsRejectedBeforePlayback()
    {
        Assert.Throws<InvalidDataException>(() => StoryVoicePack.Load(name =>
        {
            using var source = Read(name); if (source is null) return null;
            using var copy = new MemoryStream(); source.CopyTo(copy); var data = copy.ToArray();
            if (name.EndsWith("party-00-16-named.wav", StringComparison.Ordinal)) data[^1] ^= 1;
            return new MemoryStream(data);
        }));
    }

    [Fact]
    public void APartialVoicePackIsRejectedInsteadOfSilentlyLosingDescriptions()
    {
        Assert.Throws<InvalidDataException>(() => StoryVoicePack.Load(name =>
            name.EndsWith("party-00-16-named.wav", StringComparison.Ordinal) ? null : Read(name)));
    }

    [Fact]
    public void AChangedCaptionCannotSelectAnUnrelatedRecording()
    {
        var pack = StoryVoicePack.Load();
        Assert.Throws<InvalidDataException>(() => pack.Get(new("Crono nods.", "party-00-16-named", "Crono laughs.")));
    }

    private static Stream? Read(string name) => typeof(StoryVoicePack).Assembly.GetManifestResourceStream(name);
}
