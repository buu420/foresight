# Vortex, Desert and visible presentation implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline. Root owns product edits, tests, package and deployment; user-authorized Claude Opus5.5max supplies read-only native research and the final independent review.

**Goal:** Resolve the four approved reports using visible native information and ordinary navigation.

**Architecture:** Keep current capture/formatter and controller boundaries. Enable floor steering on verified Desert routes independently of chest arrival policy. Bind vortex contacts to native scripts/current scene overrides. Add read-only presentation captures only after proving render ownership and lifecycle.

**Tech Stack:** C#/.NET9, xUnit, REA/Ghidra12.1.2, installed PC script/resource evidence, original x86 instructions in an offline Unicorn movement oracle.

**Spec:** docs/superpowers/specs/2026-10-26-vortex-desert-lavos-lara-design.md

## Constraints and review focus

- Native locks, timers, combat and player Confirm remain authoritative; no automatic password or hidden puzzle/strategy answers.
- Compare0.3.77 logs with current0.3.78; preserve valid world-menu/Epoch fixes.
- All cleared chests can remain native collision records; exits must not acquire chest holds or Confirm behavior.
- Vortex copies/shuffle/local destinations must not assume the original room's connections or floor layer.
- Presentation events require a current visible owner; stale, foreign, unreadable or closing UI must not repeat cached selections.
- Preserve meaningful localized names and control glyphs; a secret AI identifier alone cannot establish a visible imitation.
- No live-game/input/audio claim follows from offline fixtures or instruction emulation.

## Task1: Desert exit navigation

Files: Core/Navigation/NavigationModels.cs and NavigationController.cs; Mod/Navigation/FieldNavigationSource.cs, FieldNavigationGraph.cs and FieldFloorMovement.cs; Mod.Tests/Navigation/ClearedDesertExitTests.cs and existing Core moving-floor tests. Add an explicit frame policy for ordinary floor route steering, keeping existing chest reach policy separate. The controller must return a current waypoint for automatic floor steering and geometry-checked manual guidance on perpendicular drift, while retaining normal exit arrival and native cancellation. Conservative component sweeps filter blocked gait/direction candidates; native collision still owns movement.

- [x] Record logs and reproduce original native blocked input at logged positions.
- [x] Add behavior tests through the real FieldNavigationSource/NavigationController for cleared-room exits in both modes and negative controls; observe RED.
- [x] Apply the scoped steering correction; observe GREEN with existing moving-floor/native-slide checks.
- [x] Replay original movement instructions from logged starts toward actual native exit contacts; label original planes/no-actor assumptions. All8 exit cases and11 chest cases pass; Core245 and focused Mod71 pass. Record the test and commit.

## Task2: Vortex copied rooms

Files: current BonusStoryObjectives/FullStoryTargets, FieldNavigationSource/FieldTransitions and focused scene targets only where the audit proves a gap; Native capture for live overrides if required. Tests: VortexStoryNavigationTests and a dedicated copied-room regression fixture from supplied route evidence.

- [x] Match every logged no-route/empty-target failure to current code and native scripts. Fill exact file/interface choices from the read-only audit before product edits.
- [x] Add RED cases for missing verified contacts, copied-map destinations and floor/layer transitions, plus unavailable state controls.
- [x] Implement only proven bindings/corrections and verify both navigation modes, adjacent scenes and no backward/hidden fallback. Record and commit.

## Task3: Native Lavos/password presentation

Files/interfaces to be fixed by the native UI audit before implementation: a read-only capture per native presentation owner, its semantic event/formatter/runtime integration, and owned-memory/render lifecycle tests.

- [x] Verify displayed Lavos imitation and password selector paths with existing evidence and focused fresh Ghidra procedures. Record exact executable/script identities and offsets.
- [x] Add RED tests for displayed identity/selection changes, localization/control glyphs, native results and stale/foreign/unreadable owner rejection.
- [x] Implement bounded captures and announcements without inputs or timing changes. Correct any premature password answer in Story Events using visible clue state. Verify and commit each independently.

## Task4: Review, validation and local tester delivery

- [x] Obtain one fresh Opus5.5max whole-change review; resolve material findings with failing regressions and record coverage limits.
- [x] Run the appropriate focused suites and complete solution; build Release with zero warnings/errors.
- [x] Version the tester build, update pinned manifests, package the exact payload and replay its x86 assemblies with no source fallback.
- [x] Verify archive/installation, deploy with backup and preserved files, copy ZIP/checklist to Downloads and record a handoff. Keep publication separate until requested.

Execution notes: UI delivery was implemented in an isolated binary-reference overlay by Opus5.5/max, then copied and verified in the canonical assemblies by root. The fresh whole-change reviewer is a separate Opus session. Original-instruction vortex-motion replay adds8/8 coordinate-region entries on captured/derived/native planes, in addition to the Desert8/8 exits and11 chest cases. Full suite5,036 passes with xUnit.MaxParallelThreads=4; first unrestricted failures and the35-case follow-up remain retained. Source commits for tasks2/3 are grouped with the payload after review so all production assembly revisions identify the reviewed implementation.

Final delivery: payload4e735d29801cbce463535c16ad2171a4f00bea4f, candidate2; full5,052 pass, exact-package x86357 pass, Release0 warnings/errors, archive275 files and complete hash/tamper/runtime verification, independent installed303/303. Protected229 files unchanged. ZIP4a441ec7ed81bb9a1d2b0f38d265f07953eb0735d18d606ec168d615376043bd and testing notes copied to Downloads. Fresh Opus5.5max review and bounded follow-up accepted after M1 polling fix; remaining low items disposition documented. Native map-texture crash, live gameplay/controller/audible speech and recipient-PC install remain outside the verified result. No public publication. See local delivery-receipt.json and handoff2026-10-26-foresight-four-reports-0379.md.
