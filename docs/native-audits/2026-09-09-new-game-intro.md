# New Game and opening gameplay investigation

## Accepted scope and current installation

The user confirmed the Settings/resolution fix, including continued speech without a freeze after changing resolution. They then requested New Game and the intro, and explicitly selected the first milestone as **all New Game setup screens and the opening scene through first control of Crono**. They operate the game; Computer Use, synthetic input, launching, and closing the game are excluded.

The first investigation used deployed version 0.2.0. The player's subsequent 21:16 test reached Control Descriptions and exposed a duplicate text-capture error when advancing to New Game settings. The correction below is version 0.2.1. Opening dialogue and first control still require the player's next live pass.

## First live failure and nested text correction

The Reloaded log `2026-09-10 02.15.45 ~ Chrono Trigger.txt` records New Game activation and a complete `ControlDescriptionsPresented` at local 21:16:01, then `ControlDescriptionNextActivated` at 21:16:09 followed by `Localized text (3F,5) was captured more than once.` The launcher records a normal user-owned exit at 21:16:15, code 0.

Claude traced the exact executable in Ghidra; Codex independently inspected the artifact and reproduced the failure using the combined Startup/New Game hook composition. `TextManagerGetMsg` at RVA `0x1B9110` calls `OpeTextResolver` at call site RVA `0x1B9150`, then removes the prefix through the first comma when present. The nested resolver returns a temporary raw string; the outer function returns the final string used by the caller. Capturing both as separate UI text records is incorrect even when their text happens to match.

`ModeSelectSteam::init` requests 15 distinct row label/value/help keys. Its first three requests call Ope directly; its first TextManager request is `(3F,05)` at RVA `0x2A9DE8`, matching the first live failure. Ten of these requests use TextManager. The selected-help helpers at RVAs `0x2ABE60` and `0x2ABF20` make no additional localized-text requests.

The shared TextManager detour now tracks its native call depth per thread and restores that depth in `finally`. New Game skips its nested Ope observation during that call, then captures the final TextManager result once. Direct Ope requests still capture normally. Actual duplicate requests, missing/unreadable strings, and name-choice control correlation retain their existing validation. Other menu observers are unchanged.

Regression tests reproduce the native nesting with identical raw text and with a prefix removed in a separate final buffer. They require all three setup rows, selected values/help, Start and lower help, and subsequent native focus/value changes. Additional cases cover processed Yes/No text correlated to the correct controls, all 25 Control Descriptions records through TextManager, a separate duplicate request still failing without partial output, exception cleanup, and another thread's TextManager call not suppressing direct capture. The old code reproduced the exact `(3F,5)` error and the related name-choice correlation failure; the corrected code passed all 79 Mod New Game tests. Claude reviewed the fix and its native-call model; Codex added the Control Descriptions regression and documented the opt-in observation rule following that review.

The final Release suite passed **699 tests, 0 failed**: Core 52, Native 397, Prism 8, Mod 242. Version **0.2.1** was deployed while the game was closed, with **25/25** deployment checks, **27/27** installed payload checksums, and both native executable hashes verified. Installed mod SHA-256: `2E41A507F37C7240A49FDB3E90D28CB0BEE2F535EC23CF3D50DFED19CEFBBA56`. Manifest SHA-256: `21A2DAC4154C4A3DEAD14CBE8AC94E10A1A8607A88A044FC34D36647D966ABB0`. Previous installation: `Accessibility\Backups\20260909-213542`. The existing IFEO redirect remains valid; no registration step is needed. Live verification past Control Descriptions remains pending.

During packaging checks, main's ignored `.build/native` still contained August 18 executables. The previous integration worktree held the September 9 native build that exactly matched both installed executables; native sources had no changes since integration. Codex preserved the stale outputs under the research directory and restored these verified build artifacts into main before rerunning the complete suite. No launcher, installer, or registry behavior changed.

## Native entry flow

Ghidra 12.1.2 imported the exact supported executable read-only. SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. The research script assigns the existing audited fastcall signatures to `SceneManager::create` and `SceneManager::NextScene` before decompiling their callers; otherwise an unanalysed import can omit register arguments from displayed call sites.

The trace establishes:

1. Title action handler RVA `0x2CFFF0`, action 1, resets New Game data and invokes `NextScene(0)`.
2. `NextScene`, RVA `0x297B60`, moves title scene 3 to scene 7. Its factory RVA `0x2AC4F0` constructs the class whose vtable is `0x3B203C` and calls Control Descriptions init RVA `0x2ADB50`.
3. The Control Descriptions Next callback RVA `0x2AE840` invokes `NextScene(0)`. Scene 7 advances to setup scene 8, whose factory is RVA `0x2A9AF0`.
4. That setup factory selects an interface implementation from native configuration. The installed New Game settings hooks target the controller/keyboard `ModeSelectSteam` implementation at init RVA `0x2A9C60` and callback RVA `0x2AB9E0`.
5. The audited Mode Select callback invokes `NextScene(10)` when Start Game is activated. Scene 8 with action 10 enters scene `0x11` via factory RVA `0x2BB5A0`.
6. RTTI identifies the factory's object as `FieldScene`: vtable RVA `0x3A0300`, constructor RVA `0x187450`, init RVA `0x2BBB20`. Field init constructs the field data, map, message-related nodes, and FieldMenu, and schedules updates. Entering this scene alone does **not** establish that player movement is enabled or that a particular location caption is visible.

Name Input is a separate scene (`0x0B`, factory RVA `0x2BFDE0`, init RVA `0x2C0090`, vtable RVA `0x3B7168`). Do not assume it is a direct SceneManager step between setup and FieldScene: the field flow can request it. Its timing relative to the intro remains part of the player test.

## Existing coverage and unresolved behavior

`NewGameHookSet` and `NewGameNarrator` cover Control Descriptions, the three setup rows and Start Game, name entry, the character grid, direct keyboard input, localized action labels, and name-confirmation presentation/focus. `DialogueHookSet` captures MsgWindow open, update, close, and choice confirmation using the audited native layout.

The separate `OpeningMovieTimeline` belongs to the animated title movie (scene `0x1E`). Its authored timeline does not describe the in-engine opening after New Game.

The opening dialogue is present in the installed localized `cmes0.txt` resource. It contains waits, automatic page transitions, and automatic closes, so testing must check that each line is audible once and that queued speech is not cut off. The existing dialogue narrator queues lines and does not issue a generic speech interruption on ordinary close. Resource presence alone does not prove that a live intro line passes through the hooked MsgWindow path.

Static review identified retained name-screen and confirmation-manager state after normal exit, and no name-confirmation activation event. Pointer reuse could misroute later focus, but no live failure has been observed. Claude's additional native trace identifies the relevant callbacks below. The first intro's visual actions and the moment control is granted also lack dedicated narration; those require native anchors and verified visible content.

## Name confirmation and lifetime findings

The confirmation's function-object vtable at RVA `0x3B70B4` routes slot 2 through RVA `0x2C4D30`, which adds four to the function-object pointer, dereferences two arguments, and calls the lambda body at RVA `0x2C3980`. Codex inspected this decompilation and the wrapper bytes independently of Claude's narrative report.

The body takes a closure in ECX and two stack arguments (event type and key). Event 1 plays a cursor sound and returns. Event 0/key 0 records an accepted result; event 0/other key or event 2 records a declined result. These result paths mark the confirmation menu and schedule a delayed continuation. The closure contains the menu at `+0`, name scene at `+4`, proposed MSVC name string at `+8`, and another owner at `+0x20`. Treat event 2 as the native declined-result path; its user-input meaning still needs a live test.

The subsequent function-object wrapper at RVA `0x2C4E00` tail-jumps into a continuation at RVA `0x2C3B00`. The inspected decompilation applies the name and invokes SceneNext for an accepted result; the declined branch re-enables the name entry control. Both remove the confirmation owner. Capturing a choice is therefore distinct from completion of the delayed transition.

NameInputScene's scalar deleting destructor at RVA `0x2BFFC0` calls the class destructor at RVA `0x2BFE50` before freeing the `0x368`-byte object. The class destructor is a candidate to clear tracked name/confirmation pointers before native teardown. The existing hooks do not observe it. Any implementation must verify the complete ABI and exact bytes, scope cleanup to the tracked scene, preserve native calls exactly once, and test reused manager addresses and declined confirmations. These are research findings, not installed fixes.

## Verification and next live pass

Codex independently ran the Release filter `FullyQualifiedName~NewGame|FullyQualifiedName~Dialogue`: **161 passed, 0 failed** (Core 23, Native 46, Mod 92). Claude's independent focused run agreed. These tests exercise existing behavior; they do not cover the unresolved gaps above.

The next player pass is to select New Game, move through its setup screens and any name/confirmation prompt, then continue through the opening until first control. Record the last spoken line if a screen becomes silent, text is cut off, or the game stops responding. Correlate the report with the new semantic log before labeling gameplay coverage verified.

## Reproducible research

Artifacts are in `C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro`:

- `ReviewNewGameNative.java`, `new-game-field-native-review.txt`, and `new-game-field-ghidra-console.log`: exact-build decompilation of the flow above. Earlier untyped/typed reports are retained for provenance; the field report supersedes them for call arguments.
- `ReviewNameLifecycleClaude*.java` and `name-lifecycle-claude*.txt`: Claude's completed read-only callback, continuation, vtable, and teardown trace. In particular, the fourth and fifth reports contain the callback wrappers and lambda body discussed above.
- `test-results/new-game-intro-baseline_*.trx`: Codex's focused test results.
- `ReviewModeSelectTextClaude.java` and `mode-select-text-claude.txt`: wrapper/leaf relationship and exact Mode Select text call sites.
- `test-results/nested-text-red_net9.0_20260909212820.trx`: three expected pre-fix failures and two controls passing. `nested-text-final_net9.0_20260909213431.trx`: all 79 Mod New Game tests passing. `new-game-0.2.1-final-release_*.trx`: the final 699-test suite.
- `inspect_resources.py` and `resource-index.json`: read-only inventory of the installed `resources.bin` archive, whose decoded ARC1 directory contains 9,509 entries. Selected resources are extracted only into this research directory; game assets are unchanged and are not committed into the mod.

The archive structure was cross-checked against [CTViewer's resource reader](https://github.com/GitExl/CTViewer/blob/main/src/filesystem/resourcesbin.rs) and [ChronoMod's resource reader](https://github.com/jimzrt/ChronoMod/blob/main/resourcebin.cpp). [CTViewer's dialogue documentation](https://github.com/GitExl/CTViewer/blob/main/docs/scene_scripts/dialogue.md) provides a comparison for PC message-table mapping and control tags; installed data and the exact native executable remain authoritative.
