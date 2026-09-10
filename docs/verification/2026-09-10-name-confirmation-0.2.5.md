# Name confirmation diagnostic build 0.2.5

The player's 0.2.4 run correctly announced the current name, Crono, and the
four naming actions. Pressing Accept still failed at the guard requiring two
constructed CustomButtons and two correlated rendered labels. The log was
`2026-09-10 16.25.52 ~ Chrono Trigger.txt` under the Reloaded AppData logs;
the only accessibility error occurred at local 11:27:34.

That error omitted the capture counts. It cannot distinguish an extra control
from missing observations. This build adds diagnostics; it does not claim to
repair confirmation or establish intro coverage.

## Capture changes

The existing hook signatures, thread-owned capture rules, label correlation,
manager checks, native calls, and speech events remain the same. The incomplete
capture error now includes the actual control and label counts and whether a
label is pending.

Each instrumented confirmation builder records at most 64 ordered trace entries
and a final summary. The trace is written to the existing Reloaded log as one
diagnostic message, before the coverage error, with no Prism output, semantic
state change, or additional dialog. The final summary includes captured pointer
sets, rendered label text, text keys, bindings, focus, errors, and dropped-entry
count. Logged text is escaped and limited to 160 characters per item.

The trace reference is visible to other threads only while the native builder
runs. A callback without the owning thread scope is logged with `scopeMatch=False`
and is not used as captured data. The reference is cleared in `finally`, including
when the native wrapper throws, and when hooks are disabled. Diagnostic sink
failures are contained.

Each entry includes managed thread ID, scope match, active epoch, fault state,
and capture counts before processing that callback. Argument fields map as follows:

| Event | receiver | data | key0 / key1 | returned |
| --- | --- | --- | --- | --- |
| Builder.begin | name scene | unused | name byte length / capacity | unused |
| CustomButton.enter / return | constructor storage | unused | unused | native result on return |
| OpeTextResolver / TextManagerGetMsg | resolver or manager | result buffer | bank / message ID | returned text buffer |
| MenuTextLabelFactory | position argument | rendered text string | font size / unused | label node |
| ControlBinder | manager | FocusableState | manager key / unused | unused |
| FocusSet | manager | unused | manager key / unused | unused |

`suppressed` records whether Ope capture is suppressed inside TextManager::getMsg.
Name-character labels before the first choice constructor are also traced, even
though the semantic capture intentionally ignores them.

## Verification

- New regression tests first reproduced the missing diagnostics (7 failures).
- The final focused suite passed 88 tests. Fixtures cover zero or partial capture,
  an extra third constructor, reused native text storage, preceding font-12 name
  labels, callbacks on another thread, bounded trace size, native-wrapper failure,
  and diagnostic sink failure without altering successful native or speech results.
- Full Release suite: **756 passed, 0 failed**: Core 56, Native 420, Prism 8, Mod 272.
  TRX evidence is under
  `C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro\test-results`,
  prefixed `v025-final-release`.
- All 113 installed native hook byte contracts and the previously audited name
  layout and confirmation call targets matched again. Evidence:
  `v025-native-contract-verification.json` in the research directory.
- A fresh Ghidra read-only pass over the exact executable inspected the suggested
  builder-tail helper at RVA `0x2C5900`. It allocates and copies a callback object;
  there is no direct CustomButton or label-factory call in that helper. Evidence:
  `ReviewConfirmTailCodex.java`, `confirm-tail-codex.txt`, `confirm-tail-ghidra.log`.
- Claude's read-only review is `confirm-runtime-review-claude.md` in that directory.
  It identifies an extra constructor as another plausible explanation. The existing
  log does not establish that explanation, and the constructor hook's Name Entry
  activity does not prove its delivery inside the confirmation scope.

## Installation

Installed at
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Reloaded-II\Mods\chrono.trigger.accessibility`.
The game was closed before deployment. All 25 deployment checks passed, including
the existing Steam launch redirect. Independent verification matched all 27
installed SHA-256 entries to the package, confirmed version 0.2.5, and verified
the unmodified game and Cocos binaries. No sign-in or installer step is needed.

| Artifact | SHA-256 |
| --- | --- |
| Mod DLL | `76457EC28D22B9C134D82019C65967F0E0B9C3CC752D6EAF955A495FD4223950` |
| Native capture DLL | `1FF37EE81C8BED5B4D1C34CDBCFC07F9F62E398239622DC4A327B9CF3A7C9450` |
| ModConfig.json | `1A167BBC6DFA3430F4F2387AD3D5383E2B8EFE472ECD5A5DDA22425BAEF0EBE2` |

Evidence: `v025-installed-verification.json` in the research directory. Previous
installation backup:
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260910-114822`.

## Player test

Launch from Steam, choose New Game, advance to the name screen, and press Accept
once. The same error may still appear, now with counts. Report when it happens;
the local log contains the detailed trace. No agent launched or controlled the
game. Confirmation and the intro through first control remain unverified.
