using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.Startup;

public sealed record OpeningMovieEntry(TimeSpan Offset, string Text);

public interface IOpeningMovieDelay
{
    ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class OpeningMovieTimeline
{
    public static IReadOnlyList<OpeningMovieEntry> Entries { get; } =
    [
        new(TimeSpan.Zero, "A silver pendant spins in sunlight above the ocean."),
        new(TimeSpan.FromSeconds(10), "A framed group portrait gives way to close-ups of a knight-like frog and several young adventurers."),
        new(TimeSpan.FromSeconds(25), "A clock races across different eras, from ancient prehistory to medieval kingdoms."),
        new(TimeSpan.FromSeconds(45), "A red-haired swordsman battles through forests as a huge dinosaur and a metal robot appear."),
        new(TimeSpan.FromSeconds(65), "An inventor with purple hair smiles; a dark, caped sorcerer turns beneath the moon."),
        new(TimeSpan.FromSeconds(85), "A princess in white appears as shadowy monsters gather."),
        new(TimeSpan.FromSeconds(105), "The heroes charge across a bridge, battling with lightning and fire."),
        new(TimeSpan.FromSeconds(135), "A glowing circular time gate opens, then a machine flares with light."),
        new(TimeSpan.FromSeconds(150), "The Chrono Trigger title appears."),
    ];

    private readonly IOpeningMovieDelay delay;

    public OpeningMovieTimeline()
        : this(new SystemOpeningMovieDelay())
    {
    }

    public OpeningMovieTimeline(IOpeningMovieDelay delay)
    {
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async Task RunAsync(
        int generation,
        Func<bool> remainsAuthoritative,
        Action<TimedDescription> publish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remainsAuthoritative);
        ArgumentNullException.ThrowIfNull(publish);

        var previousOffset = TimeSpan.Zero;
        foreach (var entry in Entries)
        {
            var wait = entry.Offset - previousOffset;
            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await delay.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            if (cancellationToken.IsCancellationRequested || !remainsAuthoritative())
            {
                return;
            }

            publish(new TimedDescription(entry.Text, generation));
            previousOffset = entry.Offset;
        }
    }

    private sealed class SystemOpeningMovieDelay : IOpeningMovieDelay
    {
        public ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            new(Task.Delay(delay, cancellationToken));
    }
}
