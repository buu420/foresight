# Field menu and intro trace 0.2.7

The player reached first control of Crono with 0.2.6. The next in-game menu
opening produced an unmarked-label error. Version 0.2.7 adds the six audited
menu label branches described in
`../native-audits/2026-09-10-field-menu-footer.md`.

The player requested brief automatic descriptions between opening dialogue. The
visual script is drafted in `../native-audits/2026-09-10-intro-description-research.md`.
Those descriptions are not enabled in 0.2.7: exact executed cue addresses have
not yet been established. This build collects the needed gameplay evidence.

## Recorder contract

- One new native hook: field opcode dispatcher RVA `0x1619E0`, ECX context and
  one callee-cleaned opcode argument. Ghidra established the dispatcher and its
  callers at `0x160E88`, `0x160F98`, `0x161318`, and `0x16160D`. Their immediate
  continuations do not consume this call's EAX as a return value.
- Arm only from the existing validated Start Game activation (native focus key
  30), before its original callback. Returning to startup, opening a menu, or
  disabling hooks stops capture. No input is generated.
- Read the candidate executing address `context+0x24` before the original call;
  the dispatcher may change it. Log `pcMatch` by comparing its first byte with
  the dispatched opcode, since not every caller has been proven to refresh this
  address first. A mismatch cannot establish a cue address; a match is still not
  proof of scene identity. Record the control field before and after the original.
  Do not interpret any control edge as first player control.
- Script-byte access follows the verified native expression:
  `*(uint*)context + 0x12001 + executingAddress`. The eight-byte header is a
  diagnostic fingerprint, not proof of a current location. The complete 5,586-byte
  Atel_0000 candidate is hashed only after its known header matches. An absent or
  mismatching hash establishes no room identity.
- Keep dialogue boundaries in the same log stream. Coalesce identical complete
  observations; retain control changes. At most 32,768 opcode records, 250,000
  dispatches, and 16 script-header identities are captured per New Game attempt.
- All reads are bounded to x86 ranges and use the existing safe memory reader.
  A transient read failure skips that observation and logs a capture gap; a
  successful subsequent capture reports the number skipped. Eight consecutive
  failures stop only the diagnostic recorder. A different dispatch thread is
  reported once with both thread IDs, without reading its memory.
- Logging runs outside the recorder lock; inactive dispatches bypass the lock
  and memory reads entirely. Logging failures cannot stop native execution or
  speech. The original runs once, including after a capture failure; a failed
  native call is not retried.

`Intro trace:` records go to the normal Reloaded log. No game assets, external
service, recording application, additional narration runtime, or account credits
are required. No recording is uploaded.

## Validation and player replay

The native capture and recorder tests exercise the pre/post boundary, stale owner,
unreadable memory and recovery, x86 overflow, disabled capture, repeated dispatches,
both caps, dialogue boundaries, logging failures, wrong threads, opcode/address
mismatches, logging outside the lock, and original-call preservation. The
120 installed-executable hook signatures are checked by
`verify_v027_contracts.py` in the local research directory.

After deployment, the player should launch from Steam, select New Game, proceed
through the opening to first control, and open the in-game menu. That pass tests
the menu correction and supplies the script trace. Automatic descriptions and
the in-game result must not be claimed as verified before this replay.

## Claude review

The independent read-only review is `v027-review-claude.md` in the research
directory. The recorder now handles its five substantive concerns: transient
capture gaps, the too-small record budget, unreported thread mismatches,
unverified opcode/address correspondence, and logging under the recorder lock.
Four new regression tests reproduced failures before the corrections and passed
afterward. The record budget was increased eightfold while keeping fixed bounds;
this does not guarantee every possible replay fits within them.

The preparation comments document the existing inactive-hook factory contract.
The installer requires one preparation, rejects hooks already active during
preparation, and activates only after the original delegate is bound. Retrying
the same prepared registration or calling directly into a patched entry point
is not an appropriate fallback. The identity limit is now checked before adding
a seventeenth entry. Hashing is header-gated; recording itself is not.

## Installed result

On 2026-09-10 the final Release solution run passed **794 tests**: Core 56,
Prism 8, Native 437, and Mod 293, with no failures or skipped tests. The four
TRX reports use the `v027-reviewed-release` prefix in the research directory.
All **120** hook byte contracts matched the installed executable.

`Package-Mod.ps1 -SkipBuild` packaged that Release output. With the game and
launcher closed, `Deploy-Mod.ps1 -SkipNativeBuild -SkipIfeo` installed it into
the existing portable Reloaded tree. The existing launcher redirect was already
registered and passed verification; no installer or game was launched.

- Deployment verification: **25 passed, 0 failed**.
- All **27** package checksum entries match both the package and installed files.
- All four built accessibility DLLs match their installed copies.
- Installed manifest version: **0.2.7**.
- Mod DLL SHA-256: `8805D270A0B6B5121AC3CD7B571DE7E9AED96D4B86A7A77ED0C0103B19B0F96E`.
- Native DLL SHA-256: `1C4F02B8CEB43ED538AA4E78C158817A69D3904602E9BE2BF126B4B72F116F7A`.
- Manifest SHA-256: `23F0C25530B04BF1B755E5193C3F73BEA3A41EB01819A9340D0D69075D869F4E`.
- The game executable remains unchanged at the supported SHA-256.

The independent installed-file record is `v027-installed-verification.json` in
the research directory. The previous installation is preserved at
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260910-131227`.
The player replay remains pending; automatic intro descriptions remain disabled.
