using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Mod.Racing;

/// <summary>Uses the established footstep waveOut lifecycle with a separate device
/// and one voice. Replacing a cue resets this voice rather than queuing audio.</summary>
internal sealed class RaceToneWaveOutput : IRaceToneOutput
{
    private readonly List<GCHandle> samples = [];
    private readonly List<nint> headers = [];
    private nint handle, current;
    private bool disposed;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();
    private static readonly int FlagsOffset = Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags)).ToInt32();

    public RaceToneWaveOutput()
    {
        try
        {
            var format = new WaveFormat { Tag = 1, Channels = 1, Rate = RaceToneWaveform.SampleRate,
                BytesPerSecond = RaceToneWaveform.SampleRate * 2, Alignment = 2, Bits = 16 };
            Check(waveOutOpen(out handle, uint.MaxValue, ref format, 0, 0, 0), "open");
            foreach (var tone in new[] { RaceTone.Above, RaceTone.Aligned, RaceTone.Below })
            {
                var pcm = RaceToneWaveform.Create(tone);
                var sample = GCHandle.Alloc(pcm, GCHandleType.Pinned);
                samples.Add(sample);
                var header = Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.StructureToPtr(new WaveHeader { Data = sample.AddrOfPinnedObject(),
                    Length = (uint)(pcm.Length * sizeof(short)) }, header, false);
                headers.Add(header);
                Check(waveOutPrepareHeader(handle, header, HeaderSize), "prepare");
            }
        }
        catch { try { Dispose(); } catch { } throw; }
    }

    public void Play(RaceTone tone)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (tone is < RaceTone.Above or > RaceTone.Below) throw new ArgumentOutOfRangeException(nameof(tone));
        if (current != 0 && (Marshal.ReadInt32(current, FlagsOffset) & 0x10) != 0) Stop();
        var header = headers[(int)tone];
        Check(waveOutWrite(handle, header, HeaderSize), "write");
        current = header;
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Check(waveOutReset(handle), "reset");
        current = 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (handle == 0) return;
        // Never free pinned PCM or headers the driver may still own after a
        // failed reset/unprepare; retaining them is safer than a use-after-free.
        Check(waveOutReset(handle), "cleanup reset");
        var errors = new List<Exception>();
        var retainSamples = false;
        foreach (var header in headers)
        {
            if ((Marshal.ReadInt32(header, FlagsOffset) & 2) != 0)
            {
                var result = waveOutUnprepareHeader(handle, header, HeaderSize);
                if (result != 0)
                {
                    errors.Add(new InvalidOperationException($"Race tone waveOut cleanup unprepare failed: {result}."));
                    retainSamples = true;
                    continue;
                }
            }
            Marshal.FreeHGlobal(header);
        }
        var closed = waveOutClose(handle);
        if (closed != 0) errors.Add(new InvalidOperationException($"Race tone waveOut cleanup close failed: {closed}."));
        else handle = 0;
        if (!retainSamples) foreach (var sample in samples) sample.Free();
        if (errors.Count != 0) throw new AggregateException("Race tone device cleanup failed.", errors);
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"Race tone waveOut {operation} failed: {result}.");
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
