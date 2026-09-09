# Settings and resolution integration plan

The user approved integrating the existing accessibility milestone with the portable Reloaded loader, specifically restoring Settings narration and keeping speech working after a resolution change.

The existing specification is `../specs/2026-07-22-extras-settings-dialogue-field-menu-accessibility-design.md`. Preserve native, visible information, exact executable and hook-byte validation, Prism output, and the portable loader. Do not change the original game executable or invent labels from screen coordinates.

- [x] Preserve the existing uncommitted work as a binary Git patch outside the repository and repair the worktree's stale drive mapping.
- [x] Integrate `main` into `feature/extras-settings-dialogue-menu`, retain the portable loader scripts and documentation, and run a clean-environment baseline. Include all existing uncommitted Settings and shared-hook changes.
- [x] Review `SteamSettingsHookSet`, `SteamSettingsCapture`, the shared native fanout, and the Ghidra resolution callbacks with Claude. Reproduce stale-focus classification in the hook harness and add regressions before fixing runtime behavior. Live player reproduction remains a separate check below.
- [x] Verify every expanded native hook contract against the supported executable in Ghidra. All 112 contracts matched. See `../../native-audits/2026-09-09-settings-resolution.md`.
- [x] Build and package the combined implementation. Deploy to the installed game through the transactional deployer with the original executable hash intact. Version 0.2.0: 25 deployment checks and 27 installed payload checksums passed; backup `Accessibility\Backups\20260909-175956`.
- [ ] Player test: Settings entry, category and value navigation, Screen Size opening, resolution selection and return, repeated Settings entry, and title-menu speech after the display change. The user explicitly owns game control; do not use Computer Use. Correlate their report with semantic logs.
- [x] Run the complete test suite (692 passed), record the pending player check and coverage boundaries, and update the installed documentation. Preserve the validated integration in both branch histories.

Verification commands use the Windows PowerShell module path for subprocesses; the Codex PowerShell runtime otherwise passes incompatible module paths to Windows PowerShell. Tests must locate the real game by walking ancestor directories, so the preserved worktree receives the same executable checks as the primary checkout.

The subprocess test helpers now set that module path themselves. Packaging tests also disable MSBuild node/server reuse in their child environment so completed builds cannot retain redirected output handles. The final suite passed from the ordinary Codex shell without environment overrides.

The pre-integration patch is `C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-recovery\pre-integration-uncommitted.patch`, SHA-256 `5A5EC9D269347A0FE2074A238BD07FD8CC4C357188FD0018EE16C07D17558195`.
