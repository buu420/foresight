# Classic Party (Formation): focus key 999, roster records, cards and combos

Read-only audit. Executable SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`; all addresses are RVAs against
image base `0x400000`.

Sources: the hash-verified Ghidra decompilations in `artifacts/research/party-0321/` —
`formation-ghidra.txt` (`0x1BFBB0`, `0x23CD80`, `0x1BE730`), `formation-panels-ghidra.txt`
(`0x1BE850`, `0x1C04B0`, `0x1BE1D0`, `0x1BDB20`), `formation-roster-ghidra.txt` (`0x1BEAC0`,
`0x1BF200`) and `formation-fields-ghidra.txt` (`0x1BF330`, `0x1BF480`, `0x1C09E0`, `0x1C0720`), all
produced by Codex — plus my Capstone reads, the live snapshots
`live-20260914-172136.json` / `live-20260914-172613.json`, and the live getter probe
`children-getter-live.json`.

**Two claims from the first revision of this document are withdrawn; see §5 and §4.**

## 1. Focus key 999 is "the entry this page would focus is locked"

Unchanged and still correct. The tail of `0x1BFBB0`:

```c
cVar4 = *(char *)(*(int *)(node + 0x2CC) + 5 + *(int *)(node + 0x2FC) * 8);
if (cVar4 == '\0') { local_58 = param_2; }
else { uVar8 = FUN_005bdb20(); FUN_00593690(…, &DAT_00799b4c, uVar8); local_58 = (int *)0x3e7; }
*(undefined4 *)(node + 0x334) = 0xFFFFFFFF;
```

`DAT_00799B4C` is a literal **999** at RVA `0x399B4C`; `0x1BDB20` builds a plain
`nsStateMachine::State` (vtable RVA `0x3ABC2C`, 71 bytes), which has no `+0x14` control. The live map
entry for 999 accordingly has no control.

999 is a **synthetic parking key**, installed when the active-roster record at index `[node+0x2FC]`
has its story-lock byte set. It is not an overview mode and not an idle state. A reader must treat it
as a valid state with no focused control; falling back to key 0 would be wrong, because that button
is deliberately disabled in exactly this situation.

## 2. Roster records — character ID now proven

`0x1BF330` builds the records and settles the layout. It walks the party flag array at
`[DAT_0081B4C4 + 0x28] + 0x219C`, taking indices 0-2 for the current party and 3-8 for the reserve,
and for each builds an 8-byte record on the stack before pushing it via `FUN_005C0C60`:

```c
local_18 = *(int *)(… + 0x219C + iVar4 * 4);        // the character ID
local_14 = 1;   /* current party */                  // …or 0 in the reserve loop
local_13 = (byte)(*(int *)(… + (0xEFF - local_18 >> 3) * 4) >> ((byte)(0xEFF - local_18) & 7)) & 1;
FUN_005c0c60(&local_18);
```

| Record field | Meaning | Evidence |
| --- | --- | --- |
| `+0` dword | **character ID** | `local_18` from the party flag array, `0x1BF330` |
| `+4` byte | **1 = current party, 0 = reserve** | `local_14 = 1` / `= 0` in the two loops |
| `+5` byte | **story lock**, a single extracted bit | `local_13`, masked `& 1` |
| `+6`, `+7` | padding | not written |

The first revision listed bytes 0-3 as unidentified. That is corrected.

The loop bounds are also now proven: **at most 3 current** members (flag indices 0-2) and **at most 6
reserve** (indices 3-8). Because `local_13` is masked with `& 1`, the lock byte is strictly 0 or 1.

Vector layout from the constructor `0x1BE730` is unchanged: two 8-byte-record vectors at
`node + 0x2CC` and `+0x2D8`, two button vectors at `+0x2E4` and `+0x2F0`, and `+0x2FC`/`+0x300` as a
two-element index array (`0x1C1106  mov eax, [eax + edx*4 + 0x2FC]`).

## 3. Focus keys are `base + index`, base 0 active / 10 reserve

Unchanged. `local_58 = *(int *)((int)&PTR_007a325c + iVar11) + local_60`, with the dwords at
`0x3A325C` = **0** and `0x3A3264` = **10**. `0x1BF480` corroborates the split independently: it
branches on `iVar8 < 10` to choose between two helpers for the held key.

## 4. Card mapping — `+0x160` is now proven, not inferred

The first revision flagged "`vt[0x120]` ≡ children-at-`+0x160`" as inference. **That caveat is
withdrawn.** The live probe read the actual getter bytes:

```
8D 81 60 01 00 00 C3     lea eax, [ecx + 0x160] ; ret
```

recorded in `children-getter-live.json` for container `0x1E612F20`. The active card container's
children really are at `+0x160`.

* active member `i`: `card = children([node + 0x308])[i]`
* reserve member `i`: `card = ([node + 0x30C])[i]` — `0x1BEAC0` builds `+0x30C`/`+0x310` as a vector
  directly (`local_74 = in_ECX + 0x30c; *(… + 0x310) = *(… + 0x30c)`), so it is not a children list.

`0x1BEAC0` also assigns `+0x308` (its line 95 in the decompilation).

**Locked indicator.** `record[5] != 0` causes, in `0x1BFBB0`: `state + 0x11 = 1`, button
`vt[0x2B8](0)`, and card tint `DAT_0081F7E4`. `FocusableState`'s constructor writes a *word* 1 at
`+0x10`, so `+0x11` is that word's high byte and is 0 until this loop sets it. Reading one byte at
`state + 0x11` is the cheapest check and needs no virtual call.

**Held and highlighted.** `node + 0x334` is the picked-up slot and `node + 0x338` is the **current
highlighted** key — the first revision called `+0x338` merely "a companion value", which is
corrected. `0x1BEAC0` assigns it (`*(undefined4 *)(in_ECX + 0x338) = param_1`) and `0x1BF480` reads
both:

```c
iVar4 = *(int *)(in_ECX + 0x338);
iVar8 = *(int *)(in_ECX + 0x334);
if (((iVar8 != iVar4) && (-1 < iVar8)) && (-1 < iVar4)) { … preview … }
```

The combo builder handles negative held/highlighted values by omitting the swap preview.
The reader allows the no-highlight sentinel `-1`; this case is tested synthetically, not observed
live. This guard alone does not prove a player can reach that sentinel. `0x1C09E0` animates the
held/current pair. The separately decompiled `0x1C0530` creates the finger indicator at `+0x33C`
and sets both `+0x334` and `+0x338` to the picked-up key; its negative branch removes the finger
and clears `+0x334` to `-1`.

## 5. Withdrawn: "no combo panel exists"

The first revision stated that the Formation range contains no combo panel and no other bank-`0x23`
lookup. **Both statements are wrong.** `0x1BEAC0` loads bank `0x23` index **`0x83`** —
"Usable Combos" — and builds the main panel `+0x304`, the active card container `+0x308` and the
reserve card vector `+0x30C`. `0x1BF480` owns the combo panel at **`node + 0x318`**: it clears the
panel through `vt[0x148]` and repopulates it from the held (`+0x334`) and highlighted (`+0x338`)
keys, previewing the combos the swap would produce.

My earlier sweep looked only for literal bank-`0x23` immediates in a narrow address window and
missed `0x1BEAC0` entirely. The error is the same class as the earlier "no map in range" mistake:
a range-scoped negative treated as proof.

`0x1BE850` loads bank `0x23` index **`0x95`** = "Please select party members.", and sets `+0x304`
and `+0x318` (`*(Node **)(in_ECX + 0x304)`, `*(Node **)(in_ECX + 0x318)`). Index **`0x25`** =
"Party" remains the page caption. All three strings are confirmed against the live `textBank23`.

| Field | Owner | Meaning |
| --- | --- | --- |
| `node + 0x304` | `0x1BE850` | main party panel |
| `node + 0x308` | `0x1BEAC0` | active card container; cards are its children at `+0x160` |
| `node + 0x30C`/`+0x310` | `0x1BEAC0` | reserve card **vector** |
| `node + 0x318` | `0x1BE850`, populated by `0x1BF480` | **usable-combos panel** |

## 6. What the live capture does and does not establish

The only live frames are `live-20260914-172136.json` and `live-20260914-172613.json`: **one locked
current member, no reserves, focus parked at 999, `+0x334 = -1`, `+0x338 = 0`**.

Runtime-verified by those frames: the node class and manager, the 999 parking state and its absence
of a control, the single 8-byte roster record, the single active button, the empty reserve vectors,
the `+0x304`/`+0x308`/`+0x318` panel pointers with `+0x30C` null, and the `+0x160` children getter.

**Not runtime-verified, and must not be described as such:** a selected (non-parked) member, any
reserve member, any held/swap state, any multi-member roster, and the populated combo panel. Those
paths are covered only by synthetic tests built from the decompiled structure.

## 7. Review resolutions and final reader behavior

Additional root research is retained in `formation-card-builders-ghidra.txt` and
`combo-row-capstone.txt`. These resolve the review's remaining structural concerns:

- **Three current-party card slots are proven.** `0x23B210` unconditionally creates/adds a card
  in a loop with index 0 through 2, returning when `2 < iVar8`. It fills text only for present
  members. `0x1BEAC0` additionally tints/clears the unused slots. The reader requires three slots
  but reads only the number of present roster records, so blank slots are never members.
- **Reserve length is an element count.** `0x1BEAC0` resets end `+0x310` to begin `+0x30C`, then
  creates one card per present reserve member, adds it to main panel `+0x304` and appends its pointer
  to the vector. Capacity is the separate `+0x314` field. The records at `+0x2D8/+0x2DC` enumerate
  the same present reserve slots in `0x1BF330`. Matching their lengths and requiring ancestry to
  `+0x304` follows both loops; no capacity is interpreted as a count.
- **The page visibility check is intentional.** `HasAncestor(node, node)` first reads the node's
  visible byte before accepting self-ownership. It is not an unconditional true result.
- **Held lookup cannot have duplicates.** The bounded groups produce distinct keys 0..2 and
  10..15, and membership is established before looking up the held card.
- **A populated combo panel can have no ability names.** `0x15B3B0` is a returning vector append
  of a 12-byte character combination. Ghidra's unreachable-block warnings in `0x1BF480` omit
  real instructions: `0x1BF6E9` calls row builder `0x1BF750` once per combination. That builder
  adds a background (`0x1BF8EF`) and character portraits (`0x1BF9D0`) before checking abilities.
  Matching abilities alone produce name Labels (`0x1BFAD0` name lookup, `0x1BFB3E` Label factory,
  `0x1BFB4A` addChild). Thus a successfully read panel with zero name Labels means no usable
  combos, even when background/portrait children remain. A recognized but unreadable Label
  rejects the capture. The rendered names are read from this owned panel, not from a global
  technique catalogue.

The final capture validates the native node class, active manager, map state classes, button/card
ownership, record side/lock bytes, and current/reserve vector counts. The manager, focus, held and
highlighted keys, vector bounds, IDs, lock flags, card pointers and control mappings are read again
after the text capture; changing frames are discarded. No manager key is synthesized in the game.

When the native focus is 999, speech describes the instruction and visible current/reserve roster
without claiming a focused member. Otherwise it reads the corresponding card, side and position,
lock status, picked-up/moving indicator, and displayed combo names. Names and stats come from the
game's rendered cards; captions/instructions come from their proven native message IDs.

The exact live state replay first failed before implementation and now reads:
`Please select party members. Current party. Crono LV 1. HP 43/70. MP 8/8. Locked. Reserve. Empty. Usable Combos. None.`
Selected, reserve, held, preview-sentinel and nonempty combo cases remain synthetic checks.

## 8. Remaining unknowns

* Record bytes 6-7 are padding by omission, not by proof.
* The dwords `4` and `3` at `0x3A3260`/`0x3A3268` are still unexplained; the key-base loop does not
  read them.
* `0x1BE1D0` and `0x23B790` (the reserve and active button factories) were decompiled but I did not
  analyse whether a member name is reachable from a button without its card.
* Multi-member and reserve rendering remain unobserved live (§6).
