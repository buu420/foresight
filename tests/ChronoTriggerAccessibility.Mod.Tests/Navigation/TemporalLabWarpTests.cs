using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TemporalLabWarpTests
{
    [Fact]
    public void TheFifteenNativeWarpPadsHaveDistinctLabelsAndTouchContacts()
    {
        var frame = Sample(621).Frame;
        var pads = frame.Targets.Where(t => t.ActorSlot is >= 8 and <= 22).ToArray();
        Assert.Equal(15, pads.Length);
        Assert.Equal(15, pads.Select(t => t.Label).Distinct().Count());
        Assert.All(pads, t =>
        {
            Assert.StartsWith("Warp pad ", t.Label);
            Assert.Equal(NavigationCategory.Objects, t.Category);
            Assert.NotEmpty(t.ApproachPoints);
            Assert.NotNull(t.ContactPosition);
            Assert.Null(t.ConfirmFacings);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheEastSecurityChamberRouteStartsWithTheReachableNativeWarp(bool automatic)
    {
        var frame = Sample(621).Frame;
        // The closed door has a native Confirm handler. Progression must reach
        // that interaction first, rather than promise a walk through closed tiles.
        var target = Assert.Single(frame.Targets, t => t.Id == "landmark:23");
        Assert.NotEmpty(target.ApproachPoints);
        Assert.NotNull(target.ConfirmFacings);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal("actor:8:4:231", route.IntermediateId);
        var start = Start(frame, target.Id, automatic);
        Assert.True(start.Guiding, string.Join(" | ", start.Speech));
        Assert.Equal(automatic, start.AutoWalking);
        Assert.Contains(start.Speech, s => s.Contains("Warp pad A", StringComparison.Ordinal));
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        Assert.False(story.IsStoryNote);
        var guidedStory = Start(frame, story.Id, automatic);
        Assert.True(guidedStory.Guiding, string.Join(" | ", guidedStory.Speech));
        Assert.Contains(guidedStory.Speech, s => s.Contains("Warp pad A", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing")] [InlineData("hidden")] [InlineData("calls")] [InlineData("wrong-visual")]
    public void UnavailableFirstPadsCannotInventTheForwardWarpConnection(string variant)
    {
        var sample = Sample(621);
        var actors = sample.Field.Actors.Where(a => variant != "missing" || a.Index != 8)
            .Select(a => a.Index != 8 ? a : variant switch
            {
                "hidden" => a with { DrawMode = 0 }, "calls" => a with { ScriptCallsEnabled = false },
                "wrong-visual" => a with { VisualIndex = 232 }, _ => a,
            }).ToArray();
        var frame = Build(sample.Field with { Actors = actors }, sample.Map, sample.Story);
        var goals = sample.Frame.Targets.Single(t => t.Id == "landmark:23").ApproachPoints;
        Assert.NotEmpty(goals);
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, goals).Route);
        Assert.DoesNotContain(frame.Targets, t => t.ActorSlot == 8 && t.Label.StartsWith("Warp pad ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheCapturedConveyorRoomExitUsesItsNativeDoorContact(bool automatic)
    {
        var frame = Sample(617).Frame;
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        Assert.NotEmpty(target.ApproachPoints);
        var path = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(path.Route);
        Assert.Equal("landmark:11", path.IntermediateId);
        Assert.NotNull(path.IntermediateContact);
        Assert.True(Start(frame, target.Id, automatic).Guiding);
    }

    public static IEnumerable<object[]> LandingPoses => new[]
    {
        (1152, 7007), (4224, 6143), (5632, 4479), (6784, 4351), (5632, 2175),
        (14592, 4223), (12160, 1791), (11008, 5247), (9600, 7423), (8192, 7295), (2048, 7551),
    }.SelectMany(p => new[] { new object[] { p.Item1, p.Item2, false, 16 },
        new object[] { p.Item1, p.Item2, false, 32 }, new object[] { p.Item1, p.Item2, true, 32 } });

    [Theory]
    [MemberData(nameof(LandingPoses))]
    public void EachLandingHasARealFirstWalkingLegToItsNextNativePad(int x, int y, bool automatic, int speed)
    {
        // Derived poses, not new captures. E1 entry flags give these exact
        // fine coordinates; the independent mover executes no actor scripts.
        var sample = Sample(621);
        var lead = Move(sample.Field.LeadPlayer!, x, y);
        var field = sample.Field with { LeadPlayer = lead, Actors = sample.Field.Actors.Select(a => a.Index == lead.Index ? lead : a).ToArray() };
        var frame = Build(field, sample.Map, sample.Story);
        var target = frame.Targets.Single(t => t.Id == "landmark:23");
        var path = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(path.Route);
        if (path.IntermediateId is null)
        {
            var door = field.Actors.Single(a => a.Index == 23);
            Assert.NotEmpty(FieldInteractionRange.FacingsWithin(path.Route[^1].X, path.Route[^1].Y,
                door.FineX, door.FineY, door.CollisionOffsetX, 32));
            return;
        }
        var pad = frame.Targets.Single(t => t.Id == path.IntermediateId);
        Assert.StartsWith("Warp pad ", pad.Label);
        var selected = new NavigationController();
        for (var i = 0; i < 5 && selected.CurrentCategoryLabel != "Interactable Objects"; i++) selected.Handle(NavigationCommand.NextCategory, frame, 0);
        for (var i = 0; i < 40 && selected.TargetId != target.Id; i++) selected.Handle(NavigationCommand.NextTarget, frame, 0);
        Assert.Equal(target.Id, selected.TargetId);
        var result = selected.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        var speech = new List<string>(result.Speech);
        // Model the native nonzero camera contact cache for the selected pad.
        // This is a derived native-collision test, not a live +20 observation.
        var contacts = new FieldActorCollisionRules(field with { Actors = field.Actors.Select(a => a.Index == pad.ActorSlot ? a with { ActivationBinding = 128 } : a).ToArray() });
        var touched = false;
        for (var tick = 1; tick < 1500 && result.Guiding; tick++)
        {
            var direction = automatic ? result.Direction : result.ManualLeg?.Direction ?? ContactInstruction(speech.LastOrDefault());
            var (dx, dy) = direction switch
            {
                NavigationDirection.North => (0, -speed), NavigationDirection.South => (0, speed),
                NavigationDirection.West => (-speed, 0), NavigationDirection.East => (speed, 0), _ => (0, 0),
            };
            if ((dx != 0 || dy != 0) && contacts.ContactActor(frame.Player.X, frame.Player.Y, frame.Player.X + dx, frame.Player.Y + dy) == pad.ActorSlot)
            { touched = true; break; }
            frame = frame with { Player = NativeWalkingReplay.Move(sample.Map, frame.Player, direction, speed), PlayerFacing = direction };
            result = selected.Update(frame, tick * 16, !automatic && direction != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        Assert.True(touched, $"{frame.Player}: {string.Join(" | ", speech.TakeLast(6))}");
        Assert.Contains(speech, s => s.Contains(pad.Label, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing")] [InlineData("hidden")] [InlineData("calls")] [InlineData("moved")]
    public void RevokingTheNextNativePadStopsAnActiveWalk(string variant)
    {
        var sample = Sample(621);
        var lead = Move(sample.Field.LeadPlayer!, 4224, 6143);
        var field = sample.Field with { LeadPlayer = lead, Actors = sample.Field.Actors.Select(a => a.Index == lead.Index ? lead : a).ToArray() };
        var initial = Build(field, sample.Map, sample.Story);
        var target = initial.Targets.Single(t => t.Id == "landmark:23");
        var stage = NavigationPathfinder.Search(initial.Graph, initial.Player, target.ApproachPoints);
        var pad = initial.Targets.Single(t => t.Id == stage.IntermediateId);
        // Several pads remain in this section. Losing the selected one must
        // revoke its leg even though other warp connections still exist.
        var controller = new NavigationController();
        for (var i = 0; i < 5 && controller.CurrentCategoryLabel != "Interactable Objects"; i++) controller.Handle(NavigationCommand.NextCategory, initial, 0);
        for (var i = 0; i < 40 && controller.TargetId != target.Id; i++) controller.Handle(NavigationCommand.NextTarget, initial, 0);
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, initial, 1).AutoWalking);
        var actors = field.Actors.Where(a => variant != "missing" || a.Index != pad.ActorSlot).Select(a => a.Index != pad.ActorSlot ? a : variant switch
        {
            "hidden" => a with { DrawMode = 0 }, "calls" => a with { ScriptCallsEnabled = false },
            "moved" => Move(a, a.FineX + 256, a.FineY), _ => a,
        }).ToArray();
        var next = Build(field with { Actors = actors }, sample.Map, sample.Story);
        var result = controller.Update(next, 16);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
        Assert.Contains(result.Speech, s => s.Contains("passage", StringComparison.Ordinal) && s.Contains("available", StringComparison.Ordinal));
    }

    private static NavigationDirection ContactInstruction(string? line) => line is null ? NavigationDirection.None :
        line.Contains("Continue up onto", StringComparison.Ordinal) ? NavigationDirection.North :
        line.Contains("Continue down onto", StringComparison.Ordinal) ? NavigationDirection.South :
        line.Contains("Continue left onto", StringComparison.Ordinal) ? NavigationDirection.West :
        line.Contains("Continue right onto", StringComparison.Ordinal) ? NavigationDirection.East : NavigationDirection.None;

    [Theory]
    [InlineData("calls")] [InlineData("unknown-local")] [InlineData("locked")]
    public void AClosedEntranceCannotBeOpenedByMissingOrUnsatisfiedNativeAuthority(string variant)
    {
        var sample = Sample(617);
        var field = sample.Field with { Actors = sample.Field.Actors.Select(a => variant == "calls" && a.Index == 11 ? a with { ScriptCallsEnabled = false } : a).ToArray() };
        var locals = new Dictionary<int, int>(sample.Story.Locals);
        var extra = new Dictionary<int, int>(sample.Story.Extended);
        if (variant == "unknown-local") locals.Remove(100);
        if (variant == "locked") extra[0x2A] = 0;
        var frame = Build(field, sample.Map, sample.Story with { Locals = locals, Extended = extra });
        var openGoals = sample.Frame.Targets.Single(t => t.Id == "exit:1").ApproachPoints;
        Assert.NotEmpty(openGoals);
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, openGoals).Route);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OpeningTheSecurityDoorRestoresTheActualExitObjective(bool automatic)
    {
        var sample = Sample(621);
        var marker = sample.Field.Actors.Single(a => a.Index == 23);
        var action = GameNavigationCatalog.ActorInfo(621, marker)!.Actions.First(a => !a.Touch && a.Kind == "Terrain" && a.Copy is not null);
        var open = ApplyNativeCopy(sample.Map, action.Copy!);
        var lead = Move(sample.Field.LeadPlayer!, 11008, 5247);
        var field = sample.Field with { LeadPlayer = lead, Actors = sample.Field.Actors.Select(a => a.Index == lead.Index ? lead : a).ToArray() };
        var state = sample.Story with { Locals = new Dictionary<int, int>(sample.Story.Locals) { [30] = 1 } };
        var frame = Build(field, open, state);
        var exit = frame.Targets.Single(t => t.Id == "exit:1");
        Assert.Null(exit.GuidanceRestriction);
        Assert.NotEmpty(exit.ApproachPoints);
        Assert.True(Start(frame, exit.Id, automatic).Guiding);
        var story = frame.Targets.Single(t => t.Id == "story:full:lavos");
        Assert.DoesNotContain("Open", story.Label);
        Assert.False(story.IsStoryNote);
        Assert.True(Start(frame, story.Id, automatic).Guiding);
    }

    [Fact]
    public void TheWarpLookAheadHonorsASmallSearchBudget()
    {
        var frame = Sample(621).Frame;
        var target = frame.Targets.Single(t => t.Id == "landmark:23");
        var limited = ((IStagedNavigationGraph)frame.Graph).FindStage(frame.Player, target.ApproachPoints, 1);
        Assert.Null(limited.Route);
        Assert.True(limited.LimitReached);
    }

    private static FieldMapSnapshot ApplyNativeCopy(FieldMapSnapshot original, GameNavigationCatalog.TileCopy copy)
    {
        // Independent replay of this installed E5 FB copy, not a new capture.
        Assert.Equal(251, copy.Flags);
        var result = original with { CollisionShapes = original.CollisionShapes.ToArray(),
            CollisionLayers = original.CollisionLayers.ToArray(), TerrainFlags = original.TerrainFlags.ToArray() };
        foreach (var (from, to) in new[] { (original.CollisionShapes, result.CollisionShapes),
            (original.CollisionLayers, result.CollisionLayers), (original.TerrainFlags, result.TerrainFlags) })
            for (var y = copy.Top; y <= copy.Bottom; y++)
                Array.Copy(from, y * original.Width + copy.Left, to, (copy.Y + y - copy.Top) * original.Width + copy.X, copy.Right - copy.Left + 1);
        return result;
    }
    private static NavigationResult Start(NavigationFrame frame, string id, bool automatic)
    {
        // Retain the other live rows: a staged warp revalidates its actual pad,
        // in addition to the selected final destination, on every update.
        var target = frame.Targets.Single(t => t.Id == id);
        var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        for (var i = 0; i < frame.Targets.Count && controller.TargetId != id; i++) controller.Handle(NavigationCommand.NextTarget, frame, 0);
        Assert.Equal(id, controller.TargetId);
        return controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
    }
    private static FieldActorSnapshot Move(FieldActorSnapshot actor, int x, int y) => actor with
    { FineX = x, TileX = x / 256, FractionX = x % 256, FineY = y, TileY = y / 256, FractionY = y % 256 };

    internal static (NavigationFrame Frame, FieldNavigationSnapshot Field, FieldMapSnapshot Map, FieldStoryState Story) Sample(int scene)
    {
        using var doc = JsonDocument.Parse(typeof(TemporalLabWarpTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.temporal-lab-navigation-0382.json")!);
        var root = doc.RootElement.GetProperty("captures").EnumerateArray().Single(c => c.GetProperty("field").GetProperty("scene").GetInt32() == scene);
        var actors = root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var map = root.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        var f = root.GetProperty("field"); var s = root.GetProperty("story");
        var lead = actors.Single(a => a.Index == f.GetProperty("leader").GetInt32());
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, actors.Length, scene, true, 1, 0, 0,
            lead.Index, lead, actors)
        {
            ActorCollisionRadius = f.GetProperty("collisionRadius").GetInt32(),
            LastPartySlotRaw = f.GetProperty("lastPartySlotRaw").GetInt32(),
            ContactActorRaw = f.GetProperty("contactActorRaw").GetInt32(), ConfirmActorRaw = f.GetProperty("confirmActorRaw").GetInt32(),
        };
        var story = new FieldStoryState(s.GetProperty("point").GetInt32(), true)
        {
            Globals = s.GetProperty("globals").Deserialize<Dictionary<int, int>>()!,
            Locals = s.GetProperty("locals").Deserialize<Dictionary<int, int>>()!,
            Extended = s.GetProperty("extended").Deserialize<Dictionary<int, int>>()!,
            Inventory = new Dictionary<int, int>(), Party = [2, 3, 4, 128, 132, 130, 131, 6, 128],
        };
        return (Build(field, map, story), field, map, story);
    }

    internal static NavigationFrame Build(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldStoryState? story) =>
        new FieldNavigationSource(new Empty(), _ => { }).Build(field, map, new(0, 0, map.Width * 256, map.Height * 256), [], story);
    private sealed class Empty : IReadableMemory { public bool TryRead(nuint address, Span<byte> value) => false; }
}
