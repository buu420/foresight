using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

public sealed record StoryVoiceClip(byte[] Wave, double Duration, string Text);

public sealed class StoryVoicePack
{
    public const string ResourcePrefix = "ChronoTriggerAccessibility.Mod.AudioDescriptions.Voice.";
    private readonly IReadOnlyDictionary<string, StoryVoiceClip> clips;
    private StoryVoicePack(IReadOnlyDictionary<string, StoryVoiceClip> clips) => this.clips = clips;
    public int Count => clips.Count;

    public static StoryVoicePack Load(Func<string, Stream?>? read = null)
    {
        read ??= name => typeof(StoryVoicePack).Assembly.GetManifestResourceStream(name);
        using var metadata = read(ResourcePrefix + "manifest.json")
            ?? throw new InvalidDataException("The recorded story voice manifest is missing.");
        var manifest = JsonSerializer.Deserialize<Manifest>(metadata,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The recorded story voice manifest is empty.");
        var expected = StoryVoiceCueCatalog.All.ToDictionary(cue => cue.Id, cue => cue.Text, StringComparer.Ordinal);
        if (manifest.Schema != 1 || manifest.Provider != "Fish Audio" || manifest.SampleRate != 24000 ||
            manifest.Channels != 1 || manifest.Bits != 16 || manifest.Cues is null || manifest.Cues.Length != expected.Count ||
            manifest.ReferenceSha256 != "66e41e7efeb37965426f45b54898007b85d44c1cb4ff5d67927f1d2b62191a6c")
            throw new InvalidDataException("The recorded story voice manifest does not match this build.");
        var result = new Dictionary<string, StoryVoiceClip>(StringComparer.Ordinal);
        foreach (var cue in manifest.Cues)
        {
            if (cue.Id is null || !expected.TryGetValue(cue.Id, out var text) || text != cue.Text || result.ContainsKey(cue.Id))
                throw new InvalidDataException("A recorded story voice caption or identifier does not match.");
            using var input = read(ResourcePrefix + cue.Id + ".wav")
                ?? throw new InvalidDataException($"Recorded story voice {cue.Id} is missing.");
            using var output = new MemoryStream(); input.CopyTo(output); var wave = output.ToArray();
            if (Convert.ToHexString(SHA256.HashData(wave)).ToLowerInvariant() != cue.Sha256 || wave.Length != cue.Bytes)
                throw new InvalidDataException($"Recorded story voice {cue.Id} has changed.");
            var duration = VerifyWave(wave);
            if (Math.Abs(cue.Duration - duration) > 0.00001)
                throw new InvalidDataException($"Recorded story voice {cue.Id} has an incorrect duration.");
            result.Add(cue.Id, new(wave, duration, text));
        }
        return new(result);
    }

    public StoryVoiceClip Get(StoryNarration cue)
    {
        if (!clips.TryGetValue(cue.CueId, out var clip) || cue.VoiceText != clip.Text)
            throw new InvalidDataException("The requested description does not match its recorded voice.");
        return clip;
    }

    private static double VerifyWave(byte[] wave)
    {
        static int Int(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
        static short Short(byte[] bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
        if (wave.Length < 44 || !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !wave.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) || !wave.AsSpan(36, 4).SequenceEqual("data"u8) ||
            Int(wave, 4) != wave.Length - 8 || Int(wave, 16) != 16 || Short(wave, 20) != 1 || Short(wave, 22) != 1 ||
            Int(wave, 24) != 24000 || Int(wave, 28) != 48000 || Short(wave, 32) != 2 || Short(wave, 34) != 16 ||
            Int(wave, 40) != wave.Length - 44 || (wave.Length - 44) % 2 != 0)
            throw new InvalidDataException("Recorded story voice is not canonical mono PCM.");
        var duration = (wave.Length - 44) / 48000.0;
        if (duration is < 0.1 or > 20) throw new InvalidDataException("Recorded story voice duration is invalid.");
        return duration;
    }

    private sealed record Manifest(int Schema, string Provider, string ReferenceSha256,
        int SampleRate, int Channels, int Bits, Cue[] Cues);
    private sealed record Cue(string Id, string Text, string Sha256, double Duration, int Bytes);
}
