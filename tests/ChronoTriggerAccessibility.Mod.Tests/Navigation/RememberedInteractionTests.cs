using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class RememberedInteractionTests
{
    [Theory]
    [InlineData("inactive")]
    [InlineData("moved")]
    [InlineData("contact")]
    public void RememberedGeometryDoesNotReuseThePreviousCapturesInteractionState(string change)
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var actor = Npc(8, 1152, 1152);
        var before = source.Build(Field(actor), FullGameNavigationTests.Map(), new(0, 0, 2048, 2048), []);
        var goal = new NavigationPoint(1152, 1408, 1);
        Assert.Contains(NavigationDirection.North, before.Targets.Single(t => t.Id == "actor:8:4:14").ConfirmFacings!(goal));

        var currentActor = change switch
        {
            "inactive" => actor with { ActivationBinding = 0 },
            "moved" => Npc(8, 1664, 1152) with { ActivationBinding = 0 },
            _ => actor,
        };
        var current = Field(currentActor);
        if (change == "contact") current = current with { ContactActorRaw = 18, ConfirmActorRaw = 18 };
        // The actor stays loaded, but leaves the camera. No guide supplies current geometry.
        var after = source.Build(current, FullGameNavigationTests.Map(), new(0, 0, 512, 512), []);
        var remembered = after.Targets.Single(t => t.Id == "actor:8:4:14");
        Assert.True(remembered.Discovered);
        Assert.False(remembered.Visible);
        Assert.Empty(remembered.ConfirmFacings!(goal));
        if (change != "moved") Assert.True(remembered.ConfirmPending!(goal));

        var controller = new NavigationController();
        var result = controller.Handle(NavigationCommand.ToggleWalk,
            after with { Player = goal, PlayerFacing = NavigationDirection.North }, 0);
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("Arrived"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATargetWhoseScriptCallsAreDisabledIsNotConfirmReady(bool contact)
    {
        var actor = Npc(8, 1152, 1152) with { ScriptCallsEnabled = false };
        var field = Field(actor);
        if (contact) field = field with { ContactActorRaw = 16, ConfirmActorRaw = 16 };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, FullGameNavigationTests.Map(), new(0, 0, 2048, 2048), []);
        var target = frame.Targets.Single(t => t.Id == "actor:8:4:14");
        var goal = new NavigationPoint(1152, 1408, 1);
        Assert.Equal(8, FieldInteractionRange.ConfirmWinner(field.Actors, 1, goal.X, goal.Y, NavigationDirection.North));
        Assert.Empty(target.ConfirmFacings!(goal));
        Assert.True(target.ConfirmPending!(goal));
    }

    [Fact]
    public void ADisabledHigherSlotStillShadowsAnotherTargetsConfirm()
    {
        var target = Npc(8, 1152, 1152);
        var blocker = Npc(9, 1152, 1152) with { ScriptCallsEnabled = false, VisualIndex = 15 };
        var field = Field(target, blocker);
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, FullGameNavigationTests.Map(), new(0, 0, 2048, 2048), []);
        var goal = new NavigationPoint(1152, 1408, 1);
        Assert.Equal(9, FieldInteractionRange.ConfirmWinner(field.Actors, 1, goal.X, goal.Y, NavigationDirection.North));
        Assert.Empty(frame.Targets.Single(t => t.Id == "actor:8:4:14").ConfirmFacings!(goal));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(7, false)]
    [InlineData(null, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(7, true)]
    [InlineData(null, true)]
    public void ReadinessRequiresTheWinningActorsLiveScriptPriority(int? priority, bool contact)
    {
        var actor = Npc(8, 1152, 1152) with { ScriptPriority = priority };
        var field = Field(actor);
        if (contact) field = field with { ContactActorRaw = 16, ConfirmActorRaw = 16 };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, FullGameNavigationTests.Map(), new(0, 0, 2048, 2048), []);
        var target = frame.Targets.Single(t => t.Id == "actor:8:4:14");
        var goal = new NavigationPoint(1152, 1408, 1);
        // A busy target still wins the scan; dispatch refuses to preempt priorities 0 or 1.
        Assert.Equal(8, FieldInteractionRange.ConfirmWinner(field.Actors, 1, goal.X, goal.Y, NavigationDirection.North));
        Assert.Equal(priority is > 1, target.ConfirmFacings!(goal).Count != 0);
        Assert.Equal(priority is not > 1, target.ConfirmPending!(goal));
    }

    [Fact]
    public void ABusyHigherSlotStillShadowsAnotherTargetsConfirm()
    {
        var target = Npc(8, 1152, 1152);
        var blocker = Npc(9, 1152, 1152) with { ScriptPriority = 1, VisualIndex = 15 };
        var field = Field(target, blocker);
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, FullGameNavigationTests.Map(), new(0, 0, 2048, 2048), []);
        var goal = new NavigationPoint(1152, 1408, 1);
        Assert.Equal(9, FieldInteractionRange.ConfirmWinner(field.Actors, 1, goal.X, goal.Y, NavigationDirection.North));
        Assert.Empty(frame.Targets.Single(t => t.Id == "actor:8:4:14").ConfirmFacings!(goal));
    }

    private static FieldActorSnapshot Npc(int index, int x, int y) =>
        FullGameNavigationTests.Actor(index, x, y) with { ClassTag = 4 };

    private static FieldNavigationSnapshot Field(params FieldActorSnapshot[] actors)
    {
        var player = FullGameNavigationTests.Actor(1, 384, 384, true) with { ClassTag = 0, Facing = 1 };
        return FullGameNavigationTests.Field(999) with
        {
            LeadPlayer = player, LeadPlayerActorIndex = 1, Actors = [player, .. actors],
        };
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
