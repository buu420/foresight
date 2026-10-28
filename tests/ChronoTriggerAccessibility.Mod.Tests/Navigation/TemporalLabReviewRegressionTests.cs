// Whole-change review regressions for 5b3b71d. Drop beside TemporalLabWarpTests.cs (same
// project, same fixture). Each test is RED on 5b3b71d; see whole-change-review.md.
// Evidence: scene621 frame is the RECONSTRUCTED fixture; poses and the +20 camera cache
// below are DERIVED (native 17A6C0 sets +20=0x80 for drawn actors inside the camera).
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TemporalLabReviewRegressionTests
{
    [Fact]
    public void AnUncollectedItemInAnUnseenSectionIsNotDisclosed()
    {
        var sample = TemporalLabWarpTests.Sample(621);
        // Synthetic uncollected-item state, with a full-window positive control.
        var story = sample.Story with { Extended = new Dictionary<int, int>(sample.Story.Extended) { [0x2E] = 0 } };
        var field = sample.Field with { Actors = sample.Field.Actors.Select(a => a.Index == 30 ? a with { DrawMode = 1 } : a).ToArray() };
        var full = TemporalLabWarpTests.Build(field, sample.Map, story);
        Assert.Contains(full.Targets, t => t.ActorSlot == 30 && t.Visible);
        var local = new FieldNavigationSource(new Empty(), _ => { }).Build(field, sample.Map,
            new FieldViewport(0, 5632, 4224, 8832), [], story);
        Assert.DoesNotContain(local.Targets, t => t.ActorSlot == 30 && (t.Visible || t.Discovered || t.GuideAvailable));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AnOnCameraNextPadIsApproachedWithoutOffRouteReplans(bool automatic)
    {
        // Same walk as EachLandingHasARealFirstWalkingLegToItsNextNativePad, but the frame
        // itself carries the selected pad's camera cache, as the live capture will once
        // the pad is drawn on screen. The shipped test binds it only in its contact oracle.
        var sample = TemporalLabWarpTests.Sample(621);
        var field = sample.Field with { Actors = sample.Field.Actors.Select(a => a.Index == 8 ? a with { ActivationBinding = 128 } : a).ToArray() };
        var frame = TemporalLabWarpTests.Build(field, sample.Map, sample.Story);
        var story = frame.Targets.Single(t => t.Id == "story:full:lavos");
        var controller = new NavigationController();
        for (var i = 0; i < (int)story.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        var firstPlan = PlanRevision(controller);
        var speech = new List<string>(result.Speech);
        var contacts = new FieldActorCollisionRules(field);
        var touched = false;
        for (var tick = 1; tick < 1500 && result.Guiding && !touched; tick++)
        {
            var direction = automatic ? result.Direction : result.ManualLeg?.Direction ?? Spoken(speech.LastOrDefault());
            var (dx, dy) = direction switch
            {
                NavigationDirection.North => (0, -16), NavigationDirection.South => (0, 16),
                NavigationDirection.West => (-16, 0), NavigationDirection.East => (16, 0), _ => (0, 0),
            };
            if ((dx != 0 || dy != 0) && contacts.ContactActor(frame.Player.X, frame.Player.Y, frame.Player.X + dx, frame.Player.Y + dy) == 8)
            { touched = true; break; }
            frame = frame with { Player = NativeWalkingReplay.Move(sample.Map, frame.Player, direction, 16), PlayerFacing = direction };
            result = controller.Update(frame, tick * 16, !automatic && direction != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        Assert.True(touched, string.Join(" | ", speech));
        // 5b3b71d: manual guidance says "Off route. Stop moving for new directions." four
        // times on the correct leg, and each replan reruns the whole warp look-ahead
        // (automatic walking replans silently: three extra plans of about 60-90 ms each).
        Assert.DoesNotContain(speech, s => s.StartsWith("Off route", StringComparison.Ordinal));
        Assert.DoesNotContain(speech, s => s == "Route updated.");
        Assert.True(PlanRevision(controller) - firstPlan <= 1, controller.DiagnosticState);
    }

    private static int PlanRevision(NavigationController controller) =>
        int.Parse(System.Text.RegularExpressions.Regex.Match(controller.DiagnosticState, @"plan=(\d+)").Groups[1].Value);

    [Fact]
    public void UnseenPadsAndEncountersInOtherSectionsAreNotListedFromTheEntrance()
    {
        // Logged 621 camera at the tester's arrival (log line 655). 0.3.81 listed pad 8 and
        // the door only (objects=2; enemies=0). Story guidance must still route via pad A.
        var sample = TemporalLabWarpTests.Sample(621);
        var frame = new FieldNavigationSource(new Empty(), _ => { })
            .Build(sample.Field, sample.Map, new FieldViewport(0, 5632, 4224, 8832), [], sample.Story);
        bool Listed(NavigationTarget t) => t.Visible || t.Discovered || t.GuideAvailable;
        Assert.DoesNotContain(frame.Targets, t => Listed(t) && t.ActorSlot is >= 9 and <= 22);
        Assert.DoesNotContain(frame.Targets, t => Listed(t) && t.Category == NavigationCategory.Enemies);
        var story = frame.Targets.Single(t => t.Id == "story:full:lavos");
        Assert.Equal("actor:8:4:231", NavigationPathfinder.Search(frame.Graph, frame.Player, story.ApproachPoints).IntermediateId);
    }

    [Theory]
    [InlineData("story:full:lavos", false)] [InlineData("story:full:lavos", true)]
    [InlineData("landmark:23", false)] [InlineData("landmark:23", true)]
    public void ArrivingAtTheClosedSecurityDoorSaysToPressConfirm(string id, bool automatic)
    {
        // DERIVED: the 622 return landing (42,12) with facing byte1, door marker drawn on camera.
        var sample = TemporalLabWarpTests.Sample(621);
        var lead = sample.Field.LeadPlayer! with { FineX = 10880, TileX = 42, FractionX = 128, FineY = 3327, TileY = 12, FractionY = 255, Facing = 1 };
        var field = sample.Field with { LeadPlayer = lead, Actors = sample.Field.Actors
            .Select(a => a.Index == lead.Index ? lead : a.Index == 23 ? a with { ActivationBinding = 128 } : a).ToArray() };
        var frame = TemporalLabWarpTests.Build(field, sample.Map, sample.Story);
        var target = frame.Targets.Single(t => t.Id == id);
        var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        for (var i = 0; i < 40 && controller.TargetId != id; i++) controller.Handle(NavigationCommand.NextTarget, frame, 0);
        var result = controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        var speech = new List<string>(result.Speech);
        for (var tick = 1; tick < 200 && result.Guiding; tick++)
        {
            var direction = automatic ? result.Direction : result.ManualLeg?.Direction ?? NavigationDirection.None;
            frame = frame with { Player = NativeWalkingReplay.Move(sample.Map, frame.Player, direction, 16),
                PlayerFacing = direction == NavigationDirection.None ? frame.PlayerFacing : direction };
            result = controller.Update(frame, tick * 16, !automatic && direction != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        // 5b3b71d ends with "Arrived at <label>." and no Confirm instruction.
        Assert.Contains(speech, s => s.StartsWith("Arrived at", StringComparison.Ordinal) && s.Contains("Confirm", StringComparison.Ordinal));
    }

    private static NavigationDirection Spoken(string? line) => line is null ? NavigationDirection.None :
        line.Contains("Continue up", StringComparison.Ordinal) ? NavigationDirection.North :
        line.Contains("Continue down", StringComparison.Ordinal) ? NavigationDirection.South :
        line.Contains("Continue left", StringComparison.Ordinal) ? NavigationDirection.West :
        line.Contains("Continue right", StringComparison.Ordinal) ? NavigationDirection.East : NavigationDirection.None;
    private sealed class Empty : IReadableMemory { public bool TryRead(nuint address, Span<byte> value) => false; }
}
