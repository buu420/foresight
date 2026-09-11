namespace ChronoTriggerAccessibility.Core.Navigation;

public sealed class NavigationController
{
    private string? scene;
    private string? selection;
    private NavigationCategory category;
    private NavigationTarget? destination;
    private IReadOnlyList<NavigationPoint>? route;
    private int nextPoint;
    private bool guiding;
    private bool walking;
    private NavigationPoint lastPosition;
    private long lastProgress;
    private NavigationDirection announcedDirection;
    private int unitsPerTile = 16;
    private long planRevision;

    public bool IsActive => guiding;
    public string DiagnosticState => $"target={destination?.Id ?? selection ?? "none"}; plan={planRevision}; " +
        $"waypoint={nextPoint}/{route?.Count ?? 0}; next={PointText(route is not null && nextPoint < route.Count ? route[nextPoint] : null)}; " +
        $"goal={PointText(route is { Count: > 0 } ? route[^1] : null)}";
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
        Refresh(frame, speech);
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
                speech.Add($"{(walking ? "Walking to" : "Guidance to")} {target.Label}.");
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
        Refresh(frame, speech);
        if (walking && manualInput)
        {
            Stop();
            speech.Add("Navigation stopped: manual control.");
        }
        if (guiding) Follow(frame, nowMilliseconds, speech);
        return Result(speech, frame.Player);
    }

    private static string Arrival(NavigationTarget target) => $"Arrived at {target.Label}." +
        (string.IsNullOrWhiteSpace(target.ArrivalInstruction) ? "" : " " + target.ArrivalInstruction);

    private void Refresh(NavigationFrame frame, List<string> speech)
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
        if (!frame.CanNavigate && guiding)
        {
            Stop();
            speech.Add("Navigation stopped: player control is unavailable.");
        }
        if (guiding && !Eligible(frame).Any(target => target.Id == destination?.Id))
        {
            Stop();
            speech.Add("Navigation stopped: destination is no longer available.");
        }
    }

    private List<NavigationTarget> Eligible(NavigationFrame frame) => frame.Targets
        .Where(target => target.Category == category && (target.Visible || target.Discovered ||
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
        if (target.IsStoryNote)
        {
            speech.Add($"{target.Label}, {index + 1} of {targets.Count}. {target.Instruction}");
            return;
        }
        if (guiding && route is not null && nextPoint < route.Count)
        {
            speech.Add($"{target.Label}, {index + 1} of {targets.Count}. {DescribeRoute(frame.Player, 3)}");
            return;
        }
        var direction = Bearing(frame.Player, target.Position);
        var distance = Math.Ceiling(Math.Sqrt(SquaredDistance(frame.Player, target.Position)) / unitsPerTile);
        var location = frame.Player.Layer != target.Position.Layer ? "on another level" :
            direction == NavigationDirection.None ? "here" : $"{DirectionName(direction)}, {distance:0} {(distance == 1 ? "step" : "steps")} away";
        speech.Add($"{target.Label}, {index + 1} of {targets.Count}, {location}.");
        if (!string.IsNullOrWhiteSpace(target.Instruction)) speech.Add(target.Instruction);
    }

    private bool Plan(NavigationFrame frame, List<string> speech)
    {
        if (destination!.IsStoryNote)
        {
            speech.Add($"{destination.Label}. {destination.Instruction}");
            Stop();
            return false;
        }
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, destination!.ApproachPoints);
        planRevision++;
        route = search.Route;
        nextPoint = 1;
        if (route is not null) return true;
        speech.Add(search.LimitReached ? "The route search limit was reached. Try a closer destination." : $"No route to {destination.Label} is available.");
        Stop();
        return false;
    }

    private void Follow(NavigationFrame frame, long now, List<string> speech)
    {
        var currentTarget = Eligible(frame).Find(target => target.Id == destination?.Id);
        if (currentTarget is null)
        {
            speech.Add("Navigation stopped: destination is no longer available.");
            Stop();
            return;
        }
        // The chosen approach remains valid when a wide exit exposes additional
        // cells or their order changes. Do not restart speech for alternative goals.
        var mustReplan = route is null || !currentTarget.ApproachPoints.Contains(route[^1]);
        destination = currentTarget;
        if (mustReplan)
        {
            if (!Plan(frame, speech)) return;
        }
        if (route is null) return;
        if (Arrived(frame.Player, route[^1]))
        {
            speech.Add(Arrival(destination));
            Stop();
            return;
        }
        var onRoute = AdvanceAlongRoute(frame.Player);
        if (nextPoint >= route.Count) return;
        // Progress along a leg can pass multiple small graph waypoints. Only a
        // departure from the actual route or changed terrain requires a new plan.
        var previous = route[Math.Max(0, nextPoint - 1)];
        if (!onRoute ||
            !frame.Graph.Neighbours(previous).Contains(route[nextPoint]))
        {
            if (!Plan(frame, speech)) return;
            if (route!.Count == 1)
            {
                speech.Add(Arrival(destination!));
                Stop();
                return;
            }
        }
        if (walking)
        {
            if (SquaredDistance(frame.Player, lastPosition) >= Math.Pow(Math.Max(1, unitsPerTile / 8), 2) || frame.Player.Layer != lastPosition.Layer)
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
        if (direction != NavigationDirection.None && direction != announcedDirection)
        {
            speech.Add(DescribeRoute(frame.Player, announcedDirection == NavigationDirection.None ? 3 : 1));
            announcedDirection = direction;
        }
    }

    private NavigationResult Result(List<string> speech, NavigationPoint player = default) =>
        new(speech.AsReadOnly(), walking && route is not null && nextPoint < route.Count
            ? Steering(player) : NavigationDirection.None, guiding, walking);

    private bool AdvanceAlongRoute(NavigationPoint player)
    {
        var found = false;
        var tolerance = Math.Max(1, unitsPerTile / 8);
        for (var i = nextPoint; i < route!.Count; i++)
        {
            var from = route[i - 1]; var to = route[i];
            if (player.Layer != from.Layer && player.Layer != to.Layer) continue;
            var horizontal = from.Y == to.Y;
            var vertical = from.X == to.X;
            if (!horizontal && !vertical) continue;
            var cross = horizontal ? Math.Abs((long)player.Y - from.Y) : Math.Abs((long)player.X - from.X);
            var along = horizontal ? player.X : player.Y;
            var low = horizontal ? Math.Min(from.X, to.X) : Math.Min(from.Y, to.Y);
            var high = horizontal ? Math.Max(from.X, to.X) : Math.Max(from.Y, to.Y);
            if (cross > tolerance || along < (long)low - tolerance || along > (long)high + tolerance) continue;
            found = true;
            nextPoint = Near(player, to) ? i + 1 : i;
        }
        return found;
    }

    private NavigationDirection LegDirection(int index) => ExactBearing(route![index - 1], route[index]);

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
            var units = direction is NavigationDirection.West or NavigationDirection.East
                ? Math.Abs((long)route[end].X - player.X) : Math.Abs((long)route[end].Y - player.Y);
            var steps = Math.Ceiling(units * 4.0 / unitsPerTile) / 4;
            var distance = steps < 0.25 ? "less than a quarter step" : steps == 0.25 ? "a quarter step" :
                steps == 0.5 ? "half a step" : steps == 1 ? "1 step" :
                $"{steps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} steps";
            instructions.Add($"{DirectionName(direction)} {distance}");
            player = route[end]; index = end + 1;
        }
        if (instructions.Count == 0) return string.Empty;
        var text = string.Join(", then ", instructions);
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    private NavigationDirection Steering(NavigationPoint player)
    {
        var target = route![nextPoint];
        var direction = LegDirection(nextPoint);
        // The graph proves cardinal edges. Never turn a small perpendicular drift
        // into a diagonal shortcut across a corner the route has not checked.
        if (direction is NavigationDirection.West or NavigationDirection.East)
        {
            var correction = Axis((long)target.Y - player.Y);
            return correction < 0 ? NavigationDirection.North : correction > 0 ? NavigationDirection.South : direction;
        }
        if (direction is NavigationDirection.North or NavigationDirection.South)
        {
            var correction = Axis((long)target.X - player.X);
            return correction < 0 ? NavigationDirection.West : correction > 0 ? NavigationDirection.East : direction;
        }
        return NavigationDirection.None;
    }

    private void Stop()
    {
        guiding = walking = false;
        route = null;
        destination = null;
        announcedDirection = NavigationDirection.None;
    }

    private bool Near(NavigationPoint a, NavigationPoint b) =>
        a.Layer == b.Layer && Math.Abs((long)a.X - b.X) <= Math.Max(1, unitsPerTile / 16) &&
        Math.Abs((long)a.Y - b.Y) <= Math.Max(1, unitsPerTile / 16);
    private bool Arrived(NavigationPoint a, NavigationPoint b) =>
        a.Layer == b.Layer && Math.Abs((long)a.X - b.X) <= Math.Max(1, unitsPerTile / 8) &&
        Math.Abs((long)a.Y - b.Y) <= Math.Max(1, unitsPerTile / 8);
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
