namespace ChronoTriggerAccessibility.Core.Navigation;

public readonly record struct FootstepFrame(ulong Context, int Scene, int Actor, int X, int Y);

/// <summary>Measures movement between accepted field-input ticks. Input requests
/// alone cannot produce sound, and discontinuities cannot accumulate footsteps.</summary>
public sealed class FootstepTracker
{
    private FootstepFrame? previous;
    private uint previousPad;
    private long previousTime = -1;
    private long lastStep = -1;
    private double distance;
    private const double Stride = 384; // 24 rendered pixels; independent of navigation's tile count.
    public string State { get; private set; } = "reset";
    public double Distance => distance;
    public long Elapsed { get; private set; }

    public bool Update(FootstepFrame? frame, uint acceptedPad, long now)
    {
        if (frame is not { } current) { Reset(); return false; }
        var before = previous;
        var elapsed = previousTime < 0 ? -1 : now - previousTime;
        Elapsed = elapsed;
        var movedByInput = (previousPad & 0xF00) != 0;
        previous = current;
        previousPad = acceptedPad;
        previousTime = now;
        if (before is not { } old || elapsed is <= 0 or > 250 ||
            old.Context != current.Context || old.Scene != current.Scene || old.Actor != current.Actor)
        {
            distance = 0;
            lastStep = -1;
            State = before is null ? "first" : elapsed <= 0 ? "clock" : elapsed > 250 ? "gap" : "identity";
            return false;
        }
        var dx = (double)current.X - old.X;
        var dy = (double)current.Y - old.Y;
        var travelled = Math.Sqrt(dx * dx + dy * dy);
        if (!movedByInput || travelled > 256)
        {
            distance = 0;
            State = !movedByInput ? "no-input" : "jump";
            return false;
        }
        distance += travelled;
        if (travelled == 0 || distance < Stride)
        {
            State = travelled == 0 ? "stationary" : "accumulating";
            return false;
        }
        // Leave room for the 240 ms recordings even at a faster input tick rate.
        // Keep accumulated displacement while rate-limited instead of losing a stride.
        if (lastStep >= 0 && now - lastStep < 250) { State = "rate-limited"; return false; }
        distance %= Stride;
        lastStep = now;
        State = "step";
        return true;
    }

    public void Reset()
    {
        previous = null;
        previousPad = 0;
        previousTime = lastStep = -1;
        distance = 0;
        State = "reset";
        Elapsed = -1;
    }
}
