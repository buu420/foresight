using System.Runtime.InteropServices;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

/// <summary>Real Media Foundation in the test host. A WAV stands in for the decoded movie; the
/// game DLL and its topology builder are covered by the x86 native probes.</summary>
public sealed class NativeMovieNarrationTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "ct-native-narration-" + Guid.NewGuid())).FullName;

    public NativeMovieNarrationTests() => Assert.True(MFStartup(0x20070, 0) >= 0);

    [Fact]
    public void NarrationIsAggregatedWithASecondMovieInstanceThatIsReleasedOnClose()
    {
        var movie = Path.Combine(root, "tmp.mp4.wav");
        File.WriteAllBytes(movie, MovieNarrationResourceTests.Wave(48000, 48000));
        var track = MovieNarrationTrack.Create(new OriginalMovie("002.dat", new string('A', 64), 1, 1.05),
            MovieNarrationResourceTests.Wave(24000, 24000));

        var narrated = new NativeMovieNarrationMedia().Open(movie, track);
        Assert.NotEqual(0, narrated.Source);
        Assert.NotEqual(0, narrated.Descriptor);
        Assert.Equal(2, narrated.Presentation.Count);
        Assert.Equal((MovieStreamKind.Audio, true), narrated.Presentation.GetStream(0));
        Assert.Equal((MovieStreamKind.Audio, true), narrated.Presentation.GetStream(1));

        narrated.Close();
        narrated.Close();
        // The player deletes and rewrites tmp.mp4; the mod's own instance must not keep it open.
        File.Delete(movie);
        Assert.False(File.Exists(movie));
    }

    [Fact]
    public void FailedOpenReportsTheMediaFoundationError()
    {
        var track = MovieNarrationTrack.Create(new OriginalMovie("002.dat", new string('A', 64), 1, 1.05),
            MovieNarrationResourceTests.Wave(24000, 24000));
        var failure = Assert.ThrowsAny<Exception>(() =>
            new NativeMovieNarrationMedia().Open(Path.Combine(root, "missing.mp4"), track));
        Assert.Equal(unchecked((int)0x80070002), failure.HResult);
    }

    [Theory]
    [InlineData(1u, unchecked((int)0xC00D36B9), unchecked((int)0xC00D36B9))] // MEError with failed status
    [InlineData(103u, 0, null)] // MESessionStarted
    [InlineData(211u, 1, null)] // MEEndOfPresentation with a success code
    public void NativeEventReaderReturnsOnlyFailedStatus(uint type, int status, int? expected)
    {
        Assert.True(MFCreateMediaEvent(type, Guid.Empty, status, 0, out var mediaEvent) >= 0);
        try { Assert.Equal(expected, NativeMovieEvent.Failure(mediaEvent)); }
        finally { Marshal.Release(mediaEvent); }
    }

    public void Dispose()
    {
        MFShutdown();
        Directory.Delete(root, recursive: true);
    }

    [DllImport("mfplat.dll")] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaEvent(uint type, in Guid extended, int status,
        nint value, out nint mediaEvent);
}
