# Field main-menu submenus â€” native audit

Read-only. Executable SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`,
image base `0x400000`. All addresses are RVAs unless a `VA` is stated. Tooling and raw dumps in
`artifacts/research/field-submenus-0318/`.

Scope: classic Inventory, Equipment, Tech, and Formation. The separate Save/Load and Tech detail readers are described in [the integration audit](field-save-slots-native-audit.md).

## 1. Class map

An MSVC RTTI sweep of all 1051 vtables (`rtti-all.txt`) yields two parallel node families under one
base. These are the owner objects a capture must validate against.

| Class | Vtable RVA | Deleting destructor (vtable slot 0) |
| --- | --- | --- |
| `MenuNodeBase@nsMenu` | `0x3A6388` | `0x1DEE20` |
| `ClassicMenuNodeTop@nsMenu` | `0x3A4024` | `0x1D0470` |
| `ClassicMenuNodeItem@nsMenu` | `0x3A383C` | `0x1C2CB0` |
| `ClassicMenuNodeTech@nsMenu` | `0x3A3C4C` | `0x1CAA10` |
| `ClassicMenuNodeFormation@nsMenu` | `0x3A3270` | `0x1BE6C0` |
| `MenuNodeTop@nsMenu` | `0x3AA058` | `0x1D2690` |
| `MenuNodeItem@nsMenu` | `0x3A8D6C` | `0x20DAA0` |
| `MenuNodeTech@nsMenu` | `0x3A9C48` | `0x21E400` |
| `MenuNodeEquip@nsMenu` | `0x3A7C30` | `0x1FE610` |
| `MenuNodeEquipSteam@nsMenu` | `0x3A84B8` | â€” |
| `MenuNodeFormation@nsMenu` | `0x3A8884` | `0x20BA40` |
| `MenuNodeSaveLoad@nsMenu` / `â€¦Steam` | `0x3A9288` / `0x3A980C` | â€” |
| `MenuNodeConfig` / `MenuNodeConfigSteam` | `0x3A6BC4` / `0x3A702C` | â€” |
| `MenuListView@nsMenu` | `0x3A60A0` | `0x1DD860` |
| `CharaEquipManager@nsMenu` | `0x3A8194` | â€” |
| `StatusBar@nsMenu` | `0x3ABCB8` | â€” |
| `Manager@nsInput@nsMenu` | `0x3A5D0C` | â€” |
| `ManagerStack@nsInput@nsMenu` | `0x3A5D04` | â€” |
| `FocusableState@nsMenu` | `0x3AC3F4` | â€” |

`0x1D0470` and `0x1D2690` are already in `GameVersionCatalog` as the classic and touch top-menu
deleting destructors, which fixes the slot-0 convention for the whole family: slot 0 is the deleting
destructor and its ABI is the one `ClassicTopMenuDeletingDestructorDelegate` uses â€”
`(nint node, uint flags)` returning a pointer, `ret 4`. It is **not** a bare `thiscall(node)`.

There is no `ClassicMenuNodeEquip`; Equipment uses the modern node in both interfaces.

## 2. One dispatcher opens every submenu

`ClassicTopMenuActionDispatcher` (`0x2A8630`) is a jump table on `[this]` bounded by 7 (table at
`0x2A872C`). Every arm is `call 0x2A84A0`, `call <factory>`, `push eax; call 0x2A5270`.

| Arm | Factory | Node getter | Node class |
| --- | --- | --- | --- |
| 0 | `0x2A8750` | `0x204E70` | `MenuNodeEquipSteam` â€” Equipment |
| 1 | `0x2A8940` | `0x1C2CE0` | `ClassicMenuNodeItem` â€” Inventory |
| 2 | `0x2A8B60` | `0x1CA8C0` | `ClassicMenuNodeTech` â€” Tech |
| 3 | `0x2A8D80` | `0x1BE730` | `ClassicMenuNodeFormation` â€” Formation |
| 4 | `0x2A8FA0` | `0x1ECB00` | `MenuNodeConfigSteam` â€” Config |
| 5 | `0x2A91D0` | `0x218960` | `MenuNodeSaveLoadSteam` â€” Save/Load |

`0x2A5270` is the single activation point: it receives the node and the node's vtable names the
page. Arms 6 and 7 were not decoded. `0x2BD1F0` is the touch dispatcher and was not decoded.

## 3. Managers, the focusable map, and the focused control

`MenuNodeBase::MenuNodeBase` (`0x1DED90`) allocates a sixteen-byte `ManagerStack` (vtable
`0x3A5D04`), zeroes `+4`, `+8`, `+0xC`, and stores it at **`owner + 0x2C0`** (`0x1DEE02`). `+4` and
`+8` are the usual begin and end, so the top manager pointer is read at `end - 4`, where `end = [[owner + 0x2C0] + 8]`. Every
class derived from `MenuNodeBase` has one, including `CharaEquipManager` (Â§6).

`nsInput::Manager::Manager` (`0x1DCCA0`) writes vtable `0x3A5D0C` and initialises `+0x2C4` and
`+0x2D4` to `0x80000000`, `+0x2D0` to `0xF9`, and `+0x290`, `+0x294`, `+0x2BC`, `+0x2C0`, `+0x2CC`,
`+0x2D8` to zero. `+0x2C4` is the **focused key** and `0x80000000` is the native "nothing focused";
`+0x2D4` is the pressed-key latch (copied at `0x1DD02C`, cleared at `0x1DD05E`). `0x1DCCC5` calls the
cocos Node constructor first, so a `Manager` is itself a Node.

**`manager + 0x294` holds a pointer to the focusable map, not the map.** The focus setter
`0x1DD3E0` is decisive:

```
0x1DD409  mov esi, dword ptr [eax + 0x294]   ; esi = the map, loaded from the field
0x1DD414  mov ecx, esi                        ; the lookup receives the map itself
0x1DD416  call 0x411B60(map, &iter, &key)
0x1DD41E  cmp eax, dword ptr [esi + 4]        ; sentinel is map + 4
0x1DD423  mov ecx, dword ptr [eax + 0xc]      ; the FocusableState
```

Corroborated twice: `0x1DCCDC` writes a single zero dword to `+0x294` and moves straight to `+0x2BC`
(an inline `unordered_map` would need `0x294`-`0x2B8` initialised), and the registration builder
`0x1C5E30` takes the map as an argument and uses `[arg + 4]` as the list head at `0x1C5EBB`.

> Do not read the sentinel at `manager + 0x298`. An earlier revision did; reverting the dereference
> alone turns eight tests red.

`0x411B60` hashes four key bytes with FNV-1a (`0x811C9DC5` seed, `0x1000193` prime) and indexes the
bucket array at `map + 0xC` under the mask at `map + 0x18`. List nodes are `next, prev, key, value`,
which is why the setter reads the state at `iter + 0xC`. A reader can walk the sentinel ring
linearly and reach the identical entry without reimplementing the hash; the capture does, bounded to
512 entries.

`FocusableState` (vtable `0x3AC3F4`) is built at `0x191345`, `0x191B5E` and `0x1C5EA1`, all of which
zero `+4`, `+8`, `+0xC`, set `+0x10` to 1, write the vtable and store the control at **`+0x14`**.
`0x1C5E30` also shows focus keys are simply the control's index in the page's control vector
(`[ebp-0x3C] = edi`, the loop counter).

The binder `0x1DD260` (catalogued as `NsMenuControlBinder`) only installs two `std::function`
closures on the control; it is the registry above that makes the focused control addressable.

## 4. Classic Inventory

`ClassicMenuNodeItem::ClassicMenuNodeItem` is `0x1C2CE0`. The page is driven by plain fields; no
cocos traversal is needed.

`0x1C75E8` is decisive:

```
0x1C75E8  mov eax, [node + 0x2F8]      ; cursor row
0x1C75EE  lea ecx, [eax + eax*2]       ; row * 3
0x1C75F1  mov eax, [node + 0x2D0]      ; row vector begin
0x1C75F7  push [eax + ecx*4]           ; record + 0 = encoded item id
0x1C75FC  call 0x1C7610(node, encodedId)
```

Records are twelve bytes. `0x1C6627` and `0x1C6651` compute the count with the `0x2AAAAAAB`
reciprocal of 12 over `[node+0x2D4] - [node+0x2D0]`. `0x1C7FCD` skips any row whose `record + 4` is
not positive, so that is the quantity and a non-positive one draws nothing. `record + 8` is swapped
along with `+0` and `+4` during reordering (`0x1C647C`-`0x1C64A6`), so it is per-row payload of
unknown meaning and is not exposed.

| Field | Offset | Evidence |
| --- | --- | --- |
| List view | `node + 0x2C8` | `0x1C5BA2` invokes the row accessor `0x1C1B60` on it |
| Row vector begin / end | `node + 0x2D0` / `+0x2D4` | `0x1C75F1`, `0x1C6630` |
| Encoded item id / quantity | `record + 0` / `+4` | `0x1C75F7`, `0x1C7FCD` |
| Cursor row, `-1` = none | `node + 0x2F8` | `0x1C75E8`, cleared `0x1C5C6A` |
| Help panel visible | `node + 0x2FC` | set `0x1C76D1`, cleared `0x1C7691` |
| Row picked up to reorder, `-1` = none | `node + 0x32C` | `0x1C5B94`, reset `0x1C75B8` |

`node + 0x2C0` is the inherited manager stack, not a child widget, and `node + 0x2CC` is a
`StatusBar@nsMenu` rather than a plain Label; neither is read as one.

**Reorder, not use-on-target.** `0x1C5B60` commits `[node+0x32C] = [node+0x2F8]` when the held row
is negative; pressing again on the same row cancels (`0x1C66B0`) and on a different row calls
`0x1C63D0(held, cursor)` â€” a swap. Reported as "picked up" and "moving <name>".

## 5. Text

Every page loads its own caption from **bank `0x23`** with `push index; push 0x23; push out;
call 0x1B9060`. Classic and modern twins agree, which cross-checks each:

| Page | Index | Classic site | Modern site |
| --- | --- | --- | --- |
| Equipment | `0x20` | `0x1FE89B` | `0x2055E7` |
| Inventory | `0x21` | `0x1C2EE3` | `0x20DCB0` |
| Tech | `0x22` | â€” | `0x21E698` |
| Formation | `0x25` | `0x1BE96C` | `0x20BC33` |

Item names and help share one decoder with the battle item list:

```
index = (encoded & 0xFFF) + GroupBase[encoded >> 12]   ; GroupBase = image RVA 0x39906C, 13 entries
name  = bank 0x1B   (0xB7CE0)
help  = bank 0x1D   (0xB7FC0, called at 0x1C76B1)
```

`0x1C769C`-`0x1C76A5` is byte-for-byte the same split as `0x41EAEA` against the same table, so the
decoder proved in `battle-interface-native-audit.md` Â§15.3 transfers unchanged. `0x1C7610(node, id)`
is the help updater: a negative id takes the empty branch at `0x1C7642` and clears `node+0x2FC`.

## 6. Equipment: the page node, its CharaEquipManager, and the detail panel

`0x205C00` is a `MenuNodeEquipSteam` member (`mov edi, ecx` at `0x205C2A`). It releases the previous
child, allocates `0x368` bytes, runs the `CharaEquipManager` constructor `0x206380`, then:

```
0x205C99  mov  dword ptr [edi + 0x2F0], esi    ; esi = the new CharaEquipManager
0x205C9F  call dword ptr [eax + 0x10C]         ; edi->addChild(esi)
```

so **`node + 0x2F0` is the CharaEquipManager** and it is a cocos child of the page node. Its
constructor derives from `MenuNodeBase` (`0x1DED90`, hence its own manager stack at `+0x2C0`), stores
its owner at `+0x2CC`, and keeps a pointer vector at `+0x2F4`/`+0x2F8` with the count cached at
`+0x300`.

**The detail container belongs to the child.** The panel builder is `0x2066A0`, which is
`CharaEquipManager` vtable **slot 158**: `0x3A8194 + 158*4 = 0x3A840C`, inside its own table because
the next vtable begins at `0x3A84B8`. Every other vtable that appears to contain `0x2066A0` is a
shorter table overrunning into this one, and the slot numbers fall by exactly seven per preceding
class, which is the signature of that overrun. The builder keeps `this` in `[ebp-0x94]`
(`0x2066CD`), reads it back into `esi` at `0x2070B6`, and:

```
0x2069BC  mov  dword ptr [edi + 0x2EC], eax    ; creates and stores the container on itself
0x206F97  mov  ecx, dword ptr [esi + 0x2EC]  ; addChild(panel one)
0x2070BD  mov  ecx, dword ptr [esi + 0x2EC]  ; addChild(panel two)
0x2070CC  call 0x209410(panel two)
```

`0x209410` is the stat-row builder: one string via `0x1B9110` (bank `0x3F` line `0x17`) and then
seven consecutive captions from bank `0x23` lines `0x33` to `0x39` at `0x20946E`, `0x2095BE`,
`0x20970E`, `0x20985E`, `0x2099AE`, `0x209AFE` and `0x209C4E`. Those seven are the equipment stat
rows.

So the chain is **`panel = [[node + 0x2F0] + 0x2EC]`**.

> `MenuNodeEquipSteam` has an *unrelated* field at its own `+0x2EC` (written at `0x20565F`). An
> earlier revision read the container from the page node; that is the wrong owner and yields a
> different object, which is why the fixtures now plant a decoy there.

**The slot cursor lives on the child's stack.** `CharaEquipManager` code uses its own inherited
manager stack (`mov ecx, [esi + 0x2C0]` at `0x206622`, `mov esi, [ebx + 0x2C0]` at `0x208500`),
while the page node's stack carries the character selector. The capture therefore resolves the
focused control from the child's stack first and falls back to the page node's, and
`FieldSubmenuCapture.OwnsManager(image, node, manager)` accepts either so a caller can filter
manager callbacks correctly.

The panel nodes themselves are kept only as locals and cocos children; there is no per-panel field.
Reading the container's subtree gives the current and new stat lines as rendered, in the game's own
words and language. Nothing is recomputed from item tables.

Coincidence worth remembering: `+0x2EC` is the detail container on `CharaEquipManager` and the
confirmation byte on `MenuNodeSaveLoadSteam`. Both readers gate on their own vtable, so there is no
conflict, but the two must not be conflated.

## 7. Tech and Formation

Tech character selectors read the Labels inside their focused control; the separate Tech detail
reader handles ability rows. Formation uses icon-only buttons and separate character cards.
Version 0.3.21 correlates keys 0..2 and 10..15 with those cards and handles native key 999 as a
locked intended member with no focused control. It also reads the owned usable-combos panel.
The [Party audit](party-0321-native-audit.md) provides the native writers, live capture, ownership
checks and boundaries of runtime verification; the earlier generic focused-control assumption
does not apply to Formation.

## 8. Gates applied to every page

1. **Ownership.** The resolved control's cocos parent chain (`+0x16C`) is walked up to 64 levels and
   must reach the page node. This is not an assumption: `0x205C9F` shows the Equipment child being
   `addChild`ed to the node, and `0x2A5270` is the scene-level attach for the page itself.
2. **Not a container, on the generic path only.** A focused control whose vtable is `MenuListView`
   (`0x3A60A0`) is refused by the generic label reader, because its subtree is every visible row.
   The Inventory row path does not use that reader, so a focused inventory list view is still served
   from its own row data.
3. **Bounded rendered text.** Selected controls and Party cards allow up to 64 Label fragments
   (a character card uses separate fragments for captions, values and colons). The Equipment
   detail container is a panel by design and has its own bound of 32.
4. **Inventory only.** The row path is taken only when the control also descends from the node's
   list view at `node + 0x2C8`; otherwise the focused control's own labels are reported, so standing
   on a category button never announces an inventory row.

The native manager update at `1DCF1E` skips a manager whose byte `+290` is set.
Equipment uses its child stack only while that manager is enabled. A disabled
child returns focus to the parent selector; a failed active-child capture does
not fall back to a stale selector. Every control ancestor must also be visible.

## 9. Coherence

`Capture` re-reads after the whole read and discards the snapshot on any difference: the node
vtable, the **manager identity** as well as its key, the resolved control, and â€” on the Inventory
row path â€” a composite of the row vector bounds, cursor, held row, help-visible byte and the focused
record's own id and quantity. The last matters because the row vector is rebuilt when an item is
consumed while the focus key never changes.

Equipment rechecks the child pointer, its class and ancestry, and the detail-panel pointer and ancestry after reading. The Equipment detail is never claimed when it cannot be read: a missing or wrong-class child, a
panel that does not hang off the child, an unreadable subtree or an oversized one all fall back to
the focused slot's own text rather than inventing stats.

## 10. Capture contract

`FieldSubmenuCapture(IReadableMemory).Capture(nuint imageBase, nuint node)` returns
`FieldSubmenuSnapshot(Kind, Title, FocusIdentity, Text)` or null.
`FieldSubmenuCapture.OwnsManager(nuint imageBase, nuint node, nuint manager)` reports whether a
manager is the one driving that page.
`SupportedNodeVtableRvas` lists the proved classes: `ClassicMenuNodeItem` (`0x3A383C`),
`ClassicMenuNodeTech` (`0x3A3C4C`), `ClassicMenuNodeFormation` (`0x3A3270`) and `MenuNodeEquipSteam`
(`0x3A84B8`). Other identified vtables are exposed as named constants so a caller can recognise them
without the capture pretending to read them.

Null is returned for an unknown node class, an empty or foreign manager stack, a manager reporting
the `0x80000000` no-focus sentinel, a focus key with no map entry, an entry whose value is not a
`FocusableState`, a focused control outside the page node's tree, a focused container on the generic
path, a control that renders nothing or too much, a cursor outside the native row vector, a row the
renderer itself skips, an out-of-range item group, and an unreadable TextManager or caption.

## 11. Hook boundaries

Slot 0 of every node class is the deleting destructor with the `(nint node, uint flags)` / `ret 4`
ABI.

| Purpose | RVA | `this` | Args |
| --- | --- | --- | --- |
| Activate any submenu | `0x2A5270` | top-menu controller | `node` |
| Enter Inventory / Tech / Formation / Equipment | `0x1C2CE0` / `0x1CA8C0` / `0x1BE730` / `0x204E70` | â€” | node in `EAX` |
| Inventory focus changed | `0x1C7560` | node | none, `ret 0` (via lambda `0x1C8D60`, `ret 8`, events 0 and 2) |
| Inventory help updater | `0x1C7610` | node | `encodedId` |
| Focus moved, any page | `0x1DD3E0` | manager | `key` |
| Leave page | `0x1C2CB0` / `0x1CAA10` / `0x1BE6C0` / `0x1FE610` | node | `flags`, `ret 4` |

## 12. Remaining limits

* Classic Tech uses its separate selected-row manager and rendered sibling description/MP panels. See [the Save and Tech audit](field-save-slots-native-audit.md) for the verified layout and coherence checks.
* **Which control class each page focuses** cannot be pinned statically: the registration builder
  `0x1C5E30` receives the controls as a `vector<Control*>` parameter, so no class literal exists in
  the registration path. Gates 2 and 3 are the mitigation, and they are guards rather than proof.
* **Touch Inventory** and the touch twins of the other pages are not claimed: `MenuNodeItem`'s
  destructor `0x20D9B8` puts its row vector at `+0x2D8`/`+0x2DC`, not `+0x2D0`/`+0x2D4`, and the
  touch dispatcher `0x2BD1F0` is undecoded.
* **Equipment stat semantics** are not interpreted. The capture reports the rendered lines; it does
  not know which number is "current" and which is "new", and does not label them.
* No part of this has been exercised against a running game.

## 13. Validation

Fixtures distinguish the focusable-map pointer from an inline decoy, the Equipment
child panel from the parent's unrelated field, and the enabled child cursor from
a stale parent or disabled child cursor. They also cover Inventory quantities and
reordering, malformed vectors, foreign controls, hidden ancestors, and container
bounds. Complete release validation and deployed hashes are recorded separately
in the 0.3.18 release record. No in-game speech test has been performed for these
new readers.
