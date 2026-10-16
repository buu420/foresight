# Mountain of Woe route-search limit

The October 16 report repeatedly exhausts the route search in scene 392,
Mountain of Woe Station 3, story point 180. The target is exit 1 toward Station 5,
also selected by `story:full:woe-summit`. Both have 36 approach points. The
recorded player positions are 5504,13439,layer 1 and 8755,7111,layer 1. Exit 0
back to the base still routes successfully.
Earlier failures in the same report occur at 5472,13055,layer 1 and 5248,12415,layer 1
(lines 5649/5659 and 9142).

The log has 12,294 lines / 2,838,229 bytes and SHA256
`8bdb7d88bf5c2516737a0a415da774f7f1dd0c27e22868f030d5cf5ca8dcc50c`.
It contains actor inventories at lines 11942/12150, but no compressed live
terrain packet for scene 392. Its eight earlier packets concern other scenes;
they are not used as substitutes for this map's live state.

## Native evidence

The supported executable SHA256 is
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Installed scene 392 uses Atel 0506 and MapTable 0186. Its native exit 0 at tile 21,57
returns to scene 394; exit 1 at tile 63,48 leads to scene 393, Station 5. Existing
Story Events selects the onward connection correctly.

Resource decoding follows [CTViewer's PC format reference](https://github.com/GitExl/CTViewer).
Fresh hash-gated Ghidra 12.1.2 exports, opened read-only with analysis disabled,
check native movement entry RVA175E90, corner probe RVA175EE0, terrain/region RVA178C50,
layer and exit commit RVA178FF0, and directional wall-slide handlers RVA1761C0,
RVA176810 and RVA176AE0. These are native pseudocode, not recovered managed source.
The route continues to use the existing body/corner, terrain, layer, actor and
exit checks; the repair changes only the search ceiling.

## Reproduction and repair

The private probe and public fixture combine installed initial Map 186 with
the 27 actor facts logged at each reported pose. Unlogged facing, render and
script fields and complete story arrays are synthetic. This reconstructs
the failure; it does not establish the exact live map or all native flags.
The earlier two failure poses reuse the 11942 actor frame with the logged
leader position substituted; this does not claim exact actor captures for
those earlier moments.

Both onward searches hit the old 131,072-position budget. With 262,144 available,
the counting probe finds 927- and 788-point paths respectively, after expanding
132,252 and 141,758 distinct positions. The return routes take 20 and 173 points.
Fine-coordinate starts require more search than the aligned entry position
used by the older initial-map audit.
The two earlier poses also fail under the old budget and succeed under the new
one, with 922/925-point paths after 130,668/130,667 expanded positions. Their
return routes take 27/41 points. Expansion counts differ from positions admitted
to the queue, which is what the ceiling bounds.

The same explicit-budget comparison also succeeds under the installed private
32-bit .NET runtime. This exercises the routing probe in the game's process
architecture without launching or attaching to the game.

`DefaultMaximumVisited` is raised to 262,144 distinct positions. Heading state
remains bounded at nine per position; the 64-goal and 16-neighbour limits and
explicit smaller caller budgets are unchanged. No floor edge, bridge, native
interaction, target identity or story prerequisite is added or bypassed.

## Validation limits

The initial 16-case regression suite produced 14 failures and 2 passes on the
old limit. After the ceiling change, all 16 passed. Review found two additional
logged failure poses; coverage was expanded to 32 passing cases. It exercises all four
poses through Exits and Story Events in both navigation modes, checks the
return exit, retains explicit small-budget failure, and drives the navigation
controller across the mountain using an independent single-frame native
walking oracle. That oracle was transcribed from the verified movement
handlers; simulated inputs are not live gameplay.

The complete 3,816-test managed suite passes (Core 226, Mod 2,402, Native 1,178,
Prism 10). The 28 release regressions, 6 deployment-path checks and canonical-path
check also pass.

The report's route failure alone cannot prove there are no additional live
terrain changes or moving actor obstructions. Live Mountain of Woe traversal,
physical controller behavior, Steam launch and audible screen-reader checks
remain pending. No game launch or gameplay input was performed for this fix.
