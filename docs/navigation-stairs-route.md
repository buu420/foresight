# Stairs route repair, 0.3.3

The 2026-09-10 23.53.22 Reloaded log confirms that Exits and Story Events are
populated in scene 2. From player (6308,2303,1), both I and P report no route to
the stairs before injecting movement. The search limit was not reached.

## Reproduction and repair

The installed `MapTable_0060.dat` was decoded offline into its three native
64x64 property planes. Ghidra's audit of DB1F0 confirms the three-byte RLE
expansion, and 173750 confirms the cell property lookup. The decompressed asset's
SHA-256 is `344C6F7319E0A73C0CE9D72E34560CC74A856C524C341D558B6F31E71613E8F9`.
The supported EXE hash is unchanged from the preceding native audit.

The known stair exit covers (23,13) and (23,14). The captured viewport ends at
fine Y 3456, exposing part of row 13. Restricting goals to that row produces only
(5952,3456,1), (5952,3520,1), and (6016,3520,1). None is reachable with the native
body clearance checks. The same graph reaches 406 positions, including usable
entries on row 14 that were omitted from the destination.

Once any tile of an exit is visible, its connected native footprint now supplies
entry points. Flooding uses cardinal adjacency within the same exit id. A hidden
exit or a disconnected reuse of the same id must be discovered separately.
Previously valid goals are retained, the 64-goal bound remains, and unrelated
exits cannot become route shortcuts. Story Events inherit the corrected goals.

With this change, the actual room and original viewport produce 12 goals and a
51-point route ending at (5952,3648,1). Body clearance and layer rules are unchanged.

## Native movement verification

Claude independently audited the corner predicate at 178FA0 and its caller
175EE0. Native movement can slide after a corner refusal, but deleting the
graph's corner checks is unnecessary for this reproduced failure. The complete
connected stair footprint supplies a route through the existing clear edges.
Claude's separate offline replay was inspected and rerun: all 188 native
substeps were accepted unchanged, reaching (5888,3584,1), the first point inside
exit tile (23,14). No physical layer changed along this approach.

An offline harness connects the actual `NavigationController` and
`FieldNavigationSource` to the installed EXE's 175E90 movement dispatcher in
Unicorn, with the decoded room planes and the logged starting position. At
normal speeds of 16 and 32 native units per tick, P reaches the stair footprint
in 192 and 96 ticks respectively. Both finish at (5924,3647,1), within the existing
two-pixel arrival tolerance, with one route plan and no blocked stop.

Research artifacts are in the local `chrono-trigger-intro` directory:
`extract-bedroom-map-033.py`, `nav-map-loader-033-codex.txt`, `route-repro-033`,
`controller-replay-033`, `native-controller-worker-033.py`, and
`controller-native-replay-033-summary.json`. Proprietary map data and game
binaries are not added to this repository or the package.

The replay disables scene transitions and dynamic chest/actor collisions; it
establishes terrain movement to the stairs, not the actual map jump. The user
must still test the transition downstairs in the running game. No game process
was launched, controlled, or attached to during this repair.

Regression tests first failed with the old source and then passed with the fix.
They cover a partly visible two-tile exit whose entry is offscreen, stable goals
after the exit leaves view, and exclusion of hidden exit ids, disconnected
same-id tiles, and diagonal-only contacts. The observed bedroom viewport fixture
now explicitly requires the connected row 14 approaches.

The final Release suite passed 935 tests: Core 85, Prism 8, Native 497, Mod 345.
Version 0.3.3 was deployed with 25 checks passing. All 28 mod payload files
(including the checksum manifest) match the package, and all 45 loader/shared
hook files match the vendored checkout. The executable remains unchanged.
The previous installation is retained at `Accessibility/Backups/20260910-191128`.
