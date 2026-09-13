using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Dedicated voices let recorded footsteps overlap without taking the
/// process-wide PlaySound slot used by other audio. Owned only by the audio worker.</summary>
public interface IFootstepOutput : IDisposable
{
    bool TryPlay(int variation);
    void Stop();
}

public sealed class FootstepWaveOutput : IFootstepOutput
{
    private readonly List<GCHandle> samples = [];
    private readonly List<Voice> voices = [];
    private int nextVoice;
    private bool disposed;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();
    private static readonly int FlagsOffset = Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags)).ToInt32();

    public FootstepWaveOutput()
    {
        try
        {
            for (var i = 1; i <= 5; i++)
            {
                using var stream = typeof(FootstepWaveOutput).Assembly.GetManifestResourceStream(
                    $"ChronoTriggerAccessibility.Mod.Audio.footstep-{i}.wav")
                    ?? throw new InvalidDataException($"Footstep variation {i} is missing.");
                using var output = new MemoryStream();
                stream.CopyTo(output);
                var wave = output.ToArray();
                if (wave.Length < 44 || !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
                    !wave.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) ||
                    BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(16)) != 16 ||
                    BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(20)) != 1 ||
                    BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(22)) != 1 ||
                    BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24)) != 48000 ||
                    BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(34)) != 16 ||
                    !wave.AsSpan(36, 4).SequenceEqual("data"u8) ||
                    BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40)) != wave.Length - 44)
                    throw new InvalidDataException("Unsupported footstep PCM format.");
                samples.Add(GCHandle.Alloc(wave[44..], GCHandleType.Pinned));
            }
            var format = new WaveFormat { Tag = 1, Channels = 1, Rate = 48000,
                BytesPerSecond = 96000, Alignment = 2, Bits = 16 };
            for (var i = 0; i < 8; i++)
            {
                Check(waveOutOpen(out var handle, uint.MaxValue, ref format, 0, 0, 0), "open");
                var voice = new Voice(handle);
                voices.Add(voice);
                foreach (var sample in samples)
                {
                    var header = Marshal.AllocHGlobal((int)HeaderSize);
                    voice.Headers.Add(header);
                    Marshal.StructureToPtr(new WaveHeader { Data = sample.AddrOfPinnedObject(),
                        Length = (uint)((byte[])sample.Target!).Length }, header, false);
                    Check(waveOutPrepareHeader(handle, header, HeaderSize), "prepare");
                }
            }
        }
        catch { try { Dispose(); } catch { } throw; }
    }

    public bool TryPlay(int variation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (variation is < 0 or >= 5) throw new ArgumentOutOfRangeException(nameof(variation));
        for (var i = 0; i < voices.Count; i++)
        {
            var index = (nextVoice + i) % voices.Count;
            var voice = voices[index];
            if (voice.Current != 0 && (Marshal.ReadInt32(voice.Current, FlagsOffset) & 0x10) != 0) continue;
            var header = voice.Headers[variation];
            Check(waveOutWrite(voice.Handle, header, HeaderSize), "write");
            voice.Current = header;
            nextVoice = (index + 1) % voices.Count;
            return true;
        }
        return false;
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (var voice in voices) { Check(waveOutReset(voice.Handle), "reset"); voice.Current = 0; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // A failed reset/unprepare must retain memory the driver may still use.
        // The process will reclaim it; freeing an in-flight buffer is unsafe.
        var errors = new List<Exception>();
        var retainSamples = false;
        foreach (var voice in voices)
        {
            if (voice.Handle == 0) continue;
            var reset = waveOutReset(voice.Handle);
            if (reset != 0)
            {
                errors.Add(new InvalidOperationException($"Footstep waveOut cleanup reset failed: {reset}."));
                retainSamples = true;
                continue;
            }
            foreach (var header in voice.Headers)
            {
                if ((Marshal.ReadInt32(header, FlagsOffset) & 2) != 0)
                {
                    var result = waveOutUnprepareHeader(voice.Handle, header, HeaderSize);
                    if (result != 0)
                    {
                        errors.Add(new InvalidOperationException($"Footstep waveOut cleanup unprepare failed: {result}."));
                        retainSamples = true;
                        continue;
                    }
                }
                Marshal.FreeHGlobal(header);
            }
            var closed = waveOutClose(voice.Handle);
            if (closed != 0) errors.Add(new InvalidOperationException($"Footstep waveOut cleanup close failed: {closed}."));
            else voice.Handle = 0;
        }
        if (!retainSamples) foreach (var sample in samples) sample.Free();
        if (errors.Count != 0) throw new AggregateException("Footstep device cleanup failed.", errors);
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"Footstep waveOut {operation} failed: {result}.");
    }
    private sealed class Voice(nint handle)
    {
        public nint Handle = handle, Current;
        public List<nint> Headers { get; } = [];
    }
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint Rate, BytesPerSecond;
        public ushort Alignment, Bits, Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint Length, Recorded;
        public nuint User;
        public uint Flags, Loops;
        public nint Next;
        public nuint Reserved;
    }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint handle, uint device,
        ref WaveFormat format, nuint callback, nuint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(nint handle);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(nint handle);
}
