using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

/// <summary>The real output over a fake WinMM driver. The fake mutates the real native
/// WAVEHDR flags the way waveOut does (prepare 0x02, queued 0x10, done 0x01) and records
/// every native allocation, so ownership is checked on actual memory.</summary>
public sealed class StoryVoiceWaveOutputTests
{
    [Fact]
    public void PlaysOneCanonicalUtteranceOnItsOwnDeviceAndReportsPlayingUntilTheDriverIsDone()
    {
        using var api = new FakeWinMm();
        using var output = new StoryVoiceWaveOutput(api);
        var wave = Wave(480);

        Assert.True(output.TryPlay(wave));

        Assert.Equal(1, api.Opens);
        Assert.True(output.Playing);
        Assert.Equal(wave[44..], api.Written.Single());
        api.Finish();
        Assert.False(output.Playing);
    }

    [Fact]
    public void ABusyDeviceRejectsTheNextUtteranceWithoutQueuingOrFreeing()
    {
        using var api = new FakeWinMm();
        using var output = new StoryVoiceWaveOutput(api);
        Assert.True(output.TryPlay(Wave(100)));

        Assert.False(output.TryPlay(Wave(200)));

        Assert.Single(api.Written);
        Assert.Empty(api.Freed);
        api.Finish();
        Assert.True(output.TryPlay(Wave(200)));
        Assert.Equal(1, api.Opens);
        // The finished buffer and header were unprepared before being freed.
        Assert.Equal(2, api.Freed.Count);
        Assert.All(api.Freed, freed => Assert.DoesNotContain(freed, api.PreparedHeaders));
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void MalformedAudioIsRejectedBeforeTheDeviceOpens(string name, byte[] wave)
    {
        using var api = new FakeWinMm();
        using var output = new StoryVoiceWaveOutput(api);
        Assert.Throws<ArgumentException>(() => output.TryPlay(wave));
        Assert.True(api.Opens == 0, name);
        Assert.Empty(api.Allocated);
    }

    public static TheoryData<string, byte[]> Malformed()
    {
        var data = new TheoryData<string, byte[]>();
        void Add(string name, Action<byte[]> change)
        {
            var wave = Wave(64);
            change(wave);
            data.Add(name, wave);
        }
        Add("riff", w => w[0] = (byte)'X');
        Add("riff size", w => BinaryPrimitives.WriteInt32LittleEndian(w.AsSpan(4), w.Length));
        Add("wave", w => w[8] = (byte)'X');
        Add("fmt size", w => BinaryPrimitives.WriteInt32LittleEndian(w.AsSpan(16), 18));
        Add("float", w => BinaryPrimitives.WriteInt16LittleEndian(w.AsSpan(20), 3));
        Add("stereo", w => BinaryPrimitives.WriteInt16LittleEndian(w.AsSpan(22), 2));
        Add("rate", w => BinaryPrimitives.WriteInt32LittleEndian(w.AsSpan(24), 48000));
        Add("byte rate", w => BinaryPrimitives.WriteInt32LittleEndian(w.AsSpan(28), 96000));
        Add("alignment", w => BinaryPrimitives.WriteInt16LittleEndian(w.AsSpan(32), 4));
        Add("bits", w => BinaryPrimitives.WriteInt16LittleEndian(w.AsSpan(34), 8));
        Add("data tag", w => w[36] = (byte)'L');
        Add("data size", w => BinaryPrimitives.WriteInt32LittleEndian(w.AsSpan(40), 62));
        data.Add("empty", Wave(0));
        data.Add("odd", Odd());
        data.Add("short", Wave(64)[..43]);
        return data;

        static byte[] Odd()
        {
            var wave = Wave(64)[..^1];
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length - 8);
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), wave.Length - 44);
            return wave;
        }
    }

    [Fact]
    public void StopResetsAndReclaimsOnlyAfterTheDriverReturnsTheBuffer()
    {
        using var api = new FakeWinMm();
        using var output = new StoryVoiceWaveOutput(api);
        output.TryPlay(Wave(100));

        output.Stop();

        Assert.Equal(1, api.Resets);
        Assert.False(output.Playing);
        Assert.Empty(api.PreparedHeaders);
        Assert.Equal(2, api.Freed.Count);
        Assert.True(output.TryPlay(Wave(100)));
    }

    [Fact]
    public void AFailedResetKeepsTheQueuedBufferOwnedByTheDriver()
    {
        using var api = new FakeWinMm();
        var output = new StoryVoiceWaveOutput(api);
        output.TryPlay(Wave(100));
        api.ResetResult = 1;

        Assert.Throws<InvalidOperationException>(output.Stop);
        Assert.True(output.Playing);
        Assert.Empty(api.Freed);
        Assert.Throws<AggregateException>(output.Dispose);
        Assert.Empty(api.Freed);
        Assert.Equal(0, api.Closes);
    }

    [Fact]
    public void AFailedUnprepareRetainsMemoryAndIsRetriedBeforeTheNextUtterance()
    {
        using var api = new FakeWinMm();
        using var output = new StoryVoiceWaveOutput(api);
        output.TryPlay(Wave(100));
        api.Finish();
        api.UnprepareResult = 5;

        Assert.Throws<InvalidOperationException>(() => output.TryPlay(Wave(100)));
        Assert.Empty(api.Freed);
        Assert.Single(api.Written);

        api.UnprepareResult = 0;
        Assert.True(output.TryPlay(Wave(100)));
        Assert.Equal(2, api.Freed.Count);
    }

    [Fact]
    public void AFailedPrepareFreesItsUnownedMemoryAndLeavesTheDeviceUsable()
    {
        using var api = new FakeWinMm { PrepareResult = 11 };
        using var output = new StoryVoiceWaveOutput(api);

        Assert.Throws<InvalidOperationException>(() => output.TryPlay(Wave(100)));
        Assert.False(output.Playing);
        Assert.Equal(api.Allocated.Count, api.Freed.Count);

        api.PrepareResult = 0;
        Assert.True(output.TryPlay(Wave(100)));
    }

    [Theory]
    [InlineData(1)]   // the sample buffer
    [InlineData(2)]   // the WAVEHDR, after the sample was already allocated
    public void AFailedAllocationFreesWhatWasAllocatedAndLeavesTheDeviceUsable(int failing)
    {
        using var api = new FakeWinMm { FailAllocation = failing };
        using var output = new StoryVoiceWaveOutput(api);

        Assert.Throws<OutOfMemoryException>(() => output.TryPlay(Wave(100)));
        Assert.False(output.Playing);
        Assert.Equal(api.Allocated.Count, api.Freed.Count);
        Assert.Empty(api.PreparedHeaders);

        api.FailAllocation = 0;
        Assert.True(output.TryPlay(Wave(100)));
    }

    [Fact]
    public void AFailedWriteUnpreparesBeforeFreeing()
    {
        using var api = new FakeWinMm { WriteResult = 6 };
        using var output = new StoryVoiceWaveOutput(api);

        Assert.Throws<InvalidOperationException>(() => output.TryPlay(Wave(100)));
        Assert.False(output.Playing);
        Assert.Empty(api.PreparedHeaders);
        Assert.Equal(2, api.Freed.Count);
    }

    [Fact]
    public void AFailedOpenAllocatesNothingAndCanBeRetried()
    {
        using var api = new FakeWinMm { OpenResult = 2 };
        using var output = new StoryVoiceWaveOutput(api);
        Assert.Throws<InvalidOperationException>(() => output.TryPlay(Wave(100)));
        Assert.Empty(api.Allocated);
        api.OpenResult = 0;
        Assert.True(output.TryPlay(Wave(100)));
    }

    [Fact]
    public void DisposeResetsUnpreparesFreesAndClosesExactlyOnce()
    {
        using var api = new FakeWinMm();
        var output = new StoryVoiceWaveOutput(api);
        output.TryPlay(Wave(100));

        output.Dispose();
        output.Dispose();

        Assert.Equal(1, api.Closes);
        Assert.Empty(api.PreparedHeaders);
        Assert.Equal(api.Allocated.Count, api.Freed.Count);
        Assert.Throws<ObjectDisposedException>(() => output.TryPlay(Wave(100)));
    }

    [Fact]
    public void DisposeWithAFailedUnprepareRetainsMemoryButStillClosesNothingInFlight()
    {
        using var api = new FakeWinMm();
        var output = new StoryVoiceWaveOutput(api);
        output.TryPlay(Wave(100));
        api.UnprepareResult = 5;

        Assert.Throws<AggregateException>(output.Dispose);
        Assert.Empty(api.Freed);
    }

    [Fact]
    public void AFailedCloseIsReportedAfterTheBuffersWereSafelyReturned()
    {
        using var api = new FakeWinMm();
        var output = new StoryVoiceWaveOutput(api);
        output.TryPlay(Wave(100));
        api.CloseResult = 7;

        Assert.Throws<AggregateException>(output.Dispose);
        Assert.Equal(api.Allocated.Count, api.Freed.Count);
        Assert.Equal(0, api.Closes);
    }

    [Fact]
    public void DisposingAnUnusedOutputNeverOpensTheDevice()
    {
        using var api = new FakeWinMm();
        new StoryVoiceWaveOutput(api).Dispose();
        Assert.Equal(0, api.Opens);
        Assert.Equal(0, api.Closes);
    }

    internal static byte[] Wave(int dataBytes)
    {
        var wave = new byte[44 + dataBytes];
        "RIFF"u8.CopyTo(wave);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length - 8);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), 24000);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), 48000);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16);
        "data"u8.CopyTo(wave.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), dataBytes);
        for (var i = 44; i < wave.Length; i++) wave[i] = (byte)i;
        return wave;
    }

    private sealed class FakeWinMm : IStoryWaveOutApi, IDisposable
    {
        private const uint Done = 1, Prepared = 2, Queued = 0x10, StillPlaying = 33;
        private readonly nint device = 0x5150;
        private int Flags(nint header) => Marshal.ReadInt32(header, StoryVoiceWaveOutput.HeaderFlagsOffset);
        private void SetFlags(nint header, uint value) => Marshal.WriteInt32(header, StoryVoiceWaveOutput.HeaderFlagsOffset, (int)value);

        public uint OpenResult, PrepareResult, WriteResult, ResetResult, UnprepareResult, CloseResult;
        public int Opens, Resets, Closes;
        public List<nint> Allocated { get; } = [];
        public List<nint> Freed { get; } = [];
        public HashSet<nint> PreparedHeaders { get; } = [];
        public List<nint> QueuedHeaders { get; } = [];
        public List<byte[]> Written { get; } = [];

        public int FailAllocation;

        public nint Allocate(int bytes)
        {
            if (FailAllocation != 0 && Allocated.Count + 1 == FailAllocation) throw new OutOfMemoryException();
            var memory = Marshal.AllocHGlobal(bytes);
            Allocated.Add(memory);
            return memory;
        }

        public void Free(nint memory)
        {
            Assert.Contains(memory, Allocated);
            Assert.DoesNotContain(memory, Freed);
            Assert.DoesNotContain(memory, PreparedHeaders);
            Assert.DoesNotContain(memory, QueuedHeaders);
            Assert.False(QueuedHeaders.Any(h => Marshal.ReadIntPtr(h) == memory), "The driver still owns this sample.");
            Freed.Add(memory);
        }

        public uint Open(out nint handle)
        {
            handle = OpenResult == 0 ? device : 0;
            if (OpenResult == 0) Opens++;
            return OpenResult;
        }

        public uint Prepare(nint handle, nint header)
        {
            Assert.Equal(device, handle);
            if (PrepareResult != 0) return PrepareResult;
            SetFlags(header, (uint)Flags(header) | Prepared);
            PreparedHeaders.Add(header);
            return 0;
        }

        public uint Write(nint handle, nint header)
        {
            Assert.Contains(header, PreparedHeaders);
            if (WriteResult != 0) return WriteResult;
            SetFlags(header, ((uint)Flags(header) | Queued) & ~Done);
            QueuedHeaders.Add(header);
            var length = Marshal.ReadInt32(header, IntPtr.Size);
            var bytes = new byte[length];
            Marshal.Copy(Marshal.ReadIntPtr(header), bytes, 0, length);
            Written.Add(bytes);
            return 0;
        }

        public void Finish()
        {
            foreach (var header in QueuedHeaders) SetFlags(header, ((uint)Flags(header) | Done) & ~Queued);
            QueuedHeaders.Clear();
        }

        public uint Reset(nint handle)
        {
            Resets++;
            if (ResetResult != 0) return ResetResult;
            Finish();
            return 0;
        }

        public uint Unprepare(nint handle, nint header)
        {
            if (QueuedHeaders.Contains(header)) return StillPlaying;
            if (UnprepareResult != 0) return UnprepareResult;
            SetFlags(header, (uint)Flags(header) & ~Prepared);
            PreparedHeaders.Remove(header);
            return 0;
        }

        public uint Close(nint handle)
        {
            Assert.Empty(QueuedHeaders);
            Assert.Empty(PreparedHeaders);
            if (CloseResult == 0) Closes++;
            return CloseResult;
        }

        public void Dispose()
        {
            foreach (var memory in Allocated) Marshal.FreeHGlobal(memory);
        }
    }
}
