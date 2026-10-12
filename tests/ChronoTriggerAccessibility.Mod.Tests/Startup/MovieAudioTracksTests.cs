using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

public sealed class MovieAudioTracksTests
{
    [Fact]
    public void BothSoundtrackAndNarrationAreSelectedWithoutChangingVideo()
    {
        var descriptor = new Descriptor(false, true);
        Assert.True(MovieAudioTracks.SelectTogether(descriptor));
        Assert.Equal(new[] { true, true, true }, descriptor.Selected);
        Assert.Equal(new[] { 0 }, descriptor.Changes);
    }

    [Fact]
    public void FailedSelectionRestoresOriginalChoice()
    {
        var descriptor = new Descriptor(false, false) { FailIndex = 1 };
        Assert.Throws<InvalidOperationException>(() => MovieAudioTracks.SelectTogether(descriptor));
        Assert.Equal(new[] { false, false, true }, descriptor.Selected);
    }

    [Fact]
    public void UnexpectedMovieLayoutIsLeftUntouched()
    {
        var descriptor = new Descriptor(false, true) { Types = [MovieStreamKind.Audio, MovieStreamKind.Video] };
        Assert.False(MovieAudioTracks.SelectTogether(descriptor));
        Assert.Empty(descriptor.Changes);
    }

    [Fact]
    public void PlaybackReadinessRequiresSuccessfulCurrentMovieAndClearsForNextMovie()
    {
        var playback = new MovieAudioPlayback(_ => { }, _ => { });
        Assert.Equal(0, playback.Build(() => new Descriptor(false, true), () => "001.dat", () => 0));
        Assert.True(playback.OpeningReady);
        Assert.Equal(0, playback.Build(() => new Descriptor(false, true), () => null, () => 0));
        Assert.False(playback.OpeningReady);
        playback.Build(() => new Descriptor(false, true), () => "002.dat", () => 0);
        Assert.False(playback.OpeningReady);
    }

    [Fact]
    public void MovieSelectionFailureIsReportedLocallyAndStillCallsOriginal()
    {
        var messages = new List<string>();
        var playback = new MovieAudioPlayback(_ => { }, messages.Add);
        var calls = 0;
        Assert.Equal(0, playback.Build(() => new Descriptor(false, true) { FailIndex = 0 },
            () => "001.dat", () => { calls++; return 0; }));
        Assert.Equal(1, calls);
        Assert.False(playback.OpeningReady);
        Assert.Single(messages);
    }

    [Fact]
    public void NativeTopologyFailureRestoresOriginalAudioAndRetriesWithoutSilencingFallback()
    {
        var descriptor = new Descriptor(false, true);
        var messages = new List<string>();
        var playback = new MovieAudioPlayback(_ => { }, messages.Add);
        var calls = 0;
        Assert.Equal(0, playback.Build(() => descriptor, () => "001.dat", () =>
        {
            if (++calls == 1) return -1;
            Assert.Equal(new[] { false, true, true }, descriptor.Selected);
            return 0;
        }));
        Assert.Equal(2, calls);
        Assert.False(playback.OpeningReady);
        Assert.Single(messages);
    }

    [Fact]
    public void SeparatePackMovieKeepsBothTracksWithoutInjectedNarration()
    {
        var descriptor = new Descriptor(false, true);
        var narrations = 0;
        var playback = new MovieAudioPlayback(_ => { }, _ => { });
        Assert.Equal(0, playback.Build(() => descriptor, () => "002.dat", () => 0, _ =>
        {
            narrations++;
            return 0;
        }));
        Assert.Equal(0, narrations);
        Assert.Equal(new[] { true, true, true }, descriptor.Selected);
    }

    [Fact]
    public void NarratedOriginalMovieReplacesOnlyTheOriginalBuild()
    {
        var original = new Descriptor(true, true) { Types = [MovieStreamKind.Audio, MovieStreamKind.Video] };
        var calls = 0;
        var playback = new MovieAudioPlayback(_ => { }, _ => { });
        Assert.Equal(5, playback.Build(() => original, () => null, () => { calls++; return 0; }, presentation =>
        {
            Assert.Same(original, presentation);
            return 5;
        }));
        Assert.Equal(0, calls);
        Assert.False(playback.OpeningReady);
    }

    [Fact]
    public void DeclinedOrFailedNarrationBuildsTheOriginalTopologyOnce()
    {
        // The public opening and undeclared movies decline; a faulty narrator must not reach game code.
        var original = new Descriptor(true, true) { Types = [MovieStreamKind.Audio, MovieStreamKind.Video] };
        var messages = new List<string>();
        var playback = new MovieAudioPlayback(_ => { }, messages.Add);
        var calls = 0;
        Assert.Equal(3, playback.Build(() => original, () => null, () => { calls++; return 3; }, _ => null));
        Assert.Equal(1, calls);
        Assert.Empty(messages);
        Assert.Equal(3, playback.Build(() => original, () => null, () => { calls++; return 3; },
            _ => throw new InvalidOperationException("narrator fault")));
        Assert.Equal(2, calls);
        Assert.Single(messages);
        Assert.False(playback.OpeningReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalMoviePreparationLeavesOtherHooksAvailable(bool separatePack)
    {
        var root = Path.Combine(Path.GetTempPath(), "ct-movie-hook-" + Guid.NewGuid());
        var messages = new List<string>();
        try
        {
            if (separatePack)
            {
                var folder = Directory.CreateDirectory(Path.Combine(root, "Accessibility", "AudioDescriptions"));
                File.WriteAllText(Path.Combine(folder.FullName, "installed-movies.json"), JsonSerializer.Serialize(new
                {
                    version = 2, playback = "simultaneous-separate-tracks",
                    movies = new[] { new { fileName = "001.dat", narration = true, decodedSha256 = new string('A', 64),
                        audioTrackCount = 2, originalAudioVerified = true, videoVerified = true } }
                }));
            }
            // No game DLL or hook controller in the test host. This optional
            // registration must return inactive, rather than aborting the installer.
            var registration = new MovieAudioTracksHook(null!, root, _ => { }, messages.Add, () => []);
            var prepared = registration.Prepare(null!, null!);
            prepared.Activate();
            Assert.False(prepared.IsActive);
            Assert.False(registration.OpeningReady);
            Assert.Equal(separatePack ? 1 : 0, messages.Count);
            prepared.Disable();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PublicNarrationPreparesTheMovieHookWithoutASeparatePack()
    {
        // Public installs have no movie pack. Declared narration must still prepare the optional
        // hook; this test host has no game DLL, so preparation reports and stays inactive.
        var track = MovieNarrationTrack.Create(MovieNarrationCatalog.Originals[0],
            MovieNarrationResourceTests.Wave(24000, 938536));
        var messages = new List<string>();
        var registration = new MovieAudioTracksHook(null!, Path.Combine(Path.GetTempPath(), "ct-no-pack-" + Guid.NewGuid()),
            _ => { }, messages.Add, () => [track]);
        var prepared = registration.Prepare(null!, null!);
        prepared.Activate();
        Assert.False(prepared.IsActive);
        Assert.Equal([MovieNarrationInjector.UnavailableMessage], messages);
        prepared.Disable();
    }

    [Fact]
    public void BrokenNarrationResourcesAreReportedWithoutFailingPreparation()
    {
        var messages = new List<string>();
        var registration = new MovieAudioTracksHook(null!, Path.Combine(Path.GetTempPath(), "ct-no-pack-" + Guid.NewGuid()),
            _ => { }, messages.Add, () => throw new InvalidDataException("changed recording"));
        var prepared = registration.Prepare(null!, null!);
        prepared.Activate();
        Assert.False(prepared.IsActive);
        Assert.Equal([MovieNarrationInjector.UnavailableMessage], messages);
    }

    private sealed class Descriptor(bool first, bool second) : IMoviePresentation
    {
        public MovieStreamKind[] Types { get; init; } = [MovieStreamKind.Audio, MovieStreamKind.Audio, MovieStreamKind.Video];
        public bool[] Selected { get; } = [first, second, true];
        public List<int> Changes { get; } = [];
        public int FailIndex { get; init; } = -1;
        public int Count => Types.Length;
        public (MovieStreamKind Kind, bool Selected) GetStream(int index) => (Types[index], Selected[index]);
        public void Select(int index, bool selected)
        {
            if (index == FailIndex && selected) throw new InvalidOperationException("selection failed");
            Selected[index] = selected;
            Changes.Add(index);
        }
    }
}
