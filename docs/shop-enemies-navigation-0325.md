# Shop and field navigation, 0.3.25

This update adds speech to the Steam shop, adds Enemies to navigation, and repairs
approaches to interactions across counters and to treasure on another stair landing.
The shop implementation and native evidence are in [shop-native-0325.md](shop-native-0325.md).

## Enemies

The category order is People, Exits, Interactable Objects, Story Events, Enemies.
U/O changes category; J/L chooses a target; K repeats; I gives spoken directions;
P toggles automatic walking. Existing category numbers remain unchanged.

Enemies uses the native script catalogue's Encounter regions: the installed data
contains 506 such records across 137 scenes. These describe battle triggers under
leader-position and game-state conditions, not a count of unique enemies or a
claim that every battle is covered. The label is Encounter. Completed or unavailable
triggers are removed by their native guards. Creature appearance alone does not
turn a peaceful character into an enemy, and unrevealed names, parties and rewards
are not announced.

## Counter interaction

The confirm selector at RVA 17D230 records the player's coordinates and dispatches
by facing to 17D4C0, 17D610, 17D760 or 17D8B0. Their machine instructions require
an along-facing gap below 448 fine units and a lateral gap below 192. One tile is
256 units. An actor surrounded by restricted floor needs candidate standing
positions inside this interaction range, rather than a nearest-floor guess.
Ordinary open-floor approaches remain one tile away. Treasure uses its existing,
separately audited interaction rules.

Truce Market (scene 118) uses actor 9 at the counter to forward the conversation
to actor 8, the Shopkeeper. The counter's talk entry is `02 10 11`; the Shopkeeper's
talk function contains the shop instruction `C8 81`. The visible Shopkeeper and the
place where Confirm acts are therefore two different native actors. The navigation
binding uses the active counter's live position for this exact verified pair.
The same pure script-call pattern is audited for Melchior's Cabin and the
Keeper's Dome. Both actors must match their native identities and retain enabled
script calls; only a successfully bound duplicate is suppressed. Approach points
use exact coordinates and leave room for the 32-unit arrival tolerance on both axes.

No whole-map reachability flood runs during frame capture. Candidate approach
points are bounded; the route search determines which of them can be reached.
Failed-route diagnostics retain the actual count of submitted approaches.

## Stair landings

The castle stairwells contain separate landings connected by exits that return to
the same scene. A route to treasure 235 in scene 468, or 242 in scene 480, from the
bottom floor must first take the stair exit to the middle landing. FieldLandingGraph
uses the native exit cells and the catalogue's destination arrival coordinates to
find this intermediate leg. It announces the stair exit rather than claiming that
the player has reached the treasure. A changed live exit destination overrides the
catalogue, and a required passage retains its own intermediate identity. Normal cancellation on a scene or control
change remains in force; resume navigation after a transition if it stops.

## Evidence and limits

Read-only shop captures and Ghidra/machine-code audits are saved under
`artifacts/research/shop-enemies-0325/`. Tests replay the user's action menu, Buy,
Sell and quantity screens. They also cover native comparison indicators, empty
lists, Equipment ownership, encounter guards, counter geometry, and staged stair
routes. Synthetic cases are marked in the tests.

Older Guardia Forest failures for actors 59, 61 and 62 are not established as fixed
by this release. The static reconstruction does not agree with the old log's exit
connectivity, so it cannot justify inventing a route or removing those targets.
A fresh failure capture is needed if those destinations still fail on this build.
This release has not received a complete live playthrough or a live shop purchase.

## Release checks

Release tests: 1,799 passed (Core 145, Native 790, Mod 856, Prism 8),
with the three real-time footstep tests run separately. The native script decoder
and navigation compiler passed 20 tests. All 151 native hook signatures match the
supported executable. The package was installed on September 18, 2026; all 25 deployment checks passed.
All 28 mod files, 45 loader/shared-hook files, and two native launcher files match
their reviewed source payloads. The game executable is unchanged.

Implementation commit: `0b8359a9a33c89f2186c3941ce247a2e9a26b12f`. Installed Mod DLL SHA-256:
`BB4B092C4DEFC53F9EBD98BA3532C64D05400FECA317D06C22C525535FAA0E3A`. The previous installation is retained in
`Accessibility/Backups/20260918-115742` under the game directory. Detailed hashes
and test-result paths are in `artifacts/research/shop-enemies-0325/deployment-proof.json`.
