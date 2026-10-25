# North Cape and Bekkler native evidence, 0.3.73

The user approved these two reports and native input/timing preservation. Original tester log: `2026-10-22 16.12.07 ~ Chrono Trigger.txt`, 6,560 lines, SHA256 `b496111153a6d4c43c56196126b8bad6c2336125d0119f146e031c003a0ad888`. Retained local evidence: `artifacts/research/north-cape-bekkler-20261024`. The log contains North Cape's entry actors and Bekkler's introductory controls and result; it does not contain pose-by-pose telemetry or prove any number of completed rounds.

## North Cape

Scene428 uses Atel0406. Entry actor16 is drawn class4/visual113 (c120 blue shimmering light). Its interaction calls the Magus encounter controller only while global0x138 equals zero. The actual Magus is actor7, initially hidden. Actor18 is a separately gated doppel doll, not the actual character. The light's later branch is an item pickup. `NorthCapeTargets` therefore names the encounter role only with coherent scene, usable drawn sprite, enabled native calls and the zero guard. Its appearance remains described as sparkling light. It does not move a concealed character into People. Native character-name lookup preserves renames.

## Bekkler

Scene434 uses Atel0071, 25 actors. Script SHA256 `211ce025f2daae57d6aece95c5c11aa1c75173146da8eeb30fc947cf2470fd78` (case-insensitive). Header16: `19200344034403450345034503450345`. Runtime PCs omit the leading actor-count byte. Controller0 advances the game; actor9 demonstrates, actor10 polls input, actor24 gestures. Actor9 normally copies the party leader's visual0..6; the quest's doll mode forces Crono0.

| Runtime PC | Actor | Native action | Required input mask |
| --- | --- | --- | --- |
| 0382, 4F 80 11 | 0 | Opens the input window | No cue from a random choice |
| 053F, AA 1E | 9 | Right arm | CONF, 80 |
| 0546, AA 1F | 9 | Left arm | WARP, 40 |
| 054D, AA 09 | 9 | Surprise | DASH, 08 |
| 0554, AA 1A | 9 | Laugh | MENU, 01 |
| 03C4, 77 1D | 0 | Clears the active flag on failure | Native result follows |
| 03AE, EA 1C | 0 | Plays the completion music | Native result follows |

Every capture checks the scene, current executing actor, exact script header and eight instruction bytes. Post-call proof keeps context/data/actor-store/field ownership and the current round stable. AA must actually write actor9's expected animation and mode1 while drawn, and advance its dispatch PC. The round must remain active. A held press can be accepted before the pose; that visible demonstration is still described even after input acceptance. The runtime must have observed this owner's window opening for the current round. The internal round ordinal is used only for deduplication, never spoken. Repeated identical poses in different rounds therefore remain distinct.

No RNG/local07 is read. Local10 is not read before the visible pose. Only after the completed visual write does it defensively corroborate the current pose. The independent scheduling proof resolves the earlier audit's uncertainty: the per-tick opcode budget is five, and 87 01 sets the doppel period to two updates. Normal flow has no stale pre-loop pose; every demonstrated pose follows its round's window. It never chooses or predicts the announced action. The announcement comes from the executed AA and its applied animation.

Button labels follow FUN00597330: config pointer at imageBase+41B4C4; its four masks at68F4/68F8/68FC/6900 map to A/B/Y/X. The first matching mask is the same rule used by the shipped settings reader. Unknown or unreadable bindings omit the letter and keep the pose description. The independent review verifies that this executable loads the pad-token text bank, including for keyboard play. No physical keyboard key is guessed.

The field-control hook is idle while this script owns control. The observer shares the existing field-opcode hook, calls its native original once, and publishes interrupting Prism speech without a throttle or narration queue. The first button/action and start message share one utterance, with the actionable button first. A separate start utterance would be interrupted within 0-50 ms by that pose. Later rounds omit the start suffix. Capture, completion, foreground, speech and diagnostic failures cannot skip or repeat native execution. Disable/unload uses an epoch and clears round ownership. Foreground loss discards cues rather than queueing them. Four exact actor9 pose locations supersede generic recorded story gestures; other scenes keep their existing descriptions.

## Authority and verification limits

The supported executable SHA256 remains `8fe9d75e4cdc279645c5bc932fc163fd67147255fc0c673ac45bbf0a6d2e00d7`. Fresh read-only Ghidra exports cover request handler RVA164970, button labels197330, looping animation16E590 and music171780. The native audit and final review use actual Claude Opus5.5 at requested max effort. REA tools were unavailable in this session; existing Ghidra and installed script evidence supply the authority.

The web search corroborates that this is an action-copy game, but older platform button layouts are unsuitable implementation authority. An original [SNES manual scan](https://www.retrogames.cz/manualy/SNES/Chrono_Trigger_-_SNES_-_Manual.pdf) and [historical game-script transcription](https://gamefaqs.gamespot.com/ps/562913-chrono-trigger/faqs/43363) are background references only. Current PC script, input layout and the native button-label function control the implementation.

Offline tests use explicit memory fixtures and an open terrain fixture for the Cape label; they do not prove live traversal, audible cue speed, controller mapping on a tester's machine or successful completion of the minigame. Those checks remain for live testing. No timer, state, physics or generated-input write is added.
