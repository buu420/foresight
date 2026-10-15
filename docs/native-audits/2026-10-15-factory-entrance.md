# Factory entrance security and native party markers

This supports Foresight 0.3.63 and the October 15 report from a different tester.
The supplied 10,568-line log reaches scene 228, Derelict Factory, Entrance, at
story 60 with global 58=00. Actor 8 is loaded, drawn, class 4, visual 7F at fine
(2432,4864), with native script calls enabled. The mod omits it from all object
categories and repeatedly reports that the entrance-security destination cannot
be located. The party actors show Crono, Robo and Marle.

## Native evidence and root cause

Supported executable SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Resources SHA-256:
`09914BF4A8944C0708C947E7EB5683AEE4ED48BC6D5D1D75FA07B68131653C7E`.
The installed files match the native research corpus. A separate verification used a fresh
read-only, hash-gated Ghidra 12.1.2 export of 183090, 183220, 183260, 183410,
183450, 17D230 and 17FA20. These are native pseudocode, not recovered managed
source. The primary [CTViewer source](https://github.com/GitExl/CTViewer)
corroborates the installed resource-table format; the executable and PC scripts
establish the behavior below.

Native party predicates D2/CF search the expanded dwords at `ActorBase+0x1324C`.
RVA `0x183220` checks the first three slots; `0x183410` checks all nine. They compare
the full cell directly with the character ID. Initialization uses `0x80` for empty
slots; removal/addition paths mark vacated cells with `id|0x80`. The previous mod
reader accepted `0..6` or `0xFF`, rejecting normal `0x80..0x86` and discarding the whole
roster. All catalog ActiveParty/Recruited guards then became unavailable,
including the Factory terminal's party-gated actions.

The repaired reader accepts the verified `0..6` and `0x80..0x86` words and preserves
them without masking. A vacated `0x83` remains absent, rather than becoming Robo.
Invalid or changing cells still make the roster unavailable. This shared reader
also serves world/flight story predicates; its corrected markers are tested
there as well.

The log does not contain the actual roster words, so the exact rejected byte
in that session is inferred from the native encoding and matching symptoms.
An offline reproduction using the three logged player positions loses the
terminal when Party is unavailable and reaches it when party capture is valid.
No live process memory was read or written for this investigation.

## Entrance interaction and routes

Atel0039 actor 8 initializes the visible terminal at (2432,4864). Its Confirm
handler checks global 58/04 and native active-party member 3. Robo's function 5
copies source cells (50..52,59) into (14..16,18), visibly removing the laser,
and the game sets 58/04. The mod never performs that copy or presses Confirm.
The native consequences of using the terminal are not pre-announced.

The existing actor ID and story binding were correct. The terminal's verified
visible identity now also establishes object capability independently of the
optional roster capture. Drawing, native call, subject and scene checks remain.
Its existing actor-facing/Confirm rules supply both navigation modes, and
arrival asks the player to use Confirm. Robo's active-party requirement still
runs in the game. Review caught an unnecessary instruction that could name him
on an early visit; that instruction was removed before publication.

MapTable 0124 has solid laser cells 14..16,18 until the native opening. Therefore
the onward lifts were correctly unreachable before security was disabled.
Selecting those lifts now explains the security requirement rather than only
reporting no route. The return exit remains usable. After the native opening,
both lift routes are checked against the changed terrain, including the native
northbound conveyor. No alternate passage through the lasers is invented.

Map 124 is shared with other Factory rooms. The distant staircase at 11,52 is
outside this entrance's physical component before and after the laser opens.
Scene 228 crossing candidates are now checked against that component, removing
the misleading staircase without changing other scenes' crossing providers.

## Verification boundaries

Tests use the logged actor identities and player positions plus installed
initial terrain. Post-opening tests apply the three exact native shape/layer
cell values in the fixture. These are offline route checks, not logged live
terrain or completed gameplay. The original failing tests and later review,
full-suite, packaging, preservation, deployment and publication results are
recorded separately.

All 160 existing narration recordings are retained. No new narration was
generated. No game launch, Steam launch, live Factory traversal, controller
interaction or in-game listening test was performed in this repair.
