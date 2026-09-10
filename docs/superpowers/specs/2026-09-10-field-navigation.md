# Field navigation

The user approved this design on 2026-09-10. U/O change category, J/L change
destination, K repeats the destination and direction, I starts spoken guidance
while the player moves, and P toggles automatic walking to the selected destination.
Categories are People, Exits, and Interactable Objects. Information comes from
visible or already discovered targets in the current scene, without hidden rewards
or advance knowledge of destination names.

Chrono Trigger's fields use 2D coordinates, collision shapes, and overlapping
layers. Route planning must preserve layer identity and avoid blocked edges. Native
scene state, not rendered screen resolution, determines positions. The navigation
controller accepts a graph of proven traversable edges and eligible destinations;
it must not turn unknown collision data into passable space.

Destination selection is stable while the scene is unchanged. Changing category
or destination stops the previous route. K always speaks, including repeated
presses. I starts or restarts guidance to the selected destination. P starts a
route if necessary and toggles walking. Directions describe the next part of a
route; arrival stops walking. A destination that disappears invalidates its route.

Automatic movement supplies only normal directional input at the audited field
input boundary. It does not change positions, activate objects, choose dialogue,
or bypass collision. Manual game input, focus loss, menus, dialogue, battles,
scene changes, unreadable state, and a stuck player stop it. Movement is requested
for one native input read only, so no synthetic key can remain held after a hook
stops running. A stopped route never resumes automatically.

The Core project owns selection, bounded path search, and guidance state. Native
capture owns validated memory reads. The Mod project owns field input hooks,
Windows key-edge sampling while the game is foreground, and Prism announcements.
New behavior has regression tests before integration. Native addresses and ABIs
must be audited against the exact supported executable. The user performs all
live game testing; development does not launch or control the game.

The menu pass preceding this feature contained no accessibility errors. Inventory,
Equipment, and Bookmark subpages still announced unsupported coverage boundaries;
their support and opening-scene descriptions remain separate outstanding work.
