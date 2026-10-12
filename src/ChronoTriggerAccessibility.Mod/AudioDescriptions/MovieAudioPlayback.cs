namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>Movie-local recovery; never faults the shared accessibility boundary.</summary>
public sealed class MovieAudioPlayback(Action<string> diagnostic, Action<string> unavailable)
{
    private string? readyMovie;
    public bool OpeningReady => Volatile.Read(ref readyMovie) == "001.dat";

    /// <param name="narrate">Returns a narrated native result, or null to build the original topology.</param>
    /// <param name="identified">The original movie the narrator recognised, narrated or not.</param>
    /// <param name="began">Receives a known non-opening movie and whether its own voice plays, after a
    /// successful native build.</param>
    public int Build(Func<IMoviePresentation> getPresentation, Func<string?> matchMovie, Func<int> original,
        Func<IMoviePresentation, int?>? narrate = null, Func<string?>? identified = null,
        Action<string, bool>? began = null)
    {
        Volatile.Write(ref readyMovie, null);
        string? movie = null;
        IMoviePresentation? presentation = null;
        (MovieStreamKind Kind, bool Selected)[]? before = null;
        var selected = false;
        try
        {
            presentation = getPresentation();
            if (presentation.Count == 3 && (movie = matchMovie()) is not null)
            {
                before = Enumerable.Range(0, 3).Select(presentation.GetStream).ToArray();
                selected = MovieAudioTracks.SelectTogether(presentation);
                if (!selected) throw new InvalidDataException("The verified movie has an unexpected stream layout.");
            }
        }
        catch (Exception exception) { Report(exception.Message); }

        // A separate pack's own narration track is never doubled with injected narration.
        if (!selected && presentation is not null && narrate is not null)
        {
            try
            {
                if (narrate(presentation) is int narrated)
                {
                    if (narrated >= 0) Begin(began, Identified(identified), true);
                    return narrated;
                }
            }
            catch (Exception exception)
            {
                Log("Movie narration unavailable: " + exception.Message);
                try { unavailable(MovieNarrationInjector.UnavailableMessage); }
                catch (Exception) { /* Diagnostics must not escape into game code. */ }
            }
        }

        var result = original();
        if (selected && result < 0)
        {
            Report($"Native movie topology returned 0x{result:X8}; restoring its original streams.");
            // This builder returns its output only on success; failed partial nodes
            // are already released by the native code. Retry its normal presentation.
            var restored = false;
            try
            {
                for (var i = 0; i < before!.Length; i++)
                    presentation!.Select(i, before[i].Selected);
                restored = true;
            }
            catch (Exception exception) { Report(exception.Message); }
            if (restored) result = original();
            if (result >= 0) Begin(began, movie, false);
            return result;
        }
        if (selected && result >= 0)
        {
            Volatile.Write(ref readyMovie, movie);
            Log($"Movie audio: {movie}; original soundtrack and own-voice track selected together; no gain changes.");
        }
        if (result >= 0) Begin(began, movie ?? Identified(identified), selected);
        return result;
    }

    private string? Identified(Func<string?>? identified)
    {
        try { return identified?.Invoke(); }
        catch (Exception exception) { Log("Movie identification failed: " + exception.Message); return null; }
    }

    // The opening keeps its existing timeline and readiness contract, unchanged.
    private void Begin(Action<string, bool>? began, string? movie, bool narrated)
    {
        if (began is null || movie is null or "001.dat") return;
        try { began(movie, narrated); }
        catch (Exception exception) { Log("Movie lifecycle start failed: " + exception.Message); }
    }

    public void Report(string detail)
    {
        Log("Movie narration unavailable: " + detail);
        try { unavailable("Movie voice track unavailable. Opening descriptions will use the screen reader."); }
        catch (Exception) { /* Diagnostics must not escape into game code. */ }
    }

    private void Log(string message)
    {
        try { diagnostic(message); }
        catch (Exception) { /* Diagnostics must not escape into game code. */ }
    }
}
