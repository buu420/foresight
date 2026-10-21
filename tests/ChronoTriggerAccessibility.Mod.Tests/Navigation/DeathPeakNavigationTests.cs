using System.Buffers.Binary;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class DeathPeakNavigationTests
{
    [Fact]
    public void TransformedTreesAreDistinctTargetsWithNativeShelteredApproaches()
    {
        var frame = Frame(6);
        var trees = frame.Targets.Where(t => t.Id.StartsWith("death-peak:shelter:")).ToArray();
        Assert.Equal(2, trees.Length);
        Assert.Equal(2, trees.Select(t => t.Label).Distinct().Count());
        Assert.All(trees, tree =>
        {
            Assert.Equal(NavigationCategory.Objects, tree.Category);
            Assert.True(tree.GuideAvailable);
            Assert.NotEmpty(tree.ApproachPoints);
            Assert.Contains("wind", tree.ArrivalInstruction!, StringComparison.OrdinalIgnoreCase);
            Assert.All(tree.ApproachPoints, p => Assert.True(tree.Id.EndsWith("lower")
                ? p.X / 256 is >= 53 and <= 57 && p.Y / 256 is >= 48 and <= 53
                : p.X / 256 is >= 54 and <= 58 && p.Y / 256 is >= 30 and <= 36));
        });
    }

    [Fact]
    public void BeforeTheTransformationTheDrawnDollIsAShelterButNotATree()
    {
        // 0184 protects X53..57/Y48..54 from 7C bit1 alone; actor9 draws the doll (02BE) until bit2.
        var targets = Frame(2, doll: Doll()).Targets;
        var shelter = Assert.Single(targets, t => t.Id == "death-peak:shelter:lower");
        Assert.Equal("Shelter below the doll", shelter.Label);
        Assert.DoesNotContain("tree", shelter.Instruction!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tree", shelter.ArrivalInstruction!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(targets, t => t.Id == "death-peak:shelter:upper" && t.Label == "Upper shelter tree");
    }

    [Fact]
    public void TheDollShelterNeedsTheActualDrawnNativeDoll()
    {
        Assert.DoesNotContain(Frame(2).Targets, t => t.Id == "death-peak:shelter:lower");
        Assert.DoesNotContain(Frame(2, doll: Doll() with { DrawMode = 0 }).Targets, t => t.Id == "death-peak:shelter:lower");
        Assert.DoesNotContain(Frame(2, doll: Doll() with { VisualIndex = 112 }).Targets, t => t.Id == "death-peak:shelter:lower");
        // Bit2 hides the doll (02C4): a doll snapshot cannot revive the old label.
        Assert.DoesNotContain(Frame(6, doll: Doll()).Targets, t => t.Label == "Shelter below the doll");
        Assert.DoesNotContain(Frame(2, doll: Doll(), viewport: new(48 * 256, 52 * 256, 64 * 256, 64 * 256)).Targets,
            t => t.Id == "death-peak:shelter:lower");
        // The doll on screen reveals its shelter, even with the floor below it off camera.
        Assert.Contains(Frame(2, doll: Doll(), viewport: new(54 * 256, 46 * 256, 57 * 256, 48 * 256)).Targets,
            t => t.Id == "death-peak:shelter:lower");
    }

    [Fact]
    public void AnUnknownDollStateIsSilentButTheNativeUpperTreeIsNotGatedByIt()
    {
        // 01E4 draws the upper tree and 019F protects it without testing global 7C.
        Assert.DoesNotContain(Frame(null, doll: Doll()).Targets, t => t.Id == "death-peak:shelter:lower");
        Assert.Contains(Frame(null).Targets, t => t.Id == "death-peak:shelter:upper");
        Assert.Contains(Frame(0).Targets, t => t.Id == "death-peak:shelter:upper");
        Assert.DoesNotContain(Frame(0, doll: Doll()).Targets, t => t.Id == "death-peak:shelter:lower");
    }

    [Theory]
    [InlineData(2, false, "Shelter below the doll")]
    [InlineData(6, true, "Lower shelter tree")]
    public void TheRetainedNativePatchIsAPushFreeGoalWithoutTrunkAlignment(int flags, bool live, string label)
    {
        var map = Map(flags, live);
        var shelter = Assert.Single(Frame(flags, doll: flags == 2 ? Doll() : null, live: live).Targets,
            t => t.Id == "death-peak:shelter:lower");
        Assert.Equal(label, shelter.Label);
        Assert.Null(shelter.ArrivalXRange);
        Assert.NotEmpty(shelter.ApproachPoints);
        Assert.All(shelter.ApproachPoints, p =>
        {
            Assert.True(p.X / 256 is >= 53 and <= 57 && p.Y / 256 is >= 48 and <= 54, $"{p} leaves the protected area.");
            Assert.Equal(0, map.TerrainFlags[p.Y / 256 * 64 + p.X / 256] & 12);
        });
        Assert.DoesNotContain("Hold Up", shelter.ArrivalInstruction!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not push", shelter.ArrivalInstruction!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReloadedTreeHasNoPatchSoItKeepsTheTrunkGoal()
    {
        var map = Map(6);
        var tree = Assert.Single(Frame(6).Targets, t => t.Id == "death-peak:shelter:lower");
        var goal = Assert.Single(tree.ApproachPoints);
        Assert.Equal((14192, 14223), tree.ArrivalXRange);
        Assert.Equal(8, map.TerrainFlags[goal.Y / 256 * 64 + goal.X / 256] & 12);
        Assert.Contains("Hold Up", tree.ArrivalInstruction!);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide, false)]
    [InlineData(NavigationCommand.ToggleWalk, true)]
    public void BothNavigationModesReachTheDollShelter(NavigationCommand command, bool walking)
    {
        var frame = Frame(2, doll: Doll());
        var shelter = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:lower");
        frame = frame with { Targets = [shelter] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        var result = controller.Handle(command, frame, 2);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.Equal(walking, result.AutoWalking);
        // Arrival on the push-free floor needs no trunk alignment in either mode.
        var offCentre = shelter.ApproachPoints.First(p => p.X / 256 != 55);
        var near = frame with { Player = offCentre };
        controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, near, 10);
        controller.Handle(NavigationCommand.NextCategory, near, 11);
        var arrival = controller.Handle(command, near, 12);
        Assert.False(arrival.Guiding);
        Assert.Contains(arrival.Speech, s => s.Contains("Arrived at Shelter below the doll. The wind does not push you"));
    }

    [Fact]
    public void WindKnockbackRegionsAreNotOfferedAsOrdinaryExits()
    {
        Assert.DoesNotContain(Frame(6, gust: 1).Targets,
            t => t.Id.StartsWith("script-region:0:498:") && t.Category == NavigationCategory.Exits);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide, false)]
    [InlineData(NavigationCommand.ToggleWalk, true)]
    public void BothNavigationModesCanPlanToTheLowerTree(NavigationCommand command, bool walking)
    {
        var frame = Frame(6);
        var tree = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:lower");
        frame = frame with { Targets = [tree] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        var result = controller.Handle(command, frame, 2);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.Equal(walking, result.AutoWalking);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void ANewRouteCanContinueFromLowerShelterToUpperShelter(NavigationCommand command)
    {
        var frame = Frame(6);
        var lower = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:lower");
        var upper = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:upper");
        frame = frame with { Player = lower.ApproachPoints[0], Targets = [upper] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        var result = controller.Handle(command, frame, 2);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
    }

    [Fact]
    public void AnUnseenTreeIsNotRevealedByItsNativeShelterPredicate()
    {
        Assert.DoesNotContain(Frame(6, viewport: new(0, 0, 256, 256)).Targets,
            t => t.Id.StartsWith("death-peak:shelter:"));
    }

    [Theory]
    [InlineData("lower", 53, 49, 58, 55)]
    [InlineData("upper", 54, 32, 59, 38)]
    public void SeeingTheProtectedFloorDoesNotRevealATreeAboveTheCamera(
        string id, int left, int top, int right, int bottom)
    {
        Assert.DoesNotContain(Frame(6, viewport: new(left * 256, top * 256, right * 256, bottom * 256)).Targets,
            t => t.Id == "death-peak:shelter:" + id);
    }

    [Fact]
    public void AVisibleLowerCanopyCountsEvenWhenTheShelterFloorIsBelowTheCamera()
    {
        Assert.Contains(Frame(6, viewport: new(55 * 256, 43 * 256, 56 * 256, 45 * 256)).Targets,
            t => t.Id == "death-peak:shelter:lower");
    }

    [Theory]
    [InlineData("lower", 53, 48, 57, 54)]
    [InlineData("upper", 54, 30, 58, 37)]
    public void HoldingUpAtEveryShelterApproachMeetsCoverBeforeLeavingTheProtectedArea(
        string id, int left, int top, int right, int bottom)
    {
        var tree = Assert.Single(Frame(6).Targets, t => t.Id == "death-peak:shelter:" + id);
        var graph = new FieldNavigationGraph(Map(6));
        foreach (var start in tree.ApproachPoints)
        {
            var point = start;
            var visited = new HashSet<NavigationPoint>();
            while (visited.Add(point))
            {
                var north = graph.Neighbours(point)
                    .Where(next => graph.InputDirection(point, next) == NavigationDirection.North).ToArray();
                if (north.Length == 0) break;
                point = north.OrderBy(p => p.Y).First();
                Assert.True(point.X / 256 >= left && point.X / 256 <= right &&
                    point.Y / 256 >= top && point.Y / 256 <= bottom,
                    $"Holding Up from {start} leaves {id} shelter at {point}.");
                Assert.True(visited.Count <= 256, "Holding Up did not meet shelter geometry.");
            }
        }
    }

    [Theory]
    [InlineData(NavigationCommand.Guide, 16)]
    [InlineData(NavigationCommand.ToggleWalk, 16)]
    [InlineData(NavigationCommand.Guide, -32)]
    [InlineData(NavigationCommand.ToggleWalk, -32)]
    public void ArrivalWaitsUntilBothLeadingCornersWillMeetTheTree(NavigationCommand command, int offset)
    {
        var frame = Frame(6);
        var tree = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:lower");
        var goal = tree.ApproachPoints[0];
        frame = frame with { Player = goal with { X = goal.X + offset }, Targets = [tree] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        var result = controller.Handle(command, frame, 2);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("Arrived"));
        frame = frame with { Player = goal };
        var arrival = controller.Update(frame, 20);
        Assert.Contains(arrival.Speech, s => s.Contains("Arrived at"));
    }

    [Theory]
    [InlineData(NavigationCommand.Guide, 500)]
    [InlineData(NavigationCommand.ToggleWalk, 20)]
    public void ASmallDriftAtTheTreeRequiresHorizontalAlignmentInBothModes(NavigationCommand command, long now)
    {
        var frame = Frame(6);
        var tree = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:lower");
        var goal = tree.ApproachPoints[0];
        frame = frame with { Player = goal with { Y = goal.Y + 256 }, Targets = [tree] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        Assert.True(controller.Handle(command, frame, 2).Guiding);
        frame = frame with { Player = goal with { X = goal.X + 16 } };
        var correction = controller.Update(frame, now);
        // Manual guidance first asks the moving player to stop before replanning.
        if (command == NavigationCommand.Guide) correction = controller.Update(frame, now + 250);
        Assert.True(correction.Guiding, string.Join(" ", correction.Speech));
        Assert.Equal(NavigationDirection.West,
            command == NavigationCommand.Guide ? correction.ManualLeg!.Direction : correction.Direction);
    }

    [Fact]
    public void ManualAlignmentIsSpokenOnceWhileThePlayerWaits()
    {
        var frame = Frame(6);
        var tree = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:lower");
        var goal = tree.ApproachPoints[0];
        frame = frame with { Player = goal with { Y = goal.Y + 256 }, Targets = [tree] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        controller.Handle(NavigationCommand.Guide, frame, 2);
        frame = frame with { Player = goal with { X = goal.X + 16 } };
        var speech = new List<string>(controller.Update(frame, 20).Speech);
        speech.AddRange(controller.Update(frame, 300).Speech);
        for (var tick = 1; tick <= 60; tick++) speech.AddRange(controller.Update(frame, 300 + tick * 16).Speech);
        Assert.Single(speech, s => s == "Route updated.");
    }

    [Fact]
    public void CancellingDuringTheStopPauseDoesNotSuppressAlignmentOnANewRoute()
    {
        var frame = Frame(6);
        var tree = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:lower");
        var goal = tree.ApproachPoints[0];
        var below = frame with { Player = goal with { Y = goal.Y + 256 }, Targets = [tree] };
        var beside = below with { Player = goal with { X = goal.X + 16 } };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, below, 0);
        controller.Handle(NavigationCommand.NextCategory, below, 1);
        controller.Handle(NavigationCommand.Guide, below, 2);
        controller.Update(beside, 20);
        controller.Cancel("manual control");
        controller.Handle(NavigationCommand.Guide, below, 300);
        controller.Update(beside, 500);
        var correction = controller.Update(beside, 750);
        Assert.Equal(NavigationDirection.West, correction.ManualLeg!.Direction);
    }

    [Fact]
    public void PushedTreeArrivalStartsWithTheActionNeededToRemainUnderCover()
    {
        var frame = Frame(6);
        var tree = Assert.Single(frame.Targets, t => t.Id == "death-peak:shelter:lower");
        frame = frame with { Player = tree.ApproachPoints[0], Targets = [tree] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        var result = controller.Handle(NavigationCommand.Guide, frame, 2);
        Assert.StartsWith("Hold Up now.", result.Speech.Last());
    }

    [Fact]
    public void NativeWindPhaseChangesAreSpokenWithoutInventingMovingFloors()
    {
        var memory = new WindMemory();
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Contains("Wind pushes you downhill", Assert.Single(speech));
        memory.Gust(1);
        feedback.Observe(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Contains("Strong wind", speech[1]);
        for (var i = 0; i < 20; i++) feedback.Observe(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Equal(2, speech.Count);
        memory.Gust(0);
        feedback.Observe(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Contains("Gust passed", speech[2]);
        Assert.DoesNotContain(speech, s => s.Contains("floor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownWindDoesNotInventCalmOrRepeatTheSameGust()
    {
        var memory = new WindMemory();
        memory.Gust(1);
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(FieldFloorCapture.Capture(memory, 0x1000));
        memory.RefuseWind = true;
        feedback.Observe(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Contains("Wind pushes you downhill", feedback.Status);
        Assert.DoesNotContain("gust", feedback.Status!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(speech);
        memory.RefuseWind = false;
        feedback.Observe(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Single(speech);
        memory.Gust(2);
        feedback.Observe(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Contains("Wind pushes you downhill", feedback.Status);
        Assert.DoesNotContain("gust", feedback.Status!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(speech);
    }

    [Fact]
    public void APhaseChangeDuringCaptureIsUnavailableUntilStable()
    {
        var memory = new WindMemory { ChangeWindDuringRead = true };
        var floor = Assert.IsType<FieldFloorSnapshot>(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Null(floor.WindGusting);
        Assert.True(FieldFloorCapture.Capture(memory, 0x1000)!.Value.WindGusting);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARequiredDashSettingDescribesWindRatherThanAConveyor(bool readable)
    {
        var frame = Frame(6) with { MovingFloor = new(NavigationDirection.South, 16) };
        var floor = readable ? new FieldFloorSnapshot(0x1000, 0x2000, 244, 6, 0, 16, 0, 2) : (FieldFloorSnapshot?)null;
        var result = FieldFloorMovement.Adjust(frame, new([], NavigationDirection.North, true, true), floor);
        Assert.Equal(0u, result.Pad);
        Assert.Contains("wind", result.StopReason!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conveyor", result.StopReason!, StringComparison.OrdinalIgnoreCase);
    }

    // Atel_0052 actor9: load_person B9 (class 4) at tile (55,47); fine position from the tester log.
    private static FieldActorSnapshot Doll() =>
        Actor(9, 14208, 12287) with { ClassTag = 4, VisualIndex = 185, DrawMode = 1 };

    private static NavigationFrame Frame(int? flags, int gust = 0, FieldViewport? viewport = null,
        FieldActorSnapshot? doll = null, bool live = false)
    {
        var leader = Actor(6, 55 * 256 + 128, 61 * 256 + 128) with { ClassTag = 0, IsPartyMember = true };
        var script = Actor(0, 0, 0) with { ClassTag = 7, DrawMode = 0 };
        FieldActorSnapshot[] actors = doll is null ? [script, leader] : [script, doll, leader];
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 11,
            244, true, 12, 0, 1, 1, leader, actors);
        var story = flags is null ? null : new FieldStoryState(212, true)
        {
            Globals = new Dictionary<int, int> { [0x7C] = flags.Value },
            Locals = new Dictionary<int, int> { [12] = gust },
        };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, Map(flags ?? 0, live),
            viewport ?? new(0, 0, 64 * 256, 64 * 256), [], story);
    }

    /// <summary>DollPresent: startup 01E4 and 0200. Transformed: a reload after bit2 (01E4, 01F1).
    /// LiveTransformed: DollPresent followed by actor9's 02E7 copy, before any reload.</summary>
    private static FieldMapSnapshot Map(int flags, bool live = false)
    {
        using var input = typeof(DeathPeakNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.death-peak-244-map-0368.json")!;
        using var doc = JsonDocument.Parse(input);
        var d = doc.RootElement;
        var planes = d.GetProperty((flags & 4) == 0 ? "DollPresent" : live ? "LiveTransformed" : "Transformed");
        var map = new FieldMapSnapshot(d.GetProperty("Width").GetInt32(), d.GetProperty("Height").GetInt32(),
            planes.GetProperty("CollisionShapes").GetBytesFromBase64(), planes.GetProperty("TerrainFlags").GetBytesFromBase64(),
            planes.GetProperty("CollisionLayers").GetBytesFromBase64(), 1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());
        return map;
    }

    private static FieldActorSnapshot Actor(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, 14, 4, 0x2020, 1, 1, false, true, true, true);
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }

    private sealed class WindMemory : IReadableMemory
    {
        private readonly byte[] bytes = new byte[0x30000];
        public bool RefuseWind { get; set; }
        public bool ChangeWindDuringRead { get; set; }
        public WindMemory()
        {
            Write(0x1000, 0x15000); Write(0x1040, 0x2000); Write(0x1850, 0x11000);
            Write(0x1854, 0x14000); Write(0x1B9C, 0x13000); bytes[0x27000] = 3;
            Write(0x1208C, 1); Write(0x120E0, 0); Write(0x121EC, 2);
            Write(0x12010, 244); Write(0x132A0, 244);
            const int actor = 0x2000 + 0x6940 + 0x154;
            Write(actor + 0x40, 0); Write(actor + 0xD0, 1); Write(actor + 0x60, 1);
            Write(actor + 0x84, 14080); Write(actor + 0x80, 55); Write(actor + 0x7C, 0);
            Write(actor + 0x90, 15103); Write(actor + 0x8C, 58); Write(actor + 0x88, 255);
            Write(0x14088, 0); Write(0x1408C, 8); Write(0x14138, 5);
            Write(0x15FD0, 0); Write(0x15FD4, 1);
        }
        public void Gust(int value) => Write(0x2000 + 0x118B0 + 12 * 8, value);
        private void Write(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(address, 4), value);
        public bool TryRead(nuint address, Span<byte> destination)
        {
            const int windAddress = 0x2000 + 0x118B0 + 12 * 8;
            if (RefuseWind && address == windAddress) return false;
            if (address + (nuint)destination.Length > (nuint)bytes.Length) return false;
            bytes.AsSpan((int)address, destination.Length).CopyTo(destination);
            if (ChangeWindDuringRead && address == windAddress)
            {
                ChangeWindDuringRead = false;
                Gust(1);
            }
            return true;
        }
    }
}
