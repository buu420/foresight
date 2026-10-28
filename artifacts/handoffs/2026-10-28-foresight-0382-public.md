# Foresight 0.3.82 public beta: Temporal Research Lab

Public release: https://github.com/buu420/foresight/releases/tag/v0.3.82

The user's current instruction resumes public releases for ordinary fixes. The special tester-only ZIP was for crash investigation. This release includes the previously unpublished 0.3.81 Lost Sanctum changes; no separate 0.3.81 public release was created. The user approved visible warp-pad labels, native landing-area identification and reachable Story Events/manual/automatic warp progression, retaining optional branches and native controls. No further permission is needed to distribute this completed update.

## Source and immutable build

Canonical source: `D:\SteamLibrary\steamapps\common\Chrono Trigger\accessibility-mod`. Parent game folder is deployment only. Base0d3fffe; worktree `.worktrees/temporal-lab-0382`, branch `fix/temporal-lab-0382`, retained for build provenance. Commits0b7f3c7/5b3b71d/9cfad13 implement the plan and native authority;99ded6a resolves review and is the immutable payload/tag commit.

**Payload/tag commit:** `99ded6adfd1fafaf5d29dc22166bfc3b2efa9aad`. All four production DLL informational versions contain that exact commit. Later documentation does not change the payload identity. Both packages are candidate2, not candidate1.

- Standalone: `Foresight-v0.3.82-beta-win-x86.zip`, SHA256 `cd4c31bb5f78e656e71d5e835ea7d27900f4c2f043e9ea39d1d4342484a2a182`.
- AMM: `Foresight-v0.3.82-beta-amm.zip`, SHA256 `f9b41ff646fdeb00d0b2d404b6d0cd17f3d9a7b63a9efa5e02e9978961a7bffd`.
- 275 standalone files;277 AMM entries, with all275 wrapped payload bytes identical. The extra entries are the manifest and staging-only pre-install hook. No collector.
- Public 3asset set includes SHA256SUMS.txt. GitHub tag target, uploaded sizes/digests and anonymous downloads were verified.
- AMM Author CLI0.30.0 completed indexPublished, liveVerified and directorySubmitted. Catalog commit `50c304ec31b756ef6ea875b870746d317864a708`; anonymous public index SHA256 `1e24bacd3227d287c8876fa56a4a38936faa339184001ec50fd14612509a8ed0`. All5games and203prior release records are preserved; only the Chrono description and0.3.82 beta record change.

Public receipts: `artifacts/release-0.3.82-public-20261028/publication-complete.json`, candidate-verification.json, public-release.json, public-download-verification.json, public-catalog-verification.json and catalog-history-preservation.json.

## Final behavior

- All 15 verified E7 pads have stable A–O labels and neutral native landing-area descriptions. Pad B lands in a western column, not a junction.
- A bounded same-room warp graph finds the fewest native warps and returns only the current real walking leg. Guidance announces the next pad. Every warp reloads621; the player restarts guidance after landing, as after native control loss/battles.
- Initial forward chain:8→11→12→14→door23→exit1. Native E1 flags04/08 determine exact fractional landings. Native scripts own movement transitions, battles and all flag changes.
- Pad authority requires exact scene/class/visual/position/idle PC, known idle priority, load/draw/script gates and the callable actor17 dependency for 18–22. An active stage stops if its current pad loses authority.
- Edge validation uses the same pad goal that planned the named stage, fixing false off-route speech and repeated replans once the pad is on camera. Other stage kinds retain their existing policy.
- Warp connectivity retains Story Events binding and visible/discovered destinations. Unseen disconnected pads, encounters and optional items remain out of entity lists. The native optional branches in the current section remain available.
- Future connectivity after an E1 reload does not inherit current creature positions. The current walking leg keeps native collisions and ordinary creature waits. The remote-block case is synthetic robustness coverage, not an attribution to a live tester failure.
-621door23 is a native Confirm interaction; touch is geometrically impossible. Story Events reaches it while local30=0, explicitly says to press Confirm, then resumes the actual exit after its native copy opens. Unknown native readiness remains pending.617door11 retains its separately proven guarded touch opener.
- The inherited0.3.81 fixes cover current-area/current-era Sanctum objectives, the open Lightless Cave, Waystone/Nu/builder contacts, fortress passages and Winding Passage walking cues. Native unfinished-bridge restrictions remain.

## Evidence, review and tests

Research: `artifacts/research/temporal-lab-20261028`. Log `2026-10-28 14.39.47 ~ Chrono Trigger.txt` is retained unchanged; SHA256 `5ff13cffdabd76594f3ae22452fc1164bdd7fbe100d1e79e87072757c106f917`. It reports 0.3.79. There is one exact scene617 route capture. Scene621 combines native initial-resource geometry, logged actors and explicitly reconstructed state. Landing poses and positive on-camera bindings are derived/synthetic, not new live captures.

Executable SHA256 `8fe9d75e4cdc279645c5bc932fc163fd67147255fc0c673ac45bbf0a6d2e00d7`; resources SHA256 `09914bf4a8944c0708c947e7eb5683aee4ed48bc6d5d1d75fa07b68131653c7e`. Raw Atel0617/0621/0622 and the validated Ghidra corpus establish handlers, native contact/facing, landing fractions and reload/local reset behavior. Web material was orientation only.

Claude used actual **claude-opus-5-5**, requested **max**, with no model fallback. Native audit sessionbcb16cb6-e7c3-46b6-9f4c-d2e9cdb40ba9; whole-change review369f669a-0966-42bd-b434-0c8a060757a7. `review-resolution.md` records M1/M2/M3 and minor rulings. The review's initial seven regressions were RED on 9cfad13; they pass in the final suite. The superseded frozen-zero-door-binding probe is not readiness proof.17A6C0 clears +20 for hidden actors, correcting the audit's concern about hidden enemy26 contacts.

- **5346 source tests:**245 Core,1517 Native,3574 Mod,10 Prism. Both the isolated source suite and canonical suite against the stable final package stage passed.
- **491 exact payload checks:** the private x86 CLR loads all four production DLLs only from candidate2; source-DLL fallback is forbidden. Their hashes and informational versions match the package. Fixture assemblies come from the final canonical build.
- 80 new lab cases include 33 original-native movement first legs from 11 logged/derived poses (manual16/manual32/auto32), expected next pads, on-camera stage following, authority/gate revocation, native door handling/readiness, current/future collision separation and unseen-item culling. The native mover executes no actor scripts, AI, battles, Confirm or E1.
- 4 Python package tests,6 AMM manifest tests, archive closure, x86/no-elevation checks, private CLR startup, clean installer verification and corrupt-payload rejection passed.
- Reviewer export Native failures were missing-resource layout errors; one pre-existing timing test failed under parallel load and passed alone. The final canonical suite is green.

## Local delivery and preservation

Deployed to `D:\SteamLibrary\steamapps\common\Chrono Trigger`; game and bootstrap were closed. Backup: `D:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20261028-144812-995-6f4a33aa`. Developer deployment passed 301 checks with registry migration skipped; independent normal verification then passed 303/303. No game or Steam launch, physical input, screen-reader/audio test or messages to testers occurred.

All 275 installed files match the standalone archive;229 protected files including saves, settings, narration and unrelated mods are unchanged. The deployer and finalizer backed up4owned text/manifest refreshes. Four differences were line-ending normalization only and required both the old installed receipt and normalized-content equality. The AMM staging hook similarly matches its exact worktree packaging source and normalized committed/canonical content; see amm-hook-line-endings.json. No functional hook change was made.

Downloads contains the standalone ZIP, its .sha256, the AMM ZIP, combined `Foresight-0.3.82-release-SHA256SUMS.txt` and `Foresight-0.3.82-testing-notes.txt`. Standalone installation: close the game, extract beside Chrono Trigger.exe and run Install Foresight.cmd. AMM: refresh buu420 and select beta.

## Limits and retained state

Live gameplay, actual pad/door +20 captures, physical-controller input, audible speech and recipient-PC installation remain unverified. The separate native map-texture creation crash remains unresolved. No new voice/narration work was performed; that expansion remains paused. Retest the forward chain, optional branches, door Confirm, return route after registration and the inherited Sanctum changes in both modes. Restart guidance after native reload/control loss. Request complete Reloaded gameplay logs for failures.

Native-audit scope incident: despite explicit read-only limits, Claude briefly created one project-memory note and edited two existing Claude-memory files. It deleted the new note and reported restoring the previous text. Root readback confirms route-evidence prose differs only by managed modified metadata/trailing blank line; exact original bytes/timestamps have no baseline. Root made no memory writes. See claude-native-scope-incident.json. Whole-change review had no Write/Edit memory tools. The user was informed. No Codex memory update was authorized or made.

Unrelated untracked `.claude/` and `docs/claude-code-cli-setup.md` remain untouched. Do not clean other worktrees. The only known minor UI residue is the existing bonus Objects/story alias pattern's duplicate door row; it does not disclose unseen sections or change native controls.
