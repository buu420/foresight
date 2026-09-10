# Name Entry and Accept version 0.2.4

Subsequent player test: the current name correctly read as Crono and the naming
actions read, but Accept still failed the two-control/two-rendered-label guard.
See the [0.2.5 diagnostic follow-up](2026-09-10-name-confirmation-0.2.5.md).
The verification below records the original 0.2.4 delivery state.

This release corrects the name reader's use of the saved grid-entry name and
captures confirmation choices from the text actually passed to the label
renderer. The previous version could announce an empty name while the game
held a nonempty name, then fault because a choice lookup notification was
missing. See the [native audit](../native-audits/2026-09-10-name-accept.md).

Version 0.2.4 is installed at
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Reloaded-II\Mods\chrono.trigger.accessibility`.
The existing Steam launch redirect passed verification; no sign-in or installer
step is needed to test the game.

## Verification

- The stale-name regression failed all three cases before the source correction:
  saved empty/current Crono, saved Crono/current Lucca, and saved Crono/current
  empty. All now pass.
- The confirmation regression reproduced the prior constructor-correlation
  failure when localized lookup observations were absent. It now captures the
  actual rendered `Oui`/`Non` labels through the shared label hook, preserving
  the original return and native focus on the second choice.
- Missing, blank, unreadable, null, duplicate, and conflicting rendered-label
  cases still fail explicitly. Pointer ownership, exact vtables, getter bytes,
  UTF-8, capacity, NUL-termination, and name-length checks remain enforced.
- Full Release suite: **746 passed, 0 failed** (Core 56, Native 420, Prism 8,
  Mod 262). Evidence: `v024-final-release` TRX files under
  `C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro\test-results`.
- **113/113 native hook byte contracts** matched the installed executable.
  Independent verification also checked the NameEdit construction/getter path,
  the installed Cocos library identity and getter bytes, and the confirmation's
  constructor, text-lookup, and rendered-label call targets. Evidence:
  `v024-native-contract-verification.json` in the same research directory.
- `git diff --check` passed. Packaging completed with the reviewed Prism hash.
- Deployment: **25/25 checks**. Independent installed verification:
  **27/27 SHA-256 entries**, version 0.2.4, matching package files, and unchanged
  game/Cocos identities. Evidence: `v024-installed-verification.json` in the
  research directory. The game was closed before deployment.

## Installed artifacts

| Artifact | SHA-256 |
| --- | --- |
| Mod DLL | `57D51656B0041A1288ADA7645475E9F0B7EA03898B3549AD44A53EA48D837A72` |
| Native capture DLL | `577BA854533B0057ABE4B2F3D327677877CC4B5B8AD5BB27858C0B888CE6A54A` |
| ModConfig.json | `DB7576362D5CF6885D28A33AF93426659DE2E40B8A93003F0125AC3E4208F95F` |
| Launcher EXE | `972E2145667E5D30BAE22AEC19DA217C8B715984EF23C83D915BD12CF62D717D` |
| Installer EXE | `A59A0FB2107D9FA86F8A6DBF9A154510A6E024F25FDD0A32630AF17EADD15D84` |

Previous installation backup:
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260910-111955`.

## Player verification still required

Launch from Steam with the screen reader running. Choose New Game, advance
through setup, and check the current name before pressing Accept. The prompt
and both choices should read. Continue through the intro toward first control
of Crono, reporting the last spoken line and any error.

These changes have not yet been verified in the running game by the player.
No agent launched or controlled the game. The earlier
[intro coverage limits](../native-audits/2026-09-09-new-game-intro.md) still apply.
