# Death Peak, Ocean Palace and Blackbird navigation audit

This update follows the approved October 20 tester-report scope. Research uses
the supplied gameplay logs, installed resources, existing Ghidra decompiles and
recorded Steam gameplay. The supported executable identity is unchanged.

## Death Peak

Scene 244 is Atel0052, map 130. The trees are map tiles rather than actors.
Actor 0's gust tests protect X53..57/Y48..54 while global7C bit1 is set, and
X54..58/Y30..37 unconditionally. The lower doll is actor 9; its tile copy and
global7C bit2 create the lower tree. Startup0200 makes a native push-free patch
below the doll. A live transformation retains it, while a reloaded transformed
map does not. Both navigation modes prefer that actual push-free floor inside
the protected area, without inventing a future copy. The doll shelter requires
the drawn native doll; the tree uses its copied tile footprint for visibility.

When no calm patch exists, targets follow live collision geometry along the
protected area's centre line to the trunk's northward collision stop. They do
not finish at arbitrary floor beside it. Tree visibility is based on the actual
copied tree tiles, rather than the protected ground below them. The upper tree
has no unnecessary quest-flag restriction.

Native 175EE0 probes 112 fine units each side of the player's foot. Both corners
must meet the one-tile trunk for holding Up to retain cover. The ordinary
two-pixel arrival box is too wide, so these two destinations additionally require
centre-16 <= X < centre+16. Other destinations retain ordinary arrival behavior.
Both manual and automatic navigation correct small final alignment errors.

Actor 1 sets local0C for the strong gust at 0230 and clears it at 0239, beside the
native visual changes. Ghidra 169280/1693A0 establish its expanded memory layout.
The capture reads the flag coherently and never invents a phase from an
unreadable sample or from the underfoot movement force alone. Actor 1's current
PC also identifies the visual-change phase before the strong gust. Its 61-byte
native loop, exact instruction starts, class, active state, PC and flag coherence
are checked. AD's native handler 16EB10 retains the current wait-instruction PC.
No duration or countdown is inferred from the script.

The south 8/16 push is constant, independently of the gust. Feedback retains its
running advice for manual play and reports when the push stops. Phase changes
and simultaneous floor changes form one utterance. A motion suspension revokes
current status but preserves announcement cursors, so a menu or stop message
is not interrupted by the same phase repeating one frame later. Explicit
discovery resets still clear history.

On pushed tree approaches, automatic walking currently stops and arrival begins
with "Hold Up now." The player keeps movement held to remain under cover. The
native doll patch needs no holding. Automatic shelter holding remains an
optional control preference; this candidate does not assume approval for it.

Only the Warp 498 regions guarded by local0C==1 are removed from Exits: those are
the wind's failure behavior. The ordinary southern exit and ascent remain.
Native movement, gust losses and player timing remain in the game.

Regressions use native map planes with the exact startup tile copies applied,
including the doll-present and transformed variants. They exercise both modes,
tree-to-tree planning, holding Up, precise arrival, visibility, missing wind
data and normal conveyor behavior. No live climb has been performed.

## Ocean Palace

Mune's foot was seven or 13 pixels below the captured camera window when People
was empty. His sprite was still plainly visible in the recorded scene. Native
173B40/173980/1739F0 establish a 256x192 field, while the tile window includes
an extra eight-pixel row. Native 17AD30 places the sprite from its foot point.
All 37 frames of c415 rise at least 24 pixels above that point. Masa's c414 uses
the same cell layout.

The correction applies only to those exact native placements: Mune404/9/98
and Masa409/33/97, classes 5/6. A head within the proven extent counts as visible.
An unintroduced name or an actor beyond that extent is not revealed. Visibility
alone does not create a native interaction or promote an off-camera destination.

In the reported scene 414 snapshots, Dalton stood 112 pixels above the field's
top. All c424 frames lie above his foot, so the empty People list was correct
there. He retains his established name and visibility gates. The walkway
objective now uses a generic label instead of naming the coming boss.

Atel0222 hides Golem actors 15/16 beneath all background layers with 8E00, then
raises them with 8E22 during the summon. The live priority-zero instances are now
excluded, using the same exact placement/priority rule as other concealed
Palace sprites. Their native collision and later raised appearances remain.

The 13 selected Palace descriptions were checked against the gameplay cutoffs
and all 21 native anchor windows. Every reached cue fired. Mune and Masa are the
only supported descriptions before the Mammon Machine; the short log ends
before later events. No narration resources or voice recordings were changed.
Masa's first appearance and Dalton's summon/escape remain coverage gaps.

The general foot-anchor visibility rule remains conservative at camera edges;
this update does not introduce guessed extents for every game sprite.

## Blackbird

Scene 376 uses Atel0274 and Map169. The live captured planes match the installed
map byte for byte. Twenty-nine script actors represent grates at junctions.
The old catalog exposed their Warp regions as automatic passages, so the graph
made every junction terminal and could not expand routes beyond it.

Native opcode 31 (166360, dispatched by 1619E0) checks the Confirm latch at
engine+BC1, set by 189110 from input bit7. These grates only activate on Confirm;
ordinary walking crosses them. They are now Objects rather than scene links
or routing terminals. One grate's trigger tile is solid, so it is omitted.
The ten real MapJump openings retain native exit behavior and distinct labels.

The cell's touch-discovery duct and later Confirm entrance have separate
accurate labels. The native equipment Story route uses an actual opening into
Room 4. Regressions rebuild the exact logged state and replay native walking
through the junctions in both navigation modes, from each of the ten entrances.

Grate views clear FieldState+108C with E300. Native 1734D0 then stops calling
175A40, which contains the field navigation/footstep input hook. A listener on
the existing script dispatcher observes exact actor0 view-loop PCs with AF80
set. A current view is announced once and K repeats once per press. The view
cursor includes the native context and scene. A returning E0 is announced as
"Returning", before its native transition commits. Capture and speech failures
cannot suppress the original game instruction.

Only exact Atel0274 grate-look pose placements suppress the generic crouch
narration. The cell climb and other gestures are retained. Confirm is never
synthesized or consumed. Room names are announced during a proven view, while
the 28 selectable grate labels use appearance and stable numbers.

The log's long westward crawl produced 14 step counts and 15 accepted sounds;
the footstep implementation was already tracking it. Stationary wall presses
and the 36 unannounced grate views explain the reported silence. The repair adds
view-state information rather than false footsteps during stationary scenes.
R3 remains unavailable while normal field input is suspended; K supplies repeat.

Native and regression verification does not establish a live traversal. Actual
controller use, view-render timing, physical key edges, audible output and wind
reaction timing still need testing on the game.
