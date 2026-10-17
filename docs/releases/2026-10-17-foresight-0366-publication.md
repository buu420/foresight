# Foresight 0.3.66 publication audit

Published beta: [v0.3.66](https://github.com/buu420/foresight/releases/tag/v0.3.66).
Source/tag commit: `2910cc4d96f0d056cd5a8d765e1207b46d5a042d`. The tag and all three published assets are immutable.
The later publication-record commit does not rebuild or retag them.

## Delivered behavior

- Keep entrance door: live-floor opening contact in manual guidance and auto-walk,
  with the existing approved-voice passage-opening cue after the verified native copy.
- Ozzie's central-chamber touch is a required story step. Touch-only Long Passage
  figures retain their native contact without premature enemy labels.
- Identical lights have unique appearance labels and equal selectable choices,
  including off-camera Story Events. Private trap roles do not identify a light.
  Native on-tile saving feedback identifies saving; return warps have clear labels.
- Unopened invisible pits are omitted as targets and hidden route barriers.
- The external helper logs memory counters and the process exit code.

**The desktop crash is not confirmed fixed.** There is no stack or termination
status in the tester log. The native early keep music stop explains the missing
background music in this phase; audible game effects and output remain unverified.
The earlier native movie video-memory error is not established as the crash cause.

## Verification

3,869 automated tests passed (Core226, Mod2447, Native1186, Prism10), including
53 new keep cases. Thirty-three tagged package/resource contracts and 28 release
checks passed, plus six deployment path checks and the canonical-path check.
Twelve offline original-x86 door movement/contact replays passed. Opus5.5/max
performed the native audits and code reviews; all review blockers were resolved.
Original x86 proxy/helper builds passed and the final helper is in both archives.

Deployment:303 passed/0 failed. All275 public standalone payload files match the
installed files byte for byte; all28 managed package files match both archives.
The AMM archive has277 files with a staging-only pre-install hook. Setup Verify
passed after the final public-documentation refresh.

All41 protected files and10 save/settings files are unchanged. All160 prior WAVs,
their manifest and the asset set are unchanged; no new recording was generated.
There was no game/Steam launch, physical controller test, player Confirm,
audible-Prism confirmation or live keep playthrough for this release.

## Public hashes

| Artifact | SHA256 |
|---|---|
| Standalone | `8b5d914dcc2902d1354b81f49316a16672b7bd34963e0252d4978707f02fd191` |
| AMM, after manifest repair | `cbee5017e3ee96de81b848bd259c1f2a6b8b96e1313f2e76b9585260c178a5ee` |
| Mod DLL | `d9300060c40d4253f1cff089872236bc0d463b0e226aeb23ce9abb9a40bfafcc` |
| Core DLL | `3c9eae3b84f0817fe12a65bc0e4c65747560230d703b9b003425966debd24bd5` |
| Native DLL | `08945348da74bda7e4f7bc67047f0987dc1acda485f3533590196bbb75080220` |
| x86 bootstrap helper | `0b11655c60b920e16c9c2af3640340f889170456f4d3d2546b4b0575ab4b682b` |

All three release assets were downloaded anonymously at their normal URLs and
matched the candidate bytes. The complete ordinary public catalog matched
commit `3a9b437cf38885707a09cb28f1f1ebc513fdc5e6`; CLI0.30.0 completed indexPublished,
liveVerified and directorySubmitted. Other games and all historical releases
were preserved. Catalog baseline was32843c84281a7f8726cf0edeb57dad6e0936dc6b.

See [the native audit](../native-audits/2026-10-16-keep-navigation.md) for the
captured-state basis and [release notes](v0.3.66.md) for tester instructions.
