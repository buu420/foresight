# Foresight0.3.87: present vortex switches and section-aware routes

Public beta: https://github.com/buu420/foresight/releases/tag/v0.3.87
Standalone and AMM assets are public and anonymously download-verified.
AMM completed indexPublished, liveVerified and directorySubmitted.

## Immutable payload and installed state

- Payload/tag commit: `559f3c20bc7406f1c18b27a43828dab44303ccc3` (parent73b80b1; baseca90e13).
- Catalog commit: `04ea191230c1ccc6d822a9892b821cd0490898a7`.
- Standalone SHA256: `2b7e556a5c6f39c6806361d6d8c61f7b67c5d55690defdc73ce805879ce2600b`.
- AMM SHA256: `001844b30889b0d598c1074ee211b22a55b883a7d1983b975c8660228fcf0fc3`.
- Final standalone: `artifacts/release-0.3.87-test-20261030/candidate-3`.
- Final AMM: `artifacts/release-0.3.87-public-20261030/amm-candidate-2`.
- Downloads contains both0.3.87 ZIPs, standalone and combined checksums, and
  `Foresight-0.3.87-testing-notes.txt`.
- All275 installed files match;244 protected files remain unchanged, including
  all4 saves. A current save-directory inventory found no unrecorded files.
- Final deployment backup: `D:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20261030-093011-548-03df1412`.
- ModConfig SHA256: `E9C5065DB8109AD0E128F578C65659595F4EDF2D76C43AE114488D3F6E7826DE`.
- Native bootstrap/proxy unchanged (DEC62345.../8952FE31...). No crash collector
  is distributed; the existing local collector is protected.

## Report and native evidence

Downloads `2026-10-30 11.43.14 ~ Chrono Trigger.txt`:1244302 bytes,4588 lines,
SHA256 `a7ebca873e050b13643286a69698bbda219a25a028615dd69d082367987fed9c`.
The texture diagnostic identifies0.3.86. Research is retained privately under
`artifacts/research/present-vortex-switches-20261030`.

The five exact route captures include scene608/no-route1842 and614/blocked2900.
Twelve additional fixtures are explicitly static-derived; none are claimed as
complete live captures. Test viewports cover the whole map. EXE/resources hashes
freshly match the retained Ghidra corpus (8FE9D75E.../09914BF4...). Primary CTViewer
resource/tool evidence was also consulted. No game or analysis UI was launched.

608 has disconnected A/B walking regions. The first switch must be reached via
608A ->610 ->614 exit1 ->615 north warp ->608B. After the switch,609 is connected;
the later route is609 ->611 exit3 ->613.

608 actor8 polls at live PC[0x17A,0x1FA), Extra35 bit1 clear, standing tile22,14.
613 actor8 polls[0x185,0x1E9), bit2 clear, standing tile7,8. Their visible wall
tiles are22,12 and7,6. Native31 tests Confirm. Native24 writes facing to Local3,
while the script compares Local0, so all four facings are accepted. C0 prompts
exms3#17/#18; native Yes sets the flag and reloads/changes scene, No resumes the
watcher. The mod does not choose or press Confirm.

Six tile chests are native records328/329 in612,330/331 in614,332 in615 and337
in649. The other two treasures are existing sprite pickups:610/611 actor19 and
615 actor18.614's upper330/save655 path remains blocked until the native ladder
copy (E4 at039F,23,35..40 ->39,8..13 on all three planes). Collected331 is absent
in the exact614 capture; the static entry fixture is used for unopened331 checks.

## Changes

- PresentVortexTargets exposes only the active, verified switches as Objects.
  It removes their confirmation-dependent Terrain/Warp consequences from target
  and router exit lists. Unknown/spent flags, removed class or disabled/stale PC
  cannot fabricate a switch. Arrival asks the player to examine it with Confirm.
- PresentVortexRoutes chooses actual current-room passages to the first ledge.
  FullStoryTargets treats this substitution as routing, deduplicates the bonus
  copy and keeps it in Story Events; an exit no longer pretends to be a switch.
- Narrow script openings include body-clear sampled columns in608/609/612/615.
- Native Run can stop25 units short of the walking graph's corner in the exact
 614 capture. The coherent614-only StalledCornerTolerance permits a next-leg
  turn after an observed250ms stall, within32 units, same layer, nonterminal,
  perpendicular and with a graph-proven neighbour from the real foot. Bounded
  next-leg steering avoids correcting back into the refused shortfall. Plan and
  Stop clear it; terminal/Confirm reach and Run/Dash settings remain unchanged.
- A Confirm rule accepting all four native facings no longer invents facing
  advice when facing cannot be read. Empty/partial reach remains fail-closed.

## Verification and review

-5713 full tests:Core245,Native1523,Mod3935,Prism10;91 new focused cases.
-858 exact packaged x86 checks. Four production DLLs load only from the payload;
  informational versions end in the immutable payload commit. Source DLL fallback
  is forbidden. Core/Mod hashes also match the native replay's final binaries.
-68 original-x86 terrain traces/62818 frames, both modes, unchanged16/32 speeds,
  native chest grids, all6 chest records and both switch stages. Actors, ATEL,
  battles, Confirm and actual room transfers are not executed. Independent review
  also checked no trace enters another exit/warp before its target.
-4 Python package tests,6 AMM manifest tests and303 deployment checks pass.
-275 standalone/277 AMM entries; every wrapped standalone byte matches. All3
  public asset digests/tag, anonymous downloads and public catalog are verified.
- Native investigation and independent full/follow-up reviews used actual
  claude-opus-5-5, effort max; transcript model IDs are checked. Initial controls
  have20 failing-first cases. Restored Run32 cases fail in both modes before the
  turn repair. Six false-object and4 unreadable-facing cases fail before the final
  fix; all91 focused cases pass afterward. Reviews report no remaining blocker.
- Preliminary gait advice was rejected after native review demonstrated valid
  Run slides. The first turn prototype corrected back after one frame and failed
  later at9097,14293; bounded leg steering resolves it. These prototypes were
  private. Candidate2 was only deployed locally and its old Downloads files are
  preserved privately; only candidate3 is public.

One low review point is deliberately retained: corner recovery consumes that
tile's stall retry and can stop if a second refusal occurs in the same tile.
Removing the marker alone could enable repeated promotions. Both reviewers
accept the bounded stop; no retained trace demonstrates it failing. A future
change would need a separate recovery latch and a demonstrated actor/Run case.
Story's Next passage prefix can use the pre-alias To Volcano label; the selected
native route is correct and the Exits list retains its unique labels.

No live gameplay, physical controller, audible-screen-reader or recipient-PC
installation test occurred. Test the bookmarked Volcano save, Story Events and
both navigation modes, examine both switches, make the native choices, and try
the nearby treasure routes at walking/running speed. Restart after battles and
room/control changes. Check switch speech with the player's normal screen reader.

## Resume and preservation

Canonical branch is engine-accessibility-0332. The payload worktree
`.worktrees/present-vortex-0387` remains clean at the tagged payload commit.
Main receives a documentation-only closure commit after the immutable tag; never
rebuild or replace public assets for that doc commit. Only the pre-existing
`.claude/` and `docs/claude-code-cli-setup.md` remain untracked; their two exact
file hashes are unchanged. No Codex subagents, game launch or global memory writes.

Public-release authorization persists. Broader narration remains paused, and
the separate native map-texture crash remains unresolved.
