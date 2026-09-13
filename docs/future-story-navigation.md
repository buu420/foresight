# First journey through 2300 AD and the End of Time, 0.3.11

This document records the 0.3.11 release. Its discovery restrictions were superseded by [0.3.12 guide navigation](guide-navigation-availability.md).

This release extends the existing Story Events category beyond the prison escape. The controls, one-leg manual directions, movement input, and countable footsteps are unchanged.

## Coverage

- Bangor and Trann Domes, both halves of Site 16, and Arris Dome.
- The food-store investigation, the rat pursuit, the basement consoles and passages, the computer-room sequence, and the return to Doan.
- Site 32's parking areas and walking highway, Proto Dome, and the first repair of Robo.
- The Derelict Factory's entrance, warehouse, conveyor passage, inspection room, crane rooms, laboratory floors, passcode console, power switch, and escape.
- The first End of Time arrival, the old man's introduction and call back, the room behind him, and Spekkio's magic lesson.

Named scenery includes Enertrons, consoles, lifts, the factory sign and crane controls. The Exits category names the native doorways and ladders. Current objectives bind to visible or previously discovered targets; an undiscovered destination remains an explanatory note with no invented route. The warehouse keeps its instructions and available actions together because reading its code terminals does not set a persistent native progress flag. It does not claim that a code was read or remember a guessed completion state.

The rat moves, so guidance follows its observed position; catching it still requires the player's Confirm input. Console combinations and the crane are operated by the player. Crane timing already has an audible tone. Spekkio's clockwise wall laps remain manual. Racing, puzzle input automation, combat accessibility, and cutscene audio description are not added by this release. These are navigation and objective additions, not a claim that every mechanic in this chapter is accessible.

## Research and source attribution

The requested guide is [vinheim and Bkstunt's Chrono Trigger FAQ](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/64344). Its earlier captured reference was recovered from the user's Dropbox archive during this work. Its chapter order and room connections were compared with the installed PC data; guide button names were not copied into keyboard instructions.

Additional sequence references: [Thonky, Beyond the Ruins](https://www.thonky.com/chrono-trigger/beyond-the-ruins), [Factory Ruins](https://www.thonky.com/chrono-trigger/factory-ruins), and [End of Time](https://www.thonky.com/chrono-trigger/end-of-time). The mod uses short original objective labels and does not bundle a guide or the game's dialogue.

Native format reference: [GitExl/CTViewer](https://github.com/GitExl/CTViewer), commit `2e5a206e09f0028fd5a1ca6cb9a9ed18bfc64fea`, MIT. A reproducible reader and instruction-boundary audit now live in `tools/research/future_story`; its notice is included there. The audit reads the user's installed resources and writes only the requested research report. It validates 32 scenes and the future world entrance table. It is not used at runtime.

Ghidra and native disassembly used the executable with SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. The existing field capture reads actor class at record +40 and visual index at +44. Opcode 81 writes class 3 and a player visual; 82 writes class 4 and the raw NPC visual; 83 writes class 5 and a 16-bit monster visual. Handler RVAs: 162A61, 16B010 and 16B090. Scenery controllers that do not load a sprite retain the existing class-7 marker contract. Only explicitly audited slots are admitted as scenery, using their live coordinates and activation state.

The reference decoder has a misleading opcode-55 source/destination interpretation. Fresh Ghidra output at RVA 168110 confirms it **reads** A+110B0 into local storage. Opcode 5A and opcode 56 with global index zero write story progress. PC operand lengths and helper reads were checked before using the decoded scripts. In particular, A2 has six operands, A3 two, FC one, and text and location-change instructions use the PC widths.

## Progress and routing evidence

| Native context | Evidence used |
|---|---|
| Future arrival | The forest script writes point 51; Arris advances 52 and 53; the computer sequence sets 54 and Doan's return sets 55. |
| Rat investigation | Global EC bit 10 enables the chase after reading the note. Catching clears bit 10 and sets bit 40. The caught-state check precedes the note-state check. |
| Arris bridge | Scene 216 actor 9 is the right console. Its talk function calls actor 8's operation, which toggles global A4 bit 40. The left console calls the other operation. The east passage is native exit 2. |
| Site 32 | Scene 223 has only a static west exit. Actor 8's startup at file 02AF reads the player's tiles; 02B3 tests X > 46 and 02B8 tests Y < 11. The eastern-highway target uses walkable, observed cells in this actual transition region, not the controller's position at tile 43,5. |
| Computer room | Scene 218 actor 0 updates player Y; actor 11 tests Y == 42 at file 0487. Its own FF,FF position is deliberately not a route target. Only visible walkable cells in the arrival row are offered. |
| Factory | Global 58 bit 04 is entrance security, bits 20 and 40 are the two crane patterns, and bit 01 disables the laboratory lasers. Global 5C bit 20 records the opened hatch. The other low bits of 5C describe conveyor encounters, not crane completion. |
| Power and escape | Global 1D0 bit 01 opens the security door. Scene 235 actor 15 invokes the passcode console; actor 10 operates the power switch and writes point 63 at file 0564. Escape uses the ladder, not the disabled elevator. Scene 230 then advances 64 and 66; Proto's repair ends at 69. |
| End of Time | The first arrival reaches 72, introduction 73, call back 74, room invitation 75, lesson 76, magic 77. The catalog stops after this first visit. |
| World 2 | Live native collision data separates the Bangor/Trann side, Arris side, and Proto/Factory side. A cached connected-region index filters only story objectives; visible ordinary entrances stay in Exits. A changed map or property table rebuilds the index. Routes still stop at field transitions and never cross world-map wrapping boundaries. |

Static exit identities come from `MapJumpOffsetTbl.dat` and `MapJumpDataTbl.dat`, not room-name guesses. Onward routes select the appropriate side of Site 16 and Site 32 and reverse through the Arris basement after point 54. Their positions still come from the current native exit grid. Scripted transition targets retain discovered goals while they remain walkable, and retire when their story stage or native controller disappears.

## Validation and limits

The full Release suite passed 1,120 tests (Core 128, Native 537, Mod 447, Prism 8). The archived-resource audit passed all 32 scene instruction-boundary checks. Focused tests exercise the full Arris flag chain, the return through the Guardian chamber, forward-versus-backward exits, factory power and disabled lifts, the End of Time stages, unknown flags, visibility, retired scenery, and both scripted transition regions. Future world tests cover disconnected regions, a moving player, and collision-cache invalidation. Native capture tests check all five added global indices at expanded dword offsets, including unstable and missing reads.

The player confirmed that 0.3.10 footsteps accurately match navigation distance. This release preserves that implementation. The new area catalog has been checked against scripts and offline tests; a live playthrough of these added areas remains unverified. No game input or save manipulation was performed for this release.
