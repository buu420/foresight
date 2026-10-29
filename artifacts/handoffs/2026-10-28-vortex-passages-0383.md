# Foresight 0.3.83: randomized vortex passages

Completed and publicly released on 2026-10-28 (America/Chicago). User approved
current-room passages and required visible controls in Story Events, manual
guidance and automatic walking. Standing authorization includes normal public
release. Native locks, battles and player Confirm remain authoritative.

## Current delivery

- Canonical source: `D:\SteamLibrary\steamapps\common\Chrono Trigger\accessibility-mod`.
  Parent game folder is deployment only. Branch `engine-accessibility-0332`.
- Immutable payload and annotated `v0.3.83` tag: `b21efcaa969f031e72a11135c9a8c6f98caf9867`.
  Public main receives this documentation-only closure after that payload commit.
- Public beta: https://github.com/buu420/foresight/releases/tag/v0.3.83
- AMM catalog: `https://raw.githubusercontent.com/buu420/buu-s-mods/main/index.json`.
  Catalog commit `30845e695b97f8321dd16f95bde3ca98325010d6`.
  `indexPublished`, `liveVerified`, `directorySubmitted` all completed.
- Final standalone SHA256:
  `0c8de2cebaa20e450ef2b819bc490dba931ac3c73de0439b9de7632347ac2313`.
- Final AMM SHA256:
  `fc132488b941099b68ddd0c1ef70bce0413eee3772d7d12017817b02bd69d9cc`.
- Downloads contains both `Foresight-v0.3.83-beta-win-x86.zip` and
  `Foresight-v0.3.83-beta-amm.zip`, checksums and testing notes.
- Final package is `artifacts/release-0.3.83-test-20261028/candidate-3`.
  Candidates1/2 are retained private intermediate artifacts, not public payloads.
- Deployed locally with backup:
  `D:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20261028-191419-961-bcf886d1`.
  Four owned text files refreshed after byte/receipt ownership checks. All275
  installed payload files match; all229 protected files remain unchanged.
  No crash collector included. No game/Steam/input/audio session launched.

## Evidence and root causes

Newest supplied log: `C:\Users\buu42\Downloads\2026-10-28 20.06.26 ~ Chrono Trigger.txt`.
SHA256 `8ecabbb1d8576de77b74eceecf1a65650cfd9cb3588fde2bd7a999e0d2d02eee`;
532446bytes,2305lines, identifies0.3.82. Retained under
`artifacts/research/vortex-ocean-20261028/tester-gameplay.txt`.

Six exact captures: line1128 scene634 and lines2161/2178/2207/2218/2239 scene633.
Conveyor637/660 cases are explicitly static-derived initial-resource states with
synthetic actor bindings and viewport; they are not live captures. Embedded
`vortex-passages-0382.json` preserves these provenance distinctions.

1. Scene633 has32x64 property planes but32x128 exit/script space, byte-identical
   to initial Map633 and original Palace Map175. Native173750 takes tile indices
   modulo property dimensions. Atel0633 actor0 watches leader tileY==121.
   Extra20=128 currently selects597 then601. Navigation formerly stopped at64rows.
2. Scene634 actors27/28/29 span one canyon doorway. All share touch fn2at0825;
   0824 is an empty Confirm handler. The third centre has no standable approach.
3. Atel0659 runs mandatory conveyor battles then E1at0249 to660. Extraction stops
   at the encounter, so router look-ahead lacked this mandatory continuation.
4. Atel0660 actor1 at6016/14079 opens the door on native Confirm or touch. Local6
   goes0->1 and E5 flags0x3B copies55..58,59..62 onto22..25,51..54. Actor9 at
  6016/13823 is concealed behind it and selects its warp from Extra20.

Ghidra corpus checked: engine-0332/native/functions/00173750.c and00175EE0.c.
Atel0633/0634/0637/0659/0660 raw and decoded resources are authoritative.
Web walkthrough research supplied orientation only; it does not authorize paths.
Supported executable SHA256:
`8fe9d75e4cdc279645c5bc932fc163fd67147255fc0c673ac45bbf0a6d2e00d7`.
Resources SHA256:
`09914bf4a8944c0708c947e7eb5683aee4ed48bc6d5d1d75fa07b68131653c7e`.

## Implementation and review

- Extend the existing bounded OceanPalaceTargets projection only to verified633
  dimensions and coherent scene state. Retain original raw planes in diagnostics.
- Require633 warp watcher to remain a usable class7 actor with processing enabled.
- Add659->660 solely as router look-ahead, not an invented walkable exit or combat
  automation. Existing native conditional catalog already has all633 branches.
- Merge only the verified fixed634 doorway markers. Use neutral Vortex passage
  fallback labels without overwriting explicit local feature names.
- Give660's door an explicit native Confirm approach and current Story objective.
  Actor9 stays unlisted unless Local6==1; missing/stale state fails closed.
- New52 passage tests; existing synthetic open-room/coverage fixtures now carry
  native Local6=1. No coverage exclusions were added.
- Claude review used actual `claude-opus-5-5`, effort max, session
  `aee00910-fa84-47ed-bf09-e300ff7bb882`, read-only. It confirmed native logic and
  identified closed-door visibility, label preservation and the0825 comment.
  Those findings were resolved. Its requested open-state test had also been
  added during the review and now asserts disappearance of the spent door.

TDD: initial constructor compiler failure was not behavioral RED. After correction,
verified baseline RED had18 failures/2passes. The stopped-watcher negative caught
one additional defect. Closed/unknown/stale door guards separately produced3
behavioral RED cases before restored source passed. The attempted plain-floor
conveyor replay was invalid because it excludes floor push; it was replaced by
an explicit derived endpoint/Confirm-reach check, not reported as motion proof.

## Verification and limits

-5398 source tests: Core245, Native1517, Mod3626, Prism10; zero failures/skips.
-543 fixture checks against all four exact packaged production DLLs under private
  x86 .NET. All informational versions end in the immutable payload commit;
  production source-DLL fallback is forbidden.
-4 release-package tests,6 AMM manifest tests and303 deployment checks pass.
  Standalone275 files; AMM277 including the staging-only pre-install hook. All275
  wrapped payload bytes match. Other catalog games and all historical releases
  are preserved; all three public assets were anonymously downloaded and hashed.
-15 complete staircase controller traces (five captured positions, manual16,
  manual32, auto32) pass21,209 frames of the ORIGINAL x86 terrain mover175E90 in
  Unicorn with raw64-row planes. Scene jumps disabled; actor/encounter scripts,
  combat, Confirm and actual transitions excluded. Emulation never touched game
  memory or ran the game. Private native-probe sources and result JSON retained.
-Managed walking replays are independently transcribed rules. They do not execute
  original game bytes. Corrected the0.3.82 GitHub release-body wording that called
  its33 managed lab replays original-native; old assets/tag/identity are unchanged.
  Counts in prior records remain valid, but interpret that old wording accordingly.
-Live room transitions, encounter recovery, native conveyor push, controller,
  screen-reader audibility and recipient-PC installation still require testing.
  This is not an audit of every randomized room/seed. Separate native map-texture
  creation crash remains unresolved; audio-description expansion remains paused.

Retest both modes through634 doorway,637 conveyor, native battles659, exit door660
and the full633 staircase. Press Confirm yourself when prompted; restart guidance
following native room transitions and battles. Obtain a complete Reloaded gameplay
log for any missing feature/failure, selected target and current Run setting.

Private evidence: `artifacts/research/vortex-ocean-20261028`.
Public publication receipts: `artifacts/release-0.3.83-public-20261028` including
`publication-complete.json`, `candidate-verification.json`, exact probe, catalog
preservation and anonymous-download receipts. Final closure receipt follows this
handoff commit. Worktree `.worktrees/vortex-passages-0383` retained. User-owned
untracked `.claude/` and `docs/claude-code-cli-setup.md` left in place. The catalog's
untracked `.amm-author.lock` is CLI-owned; tracked catalog files are clean.
