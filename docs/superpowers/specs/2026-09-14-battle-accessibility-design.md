# Battle accessibility

Status: approved on 2026-09-14, with the user's party-reading shortcuts below. Implemented for the 0.3.17 test release; see the final integration notes below.

## Intended experience

The player keeps the game's normal battle controls. When a character becomes available, speech identifies the character and selected command. Moving the native cursor reads the command, Tech, Item, or target it actually selects. Tech and Item choices include the displayed cost, quantity, availability, and description. Target groups identify every highlighted recipient; repeated enemy names remain distinguishable throughout that battle.

Automatically announce displayed battle messages, damage, healing, status changes, and results. Cursor speech takes priority over background updates. Preserve a repeatable current selection and avoid filling the speech queue with unchanged frames or animating health-bar values.

K repeats the current battle selection. Number keys 1, 2, and 3 select the corresponding party member for status reading, announcing the member's name. H reads that member's current and maximum HP; M reads current and maximum MP. This inspection selection is independent of the character whose native command menu is active. Missing party slots are announced without switching to a different member. Existing navigation keys retain their behavior outside battle. Battle activation stops an outstanding automatic route and footstep playback.

Read only information that the game presents visually. For example, ordinary enemy HP is not available merely because it can be read from memory. Native visible HP-revealing effects are a distinct presentation case to audit.

## Native source and integration

Support both classic and touch battle interfaces using their native lifecycle and presentation paths. Validate the executable hash and every hook's bytes and calling convention through the existing hook catalog. Keep native callbacks exception-contained, validate object ownership and bounded reads, and invalidate snapshots on teardown or scene changes.

Add a battle capture layer in Native, battle semantic events/narration in Core, and a battle hook/runtime participant in Mod. The composition must activate and roll back the complete required hook set. Use the current Prism dispatcher and foreground/modifier guards.

Evidence available at design approval (subsequent corrections are recorded below):

- Classic and touch BattleMenu RTTI and their separate update paths.
- The classic base-command renderer identifies the active party slot, native Attack/Tech-or-Combo/Item row, and visibility gates. These fields are distinct from the Tech list cursor and page.
- The target cursor commits a battler ID from its candidate table. The separate table used by touch hover is identified, but group membership is not yet proven.
- Both interfaces call the same party HP/MP formatter at RVA 0x1BDD0.
- That formatter reads three visible party slots, with native custom character names and current/maximum HP/MP.
- The battle message display routine at RVA 0x19940 resolves localized text and invokes the native name/number formatter before setting the rendered label.
- The result driver uses that display routine for EXP, TP, gold, obtained items, level increases, and learned Techs. Escape messages also reach that routine.
- Numeric and miss popup writers feed a shared renderer. Per-presentation serials can distinguish consecutive identical hits from repeated animation frames without inferring damage from HP differences.
- A native displayed-message path identifies the enemy-name index at canvas + 0x1A018 + targetSlot*4. Its separate HP-reveal reads must not supply ordinary enemy HP.
- The battle scene owns its menu at +0x18CC. Exact activation and teardown callback signatures still require validation; menu +0x368 must not be assumed to point back to that scene.

Research evidence is in `artifacts/research/battle-0317/`. Claude owns the Ghidra command/target/lifecycle audit; Codex owns party display, result text, composition, and verification. Offline disassembly does not establish live coverage.

## Acceptance checks

1. Enter, leave, and re-enter battle without stale focus, stale party data, navigation input, or footsteps carrying across the boundary.
2. Read the ready character, each native command, Tech and Item list choices, cancellation, cursor memory, unavailable choices, and target changes.
3. Distinguish same-named enemies and identify native group targeting without including non-highlighted recipients.
4. Read party HP/MP on demand and announce visible combat outcomes once per presentation, including repeated identical hits.
5. Read victory rewards, level/Tech gains, escape messages, and defeat presentation as actually displayed.
6. Preserve existing startup, menu, dialogue, navigation, and footstep behavior; verify full composition and hook-byte contracts.
7. Build and package the confirmed design, deploy transactionally while the game is closed, and compare installed payload hashes. Report any remaining live validation explicitly.

## Final integration notes

The integrated reader covers command and list focus, native group markers, applied party visual statuses, finished visible message labels, discrete popup writers, and the shared battle menu lifecycle. Sections 15-17 of `docs/battle-interface-native-audit.md` and the companion feedback audit record the final native corrections and offline checks. Descriptions are read through actual message presentation; the battle Item list has no help text to add.

The popup motion-table difference remains unproven as an HP/MP distinction. Popup speech therefore reads damage/recovery and the exact displayed amount without adding an HP/MP unit. H and M use the proven HUD fields. Element-icon-only message meaning needs further capture. Both interfaces, battle results and defeat still require live testing; synthetic fixtures are not that evidence. Deployment and final tests are recorded in the implementation plan and release proof.
