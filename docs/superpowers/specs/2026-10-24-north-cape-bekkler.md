# North Cape identity and Bekkler copy-game cues

The user approved identifying the North Cape encounter and announcing each visible Bekkler demonstration with its on-screen button, plus the game start/result, while preserving native timing and player input. Existing C# capture, Prism speech and opcode hooks are the implementation choice.

North Cape scene428/Atel0406 has a drawn blue sparkle (slot16, class4, visual113), which calls the encounter controller while G138 is zero. Actual Magus is slot7, class3/visual6, hidden in the supplied entry snapshot. Slot18 is a separately gated doppel doll, not evidence that the real character is present. The requested encounter name should identify the sparkle's role without presenting the light as a visible person. Its appearance remains described, actual character names follow native visibility and custom names, and the later item branch must retain its own label.

Bekkler scene434/Atel0071 reads its introductory controls through the existing dialogue reader, but the supplied run has no demonstrated-pose announcements. The tester's on-screen controls are A/left arm, B/right arm, Y/laugh and X/surprise; these are live mappings, not fixed constants. Resolve each action's mask through the native button-label configuration. Cues must follow completed native visual actions, not a future random choice, and remain brief enough for the game's pace. The game already presents result dialogue; avoid inventing or duplicating hidden score information.

## Approaches

Use the existing native opcode observer and verified actor/script/animation state. It can distinguish actual repeated demonstrations and survives the field input hook being inactive during scripted play. Raw animation polling alone risks missing or merging identical rounds; prerecorded narration alone cannot follow the randomized sequence. The native observer is the selected implementation approach, subject to the bounded audit's exact timing proof. Button-first interrupting Prism cues are the proposed low-latency output, with no new recorded narration assets.

## Constraints

- No generated input, automated play, native timer/physics/state changes or advance notice of hidden choices.
- No global renaming of sparkle graphics, hidden NPC discovery, duplicate normal followers or mislabelled doppel dolls.
- Preserve current native names, controller/button presentation, game/script identity, foreground and disable/unload gates.
- Missing native authority cannot produce a guessed cue. Callback failures must not prevent or repeat native execution.
- Build a local tester candidate and deploy it. Public publication requires the user's separate release request.

Evidence is retained under `artifacts/research/north-cape-bekkler-20261024`. No supplied gameplay was locally reproduced; source-backed/offline checks must not be described as live controller or audible-speech proof.
