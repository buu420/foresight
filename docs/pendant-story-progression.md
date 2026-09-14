# Pendant story progression repair, 0.3.14

The September 13 log shows the girl repeatedly saying she has lost her pendant while Story Events still selects "Talk with the young woman". The pendant actor is drawn at `(7808,2303)` in scene 439 but is missing from Interactable Objects and the story candidates. Its captured fields are class 4, visual 99, index 15, draw 1, activation byte 0, binding 128, and loaded flag 0.

Two conditions caused the failure. The source required the native activation byte for every object, excluding this scripted pickup. The story catalog also continued to list talking to the girl after that conversation was complete.

## Evidence

The installed scene table maps scene 439 to `Atel_0074.dat`. The script SHA-256 is `574098067D910D56A9B30707F3DC837C56990AF788CAE55374A9C90CF4C04307`. File offsets and global indices below are hexadecimal.

| Offset | Native script behavior |
|---|---|
| 0673 | Actor 3 speaks message 0028, then sets global 55 bit 2. The localized message is the missing-pendant conversation. |
| 06B6 | Collision sequence sets global 54 bits 4 and 6, then story point 6. |
| 08DC | Actor 15 loads NPC visual 63 hexadecimal, the pendant. |
| 08E8 | Actor 15's initialization uses global 54 mask 10 to show the pendant. |
| 0909 | Pickup sets global 54 bit 5 and clears bit 4, then the script removes the actor. |
| 065D | Joining the girl advances story point to 8. |

Global bit operations and the four-byte native storage stride follow the existing executable audit recorded in `FieldStoryCapture`. The [CTViewer global-memory documentation](https://github.com/GitExl/CTViewer/blob/2e5a206e09f0028fd5a1ca6cb9a9ed18bfc64fea/docs/scene_scripts/memory_areas.md) and its opcode decoder provide the resource-format reference. The user-supplied Vinheim/Bkstunt guide recommends talking to the girl before retrieving the pendant; the native 55/04 flag records that conversation.

## Behavior

The exact scene/actor/class/visual combination can provide an object target while the native dropped-pendant flag is set and the collected flag is clear. It must still be a usable, drawn actor inside the current map with a live script binding. Coordinates and approaches come from the running game. This exception does not promote other sprites or bypass collision.

After global 55/04 is set, Story Events replaces the completed conversation with "Pick up the fallen pendant". After collection it selects "Return the pendant to the young woman", and after she joins it continues toward the exhibit. The pickup also appears under Interactable Objects and follows the existing guide availability rules. Relevant global values are included in bounded inventory diagnostics for subsequent live validation.

The regression failed on the previous source. It passes captured actor facts through field target building, category selection, K repeat, and automatic route planning, then checks collection and onward progression. It also rejects wrong scene/actor/visual, removed or hidden sprites, unknown flags, and incoherent scene state. All 174 navigation tests passed. An in-game playthrough remains the final verification of the new build.

The script checks, log excerpt, and test results are saved under `artifacts/research/pendant-story-0314`.
