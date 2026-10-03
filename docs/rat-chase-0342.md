# Arris Dome rat pursuit, 0.3.42

The report's two Downloads logs are identical (SHA-256 B57EAADD8FEF5C83FDFBA0EB722A74D8E5F2E70957605BBA7E2835B3DA0BDA4C). In the October 1 session, navigation announced arrival at Catch the rat at 09:34:36 and stopped. At 09:34:40 and 09:34:44, the player's Confirm input (`0x80`) stopped restarted pursuit as manual control. The eventual missing-destination message followed the rat disappearing; it was not the initial cause.

The fix keeps an explicitly marked pursuit active at a confirmed standing point, refreshes its route as the actor moves, and passes the player's held Confirm through alongside ordinary directional input. Every other input retains normal cancellation. Catch-range speech uses the current native Confirm resolver and leader facing. Automatic pursuit omits routine turn speech so the catch cue is not buried. Manual guidance retains counted legs. The exception applies only to scene 221's actors 12 and 13 (class 5, visual 134), during the uncaught story stage, and to the corresponding Story Events entry.

## Native evidence

Supported executable SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

- Ghidra export `native/functions/0017D0C0.c`: reads the native held Confirm bit; tries on the initial press, skips held ticks 2 through 19, and retries from tick 20. It scans facing and invokes the normal interaction dispatcher.
- `0017FA20.c`: dispatch requires the actor's call gates open and script priority above 1. Existing `FieldInteractionRange` and `ActorConfirm` preserve those checks.
- Scene 221's mapinfo binds map 119 and script `Atel_0156`. Actor 12's handler at file offsets 0298–02A7 and actor 13's at 02F7–0306 disable player control, show dialogue, clear global EC bit 10, and set EC bit 40. The route yields when native control ends; it never writes the story flags or generates Confirm.
- The same script shows the directional chase scripts and hides the fleeing actor at their ends. Loss of the live target stops pursuit and retains the retry instruction.

The [Arris Dome walkthrough](https://www.arrpeegeez.com/2014/07/chrono-trigger-walkthrough-part-seven_24.html) corroborates chasing and using Confirm. Button behavior and availability come from the PC executable and scripts rather than console button names.

## Validation

Four regression tests failed against the old behavior: arrival ended pursuit, waiting in range ended pursuit, manual guidance ended, and physical Confirm cancelled auto-walk. Two additional native-source tests failed before enabling the ordinary rat entry. The corrected tests exercise continued pursuit, moving goals, actual facing, input cancellation, control loss, caught/escaped targets, ordinary NPC behavior, and both rat slots.

`arris-rat-map-0342.json` was exported from the installed initial terrain through `export_route_fixtures.py`. Four tests use that actual rafter terrain, player coordinates from the report, and constructed nearby rat positions to exercise movement in all four directions. They are offline tests, not a recorded live capture or proof of a completed in-game catch. The reporter still needs to confirm the complete sequence in game.
