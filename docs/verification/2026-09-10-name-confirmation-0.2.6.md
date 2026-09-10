# Name confirmation repair 0.2.6

Pressing Accept previously faulted because the mod counted the prompt window
among the Yes/No choices. The player's diagnostic run captured the complete
question, both choices, and their two distinct managers. Version 0.2.6 uses that
native separation and preserves the actual rendered question, including multiple
lines. See the [native audit](../native-audits/2026-09-10-confirmation-prompt-window.md).

## Verification

- Replaying the three-control live sequence reproduced the old count-guard
  failure. A two-line prompt reproduced the old pending-control failure. Both
  passed after the correction.
- Tests reject missing/unreadable/blank prompts and mismatched prompt control or
  focus. Existing invalid-choice and extra-binding tests still pass. Prompt-window
  focus does not announce a choice; choice focus still announces Yes or No.
- The rendered-prompt regression initially failed the old placeholder requirement.
  It now preserves line breaks and does not substitute remaining `<NAME>` text.
- Full Release suite: **764 passed, 0 failed**: Core 56, Native 421, Prism 8,
  Mod 279. Evidence: `v026-final-release` TRX files under
  `C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro\test-results`.
- All **113 native hook byte contracts** matched. Independent native verification
  additionally checked nine prompt construction/rendering call sites, the
  backslash splitter, `<NAME>` token, and prompt manager's literal focus 0.
  Existing current-name field/getter and Cocos identity checks passed.
- `git diff --check` passed. No native calling conventions or hook registrations
  changed. Claude supplied the independent prompt-window and text-loop audit.

## Installation

Installed at
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Reloaded-II\Mods\chrono.trigger.accessibility`.
The game was closed before deployment. All 25 deployment checks passed, including
the existing Steam redirect. Independent verification matched all 27 installed
SHA-256 entries to the package and confirmed version 0.2.6 and the unchanged game
and Cocos binaries. No sign-in or installer action is required.

| Artifact | SHA-256 |
| --- | --- |
| Mod DLL | `235FF6DC5CEA38B8AD9F2EABAE76E677ABA38C834C3D1F9C2F318D5CD02E4E50` |
| Native capture DLL | `BBD01BFFE338263512AD172CB2A24398D5E19E256943629866F9A64DB2AB3A88` |
| ModConfig.json | `F7C9A70178B270AFBEEA471EA5CA61B6A4E7687689CDFE1AD3258F9FE8732610` |

Evidence: `v026-installed-verification.json` in the research directory. Previous
installation backup:
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260910-121318`.

## Player test still required

Launch from Steam and advance through New Game setup. Press Accept at the name
screen. Check that the question and both Yes/No choices read, then select Yes
to continue into the intro. Report the last spoken line if reading stops or an
error appears. The opening through first control of Crono remains the current
test boundary; no agent launched or controlled the game.

The [earlier intro limits](../native-audits/2026-09-09-new-game-intro.md) and
unverified window destruction/reuse paths remain open. Offline tests establish
the captured-sequence correction, not a successful live gameplay pass.
