using System.Security.Cryptography;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

public sealed class MovieNarrationInjectorTests : IDisposable
{
    private const nint GameSource = 0x1000;
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "ct-movie-injector-" + Guid.NewGuid())).FullName;
    private readonly string decoded;
    private readonly MovieNarrationTrack track;
    private readonly MovieNarrationLifetime lifetime = new();
    private readonly List<string> announcements = [];
    private bool closeHookActive = true;

    public MovieNarrationInjectorTests()
    {
        decoded = Path.Combine(root, "tmp.mp4");
        File.WriteAllBytes(decoded, [1, 2, 3]);
        track = MovieNarrationTrack.Create(new OriginalMovie("002.dat", Convert.ToHexString(SHA256.HashData([1, 2, 3])), 3,
            39.105708), MovieNarrationResourceTests.Wave(24000, 938536));
    }

    [Fact]
    public void ExactOriginalMovieIsBuiltFromTheAggregateAndOwnedUntilItsSessionCloses()
    {
        var narrated = new Narrated(Narrated.Layout);
        var media = new Media(() => narrated);
        var calls = new List<(nint Source, nint Descriptor)>();
        Assert.Equal(0, Injector(media).TryBuild(Original(), GameSource, decoded, (source, descriptor) =>
        {
            calls.Add((source, descriptor));
            return 0;
        }));
        Assert.Equal([(Narrated.AggregateSource, Narrated.AggregateDescriptor)], calls);
        Assert.Equal((decoded, track), Assert.Single(media.Opened));
        Assert.Empty(announcements);

        // Another player's session does not own this narration.
        lifetime.CloseSession(0x9000, _ => 0x2000, _ => 0);
        Assert.Equal(0, narrated.Closes);
        // Teardown only follows the game's own close of the matching session.
        Assert.Equal(7, lifetime.CloseSession(0x9000, _ => GameSource, _ =>
        {
            Assert.Equal(0, narrated.Closes);
            return 7;
        }));
        Assert.Equal(1, narrated.Closes);
        lifetime.CloseSession(0x9000, _ => GameSource, _ => 0);
        Assert.Equal(1, narrated.Closes);
    }

    [Fact]
    public void OpeningAndUndeclaredMoviesAreLeftToTheNativeBuilder()
    {
        File.WriteAllBytes(decoded, [9, 9, 9]);
        var media = new Media(() => throw new InvalidOperationException("must not open"));
        Assert.Null(Injector(media).TryBuild(Original(), GameSource, decoded, (_, _) => throw new InvalidOperationException()));
        Assert.Empty(media.Opened);
        Assert.Empty(announcements);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SeparatePackOrUnexpectedLayoutsAreNotNarrated(bool separatePack)
    {
        var media = new Media(() => throw new InvalidOperationException("must not open"));
        var presentation = separatePack
            ? new Presentation((MovieStreamKind.Audio, false), (MovieStreamKind.Audio, true), (MovieStreamKind.Video, true))
            : new Presentation((MovieStreamKind.Audio, true), (MovieStreamKind.Video, false));
        Assert.Null(Injector(media).TryBuild(presentation, GameSource, decoded, (_, _) => throw new InvalidOperationException()));
        Assert.Empty(media.Opened);
    }

    [Fact]
    public void NarrationNeedsTheNativeSessionCloseHook()
    {
        closeHookActive = false;
        var media = new Media(() => throw new InvalidOperationException("must not open"));
        Assert.Null(Injector(media).TryBuild(Original(), GameSource, decoded, (_, _) => throw new InvalidOperationException()));
        Assert.Empty(media.Opened);
        Assert.Single(announcements);
    }

    [Fact]
    public void MediaFailureRollsBackToTheNativeTopology()
    {
        var media = new Media(() => throw new InvalidOperationException("source resolver failed"));
        Assert.Null(Injector(media).TryBuild(Original(), GameSource, decoded, (_, _) => throw new InvalidOperationException()));
        Assert.Single(announcements);
        // Nothing is owned, so closing a player does not read game memory.
        var reads = 0;
        Assert.Equal(3, lifetime.CloseSession(0x9000, _ => { reads++; return GameSource; }, _ => 3));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void UnexpectedAggregateLayoutClosesTheModSourcesAndRollsBack()
    {
        var narrated = new Narrated(new Presentation((MovieStreamKind.Audio, true), (MovieStreamKind.Video, true)));
        var built = false;
        Assert.Null(Injector(new Media(() => narrated)).TryBuild(Original(), GameSource, decoded, (_, _) =>
        {
            built = true;
            return 0;
        }));
        Assert.False(built);
        Assert.Equal(1, narrated.Closes);
        Assert.Single(announcements);
    }

    [Fact]
    public void NativeBuilderFailureClosesTheModSourcesAndRollsBack()
    {
        var narrated = new Narrated(Narrated.Layout);
        Assert.Null(Injector(new Media(() => narrated)).TryBuild(Original(), GameSource, decoded,
            (_, _) => unchecked((int)0x80004005)));
        Assert.Equal(1, narrated.Closes);
        Assert.Single(announcements);
        lifetime.CloseSession(0x9000, _ => GameSource, _ => 0);
        Assert.Equal(1, narrated.Closes);
    }

    [Fact]
    public void TheSameNativeSourceIsNotNarratedTwice()
    {
        var media = new Media(() => new Narrated(Narrated.Layout));
        var injector = Injector(media);
        Assert.Equal(0, injector.TryBuild(Original(), GameSource, decoded, (_, _) => 0));
        Assert.Null(injector.TryBuild(Original(), GameSource, decoded, (_, _) => 0));
        Assert.Single(media.Opened);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private MovieNarrationInjector Injector(IMovieNarrationMedia media) =>
        new([track], media, lifetime, () => closeHookActive, _ => { }, announcements.Add);

    private static Presentation Original() => new((MovieStreamKind.Audio, true), (MovieStreamKind.Video, true));

    internal sealed class Presentation(params (MovieStreamKind Kind, bool Selected)[] streams) : IMoviePresentation
    {
        public int Count => streams.Length;
        public (MovieStreamKind Kind, bool Selected) GetStream(int index) => streams[index];
        public void Select(int index, bool selected) => throw new InvalidOperationException("Selection must not change.");
    }

    private sealed class Media(Func<INarratedMovieSource> open) : IMovieNarrationMedia
    {
        public List<(string DecodedMovie, MovieNarrationTrack Track)> Opened { get; } = [];
        public INarratedMovieSource Open(string decodedMovie, MovieNarrationTrack narration)
        {
            Opened.Add((decodedMovie, narration));
            return open();
        }
    }

    private sealed class Narrated(IMoviePresentation presentation) : INarratedMovieSource
    {
        public const nint AggregateSource = 0x5000;
        public const nint AggregateDescriptor = 0x6000;
        public static IMoviePresentation Layout => new Presentation(
            (MovieStreamKind.Audio, true), (MovieStreamKind.Video, true), (MovieStreamKind.Audio, true));
        public nint Source => AggregateSource;
        public nint Descriptor => AggregateDescriptor;
        public IMoviePresentation Presentation => presentation;
        public int Closes { get; private set; }
        public void Close() => Closes++;
    }
}
