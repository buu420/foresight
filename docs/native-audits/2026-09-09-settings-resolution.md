# Settings integration native audit

The installed startup-only build had no Settings hooks. Its September 9 runtime log announced `Settings selected.` and then produced no Settings events. The integrated build uses `CreateCompleteAccessibilityComposition`, retaining the portable Reloaded loader and enabling the existing Settings, Extras, field-dialogue, and top-menu hook sets.

## Exact executable verification

- Executable: Steam Chrono Trigger 2.0.0.1, x86, image base `0x00400000`.
- SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
- Ghidra 12.1.2 imported the executable read-only and compared all 112 `GameVersionCatalog` byte contracts. All 112 matched; the original executable was not modified.
- The catalog includes the constant-valued `MsgWindowChoiceConfirmCallRva` as well as literal RVAs. A text scan that counts only literal RVAs incorrectly reports 111.
- Evidence and the reproducible Java script are saved in `C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-recovery`: `ReviewSettingsNative.java`, `expanded-contracts.tsv`, `settings-native-review.txt`, and `settings-ghidra.log`.

The audit also disassembled and decompiled the outer Settings builder (`0x1ED020`), resolution builder (`0x1FA010`), resolution callback (`0x1FB100`), post-selection refresh (`0x1F4EE0`), and Settings row callback (`0x1F21F0`). Byte verification establishes compatibility with this executable, not correctness of every hook under live input.

## Corrected title Settings layout

The title branch of the outer builder constructs four category records (stride `0x30`) and **three** descriptor vectors (stride `0x0C`). It builds row lists of two, two, and one row before passing the three-vector range to `0x1FB510`. The temporary-array cleanup explicitly uses a count of three and stride `0x0C`. The corresponding in-game branch uses four descriptor vectors.

The older `task-6b-settings-menu-research.md` note and original capture fixtures said title Settings had two descriptors. That statement is superseded by this audit. The recovered production capture already required three. Fixtures now include the third (Licenses) descriptor and explicitly reject a title root with only two.

Additional native capture checks cover categories with empty help text, the Screen Size action's actual rendered text when it differs from the backing value list, and the visible return-control key when Screen Size is hidden. Hidden backing rows must not be announced as visible controls.

## Resolution transition

The native resolution callback reads its manager's current focus at `+0x2C4`, invokes the setter, rebuilds Display Settings via `0x1F0310`, refreshes it, and closes the selector before returning. The resolution builder omits the first backing value when constructing selectable entries. Therefore the callback's `count - 1` limit must be interpreted against that backing vector, not used to discard the final displayed resolution.

Claude reproduced the stale-focus classification defect in an executable hook harness: after a native page rebuild, the old code emitted `MenuFocusChanged` instead of `MenuPresented`. The integrated implementation reads the native focus for focus-movement classification and uses the observed page generation change to prove that an apply rebuilt the page. This matters because the callback can reject an otherwise valid entry during its mode/size compatibility check and return before rebuilding. A rejected selection keeps the selector active; an observed rebuild resumes page narration even if the old selector manager is unreadable. Event 2 has no native selection effect and no longer produces `MenuActivated`.

A proposed 750 ms publication delay did not establish caller ordering. The title callback already publishes activation before calling native code and explicitly flushes Settings after that call returns. Title Settings now uses that completion signal without a competing timer. The existing in-game deferred entry path remains covered separately. Ten Settings runtime tests cover these transitions, including repeated changes and a slow title caller; the combined-composition test prepares every native contract once and exercises activation and disable across all seven participants.

Prism output has no game-window dependency. Window discovery is an initialization step; changing resolution does not by itself establish a speech-backend failure. Native state, callback ordering, and the subsequent semantic log must determine whether a transition loses narration.

## Offline verification and deployment

The final Release suite passed **692/692** tests (Core 52, Native 397, Prism 8, Mod 235). Results are in `chrono-trigger-recovery/test-results/integrated-final-pass_*.trx`. The packaging subprocess tests now isolate their Windows PowerShell module path and disable MSBuild server/node reuse; this resolved a test hang after the package had already been written, without changing the packaged runtime.

Version **0.2.0** was deployed on September 9, 2026. Deployment verification passed **25/25** checks, all **27** payload checksums matched the installed files, and both installed native executables matched their reviewed build output. The existing IFEO redirect remains valid and requires no registration step. The original game executable hash remains unchanged.

- Installed mod DLL SHA-256: `4B19B5BC7DEF5EAC3F1A6A04C9334752303113F133F7AD052E305D1876C57BB5`.
- Previous installation: `X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260909-175956`.

## Player verification

The user will operate the game. No Computer Use or synthetic game input is part of this validation.

After deployment, launch through Steam and check Settings entry and category/value navigation. Open Screen Size, cancel once, reopen and select a different size, then navigate Settings and return to the title menu. Repeat Settings entry and restore the original size. Record the last spoken line and selected size if output stops. Live narration after resolution changes remains unverified until this player test is reported and correlated with the new runtime log.
