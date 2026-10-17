using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class OceanPalaceCaptureTests
{
    [Fact]
    public void DaltonUsesTheNativeConversationBoundaryInsteadOfHisUnreachableThrone()
    {
        var frame = OceanPalaceFixtures.Frame("031602-7245");
        var dalton = Assert.Single(frame.Targets, t => t.Id == "actor:10:6:161");
        Assert.Equal("Dalton", dalton.Label);
        Assert.Equal(NavigationCategory.People, dalton.Category);
        Assert.NotEmpty(dalton.ApproachPoints);
        Assert.Null(dalton.ConfirmFacings);
        Assert.All(dalton.ApproachPoints, p => Assert.True(p.Y / 256 <= 12));
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, dalton.ApproachPoints).Route);
    }

    [Fact]
    public void RevealedMasaKeepsHisNativeConfirmInteractionOnTheLowerStair()
    {
        var capture = OceanPalaceFixtures.Read("053729-1215");
        // Drawn and included in the native on-camera interaction scan. Merely
        // flipping the hidden frame's draw byte leaves its culling gate off.
        var actors = capture.Field.Actors.Select(a => a.Index == 33 ? a with {
            DrawMode = 1, ActivationBinding = 128,
        } : a).ToArray();
        var frame = OceanPalaceFixtures.Frame(capture with { Field = capture.Field with { Actors = actors } });
        var masa = Assert.Single(frame.Targets, t => t.Id == "actor:33:5:151");
        Assert.NotEmpty(masa.ApproachPoints);
        Assert.NotNull(masa.ConfirmFacings);
        Assert.Contains(masa.ApproachPoints, p => masa.ConfirmFacings(p).Count != 0);
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:palace-lower-west");
        Assert.Equal(masa.ApproachPoints, story.ApproachPoints);
        Assert.Contains("Masa", story.Label);
    }

    [Fact]
    public void CapturedGreatStairUsesTheNativeRepeatedCollisionRows()
    {
        var capture = OceanPalaceFixtures.Read("053729-1215");
        Assert.Equal(64, capture.Map.Height);
        Assert.Equal(128, capture.Map.ExitHeight);
        var original = capture.Map.CollisionShapes.ToArray();
        var frame = OceanPalaceFixtures.Frame(capture);
        var from = new NavigationPoint(5504, 30592, 1);
        // Original x86 175E90, with the unexpanded captured 32x64 planes,
        // moves this pose (0,32) and keeps layer1. Coordinates are not wrapped.
        Assert.Contains(new NavigationPoint(5504, 30656, 1), frame.Graph.Neighbours(from));
        Assert.Empty(frame.Graph.Neighbours(new(5504, 128 * 256 + 128, 1)));
        Assert.Equal(original, capture.Map.CollisionShapes);
    }

    [Theory]
    [InlineData(408, true)]
    [InlineData(409, false)]
    public void RepeatingCollisionRowsRequiresTheVerifiedGreatStairIdentity(int scene, bool coherent)
    {
        var capture = OceanPalaceFixtures.Read("053729-1215");
        var frame = OceanPalaceFixtures.Frame(capture with { Field = capture.Field with {
            SceneId = scene, SceneIdCoherent = coherent,
        } });
        Assert.Empty(frame.Graph.Neighbours(new(5504, 30592, 1)));
    }

    [Fact]
    public void HiddenMasaKeepsStoryGuidanceOnTheNativeStairway()
    {
        var frame = OceanPalaceFixtures.Frame("053729-1215");
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor:33:5:151");
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:palace-lower-west");
        var onward = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        Assert.False(story.IsStoryNote);
        Assert.DoesNotContain("Masa", story.Label);
        Assert.Equal(onward.ApproachPoints, story.ApproachPoints);
        Assert.NotEmpty(story.ApproachPoints);
        var route = NavigationPathfinder.Search(frame.Graph.WithoutCreatures ?? frame.Graph,
            frame.Player, story.ApproachPoints);
        Assert.NotNull(route.Route);
        foreach (var command in new[] { NavigationCommand.Guide, NavigationCommand.ToggleWalk })
        {
            var controller = new NavigationController();
            var selected = frame with { Targets = [story] };
            for (var i = 0; i < (int)NavigationCategory.StoryEvents; i++)
                controller.Handle(NavigationCommand.NextCategory, selected, 0);
            var result = controller.Handle(command, selected, 1);
            Assert.True(result.Guiding, string.Join(" | ", result.Speech));
            Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        }
    }

    [Theory]
    [InlineData("031602-9253", "actor:10:4:167")]
    [InlineData("031602-9253", "actor:16:4:167")]
    [InlineData("031602-9253", "landmark:22")]
    [InlineData("031602-9253", "landmark:26")]
    [InlineData("031602-9253", "exit:0")]
    [InlineData("031602-9253", "exit:3")]
    [InlineData("062759-1580", "exit:8")]
    [InlineData("062759-1932", "exit:9")]
    public void AContactInOneSmallRoomCannotMakeOtherRoomsGuidable(string capture, string targetId)
    {
        var frame = OceanPalaceFixtures.Frame(capture);
        var otherRoom = frame.Targets.FirstOrDefault(t => t.Id == targetId);
        if (otherRoom is null) return;
        Assert.False(otherRoom.GuideAvailable);
        Assert.NotNull(otherRoom.GuidanceRestriction);
    }
}

internal static class OceanPalaceFixtures
{
    internal sealed record Capture(FieldNavigationSnapshot Field, FieldMapSnapshot Map, FieldStoryState Story);

    internal static Capture Read(string id)
    {
        using var stream = typeof(OceanPalaceFixtures).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.ocean-palace-captures-0367.json")!;
        using var json = JsonDocument.Parse(stream);
        var raw = json.RootElement.EnumerateArray().Single(e => e.GetProperty("Id").GetString() == id).GetProperty("Capture");
        var s = raw.GetProperty("story");
        var state = new FieldStoryState(s.GetProperty("point").GetInt32(), true) {
            Globals = s.GetProperty("globals").Deserialize<Dictionary<int, int>>()!,
            Locals = s.GetProperty("locals").Deserialize<Dictionary<int, int>>()!,
            Extended = s.GetProperty("extended").Deserialize<Dictionary<int, int>>()!,
        };
        var actors = raw.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var f = raw.GetProperty("field");
        var leader = actors.Single(a => a.Index == f.GetProperty("leader").GetInt32());
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, actors.Length,
            f.GetProperty("scene").GetInt32(), f.GetProperty("sceneCoherent").GetBoolean(),
            f.GetProperty("control").GetInt32(), f.GetProperty("inputMode").GetInt32(), 1, 1,
            leader, actors, actors.Where(a => a.IsPartyMember).Select(a => a.Index).ToArray()) {
            ActorCollisionRadius = f.GetProperty("collisionRadius").GetInt32(),
            LastPartySlotRaw = f.GetProperty("lastPartySlotRaw").GetInt32(),
            ContactActorRaw = f.GetProperty("contactActorRaw").GetInt32(),
            ConfirmActorRaw = f.GetProperty("confirmActorRaw").GetInt32(),
        };
        return new(field, raw.GetProperty("map").Deserialize<FieldMapSnapshot>()!, state);
    }

    internal static NavigationFrame Frame(string id) => Frame(Read(id));
    internal static NavigationFrame Frame(Capture c) => new FieldNavigationSource(new NoMemory(), _ => { }).Build(
        c.Field, c.Map, new(0, 0, c.Map.Width * 256, c.Map.Height * 256), [], c.Story);

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
