using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Atel_0052's gust shelters. Actor0 tests X54..58/Y30..37 unconditionally (019F) and
/// X53..57/Y48..54 while global7C bit1 is set (0184) before its gust knockback.
/// The upper tree is drawn by the unconditional startup copy 01E4. Below it, actor9 draws the
/// doll at (55,47) from bit1 until bit2 (02BE/02C4). Actor9's 02E7 copy (or 01F1 on a reload)
/// then draws the lower tree. While bit1 is set without bit2, startup 0200 clears the south
/// push on X54..56/Y49..50. A live transformation keeps that patch until the next load.</summary>
public static class DeathPeakTargets
{
    public const int DollActor = 9, DollClass = 4, DollVisual = 0xB9;
    private const int LowerLeft = 53, LowerTop = 48, LowerRight = 57, LowerBottom = 54;

    public static bool KnockbackRegion(int scene, GameNavigationCatalog.Region region) =>
        scene == 244 && region is { Actor: 0, Kind: "Warp", Destination: 498 } &&
        region.Guards.Any(g => g is { Source: "Local", Index: 12, Operation: 0, Value: 1, Expected: true });

    /// <summary>Actor9 as Atel_0052 loads it (load_person B9), currently drawn by 02BE.</summary>
    public static bool IsDrawnDoll(int scene, FieldActorSnapshot actor) =>
        scene == 244 && actor is { Index: DollActor, ClassTag: DollClass, VisualIndex: DollVisual, IsUsable: true, IsDrawn: true };

    public static IEnumerable<NavigationTarget> Build(FieldNavigationSnapshot field, FieldMapSnapshot map,
        FieldViewport viewport, FieldStoryState? story, FieldNavigationGraph graph)
    {
        if (!field.SceneIdCoherent || field.SceneId != 244 || map.TransitionPending) yield break;
        // An unreadable global leaves the lower state unknown: say nothing about it.
        if (story?.Global(0x7C) is { } flags && (flags & 2) != 0)
        {
            if ((flags & 4) != 0)
            {
                foreach (var target in Shelter("Lower shelter tree", 53, 57, 53, 43, 48, null)) yield return target;
            }
            else if (field.Actors.FirstOrDefault(a => IsDrawnDoll(field.SceneId, a)) is { } doll)
            {
                foreach (var target in Shelter("Shelter below the doll", 53, 57, 53, 43, 48, doll)) yield return target;
            }
        }
        foreach (var target in TrunkShelter("upper", "Upper shelter tree", 54, 30, 58, 36, 26, 31)) yield return target;

        IEnumerable<NavigationTarget> Shelter(string label, int left, int right, int bottom, int treeTop, int treeBottom,
            FieldActorSnapshot? doll)
        {
            // Only the live map's own push-free floor: the retained 0200 patch, never a future copy.
            // Interior samples keep the one-eighth-tile arrival box on that floor.
            var calm = Points(LowerLeft, LowerTop, LowerRight, LowerBottom).Where(Calm).Take(64).ToArray();
            var visible = doll is { } shown
                ? viewport.Contains(shown.FineX, shown.FineY)
                : Footprint(left, right, treeTop, treeBottom);
            if (calm.Length != 0)
            {
                var place = doll is null ? "the tree" : "the doll";
                yield return Target("lower", label, calm, visible, null,
                    $"Shelter below {place} during strong wind. Choose when to leave cover as the wind eases.",
                    $"The wind does not push you on this spot below {place}. Standing here is sheltered during strong wind. " +
                    "Choose when to leave cover as the wind eases.");
                yield break;
            }
            // Without the patch, only the trunk itself stops a held Up inside cover.
            if (doll is null)
                foreach (var target in TrunkShelter("lower", label, left, LowerTop, right, bottom, treeTop, treeBottom))
                    yield return target;
        }

        IEnumerable<NavigationTarget> TrunkShelter(string id, string label, int left, int top, int right, int bottom,
            int treeTop, int treeBottom)
        {
            // Approach below the tree on the protected rectangle's centre line.
            // Its sides are also sheltered, but holding Up there can pass beside
            // the trunk and leave cover. Walk the live graph north to the actual
            // collision stop, rather than assuming a standing coordinate.
            var centreX = (left + right + 1) * 128;
            if (!graph.TryPosition(centreX, bottom * 256 + 128, map.PlayerLayer, out var start)) yield break;
            // The live map must hold the copied trunk directly above that stop.
            if (graph.TryPosition(centreX, treeBottom * 256 + 128, map.PlayerLayer, out _)) yield break;
            var walked = WalkNorth(start, left, top, right, bottom);
            if (walked.Count == 0 || graph.IsTerminal(walked[^1])) yield break;
            var goal = walked[^1];
            yield return Target(id, label, [goal], Footprint(left, right, treeTop, treeBottom),
                // 175EE0 probes 112 units either side of the foot. Both corners
                // must meet the one-tile trunk when Up is held; the ordinary
                // two-pixel arrival box can otherwise pass beside it.
                (centreX - 16, centreX + 15),
                "Hold Up against the tree for shelter. The slope pushes constantly, even between gusts. Choose when to leave cover.",
                "Hold Up now. The downhill wind pushes constantly, even between gusts. Choose when to leave cover.");
        }

        // The northward walk that a held Up follows, ending at its collision stop. Empty when it leaves cover.
        List<NavigationPoint> WalkNorth(NavigationPoint point, int left, int top, int right, int bottom)
        {
            var walked = new List<NavigationPoint>();
            var visited = new HashSet<NavigationPoint>();
            while (point.X / 256 >= left && point.X / 256 <= right && point.Y / 256 >= top &&
                point.Y / 256 <= bottom && visited.Add(point) && visited.Count <= 64)
            {
                walked.Add(point);
                var north = Neighbours(point).OrderBy(p => p.Y).ToArray();
                if (north.Length == 0) return walked;
                point = north[0];
            }
            return [];
        }

        IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => graph.Neighbours(point)
            .Where(next => graph.InputDirection(point, next) == NavigationDirection.North);

        IEnumerable<NavigationPoint> Points(int left, int top, int right, int bottom)
        {
            for (var y = top * 256 + 64; y <= bottom * 256 + 192; y += 64)
            for (var x = left * 256 + 64; x <= right * 256 + 192; x += 64)
                if (graph.TryPosition(x, y, map.PlayerLayer, out var point)) yield return point;
        }

        bool Calm(NavigationPoint p) => p.X / 256 is >= LowerLeft and <= LowerRight &&
            p.Y / 256 is >= LowerTop and <= LowerBottom && !graph.IsTerminal(p) &&
            (map.TerrainFlags[p.Y / 256 * map.Width + p.X / 256] & 12) == 0;

        // 01E4 copies the source tree (22..26,58..63) into the upper footprint
        // (54..58,26..31); 01F1/02E7 copy it to (53..57,43..48).
        // Seeing sheltered ground below those tiles is not seeing the tree.
        bool Footprint(int left, int right, int treeTop, int treeBottom) =>
            Enumerable.Range(treeTop, treeBottom - treeTop + 1).Any(y =>
                Enumerable.Range(left, right - left + 1).Any(x => viewport.Contains(x * 256 + 128, y * 256 + 128)));

        static NavigationTarget Target(string id, string label, NavigationPoint[] goals, bool visible,
            (int, int)? xRange, string instruction, string arrival) =>
            new("death-peak:shelter:" + id, label, NavigationCategory.Objects, goals[0], goals, visible, visible)
            {
                GuideAvailable = visible, ArrivalXRange = xRange, Instruction = instruction, ArrivalInstruction = arrival,
                ArrivalAnnouncement = xRange is null ? null :
                    $"Hold Up now. Arrived at {label}. The slope keeps pushing between gusts. Choose when to leave cover.",
            };
    }
}
