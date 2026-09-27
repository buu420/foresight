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
        new(TimeSpan.FromSeconds(0), "Sunlight shines in a blue sky."),
        new(TimeSpan.FromSeconds(4), "Birds fly over the sea as the view pulls back through a window."),
        new(TimeSpan.FromSeconds(9), "A hand picks up a framed photograph beside a vase."),
        new(TimeSpan.FromSeconds(15), "The adventurers pose together. Close-ups show the smiling faces of Marle, Frog, Lucca, Robo, Ayla, and Crono."),
        new(TimeSpan.FromSeconds(25), "The title: Chrono Trigger."),
        new(TimeSpan.FromSeconds(33), "A spiked red shape glows. Dates spin around a clock, across castles, floating islands, and futuristic domes."),
        new(TimeSpan.FromSeconds(48), "A silver aircraft emerges."),
        new(TimeSpan.FromSeconds(51), "Crono clashes swords with a green reptile, then a purple beast."),
        new(TimeSpan.FromSeconds(58), "Ayla runs through a forest and leaps onto a flying pterodactyl."),
        new(TimeSpan.FromSeconds(66), "Lucca fires at surrounding robots. Robo blasts them, and she smiles."),
        new(TimeSpan.FromSeconds(75), "In a stone dungeon, Frog draws his sword."),
        new(TimeSpan.FromSeconds(81), "Under a full moon, Crono practices with a wooden sword, then wipes his brow."),
        new(TimeSpan.FromSeconds(88), "Lucca pauses her work, screwdriver between her teeth, then takes off her glasses."),
        new(TimeSpan.FromSeconds(95), "Marle clutches her glowing pendant. Colorful light ripples around her."),
        new(TimeSpan.FromSeconds(104), "Magus's cape billows against the full moon."),
        new(TimeSpan.FromSeconds(109), "A huge green dinosaur smashes through a wall. Crono, Ayla, and Robo face it."),
        new(TimeSpan.FromSeconds(119), "Crono charges. Robo launches his fist on a chain."),
        new(TimeSpan.FromSeconds(125), "In driving rain, a giant skeleton looms over Crono, Frog and Lucca, electricity crackling around it. Lucca fires."),
        new(TimeSpan.FromSeconds(136), "Crono leaps at a giant serpent. It bursts apart under sword strokes and fire."),
        new(TimeSpan.FromSeconds(143), "The heroes speed through the clouds in their silver aircraft, its engines blazing."),
        new(TimeSpan.FromSeconds(151), "The Chrono Trigger logo appears."),
        new(TimeSpan.FromSeconds(155), "Descriptions by ViddyScribe."),
    ];

    private readonly IOpeningMovieDelay delay;
    private readonly Func<CancellationToken, Task<bool>>? nativeNarration;
    private readonly TimeProvider clock;

    public OpeningMovieTimeline(Func<CancellationToken, Task<bool>>? nativeNarration = null)
        : this(new SystemOpeningMovieDelay(), nativeNarration)
    {
    }

    public OpeningMovieTimeline(IOpeningMovieDelay delay,
        Func<CancellationToken, Task<bool>>? nativeNarration = null, TimeProvider? clock = null)
    {
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
        this.nativeNarration = nativeNarration;
        this.clock = clock ?? TimeProvider.System;
    }

    public async Task RunAsync(
        int generation,
        Func<bool> remainsAuthoritative,
        Action<TimedDescription> publish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remainsAuthoritative);
        ArgumentNullException.ThrowIfNull(publish);
        var started = clock.GetTimestamp();

        if (nativeNarration is not null)
        {
            try
            {
                if (await nativeNarration(cancellationToken).ConfigureAwait(false)) return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }

        for (var index = 0; index < Entries.Count; index++)
        {
            // The native movie keeps running while the file is verified. Resume
            // with the current cue, never a burst of descriptions of earlier shots.
            var elapsed = clock.GetElapsedTime(started);
            if (elapsed.TotalSeconds >= 158.358208) return;
            while (index + 1 < Entries.Count && Entries[index + 1].Offset <= elapsed) index++;
            var wait = Entries[index].Offset - elapsed;
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
            // A delayed continuation may have missed more than one cue as well.
            elapsed = clock.GetElapsedTime(started);
            if (elapsed.TotalSeconds >= 158.358208) return;
            while (index + 1 < Entries.Count && Entries[index + 1].Offset <= elapsed) index++;
            publish(new TimedDescription(Entries[index].Text, generation));
        }
    }

    private sealed class SystemOpeningMovieDelay : IOpeningMovieDelay
    {
        public ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            new(Task.Delay(delay, cancellationToken));
    }
}
