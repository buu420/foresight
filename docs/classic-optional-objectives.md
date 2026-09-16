# Classic optional objectives: native audit

`ClassicOptionalObjectives` supplies 119 named actions across the six classic sidequests, Crono's revival, Lucca's campfire event, and the Black Omen. It emits one current action per quest, and only in that quest's rooms or the relevant travel hubs. Conversations use People; switches, encounters, and pickups use Objects; actual passages, time gates, and elevator rides use Exits. There are no optional StoryEvents notes without a position.

The descriptions identify the action. Positions, active actors, touch regions, exit records, collision checks, and final route availability still come from the native navigation catalogue and live frame. Ordinary chests, sealed chests, and item sparkles remain in the generic native object catalogue; this provider does not reveal unopened chest contents or duplicate a static treasure list.

## Evidence and reproduction

The primary evidence is the installed PC `resources.bin`: `Game/field/atel/Atel_*.dat`, `Game/common/MapJump*Tbl.dat`, and `Game/field/MapTable/MapTable_*.dat`. Packet offsets below are offsets in the original Atel asset, not offsets after removing its actor header. The shared Ghidra audit is in `artifacts/research/full-story-0323/native-opcodes.txt`, `native-pc-opcodes.txt`, and `native-item-opcodes.txt`. Their supported executable SHA-256 is `8fe9d75e4cdc279645c5bc932fc163fd67147255fc0c673ac45bbf0a6d2e00d7`.

`artifacts/research/full-story-0323/optional/raw.py` walks the actual PC instruction lengths using `decode_verified.py`; `writes.py` lists persistent writes; `map_sections.py` decodes the installed tile planes for auditing disconnected sections. Run them from the worktree root. The generated `raw-*.txt`, `*-writes.txt`, and `map-sections.txt` retain the evidence used here. The map dump describes section boundaries only. It is not a replacement for the live pixel collision graph.

The web walkthroughs provide a cross-check on the sequence: [Sunken Desert](https://www.thonky.com/chrono-trigger/sunken-desert), [Cyrus's Ghost](https://www.thonky.com/chrono-trigger/cyrus-ghost), [Sun Stone](https://www.thonky.com/chrono-trigger/sun-stone), [Origin of Machines](https://www.thonky.com/chrono-trigger/origin-of-machines), [Rainbow Shell](https://www.thonky.com/chrono-trigger/rainbow-shell), [Ozzie's Fort](https://www.thonky.com/chrono-trigger/ozzies-fort), [Time Egg](https://www.thonky.com/chrono-trigger/time-egg), and [Black Omen](https://www.thonky.com/chrono-trigger/black-omen). Actors, guards, flags, and coordinates come from the installed PC data rather than those guides.

In the following table, `Gxxx/mm` means global byte index `xxx` with mask `mm`, both hexadecimal. Locals are room scratch cells and are never read to choose a room section outside their owning scene. Persistent handoff flags take precedence over inventory, so a consumed quest item does not restart its collection step.

## Quest state and corrected bindings

| Quest | Native facts used by the selector |
|---|---|
| Fiona | Sapling consent is `G0F7/02`, written by scene401 actor12 or scene424 actor25. Fiona158 actor8 starts the request (`G1AD/80`). Retinite/Melphyx dies at `G1A3/01`; `/04` only marks emergence. Scene161 actors2–5 are four alternative native encounter regions for the same boss. Fiona158 actor8 accepts active Robo and sets `G19E/01`. Robo57 actor8 sets `G19E/02`, recovers him, and launches the campfire. The four hundred years pass between leaving Robo and recovering him at the shrine. |
| Lucca's past | Scene436 leads to gate83 actor4 and then room10. The past house mode is `G05A/02`; `/04` means the red gate was used. Machine4 actor12 implements the timed password. `G07C/40` is resolution, whether success or timeout; `/80` distinguishes success. Gate10 actor13 returns to83 and sets `G06A/10`. Robo83 actor2 finishes the scene and provides its reward. Ordinary Lucca's house does not receive a stale past-event objective. |
| Cyrus | Carpenter188 actor15 sets `G19E/10`; his descendant62 actor11 grants permission `/20`; wife61 actor8 gives tools `500B` and sets `/80`; delivering them to188 sets `/40`. Carpenter186 actor8 sets repair state in `G19F`: `/02` initial work, `/04` lower repair, `/08` upper repair, `/01` work report pending. Scene65 startup clears the pending bit, and sets `G1AC/80` when both later repairs exist. Scenes66 and68 set monster-clear bits `G19F/20` and `/40`. Grave73 actor8 requires active Frog and 600 AD, and awards the sword via the internal Frog function; completion is `G1A3/40`. `G1A3/08` and `/10` describe the current ruins era, not completed repairs. Shared ruins objectives are suppressed in the wrong era. |
| Sun Stone | Son of the Sun251 actor8 leads to `G13A/01`; Moon Stone251 actor17 sets `/02`. Shrine327 actor8 consumes `500F` and sets `/04`. Checking present shrine64 actor9 sets `/08` (missing stone) and `G139/01` (noticed); neither completes the quest. Jerky53 actor9 costs 9900 G and grants `500C`. Woman154 actor11 sets `G1D2/04` only for giving the jerky away; selling it consumes the item without kindness. Mayor50 actor8 returns the Moon Stone, clears `/08`, and sets `G13A/10`. Shrine64 actor9 replaces it and sets `/20`. Future shrine255 actor8 requires active Lucca and sets `/40`; her automatic workshop reward sets `/80`. |
| Geno Dome | Entry248 actor8 requires active Robo and lets the native event place him in the lead. Intro completion is `G13C/01`. First doll switches are actors14,15,16 in256: `G13D/10` and `/40` on, `/20` off. Actor17 opens charging pod `/80`; actor25 charges Robo (`G13C/40`, temporary); actor27 opens the first pod (`G13E/04`), and doll23 sets `G13C/04`. Reversal actor12 sets `G13D/04`, barrier13 `/08`, receiver-open11 `/02`, and charged receiver28 `G13E/02`. Robot8 starts following on touch; local12 distinguishes following from waiting; guardian meeting sets `G13C/80`, and doll22 sets `/02`. Atropos268 actor29 completes `G13B/20`. Left pedestal10 sets `G13C/10`, right pedestal11 `/20`. Mother Brain268 actor30 completes `G13B/10`; `G13F/80` is an unrelated ordinary encounter. |
| Rainbow Shell | Toma188 actor13 gives drink5014 and sets `G1A0/02`. Tomb63 actor8 consumes it and sets `G1A3/80`; `G1AC/10` is the nearby capsule, not Toma's promise. Rust Tyranno197 actor0 sets `G1D2/40`. Shell197 actor10 sets `G1AF/02`, but leaving its room is a separate action: actor1's southward region sets `G0A9/80` and warps to120. Guard441 actor8 starts the trial with active Marle, leading to `G050/20`. Shell440 actor9 supplies fragment5009 (`G0A2/80`) and invokes the trial scene. Post-battle `G050/40` and `G06D/10` enable Melchior440 actor23. Equipment selection sets `G06D/20`. With Sun workshop completion `G13A/80`, talking again gives the combined rewards and clears `G06D/10`. |
| Ozzie's Fort | Flea183 actor9 completes `G1A1/04`, Slash184 actor9 `/08`. Scene179 actor0 handles the blade sequence; `/10` marks passing it, while `/20` alone is the earlier approach. Scene180 actor8 leads to the three-enemy battle (`/40`). Ozzie181 actor8 first causes the switch/floor encounter (`G1A0/80`); returning to him completes `G1A1/80`. |
| Crono | Gaspar464 actor28 calls the Time Egg event (`G07C/01`, counterD5). Bekkler434 actor11 awards a doll to Crono's house: `G05E/01` is Crono's doll, while `/40` is Magus's. Mother2 actor8 sets permission `G14F/01`; doll2 actor19 clears ownership and gives inventory5013. Nu242 actor16 prepares the Poyozo dolls (`G070/04`), after inspecting the doppelganger. Poyo244 actor9 becomes the windbreak (`G07C/04`); actor10 is a capsule, not another windbreak. First Spawn264 actor0 sets `G064/08`, sparkle246 actor8 `G058/80`, second Spawn247 actor0 `G064/10`, last Spawn495 actor8 `G064/20`. Scene495 controller reads shell9 into locals11/12 and checks `(16,15)` for the ladder. Summit265 begins the revival automatically. `G057/20` is transient staging; `G057/40` is actual completion. |
| Black Omen | World presence is `G1F7`: `/08` 1000 AD, `/04` 600 AD, `/02` 12000 BC, `/10` 2300 AD. The future entrance is permanently sealed. Scene449 `G1A8/02,/04,/08,/10` selects the era and is not a completion mask. Mega308 actor0 completes `G1A8/01`, Giga323 actor10 `G071/10`, Terra325 actor16 `G15A/01`, and Spawn96 actor0 `G1A9/01`. `G15A/02` is the preceding Panel encounter, not Terra. The remaining stages are Zeal451 actor8, automatic Mammon battle422, and final Zeal107 actor9. Clearing Zeal removes the current and later eras' `G1F7` bits; an earlier era can remain available. |

## Nontrivial routes

### Geno Dome's conveyor circuit

The reversal switch cannot be reached from the first-doll side of scene256. The provider first targets its southeastern exit3, then the elevator127, upper floor268, long passage128, rear door267, and rear elevator back to256. Actor0 in256 continuously copies player X/Y to locals8/9. The separated control section is the native map's northeastern area. Only arrival on that side changes the objective to actor12.

Elevator127 stores previous scene low byte in local6 and previous entrance X in local7. These values distinguish boarding, riding, and getting out without substituting unrelated alternatives for the same objective. In267, actor8 opens the rear elevator door and local6 records that it is open. Tests cover the full circuit and ensure coordinates from another scene cannot skip it.

### Giant's Claw

Scenes195,109,110,196 each contain multiple separate sections. The scripts continuously copy leader X/Y into locals7/8. The installed tile-plane dump establishes which section each native exit serves. The route uses exact exit IDs and terrain regions:

1. 195 exit1 → throne297 exit0 → 195 exit9.
2. Scene110 actor1's left floor switch at its native `(10,26)` predicate; the local9-gated hole warps to the lower section of the same scene.
3. Scene110 actor2's left skull switch uses `(28,23)`, then exit1 →196.
4. 196 exit3 moves to its upper section; exit0 →296, then exit2 →109.
5. 109 exit7 →195 exit5 →109's chest room.
6. Actor12's chest sets local11 and starts the drop; exit1 then moves between the two prison sections of109.
7. Actor13 opens the bars (local12), exit10 leads to197.
8. The boss, examining the shell, and leaving its room are three separate actions.

Only the safe left switch's precise terrain target is used. The right switch is not an equivalent target. The native self-scene exit IDs are retained, so a generic scene-level shortest path cannot send the party back through the door just used. Side-path returns from the broken ladder have their own native exits.

### Black Omen doors

Before the next dungeon passage exists, four local steps can supersede the next boss: scene315 actor8 opens the wall above the Nus (local8); 324 actor20 opens the lower door (local10); 325 actor17 opens the next door (local10); 326 actor8 opens the last corridor door (local6). The entrance449 door uses local12. Warp pads and elevator transitions remain native catalogue connections. Presence checks require the current era's bit, so a remaining medieval Omen does not cause a cleared present-day Omen to be offered.

Scene99's lift is not a choice between equivalent routes. `G1A7/01` selects motor3 downward; `/04` selects motor2 upward; `/02` and `/08` select actor1 to disembark. The battle cinematics100/101 change these landing bits before returning to99. `raw-omen-lifts.txt` records the exact E0 transfers at packet0275/0286 and actor1's eventual destinations. The router has look-ahead continuations for those automatic rides and their proven disembarks, without fabricated current-room targets. The final compulsory battles similarly connect451 →422 →107 using DF at Atel0408:0319 and Atel0401:016B.

## Verification boundary

The focused tests exercise missing capture data, prerequisite timing, consumed-item handoffs, partial repairs, wrong-era ruins, all Sun Stone phases, both Geno dolls and their circuit, the individual Giant's Claw sections and switches, Ozzie's actual battle flags, doll delivery rather than merely winning a doll, Omen era presence and Terra's correct completion bit, categories, and every declared native actor/region/exit. The only empty arrival goals are65 (native carpenter report) and265 (native revival startup).

The classic provider has been tested offline against the regenerated installed-PC catalogue. No save editing, live game movement, or new game completion was performed for this audit. Live collision, currently active actors, coherent locals, and enabled world entrances remain required before a guide row can become a usable navigation target.
