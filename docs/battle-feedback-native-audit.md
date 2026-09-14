# Battle feedback native audit

Research for the requested battle accessibility subsystem, 2026-09-14. The first sections record the initial audit; the integration section below records the implemented reader. All findings below are offline observations of the supported executable, SHA256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`, image base `0x400000`. Addresses are RVAs.

The companion command, target and lifecycle investigation is [battle-interface-native-audit.md](battle-interface-native-audit.md). Evidence is retained under `artifacts/research/battle-0317/`. The next test release is 0.3.17.

## Party HP and MP

The shared formatter at `0x1BDD0` is called by the touch update at `0x1A1BF` and classic update at `0x200CF`. It formats the actual battle readouts, with one pass over three party slots. The canvas pointer is the global at image + `0x41B4C4`.

| Field | Address relative to canvas |
|---|---|
| Slot present | `0x19FA0 + slot*4` (int32, nonzero) |
| Current HP | `0x15BA3 + slot*0x80` (uint16) |
| Maximum HP | `0x15BA5 + slot*0x80` (uint16) |
| Current MP | `0x15BA7 + slot*0x80` (uint16) |
| Maximum MP | `0x15BA9 + slot*0x80` (uint16) |
| Party character ID | `0x1324C + slot*4` (int32) |
| Custom name | `0x1908 + characterId*24` (MSVC string) |

The formatter writes HP to the label at menu + `0x21C + slot*4` and MP to menu + `0x228 + slot*4`, using `%d/%3d` and `%d/%2d`. The classic display at `0x20FD0` controls label visibility. Its character-name initialization calls `0x14830` with ECX = canvas + `0x28`, giving the same custom-name address used elsewhere in the mod.

The three-slot limit comes from the display loop. Do not extend this capture into the enemy records that follow in the backing array. Native visible HP-revealing effects require their own audited display path.

The HUD marks the acting slot yellow using canvas + `0x1AD20`, gated by the byte at `0x1AD10 + slot*4` and the word at `0x1AD50`. The exact command-ready semantics belong to the companion audit. Canvas + `0x19E90` and menu + `0x6A8` are interface modes, not a proven active-character index.

Evidence: `root-party-display.asm`, `root-party-evidence.md`; independently checked by Claude's Ghidra decompile in `cursor.txt`.

## Displayed battle messages and results

The TextManager initializer at `0x1B7A30` loads `msg/battle.txt` as bank 0, `item.txt` as `0x1B`, `monster.txt` as `0x32`, `sfc_btl.txt` as `0x3B`, and `tech.txt` as `0x44`. The loaded native strings supply localization.

The shared message display member at `0x19940` resolves a type and message index using `0x193B0`. It then calls the native substitution formatter at `0x19510`, removes element icon markup, and sets the message label's text at callsite `0x19B9A`. The label pointer is menu + `0x234`. The prepared text contains actual names and amounts.

The native formatter substitutes:

- `<NAME_CNO>` using the custom character name selected by menu + `0x18`.
- `<NAME_MON>`, `<NAME_TEC>` and `<NAME_ITM>` using the pending string at menu + `0x1C`, prepared by `0x19880`.
- Successive `<NUMBER>` placeholders using menu + `0x08`, `0x0C`, `0x10` and `0x14`.
- The custom Magus name where its token appears.

Prefer observing the native substituted string at the audited display callsite. Merely observing a raw TextManager template is not proof that a message was displayed. Element icons should retain their meaning in speech.

The results driver at `0x75000` uses this message path: call `0x7504B` requests bank 0 line 37 and sets the actual earned EXP at menu + 8; call `0x751B3` requests line 40 after preparing the obtained item name at `0x751A0`. Neighboring TP, gold, level and Tech results use the same path. Live result timing remains unverified.

Evidence: `root-localization-init.asm`, `message-file-ids.json`, `root-message-display.asm`, `root-message-evidence.md`.

The installed `libcocos2d.dll` provides an alternative to another formatter hook: its exported `Label` LabelProtocol vtable at DLL RVA `0x4BCE00` has a string getter in slot 2, at DLL RVA `0xF558E`. The getter bytes are `8D 41 28 C3` (`return this+0x28`). The LabelProtocol subobject is at Node + `0x278`, so the actual rendered MSVC string is at Label + `0x2A0`. The Node visibility getter reads byte + `0x1AD`. After the native message display returns, read its owned label's actual string and validate the getter/layout before interpreting it. This avoids reproducing native placeholder replacement. DLL SHA256: `8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F`. Evidence: `root-cocos-label-layout.json`; no DLL hook or mutation was made.

## Individual damage, healing and misses

The popup renderer at `0x1B850` loops over exactly 11 display slots. For each visible popup it constructs a label, positions it, applies its color and draws it.

| Field | Address relative to BattleMenu |
|---|---|
| Popup visible | `0x1B5 + slot` (byte) |
| Displayed text | `0x34 + slot*24` (MSVC string) |
| Screen position | `0x13C + slot*8`, `0x140 + slot*8` (int32 x/y) |
| RGB color | `0x194 + slot*3` (three bytes) |

This is the displayed popup value, not a difference between two HP samples. It can represent an overkill hit and two consecutive hits with identical values.

The numeric writer at `0x1F730` formats the actual number and saves it into this array. It takes seven raw stack dwords: argument 1 is the popup ID, argument 3 the value, and argument 7 an RGB pointer. Its only direct caller is `0x36AED`.

The miss writer at `0x1F640` takes four raw stack dwords and reads the native localized `sfc_btl.txt` line 16. Its only caller is `0x36A59`. Both writers receive popup ID = `0x42 + targetSlot`, which resolves to the arrays above. Every writer invocation can assign a presentation serial; the renderer can then announce that serial when its popup is visible, once per presentation rather than once per animation frame.

The driver at `0x36900` owns the BattleMenu at scene + `0x18CC`. It distinguishes miss kind 5, white numeric kind 3, and green numeric kinds 1 and 2. Kinds 2 and 4 also set battle data `4848+slot*4`; `42EA0` uses it to select a second popup motion table. This distinction has not been tied to an HP/MP applier. Version 0.3.17 therefore says "recovers N" for green kinds 1/2 and "takes N damage" for white kinds 3/4, without adding an unverified HP/MP unit. Kind 5 reads the native miss text; unknown kinds retain their visible text. H/M read the separately proven party HUD. The six-row result table at `398E68` has values 0,44,88,132,176,220; only those verified rows are decoded.

Recipient identity, retained names for a newly defeated enemy, lifecycle invalidation, foreground handling and speech priority must be integrated with the command/target capture. Never infer an enemy's hidden current or maximum HP to supply missing information.

The enemy-name index has a concrete display-side source: `0x6ECD0` reads `[SceneBattle+0x50] + 0x4478 + targetSlot*4` and passes it to the name resolver with type 13 (bank `0x32`, `monster.txt`) at `0x6ECFC`. With the verified battle data base, this is canvas + `0x1A018 + targetSlot*4`. This routine is a native HP-reveal message and also reads HP, but only the name index is applicable to ordinary target and popup naming. Its HP reads must remain confined to a genuinely displayed native reveal message. See `root-enemy-name-display.asm`.

Evidence: `root-damage-render.asm`, `root-damage-presentation.asm`, `root-damage-writers.asm`, `root-damage-evidence.md`.

## Candidate hook ABI checks

`root-hook-candidates.json` records exact prologue bytes and verified `ret` stack cleanup for the shared HUD formatter, message display, popup writers and popup renderer. These six boundaries are now registered, with delegate forwarding and complete composition covered by automated tests. All 134 hook signatures match the supported executable. In-game execution has not been tested.

Before release, require meaningful capture and narration tests, native callback/ABI checks, full composition and regression verification, then transactional packaging/deployment and installed hash comparison. Keep offline evidence and live coverage claims distinct.

## Integration: target groups, visible status effects and lifetime

The shared HUD formatter `1BDD0` is a no-argument thiscall member; both native interfaces call it on their update paths. The shared base destructor `14AF0` also takes no stack arguments and returns with `C3`. Classic deleting destructor `21670` and touch deleting destructor `14A70` both invoke that base destructor. Managed ownership ends before the original base destructor runs. Hook guards still call every native original exactly once if observation fails. The six registered boundaries total 134 verified contracts with the existing mod.

`4B1C0` explicitly calls `4B390` when battle data `42D8` is a nonnegative acting slot and `434C` is nonzero; otherwise it hides targets or dispatches the command panel. The invalid selection flag is `4368`. `4B390` draws the committed target array at battle data `5134`, or canvas `1ACD4`. A negative second cell selects its single-target branch. In the group branch the marker loop rotates between array positions 0–2 and 3–5, drawing visible flags at menu `538`, `54C`, `560`, `574`. Positioner `49B00(markerId, battlerSlot)` proves which battler each marker points to. `4C550` copies the resolved candidate group into the committed array; it is not the list of every available target. The capture reads a bounded eleven-cell table but reports only its six renderer-addressed cells in group mode, and requires a visible target arrow. Alternating marker frames do not cause repeated or incomplete speech; the unrendered tail is excluded. Sentinel holes are skipped in a group; the single-target branch ignores the unused tail. The earlier proposed `1A17C` group is an action queue and is not used.

The status sprite renderer is `43DF0`. It uses Script `2848E+slot` and an animation identifier at battle data `4F14+slot*4` (canvas `1AAB4`). It sets menu `5C4+slot*20` visible and writes the icon tile at `5D4+slot*20`. Its frame table is image `398790`. The state applicator `4EA70` reads Script `28441+slot`, commits it to battle data `4E90+slot*4` (canvas `1AA30`), and dispatches at `4EB84`. Palette and pose effects use the same committed visual code even when there is no icon on an animation frame. The capture compares requested and applied codes and never reads latent underlying status bits to announce a change early.

The native status-to-visual table at `398680`, consumed by `4E3D0`, maps seven negative status masks at battler byte `1E` to visual codes. The masks are independently documented in the original [ctrando status enum research](https://github.com/Pseudoarc/ctrando/blob/main/src/ctrando/common/ctenums.py). Positive effect masks at the table's offsets 4/9 and 3/8 match the original [ctrando item/buff definitions](https://github.com/Pseudoarc/ctrando/blob/main/src/ctrando/items/itemdata.py). The PC table establishes the visual-code mapping; the SNES research supplies names, not PC addresses.

| Visual code | Meaning | Loaded battle.txt line |
|---|---|---|
| 2 | Stop | 14 |
| 3 | Berserk | 23 |
| 4 | Confuse | 11 |
| 5 | Sleep | 10 |
| 6 | Barrier | 21 |
| 7 | Shield | 20 |
| 8 | Lock | 13 |
| 9 | Blind | 12 |
| 12 | Haste | 19 |
| 13 | Slow | 9 |
| 14 | Poison | 8 |

The names are resolved from the game's loaded bank 0. The English meaning is a fallback if that text bank has not loaded. Null means unreadable/unapplied status, distinct from an empty no-status result. The runtime retains the last known visual status across unreadable frames, so a blinking icon or failed read cannot announce a false cure. It only tracks party effects; enemy internal status masks are not exposed.

The message Label's own visibility is controlled through cocos virtual `+B4`: hidden at constructors `1770E`/`1FCB3`, then set by native display functions at `1BAA7` and `200ED`. The reader polls the finished Label after the HUD update as well as observing `19940`, so preparing a hidden message does not lose its later visible presentation. Repeated unchanged text is suppressed until hidden or replaced. Native substituted labels cover actual rewards, escape and native HP-reveal messages. Element-icon-only meaning is not separately reconstructed from hidden text templates.

`BattleSession` ties these captures to `BattleRuntime`; Core owns speech deduplication and priority. The party inspection slot is separate from native command focus. Shortcuts consume held-key edges while backgrounded and rearm on entry/exit. Battle entry cancels the current automatic route and footstep playback; field/world callbacks pass only physical input while the battle owns its menu. Teardown releases this suspension without resetting discovered destinations. Persistent unavailable capture and unresolved visible labels produce audible diagnostics rather than silent invented data.

Synthetic memory, callback, narration, shortcut, and full composition tests are distinct from live gameplay. A first real encounter is still required after installation. Remaining live checks include both interfaces, reordered party members, multi-target effects, repeated hits, status changes, results, defeat, and navigation after battle.

## Final review and release verification

Claude reviewed capture bounds, slot versus character indexing, six hook ABIs, lifecycle, popup layout, and native presentation evidence. Root corrected the reordered-party case, group sentinel holes and unrendered tail, retained status across unreadable frames, stale popups after losing focus, and per-target name failure. When one name is unavailable, the other highlighted names still read. A fixed slot identity (enemy A-H or party member 1-3) supplies a usable reference for that target and its popup; it does not assert a living-enemy roster. Names are retained for pending hits whose recipient disappears before drawing.

Battle uses the existing synchronous Prism dispatcher only for changed selections, discrete results, and requested inspections. The pinned [Prism NVDA backend](https://github.com/ethindp/prism/blob/9911156998b52fee91fb2cb4f71ac793d4e546c7/source/backends/nvda.cpp) submits controller RPC without waiting for speech completion. The bridge call itself is synchronous, and alternative backends can take longer; no universal nonblocking or measured battle-latency claim is made. The battle lock has no reverse acquisition from the semantic dispatcher. Hook disabling precedes speech detachment during shutdown.

The Release regression suite passed 1,304 tests: Core 132, Native 603, Mod 561, Prism 8. The final manifest uses canonical LF line endings and is checked byte-for-byte by the manifest and packaging tests. All 134 hook signatures match the installed executable. The real-game checks listed above remain pending. Deployment evidence will be retained at `artifacts/research/release-0317/deployment-proof.json` after the transaction succeeds.
