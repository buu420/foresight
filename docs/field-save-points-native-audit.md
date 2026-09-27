# Field save points: native evidence

User report (2026-09-26 log, scene 29 Prison Towers, Guardroom, story point 46): no save point
was listed, although the player reached one at (3573, 2776) on layer 1 and saved through the
menu. The log shows the save point's actors, sparkle actor 3 (class 4, visual 0x79) and checker
actor 4 (class 7), at (3456, 2815). The sparkle was filtered out as an object because it has no
confirm action, and the checker has no catalog actor entry. Its loop is recorded only as
story-only Switch regions.

All RVAs refer to the supported executable (`8FE9D75E…2E00D7`, image base 0x400000). Evidence
came from the local Ghidra export (`artifacts/research/engine-0332/native`), the installed field
scripts read through the repository's verified decoder, and CTViewer (GitExl, commit `2e5a206`)
for opcode names, each checked against the native handler.

## What enables saving

- CanSave 0x213580 allows a field save only while FieldState+0x73C bit 7 is set. For world
  scenes 0x1F0–0x1F7 it applies a separate status test instead. The top-menu builder 0x1D0FE0
  (Save option) and 0xB7C90 run the same test.
- FieldState is the script global array. 0x18753E stores engine+0x850 = ActorBase+0x110B0, the
  same base the opcode 16 handler reads globals from (index × 4). 0xB1BF9 gives the object at
  ActorBase+0x28 that same pointer, and CanSave dereferences it. F+0x73C is therefore global
  0x1CF. This matches the ROM-hacking convention that `$7F01CF` bit 0x80 permits saving.
- Global 0x1CF bit 0x80 has exactly one kind of writer in all 661 scene scripts: opcodes 65/66
  with operand `87 CF`. Handler 0x168AD0 computes `1 << (operand & 0xF)`, and bit 0x80 of the
  operand adds 0x100 to the address. No 16-bit global store (56/58) targets 0x1CF.

## The checker loop (57 checkers in 54 scenes)

Each checker places itself on one tile (`8B x y`) and loops:

1. `22 00 lx ly`, via stub 0x16206E → 0x165E20, copies the leader's native tile into locals. It
   reads party slot F+0x11EC, then actor +0x80 (tile X) and +0x8C (tile Y). Tile X is FineX >> 8
   and tile Y is the foot's FineY >> 8.
2. `12` compares both locals with the checker's own tile.
3. On the tile, `16 cf 80 86` tests the bit:
   - **First frame:** `E8 0B` (save-point sound), then `65 87 CF` sets the bit.
   - **Later frames:** `31` (stub 0x1621CA → 0x166360) tests engine+0xBC1. That byte is latched
     at 0x189339 from +0xBBF, which 0x18924B takes from the input byte's bit 7, the SNES A
     button, which the PC port maps to Confirm. `C8 40` then opens the Save UI.
4. Anywhere else, `66 87 CF` clears the bit.

Every checker has a person 0x79 sparkle on the same tile. In 50 of the 57 the checker is itself
a sparkle; in the other seven the sparkle is a separate actor in the slot just below the checker.
Script runner 0x161560 treats that sprite specially too. It records the tile of any actor whose
visual (+0x44) is 0x79. When an actor then executes opcode 31 on that tile, the runner sets its
+0x150/+0x152 and registers it through 0x1760B0, which briefly makes it a native activation
candidate.

Ten person-0x79 actors have no CanSave setter, for example Truce Mayor's House 1F (4, 24) and
four in Magus's Keep. They cannot enable saving, so they are not save points and are not offered.

## Visibility and usability that the mod preserves

- **Hidden sparkle.** Opcodes 90/91 show and hide the sprite (+0xD0). Scene 29's sparkle hides
  before story point 0x2E while its checker still runs, so a save point is offered only while
  its sparkle is drawn.
- **Checker processing.** Opcodes 0B and 0C (stubs 0x161C0B / 0x161C61) set and clear +0x30 bit
  0x80. While it is set, the runner stops the checker's loop (0x164FA0 checks the same bit), so
  saving is not enabled. Five checkers are toggled this way:
  - Prison Passage;
  - Tyranno Lair;
  - two in the Black Omen;
  - Zeal Palace.
- **Call gate only.** Opcodes 08 and 09 set +0xE8, which blocks only confirm and touch calls into
  the actor. The save test runs inline in the checker's own loop, so this gate is not a
  save-point condition. A new snapshot field, `ScriptProcessingEnabled`, reads only +0x30 bit
  0x80.
- **Removed checker.** Opcode 0A sets class bit 0x80, and the checker is then treated as gone.
- **Script gates.** Nine checkers (eleven regions) carry their own conditions:
  - a Giant's Claw local;
  - Slash defeated;
  - Ozzie's room from story point 137;
  - the one active pit-trap save point in Magus's Keep;
  - Lavos Interior flags;
  - an Arena of the Ages local.

  These are evaluated as the catalog records them. The catalog's "bit not yet set" guard only
  separates the first frame from the Confirm frames, so it is ignored. Otherwise the save point
  would disappear while the player stands on it.
- **Solidity.** Opcode 84 writes +0xD8 (stub 0x162ACB), the snapshot's `LoadedFlag`, whose bit 0
  is solidity. Combined sparkles run `84 00`. Separate sparkles sit one slot below an unloaded
  checker, which is the first contact in 178980's descending scan. The save tile is therefore
  walkable in the existing collision model, and no special contact rule is added.

## Navigation behaviour

`FieldSavePoints` takes each catalog Switch region that sets global 0x1CF bit 0x80 on a single
tile. It is a save point only while all of these hold:

- its checker is present, not removed, processing its script, and on that tile;
- the region's own gates hold;
- a drawn person 0x79 sprite stands on the tile.

`FieldNavigationSource` then adds it as an Interactable Object:

- **Label and position:** "Save point", at the tile centre.
- **Standing goals:** the tile's interior lattice nodes (offsets 64, 128 and 192). With the
  controller's one-eighth-tile arrival box, arrival always leaves the leader's native tile equal
  to the checker's.
- **Instructions:** "Stand on it and press Confirm to save.", and on arrival "Press Confirm to
  save."
- **Guide:** like other currently available field targets, it can be selected through the guide
  before the camera shows it.
- **No duplicates:** the sparkle and checker are not listed separately, even while the engine
  briefly flags the checker as interactable.
- **Diagnostics:** the navigation inventory line gains `savePoints=checker@x:y` for live
  verification.

## Validation and limits

`FieldSavePointTests` (13 cases) start from the user's live scene 29 frame, which produced no
save point before the change. They cover hidden sparkles, disabled, removed and moved checkers,
sparkles without a checker, story gates, standing on the tile, pre-discovery guide selection and
duplicate suppression. One all-scenes case builds all 57 catalog regions with their own gates
and requires exactly one save point each, with goals inside its tile. `FieldNavigationCaptureTests`
cover the new `ScriptProcessingEnabled` field. The research census, table and scripts are in
`artifacts/research/skywalk-savepoints-0334/claude/`.

Not established: live confirmation in scenes other than 29, and save points on the world map,
where saving needs no point. Save points in special movement modes are not audited separately.
