# Engine Accessibility Implementation Plan

> For agentic workers: use the executing-plans workflow. Claude owns the UI investigation and implementation; Codex owns navigation, the engine atlas, integration, verification, and deployment.

**Goal:** Extend shared engine coverage so ordinary menus, interactions, and navigation work across game states without relying on the player to discover each missing variant.

**Architecture:** Keep verified native capture and semantic narration; generalize proven mechanisms and audit the whole native/resource surface.

**Tech Stack:** C#/.NET, Reloaded hooks, Prism, Ghidra Java, Python resource analysis and native-code replay.

**Spec:** [Engine accessibility design](../specs/2026-09-26-engine-accessibility-design.md)

**Current authorization:** The user has now explicitly authorized full-game implementation and deployment: story and optional content from the guides, all obtainable pickups, shared navigation, and normal menus. Routes to minigames and their entry points are included; minigame internal controls may be handled when players reach them. The complete decompilation is the finished foundation for this work, not the final deliverable.

## Global Constraints

- Main checkout is authoritative; old worktree artifacts are research references.
- Do not mutate game state, saves, settings, or the executable.
- Preserve the existing keyboard controls and sighted-player information boundary.
- Native facts, extraction coverage, replay results, and live testing are separate evidence classes.
- One owner per edited subsystem; serialize shared builds, timing tests, and Ghidra project access.
- No coverage claim based solely on catalog presence or a matching test count.

## Review Focus

1. UI lifecycle and control-only transitions: Claude UI audit and transition regressions.
2. Script paths lost at calls, loops, or budgets: independent all-script interaction audit.
3. Routes crossing an unrelated actor/exit: contact ownership and negative movement replays.
4. Intermediate passages using final-target rules: staged route regression tests.
5. Stale native state after scene, menu, or vehicle changes: lifecycle tests and capture review.

## Tasks

- [x] Export a reproducible native engine atlas under `artifacts/research/engine-0332`, with a checked exporter in `tools/research/engine`; include function and resource coverage, explicit decompilation failures, and binary identity. The complete export has 92,873 native entries, 92,864 successful C decompilations, nine explicitly retained library failures, and all 9,509 archive resources. See `docs/full-game-decompilation.md` and the corpus's `verification.json`.
- [x] Audit native UI owners and state transitions independently of current hooks. Ending, save/restart notices, Extras, save instructions and Classic Bookmark are integrated. Independent review's ending-focus, queued-input and native Log-selection findings are fixed. All 135 Extras tests pass; final bounded review has no remaining blocker.
- [x] Audit all scene scripts for item, money, interaction, menu, progress, terrain, encounter, and transition operations, including delegated handlers and startup continuations. Resolve compiler omissions with source-backed tests and regenerate the catalog when justified. The independent entry audit and the three remaining bounded expansions are recorded in `docs/whole-game-engine-0332.md`.
- [x] Exercise shared route behavior against native terrain/contact rules. Add failing regressions for confirmed omissions, fix their common mechanism, and replay affected scenes and negative cases. All 1,375 static exit searches pass across 443 scenes; 507 scenes were examined. Spekkio's checkpoints additionally pass the extracted-room and directed-contact replay.
- [x] Integrate shared composition changes, run focused tests, and then the release suite. All 2,108 Release tests and 170 hook signatures pass after the final repairs. The compiler suite passes 29 tests. The native atlas, replay results and coverage report are preserved as reproducible developer tools.
- [ ] Update release documentation, package and deploy while the game is closed, and compare all installed files against the package. State which results are native/replay/static versus live verified.

## Validation Commands

Run Python compiler/research tests with `py -3 -m unittest discover -s tools/research/future_story -p "test_*.py"`.

Run affected .NET projects with `dotnet test <project> -c Release --filter <focused filter>` before the final solution pass. Run footstep timing tests separately. Save TRX, hook proof, independent resource audits, replay output, and deployment comparisons in the release evidence folder.

Use `tools/Package-Mod.ps1` and `tools/Deploy-Mod.ps1` after implementation is committed and the game is closed; verify executable identity, package/installation hashes, and the release version afterward.
