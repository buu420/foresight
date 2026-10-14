# Guardia Forest's first time gate: discovery and Confirm routing

## Report and native identity

The tester reached the clearing after escaping prison and the Chancellor's pursuit. The time gate could be used through the game, but the entity list omitted it until after the transition. They had to search the clearing manually.

The supplied `2026-10-14 00.04.26 ~ Chrono Trigger.txt` is 4,469,722 bytes, 16,489 lines, SHA-256 `3a82c931446a964486332854390d3f5d0f07177d6edabbda1011ae8140b763e5`. At log lines 15205–15207, scene **20** has story point **51 / 0x33**. Actor **9** is loaded, usable, drawn, class **4**, visual **151 / 0x97**, and has script calls enabled. Its fine position is **(2432,2815)**; the leader starts at **(1925,2807)**. The viewport contains the gate. The transient activation byte is zero, but its native scan binding is 128.

The object list offers only “Sealed box”; Story Events reports no guidance. The gate listed later in scene **208** is another actor, **11**, after the first transition. It does not establish that the missing clearing gate had become discoverable before use.

The installed executable matches the tester and supported Ghidra project: SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Private proof files retain the full identity and matching resource archive hash.

## Installed script and engine proof

Scene20 uses **Atel_0114**, map table **82**. Actor9's startup at **0x0512** loads visual `0x97`, places it at tile `(9,10)`, and hides it. Actor0's loop at **0x02BB** checks story `>=0x30` and requests actor9 function4 (**0x0527**) to draw it. The chase cutscene advances the story to `0x33` before returning control.

Actor9's **Confirm** handler, function1 at **0x051C**, sets local flag9 and hides the sprite. Its touch handler, function2 at **0x0520**, is empty. Actor1's loop at **0x0332** consumes local9, runs the gate sequence and changes scene. This is a Confirm-only interaction, not a coordinate or touch trigger. The destination depends on native story/local state and is not exposed by this fix.

Fresh read-only Ghidra12.1.2 exports cover Confirm routing (**0x17D0C0**), the facing scan (**0x17D230**) and dispatch (**0x17FA20**). Exports use Ghidra's documented [decompiler API](https://ghidra.re/ghidra_docs/api/ghidra/app/decompiler/DecompileResults.html). The reader keeps the existing native geometry, selected-actor, persistent call-gate and script-priority checks. Native scan byte `+0x20` is camera dependent: planning can lead toward an actor outside the camera, while arrival must validate the live scan/dispatch state. The zero `+0x152` activation byte is not a permanent statement that this gate cannot be used.

The initial collision fixture is decoded from installed MapTable82. Fresh **0xDB1F0** decompilation confirms that the packed collision-shape byte is copied directly, with only the RLE flag stripped. It must not be shifted left again. The fixture is a 16-by-16 initial resource map with the native exit back to the forest, combined with logged actor positions. It is not a live post-cutscene terrain capture.

## Why it was omitted

The catalog gives scene20 actor9 no actions: its own Confirm handler only sets a local flag, while a different actor performs the warp. The object filter requires an activation candidate or an available proven action, so it drops this drawn gate before reaching the existing “Time Gate” appearance label. In contrast, scene208's gate has a direct cataloged warp and is listed.

There is a separate story-guide mismatch. `EarlyStoryTargets` previously ended at point48, and its clearing entry also required48. The tester's controllable clearing state is51, so it had no Story Events target despite following the preceding forest objective.

## Repair and bounds

The fix recognizes the installed script contract for **scene20 actor9 only**: coherent scene, known story at least48, correct class/visual, loaded, usable, drawn and script-call enabled. It preserves ordinary native Confirm approaches and arrival checks. Story Events supports the clearing through51 and binds to the actually available gate. It creates no gate objective when the target is absent, and the hidden sprite is retired after Confirm.

Other visual151 placements without catalog actions remain unchanged. Several may be cutscene-only or use different controllers; a shared model is insufficient proof that they can be used.

Five regressions failed before implementation, while nine exclusion cases passed. Tests after the fix cover Objects and Story Events in manual and automatic modes, verify that the native facing scan selects actor9 at the route goal, and require player Confirm at arrival. They also cover hidden/removed/unloaded/disabled/wrong-identity/unknown-state cases and retirement of a previously seen gate. Tests and Ghidra are offline evidence. No live traversal, controller or speech run was performed.

Private evidence: `artifacts/research/forest-gate-0360`, including tester excerpts and identity, installed-map source metadata, Ghidra exports, native-script findings, failing and passing test results, and independent review.
