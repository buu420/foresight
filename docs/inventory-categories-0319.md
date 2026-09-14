# Classic Inventory: category keys and the empty-list caption

Read-only static analysis. Executable SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`, image base `0x400000`; all
addresses are RVAs. Cross-checked against the live snapshot
`artifacts/research/submenus-0319/live-20260914-112737.json` (pid 55128, image base `0xD00000`,
node `0x274A5898`, `ClassicMenuNodeItem`).

Claude's pass used Capstone. Codex subsequently located the existing Ghidra installation at
`C:\Users\buu42\Tools\Ghidra\ghidra_12.1.2_PUBLIC` and ran its headless analyzer against the
read-only `ct_world` project. The hash-verified decompilation of the complete callback at
`0x1C5B20`, caption updater `0x1C55D0`, and list builder `0x1C3080` is retained in
`artifacts/research/submenus-0319/inventory-ghidra.txt`.

## 1. Category captions: bank `0x23`, index `0x40 + committed category`

`0x1C55D0` is the caption refresh, `this` = the node, arg0 = a show/hide flag:

```
0x1C55FD  cmp  byte ptr [ebp + 8], 0
0x1C5601  je   0x1C5721                 ; hide branch
0x1C5607  mov  eax, dword ptr [ebx + 0x2F0]   ; the committed category
0x1C5613  add  eax, 0x40
0x1C5616  push eax ; 0x1C5617 push 0x23 ; 0x1C561C push out
0x1C561D  call 0x1B9060                  ; TextManager::get(out, bank 0x23, 0x40 + category)
0x1C5629  mov  ecx, dword ptr [ebx + 0x2F4]   ; the caption Label
0x1C562F  add  ecx, 0x278                     ; its LabelProtocol
0x1C5638  call dword ptr [edx + 4]            ; setString
```

So the caption index is **computed**, not literal: `0x40 + [node + 0x2F0]`, covering `0x40`-`0x45`
for categories 0-5. The live snapshot agrees exactly — `+0x2F0 = 0` → index `0x40`, and the Label at
`+0x2F4` renders `Consumables`.

`node + 0x2F0` is therefore the **committed category** and `node + 0x2F4` is the caption Label.

Callers of `0x1C55D0`: `0x1C5CA2`, `0x1C5CF1`, `0x1C5DE1`, `0x1C6378`.

Other bank-`0x23` lookups inside classic Inventory use *literal* indices and are unrelated static
headers, not category names: `0x40` at `0x1C33F0`, `0x46` at `0x1C3689`, `0x51`/`0x52` at
`0x1C35B1`/`0x1C3592` (all inside the one-time builder `0x1C3270`), and `0x4F` at `0x1C6B65`. The
literal `0x40` at `0x1C33F0` is a build-time header; it is not the live category caption.

## 2. Focus key to category: `category = key - 2`, keys 2 through 7

The focus-changed callback from `0x1C5D79` reads the key into `ecx` from `[ebp + 0xC]`:

```
0x1C5D8B  cmp ecx, 8
0x1C5D8E  jl  0x1C5DBC                  ; key < 8 -> header control
          ; --- key >= 8: an item row ---
0x1C5D92  mov dword ptr [eax + 0x2F8], 0xFFFFFFFF
0x1C5D9C  lea eax, [ecx - 8]             ; row = key - 8
0x1C5DA2  call 0x1C6620                  ; set the row cursor
0x1C5DAF  call 0x1C5890                  ; scroll to that row
          ; --- key < 8 ---
0x1C5DBC  cmp ecx, 2
0x1C5DBF  jl  0x1C5DE6                   ; keys 0 and 1: no category commit at all
0x1C5DC3  add ecx, -2                    ; category = key - 2
0x1C5DC6  cmp ecx, dword ptr [eax + 0x2F0]
0x1C5DCC  je  0x1C5DE6                   ; unchanged
0x1C5DCE  mov dword ptr [eax + 0x2F0], ecx   ; COMMIT
0x1C5DD8  call 0x1C3080(node, 1)             ; rebuild the row list
0x1C5DE1  call 0x1C55D0(node, 1)             ; refresh the caption
```

Verified: **keys 2-7 map to categories 0-5**, exactly as proposed. Keys 0 and 1 fall through the
`jl 0x1C5DE6` with no commit — consistent with the 0.3.18 log speaking `Use / Switch` before it
began failing; they are the two non-category header buttons. Keys 8 and above are item rows with
`row = key - 8`.

Live cross-check: manager focus `2`, `[node + 0x2F0] = 0`. `2 - 2 = 0`. ✔

## 3. Focus and committed category cannot diverge on a category icon

The commit at `0x1C5DCE` happens **inside the focus-changed callback**, not on confirm, and is
immediately followed by the list rebuild and the caption refresh. There is no separate confirm step
that promotes a hovered icon to the committed category.

Consequences for wording, stated precisely:

* **Focus key in 2-7** — the focused icon *is* the current category. `0x40 + (key - 2)` and
  `0x40 + [node + 0x2F0]` are the same value. Naming the focused icon is truthful.
* **Focus key 0 or 1** — the player is on a header button, and `[node + 0x2F0]` still holds the
  category of the list on screen. It is truthful to call that "the current category"; it is **not**
  truthful to call it "the focused item".
* **Focus key ≥ 8** — the player is on a row; `[node + 0x2F0]` is the category that row belongs to.

The only divergence window is the few instructions between the key arriving and `0x1C5DCE`, which a
reader cannot observe.

## 4. The empty-list messages are not rendered by the classic menu

I enumerated **every** call to `0x1B9060`/`0x1B9110` in `.text` and classified how each index is
formed (literal push versus computed). Across the whole image there is exactly one site whose index
is `0x48 + something` against bank `0x23`:

```
0x20E790   ; this = ecx, arg0 = [ebp+8] parent node, arg1 = [ebp+0xC]
0x20E7C0   mov  eax, dword ptr [ebp + 0xC]
0x20E7C6   add  eax, 0x48
0x20E7C9   mov  ecx, dword ptr [0x81C3D8]
0x20E7CF   push eax ; 0x20E7D0 push 0x23 ; 0x20E7D8 push out
0x20E7D9   call 0x1B9060
0x20E7E5   mov  dword ptr [ebp - 0x68], 0x439D0000   ; fixed x
0x20E7EC   mov  dword ptr [ebp - 0x64], 0xC2DA0000   ; fixed y
0x20E807   call 0x6400B0                              ; build the Label
```

That function lives in the **touch/modern `MenuNodeItem`** range (its deleting destructor is
`0x20DAA0`), and it has exactly one caller, `0x20E75B`, also in that range.

The classic list rebuild is `0x1C3080`, called only from `0x1C2E5E` (constructor), `0x1C5C99`,
`0x1C5CE8`, `0x1C5DD8` and `0x1C636F` — all classic Inventory. **None of them reaches `0x20E790`.**
No literal index in `0x48`-`0x4D` appears anywhere in `.text` either.

**Conclusion: the classic Inventory page the user is playing never renders the bank `0x23`
`0x48`-`0x4D` strings.** They exist and are correct text, but they belong to the touch interface. An
empty classic category shows a blank/padded list, not a sentence.

### Recommendation

Do not speak `"The party has no consumable items."` on the classic page. It would claim that
on-screen text exists when it does not, which is the failure mode this project treats as worse than
silence in the opposite direction — it is an invented reading.

Derive emptiness from the row data instead, which the live snapshot already demonstrates. The
builder pads an empty category to one record: rows `[0x2D0, 0x2D4)` spanned 12 bytes — a single
record `[0, 0, 1773730048]`, encoded id `0` and quantity `0`. A later Ghidra check corrected the
earlier attribution: `0x1C7FCD` rejects item use when quantity is not positive; it is not a drawing
rule. The builder `0x1C3080` zeros each record's first eight bytes, copies real records only when
their quantity is positive, and keeps at least one record. See the
[empty-row audit](inventory-empty-row-0320-native-audit.md).

The implemented empty-category rule is deliberately narrower than a scan of nonpositive rows:
exactly one record with quantity zero, a valid category, and its matching visible heading.

A truthful phrasing is the category name plus `"Empty"` — for example `Consumables, empty` — sourced
entirely from `0x40 + [node+0x2F0]` and the verified placeholder. If the touch page is supported later, that page
*may* use `0x48 + category`, because there it genuinely is on screen.

## 5. Uncertainty

* The saved live snapshot contains the loaded strings as `textBank23`. Codex verified categories
  `0x40`-`0x45`: Consumables, Weapons, Helms, Armor, Accessories, and Key Items. The six empty
  messages at `0x48`-`0x4D` are also present, but the classic reader does not use those touch captions.
* The captured control at key 0 renders `Use / Switch`; key 1 renders `Sort list`. They are two
  controls, not separate Use and Switch buttons.
* `0x1C1B10` (category sizes) was **not** verified in this pass; I did not need it and do not assert
  it.
* Single-category evidence: the live snapshot only covers category 0 with an empty list. Behaviour
  for a populated category is inferred from the same code paths, not observed.
