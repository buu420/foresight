# Left Telepod demonstration (scene 8 actor 12) — spatial audit

Why "No route to Try the left Telepod is available", and what goal actually reaches the pod.
Research only: no source, test, build, commit, deployment or game control.

* Executable `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`, image base
  `0x400000`.
* `Game/field/MapTable/MapTable_0071.dat` sha256
  `D8E1D8DB78F2D5E4AF1DDF2AD5CB59B65FA5BCF1E40FDC7CC802012E211C6CF5`, 16x16 (root's
  `artifacts/research/telepod-pendant-0315/map-proof.json`).
* Tools: `artifacts/research/telepod-demo-0315/walkable.py` — an offline replica of
  `FieldCollisionRules` (RVA `178C50` region selection, `178FF0` layer acceptance) and
  `FieldNavigationGraph`; plus root's already-verified Unicorn replay of the native movement
  routine `0x175E90` (`telepod-pendant-0315/native-worker.py`), used unmodified.
* The opcode and progression audit is in `docs/telepod-pendant-native-audit.md` and is not
  repeated here.

## 1. The proposed cause is right, and it is worse than "likely blocked"

`FieldNavigationSource.cs:86` builds touch-landmark goals as
`At(tileX*256+128, tileY*256+128)`. For actor 12 that is **(1152, 1152)**, tile **(4,4)**.

Map 71 tile (4,4) has properties `p0=0x06, p1=0x00, p2=0x01`. `p0 >> 2 = 1`, so
`FieldCollisionRules.Region` takes the **alternate** branch and returns
`Layer = (p2 >> 3) & 3 = 0`, and `TryEnter` rejects `Layer == 0` on every current layer.

I swept every fine point in the tile on a 16-unit grid for layers 1, 2 and 3:

```
tile (4,4): walkable fine points on any layer = 0
At(1152,1152) on layers 1/2/3 -> null, null, null
```

So `At(...)` returns an **empty** approach set, the target has no goal at all, and the planner
correctly reports no route. This is not a near miss that a small offset would fix by luck: the
tile-centre formula is structurally unusable for this landmark, because the actor stands on an
impassable tile.

## 2. Where the player can actually stand

The left pod is the 2x2 block of tiles **(3,5), (4,5), (3,6), (4,6)**, each `p0=0x02, p1=0x40,
p2=0x41` → shape 0, primary region, layer 1, no neutral mask → walkable. `p2` bit `0x40` marks
exactly the three telepod pads in this map and is not consumed by `Region`; `p1` bit `0x40`
covers the whole raised stage and is not a pad marker.

Walkable tile centres, layer 1 (rows 3-8 shown; full grid in `walkable.py` output):

```
      0  1  2  3  4  5  6  7  8  9  A  B  C  D  E  F
 3:   .  .  .  .  .  .  1  1  1  1  .  .  1  1  .  .
 4:   .  .  .  .  .  .  1  1  1  .  .  .  .  .  .  .      <- actor 12 is here, at (4,4)
 5:   .  .  .  1  1  .  .  1  1  .  .  1  1  .  .  .      <- left pod top row
 6:   .  .  1  1  1  .  .  1  1  .  .  1  .  1  .  .
 7:   .  .  1  1  1  1  1  1  1  1  1  1  1  1  .  .
 8:   .  .  1  1  1  1  1  1  1  1  1  1  1  1  .  .
```

The reachable set on the pod, breadth-first on the mod's own 64-unit lattice, is identical from
all three logged starts `(2069,2058)`, `(637,1783)`, `(1106,2989)` (and from `(1788,2548)`):

```
y = 1408   x = 896, 960, 1024, 1088, 1152
y = 1472   x = 896, 960, 1024, 1088, 1152
y = 1536+  x = 1024, 1088, 1152        (tile (3,6) is a split tile, so the row narrows)
```

The native agrees exactly. Replaying `0x175E90` on the real map:

| from | step | native result |
|---|---|---|
| (1152, 1408) | up 16 | accepted → 1392 |
| (1152, 1392) | up 16 | **refused** (dy zeroed) |
| (896, 1392) / (960, 1392) / (1088, 1392) | up 16 | **refused** |
| (1152, 1408) | right 64 | **refused** |
| (1216, 1408) | right 64 | **refused** |
| (896, 1408) | left 64 | **refused** |

On the tested 64-unit lattice, the reachable top row has **x ∈ [896, 1152], y = 1408**. These samples do not establish exact horizontal bounds between lattice points. The verified upper vertical limit is y = 1392. The limits
come from the two leading corner probes 112 units out: `1216+112 = 1328` is tile 5 (blocked),
`832-112 = 720` is tile (2,5) (blocked), and `1392-112 = 1280` is the last walkable row while
`1376-112 = 1264` lands in blocked tile row 4.

## 3. The gap, and what it means for the contact test

Actor 12's script position is `8B 04 04` → fine **(1152, 1279)**, the final unit of tile row 4.
The closest the player's feet can ever get is **(1152, 1392)** — a vertical gap of **113** units,
or **129** to the nearest node on the mod's 64-lattice, `(1152, 1408)`.

Two consequences, both hard:

* **The native contact test cannot be tile equality.** The player's tile on the pod is always
  row 5 or lower; actor 12's tile is row 4, and no point of row 4 is reachable. A test of
  "player tile == actor tile" could never fire, yet the demonstration demonstrably works in the
  shipped game. The test must tolerate at least 113 units of vertical separation, i.e. roughly
  half a tile — consistent with a box of about `±0x80` around the object's fine position.
* **Do not aim at the actor's live position.** With arrival tolerance 32 the walker would need
  to get within 32 units of (1152,1279); the minimum achievable is 113, so it would walk to the
  pod and then never report arrival.

## 4. Recommended goal

**Primary goal: `(1152, 1408)`, layer 1.** It is a lattice node. The existing arrival tolerance admits y ∈ [1376,1440]; part of that interval is above the reachable vertical limit, so tolerance alone does not prove contact. Root subsequently replayed actual movement and checked the native marker contact selector; see the review below.

It is the reachable point nearest the controller, directly below it in the same column, and it
is a candidate for starting the demonstration, subject to the contact and live-validation limits below.

**Goal set, if you prefer robustness to a single point:** the whole top row of the pod,
`y = 1408, x ∈ {896, 960, 1024, 1088, 1152}`. If the contact box turns out to be narrower than
the pod, prefer the x-aligned members first (1152, then 1088, then 1024).

**Generic rule rather than a hardcode.** Replace the touch branch of
`FieldNavigationSource.cs:85-87`

```csharp
var approaches = touch
    ? At(actor.TileX * 256 + 128, actor.TileY * 256 + 128).Where(p => !graph.IsTerminal(p)).ToArray()
    : Approach(position);
```

with: start from the actor's **live fine position** snapped to the 64-lattice, and if that yields
no non-terminal points, expand outward in 64-unit rings (cap at about 1.5 tiles, ~384 units) and
take every non-terminal point at the smallest radius that produces any. For actor 12 that lands
on `(1152, 1408)` at radius 128 without naming the scene.

The existing tile-center goals remain walkable for two earlier touch landmarks: scene 120 actor 17 sits on
tile (37,11) and scene 122 actor 24 on tile (40,47), and both tile centres are walkable on
layer 1, but starting from their snapped fine positions would not necessarily be a byte-for-byte no-op. That proposed generic rule was not adopted. Worth noting that neither of those is an engine-touch
object at all — both have empty entry 1 and entry 2 and do their work in a polling idle thread,
so scene 8 actor 12 is the only shipped landmark that genuinely depends on the engine starting
entry 2.

## 5. Limits — read this before treating §3 as settled

I proved where the player can stand and that the current goal is unreachable. I did **not**
locate the native routine that starts an actor's entry 2 on contact, so the exact contact box is
bounded but not measured.

What I ruled out along the way, so nobody repeats it:

* Nothing in `Atel_0028.dat` calls actor 12 function 2 — there is no `fn 2` call anywhere in the
  scene, so the engine starts it.
* The interaction scan `0x1760B0` is a query over `record+0x150/+0x151/+0x152`, not a trigger.
* `record+0x150` ("in contact") is written only by the script stepper `FUN_00561560` (`0x161560`)
  and only when the stepped actor executes opcode `0x31` **and** its tile equals the player's
  tile (`in_ECX[0x2F2]/[0x2F3]`, captured from the visual-`0x79` player marker). Actor 12's
  script contains no `0x31`, so this path is not it.
* This bounded displacement search found writes to an actor's script PC `record+0x48` in scene init `0x1608A3` (entry 0),
  the executor's return restore `0x161375`, and the script `call` primitive `FUN_00564970`
  (`0x164970`), whose observed callers are inside the executor. This search was not exhaustive: root also found an activation write at `0x17FB45`. An entry-2 path was not identified from the displacement keys searched
  (`0x12005`, `0x6834`, `0x6988`, `0x68E0`).

If `(1152, 1408)` does not fire live, the next cheapest experiment is to log whether actor 12's
`record+0x48` ever becomes `0x6DE` (its entry-2 pointer, raw offset `0x6DF`) while the player
stands there, and if not, sweep x along y = 1408 before concluding the trigger is elsewhere.

## Root review and final scope

The repair uses a specific contact approach for scene 8 actor 12, derived from its live coordinates. It does not search for arbitrary nearby floor or alter the other touch landmarks. The installed script binding and armed bit must still be present.

Root added the recorded actor 12 to the Unicorn movement worker, with binding 128, class 7, position (1152,1279), and its non-solid collision flag. At all three logged origins and both tested speeds, native `0x178980` selects actor 12 (encoded 24 in field+0x20C4) before the real controller reports arrival at (1152,1408). These six replays use the final source with no goal override. The selector's movement probe is offset from the player's foot position, so its contact result must not be modeled as an unverified symmetric box around that foot position. The broad goal-row/ring suggestion above was not used.

This supplies positive native movement-contact evidence beyond the geometric bounds in the original audit. It still does not execute the whole entry-2 cinematic; live demonstration execution remains to be checked. See [telepod-demo-navigation.md](telepod-demo-navigation.md) for regression tests and the final implementation.
