# Classic Inventory: why the empty row reports help as visible

Read-only audit. Executable SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`; all addresses are RVAs against
image base `0x400000`.

Tooling: Ghidra 12.1.2 headless against the read-only `ct_world` project, using
`artifacts/research/inventory-0320/DecompClaude.java`. Claude's Git Bash wrapper stalled;
Codex ran the headless command directly from PowerShell, then Claude reviewed its output.
The decompiler verified the
executable hash itself (`SHA256=8fe9d75e…` in the output header). Raw output is in
`artifacts/research/inventory-0320/decomp.txt`. My earlier claim that Ghidra was unavailable was
wrong; it is installed at `C:\Users\buu42\Tools\Ghidra\ghidra_12.1.2_PUBLIC` and works.

Live cross-check: `artifacts/research/submenus-0319/live-20260914-152905.json`, image base
`0xC70000`, `ClassicMenuNodeItem` node `0x1D6A9C30`, focus key 8, cursor 0, category 0, held `-1`,
one row record `[0, 0, 1773730048]`.

## 1. The help updater tests the encoded id as **signed**

`0x1C7610` decompiles to exactly two branches:

```c
void FUN_005c7610(uint param_1)            // ECX = node, param_1 = encoded item id
{
  if ((int)param_1 < 0) {                  // 0x1C763E test edx,edx / 0x1C7640 jns
    FUN_0040f670(&DAT_0079d034, 0);        // the empty literal
    FUN_0062f160(local_30, 0);             // update help with empty text
    *(undefined1 *)(in_ECX + 0x2fc) = 0;   // 0x1C7691
  }
  else {
    FUN_004b7fc0(local_48,
      (param_1 & 0xfff) + *(int *)(&DAT_0079906c + ((int)param_1 >> 0xc) * 4));  // bank 0x1D
    FUN_0062f160(local_48, 0);             // update help with resolved text
    *(undefined1 *)(in_ECX + 0x2fc) = 1;   // 0x1C76D1
  }
}
```

The "no item" branch requires the id to be **negative**, not zero. The only callers that reach it
push a literal `-1`: `0x1C5D50` and `0x1C5DEF` (`push -1 ; call 0x1C7610`), plus the reset at
`0x1C75B8`.

## 2. Therefore `+0x2FC == 1` on the blank row is correct native behaviour

The padded placeholder record carries encoded id **0**. Zero is not negative, so `0x1C7640` falls
through to the resolve branch, which computes

```
index = (0 & 0xFFF) + GroupBase[0 >> 12] = 0 + GroupBase[0] = 0
```

looks up **bank `0x1D` line 0**, sets the Label to it, and writes `+0x2FC = 1`.

So the flag is 1 for the placeholder for the same reason it is 1 for every real item: the id was
non-negative. **It says nothing about whether a row exists.**

Live confirmation: `fields["0x2fc"] = 0x27996401`. The flag is a **byte** field — every native write
is `mov byte ptr [esi+0x2FC], imm` — and the surrounding dword holds unrelated neighbouring data. It
must be read one byte wide; a dword read yields a large non-zero value regardless of state.

## 3. The visible description is blank, not stale

It is a genuine lookup, not a leftover. In the live capture `textBank1D["0"] == ""` while 211 other
lines in that bank are non-empty, so line 0 is deliberate padding. The help panel is therefore
flagged visible while displaying an empty string.

Consequence: appending help text when `+0x2FC != 0` is harmless here (it appends nothing), but
inferring "an item is selected" from the flag is wrong.

## 4. Emptiness must be independent of the flag

`+0x2FC` records only which branch of `0x1C7610` last ran, which is driven by the sign of the
encoded id. It is 1 for the placeholder, 1 for every real item, and 0 only after an explicit `-1`.
It carries no emptiness information at all.

**Concern with removing only `helpVisible == 0` from the empty-row guard: none, and the removal is
required.** The guard is not merely unhelpful, it is inverted against reality — the one empty
category actually observed has `helpVisible == 1`, so requiring `== 0` rejects precisely the case
the guard was written to accept. That is the live failure: the exact replay returns null with no
missing reads because this single condition is false.

## 5. Quantity — and a correction to my previous audit

I previously wrote, in `inventory-categories-0319.md` §4, that `0x1C7FCD` is "the native renderer's
own rule … skips any row whose `record + 4` is not positive". **That attribution is wrong and I
withdraw it.** The Ghidra decompilation shows `0x1C7F80` is the item-use/confirm handler:

```c
void FUN_005c7f80(int param_1, int param_2, int param_3)
{
  puVar1 = (uint *)(*(int *)(in_ECX + 0x2d0) + param_2 * 0xc);        // the row record
  if ((*(int *)(*(int *)(in_ECX + 0x2d0) + 4 + param_2 * 0xc) < 1) ||
      (cVar2 = FUN_005c1c90(...), cVar2 == '\0')) {
    piVar7 = *(int **)(param_3 + 0x24);                               // reject callback
    ...
  } else { /* the use path: item attributes, party stats */ }
}
```

So `quantity < 1` gates **usability**, not drawing. That does not invalidate the field's meaning —
`record + 4` is still the quantity, and the game refuses to use a row with a non-positive one — but
it is weaker evidence than I claimed. The correct statement is that quantity ≤ 0 means "not a usable
row", proven at `0x1C7F80`; I have not located a draw-time skip.

**Quantity remains usable as the emptiness signal, and there is a second independent one.** In the
live capture the placeholder is zero in *both* fields, and encoded id 0 cannot name a real item:
`textBank1B["0"] == ""` while line 1 onward are `Wooden Sword`, `Bronze Blade`, `Steel Saber`,
`Silver Sword`. Group 0's base is 0, so id 0 is the only id that maps to the empty name.

Codex cross-checked the existing Ghidra output for the builder `0x1C3080` in
`artifacts/research/submenus-0319/inventory-ghidra.txt`: it initializes the first eight bytes of
each record to zero, copies a source record only when its quantity is positive, and resizes to at
least one record. This establishes the blank placeholder independently of the use handler.

Claude suggested additionally requiring ID zero and accepting any nonpositive quantity. That is
not the existing implementation. This repair retains its narrower quantity rule, exactly zero,
and its one-record and matching-visible-heading checks. It removes only `helpVisible == 0` from
the empty-row guard. Populated rows and help text keep their existing behavior.

## 6. Residual uncertainty

* Only one empty category (category 0, Consumables) has been observed live. The placeholder layout
  for the other five is inferred from the shared builder `0x1C3080`, not seen.
* I did not locate a draw-time row-skip rule; §5 states what is proven instead of asserting one.
* `bank 0x1D line 0` is empty *in this build's loaded data*. The reasoning above does not depend on
  it being empty — the flag is uninformative either way — but the "blank, not stale" wording does.
