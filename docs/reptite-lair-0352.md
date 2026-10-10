# Reptite Lair holes and creatures

A tester reported objects called "Sparkle" in the Reptite Lair that behaved like creatures, exits or traps. The game drew none of them as sparkles.

## What the game draws

**Holes.**
- The scripts in scenes 284 (Beetle Room), 285 (Hole 1), 286 (Hole 2) and the side room 222 load person sprite 0x85 for their holes.
- Native 16B010 and 16B120 store that index and load sprite sheet c140. Op `AC 02` (native 16EA90) holds assembly frame 2 as a static frame.
- Decoding the installed c140 cell data shows frame 2 is an open pit with a rocky rim. Frames 0 and 1 are a small dirt clump and a half-open crater.
- The old label table listed visual 140 as a sparkle. The sparkle sheets, such as c119 and c128, are separate.

**Creatures.**
- Lair creatures use `load_enemy`. Native 16B220 loads sheet 0x107 + enemy id.
- The installed sheets show the appearances in the table below.

| Enemy id | Sheet | Appearance |
| --- | --- | --- |
| 0x46 | c333 | blue armored beetle |
| 0x26 | c301 | pink-flowered plant creature |
| 0x7B | c386 | large horned green dinosaur |
| 0x01 | c264 | small green lizard |
| 0x5D | c356 | mushroom-capped creature |
| 0x79 | c384 | gold creature with green wings |

## What changed

**Holes are offered under Exits.**
- Each visible lair hole is named Hole A, Hole B and so on, in the scene's own hole order, with "Press Confirm to interact."
- The game decides whether a hole first starts a battle and where it leads. Neither is announced, and no destination is named, so the correct tunnel is never revealed.
- Navigation walks to the hole and faces it; Confirm stays with the player.
- A visible hole remains readable while the native script-call gate blocks interaction.
  It cannot start a route, and an active route stops if that gate closes.

**Parked holes are not offered.**
- Unplaced holes wait at tile (0,0) until a beetle burrows or a script places them.
- That corner is solid rock in every hole map, so a parked hole has no Confirm contact.

**Creatures keep their battle rows.**
- The rows are named by appearance instead of the generic Encounter or Creature, within lair scenes only.
- Contact rules are unchanged.

**Other scenes keep their own c140 actors.** Guardia Forest, Denadoro Mountains and Death Peak animate c140 differently, so they keep their existing labels.

## Verification on the installed maps

Regression tests use the installed collision for scenes 222, 284, 285 and 286. Each map was checked against resources.bin.

**Landing chambers.**
- The arrival in a scene-change record is where the party drops in.
- The nearest standable floor is three or four tiles lower, in a small chamber that reaches that chamber's own hole.

**What the tests cover.**
- From each landing, the target is reached and faced within native Confirm reach, by both automatic walking and spoken guidance, at walking and Dash speed. The holes covered are:
  - every fixed hole;
  - a hole dug on a beetle's route;
  - Hole 1's later hole.
- A parked hole appears when the same actor is moved, disappears while hidden, and returns when shown.
- Disabled native interaction calls prevent guidance even if the activation scan remains set;
  command-level tests verify both starting and stopping routes in both navigation modes.
- The second hole in side room 222 lies outside the arrival chamber, and no route to it is invented.

## Limits

The drop animation's exact landing tile is inferred from collision rather than traced. These also need live verification:
- holes hidden during battles;
- a hole moved under a creature after it burrows;
- whether hole fronts are reachable on the live map.

**Story guidance.** Next-passage guidance does not yet treat the holes as passages. Hole 1 and Hole 2 have no static exits.
