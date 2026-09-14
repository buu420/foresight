# Save file list and submenu integration

Audited 2026-09-14 against the installed x86 EXE, SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Addresses below are RVAs, image base `0x400000`.

## Report and cause

The 2026-09-14 Reloaded log records a successful battle, followed by Inventory
`MenuUnsupported` at 09:35:24. The prior top-menu milestone intentionally stopped
at the submenu boundary. Save/Load confirmation hooks covered the prompt and
Yes/No but did not cover the file list. The user reports combat working.

## Native evidence

Existing Ghidra decompilation is in
`artifacts/research/resume-confirmation-0314/scene-savedload.txt` and
`resume-confirm.txt`. Current EXE-verified Capstone excerpts are in
`artifacts/research/field-submenus-0318/save-*.asm`. Claude's separate Ghidra audit
of Inventory, Equipment, Tech, and Formation is recorded in
`field-submenus-native-audit.md`.

`MenuNodeSaveLoadSteam` writes vtable `0x3A980C`. Its `open` at `0x218A20` takes
`this`, mode and a back-button flag, returns a byte in AL, and cleans eight stack
bytes at `0x218D7B`. Modes 0/4/5 are save, 1 load, 2 bookmark, 3 resume. Modes 2/3
open the existing confirmation immediately and are not treated as file browsing.

The list builder `0x219AC0` creates one card per normal file (20), saves their
pointers in the vector at node `+0x2F8/+0x2FC`, and binds zero-based row keys to a
manager stored at `+0x304`. It commits initial focus at `0x219E32`. The manager
vtable is `0x3A5D0C` and its committed key is `+0x2C4`.

Selection callback `0x219FA0` uses that key to call `0x218F40` (party preview) and
`0x218FE0` (metadata). The records at node `+0x2D0/+0x2D4` have stride `0x940`;
byte zero controls whether the occupied or empty card renderer runs at
`0x21BB38`. The load action also rejects an empty record at `0x21A103`.

The selected preview panel is node `+0x2E8`. `0x218F40` rebuilds its three party
rows and `0x218FE0` adds the selected record's metadata. The party row helper
`0x23AA40` writes the name using the Label factory at `0x23AB46`, then renders
LV/HP/MP through `0x239B10`, which also creates Labels. Metadata includes the
native era text, formatted time, and currency (`0x219423`, `0x219512`,
`0x219564`, `0x21961A`). Capture reads those finished visible Labels rather than
reformatting hidden save data. Empty records do not read a lingering preview.

Save caption is loaded bank `0x23`, message `0x26`; Load is bank `0x41`, message
`0x0A`. Confirmation flag `+0x2EC` suspends file speech. The existing confirmation
builder explicitly pauses the submenu session before producing its own speech;
returning to the file list presents the selected file again.

## Bounded rendered text

`RenderedNodeTextCapture` only visits an independently identified selected card,
control, or detail panel. Retail Node children `+0x160/+0x164`, parent `+0x16C`,
and visibility `+0x1AD` were previously audited in `WorldViewportCapture`.
The [Cocos Node header](https://raw.githubusercontent.com/cocos2d/cocos2d-x/v3/cocos/2d/CCNode.h)
cross-checks scene-graph semantics; it is not the source of retail offsets.

Each Label must expose the already audited LabelProtocol getter bytes
`8D 41 28 C3` through secondary interface `+0x278`, slot `+8`; its returned MSVC
string is at Label `+0x2A0`. Non-Label nodes are not cast to Label. Visits are
bounded to 512 nodes and depth 16, hidden subtrees are omitted, children must
still belong to their parent, and every child-vector and visibility is rechecked.
The save capture also rechecks its owner, mode, selection, card/record vectors,
occupied flag, preview pointer, and ancestry of each selected panel. An unreadable
selection is retried for 500 ms to allow native redraws, then reported audibly
once. A subsequent valid selection recovers automatically.

## Classic Tech detail panels

The Tech row manager is separate from the base MenuNodeBase stack: builder
`1CDD10` stores it at node `+308` at `1CDE0C` and attaches it at `1CDE95`.
Its disabled byte `+290` decides whether row selection is active. The reader
uses the base stack for other controls only when this row manager is disabled;
a failed active-row capture cannot fall back to a stale character selector.

Category `+2FC` and row `+300` select a 12-byte record from the three vectors
at `+318 + category * 12`. Selection callback `1CE080` commits `+300` at
`1CE0FC`, then calls `1CE110`. The row manager focus `+2C4` must equal that row.
Its map pointer `+294`, sentinel `map+4`, entry key `+8`, value `+C`, and
FocusableState control `+14` identify the one rendered row to read.

`1CE110` renders the description through `B9760` and `1CA4D0` into Label
`+33C`; component Tech names use `B96D0` and Label `+340`. Record bytes 2..4
are component Tech IDs, not party member IDs. `1CE350` replaces panel `+344`,
attaches it at `1CE702`, and reads the selected record at `1CE708..1CE725`.
Its rendered Labels include the displayed MP and requirements information
(`1CE877`, `1CE904`, `1CE98A`, `1CEA32`, `1CEAD8`). Capture reads those
finished Labels; it does not translate raw IDs into hidden names or requirements.
Unlearned rows retain the game's rendered question marks.

All detail panels must be visible descendants of the same Tech node. Before and
after capture, the reader checks row focus, category, vectors, panel pointers,
and all 12 selected record bytes. EXE-verified disassembly is retained in
`artifacts/research/field-submenus-0318/root-tech-panels.asm`.

## New ABI boundaries

| Boundary | RVA | ABI |
| --- | --- | --- |
| Classic submenu replacement | `2A5270` | thiscall(scene, node), void, ret 4; scene selected node at +290 |
| Touch submenu replacement | `2BD050` | thiscall(scene, node), void, ret 4; scene selected node at +294 |
| Save list open | `218A20` | thiscall(node, mode, showBack), byte, ret 8 |
| Manager callback completed | `1DD4C0` | thiscall(manager, action, key), void, ret 8 |
| Inventory item details | `1C7610` | thiscall(node, encodedItem), void, ret 4 |
| Save selected details | `218FE0` | thiscall(node, record), void, ret 4 |
| Active manager update | `1DCF10` | thiscall(manager, floatDelta), void, ret 4; forwards the raw float stack word |

Manager vtable slot `+0x1E8` points to `1DCF10`. It calls the input handler at
`1DD07A` and returns with `ret 4` at `1DD092`. Arrow focus at `1DD17F` commits via
`1DD3E0`, then invokes the closure directly at `1DD199`, bypassing `1DD4C0`.
The update hook therefore samples only the submenu's own manager, at most every
80 ms after the complete native update, and deduplicates unchanged text.

Hooks forward each original once, preserving arguments and return value. No
input is synthesized. The native save complete destructor `218860` already has
a hook; it now also clears the file-reader owner, without installing a second
hook on the same function. Scene replacement invalidates old ownership before
native removal. The top-menu unsupported message is suppressed only when the
new submenu reader actually owns the child.

## Validation and limits

Regression fixtures cover occupied/empty files, record bounds, selection changes
during capture, hidden or reparented text, malformed vectors, submenu transitions,
confirmation ownership, speech recovery, and exact native argument forwarding.
They do not replace an in-game keyboard and screen-reader test. Save and submenu
runtime results remain unverified until the user tests the installed build.
