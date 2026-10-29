using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TemporalLabStoryTests
{
    [Theory]
    [InlineData(0, 617, "security", 620, 8)]
    [InlineData(1, 620, "door", 617, -1)]
    [InlineData(1, 618, "door", 617, -1)]
    [InlineData(1, 617, "rescuers", 617, -1)]
    [InlineData(2, 617, "alarm", 622, -1)]
    [InlineData(2, 621, "maze-door", 621, 23)]
    [InlineData(0x12, 622, "register", 622, 8)]
    [InlineData(0x16, 622, "release", 618, -1)]
    [InlineData(0x16, 618, "release", 618, -1)]
    [InlineData(0x1A, 617, "grotto", 665, -1)]
    [InlineData(0x1A, 618, "grotto", 665, -1)]
    [InlineData(0x1A, 622, "grotto", 665, -1)]
    public void NativeLabStagesPointAtActionsThatStillOperate(int flags, int scene, string id, int goalScene, int actor)
    {
        var story = State(flags);
        var step = Assert.Single(BonusStoryObjectives.Build(scene, story), o => o.Id.StartsWith("bonus:vortex:future:", StringComparison.Ordinal));
        Assert.Equal("bonus:vortex:future:" + id, step.Id);
        var goal = Assert.Single(step.Goals);
        Assert.Equal(goalScene, goal.Scene);
        if (actor < 0) Assert.Empty(goal.Actors); else Assert.Equal(new[] { actor }, goal.Actors);
        if (flags == 0x16) Assert.DoesNotContain("console", step.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AfterTheFirstConsoleBothModesReturnThroughTheRealConveyorRoomExit(bool automatic)
    {
        var sample = TemporalLabPassageTests.FreshPatrol(1);
        var frame = TemporalLabPassageTests.Build(sample.Field, sample.Map, sample.Story);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        Assert.NotEmpty(target.ApproachPoints);
        Assert.Contains("conveyor room", target.Instruction, StringComparison.OrdinalIgnoreCase);
        Assert.True(TemporalLabPassageTests.Start(frame, target.Id, automatic).Result.Guiding);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.Equal("landmark:13", route.IntermediateId);
        Assert.NotNull(route.IntermediateContact);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReturningFromCaptureCanReachTheFreshInnerDoor(bool automatic)
    {
        var s = TemporalLabPassageTests.FreshPatrol(0);
        var frame = TemporalLabPassageTests.Build(s.Field, s.Map, s.Story);
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(route.Route); Assert.Equal("landmark:13", route.IntermediateId);
        Assert.NotNull(route.IntermediateContact);
        Assert.True(TemporalLabPassageTests.Start(frame, target.Id, automatic).Result.Guiding);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheInnerDoorContactContinuesAcrossItsNativeLayerThreeLip(bool automatic)
    {
        var s = TemporalLabPassageTests.FreshPatrol(1);
        var before = TemporalLabPassageTests.Build(TemporalLabPassageTests.At(s.Field, 14464, 5343), s.Map, s.Story);
        var (controller, first) = TemporalLabPassageTests.Start(before, "story:full:lavos", automatic);
        Assert.True(first.Guiding);
        // Original175E90 produces layer3 here while moving south toward the door.
        var next = TemporalLabPassageTests.Build(TemporalLabPassageTests.At(s.Field, 14464, 5391),
            s.Map with { PlayerLayer = 3 }, s.Story);
        var result = controller.Update(next, 32, !automatic);
        Assert.True(result.Guiding, string.Join(" | ", result.Speech));
        Assert.DoesNotContain(result.Speech, x => x.StartsWith("No route", StringComparison.Ordinal));
        if (automatic) Assert.Equal(NavigationDirection.South, result.Direction);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FreshOpenedInnerDoorAllowsTheNativeLayerToSettleAndContinue(bool automatic)
    {
        var s = TemporalLabPassageTests.FreshPatrol(0);
        var marker = s.Field.Actors.Single(a => a.Index == 13);
        var copy = GameNavigationCatalog.ActorInfo(618, marker)!.Actions.First(a => a.Touch && a.Copy is { X: 55, Y: 18 }).Copy!;
        var map = TemporalLabPassageTests.Open(s.Map, copy) with { PlayerLayer = 3 };
        var field = TemporalLabPassageTests.At(s.Field, 14464, 5423);
        field = field with { Actors = field.Actors.Select(a => a.Index == 13 ? a with
            { TileX = 255, FineX = 65408, FractionX = 128, TileY = 255, FineY = 65535, FractionY = 255 } : a).ToArray() };
        var frame = TemporalLabPassageTests.Build(field, map, s.Story with
            { Locals = new Dictionary<int, int>(s.Story.Locals) { [120] = 1 } });
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
        Assert.True(TemporalLabPassageTests.Start(frame, target.Id, automatic).Result.Guiding);
    }

    [Fact]
    public void AnOpenedWarpMazeDoorDoesNotKeepAnObsoleteConfirmPrompt()
    {
        var s = TemporalLabWarpTests.Sample(621);
        var door = s.Field.Actors.Single(a => a.Index == 23);
        var copy = GameNavigationCatalog.ActorInfo(621, door)!.Actions.First(a => a.Copy is not null).Copy!;
        var frame = TemporalLabPassageTests.Build(s.Field, TemporalLabPassageTests.Open(s.Map, copy),
            s.Story with { Locals = new Dictionary<int, int>(s.Story.Locals) { [30] = 1 } });
        Assert.DoesNotContain(frame.Targets, t => t.Id == "landmark:23");
        Assert.Contains(frame.Targets, t => t.Id == "exit:1" && t.ApproachPoints.Count != 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AfterReleaseBothModesUseTheSideRoomInsteadOfAnInactiveConsole(bool automatic)
    {
        var sample = TemporalLabPassageTests.FreshReleased();
        var frame = TemporalLabPassageTests.Build(sample.Field, sample.Map, sample.Story);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        Assert.Contains("Grotto", target.Label, StringComparison.Ordinal);
        Assert.Contains("side room", target.Instruction, StringComparison.OrdinalIgnoreCase);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal("landmark:16", route.IntermediateId);
        Assert.True(TemporalLabPassageTests.Start(frame, target.Id, automatic).Result.Guiding);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheLockedWarpDoorIsIdentifiableAndItsNativeStatusCanBeReached(bool automatic)
    {
        var sample = TemporalLabPassageTests.Sample("capture-2-1818");
        var frame = TemporalLabPassageTests.Build(sample.Field, sample.Map, sample.Story);
        var door = Assert.Single(frame.Targets, t => t.Id == "landmark:11");
        Assert.Contains("door", door.Label, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(door.ApproachPoints);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints).Route);
        Assert.True(TemporalLabPassageTests.Start(frame, door.Id, automatic).Result.Guiding);
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        Assert.Contains("closed", exit.GuidanceRestriction, StringComparison.OrdinalIgnoreCase);
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
    }

    [Fact]
    public void UnknownFlagsDoNotInventTheNextLabStage()
    {
        var story = State(0); var extra = new Dictionary<int, int>(story.Extended); extra.Remove(0x2A);
        Assert.DoesNotContain(BonusStoryObjectives.Build(618, story with { Extended = extra }),
            o => o.Id.StartsWith("bonus:vortex:future:", StringComparison.Ordinal));
    }

    [Fact]
    public void VisibleRobotsHaveDistinctAppearanceLabelsAndTheirCurrentNativeHeading()
    {
        var sample = TemporalLabPassageTests.Sample("capture-2-1872");
        var frame = TemporalLabPassageTests.Build(sample.Field, sample.Map, sample.Story);
        var headings = new[] { "south", "south", "north", "south", "north" };
        for (var slot = 8; slot <= 12; slot++)
        {
            var robot = Assert.Single(frame.Targets, t => t.Id == $"actor:{slot}:5:284");
            Assert.Equal($"Robot {(char)('A' + slot - 8)}, facing {headings[slot - 8]}", robot.Label);
        }
        // Camera is derived here, since the source snapshot omitted it. A robot
        // outside that camera keeps its identity but must not disclose a heading.
        var offscreen = TemporalLabPassageTests.Build(sample.Field, sample.Map, sample.Story, new(0, 0, 256, 256));
        Assert.DoesNotContain(offscreen.Targets, t => t.Label.Contains("facing", StringComparison.Ordinal));
    }

    [Fact]
    public void RememberedRobotDoesNotRepeatAHiddenHeadingWhenStoryCaptureIsUnavailable()
    {
        var s = TemporalLabPassageTests.Sample("capture-2-1872");
        var source = new FieldNavigationSource(new Empty(), _ => { });
        var seen = source.Build(s.Field, s.Map, new(0, 0, 16384, 16384), [], s.Story);
        Assert.Contains(seen.Targets, t => t.Label.Contains("facing", StringComparison.Ordinal));
        var hidden = source.Build(s.Field, s.Map, new(0, 0, 256, 256), [], null);
        Assert.DoesNotContain(hidden.Targets, t => t.Label.Contains("facing", StringComparison.Ordinal));
    }

    internal static FieldStoryState State(int flags)
    {
        var story = TemporalLabPassageTests.Sample("capture-2-1872").Story;
        return story with { Extended = new Dictionary<int, int>(story.Extended) { [0x2A] = flags },
            Locals = new Dictionary<int, int>(story.Locals) { [30] = 0, [121] = flags == 0x1A ? 1 : 0 } };
    }
    private sealed class Empty : IReadableMemory { public bool TryRead(nuint address, Span<byte> bytes) => false; }
}
