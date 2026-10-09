# Ioka feast and identifiable people in 0.3.50

The October 9 tester log shows scene 280, story point 0x6F. Marle (actor 3,
class 1, visual 1) and Lucca (actor 4, class 2, visual 2) are drawn, loaded active
party members with enabled calls. Actual Lucca dialogue appears later in the
log. The old navigation filter removed every party member. Ayla (actor 6,
class 3, visual 5) received the generic Person label.

Read-only Ghidra analysis and installed Atel_0371 verify direct companion
interactions. Field Confirm at RVA 17D0C0 calls 17D230's facing scan, which skips
the leader rather than every party member. Dispatch at 17FA20 calls function 1
when the native call gates and script priority permit it. The separate 1760B0
party-excluding list belongs to script opcode 31. There are no companion proxies
in this scene. Marle and Lucca have their own dialogue handlers at file offsets
049E and 05EA. The supported executable remains SHA-256
8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7.

The catalog compiler now recognizes opcode 80 character loads when function 1
has a completely expanded native Talk path. This adds 24 character talk actors
and expands nine existing character identities across the installed scene
catalog. Every preexisting action, guard, region, exit and non-actor metadata
record is unchanged. Live class/visual identity, drawn/loaded state, map bounds,
script processing and current script still decide eligibility. Opcode B0 (16EC10) runs
ordinary party following for classes 1 and 2; those actors remain excluded.
Unreadable current instructions do not qualify as companion NPCs. The camera's
activation binding and temporary call gates control Confirm readiness, not
whether a discovered NPC remains listed. Lucca's idle loop disables calls during
AB 24 at Atel_0371:057A; she remains listed while guidance waits. Routes account
for every native scanned competitor, including busy companions and followers,
and never send Confirm automatically.

Current names are read from the same native saved-name strings used by 14830:
global actor store at image+41B4C4, strings at store+1908+character*24. Native
character identity is the visual index for classes 0 through 3; class-4 NPC
visuals have the separate +7 sprite bias. Invalid, missing or changing name data
does not invent a character name. Existing curated labels remain, while reviewed
PC artwork supplies appearance labels for common unnamed people. In particular,
c037 and c038 distinguish short-haired and long-haired people in green; c013
is described by its blond hair and blue clothing without assigning a story name.
Repeated generic appearance labels receive scene-local aliases ordered by native
actor index. Movement, native enumeration order and disappearance do not rename
a surviving target. A new visit resets the aliases.

The native keyboard/gamepad dialogue still asks for Confirm presses when the
drinking contest starts. The old mod instruction applied to the entire feast,
before the companion conversations. The new objective describes those
conversations and leaves the actual contest rules to the native dialogue. No
claim that holding Confirm wins, automatic contest input, or hidden outcome
guidance is added.

The installed-map decoder was cross-checked against
[CTViewer's primary source](https://github.com/GitExl/CTViewer).
Private evidence is under artifacts/research/ioka-party-0350; game textures and
dialogue dumps are excluded from public packages. Claude Teammate assisted the
read-only native audit; the implementation and regression checks were reviewed
in this workspace.

All 2,705 managed tests passed. Initial reproductions failed for missing party
targets and identical Person labels; later cases reproduced competing companion
Confirm selection, camera culling, follower filtering and Lucca's temporary
call-disable state before correction.
These checks establish offline routing and capture behavior. Live gameplay,
Prism listening and physical controller delivery were not performed. Automatic
walking retains its existing 1.5-second pending-Confirm bound; Lucca's native
idle-animation timing needs a live arrival check.
