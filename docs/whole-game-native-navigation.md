# Whole-game native navigation

The PC resource index extends field navigation beyond the first End of Time visit.
People, unopened treasure, floor pickups, usable scenery and exits use the same
categories and keys as the earlier chapters. The currently loaded scene supplies
positions, collision, active actors, treasure state and exit cells. Story objectives
and guide entries do not require camera discovery.

## Evidence and generation

The supported executable SHA256 is
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
`tools/research/future_story/build_catalog.py` checks that hash, decodes the installed
PC resources, and generates the embedded `Navigation/Data/game-navigation.json`.
The index contains all 669 Mapinfo records, 1,020 static room exits and the eight
world maps' 240 entrance records. The reproducible pickup report covers all 343
native treasure records and 143 actor identities with item-giving script actions.
`report_catalog_coverage.py` records those counts without extracting reward contents. Eight unused scene records have missing or stub
scripts; they are recorded explicitly. A script index does not imply a reachable
in-game area: the unused Arena entrance and the world-table destination 1023 are
not offered as available locations.

Ghidra and instruction checks verified the PC opcode lengths, immediate byte
comparisons, global banks, extended cells, inventory checks, coordinates, transition
operands and actor identities. Research outputs are under
`artifacts/research/full-story-0323/`. The metadata compiler follows bounded control
flow and actor requests. It records incomplete expansions explicitly, never presents
partial paths as a completed expansion, and does not run an event interpreter in the
game. Dialogue text and unrevealed treasure rewards are not embedded in the catalog.
Condition reduction uses a stable order across compiler processes. Exhaustive
three-flag truth tables check that reduction preserves availability; separate
processes with different hash seeds check reproducible output.

The native script state includes all 512 expanded global bytes, 256 encoded local
bytes, 80 extended cells, held item counts, the active/reserve roster and gold.
Reads are bounded and repeated; missing or changing cells remain unknown.
The extended range stops before the actor array. Inventory uses the native C9
search's 347 twelve-byte records; absent items are zero only after a complete read.
Party membership follows D2/CF's first-three/all-nine roster search, and gold follows
CC's native comparison. These states select script actions; they do not reveal
unopened chest contents.

## Routes and availability

The connection router chooses an onward passage using scene connectivity, then
binds it to a target actually captured in the current room. A script passage must
satisfy its current predicates. Look-ahead ignores another room's local variables
and permits changing active party members; it does not invent local movement
coordinates. World navigation continues to require live, enabled entrances and
the current connected land region.
Continuation routes cannot loop back through the current room or world map, so
a temporarily unavailable forward passage does not send the player out and back.

Startup script regions cover gates, encounter triggers and other coordinate-based
actions whose actors have no useful sprite position. Their native coordinate tests
are intersected with the live collision map. Separate intervals stay separate.
Warp regions and live touch-portal cells are terminal for unrelated routes, preventing
a route to an object from walking through a gate on the way. Same-scene drops stay
available because another disconnected section can share the same scene identifier.
Automatic conveyor battles and Omen elevator rides have audited look-ahead links;
these do not create walking destinations inside a cinematic.

The Ocean Palace entrance is another cinematic continuation: Atel0206 actor8
sets Global1F1 bit40, then enters world500. The native world-state mirror at
RVA270720 maps this flag to system1BA8. Event_0004 tests that bit at0013,
calls the underwater sequence, clears it at036C, and changes to404 at0370.
The planner therefore connects that live portal to404 after Dalton, without
inventing an Ocean Palace entrance on the ordinary world map. Its position and
Confirm approach still come from the captured actor, not a guessed floor tile.

Ocean Palace scene406 packs six separate rooms into map177. The native tile
planes have isolated occupied bands9..18,29..38,50..59, split into left/right
rooms. `OceanPalaceRoutes` selects the exact native exit serving the player's
current section. It sends upper switches through hall405 exits1/7, the central
switch through407, and lower switches through the great stair and elevator,
then412 exits2/1. Scene406 exits5/6 connect pairs of upper rooms within the same
scene ID. Elevator416's compulsory battles end at418 (Atel0224 actor11 DF05A5).
This prevents an upper-hall route from mistaking a lower-room switch for an
adjacent object just because their scene identifiers agree. The live collision
graph still supplies every movement step.
The two visions of the Mammon Machine are recorded as progress interactions,
not shortcuts into its playable room: Atel0223 actor13 returns the first to404
with point195, and the stair vision to409 with point198. The entrance trigger
retires at195 and no longer acts as a terminal across the exit corridor.

Field exits also read the current expanded 28-byte records at engine+E64, indexed
by the scene offsets at engine+E58. Native 178FF0 copies record+10 into the pending
destination. A stable runtime destination overrides the static connection, including
connections changed by later areas. If destination records are unreadable, the
collision map and current exit cells remain usable.

Optional quests remain in People, Objects or Exits. Ordinary nearby targets remain
available alongside quest guidance. Collected treasure, removed actors, retired
scenery, and disabled passages are re-evaluated on each capture. Existing counted
footsteps and one-leg manual instructions are preserved.

## Sources

- [The user's walkthrough by vinheim and Bkstunt](https://gamefaqs.gamespot.com/snes/563538-chrono-trigger/faqs/64344)
- [Thonky's Chrono Trigger guide](https://www.thonky.com/chrono-trigger/)
- [CTViewer](https://github.com/GitExl/CTViewer), for resource-format and opcode references
- [Temporal Redux](https://github.com/OnemusCT/temporal-redux), for cross-checking event terminology

Community documentation is cross-checked against the installed PC build; SNES
operand sizes are not assumed to apply to the PC release. The executable, live
capture contracts and the installed scripts remain the source of route geometry
and quest-state decisions. See `full-story-objectives.md` for the chapter and
optional-quest chains, and `vehicle-navigation.md` for Epoch and Dactyl handling.
