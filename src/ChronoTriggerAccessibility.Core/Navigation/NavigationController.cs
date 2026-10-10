namespace ChronoTriggerAccessibility.Core.Navigation;

public sealed class NavigationController
{
    private string? scene;
    private string? selection;
    private NavigationCategory category;
    private NavigationTarget? destination;
    private IReadOnlyList<NavigationPoint>? route;
    private IReadOnlyList<NavigationDirection>? inputDirections;
    private int nextPoint;
    private bool guiding;
    private bool walking;
    private NavigationPoint lastPosition;
    private long lastProgress;
    private NavigationDirection announcedDirection;
    private int unitsPerTile = 16;
    private long planRevision;
    private bool waitingForManualStop;
    private NavigationPoint observedPosition;
    private long lastManualActivity;
    private long instructionRevision;
    private int manualLegBeats;
    private string? intermediateId;
    private NavigationPoint? intermediateContact;
    private NavigationTransition? transition;
    private long? transitionContactStarted;
    private long? nativeTransitionStarted;
    private bool transitionLanded;
    private IReadOnlyList<NavigationPoint> plannedGoals = [];
    private long? passageWaitStarted;
    private long nextPassageCheck;
    private long? contactStarted;
    private NavigationPoint contactOrigin;
    private NavigationDirection contactDirection;
    private long? facingStarted;
    private NavigationDirection facingDirection;
    private readonly HashSet<NavigationPoint> unreachableGoals = [];
    private readonly HashSet<(int X, int Y, int Layer)> stallTiles = [];
    private long? pendingStarted;
    private bool chaseInReach;
    private long? lastChaseCue;
    // Survives Stop(), which a failed plan performs before anyone can read the
    // destination back. Without it the one case this field explains reports zero.
    private int plannedApproaches;

    public bool IsActive => guiding;
    public bool HasPendingTransition => guiding && transition is not null;
    /// <summary>Only the route's audited script may own a temporary input pause.
    /// A fresh landing may finish it; ordinary input gaps remain cancellations.</summary>
    public bool CanContinueTransition(NavigationFrame frame, long now) => HasPendingTransition && scene == frame.Scene &&
        now >= (nativeTransitionStarted ?? (walking ? transitionContactStarted ?? lastProgress : lastManualActivity)) &&
        now - (nativeTransitionStarted ?? (walking ? transitionContactStarted ?? lastProgress : lastManualActivity)) < 20000 &&
        (frame.ActiveTransitions.Contains(transition!.Id) && (nativeTransitionStarted is not null || NearTransition(observedPosition)) ||
         frame.CanNavigate && (transitionLanded || transition!.HasLanded(frame.Player)) &&
         (nativeTransitionStarted is not null || transitionContactStarted is not null || NearTransition(observedPosition)));
    private bool NearTransition(NavigationPoint point) => transition is not null && point.Layer == transition.Approach.Layer &&
        Math.Abs((long)point.X - transition.Approach.X) <= unitsPerTile && Math.Abs((long)point.Y - transition.Approach.Y) <= unitsPerTile;
    public bool AllowsConfirmWhileFollowing(NavigationFrame frame) => walking &&
        destination?.FollowUntilInteraction == true && scene == frame.Scene && frame.CanNavigate &&
        Eligible(frame).Any(t => t.Id == destination.Id && t.FollowUntilInteraction && !t.IsStoryNote);
    public string CurrentCategoryLabel => CategoryName(category);
    public string DiagnosticState => $"target={destination?.Id ?? selection ?? "none"}; plan={planRevision}; " +
        $"waypoint={nextPoint}/{route?.Count ?? 0}; next={PointText(route is not null && nextPoint < route.Count ? route[nextPoint] : null)}; " +
        $"goal={PointText(route is { Count: > 0 } ? route[^1] : null)}; approaches={plannedApproaches}; stage={intermediateId ?? "none"}" +
        (facingStarted is null ? "" : $"; facing={facingDirection}");
    private bool TakesAnExit => intermediateId?.StartsWith("exit:", StringComparison.Ordinal) == true;
    private static string PointText(NavigationPoint? point) => point is { } p ? $"({p.X},{p.Y},{p.Layer})" : "none";

    public NavigationResult Cancel(string reason)
    {
        var speech = new List<string>();
        if (guiding) speech.Add($"Navigation stopped: {reason}.");
        Stop();
        return Result(speech);
    }

    public NavigationResult Handle(NavigationCommand command, NavigationFrame frame, long nowMilliseconds)
    {
        var speech = new List<string>();
        Refresh(frame, speech, nowMilliseconds);
        if (!frame.CanNavigate) return Result(speech);
        var targets = Eligible(frame);
        switch (command)
        {
            case NavigationCommand.PreviousCategory:
            case NavigationCommand.NextCategory:
                Stop();
                var categoryCount = Enum.GetValues<NavigationCategory>().Length;
                category = (NavigationCategory)(((int)category + (command == NavigationCommand.NextCategory ? 1 : categoryCount - 1)) % categoryCount);
                selection = null;
                targets = Eligible(frame);
                speech.Add(CategoryName(category) + ".");
                SelectDefault(targets);
                DescribeSelection(frame, targets, speech);
                break;
            case NavigationCommand.PreviousTarget:
            case NavigationCommand.NextTarget:
                Stop();
                SelectDefault(targets);
                if (targets.Count != 0)
                {
                    var index = targets.FindIndex(target => target.Id == selection);
                    index = (index + (command == NavigationCommand.NextTarget ? 1 : targets.Count - 1)) % targets.Count;
                    selection = targets[index].Id;
                }
                DescribeSelection(frame, targets, speech);
                break;
            case NavigationCommand.Repeat:
                SelectDefault(targets);
                DescribeSelection(frame, targets, speech);
                break;
            case NavigationCommand.Guide:
            case NavigationCommand.ToggleWalk:
                if (command == NavigationCommand.ToggleWalk && walking)
                {
                    Stop();
                    speech.Add("Navigation stopped.");
                    break;
                }
                SelectDefault(targets);
                destination = targets.Find(target => target.Id == selection);
                if (destination is null)
                {
                    speech.Add(EmptyCategory());
                    break;
                }
                var target = destination;
                Stop();
                destination = target;
                if (!Plan(frame, speech)) break;
                guiding = true;
                walking = command == NavigationCommand.ToggleWalk;
                lastPosition = frame.Player;
                lastProgress = nowMilliseconds;
                observedPosition = frame.Player;
                lastManualActivity = nowMilliseconds;
                speech.Add($"{(walking ? "Walking to" : "Guidance to")} {target.Label}." +
                    (target.FollowUntilInteraction ? " You can hold Confirm while chasing." : ""));
                Follow(frame, nowMilliseconds, speech);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
        return Result(speech, frame.Player);
    }

    public NavigationResult Update(NavigationFrame frame, long nowMilliseconds, bool manualInput = false)
    {
        var speech = new List<string>();
        Refresh(frame, speech, nowMilliseconds);
        if (walking && manualInput)
        {
            Stop();
            speech.Add("Navigation stopped: manual control.");
        }
        if (guiding) Follow(frame, nowMilliseconds, speech, manualInput);
        return Result(speech, frame.Player);
    }

    private static string Arrival(NavigationTarget target) => $"Arrived at {target.Label}." +
        (string.IsNullOrWhiteSpace(target.ArrivalInstruction) ? "" : " " + target.ArrivalInstruction);

    private void Refresh(NavigationFrame frame, List<string> speech, long now)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.UnitsPerTile is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(frame));
        unitsPerTile = frame.UnitsPerTile;
        if (scene != frame.Scene)
        {
            if (guiding) speech.Add("Navigation stopped: area changed.");
            Stop();
            selection = null;
            scene = frame.Scene;
        }
        if (!frame.CanNavigate && guiding && !CanContinueTransition(frame, now))
        {
            Stop();
            speech.Add("Navigation stopped: player control is unavailable.");
        }
        if (guiding && !CanContinueTransition(frame, now) && !Eligible(frame).Any(target => target.Id == destination?.Id))
        {
            Stop();
            speech.Add("Navigation stopped: destination is no longer available.");
        }
    }

    private List<NavigationTarget> Eligible(NavigationFrame frame) => frame.Targets
        .Where(target => target.Category == category && (target.Visible || target.Discovered || target.GuideAvailable ||
            (target.Category == NavigationCategory.StoryEvents && target.IsStoryNote)) &&
            !string.IsNullOrWhiteSpace(target.Id) && !string.IsNullOrWhiteSpace(target.Label))
        .DistinctBy(target => target.Id).ToList();

    private void SelectDefault(List<NavigationTarget> targets)
    {
        if (!targets.Any(target => target.Id == selection)) selection = targets.FirstOrDefault()?.Id;
    }

    private void DescribeSelection(NavigationFrame frame, List<NavigationTarget> targets, List<string> speech)
    {
        var index = targets.FindIndex(target => target.Id == selection);
        if (index < 0) { speech.Add(EmptyCategory()); return; }
        var target = targets[index];
        if (nativeTransitionStarted is not null)
        {
            speech.Add($"{transition!.Label}. Waiting for the drop to finish.");
            return;
        }
        if (transitionContactStarted is not null)
        {
            speech.Add(TransitionInstruction());
            return;
        }
        if (contactStarted is not null)
        {
            speech.Add(target.Label + ". " + ContactInstruction());
            return;
        }
        if (waitingForManualStop)
        {
            speech.Add($"{target.Label}, {index + 1} of {targets.Count}. Stop moving for new directions.");
            return;
        }
        if (target.IsStoryNote)
        {
            speech.Add($"{target.Label}, {index + 1} of {targets.Count}. {target.Instruction}");
            return;
        }
        if (guiding && route is not null && nextPoint < route.Count)
        {
            speech.Add($"{target.Label}, {index + 1} of {targets.Count}. {DescribeRoute(frame.Player, walking ? 3 : 1)}");
            return;
        }
        var direction = Bearing(frame.Player, target.Position);
        var distance = Math.Ceiling(Math.Sqrt(SquaredDistance(frame.Player, target.Position)) / unitsPerTile);
        // Within Confirm's reach the step count misleads (the reach spans nearly two steps);
        // what still matters is which way to face.
        var reach = target.AnyConfirmFacings(frame.Player);
        var location = reach.Count != 0 ? reach.Contains(frame.PlayerFacing) ? "within reach, facing it" :
                $"within reach, face {DirectionName(reach[0])} to use Confirm" :
            frame.Player.Layer != target.Position.Layer ? "on another level" :
            direction == NavigationDirection.None ? "here" : $"{DirectionName(direction)}, {distance:0} {(distance == 1 ? "step" : "steps")} away";
        speech.Add($"{target.Label}, {index + 1} of {targets.Count}, {location}.");
        if (!string.IsNullOrWhiteSpace(target.Instruction)) speech.Add(target.Instruction);
    }

    private bool Plan(NavigationFrame frame, List<string> speech)
    {
        contactStarted = null;
        contactDirection = NavigationDirection.None;
        facingStarted = null;
        facingDirection = NavigationDirection.None;
        pendingStarted = null;
        if (destination!.IsStoryNote)
        {
            speech.Add($"{destination.Label}. {destination.Instruction}");
            Stop();
            return false;
        }
        if (!string.IsNullOrWhiteSpace(destination.GuidanceRestriction))
        {
            speech.Add(destination.GuidanceRestriction);
            Stop();
            return false;
        }
        // A goal proven not to reach its Confirm target during this guidance stays excluded.
        var goals = destination!.ApproachPoints.Where(p => !unreachableGoals.Contains(p)).ToArray();
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, goals);
        planRevision++;
        plannedApproaches = goals.Length;
        route = search.Route;
        inputDirections = search.InputDirections;
        var nextTransition = search.Transition ??
            (search.IntermediateId is null || search.IntermediateId == destination.Transition?.Id ? destination.Transition : null);
        if (transition?.Id != nextTransition?.Id)
        {
            transitionContactStarted = nativeTransitionStarted = null;
            transitionLanded = false;
        }
        transition = nextTransition;
        var nextIntermediate = search.IntermediateId ?? transition?.Id;
        if (intermediateId != nextIntermediate) passageWaitStarted = null;
        intermediateId = nextIntermediate;
        intermediateContact = search.IntermediateContact;
        plannedGoals = goals;
        nextPoint = 1;
        if (route is not null) return true;
        speech.Add(search.LimitReached ? "The route search limit was reached. Try a closer destination." : $"No route to {destination.Label} is available.");
        Stop();
        return false;
    }

    private void Follow(NavigationFrame frame, long now, List<string> speech, bool manualInput = false)
    {
        // A keyboard-only player has no callback during native movement. Check
        // the prior approach before replacing it with the first landed position.
        var continueTransition = CanContinueTransition(frame, now);
        if (frame.CanNavigate)
        {
            if (!walking && (manualInput || frame.Player != observedPosition || now < lastManualActivity))
                lastManualActivity = now;
            observedPosition = frame.Player;
        }
        if (FollowTransition(frame, now, speech, continueTransition)) return;
        var currentTarget = Eligible(frame).Find(target => target.Id == destination?.Id);
        if (currentTarget is null)
        {
            speech.Add("Navigation stopped: destination is no longer available.");
            Stop();
            return;
        }
        // Rejected standing points belong to the actor's previous position, not to a
        // moving chase for its whole lifetime.
        if (currentTarget.FollowUntilInteraction && currentTarget.Position != destination?.Position)
            unreachableGoals.Clear();
        // The chosen approach remains valid when a wide exit exposes additional
        // cells or their order changes. Do not restart speech for alternative goals.
        var leftContact = passageWaitStarted is not null && route is not null && !Arrived(frame.Graph, frame.Player, route[^1]);
        var mustReplan = leftContact || route is null || (intermediateId is null
            ? !currentTarget.ApproachPoints.Contains(route[^1])
            : !plannedGoals.Any(currentTarget.ApproachPoints.Contains));
        destination = currentTarget;
        if (destination.FollowUntilInteraction)
        {
            var ready = frame.PlayerFacing != NavigationDirection.None &&
                destination.AnyConfirmFacings(frame.Player).Contains(frame.PlayerFacing);
            if (ready && !chaseInReach && (lastChaseCue is null || now < lastChaseCue || now - lastChaseCue >= 1000))
            {
                speech.Add("In reach. Press Confirm, or keep it held.");
                lastChaseCue = now;
            }
            chaseInReach = ready;
        }
        frame = frame with { Graph = frame.Graph.ForGoals(currentTarget.ApproachPoints) };
        if (mustReplan)
        {
            if (contactStarted is not null)
            {
                lastProgress = now;
                lastPosition = frame.Player;
                announcedDirection = NavigationDirection.None;
            }
            if (leftContact)
            {
                passageWaitStarted = null;
                announcedDirection = NavigationDirection.None;
                lastProgress = now;
                lastPosition = frame.Player;
            }
            if (!Plan(frame, speech)) return;
        }
        if (route is null) return;
        if (intermediateId is not null && Arrived(frame.Graph, frame.Player, route[^1]))
        {
            nextPoint = route.Count;
            if (passageWaitStarted is null)
            {
                passageWaitStarted = now;
                nextPassageCheck = now;
                var contactLabel = frame.Targets.FirstOrDefault(t => t.Id == intermediateId)?.Label;
                // A passage opens where the player is standing; a way out has to be
                // walked through. Telling someone to wait at a door strands them.
                speech.Add(intermediateContact is { } touch
                    ? $"Continue {DirectionName(ContactBearing(frame.Player, touch))} until the passage opens."
                    : TakesAnExit
                    ? contactLabel is null ? "Take the exit here to continue." : $"Take {contactLabel} to continue."
                    : contactLabel is null ? "Waiting for the passage to open." : $"Waiting for {contactLabel} to open.");
            }
            if (now < nextPassageCheck) return;
            var waitingSince = passageWaitStarted.Value;
            if (!Plan(frame, speech)) return;
            if (intermediateId is not null && Arrived(frame.Graph, frame.Player, route![^1]))
            {
                nextPoint = route.Count;
                if (now < waitingSince || now - waitingSince >= 3000)
                {
                    speech.Add(TakesAnExit ? "Navigation stopped: the exit was not taken."
                        : "Navigation stopped: the passage did not open.");
                    Stop();
                }
                else nextPassageCheck = now + 250;
                return;
            }
            passageWaitStarted = null;
            announcedDirection = NavigationDirection.None;
            lastProgress = now;
            lastPosition = frame.Player;
        }
        if (contactStarted is not null || (destination.ContactDirection != NavigationDirection.None || destination.ContactAt(route[^1]) is not null) &&
            Arrived(frame.Graph, frame.Player, route[^1]))
        {
            nextPoint = route.Count;
            if (contactStarted is null)
            {
                contactStarted = now;
                contactOrigin = frame.Player;
                contactDirection = destination.ContactAt(route[^1]) is { } center
                    ? ContactBearing(frame.Player, center) : destination.ContactDirection;
                speech.Add(ContactInstruction());
            }
            else if (frame.Player.Layer != contactOrigin.Layer ||
                SquaredDistance(frame.Player, contactOrigin) > Math.Pow(unitsPerTile / 2.0, 2) ||
                walking && (now < contactStarted.Value || now - contactStarted.Value >= 1500))
            {
                speech.Add("Navigation stopped: the contact did not register.");
                Stop();
            }
            return;
        }
        if (destination.ConfirmAt(route[^1]) is not null &&
            (facingStarted is not null || Arrived(frame.Graph, frame.Player, route[^1])))
        {
            nextPoint = route.Count;
            FinishFacing(frame, now, speech);
            return;
        }
        if (Arrived(frame.Graph, frame.Player, route[^1]))
        {
            speech.Add(Arrival(destination));
            Stop();
            return;
        }
        var onRoute = AdvanceAlongRoute(frame.Player);
        if (nextPoint >= route.Count)
        {
            // A cell goal is reached by entering the cell, not by closing the distance,
            // so the cursor can pass the last waypoint while the player is still outside
            // it. Keep steering at that node rather than stalling on an exhausted route.
            if (route.Count < 2 || !frame.Graph.IsTerminal(route[^1])) return;
            nextPoint = route.Count - 1;
        }
        // Progress along a leg can pass multiple small graph waypoints. Only a
        // departure from the actual route or changed terrain requires a new plan.
        var previous = route[Math.Max(0, nextPoint - 1)];
        // A held Dash covers 32 units a frame, but the graph proves legs at the 16-unit
        // walking step, so a dash can run beside a proven line into a wall. Replan once
        // per tile from the real position; a stall that persists still stops below.
        // In guidance only a held press toward the spoken leg that does not move the
        // player is a stall; the leader still turns to face a refused direction.
        if (!walking && (!manualInput || Progressed(frame.Player)))
        {
            lastPosition = frame.Player;
            lastProgress = now;
        }
        var stalled = (walking || manualInput && frame.PlayerFacing == LegDirection(nextPoint)) &&
            now >= lastProgress && now - lastProgress >= 250 && !Progressed(frame.Player) &&
            stallTiles.Add((frame.Player.X / unitsPerTile, frame.Player.Y / unitsPerTile, frame.Player.Layer));
        if (!onRoute || stalled ||
            !frame.Graph.Neighbours(previous).Contains(route[nextPoint]))
        {
            // Human movement continues while a spoken turn finishes. Replanning
            // each animation tick creates alternating quarter-step corrections.
            // Wait for both key release and the native step to settle first. A
            // stalled press has already settled.
            if (!walking && !stalled && now - lastManualActivity < 200)
            {
                if (!waitingForManualStop)
                {
                    speech.Add("Off route. Stop moving for new directions.");
                    waitingForManualStop = true;
                }
                return;
            }
            if (!Plan(frame, speech)) return;
            if (!walking)
            {
                waitingForManualStop = false;
                announcedDirection = NavigationDirection.None;
                speech.Add("Route updated.");
            }
            if (route!.Count == 1)
            {
                if (intermediateId is not null || destination!.ContactDirection != NavigationDirection.None ||
                    destination.ContactAt(route[^1]) is not null || destination.ConfirmAt(route[^1]) is not null) return;
                speech.Add(Arrival(destination!));
                Stop();
                return;
            }
        }
        else if (waitingForManualStop)
        {
            waitingForManualStop = false;
            speech.Add(DescribeRoute(frame.Player, 1));
            announcedDirection = LegDirection(nextPoint);
        }
        if (walking)
        {
            if (Progressed(frame.Player))
            {
                lastPosition = frame.Player;
                lastProgress = now;
            }
            else if (now < lastProgress || now - lastProgress >= 1500)
            {
                speech.Add("Navigation stopped: movement is blocked.");
                Stop();
                return;
            }
        }
        var direction = LegDirection(nextPoint);
        if (direction != NavigationDirection.None && direction != announcedDirection &&
            !(walking && destination.FollowUntilInteraction))
        {
            // Manual guidance speaks only the leg the player should follow now.
            speech.Add(DescribeRoute(frame.Player, walking && announcedDirection == NavigationDirection.None ? 3 : 1));
            announcedDirection = direction;
        }
    }

    private bool FollowTransition(NavigationFrame frame, long now, List<string> speech, bool owned)
    {
        if (transition is null) return false;
        if (owned && frame.CanNavigate && transition.HasLanded(frame.Player)) transitionLanded = true;
        if (frame.ActiveTransitions.Contains(transition.Id) && owned)
        {
            if (nativeTransitionStarted is null)
            {
                nativeTransitionStarted = now;
                speech.Add($"{transition.Label}. Waiting for the drop to finish.");
            }
            // E3 01 may precede AD/77 cleanup. Wait for the actual return before
            // capturing the next route; its native busy flag can still be set.
            return true;
        }
        if (frame.CanNavigate && transitionLanded && owned)
        {
            var completed = transition;
            transition = null;
            transitionContactStarted = nativeTransitionStarted = null;
            transitionLanded = false;
            intermediateId = null;
            intermediateContact = null;
            passageWaitStarted = null;
            if (destination!.Id == completed.Id)
            {
                speech.Add(Arrival(destination));
                Stop();
                return true;
            }
            speech.Add($"{completed.Label} complete. Continuing to {destination!.Label}.");
            announcedDirection = NavigationDirection.None;
            lastProgress = now;
            lastPosition = observedPosition = frame.Player;
            lastManualActivity = now;
            return !Plan(frame, speech);
        }
        if (nativeTransitionStarted is not null)
        {
            speech.Add("Navigation stopped: the drop did not reach its landing.");
            Stop();
            return true;
        }
        if (!walking && transitionContactStarted is not null && !NearTransition(frame.Player))
        {
            // Manual movement can leave an armed contact. Restore the normal
            // approach leg rather than telling the player to drop from elsewhere.
            transitionContactStarted = null;
            announcedDirection = NavigationDirection.None;
            return !Plan(frame, speech);
        }
        if (transitionContactStarted is not null || route is { Count: > 0 } && route[^1] == transition.Approach &&
            AtTransitionContact(frame.Player))
        {
            nextPoint = route!.Count;
            if (transitionContactStarted is null)
            {
                transitionContactStarted = now;
                speech.Add(TransitionInstruction());
            }
            else if (walking && (now < transitionContactStarted || now - transitionContactStarted >= 3000))
            {
                speech.Add("Navigation stopped: the drop did not start.");
                Stop();
            }
            return true;
        }
        return false;
    }

    private string TransitionInstruction() => $"{transition!.Label}. Continue {DirectionName(transition.Direction)} to take the drop.";

    private bool AtTransitionContact(NavigationPoint point)
    {
        if (transition is null || point.Layer != transition.Approach.Layer) return false;
        var dx = (long)point.X - transition.Approach.X;
        var dy = (long)point.Y - transition.Approach.Y;
        var (forward, across) = transition.Direction switch
        {
            NavigationDirection.North => (-dy, dx), NavigationDirection.South => (dy, dx),
            NavigationDirection.East => (dx, dy), NavigationDirection.West => (-dx, dy),
            _ => (long.MinValue, long.MaxValue),
        };
        // South-moving floor can skip the exact approach between input ticks.
        // Stay aligned with the native contact column, allowing one tile ahead.
        return Math.Abs(across) <= unitsPerTile / 8 && forward >= -unitsPerTile / 8 && forward <= unitsPerTile;
    }

    /// <summary>At a confirmed destination, arrival is the position plus a facing that the
    /// native confirm test accepts. Automatic walking turns with the ordinary direction pad
    /// (the player may shuffle toward the actor, which stays in reach); manual guidance names
    /// the turn and confirms it once the live facing matches.</summary>
    private void FinishFacing(NavigationFrame frame, long now, List<string> speech)
    {
        var target = destination!;
        var goal = route![^1];
        var reach = target.ConfirmAt(goal)!(frame.Player);
        if (reach.Count == 0 && target.ConfirmPending?.Invoke(frame.Player) == true)
        {
            // In position, but the game would not give this target Confirm yet. Never claim it
            // is ready; wait (bounded when walking) instead of abandoning a valid goal.
            nextPoint = route.Count;
            // Readiness can be revoked mid-turn (a newly touched actor, an unreadable word):
            // release the turn's pad input; a later turn starts with its own deadline.
            facingStarted = null;
            facingDirection = NavigationDirection.None;
            if (pendingStarted is null)
            {
                pendingStarted = now;
                speech.Add($"Next to {target.Label}, but Confirm would not reach it yet.");
            }
            else if (walking && (now < pendingStarted.Value || now - pendingStarted.Value >= 1500))
            {
                speech.Add($"Navigation stopped: {target.Label} did not become ready for Confirm.");
                Stop();
            }
            return;
        }
        pendingStarted = null;
        if (reach.Count == 0)
        {
            // The actor moved, or another actor now takes Confirm here. Never call this
            // arrival: exclude this goal and try the target's other live goals first.
            unreachableGoals.Add(goal);
            if (target.ApproachPoints.Any(p => !unreachableGoals.Contains(p)))
            {
                var planSpeech = new List<string>();
                if (Plan(frame, planSpeech))
                {
                    announcedDirection = NavigationDirection.None;
                    lastProgress = now;
                    lastPosition = frame.Player;
                    if (!walking) speech.Add($"{target.Label} is out of reach from here. Route updated.");
                    return;
                }
                if (!guiding) { speech.AddRange(planSpeech); return; }
            }
            speech.Add($"Navigation stopped: {target.Label} is out of reach from here.");
            Stop();
            return;
        }
        if (frame.PlayerFacing != NavigationDirection.None && reach.Contains(frame.PlayerFacing))
        {
            if (target.FollowUntilInteraction)
            {
                // Hold a reachable position without ending pursuit. The next fresh target
                // moves the goals and restarts steering. Confirm remains the player's input.
                facingStarted = null;
                facingDirection = NavigationDirection.None;
                lastProgress = now;
                lastPosition = frame.Player;
                announcedDirection = NavigationDirection.None;
                return;
            }
            // After a spoken turn, confirm the turn itself; keep any audited arrival advice.
            speech.Add(facingStarted is not null && !walking
                ? "Facing it. " + Arrival(target) + (string.IsNullOrWhiteSpace(target.ArrivalInstruction) ? " Press Confirm." : "")
                : Arrival(target));
            Stop();
            return;
        }
        if (frame.PlayerFacing == NavigationDirection.None)
        {
            // Without a readable facing nothing can be verified or turned. Say where the player
            // is and what is still needed, without claiming they are ready to interact.
            speech.Add($"Next to {target.Label}, but the facing could not be read. " +
                $"Face {DirectionName(reach[0])}, then press Confirm.");
            Stop();
            return;
        }
        if (facingStarted is null || !reach.Contains(facingDirection))
        {
            var first = facingStarted is null;
            facingStarted ??= now;
            facingDirection = reach[0];
            if (!walking) speech.Add($"{(first ? "Next to" : "Still next to")} {target.Label}. " +
                $"Turn {DirectionName(facingDirection)} to face {target.Label}, then press Confirm.");
            return;
        }
        if (walking && (now < facingStarted.Value || now - facingStarted.Value >= 1500))
        {
            speech.Add($"Navigation stopped: could not turn to face {target.Label}.");
            Stop();
        }
    }

    private NavigationResult Result(List<string> speech, NavigationPoint player = default) =>
        new(speech.AsReadOnly(), nativeTransitionStarted is not null ? NavigationDirection.None :
            walking && transitionContactStarted is not null ? transition!.Direction :
            walking && facingStarted is not null ? facingDirection :
            walking && contactStarted is not null ? contactDirection :
            walking && passageWaitStarted is not null && intermediateContact is { } touch ? ContactBearing(player, touch) :
            walking && route is not null && nextPoint < route.Count ? Steering(player) : NavigationDirection.None, guiding, walking)
        {
            ManualLeg = guiding && !walking && nativeTransitionStarted is null && transitionContactStarted is null &&
                !waitingForManualStop && route is not null && nextPoint < route.Count
                ? new(route[LegEnd(nextPoint)], LegDirection(nextPoint), unitsPerTile, instructionRevision)
                    { ExpectedSteps = manualLegBeats } : null
        };

    private bool AdvanceAlongRoute(NavigationPoint player)
    {
        var found = false;
        var tolerance = Math.Max(1, unitsPerTile / 8);
        var closest = double.PositiveInfinity;
        for (var i = nextPoint; i < route!.Count; i++)
        {
            var from = route[i - 1]; var to = route[i];
            if (player.Layer != from.Layer && player.Layer != to.Layer) continue;
            var distance = SegmentDistanceSquared(player, from, to);
            if (distance > closest) continue;
            var horizontal = from.Y == to.Y;
            var vertical = from.X == to.X;
            if (!horizontal && !vertical)
            {
                // Only a graph-proven edge may account for sideways progress.
                // Stay within its short swept segment; departure still replans.
                if (inputDirections?.Count != route.Count - 1 ||
                    !OnSegment(player, from, to, tolerance)) continue;
                found = true;
                closest = distance;
                nextPoint = Near(player, to) ? i + 1 : i;
                continue;
            }
            var cross = horizontal ? Math.Abs((long)player.Y - from.Y) : Math.Abs((long)player.X - from.X);
            var along = horizontal ? player.X : player.Y;
            var low = horizontal ? Math.Min(from.X, to.X) : Math.Min(from.Y, to.Y);
            var high = horizontal ? Math.Max(from.X, to.X) : Math.Max(from.Y, to.Y);
            if (cross > tolerance || along < (long)low - tolerance || along > (long)high + tolerance) continue;
            found = true;
            closest = distance;
            nextPoint = Near(player, to) ? i + 1 : i;
        }
        return found;
    }

    private NavigationDirection LegDirection(int index) => inputDirections?.Count == route!.Count - 1
        ? inputDirections[index - 1] : ExactBearing(route[index - 1], route[index]);

    private static bool OnSegment(NavigationPoint player, NavigationPoint from, NavigationPoint to, int tolerance)
    {
        if (player.X < (long)Math.Min(from.X, to.X) - tolerance || player.X > (long)Math.Max(from.X, to.X) + tolerance ||
            player.Y < (long)Math.Min(from.Y, to.Y) - tolerance || player.Y > (long)Math.Max(from.Y, to.Y) + tolerance) return false;
        var dx = (double)to.X - from.X; var dy = (double)to.Y - from.Y;
        var cross = dx * ((double)player.Y - from.Y) - dy * ((double)player.X - from.X);
        return cross * cross <= (double)tolerance * tolerance * (dx * dx + dy * dy);
    }

    private static double SegmentDistanceSquared(NavigationPoint player, NavigationPoint from, NavigationPoint to)
    {
        var dx = (double)to.X - from.X; var dy = (double)to.Y - from.Y;
        var length = dx * dx + dy * dy;
        var progress = length == 0 ? 0 : Math.Clamp(
            (((double)player.X - from.X) * dx + ((double)player.Y - from.Y) * dy) / length, 0, 1);
        var x = player.X - (from.X + progress * dx); var y = player.Y - (from.Y + progress * dy);
        return x * x + y * y;
    }

    private int LegEnd(int index)
    {
        var direction = LegDirection(index);
        while (index + 1 < route!.Count && LegDirection(index + 1) == direction) index++;
        return index;
    }

    private string DescribeRoute(NavigationPoint player, int maximumLegs)
    {
        var instructions = new List<string>();
        var index = nextPoint;
        while (route is not null && index < route.Count && instructions.Count < maximumLegs)
        {
            var end = LegEnd(index);
            var direction = LegDirection(index);
            double units = direction is NavigationDirection.West or NavigationDirection.East
                ? Math.Abs((long)route[end].X - player.X) : Math.Abs((long)route[end].Y - player.Y);
            if (Enumerable.Range(index, end - index + 1).Any(i =>
                    route[i - 1].X != route[i].X && route[i - 1].Y != route[i].Y))
            {
                // Footsteps measure physical travel, including the native wall
                // slide. A count based on just the commanded axis understates it.
                units = Math.Sqrt(SquaredDistance(player, route[index]));
                for (var i = index + 1; i <= end; i++) units += Math.Sqrt(SquaredDistance(route[i - 1], route[i]));
            }
            var steps = Math.Ceiling(units * 4.0 / unitsPerTile) / 4;
            if (!walking && instructions.Count == 0) manualLegBeats = (int)Math.Min(int.MaxValue, steps);
            var distance = steps < 0.25 ? "less than a quarter step" : steps == 0.25 ? "a quarter step" :
                steps == 0.5 ? "half a step" : steps == 1 ? "1 step" :
                $"{steps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} steps";
            instructions.Add($"{DirectionName(direction)} {distance}");
            player = route[end]; index = end + 1;
        }
        if (instructions.Count == 0) return string.Empty;
        if (!walking) instructionRevision++;
        var text = string.Join(", then ", instructions);
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    private NavigationDirection Steering(NavigationPoint player)
    {
        var target = route![nextPoint];
        var direction = LegDirection(nextPoint);
        var from = route[nextPoint - 1];
        // A native slide already owns its perpendicular displacement. Correcting
        // toward the endpoint's other axis would fight the game's wall slide.
        if (inputDirections?.Count == route.Count - 1 && from.X != target.X && from.Y != target.Y)
            return direction;
        // Ordinary edges keep cardinal correction for small movement drift. A held Dash
        // can stop up to 31 units past a turn; steering back across the turn could leave
        // the level crossing it just entered, so that drift is left to the stall replan.
        var turn = nextPoint;
        while (turn > 1 && LegDirection(turn - 1) == direction) turn--;
        var into = turn > 1 ? LegDirection(turn - 1) : NavigationDirection.None;
        if (direction is NavigationDirection.West or NavigationDirection.East)
        {
            var offset = (long)target.Y - player.Y;
            var correction = Axis(offset) switch { < 0 => NavigationDirection.North, > 0 => NavigationDirection.South, _ => direction };
            return Retraces(correction, into, offset) ? direction : correction;
        }
        if (direction is NavigationDirection.North or NavigationDirection.South)
        {
            var offset = (long)target.X - player.X;
            var correction = Axis(offset) switch { < 0 => NavigationDirection.West, > 0 => NavigationDirection.East, _ => direction };
            return Retraces(correction, into, offset) ? direction : correction;
        }
        return NavigationDirection.None;
    }

    private bool Retraces(NavigationDirection correction, NavigationDirection into, long offset) =>
        Math.Abs(offset) < Math.Max(1, unitsPerTile / 8) && (correction, into) is
            (NavigationDirection.North, NavigationDirection.South) or (NavigationDirection.South, NavigationDirection.North) or
            (NavigationDirection.West, NavigationDirection.East) or (NavigationDirection.East, NavigationDirection.West);

    private bool Progressed(NavigationPoint player) => player.Layer != lastPosition.Layer ||
        SquaredDistance(player, lastPosition) >= Math.Pow(Math.Max(1, unitsPerTile / 8), 2);

    private void Stop()
    {
        guiding = walking = false;
        route = null;
        inputDirections = null;
        destination = null;
        announcedDirection = NavigationDirection.None;
        waitingForManualStop = false;
        manualLegBeats = 0;
        intermediateId = null;
        intermediateContact = null;
        transition = null;
        transitionContactStarted = nativeTransitionStarted = null;
        transitionLanded = false;
        plannedGoals = [];
        passageWaitStarted = null;
        contactStarted = null;
        contactDirection = NavigationDirection.None;
        facingStarted = null;
        facingDirection = NavigationDirection.None;
        pendingStarted = null;
        unreachableGoals.Clear();
        stallTiles.Clear();
        chaseInReach = false;
        lastChaseCue = null;
    }

    private string ContactInstruction() => "Continue " + (contactDirection switch
    {
        NavigationDirection.North => "up", NavigationDirection.South => "down",
        NavigationDirection.West => "left", NavigationDirection.East => "right", _ => "toward the checkpoint",
    }) + (destination?.Category == NavigationCategory.Enemies ? " to engage the encounter." : " until the checkpoint registers.");

    private static NavigationDirection ContactBearing(NavigationPoint from, NavigationPoint center)
    {
        var dx = (long)center.X - from.X;
        var dy = (long)center.Y - from.Y;
        if (Math.Abs(dx) > Math.Abs(dy)) return dx < 0 ? NavigationDirection.West : NavigationDirection.East;
        // At the exact centre, a north probe still makes the native contact;
        // None would leave the player stationary without activating the script.
        return dy > 0 ? NavigationDirection.South : NavigationDirection.North;
    }

    private bool Near(NavigationPoint a, NavigationPoint b) =>
        a.Layer == b.Layer && Math.Abs((long)a.X - b.X) <= Math.Max(1, unitsPerTile / 16) &&
        Math.Abs((long)a.Y - b.Y) <= Math.Max(1, unitsPerTile / 16);
    /// <summary>The game warps on the exit cell, not on proximity, so a goal that sits in
    /// one is only reached when the graph agrees the player is in that same cell. The
    /// one-eighth-tile arrival box alone can land on the far side of a boundary node.
    /// Anything that is not a terminal keeps the plain distance rule.</summary>
    private bool Arrived(INavigationGraph graph, NavigationPoint a, NavigationPoint b) =>
        a.Layer == b.Layer && Math.Abs((long)a.X - b.X) <= Math.Max(1, unitsPerTile / 8) &&
        Math.Abs((long)a.Y - b.Y) <= Math.Max(1, unitsPerTile / 8) &&
        (!graph.IsTerminal(b) || graph.IsSameTerminal(a, b));
    private static double SquaredDistance(NavigationPoint a, NavigationPoint b) =>
        Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Y - b.Y, 2);
    private NavigationDirection Bearing(NavigationPoint from, NavigationPoint to) =>
        (Axis((long)to.X - from.X), Axis((long)to.Y - from.Y)) switch
        {
            (0, -1) => NavigationDirection.North, (0, 1) => NavigationDirection.South,
            (-1, 0) => NavigationDirection.West, (1, 0) => NavigationDirection.East,
            (-1, -1) => NavigationDirection.NorthWest, (1, -1) => NavigationDirection.NorthEast,
            (-1, 1) => NavigationDirection.SouthWest, (1, 1) => NavigationDirection.SouthEast,
            _ => NavigationDirection.None,
        };
    private int Axis(long difference) => Math.Abs(difference) <= Math.Max(1, unitsPerTile / 16) ? 0 : Math.Sign(difference);
    private string EmptyCategory() => category == NavigationCategory.StoryEvents
        ? "No story guidance is available here." : "No destinations in this category.";
    private static string CategoryName(NavigationCategory value) => value switch
    {
        NavigationCategory.Objects => "Interactable Objects",
        NavigationCategory.StoryEvents => "Story Events",
        _ => value.ToString(),
    };
    private static NavigationDirection ExactBearing(NavigationPoint from, NavigationPoint to) =>
        to.X < from.X ? NavigationDirection.West : to.X > from.X ? NavigationDirection.East :
        to.Y < from.Y ? NavigationDirection.North : to.Y > from.Y ? NavigationDirection.South : NavigationDirection.None;
    private static string DirectionName(NavigationDirection value) => value switch
    {
        NavigationDirection.North => "up", NavigationDirection.South => "down",
        NavigationDirection.West => "left", NavigationDirection.East => "right",
        NavigationDirection.NorthWest => "up and left", NavigationDirection.NorthEast => "up and right",
        NavigationDirection.SouthWest => "down and left", NavigationDirection.SouthEast => "down and right",
        _ => "here",
    };
}
