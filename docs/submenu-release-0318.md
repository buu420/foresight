# Version 0.3.18: field submenus and Save slots

Deployed September 14, 2026. The player reported that combat worked, but in-game
submenus and the Save file list were silent. The Reloaded log confirmed Inventory
was still crossing the previous milestone's deliberate unsupported boundary.

The release adds native focus and selected-content readers for classic Inventory,
Tech, and Party, plus Steam Equipment and Save/Load. Inventory includes quantity,
displayed help, and reordering state. Tech includes its separately rendered help
and MP/requirements panels. Equipment follows the nested CharaEquipManager and
its detail container. Save announces only the selected card and visible preview;
empty cards cannot read a stale preview. Existing confirmations retain speech
ownership until they close. Persistent capture failure is audible after a short
redraw grace period, and valid state can recover without disabling the mod.

Claude owned the field submenu capture and native audit. Codex owned Save, Tech
detail panels, integration, and deployment, and independently corrected and
verified the Equipment owner chain and active-manager handling. The final review
also checked confirmation ownership, native argument forwarding, and bounds.
An unreadable Tech manager is retried rather than represented as a proven active
or inactive manager. The update hook runs every native update and samples again
after its 80 ms interval, including when the last selection has not moved.

Native evidence: [field submenus](field-submenus-native-audit.md) and
[Save, Tech, and integration](field-save-slots-native-audit.md).

## Verification

- Release tests: **1,383 passed**, zero failed or skipped: Core 134, Native 671,
  Mod 570, Prism 8. Includes packaging and transactional deployment regressions.
- All **141 hook signatures** match the supported installed executable, including
  the seven new boundaries. Original calls preserve their arguments and returns.
- Release build: zero warnings and errors.
- Installed deployment: **25 passed**, zero failed. The existing launch redirect
  and x86 .NET 9.0.17 runtime were verified.
- Compared every installed file to its source: 28 mod files, 45 loader/shared-hook
  files, and two native launchers. Game executable hash remained unchanged.

Code commit: `e00a1a30a5e9c9b2723a3ad0499b933fc6b4b8f3`.

Installed mod: `X:\SteamLibrary\steamapps\common\Chrono Trigger\Reloaded-II\Mods\chrono.trigger.accessibility`.

Mod DLL SHA-256:
`02973BE987B31A79D447BB29FD53BF22C1211D5D3E814C0776D5E48DEDCE59FC`.

Manifest SHA-256:
`C2DD00CF71C67F19C9652E45A378376DCA4C7DD0E4AC078B5F6F5407765432D4`.

Previous installation retained at
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260914-103919`.

Full TRX results, hook signatures, deployment output, and per-file hashes are in
the active worktree's `artifacts/research/release-0318/` directory.

## Live test still required

The new readers have not been exercised in the running game. Launch normally,
open the menu with V, and browse Inventory and Save files with the normal keys.
Also check Tech details, Equipment slot/item changes, Party, and returning from
confirmations. The touch-interface twins remain outside this new coverage.
Equipment announces rendered stat text without interpreting unlabeled numeric
columns. Runtime speech quality and completeness remain to be confirmed by the
player.
