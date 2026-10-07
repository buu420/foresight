using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FactoryAppearanceTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void DrawnConveyorRobotsHaveTheirVisibleAppearanceName(int index)
    {
        var robot = Actor(index, 170);
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var frame = source.Build(Field(robot), Map(), new(0, 0, 4096, 4096), [], State());
        var target = Assert.Single(frame.Targets, t => t.Id == $"actor:{index}:4:170");
        Assert.Equal("Conveyor robot", target.Label);
        Assert.DoesNotContain(source.Build(Field(robot with { DrawMode = 0 }), Map(),
            new(0, 0, 4096, 4096), [], State()).Targets, t => t.Label == "Conveyor robot");
        Assert.Null(FutureAreaLabels.ActorLabel(230, robot, State()));
        Assert.Null(FutureAreaLabels.ActorLabel(231, robot with { VisualIndex = 169 }, State()));
    }

    [Fact]
    public void WarehouseObjectiveDistinguishesTheTwoBeltsAndPointsToTheWesternPassage()
    {
        var goals = FutureStoryTargets.Build(231, State(), [], default);
        var note = Assert.Single(goals, t => t.IsStoryNote);
        Assert.Contains("upper conveyor", note.Instruction);
        Assert.Contains("lower conveyor", note.Instruction);
        Assert.Contains("south", note.Instruction);
        Assert.Contains("west entrance", note.Instruction);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void AConveyorRobotRemainsReadableWithoutBecomingAWalkDestination(NavigationCommand command)
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var frame = source.Build(Field(Actor(12, 170)), Map(), new(0, 0, 4096, 4096), [], State());
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var selected = controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.Contains(selected.Speech, s => s.Contains("Conveyor robot") && s.Contains("here"));
        Assert.Contains(selected.Speech, s => s.Contains("moving hazard", StringComparison.OrdinalIgnoreCase));
        var result = controller.Handle(command, frame, 16);
        Assert.False(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
        Assert.Contains(result.Speech, s => s.Contains("Choose another destination"));
    }

    [Fact]
    public void UnseenRobotsDoNotGainLivePositionsThroughGuideEligibility()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var robot = Actor(12, 170);
        Assert.DoesNotContain(source.Build(Field(robot), Map(), new(0, 0, 512, 512), [], State()).Targets,
            t => t.Id == "actor:12:4:170");
        var seen = Assert.Single(source.Build(Field(robot), Map(), new(0, 0, 4096, 4096), [], State()).Targets,
            t => t.Id == "actor:12:4:170");
        var remembered = Assert.Single(source.Build(Field(robot with { FineX = 2000 }), Map(),
            new(0, 0, 512, 512), [], State()).Targets, t => t.Id == "actor:12:4:170");
        Assert.False(remembered.Visible);
        Assert.False(remembered.GuideAvailable);
        Assert.Equal(seen.Position, remembered.Position);
    }

    private static FieldStoryState State() => new(60, false) { Globals = new Dictionary<int, int> { [0x58] = 4 } };
    private static FieldActorSnapshot Actor(int index, int visual) => new(index, 5, 1408, 128,
        5, 1408, 128, 1, 1, 1, visual, 4, 0x2020, 1, 1, false, true, true, true);
    private static FieldNavigationSnapshot Field(FieldActorSnapshot robot)
    {
        var player = Actor(1, 0) with { ClassTag = 0, IsPartyMember = true };
        return new(0x1000, 0x4000, 0x2000, 0x20000, 16, 231, true, 1, 0, 0, 1, player, [player, robot]);
    }
    private static FieldMapSnapshot Map() => new(64, 48, new byte[3072], new byte[3072],
        Enumerable.Repeat((byte)1, 3072).ToArray(), 1, false, 64, 48,
        Enumerable.Repeat((byte)128, 3072).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
