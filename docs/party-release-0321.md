# Version 0.3.21: Party menu speech

The Party menu was silent because it uses icon-only controls, places character details on separate
cards, and can open on a native state without a focused control when the intended member is locked.
Version 0.3.21 follows those native relationships. It announces the current/reserve roster when no
member is selectable; with selection available, it reads the selected card, side and position, lock
status, picked-up/moving indicator, and displayed combo names. Character names, levels and HP/MP
come from the rendered cards. Empty reserve and combo lists are identified explicitly.

The captured screen contains one locked Crono, level 1, HP 43/70 and MP 8/8. Its replay now reads:

> Please select party members. Current party. Crono LV 1. HP 43/70. MP 8/8. Locked.
> Reserve. Empty. Usable Combos. None.

The [native audit](party-0321-native-audit.md) records the Ghidra, disassembly, live-read and Claude
review evidence. A separate capture path handles Party; existing Inventory, Equipment, Save and
Tech behavior is covered by the unchanged regression suite. No new game input or memory writes
were introduced.

## Verification

- The live locked-roster and selected-card regression cases failed before implementation and pass
  afterward. The selected-card case explicitly mutates the captured memory to unlock/focus Crono.
- Release suite: **1,409 passed**, zero failed/skipped: Core 134, Native 697, Mod 570, Prism 8.
- Synthetic tests cover reserve key/card mapping, moving/picked-up speech, hidden/detached content,
  rebuilt rosters, locked parking-state validation, optional combo preview, rendered combo names,
  icon-only combo groups and unreadable labels.
- All **141 hook signatures** match the supported installed executable.
- The final package built with **zero warnings and errors**.
- The 206-segment, 8 KB embedded fixture retains only successful memory reads used by the capture.
  Non-Label nodes can intentionally fail optional Label-protocol probes; those are not required
  reads. No missing name, roster, card, manager or message-bank bytes were substituted.

Package commit: `cc18755fc7f29dbaf897b155257d87abd1549528`.
The packaged assembly's product version contains this exact commit.

Mod DLL SHA-256:
`02BB9E8275FA8F9E76DA37483B5034A00BDBB3A02D41ECB395E4BE1227111C92`.

Manifest SHA-256:
`DD35D3B1BBECFFE36178D17B3A4A50ED4EF69A0E12E184721204E6B60E0B170A`.

## Deployment and player check

Installed September 14, 2026 after the player closed the game. All **25 deployment checks passed**,
including the existing launch redirect and x86 .NET 9.0.17 runtime. Every installed file matched its
source: **28 mod files, 45 loader/shared-hook files, and two native launcher/installer executables**.
The installed assembly's product version and DLL/manifest hashes match the tested package above;
the game executable is unchanged.

Installed mod:
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Reloaded-II\Mods\chrono.trigger.accessibility`.

Previous installation retained at:
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260914-180427`.

Relaunch normally and reopen Party to check actual speech. The live evidence in this release covers
only the locked single-member screen. Multi-member selection, reserve members, swaps, and populated
combo lists are supported by native-code research and synthetic tests, not live player verification.

Build output, TRX results, hook proof and installation evidence are retained under the worktree's
`artifacts/research/release-0321/` directory.
