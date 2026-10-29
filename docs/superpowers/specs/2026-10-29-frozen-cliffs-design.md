# Frozen Cliffs navigation and slide assist

User approved: clear targets for visible cave entrances, climbs and chests; correct
Story Events and manual guidance; opt-in ordinary-input slide assistance to the
selected cave, treasure route or forward exit. Cancellation, battles, chest
opening and Confirm remain native/player controlled. Normal public release is
authorized. No game launch is required or planned for offline development.

Native evidence: current 0.3.83 log SHA699fb386e83709e7f8a6a942a74ae73a9ed1270d5edf332bc1d75cae42b10afd.
There are no full Frozen Cliffs route captures: the eight-snapshot limit was
exhausted earlier. Separate logged poses/actor facts from initial-resource maps
and derived test states. Sources remain unchanged outside the isolated checkout.

Atel0604 actor9 reads leader facing (24 00 03), waits for North (Local3==0), then
checks six cave tiles. These are interaction headings, not story locks. The
current catalog's Local3 guard hides every cave when facing elsewhere. Retain
only the exact verified six guards/owner, and expose their current routes for
valid facings while finishing with ordinary North movement. Missing/invalid
state or stopped/foreign owner cannot authorize a passage. Use unique local
appearance/position labels and do not expose treasure contents or future rooms.

The downhill terrain pushes32 fine units per native frame. Generic cardinal
walking and the existing speed16 floor compensation do not model this slope.
Use a bounded, directed movement graph for the slope, with ordinary eight-way
input, native collision/layer checks and the native Run setting. Opposing the
slope cannot produce uphill travel. Native map/actor data must be revalidated;
manual cancellation, focus loss, foreign scenes, control loss and unreadable
required state revoke input. No coordinates, facing, flags or timers are written.
If native running is not ready, explain the required game control and stop.

Use the selected target and existing auto-walk command as opt-in; do not start
movement on map entry or select a treasure route automatically. Describe the
required input in manual guidance. At cave entry, face/move North; never Confirm.
Stop across scene changes/native encounters and let the player restart guidance.
A cave above the current one-way descent must remain restricted unless a verified
local passage reaches it. Story Events selects a currently reachable forward
cave, rather than repeatedly sending the player to a return passage.

Chests: initial native records319..327 in602/603/605/606 and334..336 in648.
Use the live unopened chest capture for existence and contents stay unannounced.
Assign stable per-room letters from native record identity; opening one must not
renumber the others. Verify native interaction reach and the relevant entrance
pose for all12 chests, including disconnected side branches. Do not fabricate
chests on604's slope or expose unseen chests in another map component.

Prefer a scoped runtime interpretation of the facing guard to global catalog
regeneration: changing37 facing-reading scripts would exceed this report. A
bounded original x86 movement replay should corroborate the slope and chest
approaches. It is offline evidence, not live transition/controller/audio proof.
