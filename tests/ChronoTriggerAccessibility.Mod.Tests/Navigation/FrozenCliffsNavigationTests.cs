using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FrozenCliffsNavigationTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void AllSixCavesRemainIdentifiableWhileFacingElsewhere(int facing)
    {
        var sample = Sample(604, facing);
        Assert.Equal(6, Caves(sample.Frame).Length);
    }

    [Fact]
    public void CaveLabelsDistinguishTheirLocalOpenings()
    {
        var caves = Caves(Sample(604).Frame);
        Assert.Equal(6, caves.Length);
        Assert.Equal(6, caves.Select(t => t.Label).Distinct().Count());
        Assert.All(caves, t => Assert.Contains("cave", t.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheFinalCaveInteractionIsOrdinaryNorthMovement()
    {
        var caves = Caves(Sample(604).Frame);
        Assert.Equal(6, caves.Length);
        Assert.All(caves, t =>
        {
            Assert.Equal(NavigationDirection.North, t.ContactDirection);
            Assert.Null(t.ConfirmFacings);
            Assert.Contains("up", t.ArrivalInstruction ?? t.Instruction ?? "", StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(-1)] [InlineData(4)] [InlineData(255)]
    public void AnUnreadableOrInvalidNativeFacingCannotAuthorizeTheCaves(int facing) =>
        Assert.Empty(Caves(Sample(604, facing).Frame));

    [Theory]
    [InlineData("missing")] [InlineData("processing")] [InlineData("class")] [InlineData("script")]
    public void MissingOrForeignCaveWatchersCannotAuthorizeTheCaves(string variant)
    {
        var sample = Sample(604);
        var actors = sample.Field.Actors.Where(a => variant != "missing" || a.Index != 9).Select(a => a.Index != 9 ? a : variant switch
        {
            "processing" => a with { ScriptProcessingEnabled = false }, "class" => a with { ClassTag = 4 },
            "script" => a with { ScriptAddress = 0x1234, CurrentScriptOpcode = 0xB2 }, _ => a,
        }).ToArray();
        Assert.Empty(Caves(Build(sample.Field with { Actors = actors }, sample.Map, sample.Story, sample.Treasures)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StoryEventsUsesTheForwardCaveWhileTheLeaderFacesDown(bool automatic)
    {
        var sample = Sample(604, 1); var frame = sample.Frame;
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        Assert.False(story.IsStoryNote);
        var forward = Caves(frame).Where(t => t.Id.StartsWith("script-region:9:605:", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(forward);
        Assert.All(story.ApproachPoints, p => Assert.Contains(forward, t => t.ApproachPoints.Contains(p)));
        Assert.True(Start(frame, story.Id, automatic).Guiding);
    }

    [Fact]
    public void TheSlopeDoesNotInventTreasureChests() =>
        Assert.DoesNotContain(Sample(604).Frame.Targets, t => t.Id.StartsWith("chest:", StringComparison.Ordinal));

    internal static NavigationTarget[] Caves(NavigationFrame frame) => frame.Targets
        .Where(t => t.Id.StartsWith("script-region:9:", StringComparison.Ordinal)).ToArray();

    internal static NavigationController Select(NavigationFrame frame, string id)
    {
        var target = frame.Targets.Single(t => t.Id == id); var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        for (var i = 0; i < frame.Targets.Count && controller.TargetId != id; i++) controller.Handle(NavigationCommand.NextTarget, frame, 0);
        Assert.Equal(id, controller.TargetId); return controller;
    }
    internal static NavigationResult Start(NavigationFrame frame, string id, bool automatic) => Select(frame, id)
        .Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);

    internal static (NavigationFrame Frame, FieldNavigationSnapshot Field, FieldMapSnapshot Map, FieldStoryState Story,
        FieldTreasure[] Treasures) Sample(int scene, int facing = 0, NavigationPoint? pose = null)
    {
        using var doc = JsonDocument.Parse(typeof(FrozenCliffsNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.frozen-cliffs-initial-0384.json")!);
        var row = doc.RootElement.GetProperty("maps").EnumerateArray().Single(r => r.GetProperty("Scene").GetInt32() == scene);
        var map = row.GetProperty("Map").Deserialize<FieldMapSnapshot>()!;
        var at = pose ?? new NavigationPoint(4224, 511, 1);
        map = map with { PlayerLayer = at.Layer };
        var lead = FullGameNavigationTests.Actor(3, at.X, at.Y, true) with { Facing = Math.Max(0, facing) };
        var watcher = FullGameNavigationTests.Actor(9, 65280, 65280) with
        {
            ClassTag = 7, VisualIndex = 0, ActivationEnabled = 0, ActivationBinding = 0, LoadedFlag = 0,
            ScriptCallsEnabled = true, ScriptProcessingEnabled = true, ScriptPriority = 7,
            ScriptAddress = 0x290, CurrentScriptOpcode = 0x11,
        };
        var actors = scene == 604 ? new[] { lead, watcher } : new[] { lead };
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 17,
            scene, true, 1, 0, 0, lead.Index, lead, actors)
        { ActorCollisionRadius = 160, LastPartySlotRaw = 6, ContactActorRaw = 128, ConfirmActorRaw = 128 };
        var story = new FieldStoryState(214, true)
        {
            Globals = new Dictionary<int, int> { [0x1FB] = 0x45 },
            Locals = facing < 0 ? new Dictionary<int, int>() : new Dictionary<int, int> { [3] = facing },
            Extended = new Dictionary<int, int> { [0x0A] = 0, [0x20] = 128, [0x22] = 0 },
            Party = [2, 0, 1, 5, 133, 129, 4, 3, 6],
        };
        var treasures = row.GetProperty("Treasures").EnumerateArray().Select(t => new FieldTreasure(
            t.GetProperty("Index").GetInt32(), t.GetProperty("X").GetInt32() * 256 + 128,
            t.GetProperty("Y").GetInt32() * 256 + 128)).ToArray();
        return (Build(field, map, story, treasures), field, map, story, treasures);
    }

    internal static NavigationFrame Build(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldStoryState story,
        IReadOnlyList<FieldTreasure> treasures) => new FieldNavigationSource(new Empty(), _ => { }).Build(field, map,
        new(0, 0, map.Width * 256, map.Height * 256), treasures, story,
        (x, y) => treasures.Any(t => t.FineX / 256 == x && t.FineY / 256 == y));
    private sealed class Empty : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
