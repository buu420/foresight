# Classic Inventory: the two character-target sub-screens

Read-only audit. Executable SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`; all addresses are RVAs against
image base `0x400000`.

Evidence, all hash-verified, in `artifacts/research/battle-lists-0322/`:

| File | Contents |
| --- | --- |
| `claude-targetscreen.txt` | `0x1C7710` (builder), `0x1C7D10`, `0x1C7EF0`, `0x1C8CE0`, `0x1C8CA0`, `0x1C8B70`, `0x1C8D60`, `0x1C7560`, `0x1C2CE0` (ctor), `0x1C2E20` |
| `claude-targetcards.txt` | `0x23C050`, `0x23C270`, `0x23BC40`, `0x23DA10`, `0x23D990`, `0x1C66B0`, `0x1C3010`, `0x1C7E30`, `0x1DD5F0`, `0x21890`, `0x1DD260` |
| `claude-equiptarget.txt` | `0x1C6740`, `0x23BED0`, `0x14830`, `0x1D2160`, `0x23A140` |
| `claude-postuse.txt` | `0x1C82B0`, `0x1C8550`, `0x1C8D30` |
| `claude-itemuse.txt`, `claude-usechain.txt` | `0x1C7F80`, `0x1C5B20`, `0x1C1C90`, the two trampolines |
| `claude-targetxrefs.txt` | Capstone: every reference to the node offsets in `0x1C2000-0x1C9400`, the lambda vtable table, the call graph, and the raw push/pop/trampoline bytes |

Log: `2026-09-14 23.53.35 ~ Chrono Trigger.txt`, lines 585-655, node `0x27320CA8`.

**This is an out-of-battle failure.** The separate empty battle Item list is documented in
[battle capture evidence](battle-lists-0322-fix.md).

Version 0.3.22 implements the consumable target-card correlation described here, including the
rebuilt sheet after use. The separate equip-on-whom chooser remains outside this repair. See
[implementation and verification](items-release-0322.md). Claude owned the native audit; Codex
verified the relevant decompilation/disassembly and checked the implementation against it.

**Two claims from the first revision of this document are withdrawn; see §7 and §8.** No new live
capture was possible (the save has no consumables left), so everything below is established from
executable layout. Sections are marked **Proven** or **Hypothesis** individually.

---

## 1. Proven: confirming a row opens one of two character-target screens

`0x1C5B20` case 0 (confirm) with key ≥ 8 is a two-press model:

```c
FUN_005dd3e0(param_2);                      // setFocus
iVar3 = *(int *)(*in_ECX + 0x32c);          // the held row
if (iVar3 < 0) {                            // FIRST press: pick the row up
  FUN_005c6620(param_2 - 8);                //   cursor = row
  *(node + 0x32c) = *(node + 0x2f8);        //   held = cursor
  *(node + 0x330) = FUN_005c1b60();         //   held-row overlay sprite
  return;
}
if (param_2 - 8 == iVar3) {                 // SECOND press on the SAME row: act on it
  FUN_005c6620(iVar3);
  FUN_005c66b0(*(node + 0x32c));
  return;
}
FUN_005c63d0(*(node + 0x32c), param_2 - 8); // a different row: swap the two, then clear held
```

`0x1C66B0(row)` is the dispatcher:

```c
record = [node + 0x2D0] + row * 0xC;
if (record[1] /* quantity */ > 0) {
  if ((uint)(record[0] >> 12) < 4)                       { sfx; FUN_005c6740(row); return; }  // EQUIP
  if (record[1] > 0 && FUN_005c1c90() != 0)              { sfx; FUN_005c7710(row); return; }  // USE
}
sfx_error;                                                                                   // refuse
```

So there are **two** sub-screens on `ClassicMenuNodeItem`, both character pickers, and the reader
models neither:

| Screen | Opened by | Root container | Own manager | Closed by |
| --- | --- | --- | --- | --- |
| **Equip on whom** (item groups 0-3) | `0x1C6740` | `node + 0x300` | `node + 0x304` | `0x1C7560` |
| **Use on whom** (group 4 consumables) | `0x1C7710` | `node + 0x308` | `node + 0x314` | `0x1C7EF0` |

The native itself uses those two container pointers as the screen discriminator, in this order.
`0x1C3010`, the node's cancel handler:

```c
if (*(int *)(node + 0x300) != 0) { FUN_005c7560(); return; }   // equip screen open -> close it
if (*(int *)(node + 0x308) != 0) { FUN_005c7ef0(); return; }   // use screen open   -> close it
… otherwise invoke the page-exit callback at [node + 0x2BC] …
```

**`node + 0x300 != 0` and `node + 0x308 != 0` are the authoritative "a sub-screen is open" tests.**
Both fields are zeroed by the constructor `0x1C2CE0` and re-zeroed by their closers, so they are
never stale.

## 2. Proven: the sub-screen manager is pushed onto the node's own `+0x2C0` stack

This replaces the first revision's argument. That revision reasoned that the reader must fail
*because `FieldSubmenuCapture.cs` never mentions `0x314`*. **That argument was not sound** — the
`+0x2C0` ManagerStack is generic and is designed to carry more than one manager, so a reader that
only follows the stack could in principle have worked. The push is what settles it, and it is
explicit in the tail of `0x1C7710` (`claude-targetxrefs.txt` §5):

```
005C7C77 8bb3c0020000   mov  esi, [ebx + 0x2C0]    ; esi = the node's ManagerStack
005C7C7D 8b83140300     mov  eax, [ebx + 0x314]    ; the new target manager
005C7C82 8945a4         mov  [ebp - 0x5C], eax
005C7C8B 8b74248b …     ;  begin/end arithmetic on [esi+4] / [esi+8]
005C7C96 8b5c88fc       mov  ebx, [eax + ecx*4 - 4] ; ebx = stack.back()  (the CURRENT top)
005C7C9E 68d2040000     push 0x4D2
005C7CA5 ff15e0567800   call cocos2d::Node::stopAllActionsByTag
005C7CAB c6839002000001 mov  byte [ebx + 0x290], 1  ; suspend the previous top
005C7CB6 8d4e04         lea  ecx, [esi + 4]         ; &stack.begin
005C7CB9 e8d29be5ff     call 0x421890               ; vector<Manager*>::push_back
```

and the matching pop at the head of `0x1C7EF0`:

```
005C7EF3 83be0803000000 cmp  dword [esi + 0x308], 0
005C7EFA 747d           je   0x5C7F79
005C7EFC 8b8ec0020000   mov  ecx, [esi + 0x2C0]     ; the ManagerStack
005C7F02 e8e9560100     call 0x1DD5F0               ; pop: [stack+8] -= 4, then resume the new back
```

`0x1C6740` does the identical thing with `node + 0x304` (`claude-equiptarget.txt`, lines 436-446),
and `0x1C7560` the identical pop.

This also re-proves the stack layout the reader already assumes: `[node+0x2C0]` is a **pointer** to
the stack object, `begin` at `+4`, `end` at `+8`, capacity at `+0xC`, top = `*(end - 4)`.
`0x1DD5F0` and `0x21890` confirm both ends independently.

**Consequence.** The existing stack reader correctly finds the target manager. Its focused
transparent control is outside the item-row list, so the old capture takes `CaptureFocusedControl`,
finds no Labels and tries `CaptureInventoryCategory`. Target keys and their manager do not identify
Inventory category buttons, so that fallback fails. The missing relationship is between the
correctly identified control and its separate character card. The stack and FocusableState guards
remain valid and unchanged; the row-reader equality is not the branch taken by these controls.

## 3. Proven: the manager and its states are entirely ordinary — the `FocusableState` guard is not what fails

Root asked for the exact raw state types in case the guard is the problem. It is not.

The manager is allocated `operator new(0x2E0)` and constructed by `0x1DCCA0`, which writes
**`0x7A5D0C`** — `nsMenu::nsInput::Manager`, vtable RVA **`0x3A5D0C`**, the same constant the reader
already checks — and initialises `+0x290` (word, the suspend flag), `+0x294` (map pointer, null) and
`+0x2C0` (byte, enabled flag):

```
005DCCCB c7060c5d7a00       mov dword [esi], 0x7A5D0C
005DCCD3 66c78690020000 0000 mov word  [esi + 0x290], 0
005DCCDC c78694020000 00000000 mov dword [esi + 0x294], 0
005DCCF0 c686c002000000     mov byte  [esi + 0x2C0], 0
```

The map is built by the shared `0x23CD80` and then hung off `manager + 0x294`, and `0x1C7710` walks
it in a way that re-proves the reader's whole traversal model:

```c
*(Ref **)(node + 0x314) = manager;
pRVar10 = manager + 0x294;
*(int **)pRVar10 = piVar7;                               // manager+0x294 = the map  (a POINTER)
puVar1 = *(undefined4 **)(*(int *)pRVar10 + 4);          // sentinel = [map + 4]
for (puVar2 = **(undefined4 **)(*(int *)pRVar10 + 4); puVar2 != puVar1; puVar2 = *puVar2)
    FUN_005dd260(puVar2[3], puVar2[2]);                  // (value, key): {next, prev, key, value}
*(undefined1 *)(*(int *)(node + 0x314) + 0x2c0) = 1;     // enable
FUN_005dd3e0(*(undefined4 *)(node + 0x324));             // setFocus(node + 0x324)
```

`0x23CD80` writes each value as a heap `nsMenu::FocusableState` (vtable RVA **`0x3AC3F4`**,
`operator new(0x18)`), with the word `1` at `+0x10` and the control at `+0x14`:

```c
local_14 = operator_new(0x18);
*(undefined2 *)(local_14 + 4) = 1;                 // +0x10 word = 1 (never the disabled value here)
*local_14 = nsMenu::FocusableState::vftable;
local_14[5] = local_28;                            // +0x14 = the control
… key = param_1, param_1 incremented once per non-null control …
```

**Target focus keys are exactly `0, 1, 2, …`** — `0x1C7710` calls it as `FUN_0063cd80(0, 1, 2, 0)`,
so the first key is `0` and each subsequent control takes the next integer. There is no offset, no
`+8` row base, and no `999`-style parking key on these screens.

## 4. Proven: the focused control carries no text, but the cards do

The control at `FocusableState + 0x14` is built by `0x23C050`: a transparent 240×70
`cocos2d::ui::Layout` (background colour `(0,0,0,0)`) wrapped in an `operator new(0x430)`
**`nsMenu::CustomButton`** (`0x1D2160`, vtable RVA `0x3A4364`, second vtable `0x3A4358` at `+0x288`),
positioned at `y = -70 * index`, whose only child is a `menu/finger` sprite stored at
`CustomButton + 0x424`.

So a reader that looks for Labels under the focused control finds nothing, *correctly* — there is
nothing there. **The visible information is in a parallel card list, indexed by the same integer.**

`node + 0x310` is built by `0x23BC40` and always holds **exactly three cards**, added in order:

```c
pNVar4 = cocos2d::Node::create();                    // the container that becomes node+0x310
count = (bit7 of [party+0x219C] clear) + (…+0x21A0) + (…+0x21A4);   // present current-party members
local_70 = 0;
do {
    piVar6 = FUN_0063bed0(local_70);                 // a card: menu/CharaStatusFrame 240x36, y = -73.3*i
    addChild(pNVar4, piVar6);                        // ALWAYS added, populated or not
    if (local_70 < count) {
        … walk [party+0x219C] stride 4, skipping entries whose byte has bit 0x80 set,
          to the local_70-th present one; uVar11 = its dword = the character ID …
        FUN_00414830(local_68, uVar11);              // the character's name string
        addChild(piVar6, FUN_006400b0(ANCHOR_TOP_LEFT, 0xC));   // the name Label
        FUN_0063a140();                              // the "lv" / "hp" / "mp" children
        … getChildByName("lv") -> setPosition(200, 15); "hp" -> (0, 15); "mp" -> (0, 15) …
    }
} while (++local_70 <= 2);
```

Two facts follow directly:

* **Target key `i` ↔ card `children([node + 0x310])[i]`.** Both enumerations are the *same* walk of
  the party array in the *same* order, and both stop at the same `count`. No mapping table is needed.
* **The card layout is the one already read successfully elsewhere.** `0x23A140` — the function that
  creates the `lv`/`hp`/`mp` children — has exactly three callers: `0x23B070` (the Formation card
  builder reached from `0x1BEAC0`), `0x23BC40` (these target cards) and `0x23CB60`. The Formation
  reader already renders `Crono LV 1. HP 43/70. MP 8/8` from that structure live, so the existing
  rendered-text helper should read a target card unchanged.

`0x1C7710` additionally creates, per present member, an `operator new(0x298)` **`nsMenu::CharaAnime`**
(`0x23DA10`, vtable RVA `0x3AC0B8`) holding **the character ID at `+0x280`**, placed at
`card[i].position + (36.0, -35.0)` and added to `[node + 0x308]`. That is an independent
cross-check of the key→character correspondence that needs no string parsing.

The window itself is a 240×99 `menu/Win3` frame under `[node + 0x308]`, positioned at
`x = ((row + 1) & 1) * 240, y = -92` — i.e. the panel opens on the side of the two-column item grid
opposite the selected row. `node + 0x30C` is a plain wrapper `Node` between `+0x308` and `+0x310`.

### The all-party item type

```c
if (((byte)*(undefined4 *)(*(int *)(DAT_0081b4c4 + 0xfdc8) + 8
     + (*(uint *)(*(int *)(node + 0x2d0) + row * 0xc) & 0xfff) * 0xc) & 0x7f) == 1) {
    local_44 = FUN_0063c270();      // ONE control, 240 x (count*rowHeight), with `count` fingers
    addChild([node + 0x308], local_44);
    *(undefined4 *)(node + 0x324) = 0;
}
```

The item-data record is `0xC` bytes at `[[DAT_0081B4C4 + 0xFDC8]] + id * 0xC` (the same table
`0x1C1C90` tests for usability with `record[0] & 2`). **`record[8] & 0x7F == 1` means "targets the
whole party"**: a single control, so the only key is `0`, and `+0x324` is forced to `0`. All three
cards are still present and populated; the screen simply has one focusable region covering them.

## 5. Proven: the field contract

| Field | Meaning | Written by |
| --- | --- | --- |
| `node + 0x300` | equip-target root container (non-zero ⇒ that screen is open) | `0x1C6740`; cleared `0x1C7560` |
| `node + 0x304` | equip-target `nsInput::Manager` | `0x1C6740`; cleared `0x1C7560` |
| `node + 0x308` | use-target root container (non-zero ⇒ that screen is open) | `0x1C7710`; cleared `0x1C7EF0` |
| `node + 0x30C` | wrapper Node holding `+0x310` | `0x1C7710` |
| `node + 0x310` | **card container — 3 children, card `i` = target key `i`** | `0x1C7710`, rebuilt by `0x1C82B0` |
| `node + 0x314` | use-target `nsInput::Manager`; focus key at `manager + 0x2C4` | `0x1C7710`; cleared `0x1C7EF0` |
| `node + 0x318`/`0x31C`/`0x320` | vector of populated CharaAnime portrait icons; `end = begin` on close | `0x1C7ACF/0x1C7AD5`, `0x1C7EF0` |
| `node + 0x324` | mirror of the focused target key; **persists between openings** | ctor, `0x1C7D10` events 0/1, forced to 0 for all-party items |
| `node + 0x328` | in-flight use counter; cancel closes only while `< 1` | `0x1C7710` (=0), `0x1C7D10` (++), `0x1C8CE0` (--) |
| `node + 0x32C` | the held/picked-up row, `-1` when none | `0x1C5B20`, cleared by `0x1C6570`, `0x1C7560`, `0x1C7EF0` |
| `node + 0x330` | held-row overlay sprite | `0x1C5B20`; destroyed by all three closers |

The two event callbacks are `std::function` targets, reached only through vtable slot 2 of their
`_Func_impl` — which is why no call-graph search from the node reaches them. `claude-targetxrefs.txt`
§2 lists all eight lambdas in the class's RTTI group; three have the `void(nsInput::EventType, int)`
signature:

| `_Func_impl` vtable | `_Do_call` | Target | Constructed at |
| --- | --- | --- | --- |
| `0x3A375C` | `0x1C8F20` | `0x1C5B20` — the **item list** callback | `0x1C59CA` |
| `0x3A37B0` | `0x1C8D60` | the **equip-target** callback | `0x1C7428` (inside `0x1C6740`) |
| `0x3A3804` | `0x1C8C50` | `0x1C7D10` — the **use-target** callback | `0x1C7C13` (inside `0x1C7710`) |

Both trampolines do `add ecx, 4` before the call, so the capture block is
`{ vtable, node, row }` at `+0x0/+0x4/+0x8` — which is how `0x1C7D10` reads `*in_ECX` as the node and
`in_ECX[1]` as the row.

`0x1C7D10` itself (unchanged from the first revision, and now with the manager identified):

```c
if (event == 0) {
    if (key == *(int *)(*(int *)(*in_ECX + 0x314) + 0x2c4)) {  // confirming the already-focused target
        (*(int *)(*in_ECX + 0x328))++;
        FUN_005c7f80(key, row, completionFn);                  // apply the item
    } else *(int *)(*in_ECX + 0x324) = key;
} else if (event == 1) *(int *)(*in_ECX + 0x324) = key;
else if (event == 2 && *(int *)(*in_ECX + 0x328) < 1) FUN_005c7ef0();
```

The equip-target callback is far simpler — `0x1C8D60` plays a sound and closes the screen on both
confirm and cancel, using the event only to choose the sound id. Where the actual equip is committed
is **not traced**; see §9.

## 6. Proven: what happens after a use

`0x1C7F80(targetKey, row, completionFn)` calls `0x1C82B0(row)` at `0x1C81E1`:

```c
iVar1 = *(int *)(node + 0x2d0);
(**(**(node + 0x310) + 0x134))();          // removeFromParent the OLD card container
uVar2 = FUN_0063bc40();                     // build a fresh one
*(undefined4 *)(node + 0x310) = uVar2;      // node+0x310 is REPLACED
(**(**(node + 0x30c) + 0x10c))(uVar2);
FUN_005c2150();
(**(**(node + 0x2c8) + 0x120))();
FUN_005c5300();                             // re-render the item rows
if (*(int *)(iVar1 + row * 0xc + 4) < 1) *(undefined4 *)(… + node + 0x2d0) = 0;
```

and the completion lambda `0x1C8CE0` (capture `{vtable, node, row}`) runs when the animation ends:

```c
(*(node + 0x328))--;
record = [node + 0x2D0];
if (record[row].quantity < 1 && *(node + 0x328) < 1) {
    record[row].encodedId = 0;              // the exhausted row becomes {id 0, quantity 0}
    FUN_005c7610(0xffffffff);               // clear the help line
    FUN_005c7ef0();                         // close the target screen
}
```

Two consequences for the reader:

* **`node + 0x310` is destroyed and replaced after every single use.** A cached container pointer
  goes stale immediately; it must be re-read each frame.
* **An exhausted row is zeroed in place — the vector is not rebuilt or shortened.** The row becomes
  exactly `{encodedId 0, quantity 0}`, which is the same shape as the padded placeholder documented
  in `inventory-empty-row-0320-native-audit.md`. When the category held only that one item the
  existing empty-category rule matches and reads "Consumables. Empty." — which is what the log shows
  at 18:55:00. **But when other consumables remain, the same zeroed row sits inside a multi-record
  vector**, and the reader's "a blank row in a larger vector does not establish emptiness" rule then
  rejects it. That situation is not in the log and is not proven to be reachable in a state the
  player can rest on (the screen normally closes on the same frame), but the record shape is proven.
  Root owns whether to handle it.

## 7. Withdrawn: "picked up" was not a defect

The first revision called the 18:54:50 and 18:54:54 announcements of `Potion, 1, picked up`
misleading. **That is withdrawn.** `0x1C5B20` case 0 genuinely sets `node + 0x32C` on the first
confirm, and the row genuinely is picked up — the classic page uses the same press for "pick up to
move" and "pick up to use", and resolves the intent on the *second* press. The log is internally
consistent with this:

| Time | Log | Native |
| --- | --- | --- |
| 18:54:48 | `Potion, 1. Restores 50 HP.` | focus on the row, `+0x32C = -1` |
| 18:54:50 | `Potion, 1, picked up.` | first confirm: `+0x32C = +0x2F8` |
| 18:54:52 | `Consumables, category` / `Use / Switch` | focus moved to a header key; case 1 key<8 calls `0x1C6570`, which **clears** `+0x32C` |
| 18:54:53 | `Potion, 1.` — no "picked up" | correct: the hold really was dropped |
| 18:54:54 | `Potion, 1, picked up.` | first confirm again |
| ~18:54:55 | — | second confirm on the same row → `0x1C66B0` → `0x1C7710` |
| 18:54:56 | **`Unable to read the current selection.`** | target manager on top of the stack; §2's gate |
| 18:55:00 | `Consumables. Empty.` | `0x1C8CE0` zeroed the row and `0x1C7EF0` popped the manager |
| 18:55:03 | `Consumables. Empty.`, then the top menu shows **HP 63/70** (13/70 at 18:54:42) | the potion was consumed |

The defect in §2 is the missing target-card relationship. The first-confirm wording itself was
accurate and remains unchanged.

The HP figures 13/70 → 63/70 are read from the log's own top-menu announcements (lines 593-594 and
the 18:55:03 block), not from a user report; the first revision attributed them to the user.

## 8. Withdrawn: the "two live-distinguishable shapes"

The first revision offered two possible explanations and said only a live capture could separate
them. **That is withdrawn as unnecessary.** Shape 1 (the manager is pushed, the reader reads the
wrong key) is proven outright by §2; shape 2 (the reader keeps the list manager) cannot occur,
because the push happens inside the same call that opens the screen. The exhausted-row problem that
shape 2 described is real but separate, and is stated correctly in §6.

## 9. What a correct reading needs, and what is still open

Everything required is plain memory; nothing needs a call into game code.

1. Discriminate on `node + 0x300` then `node + 0x308`, in that order — the order `0x1C3010` uses.
2. Take the manager from that field (`+0x304` / `+0x314`), not from `+0x2DC`, and accept the stack
   top when it equals it.
3. Focused target key = `[manager + 0x2C4]`. `node + 0x324` mirrors it but **persists across
   openings** and is only forced to a valid value for all-party items, so treat it as a corroborating
   check, not a source.
4. Card = `children([node + 0x310])[key]`, re-reading `+0x310` every frame (§6). The populated icon
   vector at `node+0x318/+0x31C` has the same order and one entry per present member. Its CharaAnime
   objects carry character IDs at `+0x280`; the root's generic child order is not a character index.
5. The item under use is still the held row: `node + 0x32C` indexes `node + 0x2D0`.
6. `node + 0x328 > 0` means a use is in flight — the state in which cancel is refused.

**Open, and honestly unresolved:**

* **Where the equip screen commits an equip.** `0x1C8D60` closes on both confirm and cancel and
  ignores the key entirely, which cannot be the whole story. The commit is presumably in the
  `CustomButton` touch path (`0x1DD260` installs two touch lambdas per state) or inside `0x1C6740`'s
  own per-slot controls. Not traced; do not describe that screen's confirm behaviour yet.
* **The 18:55:01 blip.** A single failed read between two successful ones. Candidates are the
  `+0x310` rebuild and the row re-render `0x1C5300`, both inside `0x1C82B0`, and the one-frame
  `DelayTime(1/120)` continuation `0x1C7E30` schedules. I could not narrow it to one without a live
  frame, and I am not claiming one.
* **No live capture of either screen exists.** §§1-6 are executable layout; they are proven in the
  sense that the instructions are unambiguous, not in the sense that a running frame was observed.
* The party-presence encoding (`bit 0x80` of the dword at `[[DAT_0081B4C4+0x28] + 0x219C + i*4]`
  marks an absent slot) is used identically by `0x1C7710`, `0x23BC40` and `0x1BF330`. It refines
  `party-0321-native-audit.md` §2, which described the dword only as the character ID.
* `0x14830` builds the character name from `ECX + charId * 0x18 + 0x18E0`. The base register is not
  proven to be `[DAT_0081B4C4 + 0x28]`; prefer the card's own rendered Label over this table.
