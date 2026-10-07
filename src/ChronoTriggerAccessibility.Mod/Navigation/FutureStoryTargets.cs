using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>First visit to 2300 AD through the End of Time. Script counters select
/// objectives; active native actors, scenery and passages supply their positions.
/// Evidence and special interaction limits: docs/future-story-navigation.md.</summary>
public static class FutureStoryTargets
{
    public static IReadOnlyList<NavigationTarget> Build(int scene, FieldStoryState? state,
        IReadOnlyList<NavigationTarget> available, NavigationPoint player)
    {
        if (state is not { Point: >= 51 and <= 77 }) return [];
        var p = state.Point;
        var result = new List<NavigationTarget>();
        switch (scene)
        {
            case 208 when p < 55:
                Exit("bangor", "Leave Bangor Dome and explore", 0);
                break;
            case 210 when p < 55:
                Exit("trann", "Leave Trann Dome for Site 16", 0);
                break;
            case 212 when p < 55:
                Exit("site16-east", "Continue through Site 16", 1);
                break;
            case 213 when p < 55:
                Exit("site16-arrival", "Leave Site 16 toward Arris Dome", 0);
                break;
            case 214 when p < 52:
                Bind("doan", "Speak with the old man", ["actor:14:4:28"]);
                break;
            case 214 when p < 54:
                Exit("arris-basement", "Go down to the basement", 1);
                break;
            case 214 when p == 54:
                Bind("report-doan", "Report back to Doan", ["actor:14:4:28"]);
                break;
            case 214 when p is >= 55 and < 60:
                Exit("leave-arris", "Leave Arris Dome for Site 32", 0);
                break;
            case 216 when p < 54:
                if (state.Flag(0xEC, 0x40) == true)
                {
                    if (state.Flag(0xA4, 0x40) == true)
                        Exit("arris-east", "Enter the eastern basement passage", 2);
                    else if (state.Flag(0xA4, 0x40) == false)
                        Bind("arris-console", "Use the right door console", ["actor:9:4:112"], "Use the input sequence the rat explained, then enter the passage on the right.");
                    else Unavailable("arris", "The basement door's current state is unavailable. Active consoles are in Interactable Objects.");
                }
                else if (state.Flag(0xEC, 0x10) == true)
                    Exit("arris-rat", "Return to the rat in the rafters", 1);
                else if (state.Flag(0xEC, 0x40) == false && state.Flag(0xEC, 0x10) == false)
                    Exit("arris-food", "Search for the food stores", 1);
                else Unavailable("arris", "The investigation's current progress is unavailable. Explore the basement and speak with the residents upstairs.");
                break;
            case 216 when p is >= 54 and < 60:
                Exit("arris-upstairs", "Return upstairs to Doan", 0);
                break;
            case 221 when p < 54:
                if (state.Flag(0xEC, 0x40) == true)
                    Exit("rat-caught", "Return to the basement consoles", 0);
                else if (state.Flag(0xEC, 0x10) == true)
                {
                    Bind("catch-rat", "Catch the rat", ["actor:12:5:134", "actor:13:5:134"],
                        "Follow the moving rat. You can hold Confirm while automatic walking follows it. If it gets away, leave the rafters and return to try again.");
                    if (!result[^1].IsStoryNote) result[^1] = result[^1] with { FollowUntilInteraction = true };
                }
                else if (state.Flag(0xEC, 0x10) == false)
                    Exit("food-chamber", "Continue toward the food stores", 1);
                else Unavailable("rat", "The rat's current state is unavailable. Explore the rafters and the food stores.");
                break;
            case 219 when p < 54:
                if (state.Flag(0xEC, 0x10) == true || state.Flag(0xEC, 0x40) == true)
                    Exit("food-return", "Return to the rafters", 0);
                else Exit("food-stores", "Continue to the food stores", 1);
                break;
            case 344 when p < 54:
                if (state.Flag(0xEC, 0x40) == true || state.Flag(0xEC, 0x10) == true)
                    Exit("leave-food", "Return to the rafters", 0);
                else Bind("food-note", "Examine the man and his note", ["actor:8:4:26"]);
                break;
            case 215 when p < 54:
                Exit("arris-passage", "Continue through the basement", 1);
                break;
            case 217 when p < 54:
                Exit("arris-computer-room", "Enter the computer room", 1);
                break;
            case 218 when p < 54:
                Bind("arris-record", "Approach the computer", ["passage:record-arrival"]);
                break;
            case 215 or 217 or 218 or 219 or 221 or 344 when p is >= 54 and < 60:
                Exit("arris-return", "Return to Doan", 0);
                break;
            case 223 when p is >= 55 and < 60:
                Bind("site32-highway", "Continue onto the eastern highway", ["passage:eastern-highway"],
                    "This highway passage can start Johnny's bike race. During the race, use up and down to steer and your Dash action to boost. K or right-stick click reads race status.");
                break;
            case 224 when p is >= 55 and < 60:
                Exit("site32-cross", "Cross Site 32 to the east", 1);
                break;
            case 225 when p is >= 55 and < 60:
                Exit("site32-proto", "Leave Site 32 toward Proto Dome", 0);
                break;
            case 226 when p < 60:
                Bind("broken-robot", "Examine the broken robot", ["actor:4:3:3"]);
                break;
            case 226 when p is >= 60 and < 63:
                Exit("proto-factory", "Leave for the Derelict Factory", 0);
                break;
            case 226 when p is >= 63 and < 69:
                Note("robo-repair", "Robo's repair", "Listen to the conversation while Lucca repairs Robo.");
                break;
            case 226 when p is >= 69 and < 72:
                Exit("proto-gate-room", "Enter the Gate chamber", 1);
                break;
            case 227 when p is >= 69 and < 72:
                Bind("proto-gate", "Enter the Gate", ["actor:11:4:151"], "Use Confirm at the Gate when control returns.");
                break;
            case 228 when p is >= 60 and < 63:
                if (state.Flag(0x58, 4) == false)
                    Bind("factory-security", "Deactivate the entrance security", ["actor:8:4:127"]);
                else if (state.Global(0x58) is { } factoryFlags)
                    Bind("factory-wing", (factoryFlags & 0x60) == 0x60 ? "Take the lift to the laboratory" : "Take the lift to the warehouse",
                        [(factoryFlags & 0x60) == 0x60 ? "landmark:9" : "landmark:10"]);
                else Unavailable("factory", "The security system's current state is unavailable. Examine the entrance terminal.");
                break;
            case 231 when p is >= 60 and < 63:
                Note("warehouse", "Explore the warehouse", "Getting caught by a conveyor robot starts the inspection ride along the upper conveyor, moving west through three battles. The lower conveyor moves east; run west and step south into the gaps to avoid the robots. The west entrance to the conveyor passage leads toward the upper walkways, the crane and its instructions. Read the security code before returning to the laboratory.");
                // Map125: the inspection drop and south pockets are on layer1.
                // Reach the upper walkway through scene232 rather than offering
                // the disconnected crane nook as a direct walk from this floor.
                if (player.Layer == 1 && player.Y >= 26 * 256 && player.X < 37 * 256)
                {
                    Bind("warehouse-walkways", "Reach the warehouse walkways", ["exit:4", "exit:5"],
                        "Go south through the conveyor passage. Its other doorway returns to the upper warehouse walkway.");
                    break;
                }
                if (player.Layer == 1 && player.X / 256 == 13 && player.Y / 256 is >= 6 and <= 9)
                {
                    if (state.Global(0x58) is not { } alcoveCrane || (alcoveCrane & 0x60) != 0x60)
                        Known("crane", "Operate the crane", "landmark:9", "Use the crane instructions from the nearby terminal. Wait for its tone before entering a pattern.");
                    Known("crane-alcove-return", "Return through the room before the crane", "exit:0",
                        "Take the eastern doorway in that room to reach the main warehouse walkway.");
                    break;
                }
                if (state.Global(0x58) is not { } completedCrane || (completedCrane & 0x60) != 0x60)
                    KnownAny("crane-passage", "Reach the passage to the crane", ["exit:0", "exit:1"],
                        "In the room before the crane, take the western doorway to its control alcove.");
                Known("crane-codes-room", "Find the crane instructions", "exit:3");
                if (state.Global(0x58) is { } craneFlags && (craneFlags & 0x60) == 0x60)
                {
                    Known("security-code-room", "Read the security code", "exit:2");
                    Known("warehouse-return", "Return to the factory entrance", "landmark:8", "Return after reading the security code.");
                }
                break;
            case 232 when p is >= 60 and < 63:
                Exit("warehouse-walkway", "Reach the warehouse walkways", 1);
                break;
            case 233 when p is >= 60 and < 63:
                if (state.Global(0x58) is not { } movedBarrels || (movedBarrels & 0x60) != 0x60)
                    Exit("crane-passage", "Enter the crane-control alcove", 0);
                Exit("crane-return", "Return to the warehouse walkway", 1);
                break;
            case 234 when p is >= 60 and < 63:
                Bind("security-code", "Read the security-code terminal", ["actor:8:4:127"]);
                Known("security-code-return", "Return to the warehouse", "exit:0");
                break;
            case 259 when p is >= 60 and < 63:
                Bind("crane-instructions", "Read the crane instructions", ["actor:8:4:127"]);
                Known("crane-instructions-return", "Return to the warehouse", "exit:0");
                break;
            case 437 when p is >= 60 and < 63:
                Note("inspection", "Conveyor inspection room", "The conveyor carries the party through robot encounters and returns to the warehouse afterward.");
                break;
            case 229 when p is >= 60 and < 63:
                if (state.Flag(0x5C, 0x20) == false)
                {
                    // Atel0040 hides actor12 until controller0 completes the
                    // encounter requested by actors9..11. Use its real contact
                    // approaches while the terminal is still absent.
                    if (available.Any(t => t.Id == "actor:12:4:127"))
                        Bind("lab-hatch", "Open the laboratory hatch", ["actor:12:4:127"]);
                    else if (available.Any(t => t.Id is "actor-encounter:9" or "actor-encounter:10" or "actor-encounter:11"))
                        Bind("lab-entrance", "Clear the laboratory entrance",
                            ["actor-encounter:9", "actor-encounter:10", "actor-encounter:11"],
                            "Defeat the monsters to reveal the hatch terminal.");
                    else Unavailable("lab-entrance", "The laboratory encounter and hatch terminal are currently unavailable. Wait for the encounter to finish, then examine the terminal when it appears.");
                }
                else if (state.Flag(0x5C, 0x20) == true)
                    Exit("lab-downstairs", "Climb down to the lower laboratory", 0);
                else Unavailable("hatch", "The hatch's current state is unavailable. Examine the terminal in the laboratory.");
                break;
            case 230 when p is >= 60 and < 63:
                if (state.Flag(0x58, 1) == false)
                    Bind("lab-lasers", "Deactivate the laboratory lasers", ["actor:17:4:127"]);
                else if (state.Flag(0x58, 1) == true)
                    Exit("lab-power-floor", "Climb down to the power controls", 1);
                else Unavailable("lasers", "The laser controls' current state is unavailable. Examine the laboratory terminal.");
                break;
            case 235 when p is >= 60 and < 63:
                if (state.Flag(0x1D0, 1) == false)
                    Bind("power-passcode", "Enter the security passcode", ["landmark:15"], "Use the code from the warehouse security terminal.");
                else if (state.Flag(0x1D0, 1) == true)
                    Bind("power-switch", "Activate the power switch", ["landmark:10"]);
                else Unavailable("power", "The power room door's current state is unavailable. Examine its console.");
                break;
            case 235 when p is >= 63 and < 69:
                Exit("factory-escape", "Escape by the laboratory ladder", 0, "The elevators are disabled.");
                break;
            case 230 when p is >= 63 and < 66:
                Exit("factory-upper-lab", "Continue toward the factory entrance", 0);
                break;
            case 230 when p is >= 66 and < 69:
                Note("factory-rescue", "Return with Robo", "The party returns to Proto Dome after the robot encounter.");
                break;
            case 464 when p < 73:
                Bind("end-old-man", "Speak with the old man", ["actor:28:4:72"]);
                break;
            case 464 when p == 73:
                Bind("end-pillars", "Return toward the pillars of light", ["landmark:24"]);
                break;
            case 464 when p == 74:
                Bind("end-called-back", "Speak with the old man again", ["actor:28:4:72"]);
                break;
            case 464 when p is 75 or 76:
                Exit("spekkio-room", "Enter the room behind the old man", 0);
                break;
            case 465 when p < 76:
                Bind("spekkio", "Speak with Spekkio", Enumerable.Range(224, 6).Select(v => $"actor:10:5:{v}").ToArray());
                break;
            case 465 when p == 76:
                MagicLesson();
                break;
            case 465 when p == 77:
                Exit("leave-spekkio", "Return to the End of Time platform", 0);
                break;
            case 464 when p == 77:
                Note("first-visit-complete", "Explore the pillars of light", "The first End of Time lesson is complete. Step into a pillar and press Confirm to hear its destination. The next chapter begins beyond the Gate.");
                break;
        }
        return result;

        void MagicLesson()
        {
            // Atel0284: actors16..20 contact handlers advance local09..0D in
            // clockwise order. Actor16 records completed laps in0E; 0E=4 is
            // an interrupted circuit, and speaking with Spekkio resets09..0D.
            var counts = Enumerable.Range(9, 5).Select(state.Local).ToArray();
            if (state.Local(14) is not { } laps || counts.Any(v => v is null or < 0 or > 3))
            {
                Note("magic-lesson", "Spekkio's magic lesson", "Walk clockwise around the walls three times. The current checkpoint state is unavailable.");
                return;
            }
            if (laps == 3 || laps == 4 && counts.Any(v => v != 0))
            {
                Bind("magic-lesson-checkpoint", laps == 3 ? "Return to Spekkio" : "Speak with Spekkio to restart the walking lesson",
                    Enumerable.Range(224, 6).Select(v => $"actor:10:5:{v}").ToArray());
                return;
            }
            // A valid partial circuit is a prefix at n, followed by n-1. All
            // equal means the next contact is the starting point by the door.
            var next = Array.FindIndex(counts, v => v < counts[0]);
            if (next < 0) next = 0;
            string[] places = ["starting point by the door", "west wall", "north wall", "east wall", "south wall"];
            Bind("magic-lesson-checkpoint", "Walk clockwise to the " + places[next], [$"landmark:{16 + next}"],
                "The next checkpoint updates when the game registers this one.");
            NavigationDirection[] contact = [NavigationDirection.North, NavigationDirection.West,
                NavigationDirection.North, NavigationDirection.East, NavigationDirection.South];
            if (!result[^1].IsStoryNote) result[^1] = result[^1] with { ContactDirection = contact[next] };
        }

        void Bind(string id, string label, string[] ids, string? instruction = null) =>
            result.Add(StoryTarget.BindAny("future:" + id, label, available, ids, player, instruction));
        void Exit(string id, string label, int exit, string? instruction = null) => Bind(id, label, [$"exit:{exit}"], instruction);
        void Note(string id, string label, string instruction) => result.Add(StoryTarget.Note("story:future:" + id, label, instruction));
        void Unavailable(string id, string instruction) => Note(id, "Current objective", instruction);
        void Known(string id, string label, string target, string? instruction = null)
        {
            if (available.Any(t => t.Id == target)) Bind(id, label, [target], instruction);
        }
        void KnownAny(string id, string label, string[] ids, string? instruction = null)
        {
            if (available.Any(t => ids.Contains(t.Id))) Bind(id, label, ids, instruction);
        }
    }
}
