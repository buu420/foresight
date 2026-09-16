using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VehiclePromptAnnouncerTests
{
    [Fact]
    public void BoardingPromptIsSpokenOncePerRisingEdgeAndNamesTheVehicleUnderTheParty()
    {
        var speech = new List<string>();
        var announcer = new VehiclePromptAnnouncer(speech.Add);
        announcer.Observe(1, State(boarding: false));
        announcer.Observe(1, State(boarding: true), 520, 600);
        announcer.Observe(1, State(boarding: true), 520, 600);
        Assert.Equal(["On the Epoch. Press Confirm to board."], speech);
        announcer.Observe(1, State(boarding: false));
        announcer.Observe(1, State(boarding: true, dactyls: true), 720, 480);
        Assert.Equal("On the Dactyls. Press Confirm to board.", speech[^1]);
        announcer.Observe(1, State(boarding: true, dactyls: true), 720, 480);
        Assert.Equal(2, speech.Count);
    }

    [Fact]
    public void LandingPromptOnlyCountsWhileAttachedAndBoardingPromptOnlyWhileWalking()
    {
        var speech = new List<string>();
        var announcer = new VehiclePromptAnnouncer(speech.Add);
        announcer.Observe(1, State(landing: true, transport: 0));
        Assert.Empty(speech);
        announcer.Observe(1, State(landing: true, transport: 2));
        Assert.Equal(["Landing possible. Press Confirm to land."], speech);
        announcer.Observe(1, State(landing: false, transport: 2));
        announcer.Observe(1, State(landing: true, transport: 3));
        Assert.Equal(2, speech.Count);
        announcer.Observe(1, State(boarding: true, transport: 4));
        Assert.Equal(2, speech.Count);
    }

    [Fact]
    public void AContextChangeOrMissingStateRearmsTheEdges()
    {
        var speech = new List<string>();
        var announcer = new VehiclePromptAnnouncer(speech.Add);
        announcer.Observe(1, State(boarding: true), 520, 600);
        announcer.Observe(2, State(boarding: true), 520, 600);
        Assert.Equal(2, speech.Count);
        announcer.Observe(2, null);
        announcer.Observe(2, State(boarding: true), 520, 600);
        Assert.Equal(3, speech.Count);
    }

    [Fact]
    public void TheNativeOmenContactAnnouncesItsEntryChoiceInsteadOfLanding()
    {
        var speech = new List<string>();
        var announcer = new VehiclePromptAnnouncer(speech.Add);
        var state = State(transport: 2, landing: true) with
        { BlackOmen = new(640, 400, 0xD30, new(8, 8, 8, 8), true) };
        announcer.Observe(1, state);
        announcer.Observe(1, state);
        Assert.Contains("Black Omen", Assert.Single(speech));
        Assert.Contains("Confirm", speech[0]);
        Assert.DoesNotContain("land", speech[0], StringComparison.OrdinalIgnoreCase);
        announcer.Observe(1, state with { BlackOmen = null, LandingPrompt = false });
        announcer.Observe(1, state);
        Assert.Equal(2, speech.Count);
    }

    private static WorldVehicleState State(bool boarding = false, bool landing = false, int transport = 0, bool dactyls = false) =>
        new(true, 520, 600, true, dactyls, 720, 480, transport, 1, boarding, landing);
}
