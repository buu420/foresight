# World navigation and transition recovery, 0.3.7

The 0.3.6 log ends local movement after Truce Inn's exit to world 0. The field
input hook does not execute on the world map. The next live session also caught
Settings presenting before the old top menu's destruction: the unidentified
parent close cleared the child's narrator, and its next focus change stopped
menu speech. Version 0.3.7 addresses both paths.

## Input and movement

The audited world walking-task CALL at EXE RVA 264B56 targets 264C40 and supplies
the current world context in ECX. Its frame callback reads the same held-pad
getter as the game through the input manager at EXE+41C3DC. The OR instruction
at 26536F combines normal and touch directions in EDI. A separate six-byte
instruction hook runs after that OR and contributes only direction bits. Both
hooks preserve registers, flags, and stack; no OS keys or coordinates are written.

The context's script-data and actor-base pointers must match EXE+41B4BC/41B4C4.
World ID at context+3324 must match renderer+290 and script scene 496+world.
The walking control gates are D+20980 bit 7 clear, D+2E27C=1, D+2E27E=0,
D+2E280=0. Position comes from D+2E283/285, the native walking results consumed
by the entrance test. D+2E04E is scratch for a currently processed script actor;
the read-only outside-inn capture showed it pointing at an unrelated actor.
It is therefore excluded from movement identity. Persistent world bytes alone
never establish that the world task is active.

Collision uses the live 96x64 layer-two map at D+23800 and 512 property bytes at
D+25000. Native 267E40/2775D0 samples the two chips above the player's position;
either property masked with 3 blocks the next eight-pixel step. Property 4 is
walkable entrance ground. The graph offers cardinal steps and joins an in-progress
step to that lattice. It does not route across the world's wrapping outer edges.

Entrances are the live count at D+2FB38 and eight-byte records at D+25E00.
Only enabled records are eligible. Native 27E030 computes both lookup coordinates
as player pixel >>4, with no Y offset, so each entrance tile has four possible
eight-pixel contact points. Collision filters those contacts. Repeated doorway
records share one target; distinct field entrances remain separate. Arriving
announces that Confirm is required. The mod never injects Confirm.

## Visibility and text

Native 260080 creates the renderer's `worldmap` child; 276486/276496 write its
camera translation. The mod reads this child's actual scale, position, and parent
transforms, including Scene's ignore-anchor behavior. It reads the Director's
live GLView dimensions with the same resolution-policy calculation as the
retail `getVisibleSize` implementation. Audited getter bytes and the GLView
virtual target guard the DLL layout. Rotated, skewed, hidden, or unreadable trees
do not publish a guessed viewport. Visible entrance copies wrap at 1536x1024.

The live capture outside Truce Inn measured world pixels (400,304), camera
(-272,384), node scale (1.875,1.6666666), and view approximately 587.868x320.
All three parent Scenes had identity transforms with ignore-anchor enabled.
The resulting world viewport spans approximately x243.245..556.774, y192..384.

World names use the same loaded text bank 46 hex as native 294550. Saved names
come from the native character-name records at A+1908+character*24. Era labels
follow 262AD0's knowledge gates, including the unknown era before its reveal.
Local area names use the loaded debug-map label bank's exact message keys;
unavailable names produce the generic announcement `Local area`.

Only visible or already discovered enabled entrances enter navigation lists.
Disabled entries and stale routes are retired. Early world Story Events bind
to these eligible entrances and the native story counter. Vehicles, automatic
local/world route chaining, and combat remain outside this release.

## Recovery and menus

World/local source changes cancel movement, rearm navigation keys, and clear the
partial footstep stride. The F8 preference survives the transition. F8 is polled
before motion capture, so a missing position cannot silently disable its toggle.
Idle discovery timing is measured after observation finishes; slow reads no longer
look like a game pause and repeatedly erase the next key press.

Menu presentations and exits now carry the publishing hook set and native root
identity. A close from the destroyed parent cannot clear an already presented
child. The child's own close still clears it. Missing menu identities remain
errors. This reproduces and repairs the exact Settings sequence in the user's log.

## Verification

The Release suite passed 1,054 tests: Core 109, Prism 8, Native 532, Mod 405.
Regressions cover the Settings event order, field/world/field changes with reused
pointers, slow capture, unavailable motion/F8, world geometry, collision nibble
selection, enabled entrance retirement, custom names, era gates and torn reads.
Actual FASM output from both new hook builders passed four Unicorn replays of
callback arguments, held-input fallback, register/flag preservation, stack balance,
and unchanged manual input.

A replay assembled from the read-only outside-inn geometry and live collision/
entrance bytes returned 11 visible entrances. Truce Inn was at the current
position; Leene Square had an 11-step route. Story point 3 was an explicit scenario
input in this offline replay. Localized labels were taken from the installed text
asset for this replay; the production path reads the loaded text manager.

No game controls were sent. The new hooks, sound, resolution changes, Settings,
Resume and the round trip still require the player's post-installation test.
Intro audio description is separate pending work.
