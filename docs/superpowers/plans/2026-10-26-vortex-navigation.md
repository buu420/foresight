# Vortex navigation and controller menu repair plan

> Execute inline with the debugging, test-driven-development and executing-plans skills. Claude Opus 5.5 max owns an independent read-only input audit; root owns changes, tests and deployment.

**Goal:** Keep navigation browsing usable, reach the parked Epoch in both guidance modes, and keep the vortex's local route useful.

**Approved behavior:** The user confirmed “Yes, fix the menu and both navigation modes” after the proposal to repair menu input loss, the parked Epoch, vortex entrances and local Story Events. Native locks, player Confirm, vehicle control and puzzle choices remain authoritative.

**Architecture:** Retain native input boundaries and the existing C# navigation pipeline. Reproduce cancellation before changing it. Controller freshness must exclude time spent inside the mod's own synchronous work, while real pauses, disconnects and invalid ownership still revoke movement. Use native boarding shapes to keep an entrance's Confirm approach clear of a parked vehicle. A local vortex objective should supply the Story Events route inside that area, using currently active native passages.

**Approaches:** Repair the shared input and native goal selection, preserving existing cancellation limits. Raising every timeout would conceal genuine loss of control; adding a separate menu or moving the Epoch would change native controls. Neither is required for the approved behavior.

**Tech stack:** C#/.NET 9, xUnit, REA 6.8.0 with Ghidra 12.1.2, native map/script evidence and the tester's 0.3.77 log (SHA256 recorded under `artifacts/research/vortex-navigation-20261026/log-source.json`). Canonical source remains this feature-branch checkout; existing ignored research fixtures are needed. Exclude unrelated `.claude/` and `docs/claude-code-cli-setup.md`.

- [x] Confirm scope, identify log version and separate observed failures from hypotheses.
- [x] Reproduce slow-work controller cancellation; add meaningful RED tests, repair shared freshness, retain real loss-of-control checks and add bounded menu-reset diagnostics.
- [x] Verify native boarding/entrance priority and add RED tests for overlapping parked vehicles. Repair foot entrance goals without generating Confirm or moving vehicles. Existing legal flight landing rules stay unchanged.
- [x] Add RED regressions for local vortex Story Events and both navigation modes; bind to the existing unlocked native passages with unknown state withholding routes.
- [ ] Resolve the Opus audit and focused review. Run relevant suites, then the full solution and Release checks.
- [ ] Version the tester build, package and verify exact x86 payload, deploy with protected-file preservation, and put the tester ZIP and focused instructions in Downloads. Record evidence and live-test limits.

**Review focus:** Slow capture/speech versus a genuinely stale device; queued commands across scene changes; absent/unknown boarding geometry; nonaligned landing phases and disconnected terrain; vortex completion/unreadable state and entrances that lead backward.

No live gameplay, physical-controller, audible-speech or recipient-PC claim follows from an offline regression. Public release is separate from the local tester package unless requested.
