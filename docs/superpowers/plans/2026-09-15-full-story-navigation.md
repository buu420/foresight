# Whole-game navigation implementation plan

> Executed in the isolated navigation worktree. Claude supplied a bounded initial
> audit; Codex corrected native flags, bindings and routes and reviewed the integrated result.

**Goal:** Complete story and optional navigation through the ending and installed bonus areas.
**Architecture:** Extend live target capture with whole-game metadata and a staged
story catalog. Bind to active actors/exits and collision-checked local routes.
**Tech stack:** C#/.NET 9, xUnit, Python resource audit, Ghidra.
**Spec:** ../specs/2026-09-15-full-story-navigation-design.md

## Constraints

- Preserve existing keys and stepwise navigation/footstep behavior.
- Use installed PC executable SHA256 8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7.
- Do not modify saves, drive the game, expose reward contents, or invent positions.
- Retain active-state, identity, collision and scene-coherence checks.
- Deploy the finished package and dependencies under the existing authorization.

## 1. Whole-game native index and availability

- [x] Inventory existing catalogs and research, synchronize and delegate to Claude.
- [x] Extract all installed scene candidates and run fresh Ghidra availability audit.
- [x] Resolve decoder boundary gaps before generating production metadata.
- [x] Add `FullGameNavigationTests` proving an active offscreen pickup and actor
  at point 200 appear without discovery; retired actors and collected pickups do not.
- [x] Run the focused tests and observe the missing-coverage failures.
- [x] Add `GameNavigationCatalog` with scene-exit and verified marker metadata;
  integrate into `FieldNavigationSource` and `WorldNavigationSource`.
- [x] Remove the early-chapter restriction using coherent native availability;
  include PC destinations above 511 only with actual installed scene records.
- [x] Run focused navigation and native pickup tests.

## 2. Story objectives and connections

- [x] Record chapter endpoints and gates from native writes and guide sequence.
- [x] Add `FullStoryTargets.Build(scene,state,available,player)` and a
  scene connection helper returning current native exit bindings toward endpoints.
- [x] First test stage 77 onward, phase changes, distant onward exit versus nearby
  backward exit, alternative floors, unknown predicates, unavailable transitions.
- [x] Implement all main-story phases through Lavos with exact actor/exit evidence.
- [x] Integrate world-map objectives and era-return routes; retain connected-region
  filtering and current native entrance state.
- [x] Test continuous phase coverage and representative multi-room routes.

## 3. Optional quests, scenery and bonus areas

- [x] Compare Claude's matrix with native index; audit gaps and contradictions.
- [x] Add optional people/objects/exits and quest availability predicates without
  promoting optional pickups into mandatory Story Events.
- [x] Add native-backed touch/scenery transitions and instructions for puzzle
  actions, sealed doors, platforms, Epoch/Gates and bonus-area interactions.
- [x] Test collection, actor retirement, repeated visits, changing doors and
  no duplicate mandatory/optional labels.
- [x] Run a reproducible installed-resource coverage report for all supported
  scene records, exits, treasure records and scripted pickup candidates.

## 4. Verification and release

- [x] Review scope coverage and all unresolved runtime limits; resolve material gaps.
- [x] Run complete Release suite and executable hook-byte verifier.
- [x] Bump manifest to 0.3.23 and update byte-exact packaging assertions.
- [ ] Commit implementation, build package, verify hash and product version.
- [ ] Check game closed and deployment paths, deploy with backup, run deployment
  contract and installed-file comparisons.
- [ ] Record actual evidence, commit release notes, fast-forward main while
  preserving unrelated files. Final report distinguishes tests from live play.
