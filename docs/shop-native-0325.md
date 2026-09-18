# Steam shop reader, 0.3.25

The shop previously had no accessibility observer. This reader follows the actual
ShopSteamScene selection: Buy/Sell/Equip, the selected item and price, stock and
equipped count, item help, character equipment comparisons, the quantity and total,
available funds, and the Equipment page opened within the shop. It reads one
selection at a time. The game still performs every action through ordinary input.

## Evidence and ownership

Executable SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
All addresses below are RVAs. Native instructions were examined with Ghidra 12.1.2
and checked against x86 disassembly. Research files and read-only process captures
are under `artifacts/research/shop-enemies-0325/shop/`.

The game's shop overview is also documented in the original Square manual
([manual scan](https://bdjogos.com.br/manuais/210-chrono-trigger-super-nintendo-manual-usa.pdf)).
The PC implementation and the user's live screen determine this reader's layout;
the older manual is background, not an offset or selection source.

| Native component | Evidence |
| --- | --- |
| ShopSteamScene | RTTI vtable `3B3C40`, allocator `2B4A00`, size `2C0` |
| Scene update | `2B4DD0`; thiscall, float delta stack word, `ret 4` |
| Deleting destructor | `2B4AD0`; thiscall, flag stack word, returns the original `this` in EAX, `ret 4` |
| ChooseActionNode | vtable `3AA414`, builder `224520` |
| ChooseItemNode | vtable `3AAC6C`, constructor `2285F0`, builder `228770` |
| ChooseCountNode | vtable `3AA860`, initializer `225500`, Steam builder `226B50` |
| InfoNode | vtable `3AB994`, builder `22BD80` |
| Character comparison manager/card | vtables `3AB144` / `3AB700` |

Scene bytes `+298` and `+299` select action, item or quantity mode in `2B4DD0`.
Their corresponding owned nodes are `+2B0`, `+2AC` and `+2B4`. The quantity
pointer remains stale after cancellation; it is read only in quantity mode.
The Equipment flag is `+29A`. `2B63C0` creates an EquipSteam node through `204E70`
and adds it directly to the scene, retaining it in a callback rather than a scene
field. The reader requires exactly one visible direct child with the audited
EquipSteam vtable, then uses the existing Equipment capture. The hidden shop
funds panel is not read during Equipment.

Each page has the existing audited MenuNodeBase manager stack at `+2C0`. Its top
manager must have the correct vtable and be enabled (`+290 == 0`). The focused key
at `+2C4` is resolved in that manager's own map at `+294`; FocusableState `+14`
identifies the control. Every parent through the independently identified page
must be visible. Reading a list container's entire contents cannot select a row.

## Item, quantity and comparison data

`229F00` constructs Steam item rows. `ChooseItemNode +2D4` points to four vectors;
`+2EC` selects the group and `+2F4` is the cursor. Records are twelve bytes:
encoded item ID, price and owned quantity. `+2D8` is Buy=0 / Sell=1. The row must
agree with the focus key and with the list manager at `[page+2F0]+280`. The name
and price come from the selected control's rendered labels; the price must agree
with the native record. Help at `+2CC` and the separately owned InfoNode at `+2E4`
are read only for that stable selection. `229AB0` updates those details.

`2292E0` builds an empty list with one `nsStateMachine::State` (`3ABC2C`) at key
zero, rather than a FocusableState. The empty vector, active manager and state
class must all agree. The exact caption table at `3AAC48` chooses bank `23`,
messages `72..75` for Buy or `76..79` for Sell.

For quantity, `+2D8` is the transaction mode, `+2DC` the unit price and `+2E8` the
amount. Rendered quantity and total labels are `+2CC` and `+2D0`; both must match
the amount and the multiplication of amount by unit price. The Steam builder
adds the name, quantity, total and currency labels before optional Attack/Defense
details. These are given explicit spoken roles. The quantity screen is the
transaction confirmation; the reader never presses Confirm.

`scene+29C` owns the character comparison manager, whose `+288/+28C` vector holds
the cards. Card `+298` is the character ID; `-1` is an empty slot. Names use the
existing loaded party-name strings at `canvas+1908+id*24`. `22D690` determines
equipment compatibility and calls `22CEF0`: Steam's static pose (`card+288`)
means incompatible; its animated pose means compatible. The alternate layout's
`+29C` overlay is null in Steam and is not used as a Steam compatibility flag.

The Attack/Defense number nodes are `+2A4/+2A8`. `2326E0` compares a proposed
value with the node's original value at `+278`, colours the displayed label,
and updates its text. The reader speaks that rendered number and increased or
decreased, where applicable. It does not calculate hypothetical equipment stats.

## Runtime and verification

Two byte-verified hooks observe scene update and destruction. Updates are sampled
after the original function, at most once per 80 ms. The destructor retires the
speech owner before freeing memory, and preserves the original arguments and
return value. Existing menu events suspend navigation. Reading errors are retried
across redraws; a persistent failure is spoken after 500 ms, using the existing
submenu session behaviour. No process writes or synthesized input are added.

The committed fixture contains only bytes consumed by the reader from four
read-only snapshots on September 18, 2026: action menu, Buy item list, Sell item
list and quantity. Regression tests check those real layouts, ASLR identity,
detached/disabled controls, cursor disagreement, stale totals and quantity
pointers, native comparison indicators, and changes during capture. Empty Sell
and shop Equipment ownership tests are explicitly synthetic, using audited native
contracts and the earlier real Equipment fixture. They are not claims of a live
purchase, sale, or complete playthrough.
