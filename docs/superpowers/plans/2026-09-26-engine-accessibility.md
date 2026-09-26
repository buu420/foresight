# Engine Accessibility Implementation Plan

> For agentic workers: use the executing-plans workflow. Claude owns the UI investigation and implementation; Codex owns navigation, the engine atlas, integration, verification, and deployment.

**Goal:** Extend shared engine coverage so ordinary menus, interactions, and navigation work across game states without relying on the player to discover each missing variant.

**Architecture:** Keep verified native capture and semantic narration; generalize proven mechanisms and audit the whole native/resource surface.

**Tech Stack:** C#/.NET, Reloaded hooks, Prism, Ghidra Java, Python resource analysis and native-code replay.

**Spec:** [Engine accessibility design](../specs/2026-09-26-engine-accessibility-design.md)

**User clarification during work:** "Just decompile the entire game, which is what I figured you were going to do." The immediate deliverable is therefore the complete native executable/library export and game-resource/script corpus. The implementation tasks below describe subsequent engine accessibility work, not changes claimed by the decompilation itself.

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
- [ ] Audit native UI owners and state transitions independently of current hooks. Claude maintains `claude-ui-progress.md`, implements verified missing behavior in UI capture/hook/narrator files, and supplies integration requirements.
- [ ] Audit all scene scripts for item, money, interaction, menu, progress, terrain, encounter, and transition operations, including delegated handlers and startup continuations. Resolve compiler omissions with source-backed tests and regenerate the catalog when justified.
- [ ] Exercise shared route behavior against native terrain/contact rules. Add failing regressions for confirmed omissions, fix their common mechanism, and replay affected scenes and negative cases.
- [ ] Integrate shared composition changes, run focused tests, and then the release suite. Verify every installed hook signature against the supported binary. Preserve the native atlas and coverage report as reproducible developer tools.
- [ ] Update release documentation, package and deploy while the game is closed, and compare all installed files against the package. State which results are native/replay/static versus live verified.

## Validation Commands

Run Python compiler/research tests with `py -3 -m unittest discover -s tools/research/future_story -p "test_*.py"`.

Run affected .NET projects with `dotnet test <project> -c Release --filter <focused filter>` before the final solution pass. Run footstep timing tests separately. Save TRX, hook proof, independent resource audits, replay output, and deployment comparisons in the release evidence folder.

Use `tools/Package-Mod.ps1` and `tools/Deploy-Mod.ps1` after implementation is committed and the game is closed; verify executable identity, package/installation hashes, and the release version afterward.
