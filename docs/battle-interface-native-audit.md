# Battle interface — native audit

A bounded map of where the PC battle UI can be observed. This is a chronological research log;
sections 15-16 correct earlier findings. Section 17 describes final integration. Research used
the closed game's executable; no live battle capture or game control was performed.

* Executable `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`, image base
  `0x400000`. Every address below is an RVA against that base.
* Method: MSVC RTTI sweep (`artifacts/research/battle-0317/rtti.py`, 1051 vtables), xref and
  disassembly helpers (`xref.py`), and Ghidra headless `-readOnly -noanalysis` on `ct_world`
  (decompiles saved as `ctors.txt`, `listmenu.txt`, `classicmenu.txt`, `classicupdate.txt`,
  `cursor.txt`).
* Root's parallel party/stat research is in the same directory (`root-party-evidence.md` and
  the `root-*.asm` dumps). Where we overlap we agree; I do not restate it.
* **Everything below is static analysis.** Nothing here has been confirmed against a live
  battle. Sections are marked Proven (from the binary), Likely, or Unknown.
* Third pass, 2026-09-14: **section 12 supersedes parts of sections 3, 5, 7 and 11.** Read it
  first. Second pass: sections 3-6 and 9-11 are new or corrected. Where the first pass
  was wrong the claim is withdrawn explicitly in section 4 rather than quietly edited.

## 1. Class map — Proven

| class | vtable RVA | notes |
|---|---|---|
| `SceneBattle` | `0x39DD48` | 2 slots only; thin scene wrapper |
| `Battle` | `0x39DD54` | `cocos2d::Layer`, 200 slots, ~0x287C-byte state block at `+0x294` |
| `BattleMenu` | `0x39DC2C` | 9-slot interface — the **touch/modern** interface |
| `ClassicBattleMenu` | `0x39D884` | same 9 slots — the **classic** interface |
| `nsBattleListMenu::BattleListMenuBase` | `0x3A2E38` | `cocos2d::Layer` + list interface |
| `nsBattleListMenu::BattleTechMenu` | `0x3A2AFC` | Tech submenu |
| `nsBattleListMenu::BattleItemMenu` | `0x3A279C` | Item submenu |
| `BattleModeSelectScene` | `0x3AFB84` | Active/Wait selection, not in-battle |

There is **no** `Target`, `Cursor` or `Command` RTTI class. Target selection is not a separate
object; see §5.

`nsBattleListMenu` has its own callback enum: `std::function<void(nsBattleListMenu::EventType,
int)>` (`_Func_base` vtable `0x39D6DC`, lambdas `0x39D74C` / `0x39D768`). It is the same shape
as the `nsMenu::nsInput::EventType` callbacks the mod already hooks for Settings and the title.

## 2. The most useful finding: the Tech and Item submenus are ordinary nsMenu — Proven

`nsBattleListMenu::BattleListMenuBase::build(int initialIndex)` at **`0x1BABF0`**
(`__thiscall`, one stack argument) constructs the submenu out of exactly the primitives the mod
already fans out:

| primitive | RVA | existing mod hook |
|---|---|---|
| `nsMenu::CustomButton` ctor | `0x1D2160` | `HookId.NsMenuCustomButtonConstructor` |
| nsMenu input manager create | `0x1DCCA0` | (rooted by other sets) |
| nsMenu control binder | `0x1DD260` | `HookId.NsMenuControlBinder` |
| nsMenu focus setter | `0x1DD3E0` | `HookId.NsMenuFocusSetter` |
| menu UTF-8 text-label factory | `0x2400B0` | `HookId.MenuTextLabelFactory` |

Call sites inside the battle list menus:
CustomButton `0x1BB147`, `0x1BC786`, `0x1BCA07`, `0x1BD986`, `0x1BE2A0`;
manager create `0x1BB3B8`; binder `0x1BB42C`;
focus setter `0x1BB454`, `0x1BB65A`, `0x1BB787`, `0x1BC516`, `0x1BCB64`, `0x1BD60D`;
label factory `0x1BBEF2`, `0x1BC090`, `0x1BD236`, `0x1BD2B2`, `0x1BD7EB`.

So the audited constants the mod already ships apply unchanged: `ManagerVtableRva 0x3A5D0C`,
`ManagerFocusKeyOffset 0x2C4`, `CustomButtonVtableRva 0x3A4364`,
`FocusableStateVtableRva 0x3AC3F4`, `FocusableStateControlOffset 0x14`.

**Instance layout of `BattleListMenuBase`** (proven from `build`, the destructors and the
focus handlers):

| offset | meaning |
|---|---|
| `+0x290` | root `cocos2d::Node` the menu attaches to |
| `+0x298` | `cocos2d::ui::ScrollView` holding the rows |
| `+0x2A0` | `nsMenu::nsInput::Manager` — **focused index is `[+0x2A0] + 0x2C4`** |
| `+0x2CC` | `std::function<void(EventType,int)>` result callback |
| `+0x2D8` / `+0x2DC` | entry vector begin/end, **stride 12 bytes** |
| `+0x2E4`, `+0x2E8`, `+0x2EC` | three per-character row indices (dual/triple tech columns) |
| `+0x2FC` | index used as `* 0x14` into a per-character table |

The list is laid out **two columns wide** (`build` computes rows as `(count + 1) / 2`).

**Virtual interface above `cocos2d::Layer`** (diffed across the three vtables):

| slot | base | BattleTechMenu | BattleItemMenu | meaning |
|---|---|---|---|---|
| `+0x320` | `0x1BB970` | `0x1BCDD0` | `0x1BDA80` | Likely: rebuild/refresh |
| `+0x324` | pure | `0x1BC190` | `0x1BC190` | **entry count** = `(+0x2DC − +0x2D8) / 12`. Proven. |
| `+0x328` | `0x1BAAF0` | `0x1BC1B0` | `0x1BD380` | Likely: per-entry enabled/usable |
| `+0x32C` | `0x1BAB00` | `0x1BC200` | `0x1BD3D0` | **called with the newly focused index on every move.** Proven from three call sites. |
| `+0x330` | pure | `0x1BC450` | `0x1BD520` | directional / confirm handling |
| `+0x334` | `0x1BB8E0` | `0x1BCE30` | `0x1BDAB0` | Unknown |

Every focus move goes `FUN_005DD3E0(index)` → virtual `+0x32C(index)` → callback `+0x2CC`
invoked with `EventType == 2` and the index. **`EventType 2` = selection changed.**

**Consequence for the mod:** Tech/Item submenu focus needs *no new native hook*. The three
shared nsMenu hooks already observe it, and `MenuTextLabelFactory` already sees the row text.
What is missing is only the mod-side consumer that correlates them, exactly as
`SteamSettingsHookSet` does today.

## 3. The command state lives in shared globals, not in either menu — Proven (second pass)

> **Partly superseded by §12.** The architectural claim below holds, but the fields in this
> table are the **Tech submenu**, not the base Attack/Tech/Item menu. §12.1 gives the base
> command cursor. Rows now known to be Tech-list rows are marked *(Tech list)*.

This is the central result of the second pass and it supersedes §4 and §5 of the first pass.

Both battle interfaces are **views** over one block of fields in the save-data canvas
(`canvas = [0x41B4C4]`). The classic renderer `0x20540` and the touch renderer `0x1DA70` read
the *same* fields, and neither module contains a single writer of them — every write comes from
the battle engine. A consumer can therefore read the command state without hooking either
interface's input path.

| field | type | meaning | how proved |
|---|---|---|---|
| `canvas + 0x19E78` | int | **character whose command window is open**; `0xFF` = none | `0x20540`, `0x20B50` and `0x1DA70` all bail to render mode 0 when it is `0xFF`; used as the `charIdx` for every row lookup |
| `canvas + 0x19EA0 + char*4` | int | *(Tech list)* **Tech cursor row**, relative to the page base | `0x1DA70` colours a row `BLACK` instead of `WHITE` exactly when `loopRow == [0x19EA0+char*4]`; `0x20540` uses it to index the selected entry |
| `canvas + 0x19EC0 + char*4` | int | *(Tech list)* page base (scroll offset) | base of the 6-row draw loop in both renderers; scroll arrows drawn when `> 0` |
| `canvas + 0x1A2C0 + char*4` | int | *(Tech list)* total entry count | down-arrow drawn when `pageBase < count - 6` (`0x20ADC`) |
| `canvas + 0x19C8C + idx*8` | int | *(Tech list)* entry id, `idx = char*0x14 + pageBase + row`; `0xFC` Dual, `0xFD` Triple | switch in `0x1E390` |
| `canvas + 0x19C90 + idx*8` | int | entry parameter (tech/item index) | `0x1E390`, `0x20787` |
| `canvas + 0x19EB8` | int | **item list cursor row** (global, not per character) | `0x20B50` |
| `canvas + 0x19EBC` | int | item list page base | `0x20B50`, arrows at `0x20D73`/`0x20DE9` |
| `canvas + 0x1ADB0` | int | item list total count | `0x20B50` down-arrow bound |
| `canvas + 0x19EEC` | int | **phase gate**; every renderer draws nothing unless it is `0` | `0x20599`, `0x20B98`, `0x1DACC`, `0x21175` |
| `canvas + 0x1AD20` | int | **party slot currently taking input**; negative = none | set by `0x1D8F0`; the HUD yellow-highlight test |
| `canvas + 0x1AD10 + slot*4` | int | per-slot battler state; **bit `0x80` set = slot not selectable** | guard in `0x20190`, `0x1D8F0`, `0x1BDD0` |
| `canvas + 0x1AD24` | int | number of party members able to act (2 → Dual, 3 → Triple available) | `0x1E47D` / `0x1E512` compare it against 2 and 3 |
| `canvas + 0x1AD50` | int | additional gate on the acting highlight; **still unnamed** | `0x1BDD0`, `0x20190` |
| `canvas + 0x19FA0 + slot*4` | int | slot present (root, cross-checked) | `0x20FD0`, `0x1BDD0` |

**Command list geometry.** Six visible entries laid out two columns by three rows
(`x = (row & 1) * … `, `y = (row >> 1) * …` in both renderers), scrollable, `0x14` = 20 entries
reserved per character. Entry ids `0xFF`, `0xFE` and `0xFB` mean "empty, draw nothing";
`0xFC` is the Dual Tech row (shown when `[0x1AD24] >= 2`) and `0xFD` the Triple Tech row
(`>= 3`). Any other id is an ordinary command. A row is drawn greyed when
`[canvas + 0x1AEF0 + (char*0x14 + entryParam)*0x20] & 0x80` is set — i.e. **unavailable**,
which is exactly the state a screen reader must announce.

**Two shared row renderers**, both called by the classic *and* the touch path, are the single
best hook points because each call is one visible row with its identity and state:

```
0x1E390  FUN_0041E390(charIdx, entryIdx, x, y, colour)   command / tech row
         55 8B EC 6A FF 68 9F 57 76 00 64 A1 00 00 00 00 50 83 EC 78
0x1EA70  FUN_0041EA70(entryIdx, x, y, colour)            item row
         55 8B EC 6A FF 68 10 58 76 00 64 A1 00 00 00 00 50 83 EC 44
```

## 4. Corrections to the first pass

Root was right on both challenges, and one of my own claims was wrong.

* **`0x1F840` is only the Escape latch.** It reads the pad object `[0x41C3DC]`, and when the
  held mask equals `0x30` it sets `menu + 0x6C0 = 1`; when the other mask has `& 0x30` it clears
  it. That is Chrono Trigger's "hold L+R to flee". So `BattleMenu` slot `+0x18` is
  `bool escapeHeld()` and `+0x1C` clears the latch — **not** a general `isDone()`, and slot
  `+0x14` is not command-cursor movement. My first-pass §3 labels for `+0x14`/`+0x18`/`+0x1C`
  were wrong and are withdrawn.
* **`canvas + 0x19E90` / `menu + 0x6A8` is a render mode, not a party slot.** `0x200E0`
  dispatches `0 → 0x20190` (character icons), `1 → 0x20540` (per-character command list),
  `2 → 0x20B50` (shared item list), then always `0x20FD0` (party status) and, when its argument
  is non-zero, `0x1FD60` (window frame placement). The three renderers are structurally
  different, which settles it: had the value been a party slot they would have been near
  identical. My first-pass §4 sentence calling it the acting party slot is withdrawn. The
  acting-character fields are `canvas + 0x1AD20` (highlighted) and `canvas + 0x19E78` (command
  window owner); they are usually equal while a command is being chosen but they are distinct
  fields.
* **`Battle`'s vtable really is 200 slots.** The scan cap was not the cause. Slot 200
  (`0x39E074`) is `0x656D6147` = the ASCII `"Game/battle/tblbtl/PlyAtkS…"`, so the table ends at
  `+0x31C` and is followed by string data, not by import thunks. Slots 197-199 hold
  `0x362402/0x362408/0x36240E`, entries in the same 6-byte thunk table this binary uses for
  unimplemented slots (`nsBattleListMenu` slot `+0x1E8` is `0x362426`).

## 5. Target selection — **this section is wrong; see §12.2**

I could not find the target cursor in the second pass and wrongly concluded it was absent from
the shared globals. Root was right that the touch update already reads a target table; §12.2
has the proven fields. The exclusions below remain accurate and are kept only because they
still save time (they say where the target state is *not*):

* It is not in either battle menu. The classic menu has exactly three render modes (§4) and none
  of them draws over the battlefield; `0x20FD0` is the party status panel and `0x1FD60` is window
  placement.
* It is not `canvas + 0x1AD24` — that is the count of party members able to act, compared against
  2 and 3 to decide whether the Dual and Triple Tech rows appear.
* It is not reachable from the cursor artwork: `Extension/cursor.png` is referenced only by the
  texture preloader at `0x1997CA`.
* The `Battle` module (roughly `0x150000`-`0x163000`, where `Battle::Battle` at `0x15BAB0` lives)
  references only 29 distinct fields in the whole `canvas + 0x19000 … 0x1B400` window and none
  of them more than twice, so the target state is **not** in the same global block as the command
  state. It is most likely inside `Battle`'s own `0x287C`-byte instance block at `+0x294`.

Next probes, in order, for whoever picks this up: (1) find the writers of `canvas + 0x19EEC` —
the phase gate that suppresses all three renderers — since a target-selection phase must set it;
(2) map `Battle + 0x294` by following `SceneBattle + 0x50` (`canvas + 0x15BA0`, the battler array)
consumers inside the `Battle` module; (3) find who calls `nsBattleListMenu` `0x1BBB60`
(`FUN_005BBB60(charIdx, entryIndex)`, prologue
`55 8B EC 6A FF 68 8F 5C 76 00 64 A1 00 00 00 00 50 83 EC 2C`), which is how the touch command
renderer hands a chosen entry to the Tech/Item submenu, and see what it sets afterwards.

## 6. Battle UI lifecycle — partly Proven

`SceneBattle::SceneBattle` at **`0x28C00`** (called from `0x28B5C`) caches the battle data roots:

```
this+0x04 = [0x41B4BC]              script data
this+0x44 = [0x41B4C4]              save-data canvas
this+0x4C = canvas + 0x14064
this+0x50 = canvas + 0x15BA0        battler array base, stride 0x80
this+0x54 = canvas + 0x13558
this+0x58 = canvas + 0x110B0        the field global array the mod already reads
```

`Battle` is a `cocos2d::Layer` whose own state block is at `+0x294` and is `0x287C` bytes; the
`Battle` destructor (`0x28A60`) calls `FUN_00428E90` on it and then frees it with the explicit
size `0x287C`, which is the cleanest available proof of that block's extent. `ClassicBattleMenu`
holds its owning `Battle*` at `+0x368` (`ClassicBattleMenu::init` writes it from its first
argument), so a hook on the menu can reach the battle instance without a global.

Interface selection is `[canvas + 0x13FDC] == 1`, read by `ClassicBattleMenu::init`.

Revised `BattleMenu` interface table (corrections marked):

| slot | BattleMenu (touch) | ClassicBattleMenu | reading |
|---|---|---|---|
| `+0x00` | `0x14A70` | `0x21670` | destructor |
| `+0x04` | `0x16040` | `0x1F8A0` | `init(Battle* owner, Node* parent)` — Proven for Classic |
| `+0x08` | `0x16000` | `0x1F890` | show: sets `+0x6BD = 1`, `+0x6C1 = 0`. Proven |
| `+0x0C` | `0x19EF0` | `0x1FF90` | per-frame update. Proven |
| `+0x10` | `0x1B980` | `0x200E0` | **render dispatch** (mode 0/1/2 + status + frame). Proven this pass |
| `+0x14` | `0x19E90` | `0x1F840` | **Escape (L+R) hold latch.** Corrected this pass |
| `+0x18` | `0x19380` | `0x1F820` | `bool escapeLatched()` → byte `+0x6C0`. Corrected |
| `+0x1C` | `0x193A0` | `0x1F830` | clears `+0x6C0`. Corrected |
| `+0x20` | `0x19060` | pure | touch-only |

**Still Unknown:** the scene push/pop edges. `SceneBattle`'s 2-slot vtable (`0x71B30`,
`0x71FF0`) has no direct callers, so the scene is driven through cocos2d rather than through
`SceneManager::NextScene`. Until that is closed, the most reliable activation signal available
is `ClassicBattleMenu::init` / `BattleMenu` slot `+0x04` for start, and the destructor
(`0x21670` / `0x14A70`) for teardown — both are per-battle, both are on the object that owns
`Battle*` at `+0x368`, and both bracket every render call. That is enough for validated
activation and teardown without knowing the cocos2d scene transition.

## 7. Localised text — Proven

The `TextManager` file table is built at `0x1B7A30`; each entry stores the loaded table into
`manager->files[fileId]`. The full 76-entry map is
`artifacts/research/battle-0317/message-file-ids.json`. It is anchored on the independently
proven `0x41 = msg/start.txt` and cross-checked twice against shipped behaviour:

* `0x3A = msg/resolution.txt` → msg 4 `Quit`, msg 5 `Exit?` — exactly what the shipped title
  Quit confirmation reads.
* `0x23 = msg/menu.txt` → msg `0x8E` `You cannot save at this time.` — exactly the save-mode
  fallback prompt in the save/load confirmation builder.

The battle-relevant banks:

| fileId | file | use |
|---|---|---|
| **`0x00`** | `msg/battle.txt` | command names and battle system text |
| `0x1B` | `msg/item.txt` | item names |
| `0x1D` / `0x1C` | `msg/item_mes.txt` / `item_mes2.txt` | item descriptions |
| `0x31` | `msg/mon_tec.txt` | enemy tech names |
| `0x32` | `msg/monster.txt` | enemy names |
| `0x43` | `msg/tec_mes.txt` | tech descriptions |
| `0x44` | `msg/tech.txt` | tech names |

`msg/battle.txt` line 0 onward: `0 Attack, 1 Tech, 2 Combo, 3 Item, 4 Escape, 5 Single Tech,
6 Dual Tech, 7 Triple Tech`, then status names (`8 Poison` … `23 Berserk`) and result labels
(`0x18 Enemies:`, `0x1B EXP`, `0x1C TP`, `0x1D Money`).

No battle command string is requested with an immediate message id, so the labels are fetched
with a computed index — the existing `TextManagerGetMsg` / `OpeTextResolver` fanout will still
observe them, but the mod cannot key on a constant pair the way the Quit confirmation does.

## 8. Visible-information boundary

Party HP/MP, the highlighted acting character, command names, Tech/Item row labels and their
enabled state, and enemy *names* are all on screen and are fair to speak. Enemy HP, enemy MP,
damage formulas, turn timers and any per-enemy internal state are **not** displayed and must not
be derived, even though `SceneBattle` caches a pointer to the whole battler array at
`canvas + 0x15BA0`. The three-slot party window is bounded by `slot < 3`; the same array
continues past it into enemy records, so any reader must clamp to the party slots the HUD itself
formats.

## 9. Proposed hook set — revised after the second pass

All `__thiscall` (ECX = the object) unless noted; prologue bytes are the first 20 at the entry.

**Tier 1 — no new native hooks.** Tech and Item *submenu* focus, row labels and confirm are
already fully covered by `NsMenuCustomButtonConstructor`, `NsMenuControlBinder`,
`NsMenuFocusSetter` and `MenuTextLabelFactory` (§2). Only a mod-side consumer is missing.

**Tier 2 — the command window, both interfaces, two hooks.**

```
0x1E390  command/tech row renderer   (charIdx, entryIdx, x, y, colour)
         55 8B EC 6A FF 68 9F 57 76 00 64 A1 00 00 00 00 50 83 EC 78
0x1EA70  item row renderer           (entryIdx, x, y, colour)
         55 8B EC 6A FF 68 10 58 76 00 64 A1 00 00 00 00 50 83 EC 44
```

Both are shared by the classic and touch paths, so one pair covers both. Each call is one
visible row; the cursor is `canvas + 0x19EA0 + char*4` (commands) or `canvas + 0x19EB8` (items),
and "unavailable" is the `0x80` bit at `canvas + 0x1AEF0 + (char*0x14 + param)*0x20`. These run
every frame, so the consumer must debounce on (mode, char, cursor, pageBase).

If a per-frame render bracket is wanted instead, the classic dispatcher is

```
0x200E0  ClassicBattleMenu render dispatch
         55 8B EC 83 E4 F8 51 56 FF 75 08 8B F1 8B 8E 34 02 00 00 8B
```

**Tier 3 — lifecycle and acting character.**

```
0x1F8A0  ClassicBattleMenu::init(owner, parent)   55 8B EC 6A FF 68 6C 59 76 00 64 A1 00 00 00 00 50 81 EC 2C
0x21670  ClassicBattleMenu::~ClassicBattleMenu     (teardown edge)
0x1D8F0  select acting character(int slot)         55 8B EC 8B 15 C4 B4 81 00 8B 45 08 3B 82 20 AD 01 00 74 67
0x1BDD0  shared party HUD refresh                  55 8B EC 6A FF 68 C0 55 76 00 64 A1 00 00 00 00 50 83 EC 4C
0x28C00  SceneBattle::SceneBattle()                55 8B EC 6A FF 68 2B 61 76 00 64 A1 00 00 00 00 50 51 56 A1
```

`0x1D8F0` is the cleanest "the acting character changed" edge: it is the only writer of
`canvas + 0x1AD20` found, it is guarded by the same `0x80` availability bit the HUD uses, and it
takes the new slot as its single stack argument.

**Not recommended yet:** anything aimed at target selection (§5), and anything on `Battle`'s
200-slot vtable until that block is mapped.

## 10. Recommendation on sequencing — revised

Root's objection is correct and I withdraw my first-pass suggestion. A slice that reads out HP/MP
and lets the player browse the Tech and Item lists, but cannot tell them which command is
highlighted or who an attack will hit, is not usable battle access — it is a status readout.

With the command state now proven, the honest split is:

1. **Commands are ready to implement.** Cursor, page, count, per-entry id, availability and the
   Dual/Triple rows are all proven fields with two shared render hooks, for both interfaces.
2. **Targets are not.** Nothing in this pass located the target cursor, and §5 lists what has
   been excluded so the next pass does not repeat it. I would treat "choose an attack and know
   its recipients" as one deliverable and not present battle access as usable until the target
   side is closed, which matches root's position.

The one thing worth doing before the target work is confirming the *lifecycle* bracket in §6
(`init` / destructor on the menu that owns `Battle*`), because every later hook wants a validated
activation scope and that bracket does not depend on the unresolved cocos2d scene transition.

## 11. Limits

Static analysis only; the game was not run and nothing here has been checked against a live
battle. Field names in §3 come from display-side branches — they are what the game *draws*, which
is the right standard for a screen reader, but they are not confirmed to be the same variables the
input code writes. `FUN_00419E50(out, a, b)` at `0x19E50`, which produces the row text inside
`0x1E390`, was not decoded: its two constants `0x25`/`0x28` do not correspond to the message-file
ids in §7, so the row text path is Unknown and the file-id table must **not** be assumed to apply
there. The touch `BattleMenu` was mapped by vtable slot plus its command renderer `0x1DA70`; its
other implementations were not read. `Battle + 0x294` is still unmapped. Root's
`root-party-evidence.md` and `root-message-evidence.md` cover the party numbers and result
messages and are not restated here; where this document and root's differ, prefer root's.

## 12. Third-pass corrections — Proven unless marked

Reconciliation against Codex's disassembly findings. Every item below was re-derived here from
the binary; where I disagree with the suggested reading I say so with the evidence.

### 12.1 The base Attack / Tech / Item menu — I conflated it with the Tech list

Codex is right. The **base command menu is drawn by render mode 0 (`0x20190`)**, not mode 1.
Mode 1 (`0x20540`) and the touch renderer `0x1DA70` draw the **Tech list**, which is why they
carry the `0xFC` Dual and `0xFD` Triple special rows. So the modes are
**0 = base commands, 1 = Tech, 2 = Item**, and §3's cursor/page/count fields belong to the Tech
list only.

Base-command state, all verified by disassembly (`ebx = canvas + 0x15BA0` in `0x20190`, so the
`+0x5xxx` displacements below are canvas-relative as shown):

| field | site | meaning |
|---|---|---|
| `canvas + 0x1AD20` | `0x20202` `mov eax,[ebx+0x5180]` | **active party slot**; taken as a signed byte, negative aborts the draw |
| `canvas + 0x1AD10 + activeSlot*4` | `0x20213` `mov eax,[ebx+eax*4+0x5170]` | **mapped battler index**; negative aborts the draw |
| `canvas + 0x1AD50` | `0x20222` `cmp [ebx+0x51B0],0` | must be `0` or nothing is drawn |
| `canvas + 0x19E94 + mappedIndex*4` | `0x20239` `mov eax,[ebx+eax*4+0x42F4]` | **base-command cursor row** |

The cursor arrow confirms it from the display side: `0x204DF` computes `row * 18`
(`lea eax,[eax+eax*8]; add eax,eax`) and `0x204FF` computes `activeSlot * 0x5C`
(`imul eax,[ebp-0x7C],0x5C`, where `[ebp-0x7C]` was loaded from `canvas + 0x1AD20` at `0x20208`),
then calls the sprite helper at `0x20514`. This is exactly Codex's reading.

The three rows are emitted as `FUN_004193B0(out /*ECX*/, type /*EDX*/ = 0x25, index /*stack*/)`
with index `0`, then `1` or `3` chosen by the Combo predicate `FUN_00414880(slot)`, then `2`.
Type `0x25` resolves to **fileId `0x3B` = `msg/sfc_btl.txt`** (see §12.3), whose first four lines
are `0 Attack, 1 Tech, 2 Item, 3 Combo` — an exact content match, so the mapping is settled.

`FUN_0041D8F0` (§9) copying `canvas + 0x19E94 + X*4` now reads naturally: it carries the
base-command row across a character switch.

### 12.2 Target selection is in the shared globals after all

My §5 was wrong. The target state is reachable, and the cursor logic is small and symmetric.
Using `base = canvas + 0x15BA0` (the value the movement helpers hold at `[ecx+0x50]`):

| field | base-relative | meaning | site |
|---|---|---|---|
| `canvas + 0x19F0C` | `+0x436C` | **target cursor index**, `0 … 10`; wraps at 11 | `0x4B103` inc / `0x4B163` dec |
| `canvas + 0x1A140 + i*4` | `+0x45A0` | **candidate battler id** per cursor slot; **sign bit set (`0x80`) = skip** | `0x4B128` / `0x4B188`, `test dl,dl; js` retry |
| `canvas + 0x1ACD4` | `+0x5134` | **selected target battler id** — the recipient | `0x4B136` / `0x4B196` |
| `canvas + 0x1AEB8 + i*4` | `+0x5318` | second per-cursor table; compared against the battler under the pointer by the touch hover test | `0x1A12D` `cmp [esi+eax*4+0x5318],ebx` with `eax = [esi+0x436C]` |
| `canvas + 0x19FA0 + i*4` | `+0x4400` | slot present (root, cross-checked) | `0x1A00F` |
| `canvas + 0x19EEC` | `+0x434C` | phase gate; targeting is active when non-zero | `0x1A02F` |

Cursor-next in full (`0x4B100`), which is the cleanest statement of the model:

```
inc  [base+0x436C]                     ; cursor++
if   ([base+0x436C] >= 0x0B) = 0       ; wrap at 11
edx = [base + cursor*4 + 0x45A0]       ; candidate
if   (edx signed-negative) retry       ; 0x80 = empty slot, skip
[base+0x5134] = edx                    ; commit the selected target
```

Cursor-previous (`0x4B160`) is the mirror image, wrapping to `0x0A`.

Codex's route via `0x19FE9` also checks out: `FUN_0041A200(dest = ebp-0x5358, src = canvas+0x15BA0)`
is a structured `rep movsd` mirror that preserves offsets, so the local compare at `0x1A050`
against `[ebp-0x40]` is a copy of `canvas + 0x1AEB8`. The **live** path at `0x1A12D` reads that
table directly, so the address does not depend on the copy analysis.

**Single versus group — Likely, not Proven.** Two parallel 11-entry tables exist: `+0x45A0`
holds the battler id the cursor commits, and `+0x5318` is what the hover test compares against.
The builder writes `+0x5318` with small literals (`0x4C906` stores `3`) in an eight-fold repeating
pattern at `0x4C906/0x4C949/0x4C988/0x4C9C7/0x4CA06/0x4CA45/0x4CA84/0x4CAC5`, which reads like a
per-slot **group id**, making the hover test a group-membership test. That would be how a
whole-group attack highlights several battlers at once. I did not prove it; the safe claim is
that `canvas + 0x1ACD4` is the committed recipient and `canvas + 0x1AEB8 + cursor*4` is the value
the UI matches battlers against.

**Enemy name path — Proven.** `FUN_004193B0` types `0x0D`, `0x27` and `0x33` dispatch to
`0x1941B`, which calls the text resolver with **fileId `0x32` = `msg/monster.txt`**, so an enemy
name is one `(0x32, monsterId)` lookup. The monster id's location inside the `0x80`-byte battler
record was not established in this pass.

### 12.3 `FUN_004193B0` is a type-to-bank dispatch — withdraws the §11 caveat

§11 warned that the constants `0x25`/`0x28` do not correspond to message-file ids. That warning
is withdrawn: they are **type** ids, and `FUN_004193B0(out, type, index)` maps them to banks.
The table is a byte map at RVA `0x194D8` indexed by `type - 6` (valid `type` = `6 … 0x3B`), then a
jump table at RVA `0x194B4`. Decoded:

| type(s) | resolves to |
|---|---|
| `0x06` | fileId `0x00` `msg/battle.txt` |
| `0x07`, `0x28`, `0x34` | `FUN_004B96D0` → bank `0x44` `msg/tech.txt` (root, `root-message-evidence.md`) |
| `0x08` | `FUN_004B9760` |
| `0x09`, `0x26` | `FUN_004B7CE0` |
| `0x0A` | `FUN_004B7FC0` |
| `0x0D`, `0x27`, `0x33` | fileId `0x32` `msg/monster.txt` — **enemy names** |
| `0x0E` | fileId `0x31` `msg/mon_tec.txt` — enemy tech names |
| `0x25`, `0x3B` | fileId `0x3B` `msg/sfc_btl.txt` — **Attack / Tech / Item / Combo** |
| all other types in range | a fixed literal at `0x19D5CC`, not localised |

`FUN_00419E50(out, type, index)` at `0x19E50` forwards to `0x193B0` and then substitutes through
`0x19510`, as root decoded; the §7 file-id table therefore *does* apply to battle text, one level
of indirection down.

### 12.4 Lifecycle — the scene owns the menu; `menu + 0x368` is not the scene

Codex's ownership direction is confirmed, the `+0x368` reading is not.

* **Proven:** at `0x47186C` the code calls `FUN_004216A0` — the `ClassicBattleMenu` allocator,
  whose constructor writes `ClassicBattleMenu::vftable` (`0x79D884`) at `0x216F2` — and stores
  the result at **`[ebx + 0x18CC]`**. It immediately sets `[menu + 0x1C0] = ebx + 0x18D0` and then
  invokes `menu->vtable[+0x04]`. So the owning object holds the menu at `+0x18CC`, and that object
  is the large battle-scene block (the `0x287C` allocation that `Battle + 0x294` points to and
  that `Battle::~Battle` at `0x28A60` frees with the literal size `0x287C`). `+0x18CC` is read
  from the engine module throughout `0x29000`-`0x44000`, consistent with the engine driving the UI.
* **Not supported:** that `menu + 0x368` is that scene. The construction site pushes exactly
  **one** stack argument to slot `+0x04` — `[scene + 0x64]` — and `0x1F8A0` both stores it to
  `+0x368` and uses it as a cocos parent (`addChild` through `+0x108`/`+0x10C`). Elsewhere the
  same pointer takes raw `+0x40`/`+0x44` writes (`0x1BC24`, `0x1BC36`), which matches Codex's
  observation about `0x4AEBD0` using fields `40`/`44`, but it does not make it the scene object.
  Ghidra's two-parameter signature for `0x1F8A0` is inconsistent with the one-argument call site
  and should not be trusted.

The usable conclusion is unchanged and is now better founded: **`scene + 0x18CC` is the menu
pointer**, so a hook on `FUN_004216A0` (or on slot `+0x04`) gives a validated activation edge, and
the menu destructor gives teardown, without needing the cocos2d scene transition.

### 12.5 Status

Research only. No source, test, build, deployment or game control, and still no runtime capture.
Root owns the party numbers, result labels and the damage/miss paths
(`docs/battle-feedback-native-audit.md`, `root-party-evidence.md`, `root-damage-evidence.md`) and
those are not restated here. The user's speech-design answer is still outstanding, so nothing in
this document has been implemented.

## 13. Task 2 pre-implementation findings — new this pass

Everything here is Proven from the binary unless marked, and is what `BattleCapture` needs.

### 13.1 Finding the live Tech/Item submenu from the menu pointer

`menu + 0x35C` holds the active `nsBattleListMenu` instance, or `0` when no submenu is open.
It is a retained cocos pointer: `BattleMenu`'s constructor zeroes `+0x35C` and `+0x360`
(`0x14A46`), and the swap site at `0x417FA5`-`0x417FC9` releases the old value through the cocos
release thunk (`[0x785A58]`) and retains the new one (`[0x785A40]`) before storing it. The touch
command renderer dereferences it at `0x1DADE` and reads `+0x2D0` off it, which only makes sense
for a `BattleListMenuBase`.

`menu + 0x364` and `menu + 0x366` are the submenu-active bytes the renderers gate on
(`0x1DAD5`, `0x1DB43`, `0x1E691`, `0x1E6E4`; written at `0x17DF0` and `0x1DB4A`).

**Capture rule:** read `[menu + 0x35C]`; if non-zero, validate its vtable against
`BattleTechMenu 0x3A2AFC` or `BattleItemMenu 0x3A279C` (image-relative) and treat anything else as
incoherent. That identifies *which* list is open without guessing, and it is the modern-list focus
owner the spec asked to verify.

### 13.2 List entry layout and availability — Proven

The entry vector is `[+0x2D8] … [+0x2DC]`, stride **12**, count `(end - begin) / 12`
(virtual `+0x324`, `0x1BC190`). Availability is a single byte inside the entry, and **the two
submenus use different offsets**:

| submenu | virtual `+0x328` | usable predicate |
|---|---|---|
| Tech (`BattleTechMenu`) | `0x1BC1B0` | `entry[+0x08] != 0` |
| Item (`BattleItemMenu`) | `0x1BD380` | `entry[+0x04] != 0` |

Both first bounds-check `index < count`, so a capture must do the same.

`+0x2D4` is the **previously focused index**: virtual `+0x32C` (`0x1BC200`, `0x1BD3D0`) reads it
alongside the new index in order to un-highlight the old row. Useful for change detection, not for
the current selection — the current focus remains `[[menu+0x35C]+0x2A0] + 0x2C4`.

### 13.3 Row text is a cocos2d::Label child, and that is a problem for a pure memory read

`0x1BD3D0` shows the native path to a row's text: take the ScrollView at `+0x298`, call its
children accessor (vtable `+0x120`), `__RTDynamicCast` the child to `nsMenu::CustomButton`, fetch
its child with tag **`0x3F0`**, and `__RTDynamicCast` that to `cocos2d::Label`.

That path cannot be reproduced by `BattleCapture` as specified:

* it needs the cocos2d `Node` children-vector and tag offsets, which are not proven here, and
* `cocos2d` is a **separate module** in this build — `NameInputCapture` already has to derive
  `cocosBase` from the import at `0x3857D8` because cocos vtables cannot be pinned by image RVA.

The label *string* itself would be readable by the same self-proving trick `NameInputCapture`
uses: `[label + 0x278]` is the `LabelProtocol` sub-vtable, slot `+8` is the getter, and if its
bytes are `8D 81 <disp32> C3` then the MSVC string is at `label + 0x278 + disp`. The blocker is
reaching the `Label`, not reading it.

### 13.4 Still unproven — must not be claimed as supported

* **Group-target membership.** §12.2 stands: `canvas + 0x1ACD4` is the committed recipient and
  `canvas + 0x1AEB8 + cursor*4` is what the touch hover test matches battlers against. The
  builder at `0x4C906` and its seven siblings write small literals into that table, which *looks*
  like a group id, but nothing read this pass turns it into a proven set of highlighted
  recipients. `BattleCapture` must not report a group until that is closed.
* **Visible party status.** There is no battle-text call for `battle.txt` indices `8`-`0x17`
  (Poison … Berserk) anywhere in the battle modules: enumerating every immediate-type call site of
  `FUN_004193B0` yields type `0x25` four times (base commands), `0x26` once, and type `0x06`
  once, at `0x1BD7B8` inside the **Item** list builder — not a status path. Status is therefore
  rendered as sprites, and the icon-to-name mapping is exactly the item the spec already lists
  under Open Research. `BattlePartyMemberSnapshot.Status` must stay empty until an icon mapping is
  proven; an empty string is the honest value, not a guess.

## 14. What `BattleCapture` actually reads, and what it refuses to

Task 2 implementation record. `src/ChronoTriggerAccessibility.Native/Capture/BattleCapture.cs`
and `BattleSnapshot.cs`, covered by 20 tests in
`tests/ChronoTriggerAccessibility.Native.Tests/Capture/BattleCaptureTests.cs`. Native suite:
564 passing.

### 14.1 Ownership and coherence

`Capture(imageBase, menu)` returns null unless `menu`'s vtable is exactly
`ClassicBattleMenu 0x39D884` or `BattleMenu 0x39DC2C`, and the canvas pointer at
`0x41B4C4` is readable. The canvas pointer is re-read after the whole snapshot and a change
discards it, so a teardown mid-read reports nothing rather than a half-old mixture. Every read is
width-correct and x86-bounded; the whole body is exception-contained.

### 14.2 Focus precedence, and the phase gate that makes it safe

```
phase = [canvas + 0x19EEC]
phase != 0            -> target focus only
phase == 0, list open -> list focus only (null if the list is incoherent)
phase == 0, no list   -> base command focus
```

The gate is the one every command and list renderer already tests (`0x20599`, `0x20B98`,
`0x1DACC`, `0x21175` require zero) and the one the touch target-hover loop runs under
(`0x1A02F`). Using it both ways keeps a *stale* target cursor from being spoken during command
selection, which was the real risk in ordering these three sources. **The "non-zero means
targeting" direction is Likely, not Proven** — it is the complement of a proven condition, not
itself observed.

An open list owns the selection outright: if `[menu + 0x35C]` is a `BattleTechMenu` or
`BattleItemMenu` but its manager, count or entry is incoherent, focus is null rather than
falling through to the base command, because reading a base-command row while a submenu covers
the screen would name a row the player is not on.

### 14.3 What each focus kind reports

| kind | identity | text | source |
|---|---|---|---|
| base command | `command:<slot>:<row>` | `sfc_btl.txt` row 0-3 | §12.1: gate `0x1AD50`, slot `0x1AD20`, mapped `0x1AD10+slot*4`, row `0x19E94+mapped*4`; both slot and mapped rejected when the sign bit is set, exactly as the renderer's signed-byte tests do |
| tech row | `tech:<char>:<index>:<id>` | `tech.txt` name, or `battle.txt` 6/7 for the `0xFC`/`0xFD` Dual and Triple rows, suffixed `, unavailable` when the native usable byte is clear | focus `[[list+0x2A0]+0x2C4]`, count `(+0x2DC-+0x2D8)/12`, usable `entry[+8]`, id `0x19C8C + (char*0x14 + page + index)*8` |
| item row | `item:<index>:<id>` | **null** — see 14.4 | focus and count as above, usable `entry[+4]`, id `0x1B668 + (page+index)*4` |
| target | `target:<cursor>:<battler>` | `monster.txt` name via `0x1A018 + battler*4` | cursor `0x19F0C` bounded to 0-10, candidate `0x1A140 + cursor*4` rejected when the sign bit is set, and reported only when the candidate equals the committed recipient `0x1ACD4` |

Party slots come from the fields the HUD formatter itself uses: presence `0x19FA0+slot*4`,
character id `0x1324C+slot*4`, custom name `0x1908 + id*24`, and the four `u16`s at
`0x15BA0 + slot*0x80 + 3/5/7/9`. Absent slots are omitted, never invented. **No enemy HP is read
anywhere**; the only enemy datum consulted is the name index.

### 14.4 Deliberate gaps — encoded, not filled

* **Item names.** The item row renderer `0x1EA70` resolves the name through battle-text type
  `0x26`, which the §12.3 dispatch sends to **`FUN_004B7CE0`** — a resolver that is not decoded.
  `item.txt` is `0x1B`, and the parallel tech case does go to a plain bank, so `0x1B` is a
  plausible destination, but it is not proven and is therefore not used. `FocusText` is null for
  item rows and the identity carries the real native id so nothing is lost when `0x4B7CE0` is
  decoded. The quantity at `0x1B674 + index*4` is bounds-checked and available at the same index.
* **Status.** `BattlePartyMemberSnapshot.Status` is always empty. See 13.4.
* **Group targeting.** Only the single committed recipient is reported.

### 14.5 Group and status renderers — the bounded pass, and where root should continue

I ran the requested focused renderer pass. It did not close either item, and it **corrects my own
§12.2 speculation**, so the addresses below are the handover.

**`canvas + 0x1AEB8 + i*4` is not a group table.** At `0x1A174` the touch cursor advance tests it
with `test byte ptr [esi+eax*4+0x5318], 0x80; jne` — the identical skip convention the classic
cursor applies to `canvas + 0x1A140 + i*4` at `0x4B128`/`0x4B188`. The two are **parallel
candidate lists**, one per input path, not battler-id versus group-id. The §12.2 note calling
`0x1AEB8` a likely group id is withdrawn.

The most promising unexplored lead is an append-and-count list that looks like a resolved
recipient set:

| address | what it does |
|---|---|
| `0x4AE96` | `mov [ecx + eax*4 + 0x45DC], edi` — appends a battler at index `[base+0x45EC]` |
| `0x4AF15` | `inc [eax + 0x45EC]` — the count immediately after that append |
| `0x99AE7` | `dec [eax + 0x45EC]` — pop, so it may be a stack rather than a target set |
| `0x782E6` | initialises `[eax + 0x45DC]` to `0xFF` |
| `0xACDC7` | sets `[eax + 0x45EC]` to 1 |

Canvas-relative that is list `canvas + 0x1A17C`, count `canvas + 0x1A18C`. Whether it is the
highlighted recipient set or an action queue is exactly the open question; the `dec` at
`0x99AE7` is the reason I did not claim it.

**Status icons were not located.** Enumerating every immediate-type call site of
`FUN_004193B0` in the battle modules yields type `0x25` four times, `0x26` once and `0x06` once
(at `0x1BD7B8`, inside the *item* list builder). Nothing requests `battle.txt` `8`-`0x17`
(Poison … Berserk), which is consistent with status being drawn from a sprite atlas. The next
probe is the sprite path, not another text sweep.

### 14.6 Status of this work

Implementation and tests only. No Core, Mod, `HookContract`, `GameVersionCatalog`,
`BattleFeedbackCapture`, packaging or version files were touched, nothing was committed,
built for packaging or deployed. Synthetic fixtures do not establish live battle coverage.

## 15. Correction pass: the list state is the canvas, not the retained list menu

Root's review of the first `BattleCapture` found six defects. All six are confirmed and all six
are fixed. This section records only what was newly proved from the executable
(SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`, base `0x400000`).

### 15.1 Both interfaces select through one shared state

`ClassicBattleMenu` (vtable RVA `0x39D884`) derives from `BattleMenu` (`0x39DC2C`), and the
fields that drive both lists live on the base class, so one reader serves both:

| Field | Address | Proof |
| --- | --- | --- |
| Panel mode (0 base, 1 Tech, 2 Item) | `canvas + 0x19E90` | written at `0x19F6C`/`0x20063` |
| Menu's copy of the mode | `menu + 0x6A8` | draw dispatcher `0x200E0` switches on it at `0x42012B`; both ticks mirror the canvas into it (`0x419F33`, `0x42002A`) |
| Panel visible flags | `menu + 0x6A3` base, `+0x6A4` Tech, `+0x6A5` Item | `0x419F81`-`0x419FAE` |
| Acting character id, `0xFF` = none | `canvas + 0x19E78` | `0x420579`, `0x420B6E`, `0x41D9E8` |

The flags at `0x6A3`-`0x6A5` are only written on a *transition*, so they are still zero on the
first frame of a battle. They are recorded here but deliberately not used as a gate: refusing the
opening base-command frame would be the worst possible failure mode.

The touch path proves the same selection arithmetic the classic renderers use. `0x41DAEE`
computes the focused tech as `[canvas + 0x19EC0 + char*4] + [canvas + 0x19EA0 + char*4]` and
`0x41E6AA` computes the focused item as `[canvas + 0x19EBC] + [canvas + 0x19EB8]`, then pushes
each into the retained cocos list at `menu + 0x35C` (tech) or `menu + 0x360` (item), gated on
`menu + 0x366`. The retained list is therefore a *consumer* of the canvas state, never its
source. Reading it was the original defect; `BattleCapture` no longer reads it at all, which also
disposes of the "stale retained list must not fall back to Attack" problem at the root.

| List state | Tech | Item |
| --- | --- | --- |
| Cursor in the six-cell grid | `canvas + 0x19EA0 + char*4` | `canvas + 0x19EB8` |
| Page base, in rows | `canvas + 0x19EC0 + char*4` | `canvas + 0x19EBC` |
| Row count | `canvas + 0x1A2C0 + char*4` (`0x41DA0B`) | `canvas + 0x1ADB0` (`0x420DE0`) |
| Visible cells | 6 (`0x420689`) | 6 (`0x420C3D`) |

Both renderers bail out entirely when `canvas + 0x19EEC` is non-zero or the acting character is
`0xFF` (`0x420599`, `0x420B98`).

### 15.2 Base commands: three rows, and row 1 is Tech *or* Combo

`0x420190` draws one command window per party slot and only ever three rows, from message bank
`0x3B`: index `0` at `0x4202B7`, index `1` or `3` at `0x420335`, index `2` at `0x42039A`. The
choice between Tech and Combo is `0x414880(canvas + 0x14010, slot)`, decoded field for field:

```
[canvas+0x19ECC] == 0                                    -> Tech
([canvas+0x1A1E0+slot*4] | [canvas+0x1A1D4+slot*4]) == 0 -> Tech
[canvas+0x1A430] == 0                                    -> Combo
[canvas+0x1AD24] == 3                                    -> Combo
leader = [canvas+0x1A42C]; leader == slot                -> Tech
[canvas+0x1AD10+leader*4] & 0x80                         -> Combo, else Tech
```

The cursor is `[canvas + 0x19E94 + mapped*4]` where `mapped = [canvas + 0x1AD10 + activeSlot*4]`
and `activeSlot = [canvas + 0x1AD20]` (`0x420202`-`0x420239`), and `0x4204C2`/`0x4204FF` place it
at `(row, activeSlot)`. There is no fourth row. The acting character's customised name is
`canvas + 0x1908 + charId*24`, proved by the resolver `0x414830` whose `this` is `canvas + 0x28`
and which the HUD path feeds from `canvas + 0x1324C + slot*4` (`0x41FAF2`).

### 15.3 Item entries are 0x14 bytes and the id is grouped

`0x41EAAD` is `lea edi, [eax + eax*4]` followed by `[ebx + edi*4 + ...]`, so the effective stride
is `0x14`, not `4`. Per entry: encoded id `+0x1B668`, flag byte `+0x1B670` whose `0x80` bit swaps
in the greyed colour `[0x7859FC]`, quantity `+0x1B674`. The renderer draws nothing when the
quantity or the id is zero.

The id is split at `0x41EAEA`:

```
index = (encoded & 0xFFF) + GroupBase[encoded >> 12]
GroupBase = image RVA 0x39906C = {0, 0x6F, 0xA1, 0xC8, 0x103, 0x12E, 0x66,
                                 0x4A, 0x2E, 0x22, 2, 0x63, 1}   (13 entries)
```

and the sum is handed to battle text type `0x26`. The type dispatch byte map at RVA `0x194D8`
(indexed by `type - 6`) and jump table at `0x194B4` decode fully:

| Type | Arm | Resolution |
| --- | --- | --- |
| `0x06` | `0x419443` | bank `0x00` |
| `0x07`, `0x28`, `0x34` | `0x419433` | `0xB96D0` -> bank `0x44`, raw index (tech names) |
| `0x08` | `0x41945B` | `0xB9760` |
| `0x09`, `0x26` | `0x41946B` | `0xB7CE0` -> bank `0x1B`, raw index (item names) |
| `0x0A` | `0x41947B` | `0xB7FC0` -> bank `0x1D` (item help) |
| `0x0D`, `0x27`, `0x33` | `0x41941B` | bank `0x32` (monsters) |
| `0x0E` | `0x4193EB` | bank `0x31` |
| `0x25`, `0x3B` | `0x419403` | bank `0x3B` (battle commands) |

`0xB7CE0` performs no index transform: `push index; push 0x1B; call 0x5B9060`. Root's
`0x4B7CE0` was the VA of the same routine.

**The battle item list shows no help text.** Every caller of `0xB7FC0` outside the dispatcher
(`0x1C76B1`, `0x202779`, `0x20A08C` and the rest) lies in the field-menu module, not the battle
module, so bank `0x1D` is not captured here: it is not on screen during battle.

### 15.4 Tech rows: blanks, bracketed labels, and the MP cost

`0x41E390` reads the row at `canvas + 0x19C8C + (char*0x14 + entry)*8`; dword 0 is the id and
dword 1 is the tech's slot within the character's block. Ids `0xFB`, `0xFE` and `0xFF` render
nothing (`0x420617`). Ids `0xFC` and `0xFD` render `"["` + bank `0x3B` index `0x0D` / `0x0E` +
`"]"` (`0x41E419`, `0x41E4AE`, format strings at `0x79D694` and `0x79D690`) - the earlier note
that these were `battle.txt` `6`/`7` was wrong. Anything else is bank `0x44` at the raw id.

Availability is `test byte [canvas + 0x1AEF0 + (char*0x14 + slot)*0x20], 0x80` at `0x41E59D`;
when set the row is drawn in the same greyed colour the item list uses. The record base is
therefore `canvas + 0x1AEE8` with stride `0x20` and the flag at `+8`. The info panel at
`0x420991` prints `[record + 0xC + partySlot*4]` with `"%2d"` for the three participants, beside
each participant's live MP from the battler array - these dwords are the MP costs, and a negative
low byte means the panel leaves that participant blank.

### 15.5 Battler names

Party slots 0-2 use the customised name for `canvas + 0x1324C + slot*4`. Enemy slots 3-10 use
`canvas + 0x1A018 + battler*4` as a bank `0x32` index, which is exactly what the native popup
path does (`0x46ECF3` pushes `[battlerArray + battler*4 + 0x4478]` with type `0x0D`, and
`0x15BA0 + 0x4478 = 0x1A018`). Character ids are bounded by 7 and battler slots by 11; the
earlier `0x40` bound was wrong. `BattleCapture` enumerates the whole fixed enemy range rather
than only live enemies, so the "A"/"B" suffix it adds for identically named enemies stays with
the same slot when another of them falls. The suffix is a mod-side rendering of the spatial
distinction a sighted player gets from the sprites; it is not a native label.

The battler record fields the HUD formatter reads (`0x41BE2C`) are, relative to
`canvas + 0x15BA0 + battler*0x80`: HP `+3`, max HP `+5`, MP `+7`, max MP `+9`, all u16.

### 15.6 Coherence

`Capture` now reads a state fingerprint - canvas pointer, `canvas + 0x19E90`, `menu + 0x6A8`,
phase gate, active slot, acting character, both list cursors and page bases, and the target
cursor - before and after the whole read, and discards the snapshot if any of them moved or if
the object is no longer a battle menu. A mode whose two copies disagree is a mid-transition frame
and yields no focus rather than the wrong panel. A party slot the game says is present but that
cannot be read coherently now fails the whole capture instead of silently shrinking the party.
`Text` re-reads both TextManager vectors after pulling a string, so a reload between the bounds
check and the read cannot hand back a line from a bank that no longer exists.

### 15.7 Status of this work

`BattleCapture.cs`, `BattleSnapshot.cs`, `BattleCaptureTests.cs` and this document only. 32
focused battle tests and the whole 597-test Native suite pass. No Core, Mod, `HookContract`,
`GameVersionCatalog`, `BattleFeedbackCapture`, packaging or version files were touched, nothing
was committed, built for packaging or deployed. Synthetic fixtures do not establish live battle
coverage.

## 16. Correction: `canvas + 0x19E78` is a party slot, and enemy names are not a roster

### 16.1 The acting field is a slot, not a saved character id

Section 15 called `canvas + 0x19E78` an acting *character id* and bounded it by 7. That is wrong;
it is a party slot, bounded by 3. Three independent proofs:

* The tech row table it scales is `canvas + 0x19C8C + (idx*0x14 + entry)*8`. Three blocks end at
  `0x19E6C`, immediately below the field itself; seven blocks would end at `0x1A0EC`, on top of the
  mode at `0x19E90` and the base command rows at `0x19E94`.
* The tech record table it scales is `canvas + 0x1AEE8 + (idx*0x14 + slot)*0x20`. Three blocks end
  at exactly `0x1B668`, where the item table begins; seven blocks end at `0x1C068`, inside it.
* `0x419289` reads `[canvas + (idx*3 + n)*4 + 0x1A2CC]`, which leaves room for only three counts in
  the table at `0x1A2C0` that `0x41DA0B` bounds the tech cursor against.

The consequence in the draft capture was real: matching the field against the saved character id
at `canvas + 0x1324C` only worked when a character's id happened to equal its slot, so Lucca in
slot 1 or Robo in slot 0 lost every Tech and Item label. `BattleCapture` now reads the field as a
slot, bounds it by 3, and names it directly from `canvas + 0x1324C + slot*4`. This also agrees with
root's presentation capture, which bounds the actor to 0-2.

### 16.2 Enemy names are a lookup table, not an enumeration

`BattlerNames` no longer letters identical enemies. Nothing in the battle module proves which enemy
slots are occupied - `0x44B470` only ever names the single committed recipient, and only for
battler >= 3 - so an unused slot can carry a stale or zero index that still resolves to a real
monster name. Lettering turned that into active misinformation: a lone Nu beside three unused slots
would have been announced as "Nu A". Chrono Trigger does not letter its enemies either, so reporting
the identical name twice is what a sighted player sees.

Name indices at or above `0xFC` are now skipped. `0x430606` dispatches `0xFC`, `0xFD` and `0xFE`
away from the name path, and `0x44B47C` uses `0xFF` as its "no name" argument, so none of the four
is a monster index.

`BattleSnapshot.BattlerNames` is therefore documented as a map keyed by battler slot for consumers
that already hold one - a popup, or a committed target. It must never be read out as a roster of
live enemies.

### 16.3 Status of this work

`BattleCapture.cs`, `BattleCaptureTests.cs` and this document only. 36 focused battle tests and the
603-test Native suite pass. No Core, Mod, feedback or presentation files were touched, nothing was
committed, built for packaging or deployed.

## 17. Final integration

Sections 14-16 record intermediate work, not earlier installed releases. Their test totals describe those review points. The integrated implementation is documented in [battle-feedback-native-audit.md](battle-feedback-native-audit.md).

Native capture returns raw names keyed by native slot. BattleSession adds a fixed A-H identity for enemy slots only when presenting a committed target or popup. These letters replace spatial sprite distinctions and never depend on inferred occupancy or a duplicate-name count; a lone enemy can have letter A. The lookup is never announced as a roster. A missing name falls back per target, preserving the remaining group's names. Applied status and renderer-backed group membership come from BattlePresentationCapture rather than the old single-target fallback. The HUD Status placeholder is decorated there.

Version 0.3.17 includes the battle runtime, foreground hotkeys, six native hooks, and navigation/footstep lifetime integration. Read the release evidence in the companion audit for final test/deployment status. Live battle timing and coverage remain unverified.
