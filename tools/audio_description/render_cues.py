"""Render audio-description cues locally in the user's own cloned voice (nothing is uploaded).

Model: Qwen3-TTS 12Hz 1.7B Base (local snapshot fd4b2543...), in-context voice cloning from a
reference recording and its exact transcript. Sampling is the approved FFVII natural-cadence v5 /
Brice all-narration / Borderlands intro setting: temperature 0.68, top_p 0.9, repetition penalty
1.05, non-streaming, bfloat16, sdpa attention, one text per call.

Script input (--script) is recognised by content:
  * ViddyScribe job result: {"output": {"segments": [{"id", "start_time", "end_time", "description"}]}}
  * cue list: {"cues": [{"id", "text", "start"?, "end"?}]} or a bare JSON list of those
  * films (Descript style): {"films": [{"movie", "cues": [{"id", "start", "end", "text"}]}]}
  * SubRip (.srt) or WebVTT (.vtt): each block is one cue
Voice profile (--profile): JSON with reference_wav and reference_text (see prepare_reference.py).

Per cue, in --out: <id>.wav (mono 24 kHz PCM_24) and <id>.json (text/render hashes, seed,
fingerprint, reference hashes, model, parameters, checks). By default the waveform is the
model's unmodified take. Optional gain and final-word pitch editing are explicitly recorded;
there is no time stretching or cropping. The run writes manifest.json and check-report.json.

Checks (--asr): the take's transcript against the cue text (normalised word error rate and the
differing words), audible span (2 % of peak), lead/tail silence, peak, words and syllables per
audible second, the fastest/slowest two-second syllable rate from ASR word times, pitch median and
p10-p90 span, and speaker-encoder cosine to the reference and to the profile's identity anchors.
A take that fails (words differ, over its window, or outside --rate-band) is retried with the next
seed (--retakes); every take is kept under candidates/ and the best one is promoted. Nothing is
ever altered to make a take fit. Exit code 2 when a cue still fails, 1 on input or setup errors.

    %VPY% tools\\audio_description\\render_cues.py --script cues.json --profile <voice>\\profile.json --out <dir>
    (%VPY% = C:\\Users\\buu42\\.local\\share\\blind-soldier-voice\\venv\\Scripts\\python.exe)
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

REPO = Path(__file__).resolve().parents[2]
VOICE_ROOT = Path(os.environ.get("BLIND_SOLDIER_VOICE", r"C:\Users\buu42\.local\share\blind-soldier-voice"))
MODEL_SNAPSHOT = (VOICE_ROOT / "hf/hub/models--Qwen--Qwen3-TTS-12Hz-1.7B-Base/snapshots"
                  / "fd4b254389122332181a7c3db7f27e918eec64e3")
DEFAULT_PROFILE = REPO / "artifacts/research/audio-description-0336/voice/profile.json"
PARAMS: dict[str, Any] = {
    "language": "English",
    "non_streaming_mode": True,
    "max_new_tokens": 600,
    "temperature": 0.68,
    "top_p": 0.9,
    "repetition_penalty": 1.05,
}
SAMPLE_RATE = 24000
SUBTYPE = "PCM_24"
SCRIPT_VERSION = "ct-audio-description-voice-1"
ID_RE = re.compile(r"^[a-z0-9][a-z0-9_-]{0,63}$")
RETAKE_STRIDE = 104729


class InputError(ValueError):
    pass


# ----------------------------------------------------------------------------- inputs

def sha_bytes(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def sha_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def clean(text: Any) -> str:
    if not isinstance(text, str) or not text.strip():
        raise InputError(f"cue text must be a non-empty string, got {text!r}")
    return " ".join(text.split())


def safe_id(raw: Any, prefix: str, index: int) -> str:
    base = str(raw).strip().lower() if raw is not None else str(index + 1)
    if base.isdigit():
        base = f"{int(base):03d}"
    cue_id = re.sub(r"[^a-z0-9_-]+", "-", prefix + base).strip("-")
    if not ID_RE.match(cue_id):
        raise InputError(f"cannot make a safe id from {raw!r} with prefix {prefix!r}")
    return cue_id


def seconds(value: Any, what: str) -> float | None:
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise InputError(f"{what} must be a number, got {value!r}")
    return float(value)


def timestamp(text: str) -> float:
    parts = text.strip().replace(",", ".").split(":")
    value = 0.0
    for part in parts:
        value = value * 60 + float(part)
    return value


def load_subtitles(text: str, prefix: str) -> list[dict[str, Any]]:
    blocks = re.split(r"\n\s*\n", text.replace("\r\n", "\n").strip())
    cues = []
    for block in blocks:
        lines = [line for line in block.split("\n") if line.strip()]
        timing = next((i for i, line in enumerate(lines) if "-->" in line), None)
        if timing is None:
            continue
        start, end = (timestamp(side.split()[0]) for side in lines[timing].split("-->"))
        words = " ".join(re.sub(r"<[^>]+>", "", line) for line in lines[timing + 1:])
        label = lines[timing - 1] if timing > 0 else None
        cues.append({"id": safe_id(label if label and label.strip().isdigit() else None, prefix, len(cues)),
                     "text": clean(words), "start": start, "end": end})
    return cues


def load_script(path: Path, prefix: str) -> list[dict[str, Any]]:
    try:
        raw = path.read_text(encoding="utf-8-sig")
    except OSError as exc:
        raise InputError(f"cannot read {path}: {exc}") from exc
    if path.suffix.lower() in (".srt", ".vtt"):
        cues = load_subtitles(raw, prefix)
    else:
        try:
            document = json.loads(raw)
        except ValueError as exc:
            raise InputError(f"{path} is neither JSON nor .srt/.vtt: {exc}") from exc
        cues = []
        if isinstance(document, dict) and isinstance(document.get("output"), dict) \
                and isinstance(document["output"].get("segments"), list):
            for index, segment in enumerate(document["output"]["segments"]):
                cues.append({"id": safe_id(segment.get("id"), prefix, index), "text": clean(segment.get("description")),
                             "start": seconds(segment.get("start_time"), "start_time"),
                             "end": seconds(segment.get("end_time"), "end_time")})
        elif isinstance(document, dict) and isinstance(document.get("films"), list):
            for film in document["films"]:
                for index, cue in enumerate(film.get("cues", [])):
                    cues.append({"id": safe_id(cue.get("id"), prefix, index), "text": clean(cue.get("text")),
                                 "start": seconds(cue.get("start"), "start"), "end": seconds(cue.get("end"), "end"),
                                 "movie": film.get("movie")})
        elif isinstance(document, (dict, list)):
            items = document.get("cues") if isinstance(document, dict) else document
            if not isinstance(items, list):
                raise InputError("JSON script needs output.segments, films or cues")
            for index, cue in enumerate(items):
                if not isinstance(cue, dict):
                    raise InputError(f"cue {index} must be an object")
                cues.append({"id": safe_id(cue.get("id"), prefix, index), "text": clean(cue.get("text")),
                             "start": seconds(cue.get("start"), "start"), "end": seconds(cue.get("end"), "end")})
        else:
            raise InputError("unrecognised script")
    if not cues:
        raise InputError(f"{path} has no cues")
    seen: set[str] = set()
    for cue in cues:
        if cue["id"] in seen:
            raise InputError(f"duplicate cue id {cue['id']}")
        seen.add(cue["id"])
        if cue.get("start") is not None and cue.get("end") is not None and cue["end"] <= cue["start"]:
            raise InputError(f"{cue['id']}: end {cue['end']} is not after start {cue['start']}")
        cue["window_seconds"] = (cue["end"] - cue["start"]) if cue.get("start") is not None and cue.get("end") is not None else None
        cue["text_sha256"] = sha_text(cue["text"])
    return cues


def load_profile(path: Path) -> dict[str, Any]:
    profile = json.loads(path.read_text(encoding="utf-8"))
    base = path.parent
    for key in ("reference_wav", "reference_text"):
        if not profile.get(key):
            raise InputError(f"profile {path} lacks {key}")
    wav = Path(profile["reference_wav"])
    profile["reference_wav"] = str(wav if wav.is_absolute() else (base / wav).resolve())
    if not Path(profile["reference_wav"]).is_file():
        raise InputError(f"reference WAV missing: {profile['reference_wav']}")
    actual = sha_bytes(Path(profile["reference_wav"]))
    if profile.get("reference_sha256") and profile["reference_sha256"] != actual:
        raise InputError(f"reference WAV changed: {actual} != profile {profile['reference_sha256']}")
    profile["reference_sha256"] = actual
    profile["reference_text"] = " ".join(profile["reference_text"].split())
    anchors = []
    for anchor in profile.get("identity_anchors", []):
        anchor_path = Path(anchor)
        anchors.append(str(anchor_path if anchor_path.is_absolute() else (base / anchor_path).resolve()))
    profile["identity_anchors"] = anchors
    return profile


def fingerprint(profile: dict[str, Any], x_vector_only: bool = False) -> str:
    """Render identity. The default (ICL) keeps the same hash as before --x-vector-only existed."""
    return sha_text(json.dumps({
        "model": MODEL_SNAPSHOT.name, "reference_sha256": profile["reference_sha256"],
        "reference_text_sha256": sha_text(profile["reference_text"]), "params": PARAMS,
        "sample_rate": SAMPLE_RATE, "subtype": SUBTYPE, "script_version": SCRIPT_VERSION,
        "x_vector_only_mode": bool(x_vector_only), "dtype": "bfloat16", "attn": "sdpa"}, sort_keys=True))


def cue_seed(seed_base: int, render_text: str, take: int) -> int:
    return (seed_base + int(sha_text(render_text)[:8], 16) + take * RETAKE_STRIDE) % (2 ** 31 - 1)


# ----------------------------------------------------------------------------- environment

def gpu_preflight(min_free_gib: float, max_utilization: int) -> dict[str, Any]:
    report: dict[str, Any] = {"checked_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    try:
        query = subprocess.run(["nvidia-smi", "--query-gpu=name,memory.total,memory.used,utilization.gpu",
                                "--format=csv,noheader,nounits"], capture_output=True, text=True, check=True).stdout
        name, total, used, utilization = [part.strip() for part in query.strip().splitlines()[0].split(",")]
        report.update(gpu=name, total_gib=round(int(total) / 1024, 2), used_gib=round(int(used) / 1024, 2),
                      free_gib=round((int(total) - int(used)) / 1024, 2), utilization_percent=int(utilization))
    except (OSError, subprocess.CalledProcessError, ValueError, IndexError) as exc:
        report["error"] = f"nvidia-smi unavailable: {exc}"
        report["ok"] = False
        return report
    games = []
    for image in ("Chrono Trigger.exe", "ff7_en.exe", "ff7.exe", "Borderlands.exe"):
        listing = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {image}", "/NH"], capture_output=True, text=True).stdout
        if image.lower() in listing.lower():
            games.append(image)
    report["games_running"] = games
    report["ok"] = report["free_gib"] >= min_free_gib and report["utilization_percent"] <= max_utilization and not games
    report["limits"] = {"min_free_gib": min_free_gib, "max_utilization_percent": max_utilization}
    return report


def cuda_runtime_on_path() -> None:
    """CTranslate2 (faster-whisper) needs cublas64_12.dll; the torch cu128 wheel ships it."""
    import torch
    library = Path(torch.__file__).parent / "lib"
    if library.is_dir():
        os.add_dll_directory(str(library))
        os.environ["PATH"] = str(library) + os.pathsep + os.environ.get("PATH", "")


# ----------------------------------------------------------------------------- measurement

ONES = "zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen " \
       "seventeen eighteen nineteen".split()
TENS = "twenty thirty forty fifty sixty seventy eighty ninety".split()


def number_words(value: int) -> str:
    if value < 20:
        return ONES[value]
    if value < 100:
        return TENS[value // 10 - 2] + ("" if value % 10 == 0 else " " + ONES[value % 10])
    if value < 1000:
        rest = value % 100
        return ONES[value // 100] + " hundred" + ("" if rest == 0 else " " + number_words(rest))
    return str(value)


def normalise(text: str) -> list[str]:
    text = text.lower().replace("-", " ").replace("’", "'")
    text = re.sub(r"\d+", lambda m: number_words(int(m.group())) if len(m.group()) <= 3 else m.group(), text)
    text = re.sub(r"[^a-z0-9' ]+", " ", text)
    return [word.strip("'") for word in text.split() if word.strip("'")]


def word_diff(expected: list[str], heard: list[str]) -> tuple[int, list[str]]:
    rows, cols = len(expected) + 1, len(heard) + 1
    cost = [[0] * cols for _ in range(rows)]
    for i in range(rows):
        cost[i][0] = i
    for j in range(cols):
        cost[0][j] = j
    for i in range(1, rows):
        for j in range(1, cols):
            cost[i][j] = min(cost[i - 1][j] + 1, cost[i][j - 1] + 1,
                             cost[i - 1][j - 1] + (expected[i - 1] != heard[j - 1]))
    changes, i, j = [], len(expected), len(heard)
    while i or j:
        if i and j and cost[i][j] == cost[i - 1][j - 1] + (expected[i - 1] != heard[j - 1]):
            if expected[i - 1] != heard[j - 1]:
                changes.append(f"{expected[i - 1]}->{heard[j - 1]}")
            i, j = i - 1, j - 1
        elif i and cost[i][j] == cost[i - 1][j] + 1:
            changes.append(f"-{expected[i - 1]}")
            i -= 1
        else:
            changes.append(f"+{heard[j - 1]}")
            j -= 1
    return cost[-1][-1], changes[::-1]


class Syllables:
    def __init__(self) -> None:
        try:
            import cmudict
            self.dictionary = cmudict.dict()
        except ImportError:
            self.dictionary = {}

    def count(self, word: str) -> int:
        word = re.sub(r"[^a-z']", "", word.lower())
        if word in self.dictionary:
            return sum(any(c.isdigit() for c in phone) for phone in self.dictionary[word][0])
        groups = len(re.findall("[aeiouy]+", word))
        return max(1, groups - (word.endswith("e") and not word.endswith(("le", "ye"))))


def audio_checks(audio, rate: int, text: str, syllables: Syllables) -> dict[str, Any]:
    import numpy as np
    peak = float(np.max(np.abs(audio)))
    loud = np.flatnonzero(np.abs(audio) >= 0.02 * peak) if peak > 0 else np.array([], dtype=int)
    first, last = (int(loud[0]), int(loud[-1])) if len(loud) else (0, 0)
    audible = max(1e-6, (last - first) / rate)
    words = normalise(text)
    count = sum(syllables.count(word) for word in words)
    frame = int(0.05 * rate)
    part = audio[first:last + 1]
    frames = part[: len(part) // frame * frame].reshape(-1, frame) if len(part) >= frame else part.reshape(1, -1)
    rms = np.sqrt(np.mean(frames ** 2, axis=1))
    speech = rms[rms > 0.1 * np.max(rms)] if len(rms) else rms
    result = {
        "duration_seconds": round(len(audio) / rate, 3),
        "audible_seconds": round(audible, 3),
        "lead_silence_seconds": round(first / rate, 3),
        "tail_silence_seconds": round((len(audio) - 1 - last) / rate, 3),
        "peak": round(peak, 4),
        "speech_rms_dbfs": round(float(20 * np.log10(np.sqrt(np.mean(speech ** 2)) + 1e-12)), 2) if len(speech) else None,
        "words": len(words),
        "syllables": count,
        "words_per_audible_second": round(len(words) / audible, 2),
        "syllables_per_audible_second": round(count / audible, 2),
    }
    try:
        import parselmouth
        pitch = parselmouth.Sound(audio.astype("float64"), rate).to_pitch_ac(
            time_step=0.01, pitch_floor=50, pitch_ceiling=300).selected_array["frequency"]
        voiced = pitch[pitch > 0]
        if len(voiced) >= 10:
            result["pitch_median_hz"] = round(float(np.median(voiced)), 1)
            result["pitch_span_semitones_p10_p90"] = round(
                float(12 * np.log2(np.percentile(voiced, 90) / np.percentile(voiced, 10))), 2)
    except ImportError:
        pass
    return result


def rate_windows(words: list[dict[str, Any]], syllables: Syllables) -> dict[str, Any] | None:
    """Two-second syllable rates over ASR word timings (the v5 rate-review measure)."""
    import numpy as np
    if len(words) < 2:
        return None
    first, last = words[0]["start"], words[-1]["end"]
    counts = [syllables.count(w["word"]) for w in words]
    windows = []
    for left in np.arange(first, max(first + 0.001, last - 2 + 0.001), 0.2):
        right = left + 2
        total = sum(n * max(0.0, min(w["end"], right) - max(w["start"], left)) / max(0.02, w["end"] - w["start"])
                    for w, n in zip(words, counts))
        windows.append(total / 2)
    return {"mean_syllables_per_second": round(sum(counts) / max(0.01, last - first), 2),
            "peak_two_second_rate": round(max(windows), 2), "minimum_two_second_rate": round(min(windows), 2),
            "internal_rate_ratio": round(max(windows) / max(0.1, min(windows)), 2)}


def terminal_contour(audio, rate: int, words: list[dict[str, Any]]) -> dict[str, Any] | None:
    """Pitch at the end of the last word, in semitones against the take's own median.

    A settled statement ending falls and stays low (often into creak, which has no measurable
    pitch). The fault the user heard is a late upturn: a fall to the floor, then a rise of several
    semitones over the last ~150 ms of voicing. terminal_rise_st is the last voiced frame minus the
    lowest frame of the final 0.25 s of voicing; the 40 Hz floor follows this voice down into fry.
    A take is hooked when that rise is at least TERMINAL_RISE_LIMIT and ends above
    TERMINAL_FLOOR_ST; jitter deep in creak (e.g. +2.1 st at -11 st) is still a settled end.
    Calibrated on the user's own Recording.m4a / Recording (3).m4a sentence ends: their statements
    rise 0-1.4 st; the one rising statement (the "Hey, this is my conversation voice." greeting)
    rises 7.8 st to +8.4 st. On A (rec3-natural, model-default sampling) about 60% of seeds hook."""
    import numpy as np
    if not words:
        return None
    try:
        import parselmouth
    except ImportError:
        return None
    final = words[-1]
    pitch = parselmouth.Sound(audio.astype("float64"), rate).to_pitch_ac(time_step=0.01, pitch_floor=40,
                                                                         pitch_ceiling=300)
    times, f0 = pitch.xs(), pitch.selected_array["frequency"]
    voiced = f0 > 0
    if voiced.sum() < 10:
        return None
    median = float(np.median(f0[voiced]))
    stop = min(len(audio) / rate, final["end"] + 0.35)
    mask = voiced & (times >= final["start"]) & (times <= stop)
    result: dict[str, Any] = {"word": final["word"], "start": round(final["start"], 3), "end": round(final["end"], 3),
                              "voiced_frames": int(mask.sum())}
    if mask.sum() < 5:
        result.update(settled=True, note="final word has too little measurable pitch (creak or unvoiced)")
        return result
    t, st = times[mask], 12 * np.log2(f0[mask] / median)
    late = t >= t[-1] - 0.25
    rise = float(st[-1] - st[late].min())
    result.update(min_st=round(float(st.min()), 2), last_st=round(float(st[-1]), 2),
                  terminal_rise_st=round(rise, 2), voiced_end=round(float(t[-1]), 3),
                  settled=not (rise >= TERMINAL_RISE_LIMIT and st[-1] > TERMINAL_FLOOR_ST))
    return result


TERMINAL_RISE_LIMIT = 2.0
TERMINAL_FLOOR_ST = -8.0


# ----------------------------------------------------------------------------- rendering

class Renderer:
    def __init__(self, profile: dict[str, Any], asr_name: str, asr_device: str, glossary: str | None,
                 x_vector_only: bool = False) -> None:
        os.environ.update(HF_HOME=str(VOICE_ROOT / "hf"), HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1",
                          HF_HUB_DISABLE_PROGRESS_BARS="1")
        import numpy as np
        import torch
        from qwen_tts import Qwen3TTSModel
        self.np, self.torch = np, torch
        torch.set_num_threads(4)
        started = time.time()
        self.model = Qwen3TTSModel.from_pretrained(str(MODEL_SNAPSHOT), device_map="cuda:0", dtype=torch.bfloat16,
                                                   attn_implementation="sdpa", local_files_only=True)
        # x_vector_only: speaker embedding only; the reference audio/text is not a prosodic prefix.
        self.x_vector_only = bool(x_vector_only)
        self.prompt = self.model.create_voice_clone_prompt(ref_audio=profile["reference_wav"],
                                                           ref_text=profile["reference_text"],
                                                           x_vector_only_mode=self.x_vector_only)
        self.load_seconds = round(time.time() - started, 1)
        self.reference_embedding = self.embedding_of_file(profile["reference_wav"])
        self.anchor_embeddings = [self.embedding_of_file(path) for path in profile.get("identity_anchors", [])]
        self.syllables = Syllables()
        self.glossary = glossary
        self.asr = None
        if asr_name != "none":
            if asr_device == "cuda":
                cuda_runtime_on_path()
            from faster_whisper import WhisperModel
            self.asr = WhisperModel(asr_name, device=asr_device,
                                    compute_type="float16" if asr_device == "cuda" else "int8",
                                    download_root=str(VOICE_ROOT / "asr"), local_files_only=True)

    def embedding_of_file(self, path: str):
        import soundfile as sf
        import soxr
        audio, rate = sf.read(path, dtype="float32", always_2d=True)
        audio = audio.mean(axis=1)
        if rate != SAMPLE_RATE:
            audio = soxr.resample(audio, rate, SAMPLE_RATE).astype("float32")
        return self.embedding(audio)

    def embedding(self, audio):
        with self.torch.inference_mode():
            vector = self.model.model.extract_speaker_embedding(audio=audio.astype("float32"), sr=SAMPLE_RATE)
        return vector.float().flatten().cpu()

    def cosine(self, left, right) -> float:
        return round(float(self.torch.nn.functional.cosine_similarity(left, right, dim=0)), 4)

    def generate(self, text: str, seed: int):
        self.torch.manual_seed(seed)
        self.torch.cuda.manual_seed_all(seed)
        waves, rate = self.model.generate_voice_clone(text=text, voice_clone_prompt=self.prompt, **PARAMS)
        if rate != SAMPLE_RATE or len(waves) != 1:
            raise RuntimeError(f"unexpected model output: {len(waves)} waves at {rate} Hz")
        audio = self.np.asarray(waves[0], dtype="float32")
        if audio.ndim != 1 or len(audio) == 0 or not self.np.isfinite(audio).all():
            raise RuntimeError("model produced empty or non-finite audio")
        return audio

    def check(self, audio, text: str, window: float | None, rate_band,
              settled_endings: bool = False) -> dict[str, Any]:
        import soxr
        result = audio_checks(audio, SAMPLE_RATE, text, self.syllables)
        embedding = self.embedding(audio)
        result["speaker_cosine_to_reference"] = self.cosine(embedding, self.reference_embedding)
        if self.anchor_embeddings:
            result["speaker_cosine_to_anchors"] = [self.cosine(embedding, anchor) for anchor in self.anchor_embeddings]
        failures = []
        if self.asr is not None:
            segments, _ = self.asr.transcribe(soxr.resample(audio, SAMPLE_RATE, 16000).astype("float32"),
                                              language="en", beam_size=5, vad_filter=False,
                                              condition_on_previous_text=False, word_timestamps=True,
                                              initial_prompt=self.glossary)
            segments = list(segments)
            heard = " ".join(s.text.strip() for s in segments)
            words = [{"word": w.word.strip(), "start": w.start, "end": w.end} for s in segments for w in (s.words or [])]
            expected = normalise(text)
            errors, changes = word_diff(expected, normalise(heard))
            result.update(asr_text=heard, word_errors=errors, word_error_rate=round(errors / max(1, len(expected)), 3),
                          word_changes=changes, exact=errors == 0,
                          asr_words=[{"word": w["word"], "start": round(float(w["start"]), 3),
                                      "end": round(float(w["end"]), 3)} for w in words])
            windows = rate_windows(words, self.syllables)
            if windows:
                result["rate_windows"] = windows
            terminal = terminal_contour(audio, SAMPLE_RATE, words)
            if terminal is not None:
                result["terminal"] = terminal
            if errors:
                failures.append("words")
            if settled_endings and terminal is not None and not terminal["settled"]:
                failures.append("ending")
        if window is not None:
            result["fits_window"] = result["duration_seconds"] <= window + 1e-6
            if not result["fits_window"]:
                failures.append("window")
        if rate_band:
            rate = result["syllables_per_audible_second"]
            if not rate_band[0] <= rate <= rate_band[1]:
                failures.append("rate")
        result["failures"] = failures
        return result


def score(checks: dict[str, Any]) -> tuple:
    """Lower is better: fewest failures, then fewest word errors, then (when every take hooks) the
    smallest final upturn, then the closest speaker match."""
    rise = (checks.get("terminal") or {}).get("terminal_rise_st") if "ending" in checks["failures"] else 0.0
    return (len(checks["failures"]), checks.get("word_errors", 0), rise or 0.0,
            -checks["speaker_cosine_to_reference"])


def settle_ending(audio, rate: int, word: dict[str, Any]):
    """Fallback for a take whose every seed hooked: hold the final word's pitch at its low point.

    Praat PSOLA (overlap-add) changes pitch only (formants, timing and length stay). Only the
    pitch points after the final word's lowest point in its last 0.25 s of voicing change; the
    whole file is resynthesised but only the final word from its start (15 ms equal-power
    crossfade) replaces the original, so everything before it stays sample-identical. No fade-out,
    trimming or tempo change."""
    import numpy as np
    import parselmouth
    from parselmouth.praat import call
    sound = parselmouth.Sound(audio.astype("float64"), rate)
    manipulation = call(sound, "To Manipulation", 0.01, 40, 300)
    tier = call(manipulation, "Extract pitch tier")
    points = [(call(tier, "Get time from index", i), call(tier, "Get value at index", i))
              for i in range(1, int(call(tier, "Get number of points")) + 1)]
    stop = min(len(audio) / rate, word["end"] + 0.35)
    final = [(t, f) for t, f in points if word["start"] <= t <= stop]
    if len(final) < 5:
        return None, {"edited": False, "reason": "too few pitch points on the final word"}
    t_low, f_low = min([p for p in final if p[0] >= final[-1][0] - 0.25], key=lambda p: p[1])
    changed = [(t, f) for t, f in final if t > t_low]
    if not changed:
        return None, {"edited": False, "reason": "no upturn after the low point"}
    call(tier, "Remove points between", t_low + 1e-4, stop + 0.01)
    for t, _ in changed:
        call(tier, "Add point", t, f_low)
    call([tier, manipulation], "Replace pitch tier")
    resynth = call(manipulation, "Get resynthesis (overlap-add)").values[0].astype("float32")
    resynth = resynth[: len(audio)] if len(resynth) >= len(audio) else np.pad(resynth, (0, len(audio) - len(resynth)))
    fade = int(0.015 * rate)
    start = max(0, int(word["start"] * rate) - fade)
    ramp = (np.sin(np.linspace(0, np.pi / 2, fade)) ** 2).astype("float32")
    out = audio.copy()
    out[start:start + fade] = audio[start:start + fade] * (1 - ramp) + resynth[start:start + fade] * ramp
    out[start + fade:] = resynth[start + fade:]
    return out, {"edited": True, "method": "Praat PSOLA overlap-add, final word only, pitch held at its low point",
                 "word": word["word"], "hold_from_seconds": round(t_low, 3), "held_hz": round(f_low, 1),
                 "removed_rise_st": round(float(12 * np.log2(max(f for _, f in changed) / f_low)), 2),
                 "pitch_points_changed": len(changed), "original_until_seconds": round(start / rate, 3),
                 "crossfade_ms": 15, "length_unchanged": len(out) == len(audio)}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--script", type=Path, required=True)
    parser.add_argument("--profile", type=Path, default=DEFAULT_PROFILE)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--id-prefix", default="", help="prefix for cue ids, e.g. movie-001-")
    parser.add_argument("--render-plan", type=Path, help="pronunciation hints: {id: {render_text, reason}}")
    parser.add_argument("--only", nargs="*", default=[])
    parser.add_argument("--seed-base", type=int, default=417)
    parser.add_argument("--sampling", choices=["approved", "model-default"], default="approved",
                        help="approved: temperature 0.68, top_p 0.9 (earlier FFVII/Borderlands setting); model-default: "
                             "the snapshot's generation_config.json (temperature 0.9, top_p 1.0, top_k 50, same for the "
                             "sub-talker); explicit flags below override either")
    for name, kind in (("temperature", float), ("top-p", float), ("top-k", int), ("subtalker-temperature", float),
                       ("subtalker-top-p", float), ("subtalker-top-k", int)):
        parser.add_argument(f"--{name}", type=kind)
    parser.add_argument("--retakes", type=int, default=2, help="extra seeds tried when a take fails a check")
    parser.add_argument("--settled-endings", action="store_true",
                        help="count a late pitch upturn on the last word (terminal_contour) as a failed take, so a "
                             "new seed is tried; the fix for endings that sound like more is coming")
    parser.add_argument("--ending-fallback", choices=["none", "edit"], default="none",
                        help="edit: if every take is word-exact and fits but still hooks, hold the final word's pitch "
                             "at its low point on the least-hooked take (settle_ending; PSOLA, final word only); the "
                             "raw take stays in candidates/ and the record says pitch_manipulation")
    parser.add_argument("--x-vector-only", action="store_true",
                        help="clone from the reference's speaker embedding only (create_voice_clone_prompt "
                             "x_vector_only_mode); the reference audio/text is not used as a prosodic prefix. "
                             "Off by default (ICL). Recorded in metadata and the fingerprint")
    parser.add_argument("--rate-band", type=float, nargs=2, metavar=("MIN", "MAX"),
                        help="syllables per audible second; outside it counts as a failure (e.g. 3.5 5.5)")
    parser.add_argument("--asr", choices=["small.en", "medium.en", "none"], default="medium.en")
    parser.add_argument("--asr-device", choices=["cuda", "cpu"], default="cuda")
    parser.add_argument("--asr-glossary", help="names to prime recognition (e.g. 'Crono, Lucca, Marle, Guardia')")
    parser.add_argument("--repeat-check", action="store_true",
                        help="render each promoted take a second time with its seed and compare samples")
    parser.add_argument("--target-speech-dbfs", type=float,
                        help="apply one fixed gain so the take's speech RMS reaches this level (peak kept <= 0.95); "
                             "off by default, which keeps the model's raw level")
    parser.add_argument("--min-free-gib", type=float, default=6.0)
    parser.add_argument("--max-gpu-utilization", type=int, default=60)
    parser.add_argument("--ignore-gpu-contention", action="store_true")
    parser.add_argument("--force", action="store_true", help="re-render even when a checkpoint matches")
    parser.add_argument("--dry-run", action="store_true", help="validate inputs and report the plan only")
    args = parser.parse_args(argv)
    if args.sampling == "model-default":
        PARAMS.update(temperature=0.9, top_p=1.0, top_k=50, subtalker_temperature=0.9, subtalker_top_p=1.0,
                      subtalker_top_k=50)
    for name in ("temperature", "top_p", "top_k", "subtalker_temperature", "subtalker_top_p", "subtalker_top_k"):
        if getattr(args, name) is not None:
            PARAMS[name] = getattr(args, name)
    if not 0 <= args.retakes <= 16:
        parser.error("--retakes must be between 0 and 16")
    if args.ending_fallback != "none" and not args.settled_endings:
        parser.error("--ending-fallback needs --settled-endings")

    try:
        cues = load_script(args.script, args.id_prefix)
        profile = load_profile(args.profile)
        hints = json.loads(args.render_plan.read_text(encoding="utf-8")) if args.render_plan else {}
        unknown = sorted(set(hints) - {cue["id"] for cue in cues})
        if unknown:
            raise InputError(f"render plan names unknown cues: {unknown}")
    except (InputError, OSError, ValueError) as exc:
        print(json.dumps({"error": str(exc)}), file=sys.stderr)
        return 1
    for cue in cues:
        hint = hints.get(cue["id"], {})
        cue["render_text"] = clean(hint.get("render_text", cue["text"]))
        cue["render_note"] = hint.get("reason")
    if args.only:
        cues = [cue for cue in cues if any(cue["id"].startswith(prefix) for prefix in args.only)]
    stamp = fingerprint(profile, args.x_vector_only)
    out = args.out.resolve()
    pending = []
    for cue in cues:
        meta = out / f"{cue['id']}.json"
        current = False
        if meta.exists() and not args.force:
            try:
                record = json.loads(meta.read_text(encoding="utf-8"))
                current = (record.get("fingerprint") == stamp and record.get("text_sha256") == cue["text_sha256"]
                           and record.get("render_text") == cue["render_text"]
                           and (record.get("level") or {}).get("target_speech_dbfs") == args.target_speech_dbfs
                           and record.get("settled_endings", False) == args.settled_endings
                           and record.get("ending_fallback", "none") == args.ending_fallback
                           and bool(record.get("x_vector_only_mode", False)) == args.x_vector_only
                           and record.get("wave_sha256") == sha_bytes(out / f"{cue['id']}.wav"))
            except (OSError, ValueError):
                current = False
        if not current:
            pending.append(cue)
    preflight = gpu_preflight(args.min_free_gib, args.max_gpu_utilization)
    plan = {"script": str(args.script), "profile": str(args.profile), "out": str(out), "fingerprint": stamp,
            "reference_sha256": profile["reference_sha256"], "cues": len(cues), "pending": [c["id"] for c in pending],
            "x_vector_only_mode": args.x_vector_only, "gpu_preflight": preflight, "dry_run": args.dry_run}
    print(json.dumps(plan), flush=True)
    if args.dry_run or not pending:
        return 0
    if not preflight.get("ok") and not args.ignore_gpu_contention:
        print(json.dumps({"error": "GPU busy, short of memory or a game is running; retry later or pass "
                                   "--ignore-gpu-contention", "gpu_preflight": preflight}), file=sys.stderr)
        return 1
    if not MODEL_SNAPSHOT.is_dir():
        print(json.dumps({"error": f"model snapshot missing: {MODEL_SNAPSHOT}"}), file=sys.stderr)
        return 1

    import numpy as np
    import soundfile as sf
    renderer = Renderer(profile, args.asr, args.asr_device, args.asr_glossary, args.x_vector_only)
    out.mkdir(parents=True, exist_ok=True)
    (out / "candidates").mkdir(exist_ok=True)
    print(json.dumps({"stage": "models loaded", "seconds": renderer.load_seconds}), flush=True)
    failures = []
    started = time.time()
    for cue in pending:
        takes = []
        for take in range(args.retakes + 1):
            seed = cue_seed(args.seed_base, cue["render_text"], take)
            begun = time.time()
            audio = renderer.generate(cue["render_text"], seed)
            elapsed = time.time() - begun
            checks = renderer.check(audio, cue["text"], cue["window_seconds"], args.rate_band, args.settled_endings)
            candidate = out / "candidates" / f"{cue['id']}.take{take}.wav"
            sf.write(str(candidate), audio, SAMPLE_RATE, subtype=SUBTYPE)
            takes.append({"take": take, "seed": seed, "wave": candidate.name, "wave_sha256": sha_bytes(candidate),
                          "generation_seconds": round(elapsed, 2), "checks": checks})
            print(json.dumps({"id": cue["id"], "take": take, "seed": seed, "duration": checks["duration_seconds"],
                              "rate": checks["syllables_per_audible_second"], "failures": checks["failures"],
                              "asr": checks.get("asr_text")}), flush=True)
            if not checks["failures"]:
                break
        best = min(takes, key=lambda t: score(t["checks"]))
        destination = out / f"{cue['id']}.wav"
        raw, _ = sf.read(str(out / "candidates" / best["wave"]), dtype="float32")
        take_audio = raw
        ending_edit = None
        if args.ending_fallback == "edit" and best["checks"]["failures"] == ["ending"]:
            edited, ending_edit = settle_ending(raw, SAMPLE_RATE, best["checks"]["terminal"])
            if edited is not None:
                after = renderer.check(edited, cue["text"], cue["window_seconds"], args.rate_band, True)
                ending_edit["checks_after"] = after
                if not after["failures"]:
                    edited_path = out / "candidates" / f"{cue['id']}.take{best['take']}.settled.wav"
                    sf.write(str(edited_path), edited, SAMPLE_RATE, subtype=SUBTYPE)
                    ending_edit.update(wave=edited_path.name, raw_wave=best["wave"])
                    best = {**best, "checks": after}
                    raw = edited
                else:
                    ending_edit["edited"] = False
                    ending_edit["reason"] = f"edited take failed {after['failures']}; raw take kept"
            print(json.dumps({"id": cue["id"], "ending_edit": {k: v for k, v in ending_edit.items()
                                                               if k != "checks_after"}}), flush=True)
        edited_used = bool(ending_edit and ending_edit.get("edited"))
        level = None
        if args.target_speech_dbfs is not None and best["checks"].get("speech_rms_dbfs") is not None:
            wanted = float(args.target_speech_dbfs - best["checks"]["speech_rms_dbfs"])
            ceiling = float(20 * np.log10(0.95 / max(best["checks"]["peak"], 1e-6)))
            gain_db = min(wanted, ceiling)
            sf.write(str(destination), raw * 10 ** (gain_db / 20), SAMPLE_RATE, subtype=SUBTYPE)
            level = {"target_speech_dbfs": args.target_speech_dbfs, "gain_db": round(gain_db, 2),
                     "peak_limited": gain_db < wanted,
                     "speech_rms_dbfs_after": round(best["checks"]["speech_rms_dbfs"] + gain_db, 2),
                     "peak_after": round(best["checks"]["peak"] * 10 ** (gain_db / 20), 4)}
        elif edited_used:
            sf.write(str(destination), raw, SAMPLE_RATE, subtype=SUBTYPE)
        else:
            destination.write_bytes((out / "candidates" / best["wave"]).read_bytes())
        repeat = None
        if args.repeat_check:
            again = renderer.generate(cue["render_text"], best["seed"])
            original = take_audio
            same_length = len(again) == len(original)
            repeat = {"same_length": same_length,
                      "max_abs_difference": round(float(np.max(np.abs(again - original))), 6) if same_length else None,
                      "durations": [round(len(original) / SAMPLE_RATE, 3), round(len(again) / SAMPLE_RATE, 3)]}
        record = {
            "id": cue["id"], "movie": cue.get("movie"), "text": cue["text"], "text_sha256": cue["text_sha256"],
            "render_text": cue["render_text"], "render_note": cue["render_note"],
            "cue_start": cue.get("start"), "cue_end": cue.get("end"), "window_seconds": cue["window_seconds"],
            "seed": best["seed"], "take": best["take"], "takes": takes, "checks": best["checks"],
            "repeat_check": repeat, "level": level, "fingerprint": stamp, "model_snapshot": MODEL_SNAPSHOT.name,
            "reference_wav": profile["reference_wav"], "reference_sha256": profile["reference_sha256"],
            "reference_text_sha256": sha_text(profile["reference_text"]), "params": PARAMS,
            "dtype": "bfloat16", "attn": "sdpa", "x_vector_only_mode": args.x_vector_only, "sample_rate": SAMPLE_RATE,
            "subtype": SUBTYPE, "tempo_factor": 1.0, "pitch_manipulation": edited_used, "cropped": False,
            "settled_endings": args.settled_endings, "ending_fallback": args.ending_fallback,
            "ending_edit": ending_edit,
            "voice_local_only": True, "script_version": SCRIPT_VERSION,
            "generated_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "wave_sha256": sha_bytes(destination),
        }
        (out / f"{cue['id']}.json").write_text(json.dumps(record, indent=1) + "\n", encoding="utf-8")
        if best["checks"]["failures"]:
            failures.append({"id": cue["id"], "failures": best["checks"]["failures"],
                             "changes": best["checks"].get("word_changes")})
    manifest = []
    for cue in cues:
        meta = out / f"{cue['id']}.json"
        record = json.loads(meta.read_text(encoding="utf-8")) if meta.exists() else {}
        manifest.append({"id": cue["id"], "text_sha256": cue["text_sha256"], "start": cue.get("start"),
                         "end": cue.get("end"), "wave": f"{cue['id']}.wav" if record else None,
                         "duration_seconds": record.get("checks", {}).get("duration_seconds"),
                         "audible_seconds": record.get("checks", {}).get("audible_seconds"),
                         "lead_silence_seconds": record.get("checks", {}).get("lead_silence_seconds"),
                         "failures": record.get("checks", {}).get("failures"), "wave_sha256": record.get("wave_sha256")})
    (out / "manifest.json").write_text(json.dumps({"fingerprint": stamp, "sample_rate": SAMPLE_RATE, "subtype": SUBTYPE,
                                                   "cues": manifest}, indent=1) + "\n", encoding="utf-8")
    report = {"completed": len(pending), "elapsed_seconds": round(time.time() - started, 1), "failures": failures,
              "gpu_preflight": preflight, "fingerprint": stamp,
              "versions": {"torch": renderer.torch.__version__, "cuda": renderer.torch.version.cuda,
                           "gpu_peak_gib": round(renderer.torch.cuda.max_memory_allocated() / 2 ** 30, 2)}}
    (out / "check-report.json").write_text(json.dumps(report, indent=1) + "\n", encoding="utf-8")
    print(json.dumps({"completed": len(pending), "failures": failures}), flush=True)
    return 2 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
