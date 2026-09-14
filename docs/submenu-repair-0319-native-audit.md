# Submenu repair investigation, 2026-09-14

Status: fixes implemented for the captured Inventory category/empty-list failure and Equipment character-card failure. Both saved live frames now replay successfully through the production reader. In-game speech after deployment still needs player verification. Save-slot speech in 0.3.18 is confirmed working by the user.

Repository: `.worktrees/navigation-20260910`, based on `6ad524c`.
Executable SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
All addresses below are RVAs unless otherwise stated. Static research used Ghidra and Capstone; ASLR must be resolved from the actual running process before any live read.

## Observed failure

The Reloaded log `2026-09-14 15.55.04 ~ Chrono Trigger.txt` loaded the game at image base `0x00290000`.

- Save successfully spoke the occupied first slot and empty slots.
- Equipment at node `0x277233B8` never produced a successful selection capture.
- Inventory at node `0x2772D6A8` spoke `Use / Switch`, then repeatedly returned no snapshot when entering its item list.
- Tech at node `0x2144C770` and Party at `0x27561008` also returned no snapshot.
- These are capture failures, not fatal accessibility exceptions. The existing log does not identify the particular failed gate.

The first static pass ran with the game closed. The player subsequently opened Inventory and Equipment, allowing the read-only captures below.

## Live root causes and fixes

- Inventory capture `live-20260914-112737.json`, loaded base `0xD00000`, node `0x274A5898`: the native focus is category icon key 2. Its control has sprites and no text. The separate category Label reads Consumables. The list contains one blank padded record with quantity zero. The old generic reader therefore returned null at its empty-label check; the old row reader also refused the placeholder's zero quantity.
- Equipment capture `live-20260914-112927.json`, same loaded base, node `0x1DE275C8`: the active manager focuses Crono's one character card. Its ownership and vtable are correct, but the card contains 18 Label fragments. The old limit of 12 rejected it. The new limit bounds 64 fragments within the selected control; whole MenuListView containers are still rejected.
- Inventory icon keys 2 through 7 now read the six native category names. Emptiness is announced only for the observed one-record, zero-quantity layout with a matching visible category heading. A category being focused cannot borrow another category's empty state.
- Character-card fragments separated by the game's standalone colon Labels now form clauses, producing `Crono LV 1. HP 43/70. MP 8/8. EXP 10. Next 10` from the captured card. All native labels and values are retained.
- Item-row selection now requires the native relationship `focus key = cursor + 8` and the page's own item manager. A partially updated cursor is retried.

The minimal bytes required to replay the two actual frames are embedded in `tests/ChronoTriggerAccessibility.Native.Tests/Capture/field-submenu-native-0319.json`. Separate mutation tests cover category changes, item arrival, and removal of the character card from its menu.

## Verified Inventory relationships

- `node + 0x2DC` owns its input manager. `node + 0x2E0/0x2E4` is the vector of control pointers used to construct its focus map.
- The map builder starts at `0x1C5E20`, not `0x1C5E30` (which is an instruction inside the prologue). It wraps every control in a `FocusableState` (`vtable 0x3AC3F4`, control at `+0x14`) and uses the control's vector index as its key.
- The callback at `0x1C5D79` distinguishes header keys 0 through 7 from item keys 8 and above. For an item it passes `key - 8` to the cursor updater `0x1C6620`.
- `0x1C6620` stores the selected row at `node + 0x2F8`; when clamping, it sets manager focus to `row + 8`. The keyboard path at `0x1C52DB` also sets focus to `row + 8` before updating the row cursor.
- Item records are at `node + 0x2D0/0x2D4`, stride 12. The renderer `0x1C5300` reads the encoded item ID at record offset 0 and quantity at offset 4. The selected help updater receives that same encoded ID.
- The renderer looks up the name using bank `0x1B` through `0x0B7D70`, with the group-base table at `0x39906C` and low 12-bit item index. It updates the row's child labels tagged 2000 and 2001.
- Row controls are added to the scroll view at `node + 0x2C8` at `0x1C5041` through `0x1C5050`, then appended to the control vector. Thus native ownership must be checked, not replaced with a guessed detached-control fallback.
- `0x1C1B60` creates a held-row overlay sprite; it is not merely a row accessor. The actual scroll-to-row helper is `0x1C5890`.

The prior test fixture reversed the native key ranges (item key 3, category key 9). The fixture now uses item row 1 / key 9 and category key 3; the row-changing examples update both focus and cursor. These are synthetic fixture corrections, not evidence that the user's silence is fixed.

## Formation map construction

A proposed explanation involving `TransBranch` as a focus-map value was rejected after verification. The Formation builder `0x1BFBB0` finds an existing map entry at `0x1BFF91`, loads its existing state at `0x1C0034`, then appends the new transition to that state's vector at offset 4 (`0x1C0038` / `0x1C003B`). Inventory does the same at `0x1C6018` through `0x1C6021`. Do not broaden the accepted state vtables on this evidence.

`0x193040` allocates an empty map and calls its constructor `0x193120`; it is not a FocusableState emplace helper. Formation subsequently populates the map via `0x23CD80` at `0x1BFDD3`, passing vectors at node offsets `0x2E4` and `0x2F0`. That shared helper allocates states starting at `0x23CE5E`. Absence of a literal vtable store within a page builder does not mean the page has no focus map.

The Equipment character card is now proven by the live snapshot above. Its deeper slot/item selection and the other submenu screens still require live coverage. No dedicated reader was implemented from speculative map-layout changes.

## Next capture

A read-only helper is prepared at `artifacts/research/submenus-0319/capture_menu.py`:

```powershell
py -3 artifacts\research\submenus-0319\capture_menu.py
```

It verifies the executable hash, resolves the actual loaded image base, requires the matching current log, validates the logged submenu class, and uses only process query/read access. It sends no input and writes no game memory. It saves the page fields, manager stacks, focus-map entries, visible-state markers, menu-owned text tree, Inventory row records, loaded text banks, and raw read segments to a timestamped JSON file beside the script. Selected control trees and their parent chains are captured separately because ScrollView containers are not all traversable through the base Node child vector.

Use that snapshot to identify the exact rejecting gate: manager selection/disabled state, map resolution, control ownership/visibility, Inventory row data/text, rendered-label availability, or coherence. Follow with a regression fixture from the observed layout, implement the supported fix, test, then package/deploy with the game closed. Preserve working Save, battle, navigation, and footsteps.
