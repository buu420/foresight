# Name Entry localized labels — native audit

**Date:** 2026-09-09
**Executable:** `Chrono Trigger.exe`, SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`
**Tooling:** Ghidra 12.1.2 headless, `--noanalysis --readOnly` import
**Artifacts:** `C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro\`
`name-refresh-codex.txt` (decompiled refresh callback), `asset-system.txt` (decoded
English system asset), `VerifyRefreshLabelClaude.java` → `verify-refresh-claude.txt`
(independent byte/disassembly verification), `ReviewNameIdTableClaude.java` →
`name-id-table-claude.txt` (button id table).

## Why

Live run `2026-09-10 03.26.38 ~ Chrono Trigger.txt`, 22:31:36:

```
ERROR: Name Entry is missing a localized grid action, Defaults, or Accept label.
```

`NewGameHookSet.FinalizeNameEntry` requires three localized keys before it will
publish anything for the screen.

## The three keys are correct. The lifecycle is wrong.

### Grid action — `(0x42, 0x08)`, from the grid refresh callback

The label is requested by the **grid refresh callback at RVA `0x2C2A20`** — the
function `NameInputCapture.RefreshBodyRva` already names. It is reached only
indirectly, through the `std::function` stored at `scene + 0x34C`
(`RefreshTargetOffset`), invoked as `(**(code **)(*impl + 8))()`.

```c
void __fastcall RefreshBody(undefined4 *param_1)   // ECX = closure, no stack args
  do {                                   // iVar10 = row, 0..7
    do {                                 // iVar9  = column, 0..10
      piVar4 = getChildByTag(*param_1, iVar10 * 0xb + 100 + iVar9);
      if (piVar4 != NULL) {
        if ((iVar10 == 7) && (iVar9 == 10)) {
          uVar5 = TextManagerGetMsg(auStack_44, 0x42, 8);   // <-- the action cell
          (**(code **)(piVar4[0x9e] + 4))(uVar5);           // setString
        } else {
          pcVar2 = *(char **)(&UNK_0079B708 +
              ((iVar10 + *(int *)(param_1[1] + 0x298) * 8) * 0xb + iVar9) * 4);
          ...
        }
      }
    } while (iVar9 < 0xb);
  } while (iVar10 < 8);
```

Independently verified in the exact executable:

```
BYTES at RVA 0x2C2A8A       = 6A 08 6A 42 50 E8 7C 66
  RVA 0x2c2a7c  CMP ESI,0xa
  RVA 0x2c2a7f  JNZ 0x006c2acd
  RVA 0x2c2a81  MOV ECX,dword ptr [0x0081c3d8]     ; TextManager singleton
  RVA 0x2c2a8a  PUSH 0x8                           ; message id
  RVA 0x2c2a8c  PUSH 0x42                          ; bank
  RVA 0x2c2a8e  PUSH EAX
  RVA 0x2c2a8f  CALL 0x005b9110                    ; TextManager::getMsg, RVA 0x1B9110
```

Two offsets corroborate the closure layout against the existing contract:
`param_1[1] + 0x298` is `scene + NameInputCapture.PageOffset`, and `&UNK_0079B708`
is `imageBase + NameInputCapture.GridPointerTableRva`. So the closure is
`{ [0] = grid node, [1] = NameInputScene }`.

The decoded English system asset gives bank 0x42 index 8 (0-based, counting the
seven `MSG_FONT_*` entries first) = **"Accept"**.

### Defaults `(0x41, 0x35)` and Accept `(0x41, 0x08)` — from the button loop

`NameGridBuilder` (RVA `0x2C08D0`) computes each button's message id as
`*(uint32*)(&UNK_007B715C + (cursor - controlArrayBase))` and requests it from
bank 0x41 at RVA `0x2C0CDF`. The table at **RVA `0x3B715C`** holds exactly two
entries before the `NameInputScene` vtable begins at RVA `0x3B7168`:

```
+0x0 (RVA 0x3B715C) = 0x35
+0x4 (RVA 0x3B7160) = 0x08
```

### Confirmation keys — unaffected

`NameConfirmationBuilder` (RVA `0x2C2F00`) does
`func_0x005b9060(buf, 0x23, 0xda)` (Ope) for the prompt and
`func_0x005b9110(buf, 0x41, iStack_b4 + 0x11)` (getMsg) in a two-iteration loop for
the choices, so `ConfirmationPromptTextKey = (0x23,0xDA)` and
`ConfirmationChoiceTextKeys = { (0x41,0x11), (0x41,0x12) }` are proven correct.

## Root cause

`NameInputScene::init` (RVA `0x2C0090`) requests five localized texts —
`(0x23,0x7E)`, `(0x41,0x39)`, `(0x41,0x3B)` in `NameInitPartB`, and
`(0x41,0x35)` + `(0x41,0x08)` in the button loop. It does **not** request
`(0x42,0x08)`, because that arrives from the refresh callback, which runs later.

The grid also starts **inactive on page -1**, so the first refresh has not
necessarily happened when the mod's Name Entry capture scope closes. Requiring all
three keys inside the init scope therefore fails the entire screen.

A walk of the direct call graph from init does not include this indirect refresh. The key must be audited at the callback that draws the cell.

## Change made in `NameInputCapture`

`TryCreateSnapshot` now takes `string? localizedActionLabel` and fails closed only
when the focused cell's kind is `NameGridCellKind.LocalizedAction` — grid row 7,
every page — and no label is available. Every other cell, the name itself, the
inactive grid and the initial page -1 state capture normally. No key value changed.

## Native callback validation

The three `std::function` objects are validated through vtable slot `+8`. That slot contains an invocation thunk, not the final callback body. Requiring the body address would always reject a real Name Entry scene. Each invocation address occurs once as a data pointer in the supported executable; none of the body addresses occurs as a data pointer.

| Purpose | Invocation RVA | Vtable RVA | Body RVA |
| --- | --- | --- | --- |
| Append glyph | `0x2C5D90` | `0x3B6ED0` | `0x2C1BE0` |
| Delete glyph | `0x2C5D20` | `0x3B6F24` | `0x2C1D10` |
| Refresh grid | `0x2C5CE0` | `0x3B6F08` | `0x2C2A20` |

All three thunks add four to ECX. The append thunk also dereferences its stack argument before calling the body and returns with `ret 4`; delete and refresh tail-jump with no stack arguments. The snapshot now validates the actual invocation addresses while retaining the independently audited body constants.

Codex's `ReviewNameThunksCodex.java` / `name-thunks-codex.txt` and Claude's independent `VerifyNameThunksClaude.java` / `verify-name-thunks-claude.txt` plus `name-fn-vtables-claude.txt` prove these mappings. Claude also verified the name string fields and initial state: constructor RVA `0x2BFFF0` sets page `-1`; builder RVA `0x2C08D0` sets active byte `0` before init returns.

## Implemented lifecycle capture

`NewGameHookSet` hooks the refresh body at RVA `0x2C2A20`, using Microsoft thiscall: the closure is in ECX and there are no stack arguments. The 48-byte signature through `XOR EDI,EDI` is unique in the supported executable:

```
55 8B EC 6A FF 68 B0 61 77 00 64 A1 00 00 00 00
50 83 EC 40 A1 D0 A0 7F 00 33 C5 89 45 F0 53 56
57 50 8D 45 F4 64 A3 00 00 00 00 89 4D BC 33 FF
```

The body closure is `{ gridNode, scene }`. Before observing its text, the hook checks the scene against the active Name Entry scene or the scene currently being initialized. It also checks that the closure is exactly `*(scene + 0x34C) + 4` and that the implementation's vtable `+8` equals the audited refresh invocation thunk.

A dedicated refresh scope copies `(42,08)` from the shared TextManager observer while the native body runs. The Ope call nested inside TextManager is still ignored by the New Game observer, so one native lookup produces one copied string. After a successful refresh, the label replaces the active scene's cached grid label. A refresh inside Name init temporarily nests under that constructor's capture scope, restores the constructor scope in `finally`, and returns its label to the constructor. Unrelated scenes do not replace an active scene's label. Missing, unreadable, or genuinely duplicated refresh text still produces a coverage error; the native callback is preserved exactly once.

Name initialization requires its actual Defaults and Accept button labels, while the initially inactive grid can have no label yet. Main-screen updates, accepting the proposed name, and direct keyboard entry can capture that valid state. The existing runtime-state reset clears the copied grid label with the rest of Name Entry state.

## Regression and deployment evidence

`NameInitUsesNativeInactiveGridAndDeferredLabels` uses the actual initial page and literal native invocation addresses. Before the change, it reproduced the player's missing-label error (`name-native-init-red_net9.0_20260909225450.trx`). It now captures the name and focused main action, including the next update, with no grid lookup.

Additional production-composition tests cover opening the grid later, capturing Accept through nested TextManager/Ope calls, copying before a native string is reused, replacing the label on redraw, capture restoration after a refresh during init, foreign scenes, missing labels, duplicate labels, and mismatched closure ownership. Native tests require the label for the focused action cell on all three pages and preserve callback, name, pointer, page, row, column, and glyph validation.

`verify_v022_contracts.py` independently compares all 113 hook byte contracts with the unchanged installed executable and verifies the unique refresh signature and all three vtable mappings. Its output is `v022-native-contract-verification.json` in the research directory. Final Release and installed-payload results are recorded in the accompanying version 0.2.2 deployment report. Live New Game and intro validation remains with the player.
