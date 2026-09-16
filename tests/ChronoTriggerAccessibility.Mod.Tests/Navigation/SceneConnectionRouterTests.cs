using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class SceneConnectionRouterTests
{
    [Fact]
    public void ChoosesTheOnwardRoomInsteadOfTheNearestBackwardDoor()
    {
        var router = new SceneConnectionRouter();
        // Denadoro Map 7: exit0 -> Map8 -> Map2 -> Masa/Mune;
        // exit1 leads back through Map3 and Map9.
        var result = router.Next(148, new(93, false), [151], [Target("exit:0", 1500), Target("exit:1", 10)]);
        Assert.Equal("exit:0", Assert.Single(result).Id);
    }

    [Fact]
    public void UsesTheNativeMedinaPillarAndRejectsItsBusyState()
    {
        var region = Assert.Single(GameNavigationCatalog.ForScene(464)!.Regions, r => r.Destination == 36);
        Assert.Equal((14, 8, 14, 8), (region.Left, region.Top, region.Right, region.Bottom));
        var router = new SceneConnectionRouter();
        var state = new FieldStoryState(77, false) { Locals = new Dictionary<int, int> { [6] = 0 } };
        var target = Target(region.Id, 100);
        Assert.Equal(target, Assert.Single(router.Next(464, state, [36], [target])));
        Assert.Empty(router.Next(464, state with { Locals = new Dictionary<int, int> { [6] = 1 } }, [36], [target]));
        Assert.Empty(router.Next(464, state with { Point = 76 }, [36], [target]));
    }

    [Fact]
    public void OnlyReturnsWorldEntrancesPresentInTheLiveSnapshot()
    {
        var router = new SceneConnectionRouter();
        var live = new Dictionary<string, int> { ["live-cave"] = 48 };
        var result = router.Next(496, new(78, false), [47], [Target("live-cave", 100), Target("disabled", 1)], live);
        Assert.Equal("live-cave", Assert.Single(result).Id);
        // Melchior's hut only reaches this goal by returning to the same world.
        live["live-cave"] = 43;
        Assert.Empty(router.Next(496, new(78, false), [47], [Target("live-cave", 100)], live));
    }

    [Fact]
    public void AllInstalledBonusScenesHaveNamesAndDeadWorldStubIsRejected()
    {
        Assert.Equal(669, GameNavigationCatalog.Scenes.Count());
        Assert.Equal(240, GameNavigationCatalog.Worlds.Count());
        Assert.NotNull(GameNavigationCatalog.AreaName(654));
        Assert.False(GameNavigationCatalog.IsFieldScene(1023));
        Assert.False(GameNavigationCatalog.IsFieldScene(499));
    }

    [Fact]
    public void LiveFieldDestinationOverridesTheStaticConnection()
    {
        var router = new SceneConnectionRouter();
        var targets = new[] { Target("exit:0", 100), Target("exit:1", 10) };
        // This room normally sends exit0 toward the summit. A native runtime
        // replacement must take precedence, including a disabled destination.
        var live = new Dictionary<int, int> { [0] = 0, [1] = 151 };
        Assert.Equal("exit:1", Assert.Single(router.Next(148, new(93, false), [151], targets,
            liveFieldDestinations: live)).Id);
        live[1] = 1023;
        Assert.Empty(router.Next(148, new(93, false), [151], targets, liveFieldDestinations: live));
    }

    [Fact]
    public void EraInferenceCannotEscapeAFieldThroughAGateOrDifferentWorld()
    {
        var state = new FieldStoryState(174, false)
        {
            Globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0),
            Extended = Enumerable.Range(0, 80).ToDictionary(i => i, _ => 0),
        };
        var router = new SceneConnectionRouter();
        Assert.NotNull(router.DistanceWithinFields(48, state, 47));
        Assert.Null(router.DistanceWithinFields(5, state, 388));
        Assert.Null(router.DistanceWithinFields(113, state, 48));
        Assert.Null(router.DistanceWithinFields(5, state, 113));
        Assert.Null(router.DistanceWithinFields(208, state, 20));
        Assert.Null(router.DistanceWithinFields(307, state, 351));
    }

    [Fact]
    public void GenoConveyorBattlesConnectToTheInteriorWithoutInventingAWalkingExit()
    {
        var state = new FieldStoryState(213, false)
        {
            Globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0),
            Extended = Enumerable.Range(0, 80).ToDictionary(i => i, _ => 0),
        };
        var router = new SceneConnectionRouter();
        Assert.NotNull(router.DistanceWithinFields(248, state, 268));
        var forward = Assert.Single(GameNavigationCatalog.ForScene(249)!.Regions, r => r.Destination == 126);
        Assert.Equal(forward.Id, Assert.Single(router.Next(249, state, [268],
            [Target(forward.Id, 100), Target("exit:0", 1)])).Id);
        Assert.Empty(router.Next(126, state, [268], [Target("fake-exit", 100)]));
    }

    [Theory]
    [InlineData(48, 47, 78)]
    [InlineData(145, 151, 93)]
    [InlineData(283, 289, 123)]
    [InlineData(165, 172, 137)]
    [InlineData(293, 295, 144)]
    [InlineData(299, 301, 151)]
    [InlineData(386, 397, 180)]
    [InlineData(401, 414, 198)]
    [InlineData(371, 363, 209)]
    public void MainDungeonEntranceConnectsToItsFinalObjectiveWithoutAnEraShortcut(int entrance, int goal, int point)
    {
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        globals[0xA3] = 0xFF; // Keep lieutenants and hall light.
        globals[0x162] = 0xFF; // Ocean Palace switches.
        globals[0xAF] = 7; globals[0xBA] = 6; globals[0xB3] = 0x40; // Blackbird stores/duct.
        var state = new FieldStoryState(point, false)
        {
            Globals = globals, Extended = Enumerable.Range(0, 80).ToDictionary(i => i, _ => 0),
        };
        Assert.NotNull(new SceneConnectionRouter().DistanceWithinFields(entrance, state, goal));
    }

    [Fact]
    public void OceanPalacePortalUsesItsUnderwaterContinuationAndRequiresTheLivePortal()
    {
        var router = new SceneConnectionRouter();
        var portal = Target("actor:8:4:151", 384);
        var exit = Target("exit:0", 368);
        var state = new FieldStoryState(189, false);
        var native = GameNavigationCatalog.ForScene(334)!.Actors.Single(a => a.Id == 8);
        Assert.Contains(native.Actions, a => a.Kind == "Warp" && a.Destination == 500 && !a.Touch);
        Assert.Equal(portal, Assert.Single(router.Next(334, state, [404], [portal, exit], fieldOnly: true)));
        Assert.Empty(router.Next(334, state, [404], [exit], fieldOnly: true));
        Assert.Empty(router.Next(334, state with { Point = 188 }, [404], [portal, exit], fieldOnly: true));
        Assert.True(router.DistanceWithinFields(500, state, 404) is null or > 2);
    }

    private static NavigationTarget Target(string id, int x) => new(id, id, NavigationCategory.Exits,
        new(x, 128, 1), [new(x, 128, 1)], true, true);
}
