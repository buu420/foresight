# Counted navigation and opening Story Events, 0.3.1

The user approved U/O for categories, J/L for targets, K for repeat, I for manual
guidance, P for automatic walking, and Story Events as a fourth category. Following
the first test, they requested counted 2D instructions such as left 3, then down 2.
One step is a 16-pixel tile, or 256 native fixed-point units. These changes were
developed offline; the user owns game control and the next live test.

## Reproduced failures and fixes

The 2026-09-10 21.50.05 Reloaded log identifies scene 2, a 64x64 map, layer 1,
and 20 actors. It records rapid direction changes under I, and blocked stops under
P. That build did not log player positions, so the exact live blocked point is unknown.

- Equal-length routes could have eight turns where one turn sufficed. Search now
  retains incoming heading and prefers fewer turns among equally short paths.
- Alternative exit goals and passing small waypoints restarted instructions.
  Route progress follows whole cardinal legs and keeps an existing valid approach.
  Large exits retain their bounded set of discovered goals as the player and camera
  move; live removal of an exit cell invalidates its old approaches.
- Steering used diagonal correction across untested corners. It now uses cardinal
  movement, matching the graph's proven edges.
- A 48-unit stress replay could stop 32 units before a final goal and time out.
  Final arrival allows two pixels; waypoint and steering tolerance remain one.
  Native input audit establishes ordinary 16/32-unit walking; 48 is a stress case.
- Exit goals sit at least four pixels inside their cells. Search can enter the
  chosen exit's footprint while preventing unrelated exits from becoming shortcuts.
- The old graph checked only feet. Native movement checks the leading body corners,
  so the graph now checks those corners before each pixel of a cardinal edge.

I announces up to three counted legs initially, then the new leg at each turn.
K repeats remaining legs. Category changes, manual override, loss of control,
scene changes, menus, and unreadable state preserve the existing stop behavior.
Command, route, position, injected pad, and stop diagnostics make the next live
failure inspectable without logging idle frames.

## Native evidence

Audited EXE SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
All addresses below are RVAs. Ghidra disassembly/decompilation and isolated Unicorn
execution were used; no live game process was driven or attached to.

The field tick calls input at 17355B and collision dispatch at 17356B when their
native control flags permit. Dispatcher 175E90 chooses right 175F70, left 176780,
up 176810, or down 176AE0 from signed mover A0/AC. Actor FineX/FineY at 84/90 are
copied to mover 148/14C at 176ABE..176AD4, with no scaling.

The horizontal leading edge uses X +/- 70 hex, at Y and Y - 70 hex. Vertical
movement uses X +/- 70 hex, at candidate Y for down and candidate Y - 70 hex for
up. Helper 175EE0 checks each corner's terrain, using 178C50 and 178FA0. Corner
checks do not commit the player layer; 178FF0 later commits the foot's result.
Seven pixels are 112 fixed-point units. The game's special smaller corner offset
in scene 163 hex is not implemented; the ordinary footprint is conservative there.
Routes choose clear cardinal edges and do not depend on the native wall slide;
some tight passages may therefore be reachable manually before being routable.

An isolated run of the actual 175E90 dispatcher on a synthetic 4x4 map with tile
(1,0) blocked reproduced the mismatch:

| Feet | Requested displacement | Native displacement |
|---|---|---|
| (320,256) | (16,0) | (0,16) |
| (320,384) | (16,0) | (16,0) |

Thus earlier body checks can slide along walls. The final 1791D3 layer-refusal
branch zeroes both components, but does not describe every collision response.
The supposed +/-256 second collision probe was rejected after tracing its only
consumer: it records a terrain flag and never blocks movement.

Research scripts and outputs are in the local `chrono-trigger-intro` research
directory: `verify-navigation-body-codex.py`, `navigation-body-native-vectors.json`,
`nav-body-functions-codex.txt`, `nav-dispatch-functions-codex.txt`, and
`nav-story-native-codex.txt`. Native actor collisions, chest collision cells, and
special map rules can still interrupt an otherwise valid static terrain route.

## Guide and catalog bindings

Reference: [Chrono Trigger Guide and Walkthrough by vinheim and Bkstunt_31](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/64344),
version 1.6, Overture (vb501) and The Millennial Fair (vb502). The user supplied
both the SNES listing and this PC listing. Both were read from their open Chrome
tabs after direct web access failed. The PC page still identifies the underlying
guide as the DS edition; PC-specific behavior is checked against the installed
executable and resources rather than inferred from the listing's platform.

The opening guide describes going downstairs, speaking with Mother, and leaving
for the fair. It later describes Lucca's telepod. Those facts guide the catalog;
the mod does not copy the walkthrough, expose rewards, or derive coordinates
from prose. Rendered unopened treasure chests remain native targets.

Installed Mapinfo scene 1 selects Atel_0323 (kitchen), scene 2 selects Atel_0324
(bedroom), and scene 8 selects Atel_0028 (telepod area). Ordinary Mother is NPC
class 4, visual 25 hex, actor 15 in the kitchen or actor 8 upstairs. Other visual
branches do not inherit the name. The telepod is scene 8 actor 11, class 4,
visual 63 hex. MapJump tables identify kitchen exit 0 as outside, kitchen exit 1
as bedroom, and bedroom exit 0 as kitchen.

Opcode 18 reads the low byte of ActorBase+110B0 at 161E80; opcode 5A writes it at
16257C. Atel_0324 sets story point 03 before first control. Expanded global bytes
use ActorBase+110B0+index*4 (168BD8/168BF5). Kitchen startup sets global 0140 bit 0
after Mother's first conversation and naming prompt, so the catalog reads
ActorBase+115B0 for that flag. Missing or changing story reads omit Story Events
without removing ordinary field targets.
The existing top-menu reserve threshold also reads this global story value as a
dword and tests it against 49 hex; its name describes that consumer, not a different
interpretation of the storage. There is no byte-versus-dword conflict.

Story Events are limited to point 03: upstairs with the introduction flag clear
offers downstairs; the kitchen offers Mother until that flag is set, then the
front door. Targets must already be visible or discovered. The catalog is
recomputed each capture and does not persist objectives across a loaded save.

## Validation boundary

Final Release suite: 920 passed (Core 85, Prism 8, Native 497, Mod 330).
All 122 hook contracts matched the installed executable. Deployment completed on
2026-09-10 with 25 checks passed, 27 packaged mod file hashes matching installed
files, and 45 loader/shared-hook files matching the vendored payload. The prior
version is in `Accessibility/Backups/20260910-173645`. No game was launched.

Regression tests cover counted speech, fewest-turn routes, stable alternative
goals, native-unit movement replay, body clearance, exit footprints, category
cycling, current-story gates, visibility, capture failure, and diagnostic throttling.
The next live test must verify I/P from the bedroom, the actual stairs transition,
Mother and the changing Story Events, and P/manual/menu cancellation.
