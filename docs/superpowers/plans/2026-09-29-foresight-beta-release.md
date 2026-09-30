# Foresight initial beta release plan

**Goal:** Publish the existing Chrono Trigger accessibility mod as Foresight, with a usable Windows download, complete controls, and a beta entry in the user's Accessibility Mod Manager catalog.

**Architecture:** Preserve the Reloaded mod ID and installed directory names. Wrap the verified mod, portable loader, native launch helper, and private x86 .NET runtime in a manual ZIP and an AMM ZIP. Publish the source and checksums with a GitHub prerelease.

**Tech Stack:** C#/.NET 9, MSVC x86, PowerShell, Python, GitHub CLI, AMM author CLI.

**Spec:** User's September 29 release request and naming correction: Foresight; initial beta; all hotkeys in README and mod description; ask the FFVII chats about AMM publication.

## Constraints

- Keep `chrono.trigger.accessibility` as the internal mod ID and use version `0.3.40` with beta release/channel metadata.
- Ship no original game executable, movies, private voice recordings, credentials, research captures, or local configuration.
- Preserve the installed approved narration pack. Movie descriptions require a separate local pack and are not part of this public beta download.
- Include source and third-party licensing. Do not claim a complete verified playthrough or universal controller compatibility.
- Preserve existing FFVII catalog entries and releases.

## Tasks

- [x] Brand manifest and README as Foresight beta; retain all keyboard and controller bindings and describe remaining gaps.
- [x] Build the public payload and manual installation instructions. Pin and verify the official private runtime download, include notices, and hash every file.
- [x] Validate supported executable, closed game, payload, and registry ownership in the standalone installer; cancellation/failure must be reported as failure. Test lifecycle behavior without altering the live registry.
- [x] Run the existing test suite, native build, package checks and AMM validators. Obtain a fresh review of release changes.
- [x] Audit tracked source/history for private data and game assets; publish source, binary ZIPs and checksums to `buu420/foresight` as a GitHub prerelease.
- [x] Investigate `chrono-trigger` publication to `buu420/buu-s-mods`. Document the uninstall blocker and defer the catalog entry rather than publish an installation that can make the game unlaunchable.
- [x] Deploy the branded mod locally and verify the approved movie pack remains intact.

## Review focus

- A clean machine needs the bundled x86 runtime without changing global environment variables.
- Manager removal must clear the owned launch redirect before deleting the launcher.
- Failed or declined installation must not report success or leave a redirect to missing files.
- Package paths must be portable and contain no personal installation paths or private media.
- Existing catalog records and the installed narration pack must survive the release.

## Execution ledger

- Baseline: `ad2b40c`, internal 0.3.39; controller behavior confirmed by the user. Publishing and messaging the named FFVII chats are explicitly authorized.
- Both FFVII chats were asked for read-only publishing guidance. The current author catalog is `buu420/buu-s-mods`; central registry listing is separate.
- Ruling: execute the authorized release directly; no additional approval menu is needed for the already requested publication.
- Ruling: defer the AMM package/catalog entry. Current upstream uninstall continues deleting the launcher after declined/failed IFEO cleanup. Both FFVII chats supplied the publishing process; findings and future commands are in `docs/mod-manager-release.md`. The user was informed.
- Native registry tests reproduced and fixed stale-owner overwrite/removal and deletion of unrelated IFEO values. Fourteen checks now pass against an isolated HKCU tree. No live IFEO changes were made by those tests.
- Native build now embeds UAC manifests explicitly and resolves UNC filesystem provider paths correctly. The first C# run found the native test's synthetic absolute path and the old installed-launcher hash comparison; the test now derives its path from the temporary directory, and the new payload was deployed with 25/25 checks before rerunning.
- Source audit inspected 1,479 historical Git blobs: no credential-pattern hits or original game/private media file paths. Untracked `.claude` and local Claude setup notes remain excluded.
- Independent release review found no remaining blockers after the removal fixes; it verified all 274 candidate archive entries and their checksums.

- Final verification: 2,393 C# tests, 14 native registry checks and 4 package tests passed. Final ZIP has 274 entries, complete matching payload hashes, embedded x86/UAC manifests, successful temporary-folder verification, working private runtime, and rejection of a modified mod DLL. ZIP SHA-256: `5527EAB23CB3435FB21C242DCB0886035F5C2255421F9BF2FAE40EFD4B040AF7`.

- Published public repository `https://github.com/buu420/foresight` and prerelease `https://github.com/buu420/foresight/releases/tag/v0.3.40`. GitHub asset digest matches the final ZIP above. Installed Foresight 0.3.40 and all four approved narration/movie hashes verified unchanged. AMM remains intentionally unlisted for the documented cleanup failure.
