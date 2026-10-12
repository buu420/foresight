using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

/// <summary>The player's HandleEvent (RVA 0x28762D) drops failed events without telling the game;
/// the observer only reads them, then hands each event to the native handler exactly once.</summary>
public sealed class MoviePlaybackFailureTests
{
    private const nint Player = 0x9000, Event = 0x7000;
    private readonly List<string> announcements = [];
    private readonly MovieNarrationLifetime lifetime;

    public MoviePlaybackFailureTests() => lifetime = new(failed: announcements.Add);

    [Theory]
    [InlineData(unchecked((int)0xC00D36B9))] // event status, e.g. MEError
    [InlineData(unchecked((int)0x80004005))] // GetType or GetStatus itself failed
    public void FailedEventIsAnnouncedOncePerPlayerAndStillHandledNatively(int failure)
    {
        var calls = new List<(nint, nint)>();
        for (var i = 0; i < 3; i++)
            Assert.Equal(failure, lifetime.HandleEvent(Player, Event, _ => failure, (player, e) =>
            {
                calls.Add((player, e));
                return failure;
            }));
        Assert.Equal([(Player, Event), (Player, Event), (Player, Event)], calls);
        Assert.Equal([MovieNarrationLifetime.PlaybackFailedMessage], announcements);
    }

    [Fact]
    public void SuccessfulAndUnrelatedEventsAreUnchanged()
    {
        var calls = 0;
        Assert.Equal(1, lifetime.HandleEvent(Player, Event, _ => null, (_, _) => { calls++; return 1; }));
        Assert.Equal(0, lifetime.HandleEvent(Player, 0, _ => null, (_, _) => { calls++; return 0; }));
        Assert.Equal(2, calls);
        Assert.Empty(announcements);
    }

    [Fact]
    public void FaultyReaderOrAnnouncerCannotSkipTheNativeHandler()
    {
        var calls = 0;
        Assert.Equal(4, lifetime.HandleEvent(Player, Event, _ => throw new InvalidOperationException("reader"),
            (_, _) => { calls++; return 4; }));
        var throwing = new MovieNarrationLifetime(failed: _ => throw new InvalidOperationException("announcer"));
        Assert.Equal(-5, throwing.HandleEvent(Player, Event, _ => -5, (_, _) => { calls++; return -5; }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void EventIsReadBeforeTheNativeHandlerReleasesIt()
    {
        var order = new List<string>();
        lifetime.HandleEvent(Player, Event, _ => { order.Add("read"); return null; }, (_, _) => { order.Add("native"); return 0; });
        Assert.Equal(["read", "native"], order);
    }

    [Fact]
    public void NativeCloseOrHookResetStartsANewPlayerSession()
    {
        lifetime.HandleEvent(Player, Event, _ => -1, (_, _) => -1);
        lifetime.CloseSession(Player, _ => 0, _ => 0);
        lifetime.HandleEvent(Player, Event, _ => -1, (_, _) => -1);
        lifetime.Reset();
        lifetime.HandleEvent(Player, Event, _ => -1, (_, _) => -1);
        Assert.Equal(3, announcements.Count);
        // A different player is its own session.
        lifetime.HandleEvent(0x9100, Event, _ => -1, (_, _) => -1);
        Assert.Equal(4, announcements.Count);
    }
}
