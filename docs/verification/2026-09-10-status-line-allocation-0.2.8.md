# StatusBar empty-line allocation 0.2.8

Version 0.2.7 still produced one top-menu missing-label-marker error in the player
log at 14:47:38. Claude located the missing call in the native StatusBar formatter;
Codex independently verified it in Ghidra and checked the machine instructions.
The diagnosis and exact constraints are in
`../native-audits/2026-09-10-status-line-allocation.md`.

## Tests

The final Release solution run passed **802 tests**, with no failures or skipped
tests: Core 56, Prism 8, Native 438, and Mod 300. The four TRX reports are under
`C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro\test-results`
with the prefix `v028-final-release`.

Five new regression cases failed before the call was instrumented, then passed:
classic and touch line allocations preserve rendered lines, nonempty input
(including whitespace) cannot be silently discarded, and an allocation outside
its owned formatter is rejected. Another test verifies calls outside any top-menu
build pass through without leaving a stale marker. The existing strict-marker
and native-original-call checks remain in place.

A missing-marker regression also first failed because its error had no identifying
diagnostic. It now records a bounded native-text preview separately from speech.
A throwing diagnostic sink still cannot interrupt the native call or suppress
the coverage error.

All **121** native hook signatures match the installed supported executable.

## Deployment

After the player closed the game, the tested Release output was packaged and
deployed using `-SkipNativeBuild -SkipIfeo`. The existing launcher and registered
redirect were reused. No game, installer, or input-control tool was launched.

- Deployment verifier: **25 passed, 0 failed**.
- All **27** package checksums match the staged and installed payload.
- All four built accessibility DLLs match their installed copies.
- Installed version: **0.2.8**.
- Mod DLL SHA-256: `1E188898401B4AB35A26B05DD3366CBDD7AE6FC9DF887CAFFCFAB4417E1723CD`.
- Native DLL SHA-256: `96A639AF6EA057A4BECC63427146E88B662391B8D1AA67540465B8A84C6E0E93`.
- Manifest SHA-256: `D0A02E307ADCCB63D42EA2FDF7A1BE128F403320D9672F913F9DD5A5737F3D9E`.
- The game executable remains unchanged at the supported SHA-256.

The independent record is `v028-installed-verification.json` in the local
research directory. The previous installation is preserved at
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260910-150108`.

## Player check still needed

Launch from Steam and open the in-game menu after gaining control. Choosing
**New Game** additionally arms the existing intro recorder so the same pass can
supply evidence for the requested descriptions. The 0.2.7 replay used Resume and
therefore did not record intro events. Automatic intro descriptions remain off;
neither their timing nor this menu correction has been verified in-game yet.
