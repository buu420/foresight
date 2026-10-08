# Closed-door navigation in Foresight 0.3.47

The October 8 tester log reports an unavailable return route from Spekkio's room
after the walking lesson. This update makes the doorway readable as an object,
plans its native opening contact, and names the End of Time hub **Main Room**.
It also fixes the shared finish for other verified automatic tile-copy passages.

## Evidence and cause

The log `2026-10-08 18.47.35 ~ Chrono Trigger.txt` contains failed scene-465 exit
plans at (1930,3571,1) and (1536,3583,1), with story point 77. Actors 15 and 16
are live class-7 markers at (1408,3839) and (1408,3583), with script calls enabled.
The object inventory disappears when movement changes the lead's facing.

Installed Atel0284 actor 0 runs `24 00 26` at 0x2A4 in its idle loop. Native
helper RVA 165F50 reads actor+69A0: 0 up, 1 down, 2 left, 3 right. Door touch
handlers 0xAD8 and 0xAF5 require local7==0 and local38==1. They copy terrain
(0,24)-(0,26) to (5,12), flags 0x3B, then set local7. There is no key, story-point
or Confirm guard on this copy. Actor 16 also has walking-lesson checkpoint code;
that code remains native.

Installed MapTable0049 is 16x32. Exit 0 at (5,15) leads to scene 464. Its northern
approach lies on the closed door's partial floor at (5,14); the other sides are
blocked. The native copy makes (5,12)-(5,14) full floor on layer 1. Local state
resets on map entry, so later visits need to open the door again.

Fresh read-only Ghidra output for 165F50, 178980, 15D090 and 160830 is saved under
`artifacts/research/doors-0347/ghidra`. The supported executable SHA256 remains
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Native touch dispatch at 178980 occurs before the terrain movement check, so
pushing into a closed door can execute its opening script. The facing-door
script audit found the scene-464 upward door and these scene-465 downward doors;
other facing checks do not receive a planning exemption.

## Routing behavior and scope

Planning accepts any captured native facing 0..3 only for the exact audited
touch-terrain guards: scene464/actor25/local11/up and scene465/actors15–16/local38/down.
Their final approaches enforce the required direction. Missing or invalid facing,
handled-copy locals, class identity and disabled calls still reject the opening.

A terrain landmark can have goals on floor supplied by its own available native
copy. Those goals are descriptive planning data. The staged graph must first
prove a reachable contact on the current floor and a continuation opened by one
specific copy. It returns only that live approach leg. Manual guidance tells the
player to continue into the contact; auto-walk holds movement for a bounded wait.
Only a fresh captured floor enables the next leg. Failure to open stops walking.

This finish applies to existing cataloged class-7 automatic touch-terrain
passages whose entire touch action set is Terrain, plus the individually audited
End of Time stairs and door markers. Other terrain triggers keep their existing
wait behavior and do not gain copied-floor landmark goals. A native audit of
scene268/actor9 found Switch actions and cutscene effects alongside tile copies;
the new center push does not apply there. It does not turn Confirm switches, hidden sprites or unknown doors
into walk-through floor. Each action retains its native guards; unrelated possible
copies are not combined. Scene465 actor15 supplies the readable doorway row;
actor16 remains available to the walking-lesson story binding without adding a
duplicate object row.

The installed Guardia Castle map66 and Atel0240/Atel0000 scripts provide a second
door proof. Scene120 actors9/10 and scene21 actors33/34 copy (3,0)-(5,2) to
(30,38), flags0x3B. That opens the blocked tile (31,40); their only touch effects
are sound, copy and the door-local flag. Scene120 uses local6==0; scene21 uses
local7==0, with point>=78 additionally required by the southern marker33.
Native collision fixtures verify contacts from both sides, fresh-floor
continuation and the southern story restriction. The pre-point78 southern
approach remains unavailable because its alternative contact timing is unverified.

Scene464's spoken field name overrides the internal character-name label before
live text fallback. Catalog destination and connection labels use the same
generic name. Internal resource tables remain unchanged.

## Verification limits and tester checks

Regression tests use the installed Spekkio terrain fixture, both reported
positions, all four facings, manual and automatic finishes, unopened timeout,
fresh opened-floor continuation, re-entry, unreadable/handled guards and disabled
calls. Existing End of Time and walking-lesson tests remain in the check set.
Package, deployment and full-suite results are recorded in the publication audit.

Live traversal, speech listening and controller-hook delivery have not been
performed for this update. Test both return-navigation modes from Spekkio's room
while facing up, left and right, including after leaving and re-entering. Confirm
the doorway appears under Objects and the hub is announced as Main Room. Also
test ordinary doors already known to open by walking into them. Send a fresh log
if a route stops or speech is missing.
