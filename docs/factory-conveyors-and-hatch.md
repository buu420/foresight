# Factory conveyors and laboratory hatch

Version 0.3.44 addresses the two Factory reports in the October 5 tester logs: unannounced conveyor movement and a hatch objective that pointed to a hidden terminal. This builds on the existing 0.3.43 bike-race work.

## Player behavior

The mod announces the actual moving-floor direction on entry, direction changes, and exit. K repeats that status with the selected navigation target. Read failures do not claim the player stepped off a belt. Battles, menus, focus loss, and navigation suspension revoke the captured state.

The warehouse's four drawn conveyor robots now have the appearance label **Conveyor robot**, a moving-hazard instruction, and readable positions. They cannot be selected as guidance or automatic-walking destinations: the second log's automatic walk to a generic Object robot triggered another inspection ride. Hidden robots remain absent from ordinary object navigation. The mod does not announce whether the invisible native robot-contact gate or belt gap is armed.

Getting caught by a robot starts the scripted westward ride along the upper conveyor through three battles. The upper belt cannot be entered as an ordinary walking route. On the lower east-moving belt, run west and step south into the gaps to avoid robots. After inspection drops the party at the west end, **Reach the warehouse walkways** binds to the live southern passage exits. In that passage, its eastern door returns to the upper warehouse walkway. To operate the isolated crane controls, the objectives first route through the room before the crane and its western doorway; its eastern doorway returns to the main walkway after moving the barrels. The [Factory Ruins walkthrough](https://www.thonky.com/chrono-trigger/factory-ruins) corroborates the passage and crane sequence; the PC script, exit records, and collision data supply the implementation evidence.

Automatic walking requests the game's native Dash action when opposed input on the lower belt would cancel movement, including upstream alignment corrections. Perpendicular and with-belt inputs remain available at walking speed. It reads the configured run mode and current toggle first. Holding synthetic Dash does not invent the separate release edge that changes the game's toggle. If running requires that manual toggle, automatic walking stops and explains that the player must enable running with their Dash control. An automatic route that needs upstream alignment still requires running; this does not claim that manual perpendicular movement requires it. Movement, combat, interaction, collision, and story progression remain native.

Laboratory Story Events now follows the actual sequence:

1. **Clear the laboratory entrance** uses the active encounter actors and their native contact approaches while the terminal is hidden.
2. **Open the laboratory hatch** uses the drawn terminal and its native Confirm approaches after the encounter.
3. **Climb down to the lower laboratory** uses the live ladder exit after the hatch opens.

The mod does not reveal the hidden terminal as an interactable object. Walking into the selected entrance encounter triggers the native battle; the player handles combat and presses Confirm at the terminal. An unreadable prerequisite produces an explanatory nonspatial note instead of a fabricated destination.

## Native evidence

The supported executable SHA-256 is `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Ghidra 12.1.2 re-decompiled the movement functions from the existing research project in read-only mode on October 6. The local recheck and original proprietary exports remain under `artifacts/research`; they are not bundled in the mod.

- RVA `1734D0` copies cached floor force into movement accumulators before accepting player input.
- RVA `173750` reads the live terrain flags. RVA `178FF0` decodes their speed and direction into motion state `engine+854 -> +88/+8C`. Bits 2..3 select speeds 0, 8, 16, or 32; bits 0..1 select north, south, west, or east. The capture checks that the cached force agrees with those flags and rechecks leader, control, input mode, and scene identity.
- RVA `175A40` selects input speed 16 or 32 using `175C90`. The Dash action is pad bit 8. Run mode, toggle, and held-action counter are at actor base `13FD0`, `13FD4`, and `13FD8`. Only ordinary accepted pad input is changed; no run-state or coordinate writes occur.
- Installed `MapTable_0125` has the upper west-moving speed-32 belt on row 19, the lower east-moving speed-16 belt on row 25, and the north-moving speed-16 turn at column 44. The upper belt and north turn have nonenterable collision layers. `Atel_0042` changes the lower belt gap through native tile copies of flags/layers without changing graphics. Live navigation continues using the captured collision map after those copies.
- From the logged inspection-drop positions `(1153,7154)` and `(1153,6802)`, exit 4 is south without crossing the belt. Scene 232 exit 1 returns to scene 231's upper layer-2 walkway. The crane marker at `(13,9)` lies in an isolated layer-1 nook; scene 233 exit 0 reaches it, while exit 1 returns to the main warehouse walkway. Objectives use live destinations, not offline coordinates as movement targets.
- `Atel_0040` hides terminal actor 12 until controller actor 0 finishes the encounter requested by actors 9..11. The controller reveals the terminal after battle. Terminal interaction opens the hatch and sets global `5C` bit `20`; the opened ladder uses exit 0. The story repair consumes already-guarded encounter targets rather than guessed monster coordinates.

## Verification limits

Regression tests exercise the complete hatch sequence, drawn/hidden robot labels and hazard restrictions, cross-scene passage objectives, native force and run-state capture, announcements without frame spam, K readback, interruption safety, and automatic walking across synthetic east-speed-16 and west-speed-32 floors. The movement simulation uses explicitly open test terrain; it is not a recorded Factory playthrough and does not imply the upper inspection belt can be walked. No live Factory listening, controller run-toggle test, or full warehouse route was performed in this repair session. The existing beta limitations still apply, including unavoidable native encounters and routes that depend on current story/collision state.
