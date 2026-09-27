using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Local objectives through the prison escape. The counter and passage
/// flags select the chapter; active native targets supply route positions.</summary>
public static class EarlyStoryTargets
{
    public static IReadOnlyList<NavigationTarget> Build(int scene, FieldStoryState? state,
        IReadOnlyList<NavigationTarget> available, NavigationPoint player)
    {
        if (state is null || state.Point is < 3 or > 48) return [];
        var p = state.Point;
        var result = new List<NavigationTarget>();
        switch (scene)
        {
            case 5 when p < 12:
                if (p >= 8 && state.Flag(0x55, 0x80) is null)
                    Note("fair-progress", "Lucca's exhibit", "The exhibit's current progress is unavailable. Speak with the fairgoers.");
                else if (p >= 8 && state.Flag(0x55, 0x80) == false)
                    Bind("fair-news", "Ask about Lucca's exhibit", ["actor:17:4:80", "actor:16:4:22", "actor:22:4:198", "actor:24:4:22"]);
                else Exit("rear-plaza", "Continue to the rear plaza", [1]);
                break;
            case 439 when p < 6:
                Actor("meet-girl", "Approach the young woman", 3, 1);
                break;
            case 439 when p == 6:
                if (state.Flag(0x54, 0x20) == true)
                    Actor("girl-pendant", "Return the pendant to the young woman", 3, 1);
                else if (state.Flag(0x55, 4) == true && state.Flag(0x54, 0x10) == true)
                    Bind("pendant", "Pick up the fallen pendant", ["actor:15:4:99"]);
                else
                {
                    Actor("girl-pendant", "Talk with the young woman", 3, 1);
                    if (state.Flag(0x54, 0x10) == true) Known("pendant", "Pick up the fallen pendant", "actor:15:4:99");
                }
                break;
            case 439 when p is >= 8 and < 12:
                if (state.Flag(0x55, 0x80) is null)
                    Note("fair-progress", "Lucca's exhibit", "The exhibit's current progress is unavailable. Speak with the fairgoers.");
                else if (state.Flag(0x55, 0x80) == false)
                    Exit("fair-news", "Return to the central plaza", [0], "Speak with the fairgoers about Lucca's exhibit.");
                else Exit("exhibit", "Visit Lucca's exhibit", [1]);
                break;
            case 6 or 7 when p < 12:
                Exit("back-to-fair", "Return to the fair", [0]);
                break;
            case 8 when p < 10:
                Actor("lucca", "Talk with Lucca", 13, 2);
                break;
            case 8 when p == 10:
                if (state.Flag(0x56, 2) is null)
                    Note("telepod-progress", "Lucca's demonstration", "The demonstration's current progress is unavailable. Follow Lucca's spoken instructions.");
                else if (state.Flag(0x56, 2) == true) Actor("marle-demo", "Talk with Marle", 14, 1);
                else Bind("try-telepod", "Try the left Telepod", ["landmark:12"], "Step onto the left pod, as Lucca requested.");
                break;
            case 8 when p == 12:
                var pendant = StoryTarget.BindAny("follow-marle", "Pick up Marle's pendant", available,
                    ["actor:11:4:99"], player, "The pendant is on the left Telepod.");
                result.Add(pendant with { ArrivalInstruction = "Face the pendant and use confirm to pick it up." });
                break;
            case 113 when p < 15:
                Exit("leave-clearing", "Explore Truce Canyon", [0]);
                break;
            case 112 when p < 33:
                Exit("leave-canyon", "Leave Truce Canyon", [0], "Visit Truce and find out where you are.");
                break;
            case 114 or 115 or 116 or 117 or 118 when p is >= 12 and < 15:
                Exit("truce", scene == 117 ? "Go downstairs" : "Leave for Guardia Forest", [0]);
                break;
            case 119 when p is >= 12 and < 16:
                Exit("castle", "Continue toward Guardia Castle", [1]);
                break;
            case 120 when p is >= 15 and < 16:
                Exit("queen-tower", "Visit the queen's chamber", [3]);
                break;
            case 468 when p == 15 || p is >= 27 and < 33:
                Exit("up-queen-tower", "Continue up to the queen's chamber", [0, 1, 2]);
                break;
            case 122 when p == 15:
                // Atel0242 actor9's confirm handler moves him aside and sets
                // G0A0:20 at055A. Reaching the queen first would hit that guard.
                if (state.Flag(0xA0, 0x20) is null)
                    Note("queen-room", "The queen's room", "The guard's current progress is unavailable. Speak with the guard outside the room.");
                else if (state.Flag(0xA0, 0x20) == false)
                {
                    var guard = StoryTarget.BindAny("queen-guard", "Speak with the guard outside the queen's room",
                        available, ["actor:9:4:51"], player);
                    result.Add(guard with { ArrivalInstruction = "Face the guard and use confirm to speak." });
                }
                else Bind("queen", "Approach the queen", ["actor:20:4:69"]);
                break;
            case 122 when p is >= 16 and < 27:
                Exit("return-hall", "Return to the castle's main hall", [0]);
                break;
            case 468 when p is >= 16 and < 27 || p is >= 33 and < 39:
                Exit("down-queen-tower", "Go downstairs to the main hall", [3, 4, 5]);
                break;
            case 120 when p is >= 16 and < 18:
                Bind("meet-lucca", "Meet Lucca near the stairs", ["landmark:17"]);
                break;
            case 120 when p is >= 18 and < 27:
                Exit("find-leene", "Leave to search for Queen Leene", [0], "The cathedral is west of the forest.");
                break;
            case 119 when p is >= 18 and < 27 || p is >= 33 and < 39:
                Exit("leave-forest", "Leave Guardia Forest", [0]);
                break;
            case 129 when p is >= 18 and < 21:
                Note("search-chapel", "Investigate the cathedral", "Speak with the nuns and examine what is visible in the chapel.");
                Known("hairpin", "Examine the sparkle on the floor", "landmark:15");
                break;
            case 129 when p is >= 21 and < 27:
                if (state.Flag(0xFF, 0x10) == true) Exit("chapel-passage", "Enter the passage", [1]);
                else
                {
                    Note("find-passage", "Find a passage through the cathedral", "Examine the chapel's furnishings.");
                    Known("chapel-organ", "Play the organ", "landmark:13");
                }
                break;
            case 130 when p is >= 21 and < 27:
                Exit("inner-cathedral", "Continue into the cathedral", [5]);
                break;
            case 131 when p is >= 21 and < 27:
                if (state.Flag(0xFF, 4) == true) Exit("cathedral-passage", "Continue through the passage", [1]);
                // Actor24 removes the central spikes (Terrain copy at 0CB3)
                // and sets FF:02; actor36 opens the northern passage (FF:04).
                // Actor46's optional left switch is still in Objects. It is
                // not a prerequisite and must not send the story off course.
                else if (state.Flag(0xFF, 2) == false)
                    Bind("right-switch", "Use the right wall switch", ["landmark:24"],
                        "This opens the way to the organ. Use confirm at the switch.");
                else if (state.Flag(0xFF, 2) == true && state.Flag(0xFF, 4) == false)
                    Bind("inner-organ", "Play the organ", ["actor:36:4:100"],
                        "Return to the central room and use confirm at the organ.");
                else Note("inner-hall", "Explore the inner cathedral", "The state of the cathedral's mechanisms is unavailable.");
                break;
            case 132 when p is >= 21 and < 27:
                Exit("rescue-leene", "Continue toward Queen Leene", [1]);
                break;
            case 198 when p is >= 21 and <= 27:
                Bind("queen-leene", "Reach Queen Leene", ["actor:12:4:3"], "Use confirm to speak when you reach her.");
                break;
            case 199 or 200 or 201 or 202 or 203 when p is >= 21 and < 27:
                Exit("cathedral-main-hall", "Return toward the cathedral's main hall", [0]);
                break;
            case 120 when p is >= 27 and < 33:
                Exit("find-marle", "Return to the queen's chamber", [3]);
                break;
            case 122 when p is >= 28 and < 33:
                // Atel0242 actor8 opens the doorway and arms actor24's startup
                // event (A1:04). The disappearance region or contact25/26 sets
                // local0C to start the return scene. Actor24 has no contact handler.
                if (state.Flag(0xA1, 4) is null)
                    Note("find-marle", "Look for Marle in the queen's chamber", "The chamber's current progress is unavailable.");
                else if (state.Flag(0xA1, 4) == false)
                    Bind("find-marle", "Enter the queen's chamber", ["landmark:8"]);
                else Bind("find-marle", "Approach where Marle disappeared", ["landmark:25", "landmark:26"]);
                break;
            case 122 when p is >= 33 and < 39:
                Exit("leave-queen-room", "Return to the main hall", [0]);
                break;
            case 120 when p is >= 33 and < 39:
                Exit("return-gate", "Leave for Truce Canyon", [0]);
                break;
            case 121 or 123 or 124 when p is >= 12 and < 39:
                Exit("return-hall", "Return toward the main hall", [0]);
                break;
            case 480 when p is >= 12 and < 39:
                Exit("down-king-tower", "Go downstairs to the main hall", [3, 4, 5]);
                break;
            case 112 when p is >= 33 and < 39:
                Exit("canyon-gate", "Return to the clearing where you arrived", [1]);
                break;
            case 113 when p is >= 33 and < 39:
                Bind("return-present", "Return through the Gate", ["actor:1:4:97"], "Approach the Gate and use confirm.");
                break;
            case 8 when p is >= 39 and < 42:
                Exit("escort-marle", "Leave the exhibit with Marle", [0], "Take Marle home to Guardia Castle.");
                break;
            case 439 when p is >= 39 and < 42:
                Exit("leave-rear-plaza", "Return to the central plaza", [0]);
                break;
            case 5 when p is >= 39 and < 42:
                Exit("leave-fair", "Leave the fair for Guardia Castle", [0]);
                break;
            case 19 when p is >= 39 and < 42:
                Exit("present-castle", "Take Marle to Guardia Castle", [2]);
                break;
            case 21 when p is >= 39 and < 45:
            case 27 when p is >= 42 and < 45:
                Note("trial", "The trial", "Listen to the proceedings and answer the spoken choices when prompted.");
                break;
            case 71 when p is >= 45 and < 46 && state.Flag(0x190, 4) == false:
                Note("prison-cell", "Inside the prison cell", "You can wait here or approach the cell bars and use confirm.");
                break;
            case 71 when p == 45 && state.Flag(0x190, 4) is null:
                Note("prison-progress", "The prison", "The cell's current state is unavailable. Listen for the guards and examine your surroundings.");
                break;
            case 71 when p is >= 45 and < 48:
                Exit("leave-cells", "Continue through the prison", [0, 2, 3, 5, 6, 8]);
                break;
            case 72 when p is >= 45 and < 48:
                // One onward exit per disconnected floor. The other stairs lead
                // back down; see the native connectivity audit in the release notes.
                Exit("prison-upper-hall", "Continue toward the upper prison", [0, 2, 6, 12]);
                break;
            case 30 when p is >= 46 and < 48:
                Exit("leave-execution", "Leave the execution chamber", [0]);
                break;
            case 75 when p is >= 45 and < 48:
                Exit("return-inside-prison", "Return inside the prison tower", [0, 1]);
                break;
            case 29 when p is >= 45 and < 48:
                var guards = available.Where(t => t.Id is "actor:10:5:49" or "actor:11:5:49").ToArray();
                if (guards.Length != 0)
                {
                    var encounter = StoryTarget.BindAny("guardroom", "Approach the guards", guards,
                        guards.Select(t => t.Id).ToArray(), player);
                    // The native startup watcher triggers the encounter when the
                    // player enters the guards' row (23), not at a map exit. Keep
                    // live approaches in that row so arrival does not stop short.
                    // The watcher, not Confirm, starts it: no facing is part of arrival.
                    result.Add(encounter with { ApproachPoints = guards.SelectMany(t => t.ApproachPoints
                        .Where(a => a.Y / 256 == t.Position.Y / 256)).Distinct().ToArray(), ConfirmFacings = null, ApproachConfirms = null, ConfirmPending = null });
                    break;
                }
                Exit("prison-bridge", "Continue onto the upper bridge", [0]);
                break;
            case 28 when p is >= 45 and < 48:
                // Atel0409 uses four disconnected bridge rows. Actor1's native
                // contact first introduces the tank (G198:04), then starts its
                // encounter (G199:04). The aftermath copies the broken bridge
                // before returning control. Do not route past that sequence.
                var bridge = Math.Clamp(player.Y / (16 * 256), 0, 3);
                if (bridge != 0) Exit("cross-prison", "Continue toward the castle stairs", [new[] { 0, 2, 4, 6 }[bridge]]);
                else if (state.Flag(0x199, 4) == true)
                    Exit("cross-prison", "Continue toward the castle stairs", [0]);
                else if (state.Flag(0x199, 4) == false && state.Flag(0x198, 4) is { } approached)
                    Bind("upper-bridge", approached ? "Continue toward the tank" : "Continue across the upper bridge", ["landmark:1"]);
                else Note("upper-bridge", "The upper bridge", "The bridge's current progress is unavailable.");
                break;
            case 489 when p is >= 46 and < 48:
                Exit("castle-downstairs", "Go downstairs to the castle's main hall", [3, 4, 5]);
                break;
            case 21 when p is >= 46 and <= 48:
                Exit("escape-castle", "Leave Guardia Castle", [0]);
                break;
            case 19 when p == 48:
                Exit("escape-forest", "Find a way out through the forest", [0]);
                break;
            case 20 when p == 48:
                Note("forest-gate", "The forest clearing", "Continue toward the Gate in the clearing.");
                break;
        }
        return result;

        void Bind(string id, string label, string[] ids, string? instruction = null) =>
            result.Add(StoryTarget.BindAny(id, label, available, ids, player, instruction));
        void Exit(string id, string label, int[] exits, string? instruction = null) =>
            Bind(id, label, exits.Select(e => $"exit:{e}").ToArray(), instruction);
        void Actor(string id, string label, int actor, int visual) =>
            Bind(id, label, Enumerable.Range(0, 4).Select(c => $"actor:{actor}:{c}:{visual}").ToArray());
        void Known(string id, string label, string target)
        {
            if (available.Any(t => t.Id == target)) Bind(id, label, [target]);
        }
        void Note(string id, string label, string instruction) => result.Add(StoryTarget.Note("story:" + id, label, instruction));
    }

    public static string? Landmark(int scene, FieldStoryState? state, FieldActorSnapshot actor,
        IReadOnlyList<FieldActorSnapshot> actors) =>
        state is { Point: >= 3 and <= 48 } && actor.IsUsable && actor.ClassTag == 7 && !actor.IsPartyMember &&
        actor.ScriptCallsEnabled
        ? (scene, actor.Index, state.Point) switch
        {
            (28, 1, >= 46 and < 48) when state.Flag(0x199, 4) == false => "Upper bridge contact",
            (8, 12, 10) when state.Flag(0x56, 1) == true => "Left Telepod",
            (120, 9 or 10, _) when state.Local(6) == 0 => "Stairway past the guards",
            (122, 8, >= 28 and < 33) when state.Flag(0xA1, 4) == false => "Passage into the queen's room",
            (122, 8, _) when state.Local(7) == 0 => "Passage into the queen's room",
            (120, 17, >= 16 and < 18) => "Foot of the queen's staircase",
            (122, 25 or 26, >= 28 and < 33) when state.Flag(0xA1, 4) == true => "Where Marle disappeared",
            (129, 13, >= 18 and < 27) => "Organ",
            (129, 15, >= 18 and < 21) when actors.Any(a => a.Index == 14 && a.ClassTag == 4 &&
                a.VisualIndex == 112 && a.IsUsable && a.IsDrawn && a.TileX == actor.TileX && a.TileY == actor.TileY) => "Sparkle on the floor",
            (130, 35, >= 21 and < 27) when state.Flag(0xFF, 1) == false => "Wall switch",
            (131, 24, >= 21 and < 27) => "Right wall switch",
            (131, 46, >= 21 and < 27) => "Left wall switch",
            (131, 34, >= 21 and < 27) => "Sign",
            _ => null,
        } : null;

    public static bool IsTouchLandmark(int scene, int actor) =>
        (scene, actor) is (8, 12) or (28, 1) or (120, 17) or (122, 8 or 25 or 26);

    // These two script pickups can be drawn while the native +152 activation
    // byte is zero. Atel_0074 actor 15 uses the dropped/collected bits; Atel_0028
    // actor 11 checks story point 12 before drawing the Telepod pendant.
    public static bool IsScriptedPickupAvailable(int scene, FieldStoryState? state, FieldActorSnapshot actor) =>
        actor.ClassTag == 4 && actor.VisualIndex == 99 && actor.ScriptCallsEnabled &&
        (scene, actor.Index, state?.Point) switch
        {
            (439, 15, 6) => state!.Flag(0x54, 0x10) == true && state.Flag(0x54, 0x20) == false,
            (8, 11, 12) => true,
            _ => false,
        };
}
