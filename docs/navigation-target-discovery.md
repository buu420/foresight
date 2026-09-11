# Empty navigation lists, 0.3.2

The 2026-09-10 23.26.09 Reloaded log captures scene 2 at first control, with a
64x64 map, 20 actors, player (6308,2303,1), and native viewport
`[4096,8320) x [256,3456)`. Every category reported no destinations.

## Confirmed exit visibility failure

The installed `MapJumpOffsetTbl.dat` maps scene 2 to record 4. That eight-byte
record is `170d810b01000c06`: exit 0 occupies column 23, rows 13 and 14, and leads
to scene 1. The PC record decoder in
[CTViewer](https://github.com/GitExl/CTViewer/blob/main/src/filesystem/scene.rs)
and Ghidra's audit of loader RVA 179F90 agree on the fields and native expansion.
The supported EXE SHA-256 is
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

Row 13 begins at fine Y 3328, so its upper half intersects the captured window.
Its center is Y 3456, exactly the exclusive bottom edge. The old point test
therefore hid the only exit and the Story Event bound to it. A regression using
these coordinates failed with an empty target list before the repair.

Exit and rendered chest tiles now use rectangle overlap with positive area.
Touching an edge alone does not reveal a tile. Version 0.3.2 also limited approach
goals to the visible tiles. That restriction prevented routing to this known
staircase's reachable entry on row 14. Version 0.3.3 separates destination discovery
from its connected entry geometry; see [the stairs route repair](navigation-stairs-route.md).
Existing discoveries remain valid under the previous scene rules.

## Visible People and interactable Objects

The old source applied the native confirm-action predicate to every actor.
People now admits drawn, usable non-party actors whose class is known and not
removed. It includes visible people and creatures without promising a talk
action. The Interactable Objects category retains its native action predicate.
Hidden, removed, party, and unknown-class actors remain excluded.

The bedroom packet `Atel_0324` gives cat actor 9 visual 3B hex. Both its activate
and touch pointers are 0768, whose first opcode is 00, return. Having no action
does not make a visible cat an invalid navigation target. Synthetic zero-action
fixtures cover this distinction and the separate Objects gate. Actual actor
flag values were not present in the failing log, so the fixture does not claim
to reproduce those missing live values.

Claude's follow-up Ghidra audit confirmed that 1760B0 is reached from the confirm
path at 15D5C4 as well as the script special case at 161669. An earlier inference
that byte actor+152 was always zero for ordinary NPCs was withdrawn: a census of
literal writes cannot exclude bulk initialization. The repair does not change
that native predicate or depend on the withdrawn inference. Raw actor facts are
now logged with target counts on the first read and when counts change, with
at most 32 actor records and no repeated output for unchanged inventories.

## Guide and validation

The user supplied the [PC listing of the vinheim and Bkstunt_31 guide](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/64344).
Its opening section was read from their Chrome tab. The listing contains the
same version 1.6 DS guide as the previous link. Installed PC resources establish
coordinates, actors, exits, and current story state. The guide supplies only the
opening sequence; its text and resource binaries are not redistributed.

The Release suite passed 933 tests: Core 85, Prism 8, Native 497, Mod 343. New
coverage includes the captured bedroom viewport, all four tile edges, wholly
unseen tiles, visible characters without actions, Objects eligibility, and
bounded inventory diagnostics. Gameplay has not been driven or observed through
computer control. The user's next test must check the cat, stairs, Go downstairs,
and then I/P movement to the stairs. Objects may be empty where no eligible
interactable is visible; an empty category is not filled with invented targets.

Version 0.3.2 was deployed on 2026-09-10 with 25 deployment checks passing.
All 27 checksummed mod files plus the checksum manifest matched the package, and
all 45 loader/shared-hook files matched the vendored checkout. The prior loader
runtime configuration's CRLF conversion still normalizes to its SOURCE.md hash.
The game executable stayed unchanged. The previous installation is retained at
`Accessibility/Backups/20260910-184946`.
