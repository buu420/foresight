using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VortexPassageTests
{
    private const string PalaceExit = "script-region:0:597:0:121:255:121";

    [Theory]
    [InlineData("2161", false)] [InlineData("2161", true)]
    [InlineData("2178", false)] [InlineData("2178", true)]
    [InlineData("2207", false)] [InlineData("2207", true)]
    [InlineData("2218", false)] [InlineData("2218", true)]
    [InlineData("2239", false)] [InlineData("2239", true)]
    public void TheCapturedPalaceRoomHasItsRealLowerPassageAndStoryRoute(string key, bool automatic)
    {
        var sample = Sample(key); var frame = sample.Frame;
        var passage = Assert.Single(frame.Targets, t => t.Id == PalaceExit);
        Assert.NotEmpty(passage.ApproachPoints);
        Assert.All(passage.ApproachPoints, p => Assert.Equal(121, p.Y / 256));
        Assert.True(Start(frame, passage.Id, automatic).Guiding);
        var story = frame.Targets.Single(t => t.Id == "story:full:lavos");
        Assert.False(story.IsStoryNote);
        Assert.True(Start(frame, story.Id, automatic).Guiding);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheThreeCanyonMarkersAreOneUsablePassage(bool automatic)
    {
        var sample = Sample("1128");
        var passages = sample.Frame.Targets.Where(t => t.Category == NavigationCategory.Exits && !t.IsStoryNote).ToArray();
        var passage = Assert.Single(passages);
        Assert.NotEmpty(passage.ApproachPoints);
        Assert.True(Start(sample.Frame, passage.Id, automatic).Guiding);
        Assert.DoesNotContain(sample.Frame.Targets, t => t.Id == "landmark:29");
    }

    [Theory]
    [InlineData("1128")] [InlineData("2161")]
    public void LocalPassageLabelsDoNotRevealTheNextRandomizedRoom(string key)
    {
        var exits = Sample(key).Frame.Targets.Where(t => t.Category == NavigationCategory.Exits && !t.IsStoryNote).ToArray();
        Assert.NotEmpty(exits);
        Assert.All(exits, t => Assert.StartsWith("Vortex passage", t.Label));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheConveyorEntranceStoryRouteIncludesItsMandatoryBattleContinuation(bool automatic)
    {
        var frame = Sample("637").Frame;
        var story = frame.Targets.Single(t => t.Id == "story:full:lavos");
        Assert.False(story.IsStoryNote);
        var result = Start(frame, story.Id, automatic);
        Assert.True(result.Guiding, string.Join(" | ", result.Speech));
        var state = Sample("637").Story;
        Assert.NotNull(new SceneConnectionRouter().DistanceWithinFields(659, state, 601));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheConveyorExitDoorIsAnExplicitNativeInteraction(bool automatic)
    {
        var frame = Sample("660").Frame;
        var door = frame.Targets.Single(t => t.Id == "landmark:1");
        Assert.Contains("door", door.Label, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(door.ConfirmFacings);
        Assert.True(Start(frame, door.Id, automatic).Guiding);
        var story = frame.Targets.Single(t => t.Id == "story:full:lavos");
        Assert.False(story.IsStoryNote);
        Assert.Equal(door.ApproachPoints, story.ApproachPoints);
        Assert.True(Start(frame, story.Id, automatic).Guiding);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ConveyorDoorRouteEndsAtNativeConfirmReach(bool automatic)
    {
        var sample = Sample("660"); var frame = sample.Frame;
        var door = frame.Targets.Single(t => t.Id == "landmark:1");
        var controller = Select(frame, door.Id);
        var result = controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        Assert.True(result.Guiding);
        // Check the derived route's interaction endpoint, not conveyor motion.
        // The plain-floor walking oracle deliberately rejects moving-floor cells.
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints).Route!;
        var facings = door.ConfirmFacings!(route[^1]);
        Assert.NotEmpty(facings);
        result = controller.Update(frame with { Player = route[^1], PlayerFacing = facings[0] }, 16);
        Assert.False(result.Guiding);
        Assert.Contains(result.Speech, line => line.Contains("Press Confirm to open the conveyor exit door", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownRandomizedStateDoesNotInventAPassage()
    {
        var sample = Sample("2161");
        var extra = sample.Story.Extended.Where(p => p.Key != 0x20).ToDictionary();
        var frame = Build(sample.Field, sample.Map, sample.Story with { Extended = extra });
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Exits && !t.IsStoryNote);
    }

    [Fact]
    public void DiagnosticEvidenceRetainsTheActualUnexpandedNativePlanes()
    {
        var sample = Sample("2161");
        var encoded = sample.Frame.RouteEvidence!("no-route", PalaceExit)!;
        using var doc = JsonDocument.Parse(RouteFailureEvidence.Decode(encoded));
        Assert.Equal(64, doc.RootElement.GetProperty("map").GetProperty("Height").GetInt32());
        Assert.Equal(sample.Map.CollisionShapes, doc.RootElement.GetProperty("map").GetProperty("CollisionShapes").GetBytesFromBase64());
    }

    [Theory]
    [InlineData("scene")] [InlineData("coherence")] [InlineData("width")]
    [InlineData("height")] [InlineData("exits")] [InlineData("plane")]
    public void TheRepeatedPlaneProjectionRequiresTheVerifiedRoomAndDimensions(string variant)
    {
        var sample = Sample("2161");
        var field = variant switch { "scene" => sample.Field with { SceneId = 632 },
            "coherence" => sample.Field with { SceneIdCoherent = false }, _ => sample.Field };
        var map = variant switch { "width" => sample.Map with { Width = 64 },
            "height" => sample.Map with { Height = 32 }, "exits" => sample.Map with { ExitHeight = 64 },
            "plane" => sample.Map with { CollisionShapes = sample.Map.CollisionShapes[..^1] }, _ => sample.Map };
        Assert.Same(map, Project(field, map));
    }

    [Theory]
    [InlineData("missing")] [InlineData("processing")]
    public void ThePalacePassageRequiresItsActiveNativeWatcher(string variant)
    {
        var sample = Sample("2161");
        var actors = sample.Field.Actors.Where(a => variant != "missing" || a.Index != 0)
            .Select(a => a.Index == 0 ? a with { ScriptProcessingEnabled = false } : a).ToArray();
        var frame = Build(sample.Field with { Actors = actors }, sample.Map, sample.Story);
        Assert.DoesNotContain(frame.Targets, t => t.Id == PalaceExit);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OpeningTheConveyorDoorRestoresItsCurrentNativePassage(bool automatic)
    {
        // Derived native E5 copy, not a captured open-door state.
        var sample = Sample("660");
        var door = sample.Field.Actors.Single(a => a.Index == 1);
        var action = GameNavigationCatalog.ActorInfo(660, door)!.Actions.First(a => !a.Touch && a.Copy is not null);
        var copy = action.Copy!;
        Assert.Equal(59, copy.Flags);
        byte[] Apply(byte[] source)
        {
            var result = (byte[])source.Clone();
            for (var y = copy.Top; y <= copy.Bottom; y++)
                Array.Copy(source, y * sample.Map.Width + copy.Left, result,
                    (copy.Y + y - copy.Top) * sample.Map.Width + copy.X, copy.Right - copy.Left + 1);
            return result;
        }
        var open = sample.Map with { CollisionShapes = Apply(sample.Map.CollisionShapes),
            TerrainFlags = Apply(sample.Map.TerrainFlags), CollisionLayers = Apply(sample.Map.CollisionLayers) };
        var state = sample.Story with { Locals = new Dictionary<int, int>(sample.Story.Locals) { [6] = 1 } };
        var frame = Build(sample.Field, open, state);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "landmark:1");
        Assert.Contains(frame.Targets, t => t.Id == "landmark:9");
        var story = frame.Targets.Single(t => t.Id == "story:full:lavos");
        Assert.DoesNotContain("Open", story.Label);
        Assert.False(story.IsStoryNote);
        Assert.True(Start(frame, story.Id, automatic).Guiding);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(6)]
    public void AClosedOrUnprovenConveyorDoorDoesNotExposeTheConcealedPassage(int local)
    {
        var sample = Sample("660");
        var locals = new Dictionary<int, int>(sample.Story.Locals);
        if (local < 0) locals.Remove(6); else locals[6] = local;
        var frame = Build(sample.Field, sample.Map, sample.Story with { Locals = locals });
        Assert.DoesNotContain(frame.Targets, t => t.Id == "landmark:9");
    }

    public static IEnumerable<object[]> PalaceWalkingCases => new[] { "2161", "2178", "2207", "2218", "2239" }
        .SelectMany(key => new[] { new object[] { key, false, 16 }, new object[] { key, false, 32 }, new object[] { key, true, 32 } });

    [Theory]
    [MemberData(nameof(PalaceWalkingCases))]
    public void CapturedPalacePosesReachTheNativeExitRowUsingTranscribedWalkingRules(string key, bool automatic, int speed)
    {
        var sample = Sample(key); var frame = sample.Frame;
        var projected = Project(sample.Field, sample.Map);
        var controller = Select(frame, PalaceExit);
        var result = controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        var speech = new List<string>(result.Speech);
        for (var tick = 1; tick < 6000 && frame.Player.Y / 256 != 121 && result.Guiding; tick++)
        {
            var direction = automatic ? result.Direction : result.ManualLeg?.Direction ?? NavigationDirection.None;
            var next = NativeWalkingReplay.Move(projected, frame.Player, direction, speed);
            frame = frame with { Player = next, PlayerFacing = direction };
            result = controller.Update(frame, tick * 16, !automatic && direction != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        Assert.Equal(121, frame.Player.Y / 256);
        Assert.DoesNotContain(speech, s => s.Contains("blocked", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ARequiredBattleOrOtherNativeControlLossStopsPassageGuidance(bool automatic)
    {
        var frame = Sample("2161").Frame;
        var controller = Select(frame, PalaceExit);
        Assert.True(controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1).Guiding);
        var stopped = controller.Update(frame with { CanNavigate = false }, 16);
        Assert.False(stopped.AutoWalking);
        Assert.Equal(NavigationDirection.None, stopped.Direction);
    }

    private static NavigationResult Start(NavigationFrame frame, string id, bool automatic)
    {
        var controller = Select(frame, id);
        return controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
    }

    private static NavigationController Select(NavigationFrame frame, string id)
    {
        var target = frame.Targets.Single(t => t.Id == id); var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        for (var i = 0; i < frame.Targets.Count && controller.TargetId != id; i++) controller.Handle(NavigationCommand.NextTarget, frame, 0);
        Assert.Equal(id, controller.TargetId);
        return controller;
    }

    private static FieldMapSnapshot Project(FieldNavigationSnapshot field, FieldMapSnapshot map) =>
        (FieldMapSnapshot)typeof(FieldNavigationSource).Assembly.GetType("ChronoTriggerAccessibility.Mod.Navigation.OceanPalaceTargets")!
            .GetMethod("Map")!.Invoke(null, [field, map])!;

    internal static (NavigationFrame Frame, FieldNavigationSnapshot Field, FieldMapSnapshot Map, FieldStoryState Story) Sample(string key)
    {
        using var doc = JsonDocument.Parse(typeof(VortexPassageTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.vortex-passages-0382.json")!);
        var root = doc.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("key").GetString() == key);
        var actors = root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var map = root.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        var f = root.GetProperty("field"); var s = root.GetProperty("story");
        var lead = actors.Single(a => a.Index == f.GetProperty("leader").GetInt32());
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, Math.Max(actors.Length, actors.Max(a => a.Index) + 1),
            f.GetProperty("scene").GetInt32(), f.GetProperty("sceneCoherent").GetBoolean(), f.GetProperty("control").GetInt32(),
            f.GetProperty("inputMode").GetInt32(), 0, lead.Index, lead, actors)
        {
            ActorCollisionRadius = f.GetProperty("collisionRadius").GetInt32(), LastPartySlotRaw = f.GetProperty("lastPartySlotRaw").GetInt32(),
            ContactActorRaw = f.GetProperty("contactActorRaw").GetInt32(), ConfirmActorRaw = f.GetProperty("confirmActorRaw").GetInt32(),
        };
        var story = new FieldStoryState(s.GetProperty("point").GetInt32(), true)
        {
            Globals = s.GetProperty("globals").Deserialize<Dictionary<int, int>>()!,
            Locals = s.GetProperty("locals").Deserialize<Dictionary<int, int>>()!, Extended = s.GetProperty("extended").Deserialize<Dictionary<int, int>>()!,
        };
        return (Build(field, map, story), field, map, story);
    }

    internal static NavigationFrame Build(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldStoryState story) =>
        new FieldNavigationSource(new Empty(), _ => { }).Build(field, map, new(0, 0, map.Width * 256, map.ExitHeight * 256), [], story);
    private sealed class Empty : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
