# Sunken Desert moving-ground and chest navigation audit

## Evidence and scope

Tester log: `2026-10-24 01.46.09 ~ Chrono Trigger.txt`, SHA256 `bf716572ec9eeba45b5ab7f8d70618313a0ef439b18e1bc853312a16fc35855f`, 3,115 lines. It captures scenes159/160/161, repeated floor announcements, the walking-toggle Run stop, a reset-floor-state stop, chest65 arrival and subsequent native encounters. No live terrain-plane snapshot was supplied.

The matching Steam executable is SHA256 `8fe9d75e4cdc279645c5bc932fc163fd67147255fc0c673ac45bbf0a6d2e00d7`. Shipped scenes159/160/161 use Atel0468/0469/0470 and maps112/43/22. The two chest rooms hold records64–67 and68–74. Exact positions come from `TakaraOffsetTbl.dat`/`TakaraDataTbl.dat`; no scene coordinates are invented.

Research receipts are under `artifacts/research/sunken-desert-20261025`. Fresh Ghidra exports confirm175C90 and178FF0 against the executable. Other functions below use the matching native corpus. The [Sunken Desert walkthrough](https://www.thonky.com/chrono-trigger/sunken-desert) corroborates slippery ground and Run; native data remains authoritative, and its future quest details are not exposed.

## Native movement and Run

1734D0 copies cached ground force88/8C into the step, then175A40 adds ordinary input. Its175C90 return selects speed32 for running or16 for walking. 175DA0 normalizes ordinary diagonal input by1.39. 175E90 chooses the mover from the combined input and floor delta, so holding one cardinal direction can cause a diagonal collision probe. 178FF0 commits the foot/layer and samples the next floor force. Rejected probes can preserve the previous cached force.

175C90 initializes zero toggles according to Run mode. Mode0/2 with toggle0/1 runs with Dash released and walks with it held. Mode0/2 toggle2 requires a short native Dash press/release to change the setting; held synthetic input cannot manufacture that separate release edge. Mode1 toggle1 runs regardless of held Dash; toggle2 walks without Dash and runs with it. The mod never changes a toggle, preference, timer, speed or position and never fabricates release edges.

173750 can leave the refused probe's terrain flags in138 while88/8C remain the committed force that1734D0 applies. The initial oracle omitted that persistence, and the whole-change review reproduced an unavailable-Run stop on chest71. In scenes159–161, capture now preserves bounded cardinal committed force and fresh Run ownership across this disagreement, with a rejected-probe marker and stable double reads. Invalid forces or changed ownership/control remain unavailable; other scenes keep the previous rejection rule. Feedback on a marked sample describes the committed push and never infers the refused tile's surface. Explicit status and pending queued feedback remain available while pinned against terrain.

In scenes159–161, sixteen-unit flow uses ordinary eight-direction feedback steering toward the current route point. Native Run/walk input is selected from the proven setting. Lateral error is weighted to keep travel near the proven path, and a candidate must advance in the requested cardinal direction. The game still performs every collision, encounter and movement step. Physical Dash is preserved; when it selects walking and prevents the required travel, navigation stops with precise release/tap advice.

The input coordinator re-observes a floor snapshot cleared by the same input boundary's navigation-menu reset before making a Run decision. Existing scene/engine/control and capture ownership gates remain required. Factory and Death Peak retain their prior policy.

## Chest contact and planning

175EE0 checks the native treasure grid with179D90 before terrain. A treasure rejection does not authorize a wall slide. Sunken Desert route graphs preserve that live grid through goal-specific and creature-free views. The foot's layer check remains separate from these body-corner probes.

179940 and179C30/179CA0/179CF0/179D40 define chest Confirm reach and facing. Up can check two rows when no intervening treasure record shadows it, including an opened record. In these rooms this proven two-row contact is available even when the nearer row is walkable. Preferred contacts have no flow, or allow facing against sixteen-unit outward flow. An unreachable preference never removes the other verified approaches.

The stationary interaction policy remains active after arrival. Automatic walking uses ordinary facing/contact input and replans from the current position when the sand removes reach. Manual guidance emits no movement and can replan after released input despite continuing native ground motion. Lack of route progress is bounded even when the player oscillates. Unavailable targets, scene/control changes, cancellation and native encounters revoke movement authority. No Confirm is generated.

Manual perpendicular-flow advice names the ordinary diagonal that counters drift, without promising a fixed footstep count for that correction. It keeps the graph's approach while reaching a different floor, where added lateral movement could miss a layer-changing contact. Failed-approach evidence persists across manual release pauses; only time with movement held contributes to its timeout. This fixes a repeated stop/replan loop without imposing an idle deadline on the player.

17D0C0 reads held Confirm: the first frame is tried, frames2–19 wait, then the native handler retries while held. The arrival cue therefore permits the player's held Confirm. Some replayed contacts offer recurring windows rather than uninterrupted reach; the announcement does not claim that the chest has opened.

## Speech and verification limits

Automatic sand changes are coalesced over1,500ms and use queued NavigationAnnouncement delivery. Current committed-push/status facts update every accepted capture. Explicit repeat remains interrupting. Suspension revokes facts without inventing an off-sand event or repeating unchanged facts on resume. A Run-only change on solid ground speaks Run advice. Idle manual drift replans are bounded and do not repeat an unchanged qualitative correction; a first resume after Stop moving remains audible. Native manual reach outlives the small waypoint box, and stationary arrival cues have a3s cooldown.

The corrected original-code Unicorn oracle executes175DA0 and175E90/178FF0 on private emulated memory, using the installed executable, initial terrain and real closed treasure-grid records. It carries probe138 across ticks and normalizes diagonals in the original function, with truncation after adding force. The C# harness calls FieldFloorCapture on the resulting force/probe state inside explicit owned-memory fixtures, then uses the current navigation implementation and normal held pad choices. All eleven contacts are reached in the corrected tester-mode run, including93 rejected samples for chest71. Exact final DLLs are replayed separately in x86 for all three Run profiles and manual guidance. Full actors, encounters, scripts, exits, outer animation/facing and actual speech are not replayed. No game process is attached to or launched for this validation.

The manual replay follows the spoken leg direction, releases movement when guidance requests a stop, and performs the indicated facing. All eleven native contacts are reached after the pause accounting and diagonal advice fixes. Its immediate response to instructions is synthetic: it does not prove human reaction time, audible cue usability or successful live opening.
