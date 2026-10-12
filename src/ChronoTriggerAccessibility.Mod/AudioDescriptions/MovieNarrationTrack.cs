using System.Buffers.Binary;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>A full-length own-voice track for one original movie, played on the movie's
/// own Media Foundation clock.</summary>
public sealed class MovieNarrationTrack
{
    private MovieNarrationTrack(OriginalMovie movie, byte[] wave, int sampleRate, int samples)
    {
        Movie = movie;
        Wave = wave;
        SampleRate = sampleRate;
        Samples = samples;
    }

    public OriginalMovie Movie { get; }
    public byte[] Wave { get; }
    public int SampleRate { get; }
    public int Samples { get; }

    public static MovieNarrationTrack Create(OriginalMovie movie, byte[] wave)
    {
        static int Int(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
        static short Short(byte[] bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
        if (wave.Length < 44 || !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !wave.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) || !wave.AsSpan(36, 4).SequenceEqual("data"u8) ||
            Int(wave, 4) != wave.Length - 8 || Int(wave, 16) != 16 || Short(wave, 20) != 1 || Short(wave, 22) != 1 ||
            Int(wave, 24) is not (24000 or 48000) || Int(wave, 28) != Int(wave, 24) * 2 || Short(wave, 32) != 2 ||
            Short(wave, 34) != 16 || Int(wave, 40) != wave.Length - 44 || (wave.Length - 44) % 2 != 0)
            throw new InvalidDataException($"{movie.FileName} narration is not canonical mono 16-bit PCM.");
        var rate = Int(wave, 24);
        var samples = (wave.Length - 44) / 2;
        // Longer audio delays the native end of presentation, which is the game's COMPLETED event.
        if (samples > Math.Floor(movie.DurationSeconds * rate) ||
            samples < Math.Floor((movie.DurationSeconds - 0.1) * rate))
            throw new InvalidDataException($"{movie.FileName} narration must last the movie's full duration.");
        return new(movie, wave, rate, samples);
    }
}
