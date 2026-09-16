using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VehicleNavigationSourceTests
{
    private const int Step = WorldNavigationGraph.Step;

    [Fact]
    public void ParkedVehiclesAreObjectsAtTheirExactNativePositionWithTheBoardingInstruction()
    {
        var world = Snapshot([Entrance(6, 25, 19, 6, 12, 400, 304)], world: 3) with
        {
            Vehicles = new(true, 520, 600, true, true, 720, 480, 0, 1, false, false),
        };
        var frame = Source().Build(world, Label);
        var epoch = Assert.Single(frame.Targets, t => t.Id == VehicleStoryRouting.EpochId);
        var dactyls = Assert.Single(frame.Targets, t => t.Id == VehicleStoryRouting.DactylId);
        Assert.Equal(NavigationCategory.Objects, epoch.Category);
        Assert.Equal("Epoch", epoch.Label);
        Assert.Equal("Dactyls", dactyls.Label);
        Assert.Equal(new NavigationPoint(520 * 16, 600 * 16, 1), epoch.Position);
        Assert.Equal([new NavigationPoint(520 * 16, 600 * 16, 1)], epoch.ApproachPoints);
        Assert.Equal("Press Confirm to board.", epoch.Instruction);
        Assert.Equal("Press Confirm to board.", epoch.ArrivalInstruction);
        Assert.True(epoch.GuideAvailable);
        Assert.True(dactyls.GuideAvailable);
        Assert.Equal(new NavigationPoint(720 * 16, 480 * 16, 1), dactyls.Position);
    }

    [Fact]
    public void AVehicleOnAnotherLandmassOrAnotherEraIsListedButNotRoutable()
    {
        var (map, properties) = VehicleFlightGraphTests.Terrain(water: 0x11, cliff: 0x33);
        // The player at x=352 (tile 22) is east of the water band (tiles 10..19); the Epoch at x=64 is west of it.
        var world = Snapshot([], map, properties, player: (352, 304)) with { Vehicles = new(true, 64, 304, true, false, 0, 0, 0, 1, false, false) };
        var epoch = Assert.Single(Source().Build(world, Label).Targets, t => t.Id == VehicleStoryRouting.EpochId);
        Assert.Empty(epoch.ApproachPoints);
        Assert.False(epoch.GuideAvailable);
        var elsewhere = Snapshot([]) with { Vehicles = new(false, 64, 304, true, false, 0, 0, 0, 1, false, false) };
        Assert.DoesNotContain(Source().Build(elsewhere, Label).Targets, t => t.Id.StartsWith("vehicle:"));
        var boarding = Snapshot([]) with { Vehicles = new(true, 400, 304, true, false, 0, 0, 4, 1, false, false) };
        Assert.DoesNotContain(Source().Build(boarding, Label).Targets, t => t.Id.StartsWith("vehicle:"));
    }

    [Fact]
    public void FlightFrameOffersEntrancesThroughLegalLandingSitesAndSkipsUnlandableOnes()
    {
        var (map, properties) = VehicleFlightGraphTests.Terrain(water: 0x11, cliff: 0x33);
        properties[6] = 0x44; properties[7] = 0x00; // tile 3: entrance ground on its upper chip row
        map[19 * 96 + 28] = 3;   // Truce Inn door at tile (28,19), enclosed by the cliff band 25..31: chips 56..57 rows 38..39
        map[20 * 96 + 45] = 3;   // Leene Square door at tile (45,20): chips 90..91 rows 40..41 on open ground
        var inn = Entrance(6, 28, 19, 6, 12, 448, 304);
        var square = Entrance(9, 45, 20, 10, 5, 720, 320);
        var world = Snapshot([inn, square], map, properties) with
        {
            Vehicle = new(1, 2, 3, 4, 0, VehicleKind.Epoch, 0xC70, 3, 600, 200, 0xE0),
            Vehicles = new(true, 600, 200, true, false, 0, 0, 2, 1, false, false),
        };
        var source = Source();
        var frame = source.BuildFlight(world, Label);
        Assert.Equal("world:00000001:0:epoch", frame.Scene);
        Assert.Equal(new NavigationPoint(600 * 16, 200 * 16, 1), frame.Player);
        Assert.IsType<EpochFlightGraph>(frame.Graph);
        Assert.Equal(NavigationUnits.WorldStep, frame.UnitsPerTile);
        Assert.Contains("flying the Epoch", frame.AreaName);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Equal("Leene Square", target.Label);
        Assert.True(target.GuideAvailable);
        Assert.Contains("land", target.ArrivalInstruction!, StringComparison.OrdinalIgnoreCase);
        var walking = new WorldNavigationGraph(map, properties);
        Assert.All(target.ApproachPoints, p =>
        {
            Assert.True(VehicleLandingRules.CanLand(VehicleKind.Epoch, 0, map, properties, p));
            Assert.True(walking.CanStand(p));
        });
        Assert.DoesNotContain(target.ApproachPoints, p => p.X / Step is >= 90 and <= 91 && p.Y / Step is 40 or 41);
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(route);
        Assert.Contains(route[^1], target.ApproachPoints);
    }

    [Fact]
    public void DactylFlightUsesTheDactylGraphAndNeverLandsOnTheEpoch()
    {
        var map = new byte[6144]; var properties = new byte[512];
        var door = Entrance(2, 30, 20, 8, 272, 480, 320);
        var world = Snapshot([door], map, properties, world: 3) with
        {
            Vehicle = new(1, 2, 3, 4, 3, VehicleKind.Dactyl, 0xD70, 4, 200, 200, 0xC0),
            Vehicles = new(true, 480, 336, true, true, 200, 200, 3, 1, false, false)
            { EpochShape = new(8, 8, 8, 8), DactylShape = new(8, 8, 8, 8) },
        };
        var frame = Source().BuildFlight(world, _ => "Mystic Mountains");
        Assert.IsType<DactylFlightGraph>(frame.Graph);
        Assert.Contains("riding the Dactyls", frame.AreaName);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.True(
            Math.Abs(p.X / 16 - 480) >= 16 || Math.Abs(p.Y / 16 - 336) >= 16));
    }

    [Fact]
    public void FlightFrameRequiresTheVehicleMotion()
    {
        Assert.Throws<ArgumentException>(() => Source().BuildFlight(Snapshot([]), Label));
    }

    [Fact]
    public void DactylLandingSearchContinuesUntilAFlightReachableSiteIsFound()
    {
        var map = new byte[6144]; var properties = new byte[512];
        properties[2] = properties[3] = 0x33;
        properties[4] = 0; properties[5] = 0x33;
        for (var y = 0; y < 64; y++) map[y * 96 + 20] = 1;
        // A single clear chip row permits walking through the mountain barrier,
        // while the Dactyl's four-chip test never permits flight across it.
        map[20 * 96 + 20] = 2;
        var world = Snapshot([Entrance(2, 25, 20, 6, 272, 400, 320)], map, properties, world: 3) with
        {
            Vehicle = new(1, 2, 3, 4, 3, VehicleKind.Dactyl, 0xD70, 3, 240, 320, 0xC0),
            Vehicles = new(false, 0, 0, false, true, 240, 320, 3, 1, false, false),
        };
        var frame = Source().BuildFlight(world, Label);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.All(target.ApproachPoints, p => Assert.True(p.X < 320 * 16));
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
    }

    [Fact]
    public void BoardingFromADifferentWalkingPhaseUsesTheNativeSpriteOverlap()
    {
        var shape = new VehicleContactShape(8, 8, 8, 8);
        var world = Snapshot([], player: (603, 205)) with
        {
            WalkingEndpoint = new(603, 205),
            Vehicles = new(true, 520, 304, true, false, 0, 0, 0, 1, false, false)
            { PartyBoardingShape = shape, EpochBoardingShape = shape },
        };
        var frame = Source().Build(world, Label);
        var epoch = Assert.Single(frame.Targets);
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, epoch.ApproachPoints);
        Assert.NotNull(route);
        Assert.All(epoch.ApproachPoints, p =>
        {
            Assert.Equal(3 * 16, p.X % Step); Assert.Equal(5 * 16, p.Y % Step);
            Assert.True(Math.Abs(p.X / 16 - 520) < 16 && Math.Abs(p.Y / 16 - 304) < 16);
        });
        Assert.Equal(new NavigationPoint(520 * 16, 304 * 16, 1), epoch.Position);
    }

    [Fact]
    public void FlightLandingUsesTheNativeSpriteExtentsAtTheirStrictBoundary()
    {
        var world = Snapshot([Entrance(2, 25, 19, 6, 272, 400, 304)], world: 3) with
        {
            Vehicle = new(1, 2, 3, 4, 3, VehicleKind.Dactyl, 0xD70, 3, 200, 200, 0xC0),
            Vehicles = new(true, 416, 304, true, true, 200, 200, 3, 1, false, false)
            { EpochShape = new(8, 8, 8, 8), DactylShape = new(8, 8, 8, 8) },
        };
        var target = Assert.Single(Source().BuildFlight(world, Label).Targets, t => t.Category == NavigationCategory.Exits);
        // 264530/264690 use strict overlap. At dx16 two eight-pixel extents only
        // touch; the old invented32-pixel clearance incorrectly rejected this site.
        Assert.Contains(new NavigationPoint(400 * 16, 304 * 16, 1), target.ApproachPoints);
        Assert.DoesNotContain(new NavigationPoint(408 * 16, 304 * 16, 1), target.ApproachPoints);
    }

    [Fact]
    public void BlackOmenIsAnEpochContactDestinationEvenWhenNoGroundIsLandable()
    {
        var map = new byte[6144]; var properties = new byte[512];
        properties[0] = properties[1] = 0x11; // ocean everywhere
        var world = Snapshot([], map, properties) with
        {
            Vehicle = new(1, 2, 3, 4, 0, VehicleKind.Epoch, 0xC70, 3, 603, 205, 0xE0),
            Vehicles = new(true, 603, 205, true, false, 0, 0, 2, 1, false, false)
            { EpochShape = new(8, 8, 8, 8), BlackOmen = new(640, 400, 0xD30, new(8, 8, 8, 8), false) },
        };
        var frame = Source().BuildFlight(world, id => id == 76 ? "Black Omen" : Label(id));
        var omen = Assert.Single(frame.Targets, t => t.Label == "Black Omen");
        Assert.Equal(NavigationCategory.Exits, omen.Category);
        Assert.Contains("Confirm", omen.ArrivalInstruction);
        Assert.DoesNotContain("land", omen.ArrivalInstruction!, StringComparison.OrdinalIgnoreCase);
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, omen.ApproachPoints);
        Assert.NotNull(route);
        Assert.All(omen.ApproachPoints, p =>
        {
            Assert.Equal(3 * 16, p.X % Step); Assert.Equal(5 * 16, p.Y % Step);
            // Native contact also checks the eight neighbouring8px positions.
            Assert.InRange(Math.Abs(p.X / 16 - 640), 0, 23);
            Assert.InRange(Math.Abs(p.Y / 16 - 400), 0, 23);
        });
        Assert.DoesNotContain(Source().BuildFlight(world with { Vehicles = world.Vehicles with { BlackOmen = null } }, Label).Targets,
            t => t.Label == "Black Omen");
        Assert.DoesNotContain(Source().BuildFlight(world with { Vehicle = world.Vehicle with { Kind = VehicleKind.Dactyl } }, Label).Targets,
            t => t.Label == "Black Omen");
    }

    [Fact]
    public void StoryPoint211RoutesToTheParkedEpochForTheNativeTakeoffScene()
    {
        var world = Snapshot([], world: 6) with
        {
            StoryPoint = 211, Story = new FieldStoryState(211, false),
            Vehicles = new(true, 520, 304, true, false, 0, 0, 0, 1, false, false),
        };
        var story = Assert.Single(Source().Build(world, Label).Targets,
            t => t.Id == "story:full:black-omen-rises");
        Assert.False(story.IsStoryNote);
        Assert.Equal(new NavigationPoint(520 * 16, 304 * 16, 1), story.Position);
        Assert.Contains("board", story.ArrivalInstruction!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("time gauge", story.Instruction!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WinglessEpochUsesTheTimeGaugeForTheInteriorMudbeastObjective()
    {
        // The goal is scene 388 deep inside antiquity, not a world entrance. The
        // current future map has a gate through which unrestricted graph search
        // could eventually reach it, but a wingless Epoch can travel there directly.
        var world = Snapshot([Entrance(1, 28, 19, 6, 208, 448, 304)], world: 2) with
        {
            StoryPoint = 174, Story = new FieldStoryState(174, false),
            Vehicles = new(true, 520, 304, false, false, 0, 0, 0, 1, false, false),
        };
        var target = Assert.Single(Source().Build(world, id => id == 110 ? "12000 B.C." : Label(id)).Targets,
            t => t.Id == "story:full:mudbeast");
        Assert.False(target.IsStoryNote);
        Assert.Equal(new NavigationPoint(520 * 16, 304 * 16, 1), target.Position);
        Assert.Contains("12000 B.C.", target.Instruction);
        Assert.Contains("time gauge", target.Instruction);
        Assert.DoesNotContain("fly", target.Instruction!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HoveringEpochKeepsItsNativeNonzeroPixelPhaseThroughTheWholeRoute()
    {
        var world = Snapshot([Entrance(9, 45, 20, 10, 5, 720, 320)]) with
        {
            Vehicle = new(1, 2, 3, 4, 0, VehicleKind.Epoch, 0xC70, 3, 603, 205, 0xE0),
            Vehicles = new(true, 603, 205, true, false, 0, 0, 2, 1, false, false),
        };
        var frame = Source().BuildFlight(world, Label);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(route);
        Assert.All(route, p => { Assert.Equal(3 * 16, p.X % Step); Assert.Equal(5 * 16, p.Y % Step); });
        Assert.All(route.Zip(route.Skip(1)), p => Assert.Equal(Step,
            Math.Abs(p.First.X - p.Second.X) + Math.Abs(p.First.Y - p.Second.Y)));
    }

    [Fact]
    public void StoryObjectiveAcrossWaterReroutesToTheReachableEpoch()
    {
        var (map, properties) = VehicleFlightGraphTests.Terrain(water: 0x11, cliff: 0x33);
        // The actual world entrance is48; Heckran's room47 lies beyond room49.
        // The player (tile22) and Epoch (tile23) are east of the water.
        var cave = Entrance(3, 4, 19, 20, 48, 64, 304);
        var world = Snapshot([cave], map, properties, player: (352, 304)) with
        {
            StoryPoint = 78, Story = new FieldStoryState(78, false),
            Vehicles = new(true, 368, 304, true, false, 0, 0, 0, 1, false, false),
        };
        var frame = Source().Build(world, id => id == 20 ? "Heckran Cave" : Label(id));
        var story = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents && t.Id == "story:full:heckran");
        Assert.False(story.IsStoryNote);
        Assert.True(story.GuideAvailable);
        Assert.Equal(new NavigationPoint(368 * 16, 304 * 16, 1), story.Position);
        Assert.Contains("Board the Epoch", story.Instruction);
        Assert.Contains("board the Epoch", story.ArrivalInstruction);
        // Without a reachable vehicle the objective stays an honest note.
        var stranded = Source().Build(world with { Vehicles = new(false, 0, 0, false, false, 0, 0, 0, 1, false, false) },
            id => id == 20 ? "Heckran Cave" : Label(id));
        Assert.True(Assert.Single(stranded.Targets, t => t.Id == "story:full:heckran").IsStoryNote);
    }

    [Theory]
    [InlineData(603, 205)]
    [InlineData(607, 205)]
    public void WalkingAfterANonAlignedLandingKeepsTheNativePhaseThroughEntranceContact(int x, int y)
    {
        var world = Snapshot([Entrance(6, 25, 19, 6, 12, 400, 304)], player: (x, y)) with
        { WalkingEndpoint = new(x == 603 ? 603 : 611, 205) };
        var frame = Source().Build(world, Label);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(route);
        Assert.All(route.Skip(x == 603 ? 0 : 1), p => { Assert.Equal(3 * 16, p.X % Step); Assert.Equal(5 * 16, p.Y % Step); });
        if (x == 607) Assert.Equal(new NavigationPoint(611 * 16, 205 * 16, 1), route[1]);
        Assert.All(target.ApproachPoints, p => { Assert.InRange(p.X / 16, 400, 415); Assert.InRange(p.Y / 16, 304, 319); });
    }

    [Fact]
    public void AnObjectiveInAnotherEraUsesTheWinglessEpochRatherThanALocalGate()
    {
        // Scene5 is a real local entrance with a Gate to600AD. It is not a local
        // flight connection to scene113, and a wingless Epoch still changes era.
        var world = Snapshot([Entrance(3, 4, 19, 10, 5, 64, 304)]) with
        {
            StoryPoint = 0x51, Story = new FieldStoryState(0x51, false),
            Vehicles = new(true, 520, 304, false, false, 0, 0, 0, 1, false, false),
        };
        var frame = Source().Build(world, id => id == 107 ? "600 A.D." : Label(id));
        var story = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents && t.Id == "story:full:return-middle-ages");
        Assert.False(story.IsStoryNote);
        Assert.Equal("The destination is in 600 A.D. Board the Epoch and use its time gauge.", story.Instruction);
    }

    [Fact]
    public void OnlyAReachableEpochCanSupplyAnEraConnection()
    {
        var routing = new VehicleStoryRouting();
        var epoch = new NavigationTarget(VehicleStoryRouting.EpochId, "Epoch", NavigationCategory.Objects,
            new(520 * 16, 304 * 16, 1), [new(520 * 16, 304 * 16, 1)], true, true);
        var story = new FieldStoryState(0x51, false);
        var note = StoryTarget.Note("story:full:return-middle-ages", "Go back to 600 AD to look for Magus", "The next connection is not currently available.");
        var player = new NavigationPoint(400 * 16, 304 * 16, 1);
        var none = new Dictionary<string, int>();
        var bound = Assert.Single(routing.Reroute([note], 0, story, [epoch], true, none, player, id => id == 107 ? "600 A.D." : null));
        Assert.False(bound.IsStoryNote);
        Assert.Equal(note.Id, bound.Id);
        Assert.Equal(note.Label, bound.Label);
        Assert.Equal(NavigationCategory.StoryEvents, bound.Category);
        Assert.Equal(epoch.Position, bound.Position);
        Assert.True(bound.GuideAvailable);
        Assert.Equal("The destination is in 600 A.D. Board the Epoch and use its time gauge.", bound.Instruction);
        Assert.Equal("Press Confirm to board the Epoch.", bound.ArrivalInstruction);
        Assert.False(Assert.Single(routing.Reroute([note], 0, story, [epoch], false, none, player, _ => null)).IsStoryNote);
        // The Dactyls never cross eras.
        var dactyls = epoch with { Id = VehicleStoryRouting.DactylId, Label = "Dactyls" };
        Assert.True(Assert.Single(routing.Reroute([note], 0, story, [dactyls], true, none, player, _ => null)).IsStoryNote);
        // A vehicle that is listed but not routable is no answer either.
        Assert.True(Assert.Single(routing.Reroute([note], 0, story, [epoch with { ApproachPoints = [] }], true, none, player, _ => null)).IsStoryNote);
    }

    private static WorldNavigationSource Source() => new(new EmptyMemory(), _ => { });

    [Fact]
    public void OptionalInteriorObjectivesUseActualEraEntriesAndKeepTheirCategory()
    {
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        globals[0x13B] = 0x20; globals[0x13C] = 0x30;
        globals[0x7C] = 4; globals[0x70] = 4; globals[0x58] = 0x80; globals[0x64] = 0x38;
        var story = new FieldStoryState(212, false)
        {
            Globals = globals,
        };
        var world = Snapshot([]) with { StoryPoint = 212, Story = story,
            Vehicles = new(true, 520, 304, true, false, 0, 0, 0, 1, false, false) };
        var frame = Source().Build(world, id => id == 108 ? "2300 A.D." : Label(id));
        foreach (var id in new[] { "geno:brain", "revival:summit" })
        {
            var objective = Assert.Single(frame.Targets, t => t.Id == "story:full:quest:" + id);
            Assert.False(objective.IsStoryNote);
            Assert.Equal(NavigationCategory.Objects, objective.Category);
            Assert.Equal("The destination is in 2300 A.D. Board the Epoch and use its time gauge.", objective.Instruction);
        }
        // Geno Dome's world entrance248 reaches the interior268 entrance to Mother Brain.
        var (map, properties) = VehicleFlightGraphTests.Terrain(0x11, 0x33);
        var future = world with { Motion = world.Motion with { World = 2, PixelX = 352 }, Map = map, Properties = properties,
            Entrances = [Entrance(4, 4, 19, 6, 248, 64, 304)],
            Vehicles = world.Vehicles! with { EpochX = 368 } };
        var local = Assert.Single(Source().Build(future, Label).Targets, t => t.Id == "story:full:quest:geno:brain");
        Assert.Contains("Board the Epoch and fly", local.Instruction);
        Assert.DoesNotContain("time gauge", local.Instruction);
    }

    [Fact]
    public void OptionalBlackOmenObjectiveBindsToTheActualVehicleAndFlightContact()
    {
        var shape = new VehicleContactShape(8, 8, 8, 8);
        var world = Snapshot([]) with
        {
            StoryPoint = 212,
            Story = new(212, false) { Globals = new Dictionary<int, int>
                { [0x1F7] = 8, [0x1A7] = 0, [0x1A8] = 0, [0x1A9] = 0, [0x71] = 0, [0x15A] = 0 } },
            Vehicles = new(true, 520, 304, true, false, 0, 0, 0, 1, false, false)
                { EpochShape = shape, BlackOmen = new(643, 389, 0xD30, shape, false) },
        };
        const string id = "story:full:quest:omen:entrance";
        var boarding = Assert.Single(Source().Build(world, Label).Targets, t => t.Id == id);
        Assert.Equal(NavigationCategory.Objects, boarding.Category);
        Assert.Contains("Board the Epoch and fly", boarding.Instruction);
        Assert.DoesNotContain(Source().Build(world with { Vehicles = world.Vehicles with { BlackOmen = null } }, Label).Targets,
            t => t.Id == id);
        var flying = world with { Vehicle = new(1, 2, 3, 4, 0, VehicleKind.Epoch, 0xC70, 3, 603, 205, 0xE0),
            Vehicles = world.Vehicles with { Transport = 2 } };
        var contact = Assert.Single(Source().BuildFlight(flying, n => n == 76 ? "Black Omen" : Label(n)).Targets, t => t.Id == id);
        Assert.Contains("boarding choice", contact.ArrivalInstruction);
        Assert.All(contact.ApproachPoints, p => Assert.True(Math.Abs(p.X / 16 - 643) <= 23 && Math.Abs(p.Y / 16 - 389) <= 23));
    }

    [Fact]
    public void ADactylOnlyStoryRouteFliesToTheActualInteriorInsteadOfReturningThroughTheWorld()
    {
        var (map, properties) = VehicleFlightGraphTests.Terrain(0x11, 0x33);
        var world = Snapshot([Entrance(1, 22, 19, 6, 272, 352, 304), Entrance(2, 4, 19, 10, 299, 64, 304)],
            map, properties, world: 3, player: (352, 304)) with
        {
            StoryPoint = 147, Story = new(147, false),
            Vehicles = new(false, 0, 0, false, true, 368, 304, 0, 1, false, false),
        };
        var target = Assert.Single(Source().Build(world, Label).Targets, t => t.Id == "story:full:tyranno-lair");
        Assert.Equal(new NavigationPoint(368 * 16, 304 * 16, 1), target.Position);
        Assert.Contains("Board the Dactyls and fly", target.Instruction);
        // Once the story needs Melchior in another era, Dactyls cannot supply
        // that connection; walking to the current era's Gate remains useful.
        var later = world with { StoryPoint = 126, Story = new(126, false)
            { Inventory = new Dictionary<int, int> { [0x5008] = 1 } } }; // the recovered Gate Key
        var gate = Assert.Single(Source().Build(later, Label).Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(gate.IsStoryNote);
        Assert.DoesNotContain("Board the Dactyls", gate.Instruction);
    }
    private static string? Label(int id) => id switch { 6 => "Truce Inn", 10 => "Leene Square", 106 => "1000 A.D.", _ => null };
    private static WorldEntrance Entrance(int id, int tx, int ty, int name, int dest, int x, int y) =>
        new(id, tx, ty, name, dest, 0, 52, 28, true)
        { ContactPoints = [new(tx * 16, ty * 16), new(tx * 16 + 8, ty * 16), new(tx * 16, ty * 16 + 8), new(tx * 16 + 8, ty * 16 + 8)] };
    private static WorldNavigationSnapshot Snapshot(IReadOnlyList<WorldEntrance> exits, byte[]? map = null, byte[]? properties = null,
        int world = 0, (int X, int Y)? player = null) =>
        new(new(1, 2, 3, 4, world, 0, player?.X ?? 400, player?.Y ?? 304), map ?? new byte[6144], properties ?? new byte[512], exits,
            new(0, 0, 1536 * 16, 1024 * 16), null) { EraMessageIndex = 106 };
    private sealed class EmptyMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
