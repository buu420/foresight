# Ocean Palace navigation and description audit

This repair uses 23 original navigation captures from three October 17 tester
logs, the installed field scripts and graphics, fresh read-only Ghidra exports,
offline execution of the original x86 movement code, and recorded Steam
gameplay. The supported executable SHA-256 remains
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

## Routes and entities

The great stair, scene409/Atel0397, has 32×64 collision planes but a 32×128
playable extent. The native readers mask and wrap collision coordinates;
script positions and exit cells retain their full height. The repair repeats
the captured planes within this exact room and extent. It does not wrap the
player position, invent floor, alter native terrain or generalize this rule
to other maps. Masa appears only after the last stair battle. Until then,
Story Events follows the real lower stair exit through the native triggers.

Scene406 contains six physically separated chambers. Its native self-exits
connect pairs of rooms; a reachable switch in one chamber cannot make all six
chambers' targets reachable. The target filter now preserves those boundaries
even when a staged route exists in the current chamber.

Native control handlers distinguish floor switches, wall switches and the
elevator control. The lower wall-switch corridors are blocked until the
native Scouter contact encounter changes them. The elevator's controller9
similarly starts an encounter past row18 before its control becomes usable.
Story routes stop at those native contacts first. Navigation never starts
combat by an injected command, changes story bits or presses Confirm.

Dalton's scene334 conversation starts at controller8's Y<13 region; his own
Confirm function is empty. His People target uses that region. In scene423,
the Queen's native approach region requests Lavos's story function. That
interaction is attached to the visible named character. Scripted map jumps
from the machine and Lavos cutscenes are retained for Story Events but are
not presented as visible doorways into subsequent battle maps.

Names use exact scene, slot, class and sprite identities and a conservative
story gate after native dialogue introduced each name. The Prophet's slot
never exposes a later identity. Alternate sprites in reused slots do not
inherit the name. Existing custom party names, Kino and Ozzie handling remain.

A name alone does not make a new off-camera story character a destination.
Dalton's later Palace approach is attached only after he is seen, using his
controller's real boundary. Algetty's cast parked below the doorway while its
native scene flag is pending is excluded from People and route candidates.
The automatic entry scene receives a non-spatial instruction rather than a
route to the parked Dalton. Exact sprites drawn beneath opaque backgrounds
in Algetty and the Palace are omitted while native collision remains intact.

The bridge switch's visible panel is retained while its handler is inactive,
with a neutral restriction. Once available, the panel uses the verified
handler's touch goals. It does not reveal the hidden handler's sprite or a
puzzle solution, and the panel is offered only once.

## Descriptions and movie

Supported Palace descriptions bind to exact script digest, length, scene,
actor, runtime PC, instruction bytes and story range. The subject must be
drawn and inside the native camera, and the original handler must produce
the expected animation or movement state. The cues describe visible actions
without inferring motives, hidden identities or future outcomes. They are
deduplicated per scene visit. The two Palace arm gestures receive their
specific description instead of the generic waving description.

The native scene-change code invokes movie007 on arrival at scene415 at
story0xCB. Non-Japanese language settings select `007-en.dat`. Both originals
last 54.0873 seconds and share the same soundtrack. The recorded narration
contains 13 reviewed cues, including the English on-screen text. One 24 kHz
mono PCM master serves both verified movie identities; the native soundtrack
is retained. Separate verified narration packs still take precedence.

The recording uses the existing approved Fish voice and reference digest.
Earlier recordings are unchanged. Private credentials, the reference voice,
game movies, tester logs and reference gameplay are excluded from downloads.

## Evidence limits

Captured-map tests exercise actual graphs and both navigation commands.
Native byte replays and the silent original Media Foundation player probe
verify movement and movie timing separately. The movie probe reached normal
end after pause/resume without failed media events. These are offline checks;
no live game was launched and no physical controller or audible narration
test was performed for this release.

This is selected coverage, not a complete description of every Palace event.
Unverified camera effects and the later world-map disaster sequence remain
outside these anchored field cues. The earlier keep desktop crash remains
unconfirmed. Testing should include the real switch encounters, long stair,
Masa reveal, elevator, Dalton, and the machine/Lavos sequence.
