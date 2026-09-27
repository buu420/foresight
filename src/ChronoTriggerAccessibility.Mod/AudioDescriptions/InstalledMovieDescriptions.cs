using System.Security.Cryptography;
using System.Text.Json;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>A restored or changed game movie must regain its spoken fallback.</summary>
public static class InstalledMovieDescriptions
{
    public static async Task<bool> HasOpeningNarrationAsync(string gameRoot,
        CancellationToken cancellationToken = default, bool separateTrackPlaybackAvailable = false)
    {
        try
        {
            var manifest = Path.Combine(gameRoot, "Accessibility", "AudioDescriptions", "installed-movies.json");
            await using var input = File.OpenRead(manifest);
            using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var value) || value is not (1 or 2) ||
                !root.TryGetProperty("movies", out var movies) || movies.ValueKind != JsonValueKind.Array) return false;
            if (value == 2 && (!separateTrackPlaybackAvailable || !SeparatePlayback(root))) return false;
            string? expected = null;
            foreach (var movie in movies.EnumerateArray())
            {
                if (movie.ValueKind != JsonValueKind.Object ||
                    !movie.TryGetProperty("fileName", out var name) || name.ValueKind != JsonValueKind.String ||
                    name.GetString() != "001.dat") continue;
                if (expected is not null ||
                    !movie.TryGetProperty("narration", out var narration) || narration.ValueKind != JsonValueKind.True ||
                    !movie.TryGetProperty("sha256", out var hash) || hash.ValueKind != JsonValueKind.String) return false;
                if (value == 2 && !SeparateMovie(movie)) return false;
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

    public static bool HasSeparatePack(string gameRoot)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(gameRoot,
                "Accessibility", "AudioDescriptions", "installed-movies.json")));
            var root = document.RootElement;
            return SeparatePlayback(root) && root.TryGetProperty("movies", out var movies) &&
                movies.ValueKind == JsonValueKind.Array && movies.EnumerateArray().Any(SeparateMovie);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static string? MatchSeparateMovie(string gameRoot, string decodedMovie)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(gameRoot,
                "Accessibility", "AudioDescriptions", "installed-movies.json")));
            var root = document.RootElement;
            if (!SeparatePlayback(root) || !root.TryGetProperty("movies", out var movies) ||
                movies.ValueKind != JsonValueKind.Array) return null;
            using var input = File.OpenRead(decodedMovie);
            var actual = Convert.ToHexString(SHA256.HashData(input));
            string? match = null;
            foreach (var movie in movies.EnumerateArray())
            {
                if (!SeparateMovie(movie) ||
                    !movie.GetProperty("decodedSha256").GetString()!.Equals(actual, StringComparison.OrdinalIgnoreCase)) continue;
                var name = movie.GetProperty("fileName").GetString();
                if (match is not null) return null;
                match = name;
            }
            return match;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool SeparatePlayback(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number &&
        version.TryGetInt32(out var value) && value == 2 &&
        root.TryGetProperty("playback", out var playback) && playback.ValueKind == JsonValueKind.String &&
        playback.GetString() == "simultaneous-separate-tracks";

    private static bool SeparateMovie(JsonElement movie) => movie.ValueKind == JsonValueKind.Object &&
        movie.TryGetProperty("fileName", out var name) && name.ValueKind == JsonValueKind.String &&
        name.GetString() is "001.dat" or "002.dat" or "003.dat" or "004.dat" or "005.dat" or "006.dat" or "007.dat" or "007-en.dat" or "008.dat" &&
        movie.TryGetProperty("narration", out var narration) && narration.ValueKind == JsonValueKind.True &&
        movie.TryGetProperty("originalAudioVerified", out var audio) && audio.ValueKind == JsonValueKind.True &&
        movie.TryGetProperty("videoVerified", out var video) && video.ValueKind == JsonValueKind.True &&
        movie.TryGetProperty("audioTrackCount", out var count) && count.ValueKind == JsonValueKind.Number &&
        count.TryGetInt32(out var tracks) && tracks == 2 &&
        movie.TryGetProperty("decodedSha256", out var hash) && hash.ValueKind == JsonValueKind.String &&
        hash.GetString() is { Length: 64 } value && value.All(Uri.IsHexDigit);
}
