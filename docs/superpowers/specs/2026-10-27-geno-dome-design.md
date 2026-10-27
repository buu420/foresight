# Geno Dome controls and visible feedback

Approved in chat on October 27, 2026: distinct labels for visible Geno Dome switches,
pods and droids; short charging/discharging and following/waiting cues; and checks
and repairs for both navigation modes to the native interactions.

The tester's 0.3.79 log is retained in the canonical checkout at
`artifacts/research/geno-dome-20261027/tester-log.txt` (SHA256
`1a77e751407cfca2ec4ba05e10e8ba853cf14410fef8679fb8eaf627b256abdb`).
It records fourteen distinct scenery-marker selections across scenes 248, 250
and 256. The native scripts are Atel0349, Atel0351 and Atel0355.

## Approach

Extend the existing native navigation catalogue with narrowly scoped appearance
labels and native contact geometry. Observe already-captured field actors and
story state for visible feedback. This fits the existing field-input lifecycle
and avoids additional native hooks or a second field poller. Hard-coded puzzle
solutions and a separate minigame controller would not supply sighted parity.

## Behavior and constraints

- Identify local terminals, distinct switches, pods, elevator controls and the
  two puzzle droids without revealing a successful combination or unseen reward.
- Announce the displayed electrical charge and its disappearance, not a hidden
  timer. Identify the droid's demonstrated following/waiting changes once.
- Require a coherent native scene and the relevant observable actor/state.
  Missing data must not be interpreted as discharge or a droid stopping.
- Keep native Confirm, timing, battles, locks and puzzle choices under player
  control. Route fixes use real current collision and native contacts.
- Keep unrelated rooms, shuffled vortex copies, narration assets and settings
  unchanged. This update uses short Prism feedback, not new recorded narration.
- Build, package and deploy a local 0.3.80 tester build after verification.
  Public publication is a separate user request.

## Verification

Use native script and Ghidra evidence, the tester's actual actor captures and
derived map fixtures with clear provenance. Verify labels, absence/visibility,
state transitions, deduplication, ownership changes and both route modes.
Run the full suite, verify the exact packaged x86 assemblies and deployment,
and preserve saves, settings, narration and other installed mod files.
Offline replays do not prove live controller, audible speech or recipient play.
