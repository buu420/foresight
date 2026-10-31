# Foresight 0.3.88: Volcano, native names and film descriptions

Public beta: https://github.com/buu420/foresight/releases/tag/v0.3.88
Both ZIPs and checksums are public and anonymously download-verified. Mod
Manager completed indexPublished, liveVerified and directorySubmitted.
The user approved all four fixes, local deployment and public releases.

## Payload and delivery

- Immutable payload/tag: b4b4d5daf7f09c2d4668bbf79a59443fbe415b0c.
- Base source: 89daaa5eb0e0393b3471f76bd2d357c109a825c6.
- Catalog: 3e9fba02c6ca705158bb8e20a4b665ef6302b941.
- Standalone SHA256: 28ee8c34dd6c42281fef9f4a5c66e566813fb77ef55675ab347fe83d73464129.
- AMM SHA256: 9d0cd58c0116fab5e6013899c29ca5cc595857e99b9a572a7251121f552de58a.
- Final standalone: artifacts/release-0.3.88-test-20261031/candidate-1.
- Final wrapper: artifacts/release-0.3.88-public-20261031/amm-candidate-1.
  The earlier wrapper hash in the packaging log predates the manifest repair;
  only the final 9d0cd58c archive is published.
- Both ZIPs, release checksums and Foresight-0.3.88-testing-notes.txt are in
  C:\Users\buu42\Downloads.
- ModConfig SHA256: 10998359B9136D515A1F22D7B01E5D4880DBDBF5A63DC22B3229596F4FB03B2A.
- Deployment backup:
  D:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20261031-033305-680-12995393.
- Bootstrap/proxy are unchanged (DEC62345.../8952FE31...). No crash collector
  is packaged. The pre-existing local collector is preserved.

The canonical checkout is integrated and pushed. Its only untracked files are
the user's .claude/ and docs/claude-code-cli-setup.md; both original hashes are
unchanged. The task worktree .worktrees/volcano-names-narration-0388 stays clean
at the immutable payload commit for evidence. Closing documentation is a
separate source commit and does not change the tag, packages or installed DLLs.

## Native-backed changes

The supplied Volcano log identifies 0.3.86. The 0.3.87 passage repair is retained,
with the exact scene608 positions681/1785 now covered by direct/manual, auto-walk
and Story Events tests at native walking16 and running32. No gait override is
introduced. All six nearby native tile-chest records, including collected
states, are checked. Actual script controls, switches and chest Confirm remain
native; no randomized room ahead is previewed.

Bonus fields512..668 use the current native map-name table rather than the
debug bank that ends at495. Scene/table/count/record/owner/text are rechecked.
MapMsgDataTable record+4 is one-based map.txt bank0x22; named MSG_FIELD_* keys
and current saved-name substitutions are retained. Row0/255 or unreadable data
does not fabricate a name. Reused names retain the native discovery flag at
owner+688C, including bonus634's Truce Canyon alias. The native unknown row is
spoken as "Unidentified area" without revealing its identity. Spanish ¿...?
and the supported question/ellipsis punctuation are covered. Existing base
room-label policy is unchanged. Exactly30 fallback Name fields lose soft line
breaks; no other fields or any of the669 scene connections change. Destination
labels still use the existing English catalog.

Native monster text includes verified ShadeFC..FE and bonus104..130 when its
380-row bank supports them. Placeholder indices are excluded by native index,
not an English text filter. Candidate slots are sampled twice under the current
targeting gate; stale names alone never become a roster. Unique targets speak
their names without letters. Actual duplicates use fixed slotA..H identifiers,
retained for the same(slot,name) in battle and cleared at ownership/lifetime
boundaries. Party HP/MP feedback stays intact. Repeated Attack-name popups are
suppressed only after that exact owner/focus/label was already published in the
foreground. The hook-before-HUD and immediate Confirm regression initially
lost speech, was reproduced failing-first, and now passes.

See docs/native-names-0388.md and the corrected battle native-audit docs for
addresses and contracts. Supported EXE/resources hashes freshly match the
retained Ghidra corpus (8FE9D75E.../09914BF4...). Primary CTViewer evidence and
official Fish API documentation were also consulted.

## Narration and review

Eight cues in native005 (Flight of the Dactyls) and nineteen in008 (What Comes
to Pass) add27 descriptions in the previously approved Fish voice. Key/model
identifiers remain private. Original game films are not shipped. Exact asset
hashes, PCM format, native scheduling addresses and scripts are in
docs/movie-narration-0388.md/.json. The existing opening and177 other recordings
are unchanged. Two embedded WAV masters and only their two movie-manifest
entries are added; all older cues/metadata/movie entries are retained.

Descript was tried with the selected footage, but its rendered footage was
unavailable or black. Its answers are not visual verification. Sequential
timestamped local frames establish each description. Final review removed an
unproven rider count, unsupported eyes/smile and mistaken doorway-person claims,
corrected cue starts, and made "The swordsman" explicit after the armored
watcher. No sword handover or hidden identity is inferred. All27 clips pass
ASR(minimum similarity0.962), format, non-silence, clipping, cue windows and
sample-exact placement. These checks do not establish audible voice quality.

All Claude research, implementation and independent whole-branch/follow-up
reviews used verified actual claude-opus-5-5 with max reasoning. No remaining
Critical/Important finding was reported. The final Spanish unknown-punctuation
minor was fixed with failing-first tests after review. The review's reference
to81d818c containing the final narration wording is inaccurate: those edits were
then an uncommitted delta and are included in the immutable b4b4d5d payload.
Private review receipts and model verification are retained under research.

## Verification and preservation

- Full Release build: zero warnings/errors;5,788 tests pass: Core245,
  Native1566, Mod3967, Prism10.
- 1,037 checks load only the exact four packaged production DLLs under private
  32-bit .NET. SHA256 and informational versions bind them to b4b4d5d.
- 84 original-x86 terrain traces pass at unchanged walking/running speeds,
  both navigation modes, Story and chest states. Core/Mod hashes match the
  immutable package. Actors, ATEL, battles, Confirm and room transfers are
  not executed by these replays.
- Nine silent native movie checks pass. Five use the exact packaged DLLs and
  embedded masters with native CPlayer/event processing, completion,
  pause/resume and early closure. Four original/narration stream checks verify
  full playback/skip and sample continuity. Artificial MF failure events test
  failure handling and are not natural movie-playback failures. Probe windows
  were private, hidden and muted before playback.
- Four release-package tests, six AMM manifest tests and303 fresh deployment
  checks pass. All275 installed standalone files and every wrapped byte match;
  the wrapper contains277 files. The game remained closed throughout deployment.
- Original244-file protection audit is retained privately. Exactly243 files
  remain byte-for-byte unchanged, including executable/resources, all recorded
  saves, settings, other mods and177 previous recordings. The remaining entry
  is the authorized maintained SOURCE voice manifest: only005/008 metadata was
  added. Its before/after hashes and structural comparison are retained in
  authorized-source-manifest-update.json; no prior baseline hash was refreshed.
- Both user-owned source files remain unchanged. All prior Chrono releases and
  unrelated games/releases in the catalog are preserved. The three public
  assets, immutable tag and anonymous public index were independently checked.

Research: artifacts/research/volcano-names-20261031.
Publication/receipts: artifacts/release-0.3.88-public-20261031.
The latter contains publication-complete.json and final closure-verification.json.

## Tester coverage and outstanding limits

Revisit the bookmarked Volcano room: passage, Story Events and nearby chests in
both guidance modes, walking and running. In Lost Sanctum and other bonus areas,
check native/localized location announcements and unique/duplicate enemy names,
including damage feedback and fast Confirm. Play unlocked005/008 gallery entries
through completion and native skip, retaining original music/effects.

Live gameplay, physical-controller input, audible narration/screen-reader speech
and recipient-PC installation were not performed. No Steam/game launch or
player input occurred. Gallery opening001 narration remains unverified; its
existing Fish timeline is preserved. This adds two verified films, not complete
description coverage of all dialogue and silent actions. The separate native
map-texture-creation crash remains unresolved and is not claimed fixed here.
