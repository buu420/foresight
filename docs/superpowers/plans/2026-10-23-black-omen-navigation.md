# Black Omen Interaction and Navigation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation. Steps use checkbox syntax for tracking.

**Goal:** Make the reported Black Omen teleport pads and final door interaction identifiable and reachable in both navigation modes.

**Architecture:** Reuse native scene/actor/script identity, the captured terrain graph and existing staged-contact navigation. Apply room-specific corrections only where installed script and native movement evidence prove them. Keep destination metadata separate from player-visible appearance and movement authority.

**Tech Stack:** C#/.NET9, x86 native capture, xUnit, Ghidra12.1.2, existing packaging/deployment tools.

**Spec:** User's October23 approval of the proposed teleport-pad, final-door and local Story Events behavior. The single Downloads log and extracted facts are recorded in `artifacts/research/black-omen-20261023/log-summary.json`.

## Global Constraints

- Native locks, combat, player Confirm and native input/control gates remain authoritative.
- No guessed coordinates, physics/state writes, invisible required switches or hidden future names.
- Initial installed terrain plus reconstructed script state is offline evidence, not a tester live capture. The eight actual route snapshots cover scenes315/317 only.
- Teleport pads are Interactable Objects with distinguishable labels; ordinary doorways remain Exits. Preserve native arrival/re-entry guards.
- The final door and its native opening interaction remain identifiable while closed. A route must reach the usable side and wait for fresh opening/contact evidence before continuing.
- A local Black Omen progression route must not choose the return elevator as a substitute for a missing forward control.
- No new narration/voice assets or renderer crash patch in this update.

## Review Focus

- Arrival-state flags temporarily disable teleporting; the drawn pad should remain identifiable with an accurate availability instruction.
- A trigger occupying the edge of a collision tile needs native-valid approach samples; tile centers alone may be invalid.
- Shared or wrapped collision planes do not justify joining disconnected landings or generalizing room dimensions.
- Unknown actor identity, script state, map layer or door contact must not authorize movement through a wall.
- Required encounters and the door opening must stay native; restart behavior after native control loss must be clear.

## Task1: Native evidence and teleport pads

Files: `FieldNavigationSource.cs`, a scoped `BlackOmenTargets.cs`/route helper if justified, and `BlackOmenNavigationTests.cs` with installed-map fixtures.

- [x] Audit Atel0453/0454 and0495/0496, map12/map9 and their native coordinate/arrival guards with Ghidra-backed operand meanings. Distinguish drawn pads from invisible bookkeeping regions.
- [x] Reproduce the current missing Objects category and failed pad approach on the existing source before changing behavior.
- [x] Add the minimum room-specific labels/category and native-valid trigger goals, with explicit inactive/unknown-state tests and both navigation modes.
- [x] Run the focused tests and document native facts and remaining live-test limits.

## Task2: Final door and local progression

Files: `FieldNavigationSource.cs`, scoped target/route helper, `ClassicOptionalObjectives.cs`/`FullStoryNavigator.cs` only if native story selection needs correction; tests in `BlackOmenNavigationTests.cs`.

- [x] Independently audit scene324/Atel0389 actor20, its collision/opening script and story predicates. Compare with verified existing touch-door behavior.
- [x] Reproduce the reported landmark:20 no-route and elevator-return objective, using source-backed fixtures with their evidence limits stated.
- [x] Repair the native interaction goals and opening label, preserving door/encounter/control guards; bind the local Story Events route to the verified forward contact.
- [x] Verify both manual guidance and auto-walk, closed/open transitions, inaccessible-side rejection and unknown proof behavior.

## Task3: Review, package and local deployment

- [x] Review the complete change with a fresh Opus5.5 review; resolve important findings with meaningful regressions.
- [x] Run the full solution suite and appropriate package checks, update version/release notes/native audit and build Release.
- [x] Package a verified tester ZIP, deploy owned files with the game closed, verify installed hashes and preserve saves/settings/narration.
- [x] Record paths, exact checks and unperformed live tests in a handoff. Public publication requires a separate user release request.

## Execution Rulings

Implementation stays in the established Chrono Trigger task checkout on `engine-accessibility-0332`; the parallel Claude task is read-only. This retains the installed-game resource corpus, ignored loader dependencies and working fixture paths. Unrelated untracked `.claude/` and `docs/claude-code-cli-setup.md` are outside scope.

The user has approved the written behavior above. Routine implementation decisions use the existing C# native-backed architecture and do not require repeated confirmation.

## Completion Evidence

Version0.3.72 payload was built from source commit `a70bbdb67bc7cbc83eda94408e541c9cfb39e9e0`. The Opus5.5 review's opening-frame defect was reproduced and resolved;52 focused and4,512 solution tests passed. The final packaged DLLs passed an isolated x86 replay including native yielded opening frames. All275 standalone files and28 mod files match the installed candidate; deployment verification passed303 checks and all229 protected files are unchanged. Detailed receipts and live-test limits are in `artifacts/handoffs/2026-10-23-foresight-0372.md`. This plan closure is documentation only; it does not rebuild or alter the verified payload.
