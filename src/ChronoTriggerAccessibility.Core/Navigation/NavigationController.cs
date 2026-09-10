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

    public bool IsActive => guiding;

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
                category = (NavigationCategory)(((int)category + (command == NavigationCommand.NextCategory ? 1 : 2)) % 3);
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
                    speech.Add("No destinations in this category.");
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
        .Where(target => target.Category == category && (target.Visible || target.Discovered) &&
            !string.IsNullOrWhiteSpace(target.Id) && !string.IsNullOrWhiteSpace(target.Label))
        .DistinctBy(target => target.Id).ToList();

    private void SelectDefault(List<NavigationTarget> targets)
    {
        if (!targets.Any(target => target.Id == selection)) selection = targets.FirstOrDefault()?.Id;
    }

    private void DescribeSelection(NavigationFrame frame, List<NavigationTarget> targets, List<string> speech)
    {
        var index = targets.FindIndex(target => target.Id == selection);
        if (index < 0) { speech.Add("No destinations in this category."); return; }
        var target = targets[index];
        var direction = Bearing(frame.Player, target.Position);
        var distance = Math.Ceiling(Math.Sqrt(SquaredDistance(frame.Player, target.Position)) / unitsPerTile);
        var location = frame.Player.Layer != target.Position.Layer ? "on another level" :
            direction == NavigationDirection.None ? "here" : $"{DirectionName(direction)}, {distance:0} tiles away";
        speech.Add($"{target.Label}, {index + 1} of {targets.Count}, {location}.");
        if (guiding && route is not null && nextPoint < route.Count)
            speech.Add($"Next direction: {DirectionName(Bearing(frame.Player, route[nextPoint]))}.");
    }

    private bool Plan(NavigationFrame frame, List<string> speech)
    {
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, destination!.ApproachPoints);
        route = search.Route;
        nextPoint = 1;
        announcedDirection = NavigationDirection.None;
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
        if (!currentTarget.ApproachPoints.SequenceEqual(destination!.ApproachPoints))
        {
            destination = currentTarget;
            if (!Plan(frame, speech)) return;
        }
        if (route is null) return;
        if (Near(frame.Player, route[^1]))
        {
            speech.Add($"Arrived at {destination.Label}.");
            Stop();
            return;
        }
        while (nextPoint < route.Count && Near(frame.Player, route[nextPoint])) nextPoint++;
        if (nextPoint >= route.Count) return;
        // Recompute after a deviation or a changed obstacle, rather than walking a stale edge.
        var previous = route[Math.Max(0, nextPoint - 1)];
        if (SquaredDistance(frame.Player, previous) > Math.Pow(unitsPerTile / 2.0, 2) || frame.Player.Layer != previous.Layer ||
            !frame.Graph.Neighbours(previous).Contains(route[nextPoint]))
        {
            if (!Plan(frame, speech)) return;
            if (route!.Count == 1)
            {
                speech.Add($"Arrived at {destination!.Label}.");
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
        var direction = Bearing(frame.Player, route![nextPoint]);
        if (direction != NavigationDirection.None && direction != announcedDirection)
        {
            speech.Add($"Go {DirectionName(direction)}.");
            announcedDirection = direction;
        }
    }

    private NavigationResult Result(List<string> speech, NavigationPoint player = default) =>
        new(speech.AsReadOnly(), walking && route is not null && nextPoint < route.Count
            ? Bearing(player, route[nextPoint]) : NavigationDirection.None, guiding, walking);

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
    private static string CategoryName(NavigationCategory value) => value == NavigationCategory.Objects ? "Interactable Objects" : value.ToString();
    private static string DirectionName(NavigationDirection value) => value.ToString().ToLowerInvariant();
}
