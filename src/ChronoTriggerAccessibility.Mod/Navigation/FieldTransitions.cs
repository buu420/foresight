using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Installed Atel_0187 and Atel_0145 party-motion handlers. These are
/// scenery contacts, not the enemy actors' own animations. See the native audit.</summary>
public static class FieldTransitions
{
    public sealed record Entry(NavigationTransition Transition, bool Available, bool Active, int SharedLock);
    private sealed record Definition(int Scene, int Actor, int X, int Y, string Label,
        NavigationPoint Approach, NavigationPoint Landing, int[] LandingLayers,
        int Begin, int End, int SharedLock = -1, int Alias = -1);

    private static readonly Definition[] definitions =
    [
        new(143, 9, 1408, 3968, "Drop to lower path", new(1408, 3808, 1), new(1408, 4736, 1), [1], 0x54E, 0x56C),
        new(144, 16, 10112, 7423, "Slide drop A", new(10368, 7168, 3), new(9856, 10880, 3), [3, 1], 0x4FD, 0x531, 101, 21),
        new(144, 17, 10112, 11263, "Lower path drop", new(10016, 11088, 1), new(10880, 13440, 1), [1], 0x538, 0x55C),
        new(144, 19, 12672, 5631, "Slide drop B", new(12672, 5376, 2), new(12672, 12672, 2), [2], 0x58F, 0x5B5, 101),
        new(144, 18, 13440, 6143, "Slide drop C", new(13440, 5888, 2), new(12672, 12672, 2), [2], 0x562, 0x588, 101),
    ];

    public static IReadOnlyList<Entry> Find(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldStoryState? story)
    {
        if (!field.SceneIdCoherent || field.InputMode != 0 || map.TransitionPending || story is not { Point: >= 3 } ||
            field.LeadPlayer is not { IsUsable: true, IsDrawn: true }) return [];
        var found = new List<Entry>();
        var floor = new FieldNavigationGraph(map);
        foreach (var d in definitions.Where(d => d.Scene == field.SceneId))
        {
            var actor = field.Actors.FirstOrDefault(a => Matches(a, d.Actor, d.X, d.Y));
            if (actor is null) continue;
            // 07 waits on the called party function. +48 is its native PC, not
            // an animation id; priority and the exact handler exclude foreign cutscenes.
            var priorityMatches = (d.Scene, d.Actor) switch
            {
                (144, 16) => actor.ScriptPriority is 2 or 5, // proxy 21 requests priority 5
                (144, 18) => actor.ScriptPriority is 1 or 2, // native Confirm or touch
                _ => actor.ScriptPriority == 2,
            };
            var active = priorityMatches && actor.ScriptAddress is { } pc && pc >= d.Begin && pc <= d.End;
            var available = field.SceneId == 143
                ? story.Local(7) is not null && story.Local(19) == 0 &&
                    field.Actors.Any(a => a.Index == 0 && a.ScriptProcessingEnabled)
                : (d.Actor is not (16 or 17) || story.Local(100) is { } busy && busy != 1) &&
                    (d.SharedLock < 0 || story.Local(d.SharedLock) is { } used && used != 1);
            if (d.Alias >= 0 && !field.Actors.Any(a => Matches(a, d.Alias, 10368, 7423))) available = false;
            if (!available && !active) continue;
            if (!floor.TryPosition(d.Approach.X, d.Approach.Y, d.Approach.Layer, out var approach) || approach != d.Approach) continue;
            var transition = new NavigationTransition($"transition:{d.Actor}", d.Label, approach, d.Landing,
                NavigationDirection.South, d.LandingLayers);
            found.Add(new(transition, available, active, d.SharedLock));
        }
        return found;
    }

    private static bool Matches(FieldActorSnapshot actor, int id, int x, int y) =>
        actor.Index == id && actor.ClassTag == 7 && actor.IsUsable && !actor.IsPartyMember &&
        (actor.LoadedFlag & 1) == 0 && actor.CollisionOffsetX == 0 &&
        actor.FineX == x && actor.FineY == y && actor.ScriptCallsEnabled && actor.ScriptProcessingEnabled;
}
