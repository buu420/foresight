# Manual turn recovery, 0.3.8

The 2026-09-13 15.56.15 Reloaded log records a missed right-to-up turn at
world fine coordinates (3968,9856). Crono continued right while the instruction
finished. Every off-route animation tick started another search, yielding
alternating quarter-step corrections and 94 plans by 10:58:47.

Guidance now gives a single advance cue within four steps of the end of a long
leg. If the player leaves the route, it says "Off route. Stop moving for new
directions." It waits for the movement/action keys to be released and for the
position to remain still for 200 milliseconds before searching again. This also
allows an eight-pixel world step already in progress to finish. The replacement
route is announced even if its first direction matches the previous instruction.
K repeats the request to stop while recovery is pending. Returning to the
existing route resumes its directions immediately.

I remains spoken guidance only. It never inserts a direction or prevents manual
movement. P retains its separate automatic steering, cancellation, and blocked
movement checks. Local steps remain 16 pixels; world steps remain eight pixels.

Four focused tests cover the advance cue, replay the observed missed turn and
native step settling, recover when the player returns to the original route,
and avoid asking an already stationary player to stop after terrain changes.
The first two failed against 0.3.7; the terrain case caught a review finding.
The focused replay and all 113 Core tests pass
after the change. The small test passage reproduces the logged route shape and
motion sequence; it is not substituted for the game's live collision map.
