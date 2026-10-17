# Keep navigation and desktop-closure report

The October 16 report ends while leaving the Western Passage in Magus's Keep.
The tester confirmed a closure to the desktop. No exception, native stack or
process exit status appears in the supplied log, so **the crash cause remains
unconfirmed**. This audit separates that report from a reproduced entrance-door
navigation defect found earlier in the same log.

## Confirmed closed-door defect

The full live failure captures at lines 12469, 12490 and 12518 show scene 165,
story point 136, and a closed entrance door. The first player position is
`(2034,8432,1)`; the other two are `(1682,8224,1)`. Exit 1 and the western-wing
story target have no approach points because the doorway floor is still closed.

Installed Atel0246 actor 14, function 2 at raw offset `0x688`, opens this door
automatically when local 7 is zero. It disables player control, animates two
tile copies, then restores control. The final native copy is `(4,39)-(7,43)`
to `(6,26)`, flags `0x3B`. Local 7 records whether the opening ran; it is not
the player's facing. The captured marker is at `(1920,7679)`, class 7, with
script calls and processing enabled and priority 7.

Foresight now retains planning goals supplied by this exact opening copy and
names the object **Keep entrance door**. A staged route must first reach the
door's contact on the **live closed floor**. The usual central contacts lie
inside the blocked tiles; the southern edge contact supplies the reachable
approach. Native contact selector RVA `178980` sees the upward leading probe
before terrain movement is rejected. No copied floor becomes movement authority.

The game still performs the opening. Navigation releases movement when the
native sequence takes control. Restart guidance or auto-walk after it finishes;
only a fresh captured open floor permits the onward route. Confirm, story
flags, collision planes and game locks are not automated or written.

The final opening copy also triggers the existing **A passage opens** recording.
Its native scene-action anchor requires scene165/Atel0246, actor14, runtime
PC `0x69E`, the full installed script digest, exact E5 bytes, marker identity
and the doorway's own tiles inside the camera. Native queued-copy completion
is required. The first animation frame and E4 reload path do not trigger it.
This adds no new voice assets.

## Identical lights and story progression

The keep's lights use the same NPC121/c128 sprite. In scene471 their save,
warp and ambush roles depend on private state; visual appearance and the first
chime do not distinguish them. Scene174 also has two identical lights.
Foresight presents these as stable **Glowing point A–D** or **A–B**, offers
each separately under Objects and Story Events, and gives all of them the
same activation-terminal treatment. An unselected visible light is avoided
without consulting its hidden role. Saving is named only while the leader
stands on the actual checker tile and native CanSave is set.

The hidden-role regions and actor-specific navigation links are suppressed.
Anonymous room connections preserve dungeon-level look-ahead; they carry no
target or actor ID and cannot select a light in the current room.
Scene168's unopened pits look like ordinary floor: their private warp regions
are neither targets nor hidden routing barriers. The native game still drops
the party when a trap is triggered, and captured changed terrain still applies.
The entrance-hall light is an object rather than a premature Enemies entry.
Return lights beside the arrival in scenes170/447 are identified as return
warps, gated by their live drawn sprite and running controller.

Disguised people no longer gain duplicate Enemies entries from their scripts.
The native game hides them and draws separate creatures when they transform;
hidden actors leave the list. The Western Passage's five figures include two
children and three adults; their existing appearance labels are preserved.

Atel0251 actor10 fn2 begins at `0x4C8` and sets global AC bit02 at `0x50E`
after Ozzie's interaction. Story Events now routes to that native touch before
the onward corridor, instead of waiting forever for Ozzie to move. The earlier
west-wing figure objective now says **Talk to**, matching its Confirm handler.

Appearance regressions cover all six private pit-role assignments, equal
choices, saving feedback, unselected-light terminals, hidden/removed sprites,
invisible pit routing and disguised figures on synthetic clear floor. Separate
Ozzie regressions use the earlier reported position and installed initial map94;
unreported actor state is not reconstructed. These are not live dungeon tests.
Small-camera regressions also keep every light selectable outside the current
view. The Long Passage's visible figures retain their native walking contact;
removing premature battle labels does not replace that contact with Confirm.

## Audio findings

The installed startup scripts for scenes 165, 166, 469, 169, 171, 125 and 173
stop music with `EA 00` before the entrance-hall introduction. Atel0247 starts
the keep music with `EA 40` at `0x04F2` during that introduction, after both
wings have been visited. The reported first visit had not reached it. This
fits the native early silence; adding background music would change the game.

Fresh read-only Ghidra exports of `171700`, `171780`, `1A35F0` and `1A2C60`
confirm the sound command and channel-stop meanings. Root also checked the
eight exact startup/introduction instruction pairs against installed resources.
The scene-to-script mapping comes from mapinfo, not matching scene numbers.
The resource-format reference is [CTViewer](https://github.com/GitExl/CTViewer).

The log shows accepted footsteps, but accepted buffers do not establish what
the tester heard. Sound effects, device selection and audible output remain
unverified. No volume or mute change is included.

Movie 003 earlier reports `0x8876017C`, matching the Windows SDK's
`D3DERR_OUTOFVIDEOMEMORY` definition. Narration closes after the failed movie,
and the log continues for about nineteen minutes. A connection between that
error, resource use and the later desktop closure is **not established**.

## Crash diagnostics and limits

The external attach helper now records the game's private bytes, working set
and peak working set/commit once per minute, and its hexadecimal exit status after
the process handle signals. [GetExitCodeProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getexitcodeprocess)
can return the unhandled exception value when one terminates a process. This
does not install an exception handler inside the game or claim crash recovery.
Memory counters do not measure VRAM or prove address-space fragmentation.
Diagnostic sampling failures cannot end the helper's pointer lease.

The last native input cancellation is not a crash stack: the same cancellation
appears earlier before a normal battle. Fresh Ghidra checks of field input,
dash, joystick, load, movement and save-gate routines found no demonstrated
hook fault in the recorded path. The supported executable SHA256 is
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

Seventeen focused regressions cover the three full live door captures in both
navigation modes, exit/story/object selection, the final native opening copy,
and missing or disabled script gates. Twelve private replays pair the real C#
controller with original x86 movement and contact code at walking and dash
speeds. Each reaches actor 14 before the closed floor, releases on the modeled
native control pause, and reaches exit 1 after an explicit restart on the
native-open copy. The replay applies that copy offline; it does not execute
the animation script or constitute a live playthrough.

An additional initial-terrain audit found routes for 53 reachable exit searches
across sixteen keep maps. Scene 447's static terrain could not be decoded, and
initial maps do not cover every dynamic state. Live traversal, physical
controller delivery, screen-reader listening and the reported crash still
need tester confirmation.

If the game closes again, include both the accessibility gameplay log and
`Accessibility/Logs/bootstrap.log`, especially the final exit-code and memory
lines. Report whether footsteps, game effects and music were each audible.
