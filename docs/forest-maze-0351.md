# Forest Maze navigation in 0.3.51

The October 9 tester log enters Forest Maze scene 282 at story point 0x7B,
position (2304,767), physical layer 1. It reports no route to exit 1. The story
target for the Reptite Lair instead chooses the northern entrance. The log has
older actor diagnostics, but the same failure reproduces against 0.3.50's
unchanged walking graph and the installed map, without actor collisions.

The supported executable remains SHA-256
8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7.
Installed scene 282 uses Atel_0490 and MapTable_0161, with a 64 by 48 collision
map. MapJump records 457 and 458 both name world scene 499, but their arrivals
are (68,88) and (68,95), in eight-pixel overworld coordinates. Treating a whole
overworld as one graph node loses the distinction between disconnected areas.

Read-only Ghidra analysis of 263150 and 25FDC0, and comparison with the executable's
world header at RVA 39A918, verify world 3 loads Map_0004 and Id_0003. The captured
collision plane is the second 96 by 64 map plane. Native 267E40 and 264C40 check
the two leading eight-pixel properties before walking. The northern arrival
connects to Ioka and the northern entrances; the southern arrival connects to
the Reptite Lair. An installed-map regression proves only the southern arrival
can walk to the lair entrance. Forest passage look-ahead therefore considers
those verified continuations, excluding return through the same maze or the
abstract overworld node. Current captured exit destinations still override
the catalog. The doorways are labelled Outside, north side and Outside, south
side, including the story reminder's next-passage label.

The maze's diagonal paths are also disconnected in the old mod graph. Cardinal
native input can move sideways when one leading body corner hits terrain.
Ghidra's horizontal and vertical movers, including 1761C0, 176810 and 176AE0,
contain these slide and retry paths. The planner must account for those native
movements rather than connecting physical layers freely. Core guidance carries
the graph's verified input command per edge, so a sideways slide does not cause
an incorrect perpendicular correction. Manual guidance recognises progress on
the same verified segment; departure and manual cancellation still work.
Passage, terrain-copy and drop wrappers retain those commands through staged
plans. Previewed native drops remain staged transitions rather than walking
shortcuts.

Independent native frames exposed two Dash failures: a layer sliver at
(8928,10512,2) on the return route, and a repeated plan at (6272,2048) from the
logged arrival. Guidance and auto-walk now replan once per tile and layer after
250 ms without progress, using the actual position. Manual recovery requires a
held press facing the spoken leg. The existing automatic blocked stop remains
at 1.5 seconds. Straight-leg steering leaves offsets under 32 units alone when
correcting them would retrace the preceding turn. This avoids reversing a
native layer change, and retains the original steering deadband.

Ordinary level crossings are no longer limited to three Denadoro scenes.
Candidates require a layer-3 region whose native walking graph reaches both
physical layers nearby. A layer-3 tile alone does not qualify. Connected cells
form a target with a unique letter, available through the existing area's guide
rules. Collision data and current native restrictions still control movement.

The look-ahead audit covered 391 scenes from static field and world arrivals.
It found the same missing movement pattern in Forest Maze, Reptite Lair scene
290, Giant's Claw scene 196, and Death Peak scenes 262 and 264. This bounds the
audit: script-only arrivals, live actors, treasure changes and later story
variants are not a complete game playthrough. Two initial terrain exports,
scenes 378 and 447, are unsupported by the existing offline decoder.
Death Peak scene 262 also has wind tiles. Its static terrain routes are checked;
the full native floor-push update is outside the independent movement replay.
Slides on moving floors, off-map probe wrapping and the special shortened head
in scene 0x163 are conservatively excluded from the new slide edges.

The resource format was checked against
[CTViewer's world reader](https://github.com/GitExl/CTViewer/blob/main/src/filesystem/world.rs)
and [tile-property documentation](https://github.com/GitExl/CTViewer/blob/main/docs/worlds/map_data.md).
Native executable behavior remains authoritative. Claude Teammate owns the
native slide audit and field graph regressions; core guidance, passage selection
and crossing integration were checked in this workspace. Private logs and
Ghidra exports remain under artifacts/research/forest-maze-0351 and are excluded
from the public packages.

All 2,806 managed tests pass. An independent single-frame movement oracle checks
28 combinations of plain installed terrain, arrival, walk/Dash speed and
manual/automatic mode, plus four traced frame samples. The cathedral replays
also use native frames rather than restricting movement to straight edges.
A separate controller/field-source replay reaches the southern exit from the
tester coordinates in all four speed/mode combinations. Claude Teammate also
implemented and reviewed the bounded Dash recovery. No significant review
errors remain. Forest passage look-ahead is bounded to the verified local
overworld regions; it does not claim further travel through world 499 for
destinations beyond their listed onward scenes.

Live maze traversal, Prism listening and physical controller delivery still
need tester verification. Automatic movement does not fight, choose dialogue
or press Confirm. The story-action description work remains paused.
