namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>An aggregate Media Foundation source owned by the mod. Close shuts down the aggregate
/// and its sub-sources; it is only valid when no session uses them.</summary>
public interface INarratedMovieSource
{
    nint Source { get; }
    nint Descriptor { get; }
    IMoviePresentation Presentation { get; }
    void Close();
}

public interface IMovieNarrationMedia
{
    INarratedMovieSource Open(string decodedMovie, MovieNarrationTrack track);
}

/// <summary>Gives the game's own topology builder an aggregate of a second instance of the verified
/// movie and its own-voice track. The player's source is never aggregated: MFCreateAggregateSource
/// subscribes to its sub-sources, after which that source could no longer start, so it could not
/// restore the original topology.</summary>
public sealed class MovieNarrationInjector(IReadOnlyList<MovieNarrationTrack> tracks, IMovieNarrationMedia media,
    MovieNarrationLifetime lifetime, Func<bool> lifetimeActive, Action<string> diagnostic, Action<string> unavailable)
{
    public const string UnavailableMessage = "Recorded movie descriptions are unavailable.";

    /// <summary>The original movie recognised by the latest build, whether or not narration was added.</summary>
    public string? IdentifiedMovie { get; private set; }

    /// <summary>Returns the native result of a narrated topology, or null when the caller must build
    /// the original topology from the player's untouched source.</summary>
    public int? TryBuild(IMoviePresentation presentation, nint movieSource, string decodedMovie,
        Func<nint, nint, int> original)
    {
        OriginalMovie? movie = null;
        INarratedMovieSource? narrated = null;
        IdentifiedMovie = null;
        try
        {
            if (tracks.Count == 0 || !Matches(presentation, MovieStreamKind.Audio, MovieStreamKind.Video)) return null;
            movie = MovieNarrationCatalog.Match(decodedMovie, tracks.Select(track => track.Movie));
            if (movie is null) return null;
            IdentifiedMovie = movie.FileName;
            if (!lifetimeActive()) throw new InvalidOperationException("the native session close hook is not active.");
            if (lifetime.Owns(movieSource)) throw new InvalidOperationException("this player source already has narration.");
            narrated = media.Open(decodedMovie, tracks.Single(track => track.Movie == movie));
            if (!Matches(narrated.Presentation, MovieStreamKind.Audio, MovieStreamKind.Video, MovieStreamKind.Audio))
                throw new InvalidDataException("the narrated movie has an unexpected stream layout.");
            lifetime.Attach(movieSource, narrated);
            var result = original(narrated.Source, narrated.Descriptor);
            // The builder returns output only on success and releases failed partial nodes itself.
            if (result < 0) throw new InvalidOperationException($"native movie topology returned 0x{result:X8}.");
            narrated = null;
            Log($"Movie narration: {movie.FileName}; original soundtrack, video and own-voice narration share the " +
                "native Media Foundation session; no gain changes.");
            return result;
        }
        catch (Exception exception)
        {
            // Owns() was false before Open, so this removes only this attempt's own entry.
            if (narrated is not null) lifetime.Detach(movieSource);
            Log($"Movie narration unavailable for {movie?.FileName ?? "a movie"}: {exception.Message} " +
                "The original movie topology is used.");
            try { unavailable(UnavailableMessage); }
            catch (Exception) { /* Diagnostics must not escape into game code. */ }
            return null;
        }
        finally
        {
            // Only the mod's own second movie instance and narration; no session references them.
            if (narrated is not null)
            {
                try { narrated.Close(); }
                catch (Exception exception) { Log("Movie narration rollback close failed: " + exception.Message); }
            }
        }
    }

    private static bool Matches(IMoviePresentation presentation, params MovieStreamKind[] kinds)
    {
        if (presentation.Count != kinds.Length) return false;
        var streams = Enumerable.Range(0, kinds.Length).Select(presentation.GetStream).ToArray();
        return streams.All(stream => stream.Selected) &&
            streams.Select(stream => stream.Kind).Order().SequenceEqual(kinds.Order());
    }

    private void Log(string message)
    {
        try { diagnostic(message); }
        catch (Exception) { /* Diagnostics must not escape into game code. */ }
    }
}
