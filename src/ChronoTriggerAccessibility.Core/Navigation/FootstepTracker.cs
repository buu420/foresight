namespace ChronoTriggerAccessibility.Core.Navigation;

public readonly record struct FootstepFrame(ulong Context, int Scene, int Actor, int X, int Y,
    int UnitsPerStep = NavigationUnits.LocalStep);

/// <summary>Measures movement between accepted field-input ticks. Input requests
/// alone cannot produce sound, and discontinuities cannot accumulate footsteps.</summary>
public sealed class FootstepTracker
{
    // A discontinuity guard in physical fine coordinates (16 rendered pixels),
    // independent of the different local/world navigation step lengths.
    private const double MaximumTickDistance = 16 * 16;
    private FootstepFrame? previous;
    private uint previousPad;
    private long previousTime = -1;
    private double distance;
    private double releaseDistance;
    private long releaseTime = -1;
    private NavigationLeg? guidance;
    private int expectedLegBeats, legBeats;
    private bool discontinuity;
    public string State { get; private set; } = "reset";
    public double Distance => distance;
    public long Elapsed { get; private set; }
    public int Steps { get; private set; }

    public bool Update(FootstepFrame? frame, uint acceptedPad, long now, NavigationLeg? currentGuidance = null)
    {
        var before = previous;
        Measure(frame, acceptedPad, now);
        if (frame is not { } current) return false;
        if (currentGuidance is not null && currentGuidance.UnitsPerStep != current.UnitsPerStep)
            throw new ArgumentException("Navigation and footstep units disagree.", nameof(currentGuidance));
        legBeats += Steps;
        if (discontinuity || currentGuidance != guidance)
        {
            // Navigation accepts a turn/arrival slightly before the exact endpoint.
            // Complete only the full beats promised by the old instruction, then
            // begin the new count at this position. Fractional legs keep fractions.
            var completed = !discontinuity && guidance is { } old &&
                (currentGuidance is null || currentGuidance.End != old.End) && before is { } prior &&
                (current.X != prior.X || current.Y != prior.Y) &&
                Math.Abs((long)current.X - old.End.X) <= old.UnitsPerStep / 8 &&
                Math.Abs((long)current.Y - old.End.Y) <= old.UnitsPerStep / 8;
            if (completed) Steps += Math.Min(1, Math.Max(0, expectedLegBeats - legBeats));
            else Steps = 0;
            guidance = currentGuidance;
            distance = 0;
            legBeats = expectedLegBeats = 0;
            if (guidance is { } next)
            {
                var remaining = next.Direction is NavigationDirection.East or NavigationDirection.West
                    ? Math.Abs((long)current.X - next.End.X) : Math.Abs((long)current.Y - next.End.Y);
                expectedLegBeats = next.ExpectedSteps ?? (int)(Math.Ceiling(remaining * 4.0 / next.UnitsPerStep) / 4);
            }
        }
        if (Steps > 0) State = "step";
        return Steps > 0;
    }

    private bool Measure(FootstepFrame? frame, uint acceptedPad, long now)
    {
        Steps = 0;
        discontinuity = false;
        if (frame is not { } current) { Reset(); return false; }
        if (current.UnitsPerStep <= 0) throw new ArgumentOutOfRangeException(nameof(frame));
        var before = previous;
        var elapsed = previousTime < 0 ? -1 : now - previousTime;
        Elapsed = elapsed;
        var movedByInput = (previousPad & 0xF00) != 0;
        previous = current;
        previousPad = acceptedPad;
        previousTime = now;
        // The native field updater can sample twice within the same Windows clock
        // tick. Zero elapsed time is valid; unchanged coordinates stay silent below.
        if (before is not { } old || elapsed is < 0 or > 250 ||
            old.Context != current.Context || old.Scene != current.Scene || old.Actor != current.Actor ||
            old.UnitsPerStep != current.UnitsPerStep)
        {
            distance = 0;
            discontinuity = true;
            releaseDistance = 0;
            releaseTime = -1;
            State = before is null ? "first" : elapsed < 0 ? "clock" : elapsed > 250 ? "gap" : "identity";
            return false;
        }
        var dx = (double)current.X - old.X;
        var dy = (double)current.Y - old.Y;
        var travelled = Math.Sqrt(dx * dx + dy * dy);
        // A world walking action can complete its eight-pixel step after key
        // release. Permit only that bounded remainder, never unlimited motion.
        if (current.UnitsPerStep == NavigationUnits.WorldStep)
        {
            if (movedByInput) { releaseDistance = current.UnitsPerStep; releaseTime = now; }
            else movedByInput = releaseTime >= 0 && now - releaseTime <= 250 &&
                travelled <= releaseDistance && releaseDistance > 0;
            if ((acceptedPad & 0xF00) == 0) releaseDistance = Math.Max(0, releaseDistance - travelled);
        }
        if (!movedByInput || travelled > MaximumTickDistance)
        {
            // A stationary pause must not erase the fraction already walked.
            if (travelled != 0) { distance = 0; discontinuity = true; }
            State = !movedByInput ? "no-input" : "jump";
            return false;
        }
        distance += travelled;
        if (travelled == 0 || distance < current.UnitsPerStep)
        {
            State = travelled == 0 ? "stationary" : "accumulating";
            return false;
        }
        Steps = (int)(distance / current.UnitsPerStep);
        distance -= Steps * current.UnitsPerStep;
        State = "step";
        return true;
    }

    public void Reset()
    {
        previous = null;
        previousPad = 0;
        previousTime = releaseTime = -1;
        releaseDistance = 0;
        Steps = 0;
        guidance = null;
        expectedLegBeats = legBeats = 0;
        discontinuity = true;
        distance = 0;
        State = "reset";
        Elapsed = -1;
    }
}
