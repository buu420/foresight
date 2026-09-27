using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class StoryArrivalRegionTests
{
    [Fact]
    public void OceanPalaceStorySwitchRetainsTheObjectsNativeContact()
    {
        var actor = FullGameNavigationTests.Actor(10, 5 * 256 + 128, 5 * 256 + 255)
            with { ClassTag = 4, VisualIndex = 167 };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            FullGameNavigationTests.Field(406, actor), FullGameNavigationTests.Map(), new(0, 0, 256, 256), [],
            new FieldStoryState(195, false) { Globals = new Dictionary<int, int> { [0x162] = 0 } });
        var objectTarget = Assert.Single(frame.Targets, t => t.Id == "actor:10:4:167");
        Assert.NotNull(objectTarget.ContactPosition);
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:palace-east-switch");
        Assert.Equal(objectTarget.ContactPosition, story.ContactPosition);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StoryAlternativesFinishOnlyTheContactAtTheChosenRouteGoal(bool secondNeedsContact)
    {
        // Visibility puts the unreachable first candidate ahead of a usable
        // alternative. Finishing must follow the goal the pathfinder chose.
        var first = new NavigationTarget("first", "First", NavigationCategory.Objects,
            new(384, 256, 1), [new(384, 256, 1)], true, true) { ContactPosition = new(256, 256, 1) };
        var second = first with { Id = "second", Position = new(896, 256, 1),
            ApproachPoints = [new(896, 256, 1)], Visible = false,
            ContactPosition = secondNeedsContact ? new(1024, 256, 1) : null };
        var story = StoryTarget.BindAny("alternative", "Alternative", [first, second], [first.Id, second.Id], second.Position);
        var frame = new NavigationFrame("alternatives", true, second.Position, [story], new NoEdges(), 256);
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, i);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 4);
        Assert.Equal(secondNeedsContact, result.AutoWalking);
        Assert.Equal(secondNeedsContact ? NavigationDirection.East : NavigationDirection.None, result.Direction);
    }

    private sealed class NoEdges : INavigationGraph
    { public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => []; }

    [Fact]
    public void MammonMachineApproachBindsBeforeTheLavosScene()
    {
        var state = new FieldStoryState(0xC9, false) { Locals = new Dictionary<int, int> { [8] = 2 } };
        var frame = Build(415, "mammon-415-map.json", state, 11);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:ocean-palace-lavos");
        Assert.False(target.IsStoryNote);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.InRange(p.Y / 256, 0, 22));
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void MammonApproachRetainsItsNativePhaseGate(int phase)
    {
        var state = new FieldStoryState(0xC9, false) { Locals = new Dictionary<int, int> { [8] = phase } };
        var frame = Build(415, "mammon-415-map.json", state, 11);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:ocean-palace-lavos");
        Assert.True(target.IsStoryNote);
    }

    [Fact]
    public void DeathPeakRevivalRoutesToTheSummitTrigger()
    {
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        globals[0x70] = 4; globals[0x7C] = 6; globals[0x64] = 0x38; globals[0x58] = 0x80;
        var state = new FieldStoryState(0xD5, false) { Globals = globals };
        var frame = Build(265, "death-peak-265-map.json", state);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:quest:revival:summit");
        Assert.False(target.IsStoryNote);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.InRange(p.Y / 256, 0, 16));
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
        globals[0x57] = 0x40;
        Assert.DoesNotContain(Build(265, "death-peak-265-map.json", state).Targets,
            t => t.Id == "story:full:quest:revival:summit");
    }

    private static NavigationFrame Build(int scene, string resource, FieldStoryState state, int controllerIndex = 0)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation." + resource)!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
        var player = FullGameNavigationTests.Actor(1, 7 * 256 + 128, (scene == 415 ? 45 : 30) * 256 + 128, true) with { ClassTag = 0 };
        var controller = FullGameNavigationTests.Actor(controllerIndex, 0, 0) with { ClassTag = 7, DrawMode = 0, LoadedFlag = 0 };
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [controller, player] };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 256, 256), [], state);
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
