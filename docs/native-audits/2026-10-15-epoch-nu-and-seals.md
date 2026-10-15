# Nu, sealed landmarks, Epoch access and quest introductions

This audit supports Foresight 0.3.62. Four unique October 15 gameplay logs were
matched to the reports; the fifth supplied file was a byte-identical duplicate.
No game was launched or controlled, and no story state or physics was written.

## Supported native evidence

The executable SHA-256 is
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`;
resources.bin is
`09914BF4A8944C0708C947E7EB5683AEE4ED48BC6D5D1D75FA07B68131653C7E`.
Fresh Ghidra 12.1.2 exports were read-only and hash-gated. They are native
pseudocode, not recovered managed source. The installed resource formats were
cross-checked against the primary [CTViewer source](https://github.com/GitExl/CTViewer);
the installed packets and executable establish the behavior below.

## Nu and sealed doors

Enemy IDs 0 and 130 use the Nu species identity. Native load_enemy at RVA
16B220 stores class 5, or class 6 for its high-bit load mode, while retaining
the enemy ID as the visual index. Class 4 uses a separate sprite table.
Nu labels require a reviewed NPC placement, drawing and call gates, and an
available native Talk/Menu action without an available encounter. Other
placements retain their existing behavior. Keeper's Dome dialogue identifies
its construct as Strange Construct; the labels do not expose its later identity.

| Scene | Door markers | Native open local |
|---|---|---|
| 208 | 8 | 7 |
| 210 | 17 | 7 |
| 217 | 9 | 0A |
| 241 | 12 and 13, canonical 13 | 8 |
| 242 | 14 and 15, canonical 15 | 8 |

The old label window, story 51–77, incorrectly discarded the first three door
names on later visits and omitted the Dome doors. The new closed-state labels
use each handler's own local. Forwarding markers share a destination and Confirm
geometry. Below the native story gate 0xAB the relevant seals remain locked;
the Hallway door has no separate story gate. Native collision and terrain-copy
conditions still control every route through a doorway.

## Forest Ruins pyramid

Scene 42, Atel0423, draws the pyramid in the map. It is not a Confirm actor.
Before global 19C bit04 is set, it is listed as Pyramid. At story 0xAB or later,
the actor0 loop reacts to leader tile row18, then runs the pendant sequence,
removes the pyramid tiles, sets 19C/04 and shows its contents. Guidance ends on
the native approach row. Before that sequence the contents remain hidden;
the mod supplies no contents or reward-choice hint.

Installed terrain fixtures verify both guidance modes across the native
entrance level change and to the closed Dome door approaches. These are offline
route regressions, not completed live traversal.

## End of Time Epoch dock

Scene464 Atel0283 actor36 is a sprite-less marker at fine coordinates
(10112,6399), tile(39,24), layer1. The old catalog mis-sized A4, missing its
boarding branch and offering an unrelated encounter at this position. The
dedicated provider requires story174 or later, the native parked-Epoch bit
AB/80, its exact marker and native dispatch gates. After story203 the script's
intervening conversation gate is also respected. Owning an Epoch parked
elsewhere does not create this dock.

Approach uses native touch geometry (178980/16EF30). The game then asks its
boarding Yes/No question; the mod never answers it. The provided log at 09:52:30
already contains this native question and the player declining. That is evidence
that the native interaction was available in the reported session. The logger
printed only the first 32 actor records, so the individual actor36 dispatch
fields were inferred from successful native execution and installed initialization.
New helper and integration tests cover the exact installed floor, both navigation
modes, missing/unknown/late state, off-camera availability and stale-target removal.

## Optional introduction guards

Fiona's first request sets global1AD/80 in Atel0467 actor8 at file0201 (actor9
also sets it). Global0F7/02 is the earlier sapling consent, not this introduction.
Remote desert guidance now needs the introduction or actual later progress.
Three premature initial desert objectives were removed. The actual local NPC
remains in People. The undisclosed combat-weakness hint was removed.

Other audited classic starts use their native introduction/progress flags or
stay local to the visible interaction. Unaccepted bonus requests stay in their
own meeting room; accepted/progressed requests retain remote guidance. Routed
quest directions are Story Events rather than impersonating a remote NPC or
object. This audit does not establish that every later accepted-quest puzzle
instruction is already known to the player.

## Recorded action proofs

Atel0163 is 2159 bytes with SHA-256
`31EB20EC02C914BE51D48F153C1FAE386757EAC1B0F6F1349F0702BE3A532B65`.
The visible class5/visual0 actor14 uses c263. Rendered frames0C and0D support
the physical arm/mouth and settling descriptions. Exact runtime PCs610,5FF,
639,61D and608 are matched with the full loaded-script digest and on-camera
subject. The timed upward move additionally requires native movement state,
remaining duration, direction and actual velocity after handler16CB50/17B1B0.
Static poses require the applied frame/mode from16EA90. Held cues retain the
existing textbox, scene, foreground, battle and movie cancellation rules.

Movie006 is identified by its decoded size and full digest and uses the existing
native movie-session narration contract. See [the reviewed script](../movie-narration-0362.md).
Muted x86 native topology probes of this exact film/master passed natural
completion, pause/resume and early close with zero failed events. This is
standalone media-pipeline evidence, separate from game-session hook and listening
validation.

The final full managed suite passed 3,718 tests with no failures or skips
(Native 1,166; Core 226; Prism 10; Mod 2,316). The release script, AMM manifest
and deployment-path checks also passed. Final packages are rebuilt from the
release tag and checked separately from these source tests.

All 156 previously shipped WAVs, 41 protected game/resource files and ten local
save/settings files were checked for preservation. Full release/deployment and
public download evidence is recorded separately in the publication audit. No
live controller, boarding, traversal, Steam launch or audible listening test
was performed for this update.
