using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Tester captures 2026-10-17 06.27.59: the elevator (411) and the two
/// lower small rooms (406). After-battle states copy the logged actor facts.</summary>
public sealed class OceanPalaceProgressionTests
{
    private const string LowerWest = "story:full:palace-lower-west";
    private const string LowerEast = "story:full:palace-lower-east";
    private const string Ambush = "script-encounter:9:16400:0:19:255:255";
    private const string Control = "actor:10:4:113";

    [Fact]
    public void ElevatorStoryFollowsTheAmbushRowWhileTheControlCannotTakeConfirm()
    {
        var frame = OceanPalaceFixtures.Frame("062759-1078");
        // Atel0221 actor16 is scanned before the control at every in-reach point.
        Assert.Empty(Single(frame, Control).ApproachPoints);
        var ambush = Single(frame, Ambush);
        var story = Single(frame, LowerWest);
        Assert.False(story.IsStoryNote);
        Assert.Equal(ambush.ApproachPoints, story.ApproachPoints);
        Assert.All(story.ApproachPoints, p => Assert.True(p.Y / 256 >= 19, $"{p} is above the ambush row"));
        Assert.Null(story.ConfirmFacings);
        Assert.Contains(Route(frame, story)[^1], ambush.ApproachPoints);
        AssertGuides(frame, story);
    }

    [Fact]
    public void ElevatorStoryUsesTheControlOnceTheAmbushHasRun()
    {
        var frame = OceanPalaceFixtures.Frame(AfterAmbush());
        Assert.DoesNotContain(frame.Targets, t => t.Id == Ambush);
        var control = Single(frame, Control);
        var story = Single(frame, LowerWest);
        Assert.NotEmpty(control.ApproachPoints);
        Assert.Equal(control.ApproachPoints, story.ApproachPoints);
        var end = Route(frame, story)[^1];
        Assert.NotEmpty(story.ConfirmAt(end)!(end));
        AssertGuides(frame, story);
    }

    [Fact]
    public void AnotherStoppedMarkerDoesNotRetireTheAmbushRow()
    {
        var capture = Patch(OceanPalaceFixtures.Read("062759-1078"), null,
            (11, a => a with { ScriptCallsEnabled = false, ScriptProcessingEnabled = false }));
        Assert.Contains(OceanPalaceFixtures.Frame(capture).Targets, t => t.Id == Ambush);
    }

    [Fact]
    public void AMismatchedControllerIsNotTreatedAsTheSpentAmbush()
    {
        var capture = Patch(OceanPalaceFixtures.Read("062759-1078"), null,
            (9, a => a with { ClassTag = 4, ScriptCallsEnabled = false, ScriptProcessingEnabled = false }));
        Assert.Contains(OceanPalaceFixtures.Frame(capture).Targets, t => t.Id == Ambush);
    }

    [Theory]
    [InlineData("062759-1580", LowerWest, "actor-encounter:25")]
    [InlineData("062759-1932", LowerEast, "actor-encounter:29")]
    public void LowerRoomStoryReachesTheCreatureInFrontOfTheBlockedPassage(string id, string storyId, string creatureId)
    {
        var frame = OceanPalaceFixtures.Frame(id);
        var creature = Single(frame, creatureId);
        var story = Single(frame, storyId);
        Assert.Equal(creature.ApproachPoints, story.ApproachPoints);
        // Native contact (Atel0394 actor25/29 function 2); never a Confirm finish.
        Assert.All(story.ApproachPoints, p => Assert.NotNull(story.ContactAt(p)));
        Assert.Null(story.ConfirmFacings);
        Assert.True(story.ApproachConfirms is null || story.ApproachConfirms.Count == 0);
        Assert.Contains(Route(frame, story)[^1], creature.ApproachPoints);
        AssertGuides(frame, story);
    }

    [Theory]
    [InlineData(true, LowerWest, "landmark:22", "actor-encounter:25")]
    [InlineData(false, LowerEast, "landmark:26", "actor-encounter:29")]
    public void LowerRoomStoryReturnsToTheWallSwitchAfterTheBattle(bool west, string storyId, string switchId, string creatureId)
    {
        var frame = OceanPalaceFixtures.Frame(AfterLowerBattle(west));
        Assert.DoesNotContain(frame.Targets, t => t.Id == creatureId);
        var wall = Single(frame, switchId);
        Assert.Null(wall.GuidanceRestriction);
        var story = Single(frame, storyId);
        Assert.Equal(wall.ApproachPoints, story.ApproachPoints);
        var end = Route(frame, story)[^1];
        Assert.NotEmpty(story.ConfirmAt(end)!(end));
        AssertGuides(frame, story);
        AssertGuides(frame, wall);
    }

    [Theory]
    [InlineData("062759-1580", "landmark:22", "actor-encounter:25", 30, 31)]
    [InlineData("062759-1932", "landmark:26", "actor-encounter:29", 32, 33)]
    public void AWallSwitchBehindTheStandingPeopleIsRestrictedAndTheCreatureStaysReachable(
        string id, string switchId, string creatureId, int first, int second)
    {
        var frame = OceanPalaceFixtures.Frame(id);
        var wall = Single(frame, switchId);
        Assert.Equal(NavigationCategory.Objects, wall.Category);
        Assert.False(string.IsNullOrWhiteSpace(wall.GuidanceRestriction));
        Assert.False(string.IsNullOrWhiteSpace(wall.Instruction));
        // The concealed battle identity and its outcome stay undisclosed.
        foreach (var text in new[] { wall.GuidanceRestriction!, wall.Instruction! })
        foreach (var word in new[] { "Scouter", "battle", "fight", "disappear", "vanish" })
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        foreach (var command in new[] { NavigationCommand.Guide, NavigationCommand.ToggleWalk })
        {
            var result = Select(frame, wall).Handle(command, frame with { Targets = [wall] }, 1);
            Assert.False(result.Guiding);
            Assert.Contains(wall.GuidanceRestriction!, string.Join(" ", result.Speech));
        }
        var creature = Single(frame, creatureId);
        Assert.Equal(NavigationCategory.Enemies, creature.Category);
        AssertGuides(frame, creature);
        // Their native collision remains. The exact priority-zero placements
        // sit behind the background and must not be announced as visible people.
        Assert.DoesNotContain(frame.Targets, t => t.Id == $"actor:{first}:4:48");
        Assert.DoesNotContain(frame.Targets, t => t.Id == $"actor:{second}:4:48");
    }

    [Theory]
    [InlineData(30, "visual")]
    [InlineData(31, "hidden")]
    [InlineData(30, "off-camera")]
    public void OnlyTheExactVisibleBlockersRestrictTheWallSwitch(int slot, string change)
    {
        var capture = Patch(OceanPalaceFixtures.Read("062759-1580"), null, (slot, a => change switch
        {
            "visual" => a with { VisualIndex = 0x31 },
            "hidden" => a with { DrawMode = 0 },
            _ => a with { ActivationBinding = 0 },
        }));
        var frame = OceanPalaceFixtures.Frame(capture);
        Assert.Null(Single(frame, "landmark:22").GuidanceRestriction);
        var story = Single(frame, LowerWest);
        Assert.Equal(Single(frame, "landmark:22").ApproachPoints, story.ApproachPoints);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("visual")]
    public void WithoutTheIdentifiableCreatureTheBlockedSwitchNamesOnlyThePeople(string change)
    {
        var capture = Patch(OceanPalaceFixtures.Read("062759-1580"), null, (25, a => change switch
        {
            "hidden" => a with { DrawMode = 0 },
            "disabled" => a with { ScriptCallsEnabled = false },
            _ => a with { VisualIndex = 0x53 },
        }));
        var frame = OceanPalaceFixtures.Frame(capture);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor-encounter:25");
        var wall = Single(frame, "landmark:22");
        var story = Single(frame, LowerWest);
        Assert.Equal(wall.ApproachPoints, story.ApproachPoints);
        foreach (var restriction in new[] { wall.GuidanceRestriction, story.GuidanceRestriction })
        {
            Assert.False(string.IsNullOrWhiteSpace(restriction));
            Assert.DoesNotContain("creature", restriction, StringComparison.OrdinalIgnoreCase);
        }
        var result = Select(frame, story).Handle(NavigationCommand.ToggleWalk, frame with { Targets = [story] }, 1);
        Assert.False(result.Guiding);
    }

    [Theory]
    [InlineData("031602-9253", "actor:10:4:167")]
    [InlineData("031602-9253", "actor:16:4:167")]
    public void UpperRoomFloorPanelsAreFloorSwitchesFinishedByContact(string id, string targetId)
    {
        var target = Single(OceanPalaceFixtures.Frame(id), targetId);
        Assert.Equal("Floor switch", target.Label);
        Assert.Equal(NavigationCategory.Objects, target.Category);
        Assert.NotNull(target.ContactPosition);
        Assert.Null(target.ConfirmFacings);
    }

    [Theory]
    [InlineData("062759-1580", "landmark:22")]
    [InlineData("062759-1932", "landmark:26")]
    public void LowerRoomWallMarkersAreWallSwitches(string id, string targetId)
    {
        var target = Single(OceanPalaceFixtures.Frame(id), targetId);
        Assert.Equal("Wall switch", target.Label);
        Assert.Equal(NavigationCategory.Objects, target.Category);
    }

    [Fact]
    public void AnActivatedWallSwitchIsNotOfferedAgain()
    {
        // G162 0x17: actor22's Confirm (0704) now returns at once, as before naming.
        var frame = OceanPalaceFixtures.Frame("062759-1932");
        Assert.Equal(0x10, OceanPalaceFixtures.Read("062759-1932").Story.Global(0x162)!.Value & 0x10);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "landmark:22");
    }

    [Fact]
    public void TheElevatorControlIsNamedAndKeepsItsConfirmFinish()
    {
        var target = Single(OceanPalaceFixtures.Frame(AfterAmbush()), Control);
        Assert.Equal("Elevator control", target.Label);
        Assert.Equal(NavigationCategory.Objects, target.Category);
        Assert.NotNull(target.ConfirmFacings);
        Assert.Null(target.ContactPosition);
    }

    [Fact]
    public void TheLowerElevatorControlIsNamedAndKeepsItsConfirmFinish()
    {
        // Atel0226 actor9: load_npc 71 at (3488,5216); function 1 rides to 417.
        var frame = Synthetic(418, 16, 48, new FieldStoryState(198, true), (2048, 3583),
            Actor(9, 3488, 5216, 4, 0x71));
        var target = Single(frame, "actor:9:4:113");
        Assert.Equal("Elevator control", target.Label);
        Assert.Equal(NavigationCategory.Objects, target.Category);
        Assert.NotNull(target.ConfirmFacings);
    }

    [Fact]
    public void TheCentralSwitchActorIsAFloorSwitchNotAPerson()
    {
        // Atel0395: panel actor7 (person A7) has no handler; actor8 (enemy D5,
        // priority 04 beneath the panel) runs the switch on Confirm or contact.
        var story = new FieldStoryState(195, true) { Globals = new Dictionary<int, int> { [0x162] = 0x06 } };
        var frame = Synthetic(407, 48, 64, story, (6144, 11519),
            Actor(7, 6144, 12784, 4, 0xA7) with { LoadedFlag = 0 },
            Actor(8, 6144, 12656, 5, 0xD5) with { LoadedFlag = 0 });
        var target = Single(frame, "actor:8:5:213");
        Assert.Equal("Floor switch", target.Label);
        Assert.Equal(NavigationCategory.Objects, target.Category);
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.People);
        var central = Single(frame, "story:full:palace-switch");
        Assert.Equal(target.ApproachPoints, central.ApproachPoints);
    }

    [Fact]
    public void TheBridgeSwitchIsItsVisiblePanelFinishedByTheKnobsContact()
    {
        // Atel0399: panel actor8 (person A7, priority 26) is what is drawn; knob actor9
        // (person 64, priority 04 beneath it) is the bridge switch's touch handler.
        var story = new FieldStoryState(198, true) { Globals = new Dictionary<int, int> { [0x162] = 0x77, [0x15A] = 0 } };
        var frame = Synthetic(412, 32, 32, story, (4336, 3583),
            Actor(8, 4336, 6640, 4, 0xA7) with { LoadedFlag = 0 },
            Actor(9, 4336, 6480, 4, 0x64) with { RenderPriorityPacked = 0, LoadedFlag = 0 });
        var target = Single(frame, "actor:8:4:167");
        Assert.Equal("Floor switch", target.Label);
        Assert.Equal(NavigationCategory.Objects, target.Category);
        Assert.Null(target.ConfirmFacings);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:9:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(406, 10, 4, 0xA6)]
    [InlineData(405, 10, 4, 0xA7)]
    [InlineData(406, 10, 5, 0xA7)]
    [InlineData(411, 10, 4, 0x72)]
    [InlineData(418, 9, 5, 0x71)]
    public void SwitchNamesRequireTheExactNativeIdentity(int scene, int slot, int classTag, int visual)
    {
        // An activation candidate stays listed under its generic appearance.
        var frame = Synthetic(scene, 48, 64, new FieldStoryState(195, true), (4096, 4351),
            Actor(slot, 4096, 3568, classTag, visual) with { ActivationEnabled = 1 });
        var target = Assert.Single(frame.Targets, t => t.Id == $"actor:{slot}:{classTag}:{visual}");
        Assert.NotEqual("Floor switch", target.Label);
        Assert.NotEqual("Elevator control", target.Label);
    }

    private static OceanPalaceFixtures.Capture AfterAmbush() =>
        // Logged 01:31:03/01:32:30: actor9 stopped itself (0B 12 at 02D0), actor16 hidden.
        Patch(OceanPalaceFixtures.Read("062759-1078"), (1888, 5144),
            (9, a => a with { DrawMode = 0, ScriptCallsEnabled = false, ScriptProcessingEnabled = false }),
            (16, Spent),
            (12, a => Spent(a) with { FineX = 1152, FineY = 4820 }), (13, a => Spent(a) with { FineX = 2944, FineY = 4820 }),
            (14, a => Spent(a) with { FineX = 1152, FineY = 6028 }), (15, a => Spent(a) with { FineX = 2944, FineY = 6028 }));

    private static OceanPalaceFixtures.Capture AfterLowerBattle(bool west) => west
        // Logged 01:36:33 (west) and 01:38:33 (east, people slots by the same f4).
        ? Patch(OceanPalaceFixtures.Read("062759-1580"), (4092, 8707),
            (23, a => Spent(a) with { FineX = 4600 }), (24, a => Spent(a) with { FineX = 3592 }), (25, Spent),
            (30, Gone), (31, Gone))
        : Patch(OceanPalaceFixtures.Read("062759-1932"), (9972, 8724),
            (27, a => Spent(a) with { FineX = 10488 }), (28, a => Spent(a) with { FineX = 9480 }), (29, Spent),
            (32, Gone), (33, Gone));

    private static FieldActorSnapshot Spent(FieldActorSnapshot a) =>
        a with { DrawMode = 0, ActivationBinding = 0, ScriptCallsEnabled = false };

    private static FieldActorSnapshot Gone(FieldActorSnapshot a) => a with { DrawMode = 0, ActivationBinding = 0 };

    private static OceanPalaceFixtures.Capture Patch(OceanPalaceFixtures.Capture capture, (int X, int Y)? player,
        params (int Slot, Func<FieldActorSnapshot, FieldActorSnapshot> Change)[] changes)
    {
        var leader = capture.Field.LeadPlayer!.Index;
        var actors = capture.Field.Actors.Select(a =>
        {
            foreach (var (slot, change) in changes)
                if (a.Index == slot) a = change(a);
            if (player is { } p && a.Index == leader)
                a = a with { FineX = p.X, FineY = p.Y, TileX = p.X >> 8, TileY = p.Y >> 8, FractionX = p.X & 255, FractionY = p.Y & 255 };
            return a with { TileX = a.FineX >> 8, TileY = a.FineY >> 8, FractionX = a.FineX & 255, FractionY = a.FineY & 255 };
        }).ToArray();
        return capture with { Field = capture.Field with { Actors = actors, LeadPlayer = actors.Single(a => a.Index == leader) } };
    }

    private static NavigationTarget Single(NavigationFrame frame, string id) => Assert.Single(frame.Targets, t => t.Id == id);

    private static IReadOnlyList<NavigationPoint> Route(NavigationFrame frame, NavigationTarget target)
    {
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        return route;
    }

    private static NavigationController Select(NavigationFrame frame, NavigationTarget target)
    {
        var controller = new NavigationController();
        var selected = frame with { Targets = [target] };
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, selected, 0);
        return controller;
    }

    private static void AssertGuides(NavigationFrame frame, NavigationTarget target)
    {
        foreach (var command in new[] { NavigationCommand.Guide, NavigationCommand.ToggleWalk })
        {
            var result = Select(frame, target).Handle(command, frame with { Targets = [target] }, 1);
            Assert.True(result.Guiding, string.Join(" | ", result.Speech));
            Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        }
    }

    private static NavigationFrame Synthetic(int scene, int width, int height, FieldStoryState story,
        (int X, int Y) player, params FieldActorSnapshot[] actors)
    {
        var lead = Actor(1, player.X, player.Y, 0, 0) with { IsPartyMember = true };
        var all = new[] { lead }.Concat(actors).ToArray();
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, all.Length, scene, true,
            1, 0, 1, 1, lead, all, [1]);
        var cells = width * height;
        var map = new FieldMapSnapshot(width, height, new byte[cells], new byte[cells],
            Enumerable.Repeat((byte)1, cells).ToArray(), 1, false, width, height,
            Enumerable.Repeat((byte)128, cells).ToArray());
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map,
            new(0, 0, width * 256, height * 256), [], story);
    }

    private static FieldActorSnapshot Actor(int index, int x, int y, int classTag, int visual) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, visual, classTag, 0x2020,
            0, 128, false, true, true, true) { CurrentScriptOpcode = 0xB2 };

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
