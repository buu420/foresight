# Vehicle navigation: Epoch, Dactyls, and the time gauge

This change extends world-map navigation to the two flying vehicles and reads the
Epoch's era selector. The existing U/O, J/L, K, I, P keys and F8 are unchanged and
work the same way in flight. Nothing here presses Confirm, opens the gauge, or
writes a coordinate; the mod only adds direction bits at the game's own input
sites and reads the game's own prompt flags.

Native evidence for every address below is in
`artifacts/research/full-story-0323/vehicles/vehicle-navigation-native-audit.md`
and its Ghidra decompiles. Executable SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

## What the player gets

- **Parked vehicles.** When the Epoch is in the current era, or the Dactyls in
  65 000 000 BC, Interactable Objects lists it at its exact native position with
  "Press Confirm to board." Routes use the walking graph; a vehicle on another
  landmass is listed but not routable.
- **The boarding prompt.** When the party stands on a vehicle the game draws an
  icon above them (world layer 2951B0 reads A+109B5). The mod speaks "On the
  Epoch. Press Confirm to board." once per rising edge. Boarding, take-off and
  landing animations are not controllable; navigation waits for the real flight.
- **Flight.** While the Epoch (with wings) or the Dactyls are flying under player
  control, the same categories and keys work. Exits lists every enabled world
  entrance whose surroundings contain a legal landing site; each route ends at a
  landing site that is walk-connected to that door, with "Press Confirm to land,
  then walk to X." Story Events binds the current objective to those entrances
  through the existing full-story targets. The area name reads "World map, 600
  A.D., flying the Epoch" or "riding the Dactyls".
- **The landing prompt.** Hovering with no input where landing is legal sets
  A+109B6 and draws the icon; the mod speaks "Landing possible. Press Confirm to
  land." once per rising edge. Automatic flight releases the direction when the
  route ends, so the game computes that flag on the next frame.
- **Story routing through a vehicle.** When the objective's destination is an
  interior reached through an entrance on another landmass of this era, or in
  another era, its guide binds the objective to the
  nearest reachable vehicle: "Board the Epoch and fly." or "The destination is in
  600 A.D. Board the Epoch and use its time gauge."
  The original wingless Epoch can change era; only map flight requires wings.
  At story counter211 the next step binds to boarding and taking off in the Epoch.
- **Black Omen.** The Epoch's Exits list includes the Omen when its native contact
  task and current-era presence flag exist. Routes end at the native contact area,
  followed by "Press Confirm to open the Black Omen boarding choice." Confirm and
  the choice remain manual. The future platform can be approached, but its door
  does not open; optional dungeon objectives are offered only in playable eras.
- **Time gauge.** Opening the gauge presents a menu: "Time gauge. 1000 A.D., 4 of
  7. Current era. Not selectable." Moving the highlight speaks the era name and
  its description from the game's own text; committing speaks "2300 A.D.
  selected. Traveling to 2300 A.D."; cancelling speaks "Time gauge closed." The
  gauge's keys are the game's: up or right moves toward the End of Time, down or
  left toward 65 000 000 BC, Confirm travels to a highlighted era other than the
  current one, Cancel closes.

## Native boundaries

| Boundary | RVA | Bytes | Use |
|---|---|---|---|
| Epoch task tick | 2766EA | `E8 E1 7A 01 00` | call 28E1D0 in the task dispatch 276590; every frame the Epoch actor exists |
| Dactyl task tick | 276718 | `E8 C3 3A 01 00` | call 28A1E0; every frame the Dactyl actor exists |
| Epoch pad, hover gate | 28E8C2 | `0B BE 1C 33 00 00` | state 0x0F of 28E1D0 |
| Epoch pad, dispatch | 28EAB9 | `0B BE 1C 33 00 00` | state 0x10 |
| Dactyl pad, hover gate | 28A761 | `0B BE 1C 33 00 00` | state 4 of 28A1E0 |
| Dactyl pad, dispatch | 28A94C | `0B BE 1C 33 00 00` | state 5 |
| AgeSelectScene::init | 2989B0 | `55 8B EC 6A FF 68 BF 95 77 00 64 A1 00 00 00 00` | gauge opened |
| AgeSelectScene::update | 2996D0 | `55 8B EC 83 E4 F8 51 A1 DC C3 81 00 85 C0 53 56` | highlight, commit, cancel |

The four pad sites are the same `or edi,[esi+0x331C]` instruction as the three
walking sites 265147/265247/26536F, with ESI the world context and EDI the pad;
the probe and its register contract are reused unchanged. A Capstone scan of both
task functions confirmed that no branch targets the interior or the byte after any
of the four sites, and that the two tick sites are `je` targets only at their first
byte. Direction bits 0x800/0x400/0x200/0x100 are the native ones; the mod injects
nothing else. Physical input at any site, a transport or master-action change, a
menu, dialogue, battle, or focus loss cancels automatic flight, as on foot.

The vehicle ticks fire for a parked vehicle too. Only the tick whose capture proves
that vehicle is the player's current transport (`D+2E27C==1`, `D+2E27E` 2 or 3,
`D+2E280==0`, flying bit 0x40 in `D+2E294`/`D+2E29E`, wings for the Epoch, no
landing bit for the Dactyls, `D+2E04E` naming an actor whose task word is 0x42DD
or 0x4CC5 in state 3 or 4) reaches the navigation runtime; every other vehicle
tick leaves walking navigation alone. If the active flight loses control, its
direction is cleared immediately.

## Movement model

Both vehicles fly at 2 px per frame in eight-pixel segments and re-read input
after each segment, the same lattice and unit (128 fine units) as walking. The
Epoch graph is free flight over the whole map. The Dactyl graph refuses a segment
when any of the four chips 267E40 samples at the target (columns x-1..x of rows
y-1..y, x=X>>3) has both low property bits set. Neither graph routes across the
map's wrapping edges: the core route model has no seam legs, so the long way is
planned and the seam is left to manual flight.

Landing follows the native tests exactly: all six chips in columns x-1..x of rows
y-1..y+1 must have property bits 0..2 clear; the era rectangles of 286FE0 in 8-px
tiles (1000 AD x 0x41..0x71, y 0x64..0x7E; 600 AD x 0x60..0x6D, y 0x76..0x7E;
65M BC x 0..0xD, y 0..0x33; 12000 BC x 0..0x1F, y 0x60..0x7E) and the Dactyls'
own copy of the 65M BC rectangle are forbidden; and the other vehicle must not be
under the landing spot. Runtime reads the directional sprite extents at
D+34946/3494E and uses the strict opposing-side comparisons from264530/264690.
The installed records contain eight pixels per side, making the overlap boundary
16 pixels. Landing search follows the whole walking component until it finds
16 legal sites; the Dactyls additionally require those sites to be in their current
flight component. There is no fixed distance cutoff around a door. The Omen's
boarding contact area is excluded from ordinary landing suggestions.

Vehicle positions are `D+2E290/292` (Epoch) and `D+2E29A/29C` (Dactyls) in world
pixels; take-off and landing leave the logical position unchanged.
Routes preserve the actual position modulo eight pixels. Mid-segment captures
read actor+2A/+2C and finish that committed endpoint before offering a turn.
Disembark2673B0 copies the vehicle position exactly to the party, so the walking
capture also reads the player task's committed endpoint. Entrance and boarding
approaches are chosen on that same attainable lattice. Ordinary zero-phase
walking and the separate footstep motion capture retain their existing behavior.

## Footsteps

Flight is silent. The walking capture still rejects any transport other than 0,
so boarding walks, flight, and disembarking never produce a footstep frame; the
vehicle ticks are not wired to the footstep runtime at all; and a manual flight
leg is never synchronized with the footstep counter. Mode changes reset the
partial stride as before.

## Integration API for story routing

`WorldNavigationSource.Build` adds parked vehicles as Objects
(`vehicle:epoch`, `vehicle:dactyl`) and, after `FullStoryTargets.Build`, calls
`VehicleStoryRouting.Reroute(storyTargets, world, story, reachableVehicles,
epochWings, worldDestinations, player, label)` on its own `VehicleStoryRouting`
instance. Already bound walking objectives remain available. Unbound main and
optional objectives are considered using `SceneConnectionRouter.DistanceWithinFields`:

1. A live entrance's interior reaches the objective without a time Gate, travel
   cinematic or another landmass: the objective is bound to the nearest reachable
   vehicle: "The way there is not on this landmass. Board the Epoch and fly."
2. Nothing in this era reaches it and the Epoch is reachable: the objective
   is bound to the Epoch naming the era whose entrance reaches the interior: "The
   destination is in 600 A.D. Board the Epoch and use its time gauge."

The Dactyls never supply an era connection, and a wingless Epoch never supplies a
local flight connection. The rebound target keeps the
objective's id, label and category, takes the vehicle's position and approach points, and
sets `GuideAvailable`. The navigation source can call the same method after binding,
read `WorldNavigationSnapshot.Vehicles` (`WorldVehicleState`), or construct
`VehicleStoryRouting` with an injected distance function. The
era-name sentence helper `VehicleStoryRouting.Sentence` avoids a doubled period
after "A.D.". `WorldNavigationSource.CaptureFlight(context, kind)` returns the
flight frame whose Exits carry landing approach points and whose Story Events come
from `FullStoryTargets.Build` over those exits.

When only Dactyls are available, same-era walking and flight bindings are evaluated
first. An unbound objective can still fall back to walking toward a real time Gate,
provided the captured story and inventory satisfy that Gate's requirements.

## Tests

Native: `VehicleNavigationCaptureTests` (transport, flying and wings bits, actor
task and state, torn reads, parked state and prompt flags, walking versus flight
capture), `TimeGaugeCaptureTests` (slot table, labels 0xAD-slot in bank 0x23,
commit and cancel), catalog contracts and counts. Mod: `VehicleFlightGraphTests`
(water and cliffs for both vehicles, wrap edges, lattice joins),
`VehicleLandingRulesTests` (six-chip test, every era rectangle, other vehicle,
walk-connected sites, island doors), `VehicleNavigationSourceTests` (parked
targets, unreachable vehicles, flight frames, Dactyl graph and Epoch clearance,
reroutes across water and across eras), `VehicleNavigationRuntimeTests` (parked
ticks never poll or cancel walking, flight keys and pad sites, manual cancel,
silent footsteps, landing hand-off), `VehiclePromptAnnouncerTests`,
`TimeGaugeHookSetTests`, and the extended world hook-set and composition tests.

## Limits and remaining live verification

No game input or save change was performed during this audit. Deployment is
recorded separately in the release notes. Not yet verified live:
the four pad sites and two tick sites executing under Reloaded in the running
game; the Epoch's
custom name (the mod says "Epoch"); the exact era text the gauge shows for an
unrevealed era; and the era label used for another-era rerouting, which uses the
plain era names rather than the world map's knowledge gate. The next live test
should board the Epoch, fly a route with P, land with Confirm, open the gauge, and
travel once.

Offline native evidence in `artifacts/research/full-story-0323/vehicles` includes
`decomp-vehicle-corrections.txt`, `decomp-walking-phase.txt`, and
`vehicle-geometry-replay.json`. The replay executes installed x86 overlap, Omen
contact, disembark and endpoint routines in an isolated Unicorn memory image;
it does not run the game or synthesize game input.
