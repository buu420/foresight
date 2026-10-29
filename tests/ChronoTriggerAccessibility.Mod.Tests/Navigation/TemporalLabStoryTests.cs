using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
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
        var sample = TemporalLabPassageTests.Sample("capture-2-1872");
        var frame = TemporalLabPassageTests.Build(sample.Field, sample.Map, State(1));
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        Assert.NotEmpty(target.ApproachPoints);
        Assert.Contains("conveyor room", target.Instruction, StringComparison.OrdinalIgnoreCase);
        Assert.True(TemporalLabPassageTests.Start(frame, target.Id, automatic).Result.Guiding);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AfterReleaseBothModesUseTheSideRoomInsteadOfAnInactiveConsole(bool automatic)
    {
        var sample = TemporalLabPassageTests.Sample("capture-2-1872");
        var frame = TemporalLabPassageTests.Build(sample.Field, TemporalLabPassageTests.Released(sample.Map), State(0x1A));
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

    internal static FieldStoryState State(int flags)
    {
        var story = TemporalLabPassageTests.Sample("capture-2-1872").Story;
        return story with { Extended = new Dictionary<int, int>(story.Extended) { [0x2A] = flags },
            Locals = new Dictionary<int, int>(story.Locals) { [30] = 0, [121] = flags == 0x1A ? 1 : 0 } };
    }
}
