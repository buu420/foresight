# Death Peak automatic shelter holding

The user approved keeping Up held at the later shelter trees, including while
browsing navigation, until manual control or a new route. This follows the
[0.3.68 native audit](2026-10-20-peak-palace-blackbird.md); the earlier
stop-on-arrival behavior is superseded only for automatic pushed-tree arrivals.

The installed Atel0052 gust predicates protect lower X53..57/Y48..54 while
global7C bit1 is set and upper X54..58/Y30..37 unconditionally. Existing target
construction proves the copied trunk and walks the live collision graph north
to its contact. Ghidra 175EE0's leading-corner probes require
centre-16 <= X < centre+16 for both sides to meet the trunk. Hold bounds retain
that exact fine-X interval and the native protected tile-Y interval/layer.
There is no hold on the native doll or retained push-free patch.

Automatic arrival enters an explicit directional-hold state. Each fresh native
input frame must still prove the same target and bounds, readable control and
the player's position inside the protected area. Holding skips ordinary route
stall detection because contact with the tree is intentional. Ghidra 175A40's
0x800 branch adds the negative native walking/running velocity to the floor
velocity. On south-16 floor, walking Up nets zero; 175E90 returns without moving.
Running Up meets both aligned trunk corners and 176810 cancels the movement.
The hold therefore uses plain Up and preserves the player's own Dash input.
Climbing still uses FieldFloorMovement's running adjustment; holding bypasses
that requirement, including when run-setting capture is unavailable.

Category and destination browsing retain the held target independently of the
selection being read. Opening/closing the controller navigation menu preserves
only a valid hold; ordinary walking still cancels. A successful new guide or
walk replaces the hold. If planning fails or the selection is empty/blocked,
the same input frame's proven shelter is restored with "Still holding shelter."
P on the currently held shelter remains the normal stop action.

Manual game input and the existing foreground, pause, context, scene,
controller, fault and disable gates cancel the hold without resuming it.
Returning to a valid state does not silently restart movement. Native scripts,
hazards, player Confirm and departure timing remain unchanged.

Regressions use the installed-map fixture and actual FieldNavigationSource,
controller and runtime. They check repeated blocked-contact input, both trees,
keyboard/controller browsing and route replacement, push-free arrivals, native
run settings, bounds and movement-authority cancellation. Static replay does
not verify actual wind/contact oscillation, controller hardware or audible
speech; those remain live-test requirements.
