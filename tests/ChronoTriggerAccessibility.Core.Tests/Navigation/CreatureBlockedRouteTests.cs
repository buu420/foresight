using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

/// <summary>Magus's Keep stairways 176 and 446 (2026-10-12 tester log): a single wandering
/// enemy inside the stair corridor seals the live route, while the same terrain without
/// creatures still reaches the goal. Native contact (178980) blocks the player there, and
/// touching the creature starts a battle, so guidance waits instead of reporting that the
/// destination cannot be reached.</summary>
public sealed class CreatureBlockedRouteTests
{
    private static readonly NavigationPoint Start = new(0, 0, 1);
    private static readonly NavigationPoint Goal = new(256, 0, 1);
    private static readonly NavigationTarget Destination = new("exit", "Stairs", NavigationCategory.Exits, Goal, [Goal], true, true);

    [Fact]
    public void AWanderingCreatureInTheOnlyCorridorPausesWalkingUntilItMoves()
    {
        var controller = new NavigationController();
        var blocked = Frame(creatureX: 128);
        controller.Handle(NavigationCommand.NextCategory, blocked, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, blocked, 0);

        Assert.DoesNotContain(result.Speech, s => s.Contains("No route", StringComparison.Ordinal));
        Assert.Contains("Blue creature is blocking the way. Guidance continues when the way clears.", result.Speech);
        Assert.True(result.Guiding);
        Assert.True(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);

        // While it stays, nothing is repeated and no input is produced.
        for (var now = 100; now <= 1000; now += 100)
        {
            result = controller.Update(blocked, now);
            Assert.Empty(result.Speech);
            Assert.Equal(NavigationDirection.None, result.Direction);
            Assert.True(result.AutoWalking);
        }

        var clear = Frame(creatureX: null);
        result = controller.Update(clear, 1600);
        Assert.Contains("The way is clear.", result.Speech);
        Assert.Equal(NavigationDirection.East, result.Direction);
    }

    [Fact]
    public void ACreatureSteppingIntoTheRouteMidWalkDoesNotStopGuidance()
    {
        var controller = new NavigationController();
        var clear = Frame(creatureX: null);
        controller.Handle(NavigationCommand.NextCategory, clear, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, clear, 0);
        Assert.Equal(NavigationDirection.East, result.Direction);

        // The creature walks into the corridor and the next step toward the stairs is closed.
        var blocked = Frame(creatureX: 128, player: new(112, 0, 1));
        result = controller.Update(blocked, 600);
        Assert.DoesNotContain(result.Speech, s => s.Contains("stopped", StringComparison.Ordinal));
        Assert.Contains("Blue creature is blocking the way. Guidance continues when the way clears.", result.Speech);
        Assert.True(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
    }

    private const string Neutral = "The way is temporarily blocked. Guidance continues when it clears.";

    [Fact]
    public void AnUnlistedBlockerIsNeverCalledACreature()
    {
        var controller = new NavigationController();
        var blocked = Frame(creatureX: 128, listed: false);
        controller.Handle(NavigationCommand.NextCategory, blocked, 0);
        var result = controller.Handle(NavigationCommand.Guide, blocked, 0);
        Assert.Contains(Neutral, result.Speech);
        Assert.DoesNotContain(result.Speech, s => s.Contains("creature", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.Guiding);
    }

    [Fact]
    public void AHiddenBlockerIsNotRevealedByItsRememberedRow()
    {
        var controller = new NavigationController();
        var blocked = Frame(creatureX: 128, visible: false);
        controller.Handle(NavigationCommand.NextCategory, blocked, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, blocked, 0);
        Assert.Contains(Neutral, result.Speech);
        Assert.DoesNotContain(result.Speech, s => s.Contains("Blue creature", StringComparison.Ordinal));
    }

    [Fact]
    public void AVisibleBystanderNearTheCutIsNotBlamedForAnotherSlotsContact()
    {
        var controller = new NavigationController();
        var blocked = Frame(creatureX: 128, listed: false, bystander: true);
        controller.Handle(NavigationCommand.NextCategory, blocked, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, blocked, 0);
        Assert.Contains(Neutral, result.Speech);
        Assert.DoesNotContain(result.Speech, s => s.Contains("Villager", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutANativeContactSlotTheVisibleRowIsNotNamed()
    {
        var controller = new NavigationController();
        var blocked = Frame(creatureX: 128, contactSlots: false);
        controller.Handle(NavigationCommand.NextCategory, blocked, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, blocked, 0);
        Assert.Contains(Neutral, result.Speech);
    }

    [Fact]
    public void TerrainWithNoRouteEvenWithoutCreaturesStillReportsNoRoute()
    {
        var controller = new NavigationController();
        var walled = Frame(creatureX: 128, wall: true);
        controller.Handle(NavigationCommand.NextCategory, walled, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, walled, 0);
        Assert.Contains("No route to Stairs is available.", result.Speech);
        Assert.False(result.Guiding);
    }

    [Fact]
    public void StoppingWhileBlockedLeavesNothingPending()
    {
        var controller = new NavigationController();
        var blocked = Frame(creatureX: 128);
        controller.Handle(NavigationCommand.NextCategory, blocked, 0);
        controller.Handle(NavigationCommand.ToggleWalk, blocked, 0);
        var stopped = controller.Handle(NavigationCommand.ToggleWalk, blocked, 100);
        Assert.Contains("Navigation stopped.", stopped.Speech);
        var later = controller.Update(Frame(creatureX: null), 2000);
        Assert.Empty(later.Speech);
        Assert.False(later.Guiding);
    }

    private static NavigationFrame Frame(int? creatureX, NavigationPoint? player = null, bool listed = true, bool wall = false,
        bool visible = true, bool bystander = false, bool contactSlots = true)
    {
        var graph = new Row(creatureX, wall, contactSlots: contactSlots);
        var targets = new List<NavigationTarget> { Destination };
        if (creatureX is { } x && listed)
            targets.Add(new("actor:18", "Blue creature", NavigationCategory.Enemies, new(x, 0, 1), [new(x - 16, 0, 1)], visible, true)
                { ActorSlot = 18 });
        if (creatureX is { } y && bystander)
            targets.Add(new("actor:5", "Villager", NavigationCategory.People, new(y + 8, 0, 1), [new(y + 24, 0, 1)], true, true)
                { ActorSlot = 5 });
        return new NavigationFrame("keep-446", true, player ?? Start, targets, graph, 16);
    }

    /// <summary>A one-cell-high corridor of 16-unit steps. A creature closes the step into
    /// and out of its cell; a wall closes it for every graph.</summary>
    private sealed class Row(int? creatureX, bool wall, bool creaturesIgnored = false, bool contactSlots = true) : INavigationGraph
    {
        public INavigationGraph? WithoutCreatures => creaturesIgnored ? null : new Row(creatureX, wall, true, contactSlots);

        /// <summary>Native contact slot 18 closes the steps into and out of the creature's cell.</summary>
        public int? BlockingActor(NavigationPoint from, NavigationPoint to) =>
            contactSlots && !creaturesIgnored && creatureX is { } x && (to.X == x || from.X == x) ? 18 : null;

        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            foreach (var next in new[] { point with { X = point.X - 16 }, point with { X = point.X + 16 } })
            {
                if (next.X < 0 || next.X > 256) continue;
                if (wall && Math.Max(point.X, next.X) == 192) continue;
                if (!creaturesIgnored && creatureX is { } x && (next.X == x || point.X == x)) continue;
                yield return next;
            }
        }
    }
}
