# Denadoro navigation transitions in 0.3.49

The user approved identifiable verified drops/jumps and level crossings, with
manual guidance and automatic walking continuing through native movement while
preserving direction and restrictions. Earlier release and local deployment
authorization continued. The tester supplied
`2026-10-09 16.06.36 ~ Chrono Trigger.txt` from Downloads.

## Report and native evidence

The log shows inaccessible exits in Denadoro scenes 142–145 and normal native
layer changes in scene 144. In scene 143 it records a move from approximately
`(1392,3071,1)` to `(1392,4675,1)` accompanied by the reported jump. Walking-only
collision search cannot bridge that scripted drop. Some scene 142 and 145 exits
belong to separate genuine walking components; this release does not manufacture
connections between them or plan across an unobserved scene change.

Read-only Ghidra analysis used the installed executable with SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Fresh selective exports cover native actor contacts, movement, collision-layer
acceptance, script dispatch, function waits and party joins. Installed
`Atel_0187` and `Atel_0145`, scene actors and collision maps provide the bounded
handler definitions. Claude's independent native audit and implementation
review are additional evidence; production decisions and verification remain
with the primary agent.

[CTViewer's movement decoder](https://github.com/GitExl/CTViewer/blob/2e5a206e09f0028fd5a1ca6cb9a9ed18bfc64fea/src/scene_script/decoder/ops_movement.rs)
is a primary format reference. Installed handlers and the supported executable
establish the actual game behavior. Opcode A0 moves an actor to a tile; enemy
self-movement scripts and camera-only calls are not party traversal.

| Scene and actor | Native contact and restriction | Native party landing |
| --- | --- | --- |
| 143 actor 9 | Touch function 2; approach from above; local 19 busy state | Tile 5,18, layer 1 |
| 144 actor 16 / proxy 21 | Touch; local 100 busy and shared local 101 unused | Tile 38,42; stored layer 3, then walking layer 1 |
| 144 actor 17 | Touch; local 100 not busy | Tile 42,52, layer 1 |
| 144 actor 19 | Touch; shared local 101 unused | Tile 49,49, layer 2 |
| 144 actor 18 | Confirm or touch; shared local 101 unused | Tile 49,49, layer 2 |

Each handler disables native control, waits on the called party movement and
joins the followers before restoring control. Function 07 waits for the called
function; DA waits for the party join. A0 stops at the first fine position inside
the destination tile rather than an assumed center. It does not assign a new
collision layer. Navigation accepts the verified landing tile and layers, then
captures the real floor again. Scene 144's layer-3 waterfall approach and
layer-1 walking continuation are checked against native collision rules; live
traversal remains a required tester check.

## Player-facing behavior and safeguards

Verified scenery gets distinct, generic labels. Ordinary level crossings are
derived from native layer-3 floor cells that provably connect both walking
layers. Adjacent cells form one named crossing. A layer-3 cell alone does not
qualify; a waterfall contact is not an ordinary level crossing. Geometry caches
include shapes, terrain, layers and exit cells. Live actor collision remains in
route search.

Drop planning returns only the normal walking leg to a verified contact. A
landing preview proves an eventual route exists; it never becomes a walking
edge or an input authorization. The native script owns party movement. At the
approach, speech identifies the drop and asks the manual player to continue
down. Automatic walking uses normal direction input to enter that contact.
During the native movement pause it sends no navigation input. It then resumes
only after the expected landing and the owning handler's return are observed and
a fresh route is captured. Restored control before AD/77 busy-flag cleanup does
not authorize the next leg. The aligned contact tolerates the moving chute
carrying the player past its exact approach between input ticks. Manual guidance
returns to the approach if the player steps away, while forward chute movement
keeps the contact armed.
An observed landing remains confirmed during the handler's final wait, so a
manual player can keep moving while it finishes; replanning uses the live
position after its return. The confirmation clears when navigation stops,
completes or changes transition.
Two consecutive native drops in scene 144 are supported without reusing a
consumed shared one-use waterfall.

Native-script ownership requires the exact scene, class-7 scenery actor,
position, call/processing flags, script priority and current program-counter
range. Closed native control alone is insufficient. Missing locals, changed
actors, blocking loaded flags or offsets, unexpected scripts/landings and a
bounded 20-second ownership window stop or withhold the route. That window is a
conservative mod timeout, not a native timing constant: three members move in
sequence on the longer slides. Keyboard continuation retains the prior approach
when the native game removes every airborne input callback. Manual actions, focus/scene/engine
changes, capture failure, stale controller polls and controller disconnects
continue to cancel navigation. The input-callback gap exception applies only to
an owned, freshly verified transition; it does not reopen general cutscenes.
Native Confirm and battle actions are never synthesized.

## Verification and limits

The new regression cases were observed failing before their implementation.
Installed map fixtures reproduce the reported missing routes, actual logged
landing coordinates and the ordinary level change near `(11915,2228,2)`.
Tests cover manual/automatic contact guidance, script waits with no navigation
input, fresh landing replanning, direction and shared one-use restrictions,
distinct labels, controller ownership/freshness and cancellation boundaries.

The full managed suite passes 2,671 tests: Native 902, Core 195, Prism 9,
Mod 1,565, including 176 hook byte contracts and supported executable identity.
This establishes source behavior against native-derived fixtures and captured
states. It does not establish a completed live Denadoro playthrough, audible
Prism output or physical controller delivery. Package, deployment and public
download evidence is recorded in the publication audit.
Independent review found no remaining important issue after busy-flag cleanup,
longer native waits, moving-floor contacts, keyboard-only handoffs and continued
manual movement after a confirmed landing were covered by regression tests.
