using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FullGameNavigationTests
{
    [Fact]
    public void LateGameActivePeopleAndPickupsDoNotRequireCameraDiscovery()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(401, Actor(8, 1280, 1280));
        var frame = source.Build(field, Map(), new(0, 0, 512, 512),
            [new(300, 1536, 1536), new(301, 1536, 1280) { IsChest = false }], new(189, false));
        var person = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:14");
        Assert.True(person.GuideAvailable);
        Assert.False(person.Discovered);
        Assert.False(person.Visible);
        Assert.Equal(2, frame.Targets.Count(t => t.Id.StartsWith("chest:")));
        Assert.All(frame.Targets.Where(t => t.Id.StartsWith("chest:")), t => Assert.True(t.GuideAvailable));
    }

    [Fact]
    public void RetiredActorsAndCollectedPickupsDisappearAfterLateGameCapture()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var actor = Actor(8, 1280, 1280);
        source.Build(Field(401, actor), Map(), new(0, 0, 512, 512), [new(300, 1536, 1536)], new(189, false));
        var after = source.Build(Field(401, actor with { DrawMode = 0 }), Map(), new(0, 0, 512, 512), [], new(189, false));
        Assert.DoesNotContain(after.Targets, t => t.Id.StartsWith("actor:") || t.Id.StartsWith("chest:"));
    }

    [Fact]
    public void LateGameOffscreenExitHasItsNativeDestinationNameAndRoutePoints()
    {
        var map = Map(); map.ExitCells[5 * 8 + 5] = 0;
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            Field(151), map, new(0, 0, 512, 512), [], new(93, false));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.Contains("Denadoro", exit.Label);
        Assert.True(exit.GuideAvailable);
        Assert.NotEmpty(exit.ApproachPoints);
        Assert.False(exit.Discovered);
    }

    [Fact]
    public void IncoherentSceneCannotExposeOffscreenGuideTargets()
    {
        var field = Field(401, Actor(8, 1280, 1280)) with { SceneIdCoherent = false };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            field, Map(), new(0, 0, 512, 512), [new(300, 1536, 1536)], new(189, false));
        Assert.Empty(frame.Targets);
    }

    [Fact]
    public void NativeTouchOnlyFloorSwitchRoutesOntoItEvenWhenActivationFlagIsSet()
    {
        // Ocean Palace412 actor9 has an empty confirm handler. Its contact
        // handler extends the bridge after both wall switches set162:40.
        var actor = Actor(9, 1280, 1280) with { VisualIndex = 100 };
        var state = new FieldStoryState(198, false)
        {
            Globals = new Dictionary<int, int> { [0x162] = 0x77, [0x15A] = 0 },
        };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            Field(412, actor), Map(), new(0, 0, 512, 512), [], state);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(target.IsStoryNote);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.Equal((5, 5), (p.X / 256, p.Y / 256)));
    }

    [Fact]
    public void SameSceneTrapDoorRemainsAUsablePassageToAnotherRoomSection()
    {
        // Giant's Claw reloads scene110 at a different arrival after this drop.
        var player = Actor(2, 384, 384, true);
        var controller = Actor(0, 0, 0) with { ClassTag = 7, DrawMode = 0 };
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 3,
            110, true, 1, 0, 1, 1, player, [controller, player]);
        var map = new FieldMapSnapshot(32, 32, new byte[1024], new byte[1024],
            Enumerable.Repeat((byte)1, 1024).ToArray(), 1, false, 32, 32,
            Enumerable.Repeat((byte)128, 1024).ToArray());
        var state = new FieldStoryState(213, false) { Locals = new Dictionary<int, int> { [9] = 1 } };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            field, map, new(0, 0, 512, 512), [], state);
        var passage = Assert.Single(frame.Targets, t => t.Id == "script-region:0:110:0:20:10:24");
        Assert.Equal(NavigationCategory.Exits, passage.Category);
        Assert.True(passage.GuideAvailable);
        Assert.NotEmpty(passage.ApproachPoints);
    }

    [Fact]
    public void ActiveTouchPortalIsTerminalEvenWithoutAStartupCoordinateRegion()
    {
        var portal = Actor(8, 1408, 1408) with { ClassTag = 7, DrawMode = 0 };
        var state = new FieldStoryState(213, false)
        {
            Extended = new Dictionary<int, int> { [0x20] = 1 },
        };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            Field(598, portal), Map(), new(0, 0, 512, 512), [], state);
        var target = Assert.Single(frame.Targets, t => t.Id == "landmark:8");
        var contact = Assert.Single(target.ApproachPoints);
        Assert.True(frame.Graph.IsTerminal(contact));
        Assert.True(frame.Graph.IsSameTerminal(contact, contact));
        Assert.False(frame.Graph.IsSameTerminal(contact, frame.Player));
        Assert.False(frame.Graph.IsTerminal(frame.Player));
    }

    internal static FieldActorSnapshot Actor(int index, int x, int y, bool party = false) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, 14, 4, 0x2020, 1, 1, party, true, true, true);
    internal static FieldNavigationSnapshot Field(int scene, params FieldActorSnapshot[] actors)
    {
        var player = Actor(0, 384, 384, true);
        return new(0x1000, 0x4000, 0x2000, 0x20000, actors.Length + 1, scene, true, 1, 0, 1, 1,
            player, new[] { player }.Concat(actors).ToArray());
    }
    internal static FieldMapSnapshot Map() => new(8, 8, new byte[64], new byte[64],
        Enumerable.Repeat((byte)1, 64).ToArray(), 1, false, 8, 8, Enumerable.Repeat((byte)128, 64).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
