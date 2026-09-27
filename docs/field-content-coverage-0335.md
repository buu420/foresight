# Field content coverage: counters, services, repeated markers, treasure (0.3.35)

This covers what the field navigation list offers for items, objects, people and shops across
the whole game. The data comes from the installed scene scripts (`resources.bin`, Atel files)
and the native field engine, not from aggregate catalog counts. Enemies and battle triggers
belong to the encounter layer (`docs/encounter-navigation-0335.md`) and are only counted here.

## What the scripts contain

`tools/research/future_story/audit_content.py` walks every actor of every
scene script. 1,973 actors in 449 scenes have a Confirm (function 1) or contact (function 2)
handler that acts: it talks, gives an item, opens a menu, warps, fights, heals, changes the
map or sets a story flag. Calls into other actors (opcodes 02/03/04) are followed.

`FieldContentCoverageTests.EveryScriptInteractiveActorIsOfferedOrAccountedFor` runs the real
`FieldNavigationSource` over each of them, using its installed initialisation position when present (8B puts
the feet at `x*256+0x80, y*256+0xFF`, 0x16C040; 8D is exact, 0x16C2C0), with a story state
that satisfies one of its catalogued actions. Actors positioned only by later movements use
a synthetic floor position for this listing check; this does not prove their routes. 1,523
are offered. The other 450 are accounted for:

| reason | actors | owner |
| --- | ---: | --- |
| only a battle (D8) | 441 | encounter layer |
| story contact trigger (EarlyStoryTargets / FutureAreaLabels `IsTouchLandmark`) | 4 | story providers |
| audited scripted pickup (scenes 8 actor 11, 439 actor 15) | 2 | EarlyStoryTargets |
| parked outside the map (tile FF) | 2 | none |
| party change only (unused scene 271) | 1 | none |

Loop-driven Confirm tests (opcodes 2D/31/3C in an actor's loop) were checked separately. 121
actors poll Confirm this way. 57 are the save-point checkers (`docs/field-save-points-native-audit.md`).
The Blackbird vents are catalogued regions. The rest are party or cutscene scripts.

The actor filter tells hidden actors apart from ones that are merely out of view. `IsDrawn`
is the same test the native engine uses: 0x17A6C0 only sets the Confirm binding (+0x20) for
an actor that is drawn (+0xD0 1..7F) and inside the camera window. Hide (91, 0x162BA0) clears
+0xD0 and nothing else. Of the 86 interactive sprites hidden at initialisation, 83 can be shown
by a script: their own 90 in a requested function or their loop, or another actor's 7C. The
other 3 are the talk-only duplicates in scene 628 (unreachable).

## Keepers behind counters (stand-ins)

Innkeepers, shopkeepers, bartenders and the Truce ferry clerk stand behind counters. From
every customer tile they are outside the native Confirm range: 0x1C0 along the facing axis
and 0xC0 across it (`FieldInteractionRange`). The game gives the counter its own actor, and
Confirm on that actor runs the keeper's handler. Before this change only three hand-audited
bindings existed (`FieldActorProxies`), and only Truce Market's resolved: the other two involve
interaction markers, which are never drawn, and the resolver required drawn actors. Keepers without a binding were
offered as a person the route search could not reach ("No route to Innkeeper"), with the
counter listed separately under a generic name.

`build_field_content.py` finds 20 stand-ins in 16 scenes. They are written to
`Navigation/Data/field-content.json` and read by `FieldContentFacts`:

- **Call** (14): the whole Confirm handler forwards to one keeper, optionally behind the
  usual re-entry lock. Truce Inn's counter marker is
  `12 10 00 00 08, 75 10, 02 10 11, 77 10, 00`, a call to actor 8's handler.
- **Twin** (3): a counter marker within three tiles runs a byte-identical copy of the
  keeper's handler. Porre Market's marker and clerk are both `C8 8C, 00`.
- **Forward** (3): an object or marker that reaches the keeper on some paths but also acts
  on its own, such as Truce's "Market goods" counter. It only extends reach and keeps its
  own row. The three conditional forwards retain their native branch guards: Truce's
  current clerk depends on Point, Local06 and Global142; Medina's challenge option depends
  on Global1A1 and Global1A2. Missing capture data cannot enable these forwards. Choosing
  an option remains a player action.

With a stand-in present, the keeper's goals include everywhere Confirm reaches the stand-in.
Goals are ranked by the nearest place Confirm is accepted, so the keeper's own floor behind
the counter cannot crowd them out. A generic Call or Twin stand-in row is then dropped, but
only once its keeper has actually been offered. A visible character that stands in keeps
its own row (the Truce ferry office cat). Checked against installed terrain, 11 keepers who
had no route now route. By scene and actor they are:

- Truce Inn (12/8)
- Truce Merchant, both clerks (17/8, 17/9)
- Truce ferry office (18/8)
- Medina Inn (35/9)
- Medina Merchant (39/9)
- Porre Market (54/8 and 157/8)
- Choras Inn (62/8)
- Truce Inn 1F (116/10)
- Choras Tavern (188/8)

## Names for what a sighted player sees

Curated names still come first. Otherwise:

- A Confirm handler (or a stand-in's forwarded one) that opens a shop (C8 80–BF) is named
  **Shopkeeper** (person) or **Shop** (counter).
- One that heals (F8/F9/FA) after taking gold (CE) is named **Innkeeper** or **Inn**.
- Free healing (beds, machines) is not named: the script cannot tell them apart.
- Visual 183 is the black **Sealed box**. All 17 placements give an item from their Confirm
  handler (CA). Twelve test story point A5 (the charged pendant) themselves; the Forest Ruins
  pair and the three in the Hero's Grave are opened by their own room flags.
- Visual 151 is a **Time Gate**. All 22 placements are gate portals.
- No reward is named.

## Repeated markers

Adjacent interaction markers whose Confirm handlers are byte-identical draw one feature. The
same goes for a marker that only forwards to its neighbour. Examples are Guardia Forest's
two-tile signposts (scene 19 markers 33/34 and 35/36), Choras Tavern's bar and the Keeper's
Dome markers. 29 such groups exist. 19 of them are offered by the field layer, each as a
single row carrying every marker's standing room, which removes 29 duplicate rows; the other
10 are battle-only markers that the encounter layer presents.

## Treasure

`TakaraDataTbl` has 343 records. 12 are alias or filler entries (`x = y = 0`; 0x179690 then
uses the named scene's range), which leaves 331 treasures resolved through 150 scenes.
`FieldContentCoverageTests.EveryInstalledTreasureRecordIsReadAndOfferedUntilOpened` lays out
native memory the way 0x179690 leaves it for every one of those scenes: grid at Engine+E44,
range at FieldState+2190, layer-1 tiles at ActorBase+10FD0 and open bits at ActorBase+110B4.
It then reads the records with `FieldEnvironmentCapture.TryTreasures`. All 331 are offered:

- Of the 344 scene placements (aliases included), 339 are closed chests (collision shape bit 0
  and tile FE/EE/E0/F0). They are offered as treasure chests.
- 4 are hidden pickups with no chest graphic, offered to the guide as item pickups: Truce Inn
  record 1, Magus's Keep children's room record 80, Derelict Factory B1 record 127, and
  record 0 through scene 531's alias.

The remaining placement is record0 outside scene0's map; scene531 supplies its valid alias.
Every record disappears once its open bit is set.

## Limits

- Signs, bookshelves and other scenery drawn in the map are still named "Interactable
  scenery". The script cannot tell a signpost from a barrel, so these need per-marker
  curation.
- Route checks use initial terrain. Actors that scripts move, and terrain that scripts
  change (tile copies), were not re-verified live.
- The separate initial-terrain route harness still has 284 rows without a proved
  route and 68 without an approach. These are unresolved audit cases, not 352
  confirmed game failures: they include cinematic rooms, moved actors, changed
  terrain, missing arrival seeds and event-only maps. Examples needing further
  native-state checks include the castle dining tables, some prison tower
  interactions, Hero's Grave and Fiona's Shrine. The detailed rows are retained in
  `artifacts/research/content-coverage-0335/claude/route-audit`.
- Adjacent battle markers can still appear as separate Enemies destinations.
  Their shared outcome does not mean they share a safe contact position, so the
  object-marker grouping is not applied to contact encounters.

The generation and audit tools are preserved under `tools/research/future_story`:
`audit_content.py`, `audit_treasures.py`, `build_field_content.py`, and
`export_content_fixture.py`. Their reports remain under
`artifacts/research/content-coverage-0335/claude`. The C# regressions exercise the real
navigation and treasure readers; listing coverage does not certify a complete playthrough.
