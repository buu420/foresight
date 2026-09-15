# Version 0.3.22: empty battle items and field item targets

Opening an empty battle Item list now announces its empty state and supports K to repeat it. The
field Inventory item-use screen reads the selected character's rendered card, including HP/MP,
instead of failing on the blank selection control. Whole-party targets read the populated cards.
After an item is used, the reader follows the rebuilt sheet and reports changed displayed stats,
including when the last item has been consumed.

The player's September 14 log establishes two separate issues. Battle Item had zero entries;
Cyclone, its MP cost, its description, and its targets were already announced. A Potion obtained
after battle was then used in Inventory: Crono's logged HP rose from 13 to 63, but the character
picker generated "Unable to read the current selection."

## Implementation evidence

[Battle capture evidence](battle-lists-0322-fix.md) includes a minimal replay of the actual empty
list, Cyclone, and the three highlighted targets. The list repair retains the existing panel,
acting-member, phase and ownership checks; it also verifies that all six rendered cells are blank.

For field items, hash-verified Ghidra output and disassembly establish the following relationships:

- RVA `1C7710` constructs the target screen, stores its root at `node+308` and its sheet at
  `node+310`, and pushes the manager at `node+314` onto the existing stack. The prior stack reader
  already found that manager; the missing part was connecting its blank controls to the cards.
- RVAs `23C050` and `23C270` create transparent individual/whole-party controls with finger sprites.
  `23CD80` wraps them in ordinary FocusableState objects and assigns sequential keys from zero.
- RVA `23BC40` creates exactly three sheet card slots. The populated portrait vector at
  `node+318/+31C` determines how many are actual party members. Their card and control orders agree.
- RVA `1C7D10` mirrors the target key to `node+324`; the reader cross-checks it with the manager.
- RVA `1C82B0` replaces the sheet after use. The reader resolves it afresh each capture and reads
  rendered text without requiring an item row to retain positive quantity.

The target reader validates visible ancestry, card ownership, manager/control mappings, bounds and
stable relationships before publishing speech. Existing manager ownership and callback hooks are
unchanged. Game controls, item effects, saves, navigation and footsteps are unchanged.

Claude owned the native audits and Ghidra extraction; Codex checked the relevant native code,
implemented the readers, and tested them. The bridge timed out returning one audit; the ongoing
local Claude session and its saved evidence were recovered without another authorization request.

## Verification

- Release suite: **1,431 passed**, zero failures/skips: Core 134, Native 718, Mod 571, Prism 8.
- Empty battle replay and session speech cases failed before the repair and pass afterward.
- Field item cases failed before the repair. Synthetic native-layout tests cover the separate
  card/control relationship, one/two members, whole-party selection, post-use sheet replacement,
  zero remaining quantity, stale focus, hidden/detached cards and mutation during capture.
- All **141 hook signatures** match the supported executable.

Field target behavior has native-code and synthetic-test coverage; no live target capture was
available because the player had used the last Potion. A normal item-use check after relaunch is
still needed when another item is obtained. Battle replay is also distinct from testing the new
DLL in a running game.

Build, test and deployment evidence is retained under `artifacts/research/release-0322/`.
