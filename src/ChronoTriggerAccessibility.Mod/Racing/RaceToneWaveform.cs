namespace ChronoTriggerAccessibility.Mod.Racing;

public static class RaceToneWaveform
{
    public const int SampleRate = 48000;

    /// <summary>70ms mono PCM with a 5ms raised-cosine attack/release at 16% peak level.
    /// The aligned cue has a second harmonic to distinguish its midpoint pitch.</summary>
    public static short[] Create(RaceTone tone)
    {
        var frequency = tone switch
        {
            RaceTone.Above => 960,
            RaceTone.Aligned => 640,
            RaceTone.Below => 320,
            _ => throw new ArgumentOutOfRangeException(nameof(tone))
        };
        var samples = new short[SampleRate * 70 / 1000];
        const int fade = SampleRate * 5 / 1000;
        for (var i = 0; i < samples.Length; i++)
        {
            var phase = 2 * Math.PI * frequency * i / SampleRate;
            var wave = Math.Sin(phase);
            if (tone == RaceTone.Aligned) wave = (wave + 0.35 * Math.Sin(2 * phase)) / 1.35;
            var edge = Math.Min(i, samples.Length - 1 - i);
            var envelope = edge >= fade ? 1 : (1 - Math.Cos(Math.PI * edge / fade)) / 2;
            samples[i] = (short)Math.Round(short.MaxValue * 0.16 * wave * envelope);
        }
        return samples;
    }
}
