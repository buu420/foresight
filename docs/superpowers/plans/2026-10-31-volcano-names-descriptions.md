# Volcano, names and descriptions implementation plan

> Execute inline with superpowers:executing-plans. Authorized read-only Claude
> Opus5.5 max research owns native naming and narration-anchor questions. One
> fresh whole-branch review precedes public release. Scope/release approved.

**Goal:** Reach the reported Volcano opening, speak native bonus-area/enemy
names and add verified descriptions in the approved narrator voice.

**Architecture:** Reuse the existing current-frame navigation and native text
contracts; no synthetic map state or actor roster. Extend only proven scene or
movie narration contracts with exact asset and lifecycle checks.

**Tech stack:** C#/.NET9 Windows x86, xUnit, Ghidra/resource evidence, Descript
Opus5.5 review, Fish API, local video/audio analysis, existing release tools.

**Spec:** ../specs/2026-10-31-volcano-names-descriptions.md

## Global constraints and review focus

- No native state writes, puzzle answers, automatic Confirm or combat control.
- Unknown/stale text and state do not become fabricated names or roster members.
- Duplicates must have current native target/visibility evidence.
- Native/customized/localized names retain their spelling and identity.
- Descript still frames do not establish motion; corroborate with local frames.
- No default narrator, old-clip replacement, audible playback or game launch.
- Preserve native music, effects, timers, media ownership and skip behavior.
- Preserve saves/settings/other mods, .claude/settings.json and the user's CLI doc.

### Task1: Reproduce the new Volcano states and name failures

Files: existing PresentVortexNavigationTests/fixture; NavigationTextCapture,
Mod.cs/FieldNavigationSource naming integration and focused native/mod tests.

- [x] Reconstruct exact681/1785 states without altering their maps or flags.
- [x] Verify both guidance modes and16/32 native walking, including Story and chest regressions.
- [x] Trace the bonus name source from supported native data; prove stale/localization boundaries.
- [x] Write failing-first name tests, implement the narrow fix and pass focused checks.

### Task2: Accurate and concise battle identities

Files: BattleCapture/BattlePresentationCapture, BattleSession/BattleIdentity and
their owned-memory capture/session regressions as required by native evidence.

- [x] Prove name-index handling for bonus monsters and exact reserved markers.
- [x] Define duplicate evidence without using ReadBattlerNames as a roster.
- [x] Add failing-first cases for bonus names, unique/duplicate targets, stale slots and transitions.
- [x] Implement minimal capture/presentation changes; preserve party HP/MP and feedback behavior.

### Task3: Descriptions with native timing and approved voice

Files: MovieNarrationCatalog/Voice manifest or existing StoryAction capture,
catalog/runtime as supported by reviewed clips; focused movie/story tests/docs.

- [x] Inventory existing narration and actual missing footage; retain Descript results and failure.
- [x] Review exact local footage and native anchors for a concrete set of missing visible actions.
- [x] Add only corroborated cues; preserve custom-name/appearance and introduction boundaries.
- [x] Generate/cache Fish voice clips using the approved model without exposing credentials.
- [x] Verify timing, transcription, non-silence/peaks, resource hashes and native lifetime/skip.

### Task4: Review, deployment and public0.3.88

- [ ] Resolve fresh whole-branch Opus5.5 max review findings with reproduced evidence.
- [ ] Run full solution and exact packaged x86/native checks, confirm real counts and commit identities.
- [ ] Package standalone/AMM without a crash collector; compare every wrapped payload entry.
- [ ] Deploy while the game is closed; verify every installed byte and protected-file hashes.
- [ ] Publish immutable beta/tag and catalog, anonymously verify downloads and preserve history.
- [ ] Record a handoff/closure with explicit unperformed live tests.

## Ledger

- Base89daaa5, public0.3.87 payload559f3c2. Isolated worktree
  `.worktrees/volcano-names-narration-0388`; native worktree tool is known to
  reject this nested repository, so the established ignored Git fallback is used.
- Scope approved: Yes, cover all of those. New log copied after user's recheck.
- Supported EXE/resources hashes freshly match8FE9D75E.../09914BF4....
- Descript project210c466d-d712-4a02-a8ae-292b7595d569 unchanged by read-only
  audit. First job succeeds with limited screenshots; second fails unexpectedly.
- No product change has been made yet. Native naming/narration questions remain
  with read-only Opus5.5 max collaborators; final designs follow their evidence.

- Pre-commit verification: all 5,785 checks pass (Core245, Native1566, Mod3964, Prism10), zero build warnings/errors. Final review and payload-bound native/deployment/publication verification remain.
- Native names are limited to verified bonus-field tables and supported monster ranges. Existing base-area naming policy is unchanged. Borrowed bonus names retain the native discovery bits.
- Partial Opus review found an unproven third rider; corrected to visible winged reptiles. Unproven eye/smile and doorway-person claims removed. Native frame review also corrected cue starts and added a crowned man, kneeling/standing and an armor-clad watcher. Final27 clips pass ASR, clipping, format, windows and exact silence/placement; 177 previous recordings unchanged.
- Root reproduced loss of a native enemy-name popup before a HUD refresh, then added a foreground presented-focus check. The regression fails before that fix and passes with all31 battle-session/runtime checks.
- 84 preliminary original-mover traces pass, including exact681/1785 direct and Story routes. Final packaged assembly binding remains required.
