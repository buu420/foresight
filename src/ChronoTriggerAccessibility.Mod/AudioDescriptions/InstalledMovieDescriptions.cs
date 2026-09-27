using System.Security.Cryptography;
using System.Text.Json;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>A restored or changed game movie must regain its spoken fallback.</summary>
public static class InstalledMovieDescriptions
{
    public static async Task<bool> HasOpeningNarrationAsync(string gameRoot,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = Path.Combine(gameRoot, "Accessibility", "AudioDescriptions", "installed-movies.json");
            await using var input = File.OpenRead(manifest);
            using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var value) || value != 1 ||
                !root.TryGetProperty("movies", out var movies) || movies.ValueKind != JsonValueKind.Array) return false;
            string? expected = null;
            foreach (var movie in movies.EnumerateArray())
            {
                if (movie.ValueKind != JsonValueKind.Object ||
                    !movie.TryGetProperty("fileName", out var name) || name.ValueKind != JsonValueKind.String ||
                    name.GetString() != "001.dat") continue;
                if (expected is not null ||
                    !movie.TryGetProperty("narration", out var narration) || narration.ValueKind != JsonValueKind.True ||
                    !movie.TryGetProperty("sha256", out var hash) || hash.ValueKind != JsonValueKind.String) return false;
                expected = hash.GetString();
            }
            if (expected is null || expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c))) return false;
            // The filename is fixed; manifest content cannot redirect the read.
            await using var movieFile = File.OpenRead(Path.Combine(gameRoot, "001.dat"));
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(movieFile, cancellationToken));
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
