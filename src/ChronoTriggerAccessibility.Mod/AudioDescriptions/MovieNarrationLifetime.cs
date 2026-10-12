namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

public enum MovieLifecycleKind { Started, Ended, Reset }

/// <summary>Narrated is true when the movie plays with an own-voice track (injected or separate pack).</summary>
public sealed record MovieLifecycleChange(MovieLifecycleKind Kind, string Movie, bool Narrated);

/// <summary>A known movie player covers the field from its native topology build until the player's
/// own CloseSession returns. Changes are raised on the game thread; observers must be brief.</summary>
public interface IMovieLifecycle
{
    string? ActiveMovie { get; }
    event Action<MovieLifecycleChange>? Changed;
}

/// <summary>Keeps narration sources alive until the player closes the session that plays them.
/// Each entry is keyed by the player's own movie source, which narration never wraps.</summary>
public sealed class MovieNarrationLifetime(Action<string>? diagnostic = null, Action<string>? failed = null)
    : IMovieLifecycle
{
    public const string PlaybackFailedMessage = "Movie playback failed. Use the game's movie-skip control.";
    private readonly HashSet<nint> failedPlayers = [];
    private readonly Dictionary<nint, INarratedMovieSource> owned = [];
    private readonly List<(nint Source, string Movie, bool Narrated)> active = [];
    private readonly Lock gate = new();
    private string? activeMovie;

    public string? ActiveMovie => Volatile.Read(ref activeMovie);
    public event Action<MovieLifecycleChange>? Changed;

    public bool Owns(nint movieSource)
    {
        lock (gate) return owned.ContainsKey(movieSource);
    }

    public void Attach(nint movieSource, INarratedMovieSource narrated)
    {
        lock (gate) owned.Add(movieSource, narrated);
    }

    public void Detach(nint movieSource)
    {
        lock (gate) owned.Remove(movieSource);
    }

    /// <summary>Called after a successful native topology build for an identified movie. Only valid
    /// while the CloseSession hook is active, which supplies the matching end.</summary>
    public void Begin(nint movieSource, string movie, bool narrated)
    {
        lock (gate)
        {
            if (active.Any(entry => entry.Source == movieSource)) return;
            active.Add((movieSource, movie, narrated));
            Volatile.Write(ref activeMovie, movie);
        }
        Raise(new(MovieLifecycleKind.Started, movie, narrated));
    }

    /// <summary>The hook was disabled, so no native close can arrive. Activity ends; sources are kept,
    /// because a session that may still be playing must not lose its source.</summary>
    public void Reset()
    {
        (nint Source, string Movie, bool Narrated)[] ended;
        lock (gate)
        {
            ended = [.. active];
            active.Clear();
            failedPlayers.Clear();
            Volatile.Write(ref activeMovie, null);
        }
        foreach (var entry in ended) Raise(new(MovieLifecycleKind.Reset, entry.Movie, entry.Narrated));
    }

    /// <summary>The player's CloseSession (libcocos2d RVA 0x287361) closes the media session, waits
    /// for MESessionClosed, then shuts down its source and session. Narration follows afterwards,
    /// before the destructor's MFShutdown.</summary>
    public int CloseSession(nint player, Func<nint, nint> readSource, Func<nint, int> original)
    {
        nint source = 0;
        bool any;
        lock (gate) any = owned.Count > 0 || active.Count > 0;
        if (any)
        {
            try { source = readSource(player); }
            catch (Exception exception) { Log("Movie narration could not identify the closing session: " + exception.Message); }
        }
        var result = original(player);
        // The next session of this player reports its own failure.
        lock (gate) failedPlayers.Remove(player);
        if (source == 0) return result;
        INarratedMovieSource? narrated;
        (nint Source, string Movie, bool Narrated)? ended = null;
        lock (gate)
        {
            owned.Remove(source, out narrated);
            var index = active.FindIndex(entry => entry.Source == source);
            if (index >= 0)
            {
                ended = active[index];
                active.RemoveAt(index);
                Volatile.Write(ref activeMovie, active.Count == 0 ? null : active[^1].Movie);
            }
        }
        if (narrated is not null)
        {
            try
            {
                narrated.Close();
                Log("Movie narration closed with the native player session.");
            }
            catch (Exception exception) { Log("Movie narration close failed: " + exception.Message); }
        }
        if (ended is { } movie) Raise(new(MovieLifecycleKind.Ended, movie.Movie, movie.Narrated));
        return result;
    }

    /// <summary>The player's HandleEvent (libcocos2d RVA 0x28762D, thiscall, RET 4) returns without
    /// notifying the game when GetType, GetStatus or the event status fails, so the movie never
    /// completes. The event is only read here, before the native handler releases it, and is then
    /// handed to that handler exactly once.</summary>
    /// <param name="readFailure">The failing HRESULT, or null for a successful event.</param>
    public int HandleEvent(nint player, nint mediaEvent, Func<nint, int?> readFailure, Func<nint, nint, int> original)
    {
        int? failure = null;
        if (mediaEvent != 0)
        {
            try { failure = readFailure(mediaEvent); }
            catch (Exception exception) { Log("Movie event could not be read: " + exception.Message); }
        }
        var result = original(player, mediaEvent);
        if (failure is { } status)
        {
            bool first;
            lock (gate) first = failedPlayers.Add(player);
            if (first)
            {
                Log($"Movie playback failed natively: 0x{status:X8}; the player ignores this event. Movie: {ActiveMovie ?? "unidentified"}.");
                try { failed?.Invoke(PlaybackFailedMessage); }
                catch (Exception exception) { Log("Movie playback failure announcement failed: " + exception.Message); }
            }
        }
        return result;
    }

    private void Raise(MovieLifecycleChange change)
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<MovieLifecycleChange>>())
        {
            try { handler(change); }
            catch (Exception exception) { Log($"Movie lifecycle observer failed: {exception.Message}"); }
        }
    }

    private void Log(string message)
    {
        try { diagnostic?.Invoke(message); }
        catch (Exception) { /* Diagnostics must not escape into game code. */ }
    }
}
