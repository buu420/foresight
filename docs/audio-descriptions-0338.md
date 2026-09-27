# Own-voice narration delivery and volume

The user approved preview D, `D-smoother-own-voice.wav`, after comparing it with the FF7 audio description in Downloads. The chosen delivery keeps the user's voice, joins related actions into flowing sentences, shortens excessive pauses, and gives statements a finished ending. The FF7 recording was analyzed as a delivery reference; its narrator was not a cloning input. Acoustic measurements and transcription are not a human listening review.

The source is the user's `Recording (3).m4a`, using the `rec3-natural` reference (SHA-256 `66e41e7efeb37965426f45b54898007b85d44c1cb4ff5d67927f1d2b62191a6c`). Qwen3-TTS runs locally with the speaker-embedding-only mode and model-default sampling. This mode keeps the speaker embedding without using the conversational reference as a speech continuation. Its behavior is documented in the [official Qwen implementation](https://github.com/QwenLM/Qwen3-TTS/blob/main/qwen_tts/inference/qwen3_tts_model.py). Nothing is uploaded to Fish Audio or another voice service.

The three approved performances are retained exactly before volume mastering. Remaining descriptions are generated as complete utterances, avoiding cuts between generated sentences. The final renderer checks the words, speaker similarity, ending contour, and available scene duration. Any accepted ASR spelling difference remains visible in the original checks and has an approval tied to the exact waveform hash. The ending check is a screening heuristic, not a claim that every line has been heard by a person.

Pause refinement removes only low-energy, unvoiced material between recognized words. It does not change the playback speed or pitch of spoken words. Any leading or trailing trim is outside the recognized words, checked frame by frame for energy and voicing, and recorded. A separate audit reconstructs each output from its source and documented removals. The original reviewed texts and cue timestamps remain the source of truth.

## Volume and original soundtrack

The builder balances the narration cues, then applies two-pass loudness mastering to the narration track alone. The default target is -14 LUFS with a -2 dBTP peak ceiling; each movie report records its actual result. The soundtrack has no ducking or gain change. Original AAC and H.264 packets, timestamps, and the AAC edit list are preserved as in [the separate-track implementation](audio-descriptions-0337.md).

The runtime remains compatible with mod 0.3.37 and its version-2 movie manifest. No new native hook or game executable patch is needed for this asset update.

## Installed result, 2026-09-27

All 40 descriptions passed the final text, scene-window, voice-source, ending-screen and waveform-reconstruction audit. The replacement pack was installed with the game closed, and all three installed DAT hashes match the verified pack. The exact approved preview performances remain in cues ct-001-012, ct-002-003 and ct-003-005.

| Movie | Cues | Previous narration LUFS | New narration LUFS | Increase | Encoded true peak dBTP |
| --- | ---: | ---: | ---: | ---: | ---: |
| 001 | 22 | -21.34 | -15.14 | 6.20 dB | -1.83 |
| 002 | 8 | -21.07 | -14.83 | 6.24 dB | -1.86 |
| 003 | 10 | -21.83 | -15.05 | 6.78 dB | -1.94 |

These are measurements of the final encoded narration tracks. All are below clipping. Original audio/video packets and timestamps are unchanged, and the narration waveform remains silent outside the reviewed cue windows. A native Media Foundation source probe confirms mono narration, the original stereo soundtrack, and video in the new movie. All 18 relevant Python tests pass; this asset update does not change C# or native hooks. The new pack has not been tested through live game playback or given a full human listening review.

The detailed local evidence is `final-flowing-narration-audit.json`, `pack-verification.json`, and `deployment-verification.json` under the 0338 artifact directory.

## Local reproduction and recovery

Private recordings, generated narration and game movies are not included in the public mod package. The local artifacts are under `artifacts/research/audio-description-0338`:

- `ff7-style/flowing-preview-selection.json`: the approved audition, source take, pause edits and final ASR check.
- `ff7-style/render_pack.py`: generation and refinement of the full set, with checkpoints.
- `narration-flowing`: final per-cue audio, checks and hash-bound ASR reviews.
- `verify_flowing_narration.py`: text, timing, voice-source and waveform reconstruction audit.
- `pack`: separate-track movie output and packet/timing verification reports.

Use the existing local voice Python runtime for these commands. Build and verify the replacement before restoring or installing any movie files:

```powershell
python tools/Build-DescribedMovies.py build --script artifacts/research/audio-description-0337/pack/reviewed-script.json --sources artifacts/research/audio-description-0336/movies --voices artifacts/research/audio-description-0338/narration-flowing --approvals artifacts/research/audio-description-0338/narration-flowing/reviewed-asr-approvals.json --output artifacts/research/audio-description-0338/pack
python artifacts/research/audio-description-0338/verify_pack.py --pack artifacts/research/audio-description-0338/pack
python tools/Build-DescribedMovies.py restore --game ..
python tools/Build-DescribedMovies.py install --pack artifacts/research/audio-description-0338/pack --game ..
python artifacts/research/audio-description-0338/verify_pack.py --pack artifacts/research/audio-description-0338/pack --game ..
```

Restore and install require the game to be closed. Originals and previous narrated files remain in `Accessibility/AudioDescriptions/backups`; unknown edits are never overwritten.

Coverage remains the 40 reviewed descriptions in movies 001-003. Further movies and NPC-action descriptions retain the pending coverage described in [the original report](audio-descriptions-0336.md#coverage-still-pending).
