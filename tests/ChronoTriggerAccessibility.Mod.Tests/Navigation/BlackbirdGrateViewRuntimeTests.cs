using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Dispatcher event sequences for a grate view (BlackbirdGrateViewCapture's proven
/// events). The room label is the native field name the area announcer also speaks.</summary>
public sealed class BlackbirdGrateViewRuntimeTests
{
    private const string Passage = "Looking through the grate into Blackbird, Forward Passage. Press Confirm to return to the ventilation shaft.";
    private static readonly BlackbirdGrateViewEvent Opening = new(BlackbirdGrateViewKind.Opening, 376, 1);
    private static readonly BlackbirdGrateViewEvent Viewing = new(BlackbirdGrateViewKind.Viewing, 369, 1);
    private static readonly BlackbirdGrateViewEvent Returning = new(BlackbirdGrateViewKind.Returning, 369, 1);

    [Fact]
    public void AViewIsSpokenOnceOnEntryAndOnceOnReturn()
    {
        var h = new Harness();
        h.Run(Opening, Viewing, Viewing, Viewing, Returning);
        Assert.Equal([Passage, BlackbirdGrateViewRuntime.Returned], h.Speech);
    }

    [Fact]
    public void LookingIntoTheSameRoomAgainIsAnnouncedAgain()
    {
        var h = new Harness();
        h.Run(Opening, Viewing, Viewing, Returning, Opening, Viewing, Viewing, Returning);
        Assert.Equal([Passage, BlackbirdGrateViewRuntime.Returned, Passage, BlackbirdGrateViewRuntime.Returned], h.Speech);
    }

    [Fact]
    public void AMissedReturnStillLetsTheNextViewSpeak()
    {
        var h = new Harness();
        // The return dispatch was unreadable (null); the next opening still resets the view.
        h.Run(Opening, Viewing, null, Opening, Viewing);
        Assert.Equal([Passage, Passage], h.Speech);
    }

    [Fact]
    public void UnknownCapturesNeitherReturnNorSuppress()
    {
        var h = new Harness();
        h.Run(Viewing, null, null, Viewing);
        Assert.Equal([Passage], h.Speech);
        h.Run(null, Returning);
        Assert.Equal([Passage, BlackbirdGrateViewRuntime.Returned], h.Speech);
    }

    [Fact]
    public void AReturnWithoutAViewIsSilent()
    {
        var h = new Harness();
        h.Run(Opening, Returning, Returning);
        Assert.Empty(h.Speech);
    }

    [Fact]
    public void ADifferentRoomIsANewView()
    {
        var h = new Harness();
        h.Run(Opening, Viewing, new(BlackbirdGrateViewKind.Viewing, 444, 1));
        Assert.Equal([Passage, "Looking through the grate into Blackbird, Room 9. Press Confirm to return to the ventilation shaft."], h.Speech);
    }

    [Fact]
    public void ANewEngineCanOpenTheSameViewWhenItsOpeningWasUnreadable()
    {
        var h = new Harness();
        h.Run(Viewing, Viewing with { Context = 2 });
        Assert.Equal([Passage, Passage], h.Speech);
    }

    [Fact]
    public void AForeignReturnDoesNotEndTheCurrentView()
    {
        var h = new Harness();
        h.Run(Viewing, Returning with { Context = 2 }, Viewing);
        Assert.Equal([Passage], h.Speech);
    }

    [Fact]
    public void TheRepeatKeyRepeatsOncePerPressOnlyDuringAView()
    {
        var h = new Harness();
        h.Held = true;
        h.Run(Opening, Viewing, Viewing);       // held on entry: no extra repeat
        h.Held = false;
        h.Run(Viewing);
        h.Held = true;
        h.Run(Viewing, Viewing, Viewing);       // one press, one repeat
        h.Held = false;
        h.Run(Viewing, Returning);
        h.Held = true;
        h.Run(Opening);                          // no view: the key is not ours
        Assert.Equal([Passage, Passage, BlackbirdGrateViewRuntime.Returned], h.Speech);
    }

    [Fact]
    public void OnlyE3AndE0AreCapturedAndNothingRunsWhileDisabled()
    {
        var h = new Harness();
        h.Runtime.Dispatch(1, 0x31);
        h.Runtime.Dispatch(1, 0xAA);
        Assert.Equal(0, h.Captures);
        h.Runtime.Disable();
        h.Next.Enqueue(Viewing);
        h.Runtime.Dispatch(1, 0xE3);
        Assert.Empty(h.Speech);
        Assert.Equal(0, h.Captures);
    }

    [Fact]
    public void ACaptureFaultIsLoggedAndDoesNotEscape()
    {
        var log = new List<string>();
        var runtime = new BlackbirdGrateViewRuntime((_, _) => throw new InvalidOperationException(), _ => null, _ => { }, diagnostic: log.Add);
        runtime.Enable();
        runtime.Dispatch(1, 0xE3);
        Assert.Single(log);
    }

    [Fact]
    public void AnUnnamedRoomFallsBackToTheCatalogName()
    {
        var speech = new List<string>();
        var runtime = new BlackbirdGrateViewRuntime((_, _) => Viewing, _ => null, speech.Add);
        runtime.Enable();
        runtime.Dispatch(1, 0xE3);
        Assert.Equal("Looking through the grate into Blackbird, Forward Passage. Press Confirm to return to the ventilation shaft.", Assert.Single(speech));
    }

    private sealed class Harness
    {
        public List<string> Speech { get; } = [];
        public Queue<BlackbirdGrateViewEvent?> Next { get; } = new();
        public bool Held;
        public int Captures;
        public BlackbirdGrateViewRuntime Runtime { get; }
        public Harness()
        {
            Runtime = new((_, _) => { Captures++; return Next.Dequeue(); },
                scene => scene switch { 369 => "Blackbird, Forward Passage", 444 => "Blackbird, Room 9", _ => null },
                Speech.Add, () => Held);
            Runtime.Enable();
        }
        public void Run(params BlackbirdGrateViewEvent?[] events)
        {
            foreach (var value in events)
            {
                Next.Enqueue(value);
                Runtime.Dispatch(1, value?.Kind == BlackbirdGrateViewKind.Returning ? 0xE0 : 0xE3);
            }
        }
    }
}
