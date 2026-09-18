# Equipment and Inventory modes, 0.3.27

Equipment slot selection now names the gear actually worn. Replacement selection explicitly
says "Preview", names the currently equipped item, and introduces comparison numbers with
"If equipped". The user's captured Helm selection now reads Bronze Helm instead of the
passive Hide Cap candidate. Confirming a piece of gear in Inventory opens a static information
panel; the reader now announces that panel and its close instructions. Empty Inventory slots
remain readable when rearranging items. Animated footer prefixes no longer cause repeated
partial announcements.

## Native evidence and corrections

Supported executable SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Addresses below are RVAs. Ghidra addresses use preferred base `400000`; September 18 captures
in this repair use live base `330000`. Page class is checked before interpreting any fields.

The 0.3.26 candidate-manager enabled flag is insufficient to determine Equipment's phase.
Slot hover calls `2088A0` and constructs a candidate list while the equipped-slot selector
still owns input. `CharaEquipManager+2C8` is the actual phase: zero for equipped-slot selection,
one for replacement selection. `2085E0` sets one on confirm; `208E80` and `209070` clear it on
cancel or completion. Therefore returning to phase zero does not by itself establish an equip
transaction. No success announcement is inferred from that transition.

The slot and candidate lists remain at child `+308` and `+30C`. Equipped item labels are resolved
through the slot list's own input manager and its exact key; slot captions are loaded bank
`23` messages `3C..3F`. Candidate selection retains the record/control/cursor correlation from
0.3.26. Current stats are read only when the displayed numbers agree with their native equipped
baselines, preventing an unfinished preview from being called current stats. Empty equipment
slots read "None" only when their blank rendered label also agrees with an empty native item
index. `20A720` stores the equipped ID at child `+328 + slot*12` and updates the corresponding
name Label at `+32C + slot*12`; the low twelve bits select the item name within the slot's group.

The old assumption that a StatusBar Label contains its complete text was incorrect. `22F7B0`
reveals one character every 1/30 second. The UTF-8 vector at `+2E0` and Label `+2A0` contain
that partial display. `22F160` stores the complete UTF-16 line vector at `+2D4/+2D8`, using
24-byte MSVC strings. The reader now uses that bounded, owned vector and checks the source
and ownership twice. Equipment owns its bar at child `+2F0`; Inventory owns its bar at page
`+2CC`. Description animation no longer changes the semantic announcement.

Inventory `1C66B0` dispatches equipment groups zero through three to `1C6740`. This creates the
information panel at `page+300` and its manager at `+304`, then disables the main list. The
manager has one generic `nsStateMachine::State` (vtable `3ABC2C`), key zero, with no focused
character control. The panel contains static usable-by labels and item attributes. Its callback
is **`1C8D60`**, reached through `_Func_impl` vtable `3A37B0`, slot two:

```c
if (event == 0 || event == 2) {
    play_close_sound();
    close_item_information(); // 1C7560
}
```

Confirm and Cancel both close the panel. Movement does nothing; there is no target chooser
or equipment transaction here. Earlier exploratory notes calling this an "equip target" or
interpreting the row cursor as a popup choice are superseded by this callback evidence.
Consumable target selection remains the distinct, existing `+308/+310/+314/+324` path.

The new reader requires the information manager to be the active top manager, generic state
zero, one map entry, and a visible panel owned by the Inventory page. It correlates the held
item with the row cursor and bounded native row vector, reads the panel's visible labels,
and checks ownership, selected record and panel text again before publishing.

`1C3080` preserves zeroed holes when rebuilding the Inventory vector, and `1C63D0` swaps
12-byte records without excluding holes. An ID-zero, quantity-zero row is a usable empty
slot, even when other items exist in the category. It now reads with its position and held-item
context. A nonzero ID with zero quantity still fails the native item consistency check.

## Captures and verification

Read-only captures:

- `live-20260918-132711.json`: Equipment, Helm selected, phase zero, Bronze Helm equipped,
  passive Hide Cap replacement, current Defense 22, item Defense 8.
- `live-20260918-132139.json`: Inventory information, Hide Cap quantity two, usable by Crono,
  Marle and Lucca, item Defense 3, description "A lightweight leather cap."

The committed `menu-modes-native-0327.json` contains only bytes consumed by the reader or needed
for regression mutations. The earlier Bronze Blade fixture is retained and extended from its
original capture for phase, ownership and slot-caption reads. Its missing full-description
source is explicitly modeled in test setup; it is not represented as a live captured heap.

Regression cases cover both native captures, the passive candidate list, replacement wording,
cancel back to the equipped slot, full-description updates, animated prefixes, active-manager
identity, hidden/disabled/detached modes, changes during capture, and blank placement slots.
Mutation cases validate the audited layout; they do not claim additional in-game transactions.
Existing focus/update hooks are reused. No gameplay input or process-memory writes were used.

Claude supplied Ghidra extractions and a source review. Codex checked the raw evidence, corrected
interpretation errors, implemented the changes and replayed the captures. Research artifacts
are under `artifacts/research/menu-modes-0327/`. The original
[Chrono Trigger manual](https://bdjogos.com.br/manuais/210-chrono-trigger-super-nintendo-manual-usa.pdf)
was consulted as background; the PC executable and captured state determine these controls.

Release validation: **1,829 tests passed**, zero failures or skips: Core 145, Native 820,
Mod 856 (including three separately run footstep timing tests), Prism 8. All **151 native
hook signatures** match the supported executable. The new Inventory-information replay
and the blank-slot regressions failed before their repairs and passed afterward. The passive
candidate and full-description regressions likewise exposed their corresponding defects.

These results verify captured state and audited transitions. Hearing the installed DLL and
performing an actual equip/cancel cycle still require a normal game launch after installation.

Review resolution: the empty-equipment-slot finding was addressed using the native ID and two
regressions. Candidate slot `+304` remains correct: `2088A0` writes it from its slot argument
before building the candidates, and the commit callback consumes it. A proposed fallback to the
disabled Inventory list was rejected because it would describe an inactive control when the
information panel was still open. Its stable native mode is now readable directly.
