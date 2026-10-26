# Vortex navigation and controller menu repair plan

> Execute inline with the debugging, test-driven-development and executing-plans skills. Claude Opus 5.5 max owns an independent read-only input audit; root owns changes, tests and deployment.

**Goal:** Keep navigation browsing usable, reach the parked Epoch in both guidance modes, and keep the vortex's local route useful.

**Approved behavior:** The user confirmed “Yes, fix the menu and both navigation modes” after the proposal to repair menu input loss, the parked Epoch, vortex entrances and local Story Events. Native locks, player Confirm, vehicle control and puzzle choices remain authoritative.

**Architecture:** Retain native input boundaries and the existing C# navigation pipeline. Reproduce cancellation before changing it. Controller freshness must exclude time spent inside the mod's own synchronous work, while real pauses, disconnects and invalid ownership still revoke movement. Use native boarding shapes to keep an entrance's Confirm approach clear of a parked vehicle. A local vortex objective should supply the Story Events route inside that area, using currently active native passages.

**Approaches:** Repair the shared input and native goal selection, preserving existing cancellation limits. Raising every timeout would conceal genuine loss of control; adding a separate menu or moving the Epoch would change native controls. Neither is required for the approved behavior.

**Tech stack:** C#/.NET 9, xUnit, REA 6.8.0 with Ghidra 12.1.2, native map/script evidence and the tester's 0.3.77 log (SHA256 recorded under `artifacts/research/vortex-navigation-20261026/log-source.json`). Canonical source remains this feature-branch checkout; existing ignored research fixtures are needed. Exclude unrelated `.claude/` and `docs/claude-code-cli-setup.md`.

- [x] Confirm scope, identify log version and separate observed failures from hypotheses.
- [x] Reproduce slow-work controller cancellation; add meaningful RED tests, repair shared freshness, retain real loss-of-control checks and add bounded menu-reset diagnostics.
- [x] Verify native boarding/entrance priority and add RED tests for overlapping parked vehicles. Prefer clear foot contacts with a strict native-boundary fallback, and select legal landing sites that leave a connected entrance contact clear. Native landing restrictions and player Confirm remain authoritative.
- [x] Add RED regressions for local vortex Story Events and both navigation modes; bind to the existing unlocked native passages with unknown state withholding routes.
- [x] Resolve the Opus audit and focused review. Run relevant suites, then the full solution and Release checks.
- [x] Version the tester build, package and verify exact x86 payload, deploy with protected-file preservation, and put the tester ZIP and focused instructions in Downloads. Record evidence and live-test limits.

**Review focus:** Slow capture/speech versus a genuinely stale device; queued commands across scene changes; absent/unknown boarding geometry; nonaligned landing phases and disconnected terrain; vortex completion/unreadable state and entrances that lead backward.

No live gameplay, physical-controller, audible-speech or recipient-PC claim follows from an offline regression. Public release is separate from the local tester package unless requested.

The Opus audit reproduced a second cause of lag: about 1,092 repeated story-state enumerations per reroute. Prepare each objective's distance map once, keep value-based invalidation between captures, and verify work by enumeration count. The native loop runs up to ten task ticks per physical poll. Add that regression alongside real poll gaps and announce unexpected menu closures, including queue overflow and manual takeover.

The focused review found that 659/660 are shuffled vortex Geno Dome copies. Include them while retaining classic Geno Dome scenes248/256/268/127. Scene659 tests use an explicit synthetic live-exit override and withhold routing when it is absent. Suppress identical routed Story Events duplicates and verify the selected target ID. Native boarding extents were independently decoded from bankc2_half.bin: party8/8/8/8, Epoch and Dactyl0/0/0/8. Live captures still supply production values.

Completed as tester build0.3.78, payload commit2bf26ef53a533332e0662a436a52cd52fd5d5354. The full rerun passed4,864 tests; the exact-package x86 replay passed167 checks; deployment and independent verification passed303 checks each; all229 protected files remained unchanged. The final Opus5.5max closure found no blockers. Candidate2 supersedes candidate1. The ZIP and checklist are in Downloads. The user subsequently requested release: GitHub beta and the buu420 Amethyst beta catalog are public and anonymously verified, with the exact same standalone artifact. Fresh publication checks passed4,864 tests,167 x86 checks, four Python package tests and six AMM manifest tests. Live gameplay remains pending. See artifacts/handoffs/2026-10-26-foresight-0378.md.
