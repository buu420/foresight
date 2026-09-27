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
