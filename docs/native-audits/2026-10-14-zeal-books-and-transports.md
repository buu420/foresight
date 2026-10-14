# Zeal books and transports: 0.3.61 evidence

The October 14 tester log reaches story point 159 in Enhasa (355), Kajar (357),
and the six Skyway/Land Bridge rooms. The books already appeared as Object or
Interactable scenery. Their native handlers produce no dialogue. Room 337 spoke
Map Not Specified, and unopened bookcase passages were offered by destination
debug names despite their blocking walls.

## Native identities and restrictions

Executable SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Resources SHA-256:
`09914BF4A8944C0708C947E7EB5683AEE4ED48BC6D5D1D75FA07B68131653C7E`.

| Room | Book A | Book B | Book C |
| --- | --- | --- | --- |
| Enhasa, scene 355 / Atel0208 | actor10, class4, visual115 | actor11, class4, visual128 | Confirm marker13, class7; effect sprite12 is separate |
| Kajar, scene 357 / Atel0209 | actor11, class4, visual115 | actor12, class4, visual128 | actor13, class4, visual136 |

Locals 0A, 0B and 0C correspond to the books' displayed states. Their toggles
copy the book graphic, set or clear the matching local, and show or hide the
effect. Unknown locals omit the state. Book C's Enhasa marker is the target;
the effect sprite never creates another row. The exact NPC visual100 at Enhasa
actor9 and Kajar scene359 actor8 is a small white doll (assembly c107).

The two concealed exits (355 exit1 and 357 exit3) require local0E exactly1. The
native handler sets it after its three passage copies finish; the room loader
also restores it when returning through that passage. No puzzle counter,
sequence, reward or future destination is announced.

Transport actor8 is a class7 contact marker in scenes336,337,352,353,354,356.
Its touch handler owns teleportation. Skyway warps preserve the native
global57 bit80 restriction. The ordinary exit table continues to represent the
doorway outdoors. Scene337 shares Skyway Map58 with 354 and356 and already has
the verified Skyway name in the navigation catalog. That exact scene now uses
the catalog name instead of the missing native debug label.

End of Time's nine verified pillar regions keep their visibility predicates,
fixed labels and native Confirm instructions. They change category to Objects;
story bindings continue to use their existing IDs. Proto Dome's shortened Gate
label now matches the other Time Gate objects. No unverified portal placement
is promoted to an active interaction.

## Narration proof and visual review

Fresh Ghidra 12.1.2 exports verify E5 at RVA171C40: it queues the seven exact
copy operands, sets actors+1211C bit20, clears the advance bit, and saves PC+8.
The scheduler at161560 saves that continuation. Confirm dispatcher17D0C0 and
actor gate17FA20 retain the existing native interaction geometry. Handler16C3F0
was reviewed for the book effect's 8E operand. This is native pseudocode and
disassembly, not recovered original source.

Atel0208 is 2796 bytes, SHA-256
`A912C2735A866CE4A2CE0009BE97BCAF5CF7E94C1BF67411CCFD0FA64657C880`.
Atel0209 is 1861 bytes, SHA-256
`DCCC62DA4C8A32EE6ED19E470111921DBA0A3F427194E14BF07CB3CB24578960`.
Fourteen anchors require exact script identity, bytes, actor, coordinates,
camera visibility and the applied native queue request. The E4 reload copies
are excluded. The passage cue watches the final passage copy, only when its
own tiles are on camera. Each book tracks the last described direction so
open/close/open works while identical repeated observations stay silent.

Installed sprite assemblies c122/c135/c143 were rendered and reviewed: flame,
white water splash and blue-green swirling wisps. The closed books retain
neutral letters. Actual Steam reference frames show the effects in their rooms.
[Part 5 of the reference playthrough](https://www.youtube.com/watch?v=G98InK5fbdw)
contains Enhasa around47–49 minutes and Kajar around51–53 minutes. Descript
reviewed the corresponding 285-second excerpt, confirming the white splash and
teal effect, with some uncertainty about individual sampled motion and book
cover details. Its sparse samples do not prove individual player toggles; the
native handler and exact graphic copies supply those timing anchors.

The resource reader and sprite assembly layout were cross-checked with the
[CTViewer source](https://github.com/GitExl/CTViewer). Labels and effects were
not assigned from walkthrough puzzle order.

## Verification and limits

Navigation regressions use the unchanged packed collision bytes from the
installed initial maps and actor positions from the tester log. They exercise
both guidance modes and native Confirm winners for all six books. Changed
terrain tests copy the three native planes; they do not write game memory.

Native regressions reject wrong scripts, bytes, classes, visuals, positions,
hidden subjects, off-camera subjects and incorrect queued changes. Runtime
tests cover repeated toggles, cancellation, owner changes, original-call counts
and voice selection. Seven new recordings use the existing approved Fish model
and reference hash; local transcription, silence and clipping checks passed.
The previous 149 recordings remain byte-identical.

No game launch, player input, live traversal, controller test, Confirm test or
in-game listening was performed. Automated route tests are not live playthrough
proof. The descriptions begin when the native copy is queued; rendering follows
on the next frame. Mod-owned labels and recordings remain English.
