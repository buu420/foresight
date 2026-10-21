using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>The Blackbird's ventilation shaft (scene 376, Atel_0274, Map169) from the
/// route evidence the mod logged at the shaft entry: October 19 report, gameplay log
/// 2026-10-19 23.16.18 line 8488 (payload SHA-256 1901E0F7...6793D17B). It holds the
/// live MapTable planes, all 37 actor snapshots and the story words at (13952,10239).
/// The party roster and camera come from lines 8472 and 8469 of the same log. The live
/// planes equal installed MapTable_0169 byte for byte.</summary>
public sealed class BlackbirdDuctNavigationTests
{
    // Grate trigger tiles are the leader-tile operands of each actor's 22/12/12 loop in
    // Atel_0274, in shaft order (top, then left). Actor25 compares (54,33), a solid tile.
    // A grate looks like every other grate: the room below is only named when its view opens.
    private static readonly Dictionary<(int X, int Y), string> GrateLabels = new()
    {
        [(32, 4)] = "Viewing grate 1", [(3, 8)] = "Viewing grate 2", [(17, 11)] = "Viewing grate 3",
        [(32, 11)] = "Viewing grate 4", [(47, 11)] = "Viewing grate 5", [(23, 18)] = "Viewing grate 6",
        [(32, 18)] = "Viewing grate 7", [(53, 18)] = "Viewing grate 8", [(3, 26)] = "Viewing grate 9",
        [(23, 26)] = "Viewing grate 10", [(32, 26)] = "Viewing grate 11", [(54, 26)] = "Viewing grate 12",
        [(60, 26)] = "Viewing grate 13", [(14, 33)] = "Viewing grate 14", [(20, 33)] = "Viewing grate 15",
        [(32, 33)] = "Viewing grate 16", [(44, 33)] = "Viewing grate 17", [(10, 40)] = "Viewing grate 18",
        [(14, 40)] = "Viewing grate 19", [(32, 40)] = "Viewing grate 20", [(54, 40)] = "Viewing grate 21",
        [(60, 40)] = "Viewing grate 22", [(32, 48)] = "Viewing grate 23", [(52, 48)] = "Viewing grate 24",
        [(12, 55)] = "Viewing grate 25", [(32, 55)] = "Viewing grate 26", [(56, 55)] = "Viewing grate 27",
        [(32, 60)] = "Viewing grate 28",
    };

    [Fact]
    public void GratesAreConfirmObjectsThatRoutesCrossRatherThanPassages()
    {
        var (frame, _, _) = Shaft();
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("script-region:", StringComparison.Ordinal) &&
            t.Category != NavigationCategory.Objects);
        var grates = frame.Targets.Where(t => t.Id.StartsWith("script-region:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(GrateLabels.Count, grates.Length);
        foreach (var grate in grates)
        {
            var tile = Assert.Single(grate.ApproachPoints.Select(p => (p.X / 256, p.Y / 256)).Distinct());
            Assert.Equal(GrateLabels[tile], grate.Label);
            Assert.True(grate.GuideAvailable);
            Assert.Contains("Confirm", grate.ArrivalInstruction, StringComparison.Ordinal);
            Assert.Contains("Confirm", grate.Instruction, StringComparison.Ordinal);
            Assert.All(grate.ApproachPoints, p => Assert.False(frame.Graph.IsTerminal(p)));
        }
    }

    [Fact]
    public void OpeningsNameTheRoomBelowAndTheCellOpeningIsOneRow()
    {
        var (frame, _, _) = Shaft();
        var exits = frame.Targets.Where(t => t.Category == NavigationCategory.Exits).ToDictionary(t => t.Id, t => t.Label);
        Assert.Equal(new Dictionary<string, string>
        {
            ["exit:0"] = "Down to the Rearward Passage, lower opening",
            ["exit:1"] = "Down to the Rearward Passage, upper opening",
            ["exit:2"] = "Down to the Forward Passage",
            ["exit:3"] = "Down to Room 4",
            ["exit:4"] = "Down to Room 3",
            ["exit:5"] = "Down to Room 2, the cell",
            ["exit:6"] = "Down to Room 7",
            ["exit:7"] = "Down to Room 6",
            ["exit:8"] = "Down to Room 10",
            ["exit:9"] = "Down to Room 9",
        }, exits);
    }

    [Fact]
    public void EveryOpeningAndGrateHasARouteFromTheShaftEntry()
    {
        var (frame, _, _) = Shaft();
        var destinations = frame.Targets.Where(t => t.Category is NavigationCategory.Exits or NavigationCategory.Objects).ToArray();
        Assert.Equal(10 + GrateLabels.Count, destinations.Length);
        foreach (var target in destinations)
        {
            var search = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
            Assert.False(search.LimitReached, target.Label);
            Assert.True(search.Route is not null, $"No route to {target.Label}.");
        }
    }

    // Every native way into the shaft (catalog exits and actor warps whose destination is
    // 376): the cell's air duct, both passages and the rooms' duct openings.
    [Theory]
    [InlineData(54, 39)] [InlineData(7, 52)] [InlineData(45, 33)] [InlineData(21, 4)] [InlineData(52, 46)]
    [InlineData(12, 46)] [InlineData(53, 18)] [InlineData(9, 26)] [InlineData(17, 8)] [InlineData(44, 8)]
    public void FromEveryWayInEveryOpeningAndGrateCanBeRouted(int tileX, int tileY)
    {
        var (frame, _, _) = Shaft(tileX * 256 + 128, tileY * 256 + 255);
        foreach (var target in frame.Targets.Where(t => t.Category is NavigationCategory.Exits or NavigationCategory.Objects))
        {
            var search = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
            Assert.False(search.LimitReached, target.Label);
            Assert.True(search.Route is not null, $"No route from ({tileX},{tileY}) to {target.Label}.");
        }
    }

    [Fact]
    public void TheEquipmentObjectiveLeadsThroughTheShaftToRoom4()
    {
        var (frame, _, _) = Shaft();
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:blackbird-equipment-3");
        var opening = Assert.Single(frame.Targets, t => t.Id == "exit:3");
        Assert.Equal(opening.ApproachPoints, story.ApproachPoints);
        Assert.Contains("Next passage: Down to Room 4.", story.Instruction, StringComparison.Ordinal);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, story.ApproachPoints).Route);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothModesCrawlFromTheEntryAcrossGratesIntoRoom4(bool auto)
    {
        var (frame, map, _) = Shaft();
        var terrain = new FieldNavigationGraph(map);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:blackbird-equipment-3");
        frame = frame with { Targets = [target] };
        var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(auto ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        Assert.True(result.Guiding, string.Join(" | ", result.Speech));
        Assert.Equal(auto, result.AutoWalking);
        var speech = new List<string>(result.Speech);
        var tiles = new HashSet<(int, int)>();
        for (var tick = 1; tick <= 20000 && result.Guiding && terrain.ExitAt(frame.Player.X, frame.Player.Y) != 3; tick++)
        {
            var command = auto ? result.Direction : result.ManualLeg?.Direction ?? NavigationDirection.None;
            frame = frame with { Player = NativeWalkingReplay.Move(map, frame.Player, command), PlayerFacing = command };
            Assert.True(terrain.ExitAt(frame.Player.X, frame.Player.Y) is < 0 or 3, $"Unexpected exit at {frame.Player}");
            tiles.Add((frame.Player.X / 256, frame.Player.Y / 256));
            result = controller.Update(frame, tick * 16, !auto && command != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        Assert.Equal(3, terrain.ExitAt(frame.Player.X, frame.Player.Y));
        // The grate below the entry and two on the central shaft are crossed, never entered as exits.
        Assert.Contains((54, 40), tiles);
        Assert.Contains((32, 40), tiles);
        Assert.Contains((32, 48), tiles);
        Assert.DoesNotContain(speech, s => s.StartsWith("No route", StringComparison.Ordinal) ||
            s.Contains("search limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Room5IsReachedThroughTheForwardPassageNotThroughItsGrate()
    {
        var (frame, map, state) = Shaft();
        var next = new SceneConnectionRouter().Next(376, state, [374], frame.Targets,
            liveFieldDestinations: map.ExitDestinations);
        Assert.Equal(["exit:2"], next.Select(t => t.Id));
    }

    [Theory]
    [InlineData(0x00, "landmark:10", "Air duct", NavigationCategory.Objects)]
    [InlineData(0x40, "landmark:11", "Air duct entrance", NavigationCategory.Exits)]
    public void TheCellVentIsOfferedAsTheAirDuctItIs(int noticed, string id, string label, NavigationCategory category)
    {
        // Atel_0269: actor10 (25,7) is the touch that notices the duct (AF20 set, B340
        // clear); afterwards actor11 (22,6) climbs in on Confirm while B340 is set.
        var leader = Actor(1, 25 * 256 + 128, 9 * 256 + 255) with { ClassTag = 0, IsPartyMember = true };
        var notice = Actor(10, 25 * 256 + 128, 7 * 256 + 255) with { ClassTag = 7, LoadedFlag = 0 };
        var climb = Actor(11, 22 * 256 + 128, 6 * 256 + 255) with { ClassTag = 7, LoadedFlag = 0 };
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 12, 371, true, 1, 0, 1, 1,
            leader, [leader, notice, climb], [1]);
        var map = new FieldMapSnapshot(64, 48, new byte[64 * 48], new byte[64 * 48],
            Enumerable.Repeat((byte)1, 64 * 48).ToArray(), 1, false, 64, 48, Enumerable.Repeat((byte)128, 64 * 48).ToArray())
        { ExitDestinations = new Dictionary<int, int>() };
        var story = new FieldStoryState(207, true) { Globals = new Dictionary<int, int> { [0xAF] = 0x20, [0xB3] = noticed } };
        var targets = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, map, new(4096, 256, 8320, 3456), [], story).Targets;
        var vent = Assert.Single(targets, t => t.Id is "landmark:10" or "landmark:11");
        Assert.Equal((id, label, category), (vent.Id, vent.Label, vent.Category));
        Assert.DoesNotContain(targets, t => t.Label == "Interactable scenery");
        if (category == NavigationCategory.Exits)
            Assert.Equal("Press Confirm to climb into the air duct.", vent.ArrivalInstruction);
    }

    /// <summary>The logged frame; a different leader pose moves only the leader, whose party
    /// followers are excluded from contact. The camera stays the logged one.</summary>
    private static (NavigationFrame Frame, FieldMapSnapshot Map, FieldStoryState Story) Shaft(int? x = null, int? y = null)
    {
        using var json = JsonDocument.Parse(RouteFailureEvidence.Decode(ShaftEntryEvidence));
        var root = json.RootElement;
        var actors = root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var map = root.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        var f = root.GetProperty("field");
        var leader = actors.Single(a => a.Index == f.GetProperty("leader").GetInt32());
        if (x is { } fineX && y is { } fineY)
        {
            leader = leader with { TileX = fineX >> 8, FineX = fineX, FractionX = fineX & 255,
                TileY = fineY >> 8, FineY = fineY, FractionY = fineY & 255 };
            actors = actors.Select(a => a.Index == leader.Index ? leader : a).ToArray();
        }
        var field = new FieldNavigationSnapshot(0x1A9E8E60, 0x4000, 0x2000, 0x20000, actors.Length,
            f.GetProperty("scene").GetInt32(), f.GetProperty("sceneCoherent").GetBoolean(),
            f.GetProperty("control").GetInt32(), f.GetProperty("inputMode").GetInt32(), 0, leader.Index, leader, actors, [2, 3, 4])
        {
            LastPartySlotRaw = f.GetProperty("lastPartySlotRaw").GetInt32(),
            ActorCollisionRadius = f.GetProperty("collisionRadius").GetInt32(),
            ContactActorRaw = f.GetProperty("contactActorRaw").GetInt32(),
            ConfirmActorRaw = f.GetProperty("confirmActorRaw").GetInt32(),
        };
        var words = root.GetProperty("story");
        Dictionary<int, int> Words(string name) => words.GetProperty(name).EnumerateObject()
            .ToDictionary(p => int.Parse(p.Name), p => p.Value.GetInt32());
        var story = new FieldStoryState(words.GetProperty("point").GetInt32(), true)
        {
            Globals = Words("globals"), Locals = Words("locals"), Extended = Words("extended"),
            Party = [2, 4, 3, 128, 133, 1, 129, 5, 128],
        };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map,
            new(11776, 8192, 16000, 11392), [], story);
        return (frame, map, story);
    }

    private static FieldActorSnapshot Actor(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, 0, 4, 0x2020, 0, 128, false, true, true, true);

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }

    private const string ShaftEntryEvidence =
        "H4sIAAAAAAACCu1da2/bRhb9K4E+O8C8+DKwHyg/Nu5u0Lppdhss+kGRFUeoYnklOWmxyH/fuXMPxYdmKNKVu9aGBAhSM8PLuTPn3MeQpv8z+nV+" +
        "dzM6Hd0tX66WD5vZ6GT0aXL/Zvmwms5s8WL+efbSFmwm7xezl/eLyd1sbZt8mM8W9qr/jNbT2Z1tp5P4hM/Plh9nq9ndZnS6WT3MTkbT5d1mtVyM" +
        "TuXJaH53/7B5vbyxF4iT0WI2uZmtRqeKGi0W8/V8effj5Gb+sLaNY2owWW9+mKw2v79ZLDc/Tr6MTmOWN5lu8ulmuXJlUqWu9MN89alW+tX2yP78" +
        "nbp5v5xTl5RITka3i+X7yWJNxQJFcnRqzMlI2Z+RvbF2/TMkxfY6otLoZBRbNe3PxBan2ckoRXHmGktBrenEypJWllRcbmVFttvSoLm04mRm5ci4" +
        "OElYsEz5ChaorEBth0ZJ/qmgqbICE3t7ZQXSIeLqmA8J7qKK3qmMrrMqatJV2p5o6qC0l2ruoWZtteEDy9MsTyd84J5p7pkRfOCOGZZiMGYsxbAU" +
        "w1IMSzEsxbCUiKVELCViKRFLiVhKxFIilhKxlIilRCwlZikxS4lZSsxSYpYSs5SYpcQsJWYpMUtJWErCUhKWkrCUhKUkLCVhKQlLSVhKwlJSlpKy" +
        "lJSlpCwltVLs5KU0+TSraexwn9J8WbCkVpAUFgMpTZedpMzKsuUZy8qsLGMvz1hYxl3KuEsZdylLHMUy7lJGchKCobCS4pROJIAKYAqNo8ExwpFw" +
        "rukkQQFwKYB0KXCEQEI66SQJ64k7gUgJkQR1kighsUA6oC4VJBLWiQgO7HREFxXkAewSaJcK8hTkKcjTkKfRQ11wEfKAdQmwS6BdAu4SeJcAvATi" +
        "JSAvgXkJ0EugXgL2EriXAL4E8iWgL4F9CfBLoF8C/hL4lyCABAMkKCDBAQkSyBi2QhIPNBkZYoIzO8QFFREQiA4yo2kgRjgDZMW6xhBLpIhdUytX" +
        "UwmIIcEMmTgrRHKJHfZ2dGblGhJDFMlcHboLlkjQRBJPnHwwRYIqMsV8E1nc0eAI/Ykt7nfiXIAkujh9HV3ohPhCvJHEmIyOBSiJM84eE2u4SeS4" +
        "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" +
        "Pv9ZYJRszTjf4//25x5O563+dZvptZvn7bpU52urf3Ntw7dm0HU9Zat/eY0/Zu4U/zXmv3JNy5pJ1zi1qr8vxu2Frab+lZw0YN/Y/p234WoH/+37" +
        "K9i/pk36o3uXdajee2f899o75d6P0f/y4Pr/8+348Pp33Ylbf/mL/VdqP81Wq8n8jpbYaaU1H7Zh+9Y2Mq7DKAzbsA3bsA3bsA3bsA3bsA3bsA3b" +
        "sA3bsA3boze30rp9rdX903i32Nr6SOlp9jP7jmN9r5X72u6Rmdu9KdNdc7V7r9urou2efuZWrudaX/+dDjVdAnqP87zvWOXf5dDff1/qk9W/tl3k" +
        "4d8k99bpxvfwy2zqYfux7TuP7e24bJO7sab7oMwvk9tVdtePahnNTWX8IXs7/3lgnhpluaesrPvu7PrX70r93Tw3N8885S1zV164HZPdPuSe/ucB" +
        "nZp9DvehO57yFpwH9amPSR5Yufbgvm0O6vMf4qGH93kXfYFDrz0gLF2F9Q9cU947wJX8Kg/r77Uh1XsHeO2blwoH8zBf3b0DtqIVl7e7c97VVrXo" +
        "vwOZ68C5D3Md9a/Meav+Z63z77N5VRvbKqcj/m+rfqUb//dgsIH5jtzbi0GP/hd98JD7+VTV/6K//rs2qtC/fyySQ//dzfHp7DFxFnxhyI/Wx+QC" +
        "47wHUzyfAbsUiq86YWncGc/Xfbh026L/jo3fG3v01D/E+4YeIf374DFkS3d0Cdu56xa7V/A4pH+I5zvxns+Gtuifh/GyJ4Zo079j7OGxobv653v1" +
        "34N7r/75U+if94tdwzir67/Fz775z1vfOujO/z72fyfmaM2xOo5BM+YK96fMQ/rmoi14bL/fHv17xN7B+/XSv+v9Gvl3/sic3qd/kbtd7fGXHd6M" +
        "KfUv8/LHxF4tvnInZ/DNRzimrF9T1f+iJXfvFf9U9W/zyY/Wv81/PoL/FZv3a7t/7xbzlfrvt2sd4rQ+sVJf+9cr9r46+Bpbv/i/T+7xFPoffo3x" +
        "8XbsQLtbbOVvGLiVVvchiZ9Wk7v1nL5e8MMMn3rAtwsufptvKh8ioJ/VjxHQ77OZ/TqDXa/dZ4G+5T1fDmNwyP3sXYc2wzgN+7C7fWyGMRj2Yfdy" +
        "Y/DNw/6/3vO/DmNwdHt+OYzBsB/WDrzZU//9MEbPfg6HMRj2YR/2Q9gSt15Ly6zns/WGvrBrF2nX9HlYQR8btl8RljgqOtrP2mr6l1L2w7eGjvYT" +
        "xJH7F1Mno5j+1ab9ndBv+1HhlH7bY0ZH8/Xr1/8CxNprd6bJAAA=";
}
