using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class EnemyStoryBindingTests
{
    [Theory]
    [InlineData(0, true, 0, true)]
    [InlineData(1, true, 0, false)]
    [InlineData(0, false, 0, false)]
    [InlineData(0, true, 1, false)]
    public void FleaUsesTheVisibleConfirmActorAndActiveWaitingController(int signal, bool running, int ending, bool available)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.flea-173-map.json")!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
        var actor = FullGameNavigationTests.Actor(11, 7 * 256 + 128, 21 * 256 + 255)
            with { ClassTag = 5, VisualIndex = 240 };
        // Hidden (draw mode 0), so the per-frame 17A4D0 -> 17A6C0 pass clears its +0x20 byte;
        // the confirm scan 17D230 then skips it, and Confirm reaches the visible actor 11.
        var controller = FullGameNavigationTests.Actor(12, actor.FineX, actor.FineY)
            with { ClassTag = 5, VisualIndex = 159, DrawMode = 0, ActivationBinding = 0, ScriptProcessingEnabled = running };
        var player = FullGameNavigationTests.Actor(1, 7 * 256 + 128, 27 * 256 + 128, true) with { ClassTag = 0 };
        var state = new FieldStoryState(0x88, false)
        {
            Globals = new Dictionary<int, int> { [0xA3] = 0x76, [0x57] = 0, [0xDF] = ending },
            Locals = new Dictionary<int, int> { [0xA] = signal },
        };
        var field = FullGameNavigationTests.Field(173) with { LeadPlayer = player, Actors = [player, actor, controller] };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 256, 256), [], state);
        Assert.Equal(available, frame.Targets.Any(t => t.Id == "actor-encounter:11"));
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor-encounter:12");
        if (!available) return;
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:keep-flea");
        Assert.False(story.IsStoryNote);
        Assert.NotEmpty(story.ApproachPoints);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, story.ApproachPoints));
        var encounter = Assert.Single(frame.Targets, t => t.Id == "actor-encounter:11");
        Assert.Contains("Confirm", encounter.ArrivalInstruction);
    }

    [Fact]
    public void BlackTyrannoStoryUsesTheNativeEncounterApproach()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.tyranno-301-map.json")!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
        // Atel: actors15..20 form the approach row (5..10,10). Their
        // identical Confirm/Touch entry starts the scene and then D8.
        var markers = Enumerable.Range(15, 6).Select(i => FullGameNavigationTests.Actor(
            i, (i - 10) * 256 + 128, 10 * 256 + 255) with { ClassTag = 7, DrawMode = 0, LoadedFlag = 0 }).ToArray();
        var player = FullGameNavigationTests.Actor(1, 7 * 256 + 128, 30 * 256 + 128, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(301) with { LeadPlayer = player, Actors = [player, .. markers] };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map,
            new(0, 0, 256, 256), [], new FieldStoryState(152, false));
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:black-tyranno");
        Assert.False(story.IsStoryNote);
        Assert.NotEmpty(story.ApproachPoints);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, story.ApproachPoints));
    }

    [Theory]
    [InlineData(169, 11, 157, 136, 0x72, "story:full:keep-slash")]
    [InlineData(174, 10, 167, 136, 0x7E, "story:full:keep-ozzie")]
    [InlineData(397, 9, 166, 180, 0, "story:full:woe-summit")]
    public void MovingCreatureToEnemiesPreservesItsStoryDestination(
        int scene, int index, int visual, int point, int keepFlags, string storyId)
    {
        var actor = FullGameNavigationTests.Actor(index, 5 * 256 + 128, 5 * 256 + 255)
            with { ClassTag = 5, VisualIndex = visual };
        var state = new FieldStoryState(point, false)
        { Globals = new Dictionary<int, int> { [0xA3] = keepFlags } };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            FullGameNavigationTests.Field(scene, actor), FullGameNavigationTests.Map(), new(0, 0, 256, 256), [], state);
        Assert.Contains(frame.Targets, t => t.Id == $"actor-encounter:{index}");
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.People && t.Id.StartsWith($"actor:{index}:"));
        var story = Assert.Single(frame.Targets, t => t.Id == storyId);
        Assert.False(story.IsStoryNote);
        Assert.NotEmpty(story.ApproachPoints);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, story.ApproachPoints));
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
