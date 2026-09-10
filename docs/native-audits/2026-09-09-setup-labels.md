# New Game setup values and the Equipment placeholder

The player's version 0.2.1 test in Reloaded log `2026-09-10 03.26.38 ~ Chrono Trigger.txt` reached setup successfully. It read descriptions as values, such as `Battle Mode: Time flows constantly while in battle.. ACTIVE`, and announced `Start Game, 4 of 4. Equipment`. Start activated at local 22:31:34; a separate missing Name Entry label error followed at 22:31:36. The player exited normally at 22:31:50. No game input or Computer Use was performed by the agents.

## Exact native evidence

Codex imported the supported executable read-only into Ghidra 12.1.2, checked SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`, and decompiled/disassembled the setup initializer, control builder, and help update. Research artifacts are `ReviewModeLabelsCodex.java`, `mode-labels-codex.txt`, and `mode-labels-ghidra-console.log` under `C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro`.

The `0x88`-byte setting records have their displayed alternatives at `+0x24` and their help strings at `+0x18`. The control builder at RVA `0x2AA1E0` takes the displayed strings from `+0x24`. The help update at RVA `0x2ABF20` reads `+0x18` using the same native selected-value getter. The previous localized contracts assigned these roles backwards.

| Row | Label | Displayed alternatives, indices 0 and 1 | Corresponding help |
| --- | --- | --- | --- |
| Battle Mode | `(23,5A)` | `(3F,05)`, `(3F,06)` | `(23,C0)`, `(23,C1)` |
| Graphics | `(3F,31)` | `(3F,32)`, `(3F,33)` | `(23,C7)`, `(23,C6)` |
| Interface | `(42,1A)` | `(42,1C)`, `(42,1D)` | `(41,55)`, `(41,54)` |

The control builder obtains Start Game through TextManager at call site RVA `0x2AB6E6`, key `(23,D7)`. This is the eleventh wrapped lookup across initialization and control construction, in addition to the ten wrapped row lookups audited previously.

At RVA `0x2AB812`, the control builder obtains `(23,20)` through Ope. This is the initial text used to construct the help widget stored at scene `+0x2AC`. Before returning, at RVA `0x2AB8F7`, it calls `0x2ABF20` to replace the placeholder with the selected row's help. For Start focus 30, that function's out-of-row branch writes an empty string. Equipment is consequently not visible help to preserve or narrate on Start.

Read-only extraction of the installed localized resources independently confirms the English text: `menu.txt` index `0x20` is Equipment, `0xC0/0xC1` are battle descriptions, and `0xC7/0xC6` are graphics descriptions. The relevant `small.txt`, `system.txt`, and `start.txt` entries provide the displayed alternatives and interface descriptions. The archive extraction was cross-checked against [CTViewer's resource reader](https://github.com/GitExl/CTViewer/blob/main/src/filesystem/resourcesbin.rs); the exact installed data and native call sites are authoritative. Game assets are unchanged and are not included in the mod repository.

## Correction and regression coverage

The localized contracts now assign the two displayed alternatives and matching help to their actual roles. The placeholder key and the invented static lower-help field are removed from setup capture and semantic events. Start focus announces only its native label and position. Native selected-value getters, focus decoding, both alternatives, and strict capture validation remain in place.

The new regression feeds the real key-to-text mapping through the combined Startup/New Game hook composition, independently of the production contract arrays. It reproduces all four erroneous announcements before the fix. Afterwards it requires ACTIVE with its battle description, Original with its matching graphics description, Gamepad/Keyboard with its matching interface description, and `Start Game, 4 of 4` with no placeholder. Existing tests additionally exercise native value changes and wrapped text capture, now including the actual TextManager routing of Start Game.

Pre-fix evidence: `test-results/setup-labels-red_net9.0_20260909223813.trx`, four expected failures. The corrected focused run passed 104 tests: Core New Game 10, native Mode Select 11, Mod New Game 83. The Name Entry lifecycle and callback corrections are documented in `2026-09-09-name-entry-labels.md` and included in version 0.2.2.
