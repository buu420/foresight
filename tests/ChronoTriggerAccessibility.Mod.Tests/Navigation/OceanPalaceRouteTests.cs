using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class OceanPalaceRouteTests
{
    [Theory]
    [InlineData(405, 0, 24, 25, "exit:1")]
    [InlineData(406, 4, 16, 14, "exit:0")]
    [InlineData(405, 4, 24, 25, "exit:7")]
    [InlineData(406, 4, 20, 54, "exit:6")]
    [InlineData(406, 4, 39, 54, "landmark:16")]
    [InlineData(406, 0, 36, 12, "exit:5")]
    [InlineData(405, 7, 24, 25, "exit:4")]
    [InlineData(406, 7, 16, 14, "exit:0")]
    [InlineData(408, 7, 40, 5, "exit:2")]
    [InlineData(409, 7, 10, 5, "exit:1")]
    [InlineData(410, 7, 20, 5, "exit:1")]
    [InlineData(411, 7, 8, 14, "landmark:10")]
    [InlineData(418, 7, 8, 16, "exit:0")]
    [InlineData(412, 7, 17, 28, "exit:2")]
    [InlineData(406, 7, 16, 35, "landmark:22")]
    [InlineData(406, 23, 16, 35, "exit:9")]
    [InlineData(412, 23, 5, 13, "exit:1")]
    [InlineData(406, 23, 39, 35, "landmark:26")]
    [InlineData(406, 55, 39, 35, "exit:8")]
    [InlineData(412, 55, 29, 13, "landmark:9")]
    public void EachSwitchUsesTheEntranceServingItsActualRoom(int scene, int switches,
        int x, int y, string expected)
    {
        var info = GameNavigationCatalog.ForScene(scene)!;
        var ids = info.Exits.Select(e => $"exit:{e.Id}")
            .Concat(info.Actors.Select(a => $"landmark:{a.Id}")).ToArray();
        var available = ids.Select((id, index) => Target(id, index)).ToArray();
        var intended = Assert.Single(available, target => target.Id == expected);
        var state = new FieldStoryState(198, false)
        {
            Globals = new Dictionary<int, int> { [0x162] = switches },
        };
        var result = Assert.Single(new FullStoryTargets().Build(scene, state, available,
            new(x * 256 + 128, y * 256 + 128, 1)), t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(result.IsStoryNote);
        Assert.Equal(intended.ApproachPoints, result.ApproachPoints);
    }

    [Fact]
    public void ElevatorBattlesContinueToTheLowerHall()
    {
        var state = new FieldStoryState(198, false)
        {
            Globals = new Dictionary<int, int> { [0x162] = 0xff },
        };
        var motor = Target("landmark:10", 1);
        Assert.Equal(motor, Assert.Single(new SceneConnectionRouter().Next(411, state, [414],
            [motor, Target("exit:0", 0)], fieldOnly: true)));
    }

    [Fact]
    public void EntranceVisionAdvancesProgressAndCannotBlockTheExitAfterReturning()
    {
        var entrance = GameNavigationCatalog.ForScene(404)!;
        Assert.DoesNotContain(entrance.Regions, r => r.Destination == 415);
        var vision = Assert.Single(entrance.Regions, r => r.Actor == 9 && r.Kind == "Progress");
        var state = new FieldStoryState(194, false)
        {
            Globals = new Dictionary<int, int> { [0xF8] = 0, [0x162] = 0 },
        };
        Assert.True(vision.Available(state));
        Assert.False(vision.Available(state with { Point = 195 }));
        var trigger = Target(vision.Id, 9);
        var result = Assert.Single(new FullStoryTargets().Build(404, state,
            [trigger, Target("exit:0", 0)], new(8 * 256, 24 * 256, 1)),
            t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal(trigger.ApproachPoints, result.ApproachPoints);
        Assert.Contains("Mune", result.Label);
    }

    [Fact]
    public void StairVisionReturnsToTheStairAndCannotServeAsABossShortcut()
    {
        var masa = GameNavigationCatalog.ForScene(409)!.Actors.Single(a => a.Id == 33);
        Assert.DoesNotContain(masa.Actions, a => a.Kind == "Warp");
        Assert.Contains(masa.Actions, a => a.Kind == "Progress" && a.Value == 198 &&
            a.Available(new(197, false)) && !a.Available(new(198, false)));
        var state = new FieldStoryState(197, false)
        {
            Globals = new Dictionary<int, int> { [0x162] = 7 },
        };
        var trigger = Target("landmark:33", 33);
        var result = Assert.Single(new FullStoryTargets().Build(409, state,
            [trigger, Target("exit:1", 1)], new(19 * 256, 117 * 256, 1)),
            t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal(trigger.ApproachPoints, result.ApproachPoints);
        Assert.Contains("Masa", result.Label);
    }

    private static NavigationTarget Target(string id, int index) => new(id, id,
        NavigationCategory.Objects, new(index * 64, 128, 1), [new(index * 64, 128, 1)], true, true);
}
