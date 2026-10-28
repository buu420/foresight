# Lost Sanctum Implementation Plan

> Execute inline with superpowers:executing-plans. Claude Opus 5.5 at max owns
> a bounded native audit and an independent final review; no other Claude model.

**Goal:** Restore native Lost Sanctum quest and navigation continuity.

**Architecture:** Correct decoded passage guards, room continuation and native
contact bindings in the existing field providers. Reuse coherent captured state,
the graph and controller; movement remains native.

**Tech Stack:** C#/.NET 9 x86, Python resource decoder, Ghidra native evidence.

**Spec:** docs/superpowers/specs/2026-10-28-lost-sanctum-design.md

## Global Constraints

- Preserve native quest choices, locks, battles, timing and player Confirm.
- No state writes, automatic Confirm, new hook or regenerated narration.
- Build and deploy a versioned tester package; no public publication this turn.
- Preserve saves, settings, narration, original assets and other mods.

## Review Focus

- The cave remains available while the prehistoric village is under attack.
- Completed hand-ins and independent quests do not restart consumed-item errands.
- Disconnected rooms sharing a scene never provide a shortcut through solid floor.
- Narrow native contacts and map-edge lanes remain reachable without relaxing collision.
- Unknown state or identities provide no guessed route, label or quest introduction.

## Task 1: Cave metadata and quest continuity

Files: tools/research/future_story/bonus_regions.py; Navigation/Data/game-navigation.json;
BonusStoryObjectives.cs; FullStoryObjectives.cs; FullStoryTargets.cs;
tests/ChronoTriggerAccessibility.Mod.Tests/Navigation/LostSanctumStoryTests.cs.

- [ ] Add failing tests for scene578's independently checked cave warp during
  E11:01 / E12:80-clear, exact region bounds and E13:01 destination selection.
- [ ] Correct generator and only the affected compiled entries; retain native guards.
- [ ] Add failing tests for local Story Events, cave/bridge progress and unknown state.
- [ ] Bind current accepted Sanctum objectives in the primary story category without
  routing back to Lavos or introducing remote requests; retain independent choices.
- [ ] Run focused BonusStory, FullStory, Vortex and LostSanctum tests; commit.

## Task 2: Physical approaches and room continuation

Files: LostSanctumTargets.cs / LostSanctumRoutes.cs as needed, FieldNavigationSource.cs,
SceneConnectionRouter.cs or existing graph only where native evidence requires it;
LostSanctumNavigationTests.cs and captured/installed map fixtures.

- [ ] Record actual native route/contact cause and source offsets from the audit.
- [ ] Add failing captured-state tests for Waystone, Nu, builder, cave and fortress
  passages in manual and automatic modes, including missing/unknown/locked states.
- [ ] Correct narrow geometry or continuation at its owner, using native predicates.
- [ ] Verify no broad collision relaxation, artificial Confirm or hidden-state names.
- [ ] Run focused route suites and commit.

## Task 3: Review, package and local deployment

- [ ] Complete all solution tests and focused native replay checks.
- [ ] Obtain whole-change read-only Opus5.5 max review; resolve material findings
  with failing tests and a full passing suite.
- [ ] Set version 0.3.81 and refresh every pinned version/checksum; build Release.
- [ ] Package and verify exact x86 payload, package tests and deployment transaction
  tests sequentially against a stable staged package.
- [ ] Deploy transactionally, verify hashes and protected files, put tester ZIP and
  checksums/notes in Downloads, and write a handoff distinguishing offline and live proof.
