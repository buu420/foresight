using System.Runtime.CompilerServices;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TyrannoExteriorGuideTests
{
    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void NorthwestTerraceStoryUsesTheForwardIndoorPassage(NavigationCommand command)
    {
        // Actual 2026-10-13 01:16:23 capture, story 0x98. The top doorway is closed.
        var frame = Build("tyranno-exterior-capture-0358.json", new(3328, 512, 7552, 3712));
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:black-tyranno");
        var forward = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.NotEmpty(story.ApproachPoints);
        Assert.All(story.ApproachPoints, p => Assert.Contains(p, forward.ApproachPoints));
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, story.ApproachPoints));
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(command, frame, 1);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("No route"));
    }

    [Fact]
    public void WarpRoomStoryContinuesToTheUpperStairsInsteadOfReturningToTheLowerTerrace()
    {
        // Actual 01:16:45 capture. Native exit0 arrives in the upper-west room of302.
        var frame = Build("tyranno-warp-capture-0358.json", new(6656, 13312, 10880, 16512));
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:black-tyranno");
        var forward = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.NotEmpty(story.ApproachPoints);
        Assert.All(story.ApproachPoints, p => Assert.Contains(p, forward.ApproachPoints));
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, story.ApproachPoints));
    }

    [Fact]
    public void ClosedUnseenDoorsAreNotOfferedAsUsableNorthwestTerraceExits()
    {
        var frame = Build("tyranno-exterior-capture-0358.json", new(3328, 512, 7552, 3712));
        Assert.DoesNotContain(frame.Targets, t => t.Id is "exit:5" or "exit:8");
        Assert.All(frame.Targets.Where(t => t.Category == NavigationCategory.Exits && t.GuideAvailable),
            t => Assert.NotEmpty(t.ApproachPoints));
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void AVisibleBlockedDoorExplainsTheRestriction(NavigationCommand command)
    {
        // A synthetic wide camera checks preservation of visible information.
        var frame = Build("tyranno-exterior-capture-0358.json", new(0, 0, 16384, 16384));
        var door = Assert.Single(frame.Targets, t => t.Id == "exit:5");
        Assert.True(door.Visible);
        Assert.False(door.GuideAvailable);
        Assert.NotNull(door.GuidanceRestriction);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        for (var n = 0; controller.TargetId != door.Id; n++)
        {
            Assert.True(n < 20);
            controller.Handle(NavigationCommand.NextTarget, frame, n);
        }
        var result = controller.Handle(command, frame, 21);
        Assert.False(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Contains(door.GuidanceRestriction!, result.Speech);
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("No route"));
    }

    [Fact]
    public void StoryUsesTheNativeEncounterAliasForTheNorthernBattleMarkers()
    {
        var state = new FieldStoryState(0x98, true)
        {
            Globals = new Dictionary<int, int> { [0xED] = 0x80, [0xEE] = 0 },
        };
        var marker = GameNavigationCatalog.ForScene(304)!.Actors.Single(a => a.Id == 9);
        Assert.Contains(marker.Actions, a => a.Kind == "Encounter" && a.Available(state));
        // FieldNavigationSource publishes battle-only markers under this native
        // encounter identity, rather than an ordinary scenery interaction.
        var point = new NavigationPoint(7 * 256 + 128, 7 * 256 + 255, 1);
        var target = new NavigationTarget("actor-encounter:9", "Encounter", NavigationCategory.Enemies,
            point, [point], true, true) { GuideAvailable = true };
        var story = Assert.Single(new FullStoryTargets().Build(304, state, [target], new(7 * 256, 25 * 256, 1)),
            t => t.Id == "story:full:black-tyranno");
        Assert.False(story.IsStoryNote);
        Assert.Contains(point, story.ApproachPoints);
    }

    private static NavigationFrame Build(string fixture, FieldViewport viewport, [CallerFilePath] string file = "")
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, fixture)));
        var data = doc.RootElement; var f = data.GetProperty("field"); var s = data.GetProperty("story");
        var actors = JsonSerializer.Deserialize<FieldActorSnapshot[]>(data.GetProperty("actors"))!;
        var leader = f.GetProperty("leader").GetInt32();
        var field = new FieldNavigationSnapshot(0x10000, 0x20000, 0x30000, 0x40000, actors.Length,
            f.GetProperty("scene").GetInt32(), true, 1, 0, 0, leader, actors.Single(a => a.Index == leader),
            actors, actors.Where(a => a.IsPartyMember).Select(a => a.Index).ToArray())
        {
            LastPartySlotRaw = f.GetProperty("lastPartySlotRaw").GetInt32(),
            ActorCollisionRadius = f.GetProperty("collisionRadius").GetInt32(),
            ContactActorRaw = f.GetProperty("contactActorRaw").GetInt32(),
            ConfirmActorRaw = f.GetProperty("confirmActorRaw").GetInt32(),
        };
        var story = new FieldStoryState(s.GetProperty("point").GetInt32(), true)
        {
            Globals = JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("globals"))!,
            Locals = JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("locals"))!,
            Extended = JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("extended"))!,
        };
        var treasures = data.GetProperty("treasures").EnumerateArray().Select(t => new FieldTreasure(
            t.GetProperty("index").GetInt32(), t.GetProperty("x").GetInt32(), t.GetProperty("y").GetInt32())
            { IsChest = t.GetProperty("chest").GetBoolean() }).ToArray();
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field,
            JsonSerializer.Deserialize<FieldMapSnapshot>(data.GetProperty("map"))!, viewport, treasures, story);
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
