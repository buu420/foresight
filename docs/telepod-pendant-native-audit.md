# Telepod pendant (scene 8 actor 11) — native audit

Read-only audit for the pickup that blocks the player after Marle vanishes, and for the
transition it triggers. No source, test, build, commit, deployment or game control.

* Executable `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`, image base
  `0x400000`. Native RVAs below are against that base.
* Script `Game/field/atel/Atel_0028.dat`, sha256
  `bf881b536a9f6c9e144a712cc46a282d9e315f815c0b01de3c5a4721a33b8181`, 29 actors, packet base
  `0x3A1`. Script offsets below are **raw file offsets** in that .dat.
* Tools: existing `tools/research/future_story/{assets,connections,decode_verified}.py`
  (unmodified), plus `artifacts/research/telepod-0315/{dump,scan}.py` and Ghidra headless
  (`-readOnly -noanalysis`) on `ct_world.gpr`.

## 1. Root's diagnosis is correct

In version 0.3.14, `EarlyStoryTargets.cs:58-60` already binds `story:follow-marle` to `actor:11:4:99` at
storyline 12, but `FieldNavigationSource.cs:56-57` drops the actor before it can become an
active id: the pendant is an **Object**, `IsActivationCandidate` is false, and
`IsScriptedPickupAvailable` only answers for scene 439 actor 15. Hence
"The destination is not active in the current area."

Nothing else is wrong: the actor exists, is loaded, is drawn, and its script is live.

## 2. Opcode semantics proved from the native (needed to read the gates correctly)

Field opcode dispatcher `0x1619E0`, jump table at **`0x163620`** (256 entries).

| op | handler | proved behaviour |
|---|---|---|
| `0x16` | `0x161E39` → **`0x165AD0`** | `16 <globalIndex:u8> <value:u8> <operation:u8> <jump:u8>`. Reads `global[index]` from `ActorBase + 0x110B0 + index*4`. If `operation & 0x80`, the operation is `& 0x7F` **and the index gains `+0x100`**. Ops: 0 `==`, 1 `!=`, 2 `>`, 3 `<`, 4 `>=`, 5 `<=`, 6 `AND != 0`, 7 both operands zero. **The branch is taken when the comparison is FALSE**; when it is TRUE execution falls through to the next instruction. Target = `pc + 4 + jump`, matching `decode_verified.walk`. |
| `0x56` | `0x1624EA` → **`0x168190`** | `56 <value:u8> <index:u16 LE>` → `global[index] = value`. Index 0 is the storyline counter, so `audit.py`'s `a[1:] == b"\0\0"` progression rule stays correct. |
| `0x90` | `0x162B8C` → `0x1629A8` | writes **1** to `record + 0x6A10`, i.e. `actorBase + slot*0x154 + 0x6940 + 0xD0` = **DrawMode**. So `0x90` = **show**. |
| `0x91` | `0x162BA0` → `0x1629E6` | writes **0** to the same field. `0x91` = **hide**. |
| `0xDD` | `0x163323` → **`0x171480`** | `DD <dest:u16 LE> <facing:u8> <x:u8> <y:u8>`; destination is masked `& 0x3FF`. |

Function-slot mapping (corroborated twice by shipped, live-verified behaviour): entry **1 =
Activate** (confirm press), **entry 2 = Touch** (walk onto). Scene 8 actor 12 — the Left Telepod
the mod already lists in `IsTouchLandmark` — has an empty entry 1 (`0x6DE`, a bare `00`) and its
real body on entry 2 (`0x6DF`). Scene 439 actor 15, the first pendant, is the mirror image:
body on entry 1 (`0x8FC`), empty entry 2 (`0x91E`).

## 3. Scene 8 actor 11 — the pendant

Class 4, visual `0x63` (99) — the same sprite as scene 439 actor 15. Raw entry table:
`f0=0x640, f1=f2=0x656, f3..f15=0x6D1` (pointer *v* addresses raw offset *v+1*).

### Show gate — `f0` init at `0x641`

```
0x641  82 63              load visual 99            (DrawMode defaults to 1)
0x643  8D 00 04 00 05     position = (0x0400, 0x0500) = (1024, 1280)
0x648  16 00 0C 01 04     if global[0] != 12  -> fall through; else jump 0x650
0x64D  91                 DrawMode = 0  (hide)
0x64E  10 06              jump 0x655
0x650  8D 00 04 C0 05     position = (0x0400, 0x05C0) = (1024, 1472)
0x655  00                 return
```

**The pendant is visible exactly when the storyline counter is 12**, and only then. There is no
flag involved: unlike scene 439, nothing writes `0x54` bit `0x10`/`0x20` for this actor.

The reveal is completed by `f3` at `0x6D2`, called once from the Marle-vanish cutscene
(`0x884`, `02 16 33` = call actor 11 fn 3, inside actor 14's script):

```
0x6D2  90                 DrawMode = 1
0x6D3  AD 01              wait
0x6D5  92 40 18           move, net -24 on y
0x6D8  00                 return
```

`1472 - 24 = 1448`, which is exactly the logged `pos=(1024,1448)` — the script and the live
capture agree to the pixel. Tile = `FineX >> 8, FineY >> 8` = **(4, 5)**, one tile below the Left
Telepod pad at (4, 4).

The same cutscene ends with `0x8F5 5A 0C` (storyline = 12) and `0x8F7 66 00 56`
(clear `global[0x56]` bit `0x01`).

### Pickup — `f1` = `f2` = `0x657`

Activate and Touch share one body, so **pressing confirm beside it and walking onto it both
work**. The body has **no gate of its own**; it starts unconditionally at `0x657 EB 30 00`.

It plays the Gate-opening scene, all from `Localize/en/msg/mesk0.txt`:

| offset | string | text |
|---|---|---|
| `0x681` | `0x74` | Lucca: Boost the power output! |
| `0x684` | `0x75` | Taban: Roger! |
| `0x689` | `0x76` | Lucca: More! I need more power! |
| `0x68C` | `0x75` | Taban: Roger! |
| `0x696` | `0x77` | Lucca: There! I think we did it! |
| `0x6B3` | `0x78` | Lucca: Good luck, Crono! I'll follow you as soon as I figure out what went wrong! |

### State after the pickup

```
0x6C1  56 01 5B 00        global[0x5B]  = 1
0x6C5  56 01 ED 01        global[0x1ED] = 1
0x6C9  DD 71 00 01 08 07  change location -> scene 113, facing 1, arrival tile (8, 7)
0x6CF  FF 80
0x6D1  00
```

* **No storyline write.** The counter stays at **12** across the transition, so
  `EarlyStoryTargets` `case 113 when p < 15` picks up correctly on arrival — no gap.
* Scene 113 is `Mountains Behind Truce\Time Gate` (Atel_0334, map 100), i.e. 600 AD.
* `global[0x5B]` is a one-shot "arrived through the Telepod gate" flag: scene 113 actor 0 tests
  `== 1` at `0x27F`/`0x2FD` and clears bit `0x01` at `0x328`/`0x374`; actors 2-9 all branch on it.
  `global[0x1ED]` is set here and cleared again by scene 113 actor 0 at `0x394`. Neither is
  progression state the mod should gate anything on.

## 4. Approach or step onto?

**Approach and confirm — mirror the scene 439 fix. Do not convert this to a touch landmark.**

* The native binds both entry 1 and entry 2, so approach+confirm is genuinely supported; this is
  not a touch-only prop.
* `Approach()` (`FieldNavigationSource.cs:246`) snaps (1024, 1448) to (1024, 1472) and offers
  (768,1472) tile (3,5), (1280,1472) tile (5,5), (1024,1216) tile (4,4) and (1024,1728) tile (4,6).
  Tile (4,4) is the Left Telepod pad, so the route can put the player on actor 12. That is safe:
  actor 12's touch body is gated at `0x6DF` by `16 56 01 06 35` — `global[0x56] & 0x01` — and the
  Marle cutscene cleared that bit at `0x8F7` when it set the counter to 12. The logged
  `56=02` confirms bit `0x01` is clear, so the pad is inert and cannot hijack the walk.
* `flag152=0` is not a reason to prefer touch. The interaction scan at **`0x1760B0`** does reject
  an actor whose `+0x152` byte is zero (`cmp byte ptr [eax+0x152], 0; je`), but that byte is set
  transiently during interaction resolution at `0x161650`/`0x161659` and is zero in steady state
  for **every** actor in the logged inventory — including Taban and Lucca, who are obviously
  talkable. Root's existing comment on `IsScriptedPickupAvailable` is right, and the sampled
  `activationCandidates=0` is a snapshot artefact, not a gate.

If live testing ever shows the confirm press missing, routing onto tile (4,5) is native-supported
and safe for the reason above — but it would need `FieldNavigationSource` to grow a touch form for
`actor:` targets, which today exists only for `landmark:`.

## 5. Recommended predicate

Extend `EarlyStoryTargets.IsScriptedPickupAvailable` with a second, exact arm. The scene-439 arm
must keep its flag test; the scene-8 arm must **not** grow one, because no flag governs this
pendant:

```csharp
(scene == 439 && state is { Point: 6 } && actor.Index == 15 && ... && flags as today)
||
(scene == 8 && state is { Point: 12 } &&
 actor.Index == 11 && actor.ClassTag == 4 && actor.VisualIndex == 99 &&
 actor.ActivationBinding != 0)
```

`Point == 12` is exact rather than a range: at any other counter the init at `0x648` falls through
to `0x64D 91` and the actor is hidden, which the existing `IsDrawn` filter already excludes, so the
two conditions agree instead of fighting. `ActivationBinding` is `record + 0x20`, logged as
`field20=128`.

`Bind("follow-marle", "Pick up Marle's pendant", ["actor:11:4:99"])` then resolves, because
`FieldNavigationSource.cs:59` builds exactly `actor:{Index}:{ClassTag}:{VisualIndex}`.

Nothing else needs to change. `EarlyStoryTargets.IsTouchLandmark` should **not** gain `(8, 11)`.

## 6. Limits

Static decode plus static native decompilation; the game was not run. The function-slot mapping
(1 = Activate, 2 = Touch) is inferred from the two shipped, live-verified cases in §2 rather than
from the native dispatcher that selects the slot. Opcode `0x92`'s exact operand meaning was not
decoded — only that its net effect here is the observed -24 on y, which the live position confirms.
