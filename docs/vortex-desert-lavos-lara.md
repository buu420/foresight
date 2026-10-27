# October 26 navigation and native UI repair

The supplied runs all identify Foresight 0.3.77. The failures also exist in 0.3.78. The repair targets the supported executable SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

Research receipts and full native exports are retained locally under `artifacts/research/vortex-desert-lavos-lara-20261026`. They contain tester evidence and are excluded from distribution.

## Moving sand

The selected destination in the stalled Desert run remains `exit:0`; the log does not show a chest selection. The ordinary route lost its steering waypoint, while the chest-only path retained one. The frame now explicitly opts scenes 159–161 into moving-floor steering. The manual direction callback and automatic candidate selector reject X/Y component sweeps blocked by the captured map. This is conservative geometry pruning, not a replacement for the game's movement or collision code.

Fresh Ghidra exports of RVA 1734D0 and 175E90 establish committed force ordering and native movement. Original x86 instructions, emulated in private memory, reproduce the blocked inputs. Eight exit replays pass with the new policy (four logged or boundary starts, both modes); the same current code with that frame policy disabled passes zero. Eleven chest reach windows remain usable. These replays assume the native initial planes, opened chests, no actors or encounters and disabled exit transitions. They do not prove gameplay traversal.

## Vortex passages

- Scene 600 has actual captured planes, actors and story state. Its native actor 21 copies tiles from source 18–19, 60–63 to destination 7,3. The usable opening contact is beyond the old four fixed probes. Scoped continuation reaches its native touch before waiting. The E3 00 control lock stops navigation before the game's optional battle.
- The fine standing bands in scenes 599, 600 and 643 miss tile centres. Warp goals now use the exit lattice (0, 64, 112, 128, 192, 240), keep the arrival box inside the coordinate guard and retain at most 64 nearest points. Cell-distance bounds avoid expanding long guards unnecessarily. Scene 600 can publish exact native-copy preview goals; staged search must still prove the opening contact on the live map.
- Scene 599 uses Extra28 to identify the next currently present child, slot 8–11. Story Events supplies no answer. After all four, it follows the real passage.
- Scene 643 identifies actor 8 as a door console without its private sequence. Its separate forward doorway works in either console state.
- Scene 642 is a prediction beyond the supplied run. Native markers 14/15 run equivalent guarded warps on opposite sides of a narrow corridor. They share a reachable midpoint approach and one passage entry. Their contact footprint stays in that doorway's tile; the broad contact window does not become a map-wide barrier.
- Destination-zero placeholders are omitted; a positive current native destination overrides a static placeholder. Scene 600's invisible encounter checks are not listed as known enemies. Visible enemy sprites retain their existing categories.

Tests embed native-resource and derived planes with explicit provenance. Only scene 600's initial planes are live captures; no other fixture is described as such. The native-resource Python collision port is independent corroboration and has different coverage from the original-instruction Desert emulator.

## Password letter panel

The PC scene uses a two-row letter grid, not a sequence of SNES button names. `PasswordPanelCapture` reads after the existing field-script dispatch, because E3 00 disables ordinary field-input polling.

The reader requires coherent scene 4 / Atel 26, the current global actor store, the scene-4 script header and VirtualPad vtables. The global stick at image+41C3E0 points to pad+290. The grid's manager is pad+2D4, its focus key is manager+2C4 and its hidden input lock is manager+290. Logical visibility, node visibility and input lock must agree; owners and identities are reread. Unknown data stays unknown.

The loaded language chooses atlas-proven glyphs: `F R O C Z / A W T L I`, with Q replacing R in Japanese and both Chinese atlases. Only visible focus and layout are spoken. A hidden panel closes the speech session only after native player control returns. This preserves the accepted-letter hide/show gap without a timer heuristic. Success and timeout leave through native scene transitions. No answer, entry history or correct/incorrect judgement is extracted.

Fresh Ghidra show/grid exports are supplemented by hide/build evidence `ev_e2584a95b2c54f1ecf1949e1e79a2bada994264b1d50e981bd48ef99e555e4bb`. The accident's machine objective is gated by Global7C/20 and no longer reveals the password.

## Lavos flashbacks

The nine presentations are scenes 346–350 and 456–459, Atel 310–318. Each caption requires actor 0's pinned first pause after fade-in, the full installed script digest, renderer/field agreement and the expected loaded or drawn boss actors. Guardian's body and Giga Gaia's head are map art; their actor proof therefore differs from ordinary drawn sprites. Capture runs before the existing dispatcher; speech follows native execution. A null/non-anchor read does not rearm an already spoken entry.

Names use loaded `monster.txt`, bank 32 hexadecimal, and the current character-6 name for Magus. Fresh Ghidra evidence `ev_d04ec2457d33e286b16159ce3c8b9660a75000602fd170b09c2502a5ae109822` shows the TextManager initializer storing `msg/monster.txt` at vector offset C8. Its singleton constructor (`ev_90d8390febfd28115fcfaf454c26b69f9438c8f88da1747e8d6a5cac044bef61`) loads that vector at startup. This resolves the initial audit's concern that the bank might exist only during battle. Unreadable names still remain silent with a bounded diagnostic.

Both UI readers share the existing opcode hook. Neither adds native hooks, writes game state, injects selections, changes timers or depends on optional recorded narration. Hook tests verify before/after ordering, one original call and enable/disable behavior.

## Verification boundaries

The first unrestricted solution run had two test-wording failures (the mod says “up,” not “north”) and three scheduling timeouts in existing Settings, racing and title tests. The three timing tests passed together on retry. The complete suite then passed with the documented xUnit `MaxParallelThreads=4` setting, without product changes for those timeouts. Preserve the first log alongside the passing run.

The native UI delivery first failed 71 positive assertions against API-only stubs, then passed all 130 tests; ten safety mutations were caught. Canonical tests and exact-payload x86 checks are recorded separately. A pre-wiring hook test compilation failure is not counted as a behavioral RED result.

Live game traversal, native UI use, physical controller input, audible speech and installation on the tester's PC remain unverified. The separate native map-texture creation crash is outside this repair.
