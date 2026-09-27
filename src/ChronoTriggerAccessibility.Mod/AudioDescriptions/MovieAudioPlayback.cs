namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>Movie-local recovery; never faults the shared accessibility boundary.</summary>
public sealed class MovieAudioPlayback(Action<string> diagnostic, Action<string> unavailable)
{
    private string? readyMovie;
    public bool OpeningReady => Volatile.Read(ref readyMovie) == "001.dat";

    public int Build(Func<IMoviePresentation> getPresentation, Func<string?> matchMovie, Func<int> original)
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
            return result;
        }
        if (selected && result >= 0)
        {
            Volatile.Write(ref readyMovie, movie);
            Log($"Movie audio: {movie}; original soundtrack and own-voice track selected together; no gain changes.");
        }
        return result;
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
