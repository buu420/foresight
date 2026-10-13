using System.Runtime.CompilerServices;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TyrannoRegionGuideTests
{
    [Fact]
    public void ActualEntryCaptureOmitsUnseenExitsAndChestsFromOtherFloorSections()
    {
        var frame = Build(new(12288, 768, 16512, 3968));
        var exits = frame.Targets.Where(t => t.Category == NavigationCategory.Exits).ToArray();
        Assert.Single(exits);
        Assert.Equal("exit:8", exits[0].Id);
        Assert.DoesNotContain(frame.Targets, t => t.Id is "chest:181" or "landmark:19");
        var visibleChest = Assert.Single(frame.Targets, t => t.Id == "chest:182");
        Assert.True(visibleChest.Visible);
        Assert.False(visibleChest.GuideAvailable);
        Assert.NotNull(visibleChest.GuidanceRestriction);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, exits[0].ApproachPoints));
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void VisibleDestinationInAnotherSectionIsIdentifiableAndExplainsRestriction(NavigationCommand command)
    {
        // A deliberately wide synthetic viewport verifies retaining visible information.
        var frame = Build(new(0, 0, 16384, 16384));
        var distant = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.True(distant.Visible);
        Assert.NotNull(distant.GuidanceRestriction);
        Assert.Contains("another part of this map", distant.Instruction!);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        NavigationResult result;
        for (var n = 0; ; n++)
        {
            result = controller.Handle(NavigationCommand.Repeat, frame, n);
            if (controller.TargetId == distant.Id) break;
            Assert.True(n < 16); controller.Handle(NavigationCommand.NextTarget, frame, n);
        }
        Assert.Contains(result.Speech, s => s.Contains("another part of this map"));
        result = controller.Handle(command, frame, 20);
        Assert.False(result.Guiding); Assert.False(result.AutoWalking);
        Assert.Contains(distant.GuidanceRestriction!, result.Speech);
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("No route"));
    }

    [Fact]
    public void StoryBindingUsesOnlyTheCurrentNativeEncounterStrip()
    {
        var frame = Build(new(12288, 768, 16512, 3968));
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:tyranno-lair");
        Assert.NotEmpty(story.ApproachPoints);
        Assert.All(story.ApproachPoints, p => Assert.Equal(55, p.X / 256));
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, story.ApproachPoints));
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void ProductionSourceBindsTheHiddenCellApproachAfterTheBattle(NavigationCommand command)
    {
        var frame = Build(new(12288, 2816, 16384, 6400), afterBattle: true);
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:free-kino");
        Assert.False(story.IsStoryNote);
        Assert.Null(story.ConfirmFacings);
        Assert.All(story.ApproachPoints, p => Assert.Equal(55, p.X / 256));
        Assert.DoesNotContain(frame.Targets, t => t.Label.Contains("Kino", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(frame.Targets, t => t.Id == TyrannoLairTargets.RevealId);
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(command, frame, 1);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.DoesNotContain(result.Speech, s => s.Contains("No route"));
    }

    private static NavigationFrame Build(FieldViewport viewport, bool afterBattle = false, [CallerFilePath] string file = "")
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "tyranno-prison-capture-0357.json")));
        var data = doc.RootElement; var f = data.GetProperty("field"); var s = data.GetProperty("story");
        var actors = JsonSerializer.Deserialize<FieldActorSnapshot[]>(data.GetProperty("actors"))!;
        if (afterBattle)
        {
            // Actors and story point are from the post-battle log. The map is
            // the earlier live capture; script proof establishes no floor copy
            // between those states. Local 0A remains the pre-reveal value.
            using var later = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "tyranno-302-0357.json")));
            actors = JsonSerializer.Deserialize<FieldActorSnapshot[]>(later.RootElement.GetProperty("AfterBattleActors"))!;
        }
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
        var story = new FieldStoryState(afterBattle ? 0x96 : s.GetProperty("point").GetInt32(), true)
        {
            Globals = new Dictionary<int, int>(JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("globals"))!)
                { [0] = afterBattle ? 0x96 : s.GetProperty("point").GetInt32() },
            Locals = JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("locals"))!,
            Extended = JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("extended"))!,
        };
        var treasures = data.GetProperty("treasures").EnumerateArray().Select(t =>
            new FieldTreasure(t.GetProperty("index").GetInt32(), t.GetProperty("x").GetInt32(), t.GetProperty("y").GetInt32())
                { IsChest = t.GetProperty("chest").GetBoolean() }).ToArray();
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field,
            JsonSerializer.Deserialize<FieldMapSnapshot>(data.GetProperty("map"))!, viewport, treasures, story);
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
