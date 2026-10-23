# Black Omen pad and northern-door navigation

The user approved identifiable pad/door targets and both navigation modes with local Story Events guidance.

## Evidence and limits

One October 23 tester log has 13,636 lines, SHA256 `4604996c934697a676bdf95bfae3b21690821820c54e81e9d5245ad3147b4154`. Its eight compressed route snapshots cover scenes 315/317. The later rooms provide actor facts, inventory, poses and failed route commands, but no captured live terrain. Tests distinguish installed initial MapTables from actual captures and record unlogged facing, priority and script-state assumptions.

The independent door audit reproduced the shipped 0.3.71 inventory and failures using the installed initial map plus logged actor facts. The generic door had 24 automatic-walk and 2 manual-guidance failures; its optional alias was also present in Objects and failed. The broad Lavos objective intentionally routes to the End of Time gate, accounting for its return-elevator instruction.

Fresh read-only Ghidra exports verify native 173750/16C3F0/178FF0 coordinate wrapping against executable SHA256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Original Atel bytes and native map/jump data establish the contacts below. Proprietary exports, logs and the independent audit remain under `artifacts/research/black-omen-20261023`.

## Teleport pads

- Scene97/Atel0453: actor0 checks columns7–8,row6, with arrival guard G1A7/20 clear, then warps to98. The native usable corridor includes fine X2048 between the tile centers. Old goal X1920 is disconnected; a native-valid boundary sample repairs the route.
- Scene98/Atel0454: columns23–24,row22, guard G1A7/10 clear, return to97. The log confirms32x16 collision planes while its player uses row22 and native exit0 occupies columns23–24,row30 to99. Native collision readers take coordinates modulo plane dimensions. Only this exact room and verified exit contract repeat its fresh planes through row31; disconnected landings are not joined.
- Scenes320/321 use Atel0495/0496 actor0, with G1A7/80 and/40 re-entry guards. The forward trigger in320 is row38;321's return trigger is columns23–24,rows25–26. These contacts are pad Objects. The wider ordinary-return doorway regions retain exit behavior.
- A pad's scene, controller identity/processing, native footprint and available guard are separate proofs. Known arrival guards keep the appearance target with a restriction; unknown state prevents auto-travel. Inactive pads do not become artificial route terminals. Active pads retain native terminal behavior so an unrelated route cannot silently teleport.
- Atel0495 re-arms scene320's pad only on row42, south of its chamber; Atel0496 re-arms scene321's pad only north or south of rows24–26. Their instructions describe those directions rather than suggesting any sideways step will suffice.

The exit-grid dimensions are distinct from collision-plane dimensions. The existing Ghidra export for native 17A300 initializes engine E7C/E80 from its caller's dimensions, then writes native jump-table coordinates without modulo. Its caller at 1597E0 passes scene extent fields 83DE8/83DEC. Scene 98 has no coordinate override in this builder; an exit at row 30 requires a grid tall enough to contain it. The offline fixture reconstructs the minimum extent from jump data, not an observed live extent. Product code instead requires the current captured grid to contain both exit-0 cells at row 30 and its native destination 99 before extending the fresh collision planes.

## Northern door

Scene324 uses Atel0389 and Map13 (32x48). Actor20 is a class7 background interaction marker at fine(3072,1488). Confirm returns immediately. Function2 checks local0A==0, plays sound53, copies tiles(30..31,45..47) to(11,3) with flags79, then sets local0A. The copied collision footprint covers(11..12,3..5). There is no preceding switch requirement in this handler.

All three old marker goals were inside the closed door's leading-probe footprint. The first reachable foot row is1648; the verified approach(3072,1728) is reachable from logged poses. A sideways arrival there does not open the door. Ordinary Up invokes the native touch before the terrain rejection; its opening callback changes the game, not the mod.

The new edge contact is scoped to scene324 actor20's exact identity, position, local state, native tile-copy metadata and readable call/processing/priority gates. The opened preview supplies goals for exit1 and proves staged continuation. Movement authority remains the live graph, followed by a new capture after native opening. Unknown or missing authority restricts the door instead of using the preview as live floor. The local full:lavos binding in the five reported rooms follows their native forward control, without changing its general End of Time alternative elsewhere.

The independent review identified the intermediate native opening frames. Touch dispatch 16EF30 changes actor priority to2; E8/171700 and E5/171C40 yield before opcode75/169280 writes local0A. Actor+48 is the live VM PC, one below the file offset. Only priority2 with the same door contract and PC within fn2 (551..562) is accepted as an opening in progress. This keeps the guarded preview and exposes a fresh `PassageOpening` flag on the door, its passage and bound story row. The controller pauses movement without cancelling guidance, waits at most three seconds, and replans from the completed capture. Other priority2 states remain restricted. Keeping the opened door row avoids a false missing-destination message.

## Verification boundary

Regression tests cover pad categories, arrival/unknown guards, the narrow corridor, wrapped lower landing and separation, both navigation modes, the reconstructed final-door failure, native Up finish, missing call/priority authority, fresh opened-state continuation, and local Story Events selection. They exercise actual source and controller classes. The private replays do not launch the game or prove physical controller input, speech delivery or a completed live dungeon run. The render-texture crash is outside this repair.

The 52 focused regressions and all 4,512 solution tests passed after resolving the review's opening-frame defect (Core 227, Native 1,339, Prism 10, Mod 2,936). They include both navigation modes for the door, passage and story row, yielded closed/copied states, timeout/cancellation, unknown or invalid door authority, the tester's stuck pocket positions and armed-pad terminal behavior. A separate console replay failed on the old 0.3.71 payload's missing pad and passed with the pre-review Release DLLs under the private x86 runtime. The final replay also covers yielded opening frames. Package/deployment hashes and preservation results are recorded in the tester handoff and private receipts.
