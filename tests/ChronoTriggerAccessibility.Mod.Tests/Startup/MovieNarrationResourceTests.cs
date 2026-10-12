using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

public sealed class MovieNarrationResourceTests : IDisposable
{
    [Fact]
    public void AllThreeShippedCinemaMastersLoadAndEndNoLaterThanTheirMovies()
    {
        var tracks = MovieNarrationPack.Load();
        Assert.Equal(["002.dat", "003.dat", "004.dat"], tracks.Select(track => track.Movie.FileName));
        Assert.All(tracks, track => Assert.True(track.Samples / (double)track.SampleRate <= track.Movie.DurationSeconds));
    }

    // Hand-checked identities of the decoded original Steam movies (sha256sum of the audited XOR output).
    private const string Original002 = "ba85684dc8586665c0a78b0aa7e8b704622eca0a4993076ad319dfe84fdb1254";
    private const string NarratedPack002 = "a155a364eb0154741a9efa3e46d0508ccb3fe3274bfd2d130eeec2b0e1671056";
    private static readonly OriginalMovie Movie = new("002.dat", Original002.ToUpperInvariant(), 23838176, 39.105708);
    private readonly string root = Path.Combine(Path.GetTempPath(), "ct-movie-narration-" + Guid.NewGuid());

    [Theory]
    [InlineData(24000, 938536)]
    [InlineData(48000, 1877073)]
    public void FullLengthMonoPcmNarrationIsAccepted(int rate, int samples)
    {
        var track = MovieNarrationTrack.Create(Movie, Wave(rate, samples));
        Assert.Equal(rate, track.SampleRate);
        Assert.Equal(samples, track.Samples);
        Assert.Same(Movie, track.Movie);
    }

    [Theory]
    [InlineData(24000, 938537)]
    [InlineData(48000, 1877074)]
    public void NarrationLongerThanItsMovieIsRejected(int rate, int samples)
    {
        // A longer source delays the native end of presentation, so the game's COMPLETED event.
        Assert.Throws<InvalidDataException>(() => MovieNarrationTrack.Create(Movie, Wave(rate, samples)));
    }

    [Fact]
    public void TruncatedNarrationIsRejectedAtTheFullDurationBoundary()
    {
        Assert.Equal(936136, MovieNarrationTrack.Create(Movie, Wave(24000, 936136)).Samples);
        Assert.Throws<InvalidDataException>(() => MovieNarrationTrack.Create(Movie, Wave(24000, 936135)));
    }

    [Theory]
    [InlineData("stereo")]
    [InlineData("8-bit")]
    [InlineData("44100")]
    [InlineData("float")]
    [InlineData("riff-size")]
    [InlineData("data-size")]
    [InlineData("truncated")]
    public void NonCanonicalWaveIsRejected(string defect)
    {
        var wave = defect switch
        {
            "stereo" => Wave(24000, 938536, channels: 2),
            "8-bit" => Wave(24000, 938536, bits: 8),
            "44100" => Wave(44100, 1724563),
            "float" => Wave(24000, 938536, format: 3, bits: 32),
            _ => Wave(24000, 938536),
        };
        if (defect == "riff-size") BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length);
        if (defect == "data-size") BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), wave.Length - 46);
        if (defect == "truncated") wave = wave[..40];
        Assert.Throws<InvalidDataException>(() => MovieNarrationTrack.Create(Movie, wave));
    }

    [Fact]
    public void ExactDecodedOriginalIsIdentifiedBySizeAndHash()
    {
        Directory.CreateDirectory(root);
        var decoded = Path.Combine(root, "tmp.mp4");
        File.WriteAllBytes(decoded, [7, 8, 9]);
        var candidate = new OriginalMovie("003.dat", Convert.ToHexString(SHA256.HashData([7, 8, 9])), 3, 1);
        Assert.Same(candidate, MovieNarrationCatalog.Match(decoded, [candidate]));
        // An identity needs the exact size as well as the hash.
        Assert.Null(MovieNarrationCatalog.Match(decoded, [candidate with { DecodedBytes = 4 }]));
        File.WriteAllBytes(decoded, [7, 8, 10]);
        Assert.Null(MovieNarrationCatalog.Match(decoded, [candidate]));
        File.Delete(decoded);
        Assert.Null(MovieNarrationCatalog.Match(decoded, [candidate]));
    }

    [Fact]
    public void ManifestWithoutMovieNarrationDeclaresNoTracks()
    {
        Assert.Empty(MovieNarrationPack.Load(Resources("{\"schema\":1,\"cues\":[]}")));
    }

    [Fact]
    public void DeclaredNarrationLoadsForThePinnedOriginalMovie()
    {
        var wave = Wave(24000, 938536);
        var track = Assert.Single(MovieNarrationPack.Load(Resources(Manifest(Entry(wave)), wave)));
        Assert.Equal("002.dat", track.Movie.FileName);
        Assert.Equal(Original002, track.Movie.DecodedSha256, ignoreCase: true);
        Assert.Equal(23838176, track.Movie.DecodedBytes);
        Assert.Equal(39.105708, track.Movie.DurationSeconds);
        Assert.Equal(wave, track.Wave);
    }

    [Fact]
    public void OpeningMovieCannotBeNarratedByInjection()
    {
        // The opening already has the Fish timeline; injected narration would duplicate it.
        var wave = Wave(24000, 938536);
        var entry = Entry(wave) with { movie = "001.dat", decodedSha256 = "70aff86a94feaf1b43cdfc4a5a3af131eb2274ea3621b628662d79081ab7b4e3" };
        Assert.Throws<InvalidDataException>(() => MovieNarrationPack.Load(Resources(Manifest(entry), wave)));
    }

    [Fact]
    public void NarrationDeclaredForAChangedMovieIsRejected()
    {
        var wave = Wave(24000, 938536);
        Assert.Throws<InvalidDataException>(() => MovieNarrationPack.Load(
            Resources(Manifest(Entry(wave) with { decodedSha256 = NarratedPack002 }), wave)));
    }

    [Theory]
    [InlineData("sha256")]
    [InlineData("bytes")]
    [InlineData("samples")]
    [InlineData("sampleRate")]
    [InlineData("missing")]
    public void ChangedRecordingIsRejected(string defect)
    {
        var wave = Wave(24000, 938536);
        var entry = defect switch
        {
            "sha256" => Entry(wave) with { sha256 = new string('0', 64) },
            "bytes" => Entry(wave) with { bytes = wave.Length + 2 },
            "samples" => Entry(wave) with { samples = 938535 },
            "sampleRate" => Entry(wave) with { sampleRate = 48000 },
            _ => Entry(wave) with { resource = "movie-002-missing.wav" },
        };
        Assert.Throws<InvalidDataException>(() => MovieNarrationPack.Load(Resources(Manifest(entry), wave)));
    }

    [Fact]
    public void DuplicateMovieDeclarationsAreRejected()
    {
        var wave = Wave(24000, 938536);
        Assert.Throws<InvalidDataException>(() => MovieNarrationPack.Load(Resources(Manifest(Entry(wave), Entry(wave)), wave)));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed record ManifestEntry(string movie, string decodedSha256, string resource, string sha256,
        int bytes, int sampleRate, int samples);

    private static ManifestEntry Entry(byte[] wave) => new("002.dat", Original002, "movie-002-narration.wav",
        Convert.ToHexString(SHA256.HashData(wave)).ToLowerInvariant(), wave.Length, 24000, (wave.Length - 44) / 2);

    private static string Manifest(params ManifestEntry[] entries) => JsonSerializer.Serialize(new
    {
        schema = 1, provider = "Fish Audio", cues = Array.Empty<object>(), movieNarration = entries,
    });

    private static Func<string, Stream?> Resources(string manifest, byte[]? wave = null) => name =>
        name == StoryVoicePack.ResourcePrefix + "manifest.json" ? new MemoryStream(Encoding.UTF8.GetBytes(manifest))
        : name == StoryVoicePack.ResourcePrefix + "movie-002-narration.wav" && wave is not null ? new MemoryStream(wave)
        : null;

    internal static byte[] Wave(int rate, int samples, short channels = 1, short bits = 16, short format = 1)
    {
        var align = channels * bits / 8;
        var data = samples * align;
        var wave = new byte[44 + data];
        "RIFF"u8.CopyTo(wave);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), 36 + data);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), format);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), rate * align);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), (short)align);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), bits);
        "data"u8.CopyTo(wave.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), data);
        return wave;
    }
}
