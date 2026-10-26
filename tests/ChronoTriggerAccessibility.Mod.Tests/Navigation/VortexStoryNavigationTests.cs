using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VortexStoryNavigationTests
{
    [Theory]
    [InlineData(661, NavigationCommand.Guide)]
    [InlineData(661, NavigationCommand.ToggleWalk)]
    [InlineData(598, NavigationCommand.Guide)]
    [InlineData(598, NavigationCommand.ToggleWalk)]
    public void StoryEventsFollowsTheCurrentVortexPassageInsteadOfRoutingToLavos(int scene, NavigationCommand command)
    {
        var frame = Frame(scene);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        Assert.Equal(NavigationCategory.StoryEvents, target.Category);
        Assert.False(target.IsStoryNote);
        Assert.DoesNotContain("Lavos", target.Label);
        var local = Assert.Single(frame.Targets, t => scene == 661
            ? t.Id == "script-region:8:598:11:7:12:7" : t.Id == "landmark:8");
        Assert.Equal(local.ApproachPoints, target.ApproachPoints);
        var control = new NavigationController();
        control.Handle(NavigationCommand.PreviousCategory, frame, 0);
        control.Handle(NavigationCommand.PreviousCategory, frame, 0);
        var result = control.Handle(command, frame, 16);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.Contains(result.Speech, s => s.Contains(scene == 661 ? "Explore the Dimensional Vortex" : "shifting passages"));
        Assert.DoesNotContain(result.Speech, s => s.Contains("Lavos") || s.Contains("End of Time"));
    }

    [Theory]
    [InlineData(661, false)]
    [InlineData(661, true)]
    [InlineData(598, false)]
    [InlineData(598, true)]
    public void UnknownProgressOrMissingForwardControlDoesNotFallBackOutOfTheVortex(int scene, bool missing)
    {
        var frame = Frame(scene, missing: missing, unknown: !missing);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        Assert.True(target.IsStoryNote);
        Assert.Empty(target.ApproachPoints);
        Assert.DoesNotContain("Lavos", target.Label);
        foreach (var command in new[] { NavigationCommand.Guide, NavigationCommand.ToggleWalk })
        {
            var control = new NavigationController();
            control.Handle(NavigationCommand.PreviousCategory, frame, 0);
            control.Handle(NavigationCommand.PreviousCategory, frame, 0);
            var result = control.Handle(command, frame, 16);
            Assert.False(result.Guiding);
            Assert.False(result.AutoWalking);
            Assert.Equal(NavigationDirection.None, result.Direction);
        }
    }

    [Theory]
    [InlineData(496)]
    [InlineData(498)]
    [InlineData(502)]
    [InlineData(464)]
    [InlineData(659)]
    [InlineData(660)]
    public void WorldMapsTheHubAndGenoDomeKeepTheirExistingMainObjective(int scene)
    {
        var target = Assert.Single(FullStoryObjectives.Build(scene, State()), t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("full:lavos", target.Id);
        Assert.Equal("Face Lavos when you are ready", target.Label);
    }

    private static NavigationFrame Frame(int scene, bool missing = false, bool unknown = false)
    {
        // Poses and actor identities come from log lines1061/1121. The floor,
        // partial script state and actor storage below are synthetic. E20=80
        // selects the native entry661->598 and598->599 branches in the catalog.
        var player = FullGameNavigationTests.Actor(1, scene == 661 ? 2949 : 8320, scene == 661 ? 4863 : 16127, true);
        var marker = FullGameNavigationTests.Actor(8, scene == 661 ? 65280 : 8320, scene == 661 ? 65280 : 6399)
            with { ClassTag = 7, VisualIndex = 0, LoadedFlag = 0, ActivationEnabled = 0, ActivationBinding = 0 };
        var portal = FullGameNavigationTests.Actor(9, 2944, 7167)
            with { ClassTag = 4, VisualIndex = 193, LoadedFlag = 0, ActivationEnabled = 0, ActivationBinding = 128 };
        FieldActorSnapshot[] actors = missing ? [player, portal] : scene == 661 ? [player, marker, portal] : [player, marker];
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = actors };
        var map = new FieldMapSnapshot(64, 64, new byte[4096], new byte[4096],
            Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64,
            Enumerable.Repeat((byte)128, 4096).ToArray());
        var state = State();
        if (unknown) state = state with { Extended = new Dictionary<int, int> { [0x20] = 0x80, [0x22] = 0 } };
        return new FieldNavigationSource(new EmptyMemory(), _ => { }).Build(field, map,
            new(0, 0, 16384, 16384), [], state);
    }

    private static FieldStoryState State() => new(214, true)
    {
        Globals = new Dictionary<int, int> { [0x1FB] = 0x45 },
        Extended = new Dictionary<int, int> { [0x0A] = 0, [0x20] = 0x80, [0x22] = 0, [0x28] = 0 },
    };

    private sealed class EmptyMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
