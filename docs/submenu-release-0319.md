# Version 0.3.19: Inventory categories and character cards

Deployed September 14, 2026. Inventory's selected category was silent because
its button contains an icon and no text. Equipment's selected character card
was rejected because its 18 native Label fragments exceeded the reader's limit
of 12.

Inventory now announces the focused category using the game's loaded caption.
It also announces an empty category when the native list contains its one
zero-quantity placeholder and the visible category heading matches. A blank
row inside a populated list cannot make that list appear empty. Item-row
speech now requires the focus key and row cursor to agree.

Character-card capture accepts up to 64 fragments within the independently
selected control, retaining ownership and visibility checks. The game's
standalone separator Labels are joined into readable clauses. The saved live
Equipment frame reads `Crono LV 1. HP 43/70. MP 8/8. EXP 10. Next 10`.
The saved Inventory frame reads `Consumables, category. Empty.`.

Claude audited the native category and empty-list paths. Codex captured the live
states, verified the callback and caption lookup with Ghidra, implemented the
fixes, replayed the actual captured bytes, and deployed the release. Native
evidence and remaining capture work are in the
[submenu audit](submenu-repair-0319-native-audit.md).

## Verification

- Release tests: **1,394 passed**, zero failed or skipped: Core 134, Native 682,
  Mod 570, Prism 8. Regression coverage includes actual captured-memory replays,
  category changes, an item arriving in an empty category, and detached cards.
- All **141 hook signatures** match the supported installed executable.
- Release package rebuilt after the code commit: zero warnings or errors.
- Deployment: **25 passed**, zero failed. Existing launch redirect and x86
  .NET 9.0.17 runtime verified.
- Every installed file matched its source: 28 mod files, 45 loader/shared-hook
  files, and two native launcher/installer executables. Game executable unchanged.

Code commit: `b148e8d3f67069c599fba10329c8df135edd241d`.
The packaged assembly's product version includes this exact commit.

Installed mod:
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Reloaded-II\Mods\chrono.trigger.accessibility`.

Mod DLL SHA-256:
`471DDFB58CFFFEE4E9631CAFCEC4CAF34B376000291113C57070771F52802C87`.

Manifest SHA-256:
`F6C89A41EE22DA21AC1C6F6121515BF7E2961484BE6D75114800648C1CBAE9CB`.

Previous installation retained at
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260914-115016`.

TRX results, hook proof, deployment output, and per-file hash proof are retained
in the active worktree's `artifacts/research/release-0319/` directory.

## Live verification still required

The game closed after the Equipment character-card capture and before deployment.
This release has not yet been heard in the running game. Launch normally and
browse Inventory's categories. In Equipment, select Crono with X to test the
deeper equipment slots and item choices. Tech and Party also need player
verification; this release does not establish that every submenu is accessible.
Save-slot speech was confirmed working by the player in 0.3.18.
