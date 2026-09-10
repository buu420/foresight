# Name Entry four-control correlation

Date: 2026-09-10. Exact Steam executable SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

The user reached Start Game on 0.2.2 and reported:

> Name Entry main-action correlation failed: Expected one manager with exact distinct keys 0 through 2; found 0.

The same error appears at 10:09:10 in the Reloaded log
`2026-09-10 15.08.31 ~ Chrono Trigger.txt`. Setup values, help and Start Game
were correctly spoken earlier in that run. The game exited at 10:09:23.

## Native evidence

Ghidra 12.1.2 headless, read-only analysis of the exact executable:
`C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro\ReviewNameBindingsCodex.java`
and `name-bindings-codex.txt`. Claude independently checked the constructor
count, sole binding loop and initial focus in `name-controls-claude.txt`.

NameGridBuilder, RVA `0x2C08D0`, creates two localized buttons in a two-iteration
loop, a transparent name-field control and a separate image button. There is one
main manager, stored at scene offset `0x2A8`.

| Manager key | Control | Evidence for accessible name |
| --- | --- | --- |
| 0 | Defaults | Localized text bank `0x41`, message `0x35` |
| 1 | Accept | Localized text bank `0x41`, message `0x08` |
| 2 | Name text field | Transparent overlay associated with keyboard name entry |
| 3 | Character grid | Image button using `Extension/softkeyicon.png`; opens the character grid |

The final two names describe the native controls' functions. They are not
localized strings recovered from an image, nor a synthetic replacement menu.

BuildControlStates, RVA `0x23CD80`, assigns sequential keys to the two-button
vector starting at the builder's start argument 0. The extra controls are then
inserted explicitly:

- RVA `0x2C12E2` writes key 2; the state stores the transparent control at `+0x14`.
  Insertion at `0x2C130D` receives the key and state pair.
- RVA `0x2C13F1` writes key 3; its state stores `piStack_160`, the image button,
  at `+0x14`. Insertion occurs at `0x2C141C`.
- RVA `0x2C1528` calls NsMenuControlBinder once per node with the stored key and
  FocusableState. The state vtable is RVA `0x3AC3F4`.
- RVA `0x2C1543` calls NsMenuFocusSetter with key 2. The grid starts inactive,
  with page -1, as already established in the 0.2.2 label audit.

Thus the strict three-key filter rejected the actual four-key manager and
reported zero matching managers. The manager was present.

## Keyboard entry

The keyboard activation body at RVA `0x2C1B50` disables two vector elements at
capture `+8` and a separate control at capture `+0x14`. The close body at
`0x2C1BA0` re-enables the two elements at capture `+4` and a separate control at
capture `+0x10`. Both loops advance by four bytes and stop after eight bytes.

That set is Defaults (key 0), Accept (key 1), and the image button (key 3).
The name field at key 2 retains focus. The implementation now requires all four
distinct manager keys, validates this exact three-button enable/disable set,
and restores speech for the name field as item 3 of 4 when keyboard entry closes.
It still requires exactly two vector elements, rather than accepting a broader
or ambiguous native layout.

## Regression coverage

The new constructor regression reproduced the user's exact error before the
change. It now publishes the current name, initial name-field focus, and the
fourth control's focus using four real-style binder observations. Existing
keyboard open/type/close and confirmation tests use the corrected native set.
Negative tests reject substituting the name field for the separate grid button,
extra manager bindings, and incorrect narrated control counts.

No hook addresses or byte signatures changed. All 113 native contracts still
match the installed executable. The 0.2.2 shared text-capture and deferred
grid-label fixes remain intact. Player testing of 0.2.3 through the naming screen
and intro remains required.
