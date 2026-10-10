using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>One recorded story description at a time on its own waveOut device, so it
/// never takes the process-wide PlaySound slot or a footstep voice. Owned by one worker.</summary>
public interface IStoryVoiceOutput : IDisposable
{
    /// <summary>True until the driver marks the current buffer done (WHDR_DONE).</summary>
    bool Playing { get; }

    /// <summary>False while an utterance is playing. Throws <see cref="ArgumentException"/>
    /// for anything but a canonical 44-byte RIFF PCM 24 kHz mono 16-bit wave.</summary>
    bool TryPlay(byte[] canonicalWave);

    void Stop();
}

/// <summary>The narrow WinMM surface the output uses, plus the native memory it hands to it.</summary>
public interface IStoryWaveOutApi
{
    nint Allocate(int bytes);
    void Free(nint memory);
    uint Open(out nint device);
    uint Prepare(nint device, nint header);
    uint Write(nint device, nint header);
    uint Reset(nint device);
    uint Unprepare(nint device, nint header);
    uint Close(nint device);
}

public sealed class StoryVoiceWaveOutput : IStoryVoiceOutput
{
    public const int SampleRate = 24000;
    private const uint Done = 1, Prepared = 2;
    private static readonly int HeaderSize = Marshal.SizeOf<WaveHeader>();
    public static int HeaderFlagsOffset { get; } = Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags)).ToInt32();

    private readonly IStoryWaveOutApi api;
    private nint device;
    private Buffer? current;
    // Memory a failed cleanup could not prove returned by the driver. Never freed.
    private readonly List<Buffer> retained = [];
    private bool disposed;

    public StoryVoiceWaveOutput() : this(new WinMm()) { }

    public StoryVoiceWaveOutput(IStoryWaveOutApi api) => this.api = api ?? throw new ArgumentNullException(nameof(api));

    public bool Playing => current is { } buffer && (Flags(buffer.Header) & Done) == 0;

    public bool TryPlay(byte[] canonicalWave)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Validate(canonicalWave);
        if (Playing) return false;
        Reclaim();
        if (device == 0)
        {
            Check(api.Open(out var opened), "open");
            device = opened;
        }
        var length = canonicalWave.Length - 44;
        // Nothing below is the driver's until prepare succeeds, so any failure frees it.
        var data = api.Allocate(length);
        Buffer buffer;
        try
        {
            buffer = new Buffer(data, api.Allocate(HeaderSize));
        }
        catch
        {
            api.Free(data);
            throw;
        }
        try
        {
            Marshal.Copy(canonicalWave, 44, buffer.Data, length);
            Marshal.StructureToPtr(new WaveHeader { Data = buffer.Data, Length = (uint)length }, buffer.Header, false);
        }
        catch
        {
            Release(buffer);
            throw;
        }
        var prepared = api.Prepare(device, buffer.Header);
        if (prepared != 0)
        {
            Release(buffer);
            Check(prepared, "prepare");
        }
        var written = api.Write(device, buffer.Header);
        if (written != 0)
        {
            if (api.Unprepare(device, buffer.Header) == 0) Release(buffer);
            else retained.Add(buffer);
            Check(written, "write");
        }
        current = buffer;
        return true;
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (device == 0) return;
        // waveOutReset returns queued buffers marked done before memory is reclaimed.
        Check(api.Reset(device), "reset");
        Reclaim();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (device == 0) return;
        var errors = new List<Exception>();
        var reset = api.Reset(device);
        if (reset != 0)
        {
            // The driver may still read the queued buffer: keep it and the device.
            errors.Add(new InvalidOperationException($"Story voice waveOut cleanup reset failed: {reset}."));
            if (current is { } playing) retained.Add(playing);
            current = null;
        }
        else
        {
            try { Reclaim(); }
            catch (Exception exception) { errors.Add(exception); }
            if (current is null && retained.Count == 0)
            {
                var closed = api.Close(device);
                if (closed != 0) errors.Add(new InvalidOperationException($"Story voice waveOut cleanup close failed: {closed}."));
                else device = 0;
            }
        }
        if (errors.Count != 0) throw new AggregateException("Story voice device cleanup failed.", errors);
    }

    /// <summary>Unprepares and frees a buffer the driver has returned. On failure the
    /// buffer stays current, so a later play, stop or dispose retries it.</summary>
    private void Reclaim()
    {
        if (current is not { } buffer) return;
        if ((Flags(buffer.Header) & Prepared) != 0)
            Check(api.Unprepare(device, buffer.Header), "unprepare");
        current = null;
        Release(buffer);
    }

    private void Release(Buffer buffer)
    {
        api.Free(buffer.Header);
        api.Free(buffer.Data);
    }

    private static uint Flags(nint header) => (uint)Marshal.ReadInt32(header, HeaderFlagsOffset);

    private static void Validate(byte[] wave)
    {
        ArgumentNullException.ThrowIfNull(wave);
        if (wave.Length < 46 || (wave.Length & 1) != 0 ||
            !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(4)) != wave.Length - 8 ||
            !wave.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(16)) != 16 ||
            BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(20)) != 1 ||
            BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(22)) != 1 ||
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24)) != SampleRate ||
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(28)) != SampleRate * 2 ||
            BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(32)) != 2 ||
            BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(34)) != 16 ||
            !wave.AsSpan(36, 4).SequenceEqual("data"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40)) != wave.Length - 44)
            throw new ArgumentException("Story voice audio must be a canonical PCM 24 kHz mono 16-bit wave.", nameof(wave));
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"Story voice waveOut {operation} failed: {result}.");
    }

    private sealed record Buffer(nint Data, nint Header);

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

    private sealed class WinMm : IStoryWaveOutApi
    {
        private static readonly uint Size = (uint)HeaderSize;
        public nint Allocate(int bytes) => Marshal.AllocHGlobal(bytes);
        public void Free(nint memory) => Marshal.FreeHGlobal(memory);
        public uint Open(out nint handle)
        {
            var format = new WaveFormat { Tag = 1, Channels = 1, Rate = SampleRate,
                BytesPerSecond = SampleRate * 2, Alignment = 2, Bits = 16 };
            return waveOutOpen(out handle, uint.MaxValue, ref format, 0, 0, 0);
        }
        public uint Prepare(nint handle, nint header) => waveOutPrepareHeader(handle, header, Size);
        public uint Write(nint handle, nint header) => waveOutWrite(handle, header, Size);
        public uint Reset(nint handle) => waveOutReset(handle);
        public uint Unprepare(nint handle, nint header) => waveOutUnprepareHeader(handle, header, Size);
        public uint Close(nint handle) => waveOutClose(handle);

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormat
        {
            public ushort Tag, Channels;
            public uint Rate, BytesPerSecond;
            public ushort Alignment, Bits, Extra;
        }
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint handle, uint device,
            ref WaveFormat format, nuint callback, nuint instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint handle, nint header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint handle, nint header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(nint handle);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint handle, nint header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(nint handle);
    }
}
