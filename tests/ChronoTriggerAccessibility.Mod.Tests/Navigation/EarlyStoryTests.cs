using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class EarlyStoryTests
{
    [Theory]
    [InlineData(5, 3, "exit:1", "Continue to the rear plaza")]
    [InlineData(439, 3, "actor:3:3:1", "Approach the young woman")]
    [InlineData(8, 12, "actor:11:4:99", "Pick up Marle's pendant")]
    [InlineData(120, 15, "exit:3", "Visit the queen's chamber")]
    [InlineData(122, 16, "exit:0", "Return to the castle's main hall")]
    [InlineData(120, 18, "exit:0", "Leave to search for Queen Leene")]
    [InlineData(120, 28, "exit:3", "Return to the queen's chamber")]
    [InlineData(122, 33, "exit:0", "Return to the main hall")]
    [InlineData(113, 33, "actor:1:4:97", "Return through the Gate")]
    [InlineData(8, 39, "exit:0", "Leave the exhibit with Marle")]
    [InlineData(30, 46, "exit:0", "Leave the execution chamber")]
    [InlineData(21, 48, "exit:0", "Leave Guardia Castle")]
    [InlineData(19, 48, "exit:0", "Find a way out through the forest")]
    public void ChapterTargetsBindOnlyToObservedNativeRecords(int scene, int point, string id, string label)
    {
        var missing = Assert.Single(EarlyStoryTargets.Build(scene, State(point), [], default));
        Assert.True(missing.IsStoryNote);
        Assert.Empty(missing.ApproachPoints);
        var target = Target(id);
        var bound = Assert.Single(EarlyStoryTargets.Build(scene, State(point), [target], default));
        Assert.False(bound.IsStoryNote);
        Assert.Equal(label, bound.Label);
        Assert.Equal(target.ApproachPoints, bound.ApproachPoints);
    }

    [Fact]
    public void NativeFairFlagsAdvanceTheExhibitAndRetireThePendant()
    {
        var people = new[] { Target("actor:17:4:80"), Target("exit:1") };
        Assert.Equal("Ask about Lucca's exhibit", Assert.Single(EarlyStoryTargets.Build(5, State(8, 0x55, 0), people, default)).Label);
        Assert.Equal("Continue to the rear plaza", Assert.Single(EarlyStoryTargets.Build(5, State(8, 0x55, 0x80), people, default)).Label);
        var targets = new[] { Target("actor:3:3:1"), Target("actor:15:4:99") };
        Assert.Contains(EarlyStoryTargets.Build(439, State(6, 0x54, 0x10), targets, default), t => t.Label == "Pick up the fallen pendant");
        var pickedUp = EarlyStoryTargets.Build(439, State(6, 0x54, 0x20), targets, default);
        Assert.Equal("Return the pendant to the young woman", Assert.Single(pickedUp).Label);
    }

    [Fact]
    public void MissingOptionalFlagsDoNotInventAnUnfinishedAction()
    {
        var result = EarlyStoryTargets.Build(8, State(10), [Target("landmark:12"), Target("actor:14:3:1")], default);
        var note = Assert.Single(result);
        Assert.True(note.IsStoryNote);
        Assert.DoesNotContain("Try the left", note.Label);
        Assert.DoesNotContain(EarlyStoryTargets.Build(131, State(21), [Target("landmark:24"), Target("actor:36:4:100")], default),
            t => t.Label.StartsWith("Use ") || t.Label.StartsWith("Play "));
    }

    [Fact]
    public void CathedralPassageAndSwitchesFollowTheLiveBits()
    {
        var objects = new[] { Target("landmark:24"), Target("landmark:46"), Target("actor:36:4:100"), Target("exit:1") };
        var before = EarlyStoryTargets.Build(131, State(21, 0xFF, 0), objects, default);
        Assert.Contains(before, t => t.Label == "Use the right wall switch");
        Assert.Contains(before, t => t.Label == "Play the organ");
        var after = EarlyStoryTargets.Build(131, State(21, 0xFF, 14), objects, default);
        Assert.Equal("Continue through the passage", Assert.Single(after).Label);
        Assert.DoesNotContain(EarlyStoryTargets.Build(131, State(21, 0xFF, 0), [], default), t => t.Label.Contains("switch"));
    }

    [Fact]
    public void FirstTrialAndCellWaitAreNonSpatialAndEndWithTheArc()
    {
        Assert.True(Assert.Single(EarlyStoryTargets.Build(27, State(42), [], default)).IsStoryNote);
        Assert.Empty(EarlyStoryTargets.Build(438, State(42), [], default));
        Assert.Equal("Inside the prison cell", Assert.Single(EarlyStoryTargets.Build(71, State(45, 0x190, 0), [], default)).Label);
        Assert.DoesNotContain(EarlyStoryTargets.Build(71, State(45, 0x190, 4), [], default), t => t.Label == "Inside the prison cell");
        Assert.Empty(EarlyStoryTargets.Build(19, State(49), [], default));
        Assert.Empty(EarlyStoryTargets.Build(19, null, [], default));
    }

    [Fact]
    public void CurrentFloorTakesPriorityOverPreviouslyDiscoveredStairs()
    {
        var old = Target("exit:0") with { Visible = false, Position = new(64, 64, 1), ApproachPoints = [new(64, 64, 1)] };
        var current = Target("exit:1") with { Position = new(4096, 4096, 1), ApproachPoints = [new(4096, 4096, 1)] };
        var target = Assert.Single(EarlyStoryTargets.Build(468, State(15), [old, current], default));
        Assert.Equal(current.ApproachPoints, target.ApproachPoints);
        Assert.Equal(current.Position, target.Position);
    }

    [Theory]
    [InlineData(28, 0, 1)]
    [InlineData(28, 2, 3)]
    [InlineData(28, 4, 5)]
    [InlineData(28, 6, 7)]
    [InlineData(72, 0, 1)]
    [InlineData(72, 2, 5)]
    [InlineData(72, 6, 8)]
    [InlineData(72, 12, 11)]
    public void PrisonFloorsChooseTheExitThatContinuesUpward(int scene, int onward, int back)
    {
        var forward = Target($"exit:{onward}") with { Position = new(4096, 4096, 1), ApproachPoints = [new(4096, 4096, 1)] };
        var nearbyBack = Target($"exit:{back}");
        var objective = Assert.Single(EarlyStoryTargets.Build(scene, State(46), [nearbyBack, forward], default));
        Assert.Equal(forward.ApproachPoints, objective.ApproachPoints);
        Assert.Equal(forward.Position, objective.Position);
    }

    [Fact]
    public void NativeSceneryProvidesObjectsButUnseenSparklesStayHidden()
    {
        var organ = Actor(13, 736, 2048);
        var sparkTrigger = Actor(15, 2176, 2815);
        var sparkle = Actor(14, 2176, 2815) with { ClassTag = 4, VisualIndex = 112, DrawMode = 0 };
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(129, organ, sparkTrigger, sparkle);
        var hidden = source.Build(field, Map(), new(0, 0, 4096, 4096), [], State(18));
        Assert.Contains(hidden.Targets, t => t.Category == NavigationCategory.Objects && t.Label == "Organ");
        Assert.DoesNotContain(hidden.Targets, t => t.Label.Contains("sparkle", StringComparison.OrdinalIgnoreCase));
        var seen = source.Build(field with { Actors = [field.Actors[0], organ, sparkTrigger, sparkle with { DrawMode = 1 }] },
            Map(), new(0, 0, 4096, 4096), [], State(18));
        Assert.Contains(seen.Targets, t => t.Category == NavigationCategory.StoryEvents && t.Label == "Examine the sparkle on the floor");
        var hiddenAgain = source.Build(field, Map(), new(0, 0, 4096, 4096), [], State(18));
        Assert.DoesNotContain(hiddenAgain.Targets, t => t.Label.Contains("sparkle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FreedPrisonerParkedOutsideTheMapDoesNotLeaveAStaleObjective()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var prisoner = Actor(22, 11504, 2448) with { ClassTag = 4, VisualIndex = 41, DrawMode = 1 };
        var field = Field(72, prisoner);
        var before = source.Build(field, Map(), new(0, 0, 16384, 16384), [], State(46));
        Assert.Contains(before.Targets, t => t.Label == "Speak to the prisoner at the guillotine");
        // Native scene 72/22 parks Fritz at tile FF,FF after the rescue dialogue.
        var parked = Actor(22, 65535, 65535) with { ClassTag = 4, VisualIndex = 41, DrawMode = 1 };
        var after = source.Build(field with { Actors = [field.Actors[0], parked] }, Map(), new(0, 0, 16384, 16384), [], State(46));
        Assert.DoesNotContain(after.Targets, t => t.Id == "actor:22:4:41" || t.Label.Contains("prisoner"));
    }

    [Fact]
    public void GuardroomEncounterIsReachedBeforeTheUpperBridge()
    {
        var guard = Target("actor:10:5:49") with
        {
            Position = new(2176, 6143, 1),
            ApproachPoints = [new(2176, 5888, 1), new(1920, 6144, 1), new(2432, 6144, 1)],
        };
        var bridge = Target("exit:0");
        var before = Assert.Single(EarlyStoryTargets.Build(29, State(45), [guard, bridge], default));
        Assert.Equal("Approach the guards", before.Label);
        Assert.Equal(new[] { new NavigationPoint(2176, 5888, 1) }, before.ApproachPoints);
        var after = Assert.Single(EarlyStoryTargets.Build(29, State(46), [bridge], default));
        Assert.Equal("Continue onto the upper bridge", after.Label);
    }

    [Fact]
    public void UndiscoveredSceneryDoesNotBecomeARouteAndTouchMarkersStayOutOfObjects()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var pod = Actor(12, 1152, 1279) with { ActivationEnabled = 0 };
        var field = Field(8, pod);
        var state = State(10, 0x56, 1);
        var unseen = source.Build(field, Map(), new(0, 0, 256, 256), [], state);
        Assert.True(Assert.Single(unseen.Targets).IsStoryNote);
        var seen = source.Build(field, Map(), new(0, 0, 2048, 2048), [], state);
        var story = Assert.Single(seen.Targets);
        Assert.Equal(NavigationCategory.StoryEvents, story.Category);
        Assert.False(story.IsStoryNote);
        Assert.All(story.ApproachPoints, p => { Assert.Equal(4, p.X / 256); Assert.Equal(4, p.Y / 256); });
        Assert.NotEmpty(story.ApproachPoints);
        Assert.Empty(source.Build(field with { SceneIdCoherent = false }, Map(), new(0, 0, 2048, 2048), [], state).Targets);
    }

    private static FieldStoryState State(int point, int index = -1, int value = 0) => new(point, false)
    { Globals = index < 0 ? new Dictionary<int, int>() : new Dictionary<int, int> { [index] = value } };
    private static NavigationTarget Target(string id) => new(id, id, NavigationCategory.Objects, new(512, 512, 1), [new(256, 512, 1)], true, true);
    private static FieldActorSnapshot Actor(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 0, 0, 0, 7, 0, 1, 1, false, true, true, true);
    private static FieldNavigationSnapshot Field(int scene, params FieldActorSnapshot[] objects)
    {
        var lead = Actor(0, 128, 128) with { ClassTag = 2, DrawMode = 1, IsPartyMember = true };
        return new(0x1000, 0x4000, 0x2000, 0x20000, objects.Length + 1, scene, true, 1, 0, 0, 0, lead, [lead, .. objects]);
    }
    private static FieldMapSnapshot Map() => new(64, 64, new byte[4096], new byte[4096],
        Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
