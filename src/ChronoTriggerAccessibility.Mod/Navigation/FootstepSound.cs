using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Owns a background audio worker. Game hooks only post a bounded request;
/// wave loading and device calls never run on the game's input thread.</summary>
public sealed class FootstepSound(Action<string> diagnostic)
{
    private readonly Channel<(bool Play, long Time, long Generation)> queue =
        Channel.CreateBounded<(bool, long, long)>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private int started;
    private long generation;

    public void Play()
    {
        if (Interlocked.Exchange(ref started, 1) == 0) _ = Task.Run(Run);
        queue.Writer.TryWrite((true, Environment.TickCount64, Volatile.Read(ref generation)));
    }

    public void Stop()
    {
        var current = Interlocked.Increment(ref generation);
        if (Volatile.Read(ref started) != 0) queue.Writer.TryWrite((false, 0, current));
    }

    private async Task Run()
    {
        // Buffers remain pinned for the process lifetime: WinMM retains their addresses
        // after SND_ASYNC returns. A suspended mod can be enabled again safely.
        var buffers = new List<GCHandle>();
        var durations = new List<int>();
        var ownsSound = false;
        long playingUntil = 0;
        var index = 0;
        try
        {
            for (var i = 1; i <= 5; i++)
            {
                using var stream = typeof(FootstepSound).Assembly.GetManifestResourceStream(
                    $"ChronoTriggerAccessibility.Mod.Audio.footstep-{i}.wav")
                    ?? throw new InvalidDataException($"Footstep variation {i} is missing.");
                using var output = new MemoryStream();
                stream.CopyTo(output);
                var bytes = output.ToArray();
                // Prepared assets are 48 kHz, mono, 16-bit PCM with a 44-byte header.
                durations.Add((int)Math.Ceiling((bytes.Length - 44) / 96.0));
                buffers.Add(GCHandle.Alloc(bytes, GCHandleType.Pinned));
            }
            await foreach (var request in queue.Reader.ReadAllAsync())
            {
                if (Environment.TickCount64 >= playingUntil) ownsSound = false;
                if (!request.Play)
                {
                    if (ownsSound) { PlaySound(0, 0, 0); ownsSound = false; }
                    continue;
                }
                if (request.Generation != Volatile.Read(ref generation) ||
                    Environment.TickCount64 - request.Time > 150) continue;
                // NOSTOP yields to an existing WinMM sound; NODEFAULT prevents a system beep.
                if (PlaySound(buffers[index].AddrOfPinnedObject(), 0, 0x17))
                {
                    ownsSound = true;
                    playingUntil = Environment.TickCount64 + durations[index];
                }
                index = (index + 1) % buffers.Count;
            }
        }
        catch (Exception error)
        {
            diagnostic($"Footstep audio unavailable: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            if (ownsSound && Environment.TickCount64 < playingUntil) PlaySound(0, 0, 0);
            foreach (var buffer in buffers) if (buffer.IsAllocated) buffer.Free();
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(nint sound, nint module, uint flags);
}
