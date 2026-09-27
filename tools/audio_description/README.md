# Audio-description voice tools (local only)

These tools render audio-description text in the user's own cloned voice. The text comes from
ViddyScribe as scripts only; its preset voices are not used. Everything runs on this PC:
- the Qwen3-TTS model, the faster-whisper models and the reference recordings are read from
  local caches with the Hugging Face offline flags set;
- nothing is uploaded;
- the mod runtime is not touched.

All commands use the existing voice runtime:

    set VPY=C:\Users\buu42\.local\share\blind-soldier-voice\venv\Scripts\python.exe

| tool | purpose |
| --- | --- |
| `prepare_reference.py analyze` | Inventories the recordings in `OneDrive\Documents\Sound Recordings` (sha256, format, levels, noise floor, clipping, pitch). It transcribes each with faster-whisper medium.en (word times) and small.en (cross-check), writing `recordings-analysis.json`. |
| `prepare_reference.py build --plan plan.json --out dir` | Cuts a reference from one recording and writes `reference.wav`, `reference.txt` and `reference-provenance.json`, then re-transcribes the result. See the edits below. |
| `render_cues.py` | Renders cues in the cloned voice with the approved sampling, checks each take, retakes on failure and writes per-cue WAV and JSON plus a manifest. |
| `summarize_evaluation.py` | Compares profiles rendered on the same cues. |

`build` edits only:
- a source span;
- pauses longer than 0.4 s shortened to 0.3 s, with speech found from measured energy;
- a 60 Hz high-pass, a mono downmix and 24 kHz resampling;
- one fixed gain.

It never applies time stretching, pitch changes, denoising, compression, or anyone else's audio.

## Rendering

    %VPY% tools\audio_description\render_cues.py --script <ViddyScribe result or cue JSON/SRT/VTT> ^
        --profile artifacts\research\audio-description-0336\voice\profile.json ^
        --out <dir> --id-prefix movie-001- --asr-glossary "Crono, Lucca, Marle, Frog, Robo, Ayla, Magus, Guardia, Lavos"

Script inputs:
- a ViddyScribe job result (`output.segments[].description`, with `start_time`/`end_time`);
- `{"cues": [...]}` or a bare list;
- Descript-style `{"films": [...]}`;
- `.srt` or `.vtt`.

Each segment becomes one cue. The cue's window is its `end - start`.

Per cue the renderer writes:
- **`<id>.wav`**: mono, 24 kHz, PCM_24. By default this is the model's natural take, with no
  stretching, pitch change, cropping or compression. Explicit gain and ending-edit options
  record their changes in the metadata and retain the raw candidate.
- **`<id>.json`**: seed, fingerprint, model, reference hashes, every take tried, and the checks.
- **`candidates/<id>.takeN.wav`**: every take.
- **`manifest.json`** and **`check-report.json`**.

Checks:
- faster-whisper transcript against the text (normalised word differences);
- audible span, lead and tail silence, peak;
- words and syllables per audible second;
- two-second syllable rates;
- pitch median and span;
- speaker-encoder cosine to the reference and to the identity anchors;
- window fit.

When ASR runs, each take's checks also hold `asr_words`: the word timings from that same
transcription (`word`, `start`, `end`, in seconds, rounded to 1 ms). They are there for later pause
or paragraph work, so it doesn't need a second transcription.

A take that fails a check is retried with the next text-derived seed (`--retakes`, default 2).
The best take is promoted and failures are reported (exit code 2). A human decides whether to
reword a cue.

Other options:
- `--sampling model-default` uses the installed Qwen snapshot's sampling settings for the
  talker and acoustic sub-talker. It leaves the voice identity and speaking speed alone.
  `--temperature`, `--top-p`, `--top-k` and their `--subtalker-...` counterparts can override
  individual settings. Actual parameters are included in the render fingerprint and metadata.
- `--settled-endings` retries a take when the last word has a late upward turn. This optional
  check was calibrated against this user's recordings for declarative movie narration;
  it is a listening aid, not proof that a delivery sounds natural. It leaves rises at commas
  alone. `--retakes` allows up to 16 extra seeds and stops when a take passes all checks.
- `--ending-fallback none` keeps all narration as generated (the setting used for the revised
  pack). The optional `edit` setting uses a documented pitch edit on the final word only if
  every usable take still has the upturn. Such output sets `pitch_manipulation: true`, retains
  the original take, and must pass the word and timing checks again. It is off by default.
- `--target-speech-dbfs -18` applies one fixed gain per take so its speech RMS reaches that level,
  never raising the peak above 0.95. It is off by default, which keeps the model's raw level (own-voice
  takes come out around −24 to −27 dBFS speech). The gain is recorded in `<id>.json`.
- `--x-vector-only` builds the voice prompt from the reference's speaker embedding alone
  (`create_voice_clone_prompt(..., x_vector_only_mode=True)`), so the reference recording's own
  pacing and pauses are not continued. It is off by default, which keeps in-context (ICL) cloning.
  The mode is recorded as `x_vector_only_mode` in `<id>.json` and in the dry-run plan. Turning it
  on changes the render fingerprint, so ICL checkpoints are never reused for it; default runs keep
  their existing fingerprints and checkpoints.
- `--rate-band 3.5 5.3` makes cadence a failure condition as well.
- `--repeat-check` renders the promoted take again with the same seed and records the largest
  sample difference.
- `--dry-run` validates the script and profile and runs the GPU preflight without loading a model.
- The preflight refuses to start when less than 6 GiB of GPU memory is free, the GPU is over 60%
  busy, or a known game executable is running. Override with `--ignore-gpu-contention`.

Proper names are primed for recognition with `--asr-glossary`. When a name is misheard although
spoken correctly, it shows up as a word change to review, not as a silent pass.

Pronunciation-only fixes go in a render plan: `{"<cue id>": {"render_text": "...", "reason": "..."}}`.
The checked text stays the displayed text.

## Movie narration volume

`tools/Build-DescribedMovies.py build` now masters the narration separately after placing
the checked cues. `--narration-lufs -14` sets the loudness target and
`--narration-peak-dbtp -2` sets the true-peak ceiling. Actual measured levels are recorded in
each movie's verification JSON; a target does not guarantee an exact measured output.

The original soundtrack and video remain packet copies. Narration mastering never ducks
or changes the soundtrack, changes cue timing, or changes the speaker's pitch or speed.
The voice is kept silent outside its original cue windows. Changing source WAV gain alone
is insufficient because the movie builder balances the input cues before mastering them.
