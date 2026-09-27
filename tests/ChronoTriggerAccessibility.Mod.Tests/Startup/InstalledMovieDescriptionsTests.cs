using System.Security.Cryptography;
using System.Text.Json;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

public sealed class InstalledMovieDescriptionsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ct-audio-description-test-" + Guid.NewGuid());

    [Fact]
    public async Task OnlyMatchingInstalledOpeningMovieSuppressesSpokenFallback()
    {
        Directory.CreateDirectory(root);
        var movie = Path.Combine(root, "001.dat");
        await File.WriteAllBytesAsync(movie, [1, 2, 3, 4]);
        Assert.False(await InstalledMovieDescriptions.HasOpeningNarrationAsync(root));
        WriteManifest(Convert.ToHexString(SHA256.HashData([1, 2, 3, 4])));
        Assert.True(await InstalledMovieDescriptions.HasOpeningNarrationAsync(root));
        await File.WriteAllBytesAsync(movie, [1, 2, 3, 5]);
        Assert.False(await InstalledMovieDescriptions.HasOpeningNarrationAsync(root));
    }

    [Fact]
    public async Task BrokenOrUnrelatedManifestCannotSilenceOpeningDescriptions()
    {
        Directory.CreateDirectory(Path.Combine(root, "Accessibility", "AudioDescriptions"));
        await File.WriteAllTextAsync(ManifestPath, "not json");
        Assert.False(await InstalledMovieDescriptions.HasOpeningNarrationAsync(root));
        WriteManifest(new string('A', 64), "002.dat");
        Assert.False(await InstalledMovieDescriptions.HasOpeningNarrationAsync(root));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"version\":\"1\",\"movies\":[]}")]
    public async Task WrongJsonTypesDoNotBreakStartup(string json)
    {
        Directory.CreateDirectory(Path.Combine(root, "Accessibility", "AudioDescriptions"));
        await File.WriteAllTextAsync(ManifestPath, json);
        Assert.False(await InstalledMovieDescriptions.HasOpeningNarrationAsync(root));
    }

    private string ManifestPath => Path.Combine(root, "Accessibility", "AudioDescriptions", "installed-movies.json");
    private void WriteManifest(string hash, string name = "001.dat")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new
        {
            version = 1,
            movies = new[] { new { fileName = name, sha256 = hash, narration = true } }
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
