# Whole-game navigation coverage audit, 0.3.28

The implementation includes the main story through Lavos, the classic optional
quests, and the PC bonus areas. This audit checks those implementations against
the supplied guides and installed PC resources. It also found a shared exit-goal
defect: 86 physically reachable exits were not routable with the previous goal
rule. Version 0.3.28 repairs that rule, rather than adding a Cathedral-only
coordinate exception.

## Guide-to-implementation map

The reference sources are the user's saved copy of [vinheim and Bkstunt's
GameFAQs guide, version 1.0](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/64344),
including its main walkthrough and vb801..vb807 sidequests, and [Thonky's
chapter and sidequest index](https://www.thonky.com/chrono-trigger/).
The saved GameFAQs file was recovered from the user's Dropbox PC archive;
its prose is not redistributed with the mod. Bonus-area details and actual
route positions are checked against the installed PC scripts. DS-only Arena
material in a guide does not create a nonexistent PC entrance.

| Story stretch | Implementation and decisive bindings |
|---|---|
| Home, fair, pendant and Telepod | Opening/EarlyStoryTargets; native fair flags, actors and activation regions |
| Truce Canyon, castle and Cathedral | EarlyStoryTargets; guard conversation, stair opening, right switch, organ, Queen Leene |
| Return, trial and prison | EarlyStoryTargets; story counter, cell release, native exits and encounters |
| Ruins, Arris Dome, Site32, factory, first End of Time | FutureStoryTargets; consoles, rat, factory sequences, Robo and Spekkio |
| Medina and Heckran Cave | FullStoryObjectives: leave-end-of-time, heckran, return-middle-ages |
| Zenan Bridge | Request, kitchen, collect rations, deliver, Zombor as separate steps |
| Denadoro, Tata and Frog | Encounter, broken blade, badge, Frog conversation and hilt pickup |
| Dreamstone and reforging | Ioka, Forest Maze, Reptite Lair, return to Melchior and workshop handoffs |
| Magus's Keep | Both wings, entrance light, Slash/Flea and onward passage |
| Laruba, Dactyl Nest and Tyranno Lair | Native story steps, vehicle entry and encounters |
| Zeal, pendant and sealed passage | Palace progression and the pendant's native state |
| Keeper's Dome and Epoch | Native sealed doors, encounters and vehicle boarding/time gauge |
| Algetty and Mountain of Woe | Native encounters, Melchior and Ruby Knife handoff |
| Ocean Palace | Separate upper/central/lower switches and room-section routing |
| Blackbird and North Cape | Duct entry, all equipment sets, money/items, wing encounter and native choice |
| Time Egg and Death Peak | Gaspar, doll, Nu, peak sequence and revival flags |
| Final approach and Lavos | Native travel options, shell/interior encounters and final-room objectives |

The late-story provider contains 84 main/context actions. The opening through
the first End of Time uses the separate earlier providers above; 84 is not a
count of the entire game's events. Tests enumerate every late-story counter
value and check scene/actor identities, automatic arrivals, prerequisite
transitions and route bindings.

| Optional content | Provider and availability |
|---|---|
| Sunken Desert/Fiona and Lucca's past | ClassicOptionalObjectives; sapling, battle, Robo handoffs, campfire and return flags |
| Sun Stone | ClassicOptionalObjectives; Moon Stone, kindness/jerky, mayor, replacement and charging |
| Ozzie's Fort | ClassicOptionalObjectives; encounters and trap switches |
| Geno Dome | ClassicOptionalObjectives; Robo, doll puzzles, receiver charge, Atropos and Mother Brain |
| Cyrus/Northern Ruins | ClassicOptionalObjectives; tools, paid repairs, monster groups, era and Frog gates |
| Rainbow Shell | ClassicOptionalObjectives; Toma, Giant's Claw, shell transport, trial, fragment and Melchior |
| Black Omen | ClassicOptionalObjectives; native passages, elevator/encounter continuations and final approach |
| Lost Sanctum | BonusStoryObjectives; forest clearances, hammer, stones, ladder, materials, bridge/food, fortress/tower and smith requests |
| Dimensional Vortex and Time's Eclipse | BonusStoryObjectives; actual first-clear entrance bits, shuffled rooms, lab/volcano/cliff sequences, Shades and final portal |

The classic optional provider has 119 actions; the bonus provider has 107.
They are emitted in People, Objects or Exits as appropriate, using their
native prerequisites. They are not all simultaneously available. Named action
counts establish implemented scope, not a completed playthrough.

## Pickups and resource coverage

A fresh resource report verifies 669 scene records, 1,020 static room exits,
240 world entrance records and all 343 native treasure-table records. The
catalog also contains 143 actors with guarded item/money actions, 4,732 actor
identities and 2,387 spatial regions. Rewards are not named before opening;
the runtime checks active actors, open flags and the current collision map.

An additional independent opcode pass inspected the talk/contact functions of
catalogued actors: 124 have direct item/money operations, and none is missing
its item-interaction classification. Delegated scripts account for additional
catalogued actors. This direct pass does not claim to prove every automatic
reward or every delegated branch.

Reproduce the resource and direct-interaction checks with:

```powershell
py -3 tools/research/future_story/report_catalog_coverage.py --game-root '<installed game>' --catalog src/ChronoTriggerAccessibility.Mod/Navigation/Data/game-navigation.json --output '<report.json>'
py -3 tools/research/future_story/verify_guide.py --game-root '<installed game>' --output '<guide-bindings.json>'
```

## Evidence limits

- The Cathedral route tests use native collision planes and both logged player
  positions. The broader 86-exit repair result is an offline replay across 457
  scenes from selected starting components, not every possible runtime state.
- Eight unused/stub scripts remain explicitly identified in the resource report.
  Eleven bounded script-expansion warnings remain visible; existing audited
  bonus overrides cover the relevant forest/lab cases. Neither list is hidden
  by a claim of 100-percent live coverage.
- The main ending, each optional chain, moving platforms, timed puzzles and
  every collectible have not all been played end to end with this mod. The
  catalog and tests cannot establish that no further runtime route defect exists.
- This report concerns story, navigation and pickup coverage. Existing limits
  such as the touch-style submenu interface and pending intro description are
  still recorded in the README.

The supported executable and navigation catalog hashes, exported 310 late-story
and optional actions, pickup audit and the named 86-exit list are retained in
`artifacts/research/cathedral-tech-0328/`.
