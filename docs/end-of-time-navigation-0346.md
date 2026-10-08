# End of Time routes and distinct exits, 0.3.46

The tester confirmed that the 0.3.45 bike and End of Time pillars appear, but
reported that the pillars cannot be distinguished and the door behind the old
man remains unavailable in both navigation modes. No new log accompanied this
report. The user also requested unique labels for repeated exit names.

## Native door evidence

The installed executable SHA-256 remains
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Addresses below are RVAs at preferred image base `00400000`.

Scene 464's `Atel0283` actor 0 writes the leading actor's facing to local `0B`
every loop, using opcode `24` at script offset `04AE`. Fresh read-only Ghidra
decompilation of native helper `165F50` confirms the facing read at actor offset
`69A0`. Its values are 0 up, 1 down, 2 left and 3 right. The actor-25 door touch
handler at `12AF` requires local `0B == 0`, story point at least 75, and local 8
equal to zero before copying tiles `(4,32)-(4,35)` to `(31,13)` and setting local
8. Local 11 is therefore an approach direction, rather than a persistent lock.

The previous fixtures initialized every local to zero, so they always faced up.
New cases starting down, left or right reproduced the unavailable route. The
planner now treats only this exact terrain-touch facing guard as a requirement
for the final movement. It accepts captured values 0 through 3 for planning;
unknown or invalid facing still fails closed. All story, handled-copy, actor
class and script-call guards remain live. Both contact paths offer only the
standing point below the door, and guidance says to continue up. No native
guard, input hook, game coordinate or story state is changed.

Ghidra's `178980` touch scan uses a distance check within 160 units. The upward
probe from `(8064,4352)` reaches the marker `(8064,4095)`. A non-upward facing
causes the handler to return without marking the copy handled. This supports
retrying normal upward movement, but the live timing still needs tester coverage.

## Two consecutive passages

The initial pillar platform also needs actor 24's stair copy before reaching
actor 25. Tests at story points 75 and 220 reproduced the gap in planning through
both openings. The native map loader clears the scene locals on entry, so later
visits need the same passage handling.

Continuation proofs for these two native markers may preview the remaining
marker after previewing the first. Each proof removes the marker it just used,
so there are at most two stages. The returned route contains only the first leg
on the currently captured floor. Navigation must capture the actual opened
stairs before approaching the door, and the actual opened door before entering.
The existing bounded wait stops if a passage does not open. Early story points
73 and 74 still have no route through the locked door.

## Distinguishing targets

Pillar actors 15 through 23 now have fixed labels **Pillar of light 1** through
**Pillar of light 9**. The initial three keep their numbers when the other six
lights appear. Their native drawing flags and availability still control which
targets exist. The destination remains the game's native Confirm prompt; the
labels do not disclose destinations before the player reads that prompt.

Field, world-map and flight exits with the same name receive suffixes A, B,
and so on: **Exit A**, **Exit B**, or **Outside, exit A**. Assignment uses stable
target IDs and remembers aliases for the current area and navigation mode.
Aliases survive player movement, native record reordering, and another target
disappearing. A previously unique name gains its initial suffix when another
same-named exit becomes available. A fresh visit or navigation mode resets the
alias table. Story binding and cached target data retain their original names;
target IDs, visibility, route goals and native arrival instructions are intact.

## Verification and live scope

Regression checks cover all nine pillar numbers, alias persistence and rollover
past Z, world and flight routes, non-upward door facing, invalid native facing,
story restrictions, both controller navigation commands at the upward contact,
and successive captured floors through the stairs, door and opened exit.
Independent native and code reviews found no remaining actionable defect.

Live movement and speech listening were not performed for this repair. Test
manual guidance and automatic walking from the initial pillar platform to the
door, from the old man's room to the door while facing different directions,
and on returning after Spekkio. Check all available pillar numbers and repeated
exit labels. If a passage fails, include the latest Chrono Trigger text log.
The native bike, party menu and race controls continue to use the 0.3.45 behavior.
