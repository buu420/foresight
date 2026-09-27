using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

/// <summary>The game's confirm handler only tests an actor on the side the leader faces. A
/// walk that ends beside a shopkeeper facing along the route is in range but cannot talk to
/// them. 0.3.37 log: the walk to the Armour seller ended at (3680,9855) after a final leg to
/// the right. The log does not record the Confirm press itself; that Confirm could not reach
/// the seller from there facing right is inferred from the native geometry (17D8B0 needs the
/// seller within 0xC0 vertically; the gap is 319). Arrival therefore includes the facing.</summary>
public sealed class ConfirmFacingTests
{
    [Fact]
    public void AutomaticWalkTurnsTowardTheSellerWithTheDirectionPadBeforeArriving()
    {
        var controller = new NavigationController();
        var start = controller.Handle(NavigationCommand.ToggleWalk, Frame(new(256, 256, 1), NavigationDirection.East), 0);
        Assert.Equal(NavigationDirection.East, start.Direction);
        var there = controller.Update(Frame(Goal, NavigationDirection.East), 100);
        Assert.True(there.AutoWalking);
        Assert.Equal(NavigationDirection.North, there.Direction);
        Assert.DoesNotContain(there.Speech, s => s.StartsWith("Arrived"));
        var facing = controller.Update(Frame(Goal with { Y = 250 }, NavigationDirection.North), 150);
        Assert.False(facing.AutoWalking);
        Assert.Equal(NavigationDirection.None, facing.Direction);
        Assert.Contains("Arrived at Armour seller.", facing.Speech);
    }

    [Fact]
    public void AlreadyFacingTheSellerArrivesWithoutAnExtraPress()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame(new(256, 256, 1), NavigationDirection.East), 0);
        var there = controller.Update(Frame(Goal, NavigationDirection.North), 100);
        Assert.False(there.AutoWalking);
        Assert.Equal(NavigationDirection.None, there.Direction);
        Assert.Contains("Arrived at Armour seller.", there.Speech);
    }

    [Fact]
    public void AFailedTurnIsReportedInsteadOfAnArrival()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame(new(256, 256, 1), NavigationDirection.East), 0);
        controller.Update(Frame(Goal, NavigationDirection.East), 100);
        var stopped = controller.Update(Frame(Goal, NavigationDirection.East), 1700);
        Assert.False(stopped.AutoWalking);
        Assert.Equal(NavigationDirection.None, stopped.Direction);
        Assert.DoesNotContain(stopped.Speech, s => s.StartsWith("Arrived"));
        Assert.Contains(stopped.Speech, s => s.Contains("could not turn to face Armour seller"));
    }

    [Fact]
    public void ManualGuidanceGivesTheFinalFacingAndOnlyThenConfirmsIt()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.Guide, Frame(new(256, 256, 1), NavigationDirection.East), 0);
        var there = controller.Update(Frame(Goal, NavigationDirection.East), 100);
        Assert.True(there.Guiding);
        Assert.Equal(NavigationDirection.None, there.Direction);
        Assert.Null(there.ManualLeg);
        Assert.DoesNotContain(there.Speech, s => s.StartsWith("Arrived"));
        Assert.Contains(there.Speech, s => s.Contains("Turn up to face Armour seller"));
        var repeat = controller.Handle(NavigationCommand.Repeat, Frame(Goal, NavigationDirection.East), 200);
        Assert.Contains(repeat.Speech, s => s.Contains("within reach") && s.Contains("face up"));
        var done = controller.Update(Frame(Goal, NavigationDirection.North), 5000, manualInput: true);
        Assert.False(done.Guiding);
        Assert.Contains("Facing it. Arrived at Armour seller. Press Confirm.", done.Speech);
    }

    [Fact]
    public void AnUnreadableFacingNeverClaimsTheInteractionIsReady()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame(new(256, 256, 1), NavigationDirection.None), 0);
        var there = controller.Update(Frame(Goal, NavigationDirection.None), 100);
        Assert.False(there.AutoWalking);
        Assert.Equal(NavigationDirection.None, there.Direction);
        Assert.DoesNotContain(there.Speech, s => s.Contains("Arrived"));
        Assert.Contains("Next to Armour seller, but the facing could not be read. Face up, then press Confirm.", there.Speech);
    }

    [Fact]
    public void AnActorThatWalkedOutOfReachIsNotAnnouncedAsArrived()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame(new(256, 256, 1), NavigationDirection.East), 0);
        // The seller has stepped away since the plan; this point no longer reaches anyone.
        var gone = Frame(Goal, NavigationDirection.East, reach: _ => []);
        var there = controller.Update(gone, 100);
        Assert.DoesNotContain(there.Speech, s => s.StartsWith("Arrived"));
        Assert.False(there.AutoWalking);
        Assert.Contains(there.Speech, s => s.Contains("out of reach"));
    }

    [Fact]
    public void AGoalThatStoppedReachingIsDroppedAndTheWalkContinuesToAnotherLiveGoal()
    {
        // Two standing points; the nearer one has lost its reach (for example another actor
        // now takes Confirm there). The walk replans to the other instead of stopping.
        var far = new NavigationPoint(768, 256, 1);
        IReadOnlyList<NavigationDirection> Only(NavigationPoint p) =>
            Math.Abs(p.X - far.X) <= 32 ? [NavigationDirection.North] : [];
        var controller = new NavigationController();
        var frame = Frame(new(256, 256, 1), NavigationDirection.East, reach: Only, goals: [Goal, far]);
        var start = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.Contains(start.Speech, s => s.Contains("Right 1 step"));
        var there = controller.Update(frame with { Player = Goal }, 100);
        Assert.True(there.AutoWalking);
        Assert.Equal(NavigationDirection.East, there.Direction);
        Assert.DoesNotContain(there.Speech, s => s.StartsWith("Arrived") || s.Contains("stopped"));
        var turning = controller.Update(frame with { Player = far }, 200);
        Assert.Equal(NavigationDirection.North, turning.Direction);
        Assert.Contains("Arrived at Armour seller.", controller.Update(frame with { Player = far, PlayerFacing = NavigationDirection.North }, 250).Speech);
    }

    [Fact]
    public void MixedAlternativesKeepTheFacingOnlyForTheGoalThatNeedsIt()
    {
        var door = new NavigationPoint(768, 256, 1);
        var mixed = Frame(new(256, 256, 1), NavigationDirection.East, goals: [Goal, door]);
        var target = mixed.Targets[0] with
        {
            ConfirmFacings = null,
            ApproachConfirms = new Dictionary<NavigationPoint, Func<NavigationPoint, IReadOnlyList<NavigationDirection>>> { [Goal] = Reach },
        };
        Assert.NotNull(target.ConfirmAt(Goal));
        Assert.Null(target.ConfirmAt(door));

        // The seller's goal still needs the facing.
        var controller = new NavigationController();
        var toSeller = mixed with { Targets = [target] };
        controller.Handle(NavigationCommand.ToggleWalk, toSeller, 0);
        var atSeller = controller.Update(toSeller with { Player = Goal }, 100);
        Assert.DoesNotContain(atSeller.Speech, s => s.StartsWith("Arrived"));
        Assert.Equal(NavigationDirection.North, atSeller.Direction);

        // The door's goal is an ordinary arrival.
        var toDoor = toSeller with { Targets = [target with { ApproachPoints = [door] }] };
        controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, toDoor, 0);
        Assert.Contains("Arrived at Armour seller.", controller.Update(toDoor with { Player = door }, 100).Speech);
    }

    [Fact]
    public void ADestinationWithoutConfirmKeepsTheOrdinaryArrival()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame(new(256, 256, 1), NavigationDirection.East), 0);
        var frame = Frame(Goal, NavigationDirection.East);
        var exit = frame with { Targets = [frame.Targets[0] with { ConfirmFacings = null }] };
        var there = controller.Update(exit, 100);
        Assert.Contains("Arrived at Armour seller.", there.Speech);
    }

    [Fact]
    public void SelectionDescribesReachAndFacingInsteadOfAMisleadingStepCount()
    {
        var controller = new NavigationController();
        var result = controller.Handle(NavigationCommand.Repeat, Frame(Goal, NavigationDirection.North), 0);
        Assert.Contains(result.Speech, s => s.Contains("within reach, facing it"));
    }

    // The seller stands above the end of an east-west aisle, as in the Millennial Fair.
    private static readonly NavigationPoint Goal = new(512, 256, 1);
    private static readonly NavigationPoint Seller = new(512, 0, 1);

    private static IReadOnlyList<NavigationDirection> Reach(NavigationPoint p) =>
        Math.Abs(p.X - Seller.X) < 192 && p.Y - Seller.Y is >= 1 and < 448 ? [NavigationDirection.North] : [];

    private static NavigationFrame Frame(NavigationPoint player, NavigationDirection facing,
        Func<NavigationPoint, IReadOnlyList<NavigationDirection>>? reach = null, NavigationPoint[]? goals = null) =>
        new("fair", true, player,
            [new("seller", "Armour seller", NavigationCategory.People, Seller, goals ?? [Goal], true, true)
                { ConfirmFacings = reach ?? Reach }], new Aisle(), 256) { PlayerFacing = facing };

    private sealed class Aisle : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            if (point.X < 1024) yield return point with { X = point.X + 64 };
            if (point.X > 0) yield return point with { X = point.X - 64 };
        }
    }
}
