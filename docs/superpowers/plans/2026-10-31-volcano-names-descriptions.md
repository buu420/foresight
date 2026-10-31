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

- [x] Resolve fresh whole-branch Opus5.5 max review findings with reproduced evidence.
- [x] Run full solution and exact packaged x86/native checks, confirm real counts and commit identities.
- [x] Package standalone/AMM without a crash collector; compare every wrapped payload entry.
- [x] Deploy while the game is closed; verify every installed byte and protected-file hashes.
- [x] Publish immutable beta/tag and catalog, anonymously verify downloads and preserve history.
- [x] Record a handoff/closure with explicit unperformed live tests.

## Ledger

- Base89daaa5, public0.3.87 payload559f3c2. Isolated worktree
  `.worktrees/volcano-names-narration-0388`; native worktree tool is known to
  reject this nested repository, so the established ignored Git fallback is used.
- Scope approved: Yes, cover all of those. New log copied after user's recheck.
- Supported EXE/resources hashes freshly match8FE9D75E.../09914BF4....
- Descript's imported-footage render failed. Timestamped local frame sequences,
  original native assets and Ghidra timing evidence verify the two new films.
- Final payload/tag b4b4d5daf7f09c2d4668bbf79a59443fbe415b0c. Fresh full
  Release verification passes all 5,788 tests (Core245, Native1566, Mod3967,
  Prism10), with zero build warnings/errors.
- Native names are limited to verified bonus-field tables and supported monster ranges. Existing base-area naming policy is unchanged. Borrowed bonus names retain the native discovery bits.
- Partial Opus review found an unproven third rider; corrected to visible winged reptiles. Unproven eye/smile and doorway-person claims removed. Native frame review also corrected cue starts and added a crowned man, kneeling/standing and an armor-clad watcher. Final27 clips pass ASR, clipping, format, windows and exact silence/placement; 177 previous recordings unchanged.
- Root reproduced loss of a native enemy-name popup before a HUD refresh, then added a foreground presented-focus check. The regression fails before that fix and passes with all31 battle-session/runtime checks.
- All 84 original-mover traces pass, including exact681/1785 direct and Story
  routes. Final Core/Mod hashes and informational versions match the package.

- Final independent Opus review cleared the code after the explicit swordsman
  wording and two new Fish takes. The last unknown-name punctuation minor now
  includes the native Spanish form; all six area tests and 29 movie-resource
  checks pass together. Native byte/DWORD
  conservatism is documented. No sword handover or hidden watcher identity is
  inferred.
- The immutable package passes 1,037 checks in private 32-bit .NET, nine silent
  native movie checks, four release-package tests and six AMM manifest tests.
  All 275 standalone files match the 277-entry wrapper and local installation;
  303 fresh deployment checks pass. The game stayed closed.
- Published https://github.com/buu420/foresight/releases/tag/v0.3.88 and catalog
  commit3e9fba02c6ca705158bb8e20a4b665ef6302b941. All three assets and the
  catalog were anonymously downloaded and verified. Historical/unrelated
  records are preserved; indexPublished/liveVerified/directorySubmitted pass.
- The original 244-file protection audit remains retained: 243 unchanged files
  and one approved source voice-manifest metadata addition for005/008. All177
  previous recordings and both user-owned source files are unchanged.
- Handoff: artifacts/handoffs/2026-10-31-volcano-names-narration-0388.md.
  Live gameplay, physical input, audible speech and recipient-PC tests remain
  explicitly unperformed; the texture-creation crash remains unresolved.
