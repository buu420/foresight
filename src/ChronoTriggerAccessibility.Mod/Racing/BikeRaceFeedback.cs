using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Racing;

public sealed class BikeRaceFeedback(Action<string> speak, Action<BikeRaceLane> tone, Action stop,
    Func<bool>? tonesAvailable = null)
{
    private BikeRaceSnapshot? previous, announced;
    private long lastSpeech, lastTone = -1, leadSince;
    private int candidateLead, milestone;
    private int? announcedLead;
    private bool unavailable, unfocused;
    private bool paused;
    private bool toneFailureReported;

    public void Update(BikeRaceSnapshot? frame, long now, bool foreground, bool repeat = false)
    {
        if (!foreground) { stop(); lastTone = -1; unfocused = true; return; }
        if (frame is null)
        {
            stop(); lastTone = -1;
            if (!unavailable || repeat) speak("Bike race information unavailable.");
            unavailable = true;
            return;
        }
        var recovered = unavailable || unfocused;
        unavailable = unfocused = false;
        if (frame.Paused)
        {
            stop(); lastTone = -1;
            if (!paused || repeat) speak("Bike race paused.");
            paused = true; return;
        }
        if (paused) { recovered = true; paused = false; }
        var audioWorks = tonesAvailable?.Invoke() ?? true;
        if (frame.Phase == BikeRacePhase.Racing && audioWorks)
        {
            if (lastTone < 0 || now < lastTone || now - lastTone >= 350)
            { tone(frame.JohnnyLane); lastTone = now; }
        }
        else { stop(); lastTone = -1; }

        var parts = new List<string>();
        var speaksLead = false;
        var urgent = repeat || recovered;
        if (!audioWorks && !toneFailureReported && frame.Phase != BikeRacePhase.Finished)
        {
            parts.Add("Bike race tones are unavailable. Press K or right stick to read Johnny's position.");
            toneFailureReported = true; urgent = true;
        }
        if (previous is null)
        {
            parts.Add("Bike race. Up and down steer. " + (frame.BoostsEnabled ? "Dash boosts. " : "No boosts in this mode. ") +
                "High tone: Johnny above; low: below; middle: aligned. K or right stick repeats status.");
            urgent = true; candidateLead = frame.Lead; leadSince = now;
        }
        if (frame.Phase == BikeRacePhase.Finished)
        {
            if (previous?.Phase != frame.Phase || previous.Result != frame.Result || repeat || recovered)
            {
                parts.Add(frame.Result switch { BikeRaceResult.Won => "You win!", BikeRaceResult.Lost => "Johnny wins.", _ => "Race finished." });
                parts.Add($"Score {frame.Score}."); urgent = true;
            }
        }
        else if (frame.Phase == BikeRacePhase.Preparing)
        {
            if (previous is null || repeat || recovered) parts.Add("Preparing to race.");
        }
        else
        {
            if (previous?.Phase != BikeRacePhase.Racing) { parts.Add("Go!"); urgent = true; }
            if (frame.Lead != candidateLead) { candidateLead = frame.Lead; leadSince = now; }
            var full = repeat || recovered || previous is null || previous.Phase != BikeRacePhase.Racing;
            if (full)
            {
                parts.Add(Status(frame));
                speaksLead = true;
                milestone = 0;
            }
            else
            {
                if (frame.Lead != announcedLead && now - leadSince >= 600)
                { parts.Add(Lead(frame.Lead)); speaksLead = true; }
                if (frame.BoostsEnabled && frame.Boosts != announced?.Boosts)
                    parts.Add($"{frame.Boosts} boosts left.");
                if (frame.BoostsEnabled && frame.Boosts > 0 && frame.BoostReady && announced?.BoostReady != true)
                    parts.Add("Boost ready.");
                foreach (var point in new[] { 600, 300, 100, 50 })
                    if (previous!.Distance > point && frame.Distance <= point) milestone = point;
                if (milestone != 0) parts.Add($"{milestone} to finish.");
            }
        }
        // Coalesce state changes, never queue old positions. Results and explicit
        // repeat requests can interrupt; ordinary HUD updates have breathing room.
        if (parts.Count > 0 && (urgent || now < lastSpeech || now - lastSpeech >= 1600))
        {
            speak(string.Join(" ", parts)); lastSpeech = now; announced = frame; milestone = 0;
            if (speaksLead) announcedLead = frame.Lead;
        }
        previous = frame;
    }

    private static string Lead(int lead) => lead > 0 ? "You lead." : lead < 0 ? "Johnny leads." : "Even.";
    private static string Status(BikeRaceSnapshot frame) =>
        $"{Lead(frame.Lead)} Johnny {frame.JohnnyLane.ToString().ToLowerInvariant()}. {frame.Distance} to finish. Score {frame.Score}. " +
        (frame.BoostsEnabled ? $"{frame.Boosts} boosts left. " +
            (frame.Boosts == 0 ? "" : frame.BoostReady ? "Boost ready." : "Recharging.") : "No boosts in this mode.");

    public void Close()
    {
        stop(); previous = announced = null; lastTone = -1; milestone = 0;
        announcedLead = null;
        unavailable = unfocused = paused = toneFailureReported = false;
    }
}
