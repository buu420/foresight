# Foresight 0.3.67 publication audit

[Foresight 0.3.67 beta](https://github.com/buu420/foresight/releases/tag/v0.3.67)
is published and deployed locally. The immutable tag identifies source commit
`87a7cb0b017ae8618b6b213acf80ba7a7cb82ce4`. Later publication documentation
does not change that tag or its assets.

The update repairs Ocean Palace's full-height stair, Masa reveal, lower-switch
encounters, elevator approach and disconnected chamber targets. Named story
characters use verified identities and introduction gates; parked Algetty
characters and premature Dalton entries are omitted. The bridge panel remains
listed while inactive. Lavos owns the native approach interaction, and cinematic
map jumps stay out of Exits. Thirteen supported Palace actions and both
original movie007 variants use the approved narration voice.

## Verification

- Full suite: **4,162 passed**, zero failures or skips: Core226, Mod2640,
  Native1286, Prism10. Tagged package/voice contracts: **35 passed**.
- Release checks: four Python, six AMM manifest and eighteen PowerShell
  cases; deployment path and canonical path checks also passed.
- Ghidra and original x86 movement evidence establish the stair's native
  collision wrapping. Captured-map regressions exercise both navigation modes.
- Claude **Opus 5.5, maximum reasoning** audited and implemented bounded tasks,
  then reviewed navigation, movie visuals and exact native description anchors.
  Review findings received regressions and fixes before publication.
- Movie007's corrected child close-up cue passed transcript, format, clipping,
  timing and placement checks. The original native Media Foundation player
  reached normal completion after pause/resume, with no failed media events.
- Deployment: **303 passed**, zero failed. Setup verification passed again
  after refreshing the owned documentation and manifest from the public ZIP.
- All **275 installed payload files** match the anonymously downloaded
  standalone ZIP. All 28 managed package files also match the AMM payload.
- All 41 protected files, ten save/settings files, and 160 earlier WAV files
  are unchanged. The new pack contains 172 short cues, five unique movie
  masters and six movie identity declarations: **177 unique WAV files**.

## Published files

| File | SHA-256 |
| --- | --- |
| Foresight-v0.3.67-beta-win-x86.zip | `3295249a5cf17c9ae83b4df58c7b15b31d97bd87344e31f1edc6ef5aaf0af58c` |
| Foresight-v0.3.67-beta-amm.zip | `dd8c8b87be30e3e154507c365ce89dd844106b80fd4314ebd892eb3291591865` |
| SHA256SUMS.txt | `01aea91bb9c024dbe5cfab24da9596be018f6a786e3d486c9f96c02a17d4da65` |

GitHub draft assets, public asset digests and ordinary anonymous download URLs
were verified. The AMM digest is the final `packageValidated` digest after
the staging-only hook manifest repair, not its earlier `packageBuilt` digest.

AMM Author CLI **0.30.0** validated and published the author catalog with phases
`indexPublished`, `liveVerified`, and `directorySubmitted`. The fresh remote
baseline was checked immediately before publication. Historical Chrono
releases and unrelated records are preserved. The entire ordinary public
[catalog](https://raw.githubusercontent.com/buu420/buu-s-mods/main/index.json)
matches commit `7f59f3d798c736848248e7749b1e174b3e77e998`.

## Limits

No live game was launched for this update. Live traversal, physical controller
input, audible screen-reader output, narration pronunciation, balance and
cut synchronization remain unverified. Camera-gated cues may be skipped if the
native viewport does not yet contain their subject during a pan. This is
selected description coverage; unanchored camera effects and the later
world-map disaster sequence remain outside it. The earlier keep desktop crash
remains unconfirmed. See the [native audit](../native-audits/2026-10-17-ocean-palace.md).

The prior local installation is backed up under
`Accessibility/Backups/20261017-131157-973-a19ca370`. Credentials, private voice
references, game movies, tester logs and research footage are excluded from
the public packages.
