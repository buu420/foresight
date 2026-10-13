# Tyranno Lair navigation in 0.3.57

The October 12 tester log begins in scene 302 at story point 147 (0x93), then
shows the Turnkey encounter at 16:22:50 and story point 150 (0x96) after the battle.
All eight full route snapshots were collected before the battle; the later actor
states are recorded in the log's actor facts. The room reaches exit 8 to scene
430; its other loaded exits, markers and encounters lie in disconnected sections.

Scene 302 uses installed script Atel_0057. Actor 0 reads the leader tile, then
checks X == 55 and Y between 19 and 25. Below story 0x96, this starts the dialogue
and battle and sets 0x96. At exactly 0x96 with local 0x0A clear, crossing the same
strip requests actor 9's reveal function and sets the per-load guard. Actor 9
starts hidden, then is drawn and walks within the cell. His Confirm handler ends
by setting story 0x97. No touch handler frees him.

The tester's later position is around column 59, so the reveal strip is never
crossed again. The old "free-kino" objective therefore has no visible person to
bind. TyrannoLairTargets supplies the native column as a story candidate only
with the coherent scene, story/local guard, player control, actor-0 loop and
hidden person-06 setup state verified. The existing objective consumes that
candidate; no duplicate unnamed exit is added. Once the actor is drawn, the
existing actor binding and native Confirm-range reader take over. Hidden Kino
is never listed in People or named by this new approach.

Native actor-0 script PCs are checked within 0x0283–0x02D7; actor 9 remains at
the setup PC 0x04D4 until reveal. The script reader's +12001 PC convention is
cross-checked against the actual opcode in the log. Native local-compare and
set-local functions address the same cell at actor-base +118B0 +index*8.

The floor filter is deliberately permissive. It joins every possible half of a
tile with compatible neighbouring physical layers in either direction, using
the same layer acceptance as the native engine. Actor blockage, body clearance,
forced floor direction and exit termination are omitted. Thus a disconnected
component proves separation; a connected component does not prove a route.
Unknown poses remain eligible. Shape and layer bytes invalidate the cached index
when the native floor changes.

A current-component native stair landing or drop preserves its existing staged
search. Available automatic terrain-copy contacts retain eligibility when any
compatible current-component floor exists in a conservative neighbourhood of
the live marker. A marker in a separate disconnected room cannot exempt all
targets in that room. The exact staged graph remains the sole movement authority;
the floor filter never opens doors, combines floor previews or writes game state.

Ghidra exports from the verified Steam executable establish region selection at
RVA 178C50, layer acceptance at 178FF0 and Confirm scanning at 17D230. Game SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Native scripts and resources match the installed build. Research artifacts and
tester logs stay local; reduced map/actor fixtures support the regressions.

Checks cover the captured entry, post-battle actor facts with inferred unchanged
local state, reveal guards, both navigation modes, visible/remembered restrictions,
physical layers and neutral/split tiles, cache updates and the production source
binding. The revealed-position test uses the scripted endpoint as a test scenario;
live arrival and listening are still needed. The filter is not a general proof
that all one-way routes, switches or cross-scene connections are navigable.
