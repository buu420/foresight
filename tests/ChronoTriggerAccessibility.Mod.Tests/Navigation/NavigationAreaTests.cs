using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NavigationAreaTests
{
    [Fact]
    public void AreaAnnouncementsFollowTheRoundTripAndDoNotRepeatEverySample()
    {
        var speech = new List<string>();
        var announcer = new NavigationAreaAnnouncer(speech.Add, _ => { });
        var graph = new WorldNavigationGraph(new byte[6144], new byte[512]);
        var field = new NavigationFrame("inn",true,new(0,0,1),[],graph) { AreaName = "Truce Inn" };
        var world = field with { Scene = "world", AreaName = "World map, 1000 A.D." };
        announcer.Observe(field); announcer.Observe(field);
        announcer.Observe(world with { CanNavigate = false });
        announcer.Observe(world); announcer.Observe(field);
        Assert.Equal(new[] { "Truce Inn.", "World map, 1000 A.D.", "Truce Inn." }, speech);
    }

    [Fact]
    public void WorldArrivalSaysToConfirmWithoutGeneratingActionInput()
    {
        var point = new NavigationPoint(1024,1024,1);
        var target = new NavigationTarget("inn", "Truce Inn", NavigationCategory.People, point, [point], true, true)
            { ArrivalInstruction = "Press Confirm to enter." };
        var frame = new NavigationFrame("world", true, point, [target],
            new WorldNavigationGraph(new byte[6144],new byte[512]),128);
        var result = new NavigationController().Handle(NavigationCommand.ToggleWalk,frame,0);
        Assert.Contains(result.Speech, s => s == "Arrived at Truce Inn. Press Confirm to enter.");
        Assert.Equal(NavigationDirection.None,result.Direction);
        Assert.False(result.AutoWalking);
    }
}
