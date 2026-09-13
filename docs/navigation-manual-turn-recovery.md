# Manual guidance, 0.3.9

The player asked for one instruction at a time: "Right 5 steps," followed by
"Up 1 step" on reaching the turn, with corrected directions after a deviation.
The 2026-09-13 16.50.39 Reloaded log confirmed that both starting guidance and
replanning could announce three legs together. The earlier advance cue also
spoke the upcoming direction before the current leg was complete.

Manual guidance (I) now announces only the current leg. On reaching its end, the controller reads
the next leg. K repeats the destination and remaining distance in the current
leg. There is no advance announcement of the following turn. When K and a new
manual turn or recovery message occur on the same input frame, that new message
answers the repeat request once. Automatic walking with P retains its initial
three-leg overview and existing movement checks.

If the player leaves the route, guidance says "Off route. Stop moving for new
directions." It waits for movement/action keys to be released and for the
position to remain still for 200 milliseconds, then searches from that position
and announces "Route updated" followed by only the current corrected leg.
K repeats the request to stop while recovery is pending. Returning to the
existing route resumes its current leg immediately.

This preserves the 0.3.8 correction for repeated searches while Crono was still
moving. The 2026-09-13 15.56.15 log recorded an overshot right-to-up turn at world
fine coordinates (3968,9856), alternating quarter-step corrections, and 94 plans
by 10:58:47. Native world movement can finish an eight-pixel step after key
release; the existing Ghidra decompilation of RVA 0x264C40 confirms the position
update during movement. Both input release and position settling remain part
of recovery. This release changes managed speech, not native movement hooks.

Manual guidance never inserts a movement direction. Local steps remain 16 pixels (256 fine
units); world steps remain eight pixels (128 fine units). Step counts measure
map distance rather than key presses or individual footstep sounds.

The focused tests cover an entire three-leg route, silence before each turn,
remaining-distance repeats, departure and recovery through another passage,
native position settling, return to the old route, and changing terrain while
already stopped. Runtime tests cover K coinciding with a turn or recovery in
both local and world input paths. They also check that manual guidance preserves
physical input and does not add movement. The new speech assertions failed
against the previous behavior before the fixes were applied.

The full Release suite passed on September 13, 2026: 120 Core, 532 Native,
414 Mod, and 8 Prism tests, for 1,074 passing tests with none skipped.

The test passages reproduce route shapes and motion sequences; they do not
replace the game's live collision map. Player testing is still needed to assess
the timing and clarity of the 0.3.9 instructions during actual gameplay.

The requested sequential design is also consistent with W3C's supplemental
[guidance on clear, step-by-step instructions](https://www.w3.org/WAI/WCAG2/supplemental/patterns/o4p07-step-instructions/).
