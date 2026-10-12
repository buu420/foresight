using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

public sealed class MovieLifecycleTests
{
    private readonly MovieNarrationLifetime lifetime = new();
    private readonly List<MovieLifecycleChange> changes = [];

    public MovieLifecycleTests() => lifetime.Changed += changes.Add;

    [Fact]
    public void KnownMovieStaysActiveUntilTheNativeCloseOfItsOwnPlayerReturns()
    {
        lifetime.Begin(0x1000, "003.dat", narrated: true);
        Assert.Equal("003.dat", lifetime.ActiveMovie);
        Assert.Equal([new(MovieLifecycleKind.Started, "003.dat", true)], changes);

        lifetime.CloseSession(0x9000, _ => 0x2000, _ => 0);
        Assert.Equal("003.dat", lifetime.ActiveMovie);
        // Activity alone (no injected source) is enough to identify the closing player.
        Assert.Equal(5, lifetime.CloseSession(0x9000, _ => 0x1000, _ =>
        {
            Assert.Equal("003.dat", lifetime.ActiveMovie);
            return 5;
        }));
        Assert.Null(lifetime.ActiveMovie);
        Assert.Equal(new MovieLifecycleChange(MovieLifecycleKind.Ended, "003.dat", true), changes[^1]);
        lifetime.CloseSession(0x9000, _ => 0x1000, _ => 0);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void RepeatedBuildForTheSamePlayerDoesNotRestartTheMovie()
    {
        lifetime.Begin(0x1000, "002.dat", narrated: true);
        lifetime.Begin(0x1000, "002.dat", narrated: false);
        Assert.Single(changes);
    }

    [Fact]
    public void OldPlayerClosingDuringASceneTransitionDoesNotEndTheNewMovie()
    {
        lifetime.Begin(0x1000, "002.dat", narrated: true);
        lifetime.Begin(0x2000, "003.dat", narrated: true);
        lifetime.CloseSession(0x9000, _ => 0x1000, _ => 0);
        Assert.Equal("003.dat", lifetime.ActiveMovie);
    }

    [Fact]
    public void HookResetEndsActivityButNeverShutsDownSourcesThatMayStillPlay()
    {
        var narrated = new Narrated();
        lifetime.Attach(0x1000, narrated);
        lifetime.Begin(0x1000, "002.dat", narrated: true);
        lifetime.Reset();
        Assert.Null(lifetime.ActiveMovie);
        Assert.Equal(new MovieLifecycleChange(MovieLifecycleKind.Reset, "002.dat", true), changes[^1]);
        Assert.Equal(0, narrated.Closes);
        Assert.True(lifetime.Owns(0x1000));
    }

    [Fact]
    public void FaultyObserverCannotReachGameCode()
    {
        lifetime.Changed += _ => throw new InvalidOperationException("observer fault");
        lifetime.Begin(0x1000, "002.dat", narrated: true);
        Assert.Equal(0, lifetime.CloseSession(0x9000, _ => 0x1000, _ => 0));
        Assert.Null(lifetime.ActiveMovie);
    }

    [Theory]
    [InlineData("003.dat", true)]
    [InlineData("001.dat", false)]
    public void SeparatePackMovieBeginsExceptTheUnchangedOpening(string movie, bool begins)
    {
        var began = new List<(string, bool)>();
        var playback = new MovieAudioPlayback(_ => { }, _ => { });
        Assert.Equal(0, playback.Build(() => new Descriptor(3), () => movie, () => 0, began: (m, n) => began.Add((m, n))));
        Assert.Equal(begins ? [(movie, true)] : [], began);
    }

    [Fact]
    public void PackMovieRestoredWithoutItsVoiceStillCoversTheField()
    {
        var began = new List<(string, bool)>();
        var calls = 0;
        var playback = new MovieAudioPlayback(_ => { }, _ => { });
        Assert.Equal(0, playback.Build(() => new Descriptor(3), () => "003.dat", () => ++calls == 1 ? -1 : 0,
            began: (m, n) => began.Add((m, n))));
        Assert.Equal([("003.dat", false)], began);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(null, false)]
    public void IdentifiedOriginalBeginsWhetherNarratedOrRolledBack(int? narrated, bool voice)
    {
        var began = new List<(string, bool)>();
        var playback = new MovieAudioPlayback(_ => { }, _ => { });
        Assert.Equal(narrated ?? 6, playback.Build(() => new Descriptor(2), () => null, () => 6, _ => narrated,
            () => "004.dat", (m, n) => began.Add((m, n))));
        Assert.Equal([("004.dat", voice)], began);
    }

    [Fact]
    public void UnknownOrFailedMoviesDoNotBegin()
    {
        var began = new List<(string, bool)>();
        var playback = new MovieAudioPlayback(_ => { }, _ => { });
        playback.Build(() => new Descriptor(2), () => null, () => 0, _ => null, () => null, (m, n) => began.Add((m, n)));
        playback.Build(() => new Descriptor(2), () => null, () => -1, _ => null, () => "002.dat", (m, n) => began.Add((m, n)));
        Assert.Empty(began);
    }

    private sealed class Descriptor(int count) : IMoviePresentation
    {
        private readonly bool[] selected = count == 3 ? [false, true, true] : [true, true];
        public int Count => count;
        private readonly MovieStreamKind[] kinds = count == 3
            ? [MovieStreamKind.Audio, MovieStreamKind.Audio, MovieStreamKind.Video]
            : [MovieStreamKind.Audio, MovieStreamKind.Video];
        public (MovieStreamKind Kind, bool Selected) GetStream(int index) => (kinds[index], selected[index]);
        public void Select(int index, bool value) => selected[index] = value;
    }

    private sealed class Narrated : INarratedMovieSource
    {
        public nint Source => 0x5000;
        public nint Descriptor => 0x6000;
        public IMoviePresentation Presentation => throw new NotSupportedException();
        public int Closes { get; private set; }
        public void Close() => Closes++;
    }
}
