# Name field and Accept confirmation audit

The player's 0.2.3 run in `2026-09-10 15.42.30 ~ Chrono Trigger.txt` spoke
"Current name: empty" on entry, then "A name is required" on Accept at 10:43:09.
The game proceeded to build a confirmation, where capture reported a second
CustomButton before a localized label had been associated with the first.

The executable SHA-256 is
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
The installed `libcocos2d.dll` SHA-256 is
`8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F`.
All addresses below are RVAs or object-relative offsets, not live addresses.

## Current name

`NameInputScene+0x350` is a saved name associated with opening the character
grid. Construction initializes it to empty. The key-3 grid-opening path copies
the current name there at RVA `0x2C1A44`. Glyph append and delete read and update
the separate NameEdit control, leaving that saved copy stale. The old snapshot
therefore misreported both initial and edited names.

The delayed Accept body at `0x2C1A90` reads NameEdit's current text through
`0x2BF880`. Only a nonempty result reaches confirmation-builder call
`0x2C1B14`. The observed confirmation after a false empty-name announcement is
consistent with the two different sources.

Version 0.2.4 follows the same current-text getter using bounded memory reads:

1. Validate the existing glyph/delete/refresh function-object invocation thunks.
   Glyph and delete bodies receive implementation+4; their first closure field
   must point to the same non-null NameEdit.
2. Validate NameEdit's vtable against executable+`0x3B748C`, installed by its
   constructor at `0x2BFCBD`. Its `+0x27C` field holds the UICCTextField created
   at `0x2BFA71` and stored at `0x2BFA7D`.
3. Resolve the loaded Cocos base from the executable's UICCTextField::create IAT
   slot `0x3857D8`, subtracting that DLL export's RVA `0x285CA1`.
4. Validate UICCTextField's Node vtable at Cocos+`0x4B94B8`, its LabelProtocol
   subobject at field+`0x278`, and that protocol's vtable at Cocos+`0x4B97CC`.
5. Validate protocol slot +8 against TextFieldTTF::getString at Cocos+`0x2D6762`.
   Its exact bytes are `8D 81 0C 03 00 00 C3`: `LEA EAX,[ECX+0x30C]; RET`.
   Thus the current MSVC string is at UICCTextField+`0x584`.

The capture performs no native getter calls or native string allocation. It
retains strict UTF-8, NUL-termination, capacity, and five-UTF-16-unit checks.
Invalid ownership or layout fails explicitly; the saved scene string is never
used as a substitute. Cocos's [TextFieldTTF reference](https://docs.cocos2d-x.org/api-ref/cplusplus/v3x/dc/d95/classcocos2d_1_1_text_field_t_t_f.html)
also documents getString as the input-text accessor; the installed binary,
rather than the public documentation, establishes these offsets.

## Confirmation choices

Codex and Claude independently inspected the exact native builder at
`0x2C2F00`. The proposed name remains a six-word, by-value MSVC string in a
thiscall function; the existing delegate ABI is correct (`RET 0x18`).

The prompt resolver call is at `0x2C309A`, using `(0x23,0xDA)`. The name-character
label loop calls MenuTextLabelFactory at `0x2C3235` before either choice control.
The choice loop starts its counter at zero and runs exactly twice:

| Step | RVA | Evidence |
| --- | --- | --- |
| Construct choice CustomButton | `0x2C34A5` | Returns the same non-null storage pointer |
| Resolve choice text | `0x2C36DA` | TextManager::getMsg, bank `0x41`, id `0x11 + counter` |
| Render that text | `0x2C36F5` | MenuTextLabelFactory, returned text in EDX, font size 12 |
| Next iteration | `0x2C3738` | Exactly two iterations |
| Bind controls | `0x2C37F8` | Manager keys 0 and 1 |
| Initial focus | `0x2C3810` | Key 1 |

The constructor, initialization, refresh, name-character creation, and manager
creation audits found no nested CustomButton construction. The shared C# text
fanout and capture-scope review did not identify why the first choice's lookup
notification was absent in the live run. The old diagnostic alone cannot
distinguish missing delivery from an unrelated construction entering the scope.

The correction captures the text that is actually passed to the existing shared
MenuTextLabelFactory hook. Name-character labels before the first constructor
are excluded. Each subsequent rendered label must follow a pending constructor,
produce a non-null node with the audited font size, and contain readable,
nonblank text. If a localized lookup was observed, it must agree with the
rendered text. Finalization still requires exactly two distinct controls,
exact manager keys 0/1, matching control-pointer sets, and one authoritative
focus observation. Missing or duplicate rendered labels remain errors.

This removes a dependency on the missed lookup notification while retaining
control correlation and the game's displayed localization. If another
construction still interrupts capture, the error now includes both pointers,
counts, and observed text keys for the next live investigation.

## Evidence and limits

Research artifacts are under
`C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro`:

- `displayed-name-codex.txt`, `name-edit-layout-codex.txt`, `cocos-name-exports.json`
- `confirm-delivery-codex.txt`, `confirm-controls-codex.txt`
- Claude's `confirm-builder-review-claude.md`, `confirm-delivery-review-claude.md`,
  `confirm-order-claude.txt`, and `nested-ctor-claude.txt`

Ghidra 12.1.2 headless was used read-only against the exact executable. The
regressions cover a saved empty/stale name, current empty text, incorrect
ownership/layout/getter, absent choice lookup notifications through the real
shared label fanout, and invalid or conflicting rendered choice labels.
Player verification of Accept and the opening remains outstanding. Broader
intro and scene-lifetime boundaries remain in the [intro audit](2026-09-09-new-game-intro.md).
