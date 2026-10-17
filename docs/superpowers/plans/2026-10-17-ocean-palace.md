# Ocean Palace Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans task by task. Claude teammates own bounded audits or explicitly assigned files; Root integrates and verifies.

**Goal:** Restore useful, accurate Ocean Palace navigation and narrate verified visible story actions through the tester's current progress.

**Architecture:** Extend the existing native-backed field targets and exact dispatch anchors. Use actual actor identities and current room geometry; keep native interaction and story restrictions.

**Tech Stack:** C#/.NET 9 x86, native x86 hooks, Python resource analysis, Ghidra, Fish Audio.

**Spec:** docs/superpowers/specs/2026-10-17-ocean-palace.md

## Global Constraints

- Native locks, battles and player Confirm remain native.
- No hidden identities, future story outcomes, progression writes or automated Confirm.
- Existing recordings, saves, settings and native resources are preserved.
- Claude runs only Opus 5.5 at maximum reasoning.
- Offline verification is not live gameplay or listener confirmation.

## Review Focus

- Actor slots reused with another class/visual must not acquire a character name.
- Concealed, disabled or disconnected actors must not become misleading destinations.
- Touch triggers must remain reachable by native contact, with no Confirm substitution.
- Lavos must not expose a scripted later warp or an incorrect Nu identity.
- Description cues must require an exact script, visible subject and applied native effect.

### Task 1: Character and object targets

**Files:** StoryCharacterLabels.cs, FieldNavigationSource.cs, NuNpcLabels.cs and a focused OceanPalaceTargets helper if justified; tests under Navigation.
**Interfaces:** Existing ActorLabel(scene, actor, story), catalog actions and NavigationTarget.

- [x] Finish native/class/visual/intro audit; write regression tests for the log identities, mismatches, hidden actors and correct category.
- [x] Run the focused tests and retain the intended failures.
- [x] Add only audited names, visible switch labels and correct battle/contact classifications.
- [x] Re-run focused tests and inspect every result.

### Task 2: Native Palace routes

**Files:** OceanPalaceRoutes.cs, FieldNavigationSource.cs and catalog/tool corrections only where native evidence requires them; OceanPalaceRouteTests.cs and capture regressions.
**Interfaces:** Existing field graph/approach generation and FullStoryTargets.Build used by both navigation modes.

- [x] Reproduce missing Masa, elevator and lower switch routes from original captures/native assets.
- [x] Add tests that fail for the actual unavailable interaction or wrong component.
- [x] Repair component selection, native touch goals or terrain staging with no state writes.
- [x] Verify original captures and native x86 movement where relevant; preserve negative restrictions.

### Task 3: Visible story descriptions

**Files:** StorySceneActionCapture.cs, StoryActionCatalog.cs, StoryVoiceCueCatalog.cs, Voice/manifest.json and new WAVs; capture/catalog/runtime tests.
**Interfaces:** Existing scene-action candidate and post-native proof; approved Fish narrator model.

- [x] Review visible actions and native script anchors; record unsupported events explicitly.
- [x] Add and verify exact-script, actor/visibility/application and deduplication regressions.
- [x] Add bounded anchors and concise descriptions; generate and verify only new narration clips.
- [x] Verify all previous recordings unchanged and new cue timing/transcript/format.

### Task 4: Review, deploy and tester release

**Files:** ModConfig.json, release checks, README, native audit, handoff and publication audit.

- [x] Fresh whole-change Opus review and resolve concrete findings with regressions.
- [x] Version 0.3.67; build native/payload and run full appropriate suites plus release gates.
- [x] Deploy with backups, verify installed hashes and saved state, then immutable public release and Mod Manager publication.
- [x] Verify anonymous downloads/catalog and document live testing still outstanding.
