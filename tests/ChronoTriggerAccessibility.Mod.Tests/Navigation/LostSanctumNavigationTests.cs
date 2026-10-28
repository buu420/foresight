using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class LostSanctumNavigationTests
{
    [Theory]
    [InlineData(22562, "actor:8:4:110")]
    [InlineData(22600, "actor:8:4:110")]
    [InlineData(22722, "actor:8:4:110")]
    [InlineData(28523, "actor:9:4:76")]
    [InlineData(28556, "actor:9:4:76")]
    public void CapturedNativeContactsAndBridgePassagesCanBeReachedInBothModes(int line, string id)
    {
        var sample = Live(line);
        foreach (var automatic in new[] { false, true })
        {
            var result = VortexRoomGeometryTests.Start(sample.Frame, id, automatic);
            Assert.True(result.Guiding, string.Join(" ", result.Speech));
            Assert.Equal(automatic, result.AutoWalking);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheLaterNuPositionIsReachableAfterTheBuilderHasMoved(bool automatic)
    {
        var sample = Live(28600);
        // Derived replay: later logged facts33828 (not a second full capture).
        // Same unfinished bridge floor, Nu at5654,2559 and builder at6784,2047.
        var actors = sample.Field.Actors.Select(a => a.Index switch
        {
            1 => Move(a, 6592, 2559),
            8 => Move(a, 5654, 2559),
            9 => Move(a, 6784, 2047),
            _ => a,
        }).ToArray();
        var field = sample.Field with { Actors = actors, LeadPlayer = actors.Single(a => a.Index == 1) };
        var story = sample.Story with { Extended = new Dictionary<int, int>(sample.Story.Extended) { [0x1B] = 0xB8 } };
        var frame = new FieldNavigationSource(new Empty(), _ => { }).Build(field, sample.Map,
            new(0, 0, sample.Map.Width * 256, sample.Map.Height * 256), [], story);
        var result = VortexRoomGeometryTests.Start(frame, "actor:8:5:289", automatic);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.Equal(automatic, result.AutoWalking);
    }

    [Fact]
    public void AnUnfinishedBridgeDoesNotPromiseAPathAcrossTheMissingSpan()
    {
        var sample = Live(28572);
        var target = Assert.Single(sample.Frame.Targets, t => t.Id == "exit:1");
        Assert.Contains("unfinished", target.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not complete", target.GuidanceRestriction!);
        foreach (var automatic in new[] { false, true })
            Assert.False(VortexRoomGeometryTests.Start(sample.Frame, target.Id, automatic).Guiding);
    }

    [Fact]
    public void IntroducedBridgeCharactersKeepTheirIdentitiesInPeople()
    {
        var frame = Live(28600).Frame;
        var nu = Assert.Single(frame.Targets, t => t.Id == "actor:8:5:289");
        var builder = Assert.Single(frame.Targets, t => t.Id == "actor:9:4:76");
        Assert.Equal(NavigationCategory.People, nu.Category);
        Assert.Equal("Nu Master", nu.Label);
        Assert.Equal(NavigationCategory.People, builder.Category);
        Assert.Equal("Reptite bridge builder", builder.Label);
    }

    [Theory]
    [InlineData(12655, 7551)] [InlineData(12655, 7935)] [InlineData(2941, 1018)]
    public void CavePassageCanBeReachedFromTheLoggedWindingPassagePoses(int x, int y)
    {
        // Reconstructed initial terrain and logged actors, with explicitly
        // synthetic uncaptured state; this is not an additional live evidence blob.
        var sample = Live(31620);
        var lead = Move(sample.Field.LeadPlayer!, x, y);
        var field = sample.Field with { LeadPlayer = lead, Actors = sample.Field.Actors.Select(a => a.Index == lead.Index ? lead : a).ToArray() };
        var frame = new FieldNavigationSource(new Empty(), _ => { }).Build(field, sample.Map,
            new(0, 0, sample.Map.Width * 256, sample.Map.Height * 256), [], sample.Story);
        const string id = "script-region:0:580:0:2:4:9";
        Assert.Contains(frame.Targets, t => t.Id == id && t.Category == NavigationCategory.Exits);
        foreach (var automatic in new[] { false, true })
            Assert.True(VortexRoomGeometryTests.Start(frame, id, automatic).Guiding);
    }

    [Theory]
    [InlineData(2, 9)] [InlineData(3, 10)] [InlineData(4, 11)]
    public void FortressDoorsStageThroughTheirNativeOpeningContacts(int exit, int marker)
    {
        var sample = Live(39808);
        var target = Assert.Single(sample.Frame.Targets, t => t.Id == $"exit:{exit}");
        var stage = Assert.IsAssignableFrom<IStagedNavigationGraph>(sample.Frame.Graph)
            .FindStage(sample.Frame.Player, target.ApproachPoints, NavigationPathfinder.DefaultMaximumVisited);
        Assert.NotNull(stage.Route);
        Assert.Equal($"landmark:{marker}", stage.IntermediateId);
        Assert.NotNull(stage.IntermediateContact);
        foreach (var automatic in new[] { false, true })
            Assert.True(VortexRoomGeometryTests.Start(sample.Frame, target.Id, automatic).Guiding);
    }

    [Theory]
    [InlineData("disabled")] [InlineData("missing")] [InlineData("unknown-local")]
    public void ClosedDoorPreviewRequiresItsLiveNativeHandler(string variant)
    {
        var sample = Live(39808);
        var actors = sample.Field.Actors.Where(a => variant != "missing" || a.Index != 10)
            .Select(a => a.Index == 10 && variant == "disabled" ? a with { ScriptCallsEnabled = false } : a).ToArray();
        var locals = new Dictionary<int, int>(sample.Story.Locals);
        if (variant == "unknown-local") locals.Remove(14);
        var frame = new FieldNavigationSource(new Empty(), _ => { }).Build(sample.Field with { Actors = actors }, sample.Map,
            new(0, 0, sample.Map.Width * 256, sample.Map.Height * 256), [], sample.Story with { Locals = locals });
        foreach (var automatic in new[] { false, true })
            Assert.False(VortexRoomGeometryTests.Start(frame, "exit:3", automatic).Guiding);
    }

    [Fact]
    public void TheBuilderStillPhysicallyBlocksTheNuBeforeHeMoves()
    {
        var frame = Live(28600).Frame;
        foreach (var automatic in new[] { false, true })
            Assert.False(VortexRoomGeometryTests.Start(frame, "actor:8:5:289", automatic).Guiding);
    }

    [Theory]
    [InlineData(0, 1, 0x208u)] [InlineData(1, 2, 0x200u)]
    [InlineData(1, 1, 0x200u)] [InlineData(2, 0, 0x208u)]
    public void WindingPassageCornerUsesASelectableShortNativeStep(int mode, int toggle, uint expected)
    {
        var sample = Live(18917);
        Assert.True(sample.Frame.CompensateMovingFloor);
        Assert.NotNull(sample.Frame.CanTraverseFloorStep);
        var steering = new NavigationResult([], NavigationDirection.West, true, true)
            { SteeringPoint = new(9088, 6012, 1) };
        var pad = FieldFloorMovement.Adjust(sample.Frame, steering, new(0x1000, 0x4000, 581, 0, 0, 0, mode, toggle));
        Assert.Equal(expected, pad.Pad);
        Assert.Null(pad.StopReason);
    }

    private static FieldActorSnapshot Move(FieldActorSnapshot actor, int x, int y) => actor with
    { FineX = x, TileX = x / 256, FractionX = x % 256, FineY = y, TileY = y / 256, FractionY = y % 256 };

    internal static (NavigationFrame Frame, FieldNavigationSnapshot Field, FieldMapSnapshot Map, FieldStoryState Story) Live(int line)
    {
        using var doc = JsonDocument.Parse(typeof(LostSanctumNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.lost-sanctum-live-0380.json")!);
        var root = doc.RootElement.GetProperty("captures").EnumerateArray()
            .Single(e => e.GetProperty("provenance").GetProperty("line").GetInt32() == line);
        var map = root.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        var actors = root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var f = root.GetProperty("field"); var s = root.GetProperty("story");
        var lead = actors.Single(a => a.Index == f.GetProperty("leader").GetInt32());
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, actors.Length,
            f.GetProperty("scene").GetInt32(), true, 1, 0, 0, lead.Index, lead, actors)
        {
            ActorCollisionRadius = f.GetProperty("collisionRadius").GetInt32(),
            LastPartySlotRaw = f.GetProperty("lastPartySlotRaw").GetInt32(),
            ContactActorRaw = f.GetProperty("contactActorRaw").GetInt32(),
            ConfirmActorRaw = f.GetProperty("confirmActorRaw").GetInt32(),
        };
        var story = new FieldStoryState(s.GetProperty("point").GetInt32(), true)
        {
            Globals = s.GetProperty("globals").Deserialize<Dictionary<int, int>>()!,
            Locals = s.GetProperty("locals").Deserialize<Dictionary<int, int>>()!,
            Extended = s.GetProperty("extended").Deserialize<Dictionary<int, int>>()!,
            Inventory = new Dictionary<int, int>(), Party = [0, 5, 6, 130, 4, 131, 2, 3, 1],
        };
        var treasures = root.GetProperty("treasures").EnumerateArray().Select(t =>
            new FieldTreasure(t.GetProperty("index").GetInt32(), t.GetProperty("x").GetInt32(), t.GetProperty("y").GetInt32())
                { IsChest = t.GetProperty("chest").GetBoolean() }).ToArray();
        var frame = new FieldNavigationSource(new Empty(), _ => { }).Build(field, map,
            new(0, 0, map.Width * 256, map.Height * 256), treasures, story);
        return (frame, field, map, story);
    }

    private sealed class Empty : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> bytes) => false;
    }
}
