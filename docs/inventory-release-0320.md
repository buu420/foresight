# Version 0.3.20: entering an empty Inventory list

Deployed September 14, 2026. The player confirmed Equipment reads correctly in
0.3.19, but one Inventory selection remained silent. A new read-only capture
showed that the Consumables category spoke correctly; entering its blank list
then produced `Unable to read the current selection.`.

The native game enables help mode for every nonnegative encoded item ID,
including the empty placeholder's ID zero. The reader incorrectly required that
flag to be off before announcing an empty row. The fix removes that condition
while retaining the single zero-quantity record, selected cursor, owned control,
and matching visible category-heading checks. The captured failing screen now
reads `Consumables. Empty.` without borrowing item-zero help text.

Claude audited the native help and item-use paths; Codex captured the live state,
ran Ghidra directly after the audit shell stalled, implemented the repair, and
verified the resulting package. The two audits agreed on the help-flag cause.
The [native audit](inventory-empty-row-0320-native-audit.md) also records a
correction to the previous quantity evidence and the conservative rule retained
by this release.

## Verification

- Exact captured-memory replay failed before the fix and passes afterward,
  with no missing reads in either run.
- Release tests: **1,397 passed**, zero failed or skipped: Core 134, Native 685,
  Mod 570, Prism 8. Includes both help-mode states, refusal to call a blank row
  in a populated list empty, and preservation of earlier live menu captures.
- All **141 hook signatures** match the installed supported executable.
- Release package build: zero warnings and errors.
- Installed deployment: **25 passed**, zero failed. Existing launch redirect
  and x86 .NET 9.0.17 runtime verified.
- Every installed file matched its source: 28 mod files, 45 loader/shared-hook
  files, and two native launcher/installer executables. Game executable unchanged.

Behavior commit: `a204df4e36086af956fe68ae452ddd1a39628319`.
Package commit, including corrected native comments:
`d929b08ce3bbe584db2ad59a49079ae1b4dc685e`.
The installed assembly's product version contains this exact package commit.

Installed mod:
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Reloaded-II\Mods\chrono.trigger.accessibility`.

Mod DLL SHA-256:
`81B5B596A78A73660078B95C90614FD0385165CFF7CB3EF131C89938051AD150`.

Manifest SHA-256:
`5125C02989E48B11822E9B8C7154D231BCFC2F3D158B250AB92C4302641CF5C1`.

Previous installation retained at
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260914-153943`.

Full TRX results, hook proof, deployment output, and installed file hashes are in
the worktree's `artifacts/research/release-0320/` directory. The embedded minimal
live capture is `tests/ChronoTriggerAccessibility.Native.Tests/Capture/field-submenu-native-0320.json`.

## Player check

The game closed after the capture and before installation. Speech in the newly
installed game still needs confirmation: relaunch normally and enter the same
empty Inventory list. This release changes only that Inventory empty-row gate;
Equipment's working capture behavior is preserved.
