# Audio descriptions, 0.3.36

This is the historical first-pack report. Version [0.3.37](audio-descriptions-0337.md) replaces its ducked premixes with separate simultaneous narration and unchanged original soundtrack tracks, as requested by the user. The scripts and voice takes remain the same.

The first movie pack uses the user's own voice for 40 short descriptions across three game movies. ViddyScribe produced text only. Narration was generated locally using the authorised OneDrive recordings; no voice recording was uploaded.

| Movie | Coverage | ViddyScribe job |
| --- | --- | --- |
| 001, 158.358208 s | Animated opening montage, 22 cues | 824b161d-ddd8-4805-af2a-e167966d85f4 |
| 002, 39.105708 s | Ayla fights the reptiles, 8 cues | a747c7e1-63b0-4e76-ae64-e1b9bd0d834d |
| 003, 65.498708 s | Frog opens a passage through the mountain, 10 cues | 00a87e06-0463-4316-b8fe-42b945c106a1 |

The reviewed script is [movies-0336.json](audio-descriptions/movies-0336.json). Each movie includes a short ViddyScribe credit. The soundtrack remains present, lowered as needed under each line with short fades and a 12 dB RMS narration margin. A fixed 9 dB reduction left some words masked by loud effects and was rejected after checking the mixed audio. Voice speed and pitch are unchanged. The first opening cue starts one second into the movie, leaving room for the reader's existing "Opening movie" announcement. The independent screen-reader fallback still starts with the first frame.

## Playback and recovery

Descriptions are mixed into copies of the actual native movie audio. The video packets, frame count and timing are retained. The game's movie player owns playback, so there is no second audio timer to continue after a movie is skipped. This applies to story playback and Extras without changing story flags, unlocks or save files.

The mod suppresses its opening screen-reader timeline only when `Accessibility/AudioDescriptions/installed-movies.json` identifies the matching narrated `001.dat` hash. If the pack is absent, damaged, or replaced by Steam file verification, the corrected screen-reader fallback remains available. Manifest errors and a cancelled verification cannot release stale speech. The other two movies use their own native audio and do not have a separate fallback timeline.

The movie installer verifies every original and every pack file before replacement, copies and verifies originals in `Accessibility/AudioDescriptions/backups`, and restores changed files if installation fails. An existing pack must be restored before another is installed. Restoration accepts originals already restored by Steam, but refuses to overwrite an unknown edited movie. The executable is never changed. Fallback cue timing includes time spent verifying the movie; a slow read resumes at the current shot instead of speaking expired cues.

With the game closed:

```powershell
py -3 tools/Build-DescribedMovies.py install --pack artifacts/research/audio-description-0336/pack --game ..
py -3 tools/Build-DescribedMovies.py restore --game ..
```

The standard mod package does not redistribute game movies or private voice recordings. They remain local. Use the already prepared local pack for this installation. The normal mod deployment leaves `Accessibility/AudioDescriptions` intact.

## Source and native verification

The supported executable remains SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

Ghidra's `libcocos2d.dll` function at RVA `0x287FAA`, `VideoPlayer::setFileName`, decodes each movie byte with a decrementing 8-bit XOR key starting at `0xFF`. The transform is symmetric. The builder performs a full encoded/decoded hash roundtrip.

The opening scene is scene `0x1E`, constructed at game RVA `0x2AE8B0`. Its player at `0x2AE9B0` reads the string at VA `0x81F9B8`. A direct disassembly of the otherwise missing static initializer at RVA `0xBC50` confirms that it assigns the seven-byte literal `001.dat` from VA `0x7A524C` to that string. This connects the fallback suppression to the correct movie. The general movie scene uses the same native video player.

Generated text was checked against extracted frames. Corrections include a red spiked shape incorrectly called Earth, Frog incorrectly called Magus, and Crono's foreground foot incorrectly described as a pursuer. Output duration and cue windows are checked separately from ViddyScribe's estimated speaking time.

All 40 raw cues fit their final windows. The final encoded movie audio was transcribed separately: 37 cues matched exactly; the other three differed only in Chrono/Crono, stares/stairs, or broad sword/broadsword spelling. Every video packet's contents, PTS, DTS and duration match the source. MP4 container duration summaries differ by at most 0.000059 seconds because of remux rounding. Installation rollback, changed-source rejection, changed-pack rejection, Steam-restored originals and guarded restoration have seven Python regression checks. The C# startup and manifest checks cover matching/missing/replaced movies, malformed JSON types, cancellation while verification is pending, and a simulated slow hash read.

Claude Opus 5.5 prepared a fresh voice reference from `Recording (3).m4a`: a 16.6-second selection, shortened pauses, a high-pass filter, mono conversion, and level adjustment. This reference contains only the user's real recording. The local Qwen3-TTS and faster-whisper models generate and check every line. Word mismatches are retained for explicit review; a homophone spelling mismatch is not a listening test. There has been no human listening approval or live in-game playback test of this pack yet.

## Coverage still pending

ViddyScribe completed one additional text script for the 268-second New Game gameplay reference (job `0629d7d4-8455-42e5-9ab4-aae7cad300c2`). It includes NPC actions, but several timestamps precede the visible actions and a claimed purse pickup is unsupported. It has not been installed as a timed script. Player-paced dialogue needs native event bindings and coordination with the screen reader.

The bedroom is scene 2, script **324**, not script 0. The current resource mapping and `Atel_0324.dat` give these investigation anchors (file offsets; runtime script PC is offset minus one):

| Action | Evidence to verify in a live opening trace |
| --- | --- |
| Mother opens curtains | Actor 10, `E5` tile copies at `0x7C5` / `0x7CD`, alternate path `0x7DF`; closing curtains is a different branch |
| Crono rises / stretches | Actor 1, function 5, `AB 21` at `0x42C`, `AA 20` at `0x434` |
| First player control | Actor 1, same function, `E3 01` at `0x443`, after the opening-specific story transition |

These are research anchors, not enabled descriptions. Broad animation-opcode narration would confuse repeated poses, named-party substitutions and unrelated events. It must not be treated as whole-game NPC coverage.

Movies 004, 005, 006, 007-en and 008 are extracted and inventoried but have no ViddyScribe script. Further submissions stopped when upload for 004 returned `429 plan_limit_exceeded` for transcription minutes: limit 50, used 29.16, reserved 20, remaining 0.84, requested 5. This is an account allowance limit, not a retryable network failure. No extra credits were purchased and no preset narration voice was requested.

The local research directory retains the original exports, stable request IDs, frame reviews, quota error, voice provenance, per-cue checks, and movie verification reports for continuation without duplicate paid jobs.
