# Field navigation native evidence

The user approved U/O categories, J/L destinations, K repeat, I guidance, and P
automatic walking on 2026-09-10. No game input, launch, or live debugger was used
for development. All RVAs below refer to the supported EXE hash in
`GameVersionCatalog`. Native research combined Ghidra, instruction-level checks,
and the primary [CTViewer source](https://github.com/GitExl/CTViewer). CTViewer's
emulated behavior was treated as a lead, not a substitute for the installed binary.

## Coordinates, state, and input

- Engine+40 is actor storage A; actors are A+6940+index*154. Native coordinate
  setters 16C040 and 16C2C0 establish X at 7C/80/84 and Y at 88/8C/90: fraction,
  tile, fixed point. One tile is 256 fixed-point units, or 16 pixels. The native
  Y coordinate is a foot position, retained without an invented sprite offset.
- Engine+850 points to field state F; engine+854 points to movement state M at
  A+1327C. Current scene F+1010 is mirrored by renderer[engine+B9C]+2A0. F+105C
  is a destination and must not identify the current scene.
- F+11EC/11F0/11F4 identify the three party actors in index*2 encoding; bit80
  means empty. 1760B0 excludes all three, actor zero, removed actors (class+40
  bit80), zero actor+20, and zero byte actor+152 from activation candidates.
- Actor+D0 is draw mode; actor+D8 is load state. Actor+40 is class, +44 the
  class-local visual index, and +08 packed render priority, not a collision box.
  Opcode81's inline write at162A80 proves PC-as-NPC class3. NPC class4 and enemy
  class5 use visual biases7 and107 at16B156/16B257. Appearance labels were checked
  against the installed c000..c262 BMP/CEL first-frame assemblies. Names and
  rewards were not inferred from scripts.
- Field input175A40 checks native control before reaching175A8D. At175A8D,
  EDI=engine, ESI=pad, ECX=engine, and the pad is already pushed for the original
  CALL175C90 dash predicate. The probe changes saved ESI and the existing stack
  argument together. The original call runs once. Direction bits are800/400/200/100
  for up/down/left/right. 174B60 consumes the resulting movement for the actor.
- The compiled probe assembly was exercised in an isolated x86 emulator in48
  combinations. Callback arguments, restored GPRs, flags, stack depth, and the
  original dash argument were checked. No OS key or position write is involved.

## Terrain, exits, and visibility

- MapTable loader DB1F0 populates three byte-plane descriptors at A+1100C,
  A+11020, A+11034. Each is begin/end/capacity/width/height. 173750 reads them.
- 178C50 selects one of two physical regions within each collision tile. Prop2
  encodes first layer in bits0..1, neutral bit2; second layer in bits3..4, neutral
  bit5. Shape-dependent region selection is preserved in `FieldCollisionRules`.
  The physical player layer is byte D+2E155, D=engine[0]. 178FF0 permits matching
  layers and layer3 transitions, with a distinct rule for neutral regions.
- The exact178C50 routine was run against synthetic memory in Unicorn. Its2,560
  outputs form the embedded regression vectors. These validate collision shape
  classification, not every special movement mode or dynamic actor collision.
- Active exit cells use engine+E70, dimensionsE7C/E80. A byte below80 is a local
  exit index, with offsets atE58 and28-byte records atE64. 178FF0 consumes that
  grid and sets transition bit80 at F+106C. Routes cannot pass through other
  exits. The planner does not name unseen destination areas.
- 179690 constructs the chest grid at engine+E44, dimensionsE50/E54, and resolves
  shared-table aliases into F+2190/2194. Open bits are A+110B4+((index>>3)&63)*4.
  Only property0 bit1 plus a rendered closed tile FE/EE/E0/F0 is exposed; reward
  contents and invisible pickups are excluded.
- 173B40 moves camera bounds M+10/14/18/1C in eight-pixel units. 173C90/173D00
  and173DD0/173D60 update phases M+1A8/1B4. 15DFA0 uses these bounds. This pass
  uses that native window conservatively; extra widescreen margins are a known
  coverage gap. Desktop resolution never supplies route coordinates.

## Scope and validation boundaries

The first release supports local field navigation with generic appearance labels.
It does not establish overworld navigation, special-field movement, complete
occlusion analysis, or every NPC interaction range. Ordinary game collision still
enforces dynamic obstacles; blocked walking stops after1.5seconds. Guidance and
walking require a fresh user command after control is lost. Offline integration
tests cover key edges, semantic interruption, scene/focus/manual cancellation,
visibility/discovery, route layers, exits, arrival, and search bounds. A live
bedroom-to-exit test by the user remains required.

## Release review

The final Release suite passed 896 tests (Core 72, Prism 8, Native 493, Mod 323),
with no failures or skipped tests. All 122 hook signatures matched the installed
executable. The 2,560 collision vectors are checked within the Native suite; the
48 compiled-assembly emulation cases are an additional offline check.

Claude reviewed input-hook arithmetic, direction bits, fault handling, routing,
and semantic interruptions. Root separately checked target selection, map and
viewport capture, appearance labels, and composition. Manual action buttons, as
well as directional buttons, intentionally cancel automatic walking so normal
interaction takes control immediately. The stop announcement precedes any
subsequent dialogue event, which can interrupt it.

Navigation uses the existing synchronous Prism dispatch only for commands and
changed directions, not every frame. The pinned upstream
[NVDA backend](https://github.com/ethindp/prism/blob/9911156998b52fee91fb2cb4f71ac793d4e546c7/source/backends/nvda.cpp)
submits speech through controller RPC without waiting for speech completion.
Backend dispatch can still take time; live speech latency remains unmeasured.
The alternative OneCore backend also waits for synthesis, so no claim of
universally nonblocking speech is made.

Version 0.3.0 was deployed with the game closed. Deployment verification passed
25 checks, including the existing launch redirect and unchanged game executable.
All 27 mod payload files matched their SHA256 manifest; all 45 loader/shared-hook
files matched the vendored sources. The previous installation is retained under
`Accessibility/Backups/20260910-164637` in the game folder.

Deployed DLL SHA256 values:

| DLL | SHA256 |
|---|---|
| Mod | `05DFF6FA2E0DE9CE4746F8739D42BD128AF0E5B558317E84AF4DCE4F0BF055ED` |
| Native | `05834FCEA7B602545038854BA2BBABE1D64272444AFF0323A89835BEF28CF3D8` |
| Core | `264FBD4A554913E9114F32F10662514A65B1E33737445742AAD0A4FEEAEEA25A` |
