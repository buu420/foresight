using System.Security.Cryptography;
using System.Text.Json;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>Movie narration masters declared under "movieNarration" in the story voice manifest.</summary>
public static class MovieNarrationPack
{
    public static IReadOnlyList<MovieNarrationTrack> Load(Func<string, Stream?>? read = null)
    {
        read ??= name => typeof(MovieNarrationPack).Assembly.GetManifestResourceStream(name);
        using var metadata = read(StoryVoicePack.ResourcePrefix + "manifest.json");
        if (metadata is null) return [];
        using var document = JsonDocument.Parse(metadata);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("movieNarration", out var entries)) return [];
        if (entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The movie narration declarations are malformed.");
        var tracks = new List<MovieNarrationTrack>();
        foreach (var element in entries.EnumerateArray())
        {
            var entry = element.ValueKind == JsonValueKind.Object
                ? element.Deserialize<Entry>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;
            if (entry?.Movie is null || entry.DecodedSha256 is null || entry.Resource is null || entry.Sha256 is null)
                throw new InvalidDataException("A movie narration declaration is incomplete.");
            var movie = MovieNarrationCatalog.Originals.SingleOrDefault(original => original.FileName == entry.Movie)
                ?? throw new InvalidDataException($"{entry.Movie} does not accept injected narration.");
            if (!movie.DecodedSha256.Equals(entry.DecodedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{entry.Movie} narration is declared for a different movie file.");
            if (tracks.Any(track => track.Movie == movie))
                throw new InvalidDataException($"{entry.Movie} narration is declared more than once.");
            using var input = read(StoryVoicePack.ResourcePrefix + entry.Resource)
                ?? throw new InvalidDataException($"{entry.Movie} narration recording is missing.");
            using var output = new MemoryStream();
            input.CopyTo(output);
            var wave = output.ToArray();
            if (wave.Length != entry.Bytes ||
                !Convert.ToHexString(SHA256.HashData(wave)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{entry.Movie} narration recording has changed.");
            var loaded = MovieNarrationTrack.Create(movie, wave);
            if (loaded.SampleRate != entry.SampleRate || loaded.Samples != entry.Samples)
                throw new InvalidDataException($"{entry.Movie} narration recording does not match its declaration.");
            tracks.Add(loaded);
        }
        return tracks.AsReadOnly();
    }

    private sealed record Entry(string? Movie, string? DecodedSha256, string? Resource, string? Sha256,
        int Bytes, int SampleRate, int Samples);
}
