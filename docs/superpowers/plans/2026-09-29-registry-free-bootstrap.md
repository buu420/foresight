# Foresight registry-free bootstrap and manager release

**Goal:** Follow the user's corrected requirement: normal Steam launch and mod-manager installation without creating registry entries, like Blind Soldier.

**Architecture:** An x86 game-folder `winmm.dll` forwards the Windows multimedia API and starts a local attach helper. The helper validates the existing game process, temporarily leases Reloaded's configuration, injects the bundled bootstrapper and restores the configuration after game exit. This reuses Blind Soldier's proxy/attach design and Foresight's tested injection/lease code. No IFEO registration or system environment changes are used. Old Foresight-owned IFEO values are removed once during migration.

**Evidence:** Ghidra's complete engine export records `WINMM.DLL::joyGetPosEx` and `joyGetNumDevs` as imports. Blind Soldier's x86 WinMM proxy, export table and attach helper are available locally; the FFVII chat has been asked for current loader-lock/readiness findings.

## Work

- [x] Add proxy and attach helper with x86 export-forwarding smoke tests and process/path/event validation.
- [x] Replace public installation/deployment with file-based loading; keep old registration removal as migration only. Preserve foreign DLLs, registry values, narration and saves.
- [x] Update packaging and release verification for the proxy/helper and registry-free layout.
- [x] Build and test, including a controlled normal game launch and log verification if the game is closed.
- [x] Build and validate AMM package with no registry-creating post-install hook and no required uninstall cleanup. Include all controls and beta limitations.
- [x] Publish the next beta and add it to the existing author catalog, preserving all FFVII entries; verify public asset hashes.

## Rulings

- The earlier proposed registry-based AMM hooks were uncommitted and unpublished. They are superseded by this user correction.
- Removing the old mod-owned registration is authorized migration, not a registry dependency for the new mod.
- The existing public 0.3.40 prerelease remains historical; the new loader is released as 0.3.41 beta after validation.

## Verified decisions

The current Blind Soldier release uses a Version proxy. Its separate forwarding/bootstrap readiness was used here; its historical WinMM wait-for-injection cycle was not reused. Foresight also requires a per-launch managed acknowledgement after Prism and all required hooks become active.

Normal Steam startup was verified with the IFEO values absent in both registry views. Native and managed readiness, NVDA and startup announcements were logged. Game exit restored the original Reloaded pointer. Executable and narration hashes remained unchanged. A cold call to the proxy from another DLL's DllMain remains an unverified compatibility boundary; no such supported-game failure was observed in the real launch.

Published Foresight v0.3.41 as a GitHub prerelease and catalog beta. Public asset SHA-256 values matched local packages. Catalog commit 66b36ee preserves the two existing FFVII game definitions and all 114 older release records.
