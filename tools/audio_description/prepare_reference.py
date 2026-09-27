"""Prepare a local voice-cloning reference from the user's own recordings (never uploaded).

Run with the local voice runtime:

    C:\\Users\\buu42\\.local\\share\\blind-soldier-voice\\venv\\Scripts\\python.exe tools\\audio_description\\prepare_reference.py analyze
    ...\\python.exe tools\\audio_description\\prepare_reference.py build --plan <plan.json>

analyze  Inventories every recording (sha256, format, levels, noise floor, clipping) and
         transcribes it twice with the cached faster-whisper models (medium.en with word
         timestamps, small.en as a cross-check), writing recordings-analysis.json.
build    Cuts the reference from the plan's source ranges with a documented edit list: keep
         ranges, shorten silences between them to a fixed natural gap, 60 Hz high-pass, mono,
         24 kHz, one fixed gain to a target peak. No time stretching, pitch change or denoising.
         Writes reference.wav, reference.txt (the exact transcript the model is given) and
         reference-provenance.json, then re-transcribes the result to verify the text.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
VOICE_ROOT = Path(os.environ.get("BLIND_SOLDIER_VOICE", r"C:\Users\buu42\.local\share\blind-soldier-voice"))
RECORDINGS = Path(os.environ.get("VOICE_RECORDINGS", r"C:\Users\buu42\OneDrive\Documents\Sound Recordings"))
DEFAULT_OUT = REPO / "artifacts/research/audio-description-0336/voice"
SOURCES = ["Recording.m4a", "Recording (2).m4a", "Recording (3).m4a"]
ANALYSIS_RATE = 48000
MODEL_RATE = 24000


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def probe(path: Path) -> dict:
    result = subprocess.run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(path)],
                            capture_output=True, text=True, check=True)
    info = json.loads(result.stdout)
    stream = next(s for s in info["streams"] if s["codec_type"] == "audio")
    return {"codec": stream["codec_name"], "sample_rate": int(stream["sample_rate"]), "channels": stream["channels"],
            "duration": float(info["format"]["duration"]), "bit_rate": int(info["format"].get("bit_rate", 0)),
            "creation_time": stream.get("tags", {}).get("creation_time")}


def decode(path: Path, rate: int, channels: int = 2):
    """Float32 samples, (frames, channels), decoded by ffmpeg exactly once per call."""
    import numpy as np
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-f", "f32le", "-acodec", "pcm_f32le",
                          "-ac", str(channels), "-ar", str(rate), "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype="<f4").reshape(-1, channels).copy()


def levels(mono, rate: int) -> dict:
    import numpy as np
    frame = int(0.05 * rate)
    frames = mono[: len(mono) // frame * frame].reshape(-1, frame)
    rms = np.sqrt(np.mean(frames ** 2, axis=1)) + 1e-12
    db = 20 * np.log10(rms)
    peak = float(np.max(np.abs(mono)))
    return {
        "peak": peak,
        "peak_dbfs": round(20 * np.log10(max(peak, 1e-12)), 2),
        "rms_dbfs": round(float(20 * np.log10(np.sqrt(np.mean(mono ** 2)) + 1e-12)), 2),
        "noise_floor_dbfs_p10": round(float(np.percentile(db, 10)), 2),
        "speech_level_dbfs_p90": round(float(np.percentile(db, 90)), 2),
        "snr_estimate_db": round(float(np.percentile(db, 90) - np.percentile(db, 10)), 2),
        "clipped_samples": int(np.sum(np.abs(mono) >= 0.999)),
    }


def cuda_runtime_on_path() -> None:
    """CTranslate2 needs cublas64_12.dll; the venv's torch cu128 wheel ships it in torch/lib."""
    import torch
    library = Path(torch.__file__).parent / "lib"
    if library.is_dir():
        os.add_dll_directory(str(library))
        os.environ["PATH"] = str(library) + os.pathsep + os.environ.get("PATH", "")


def whisper(name: str, device: str):
    os.environ.update(HF_HUB_OFFLINE="1", HF_HUB_DISABLE_PROGRESS_BARS="1")
    if device == "cuda":
        cuda_runtime_on_path()
    from faster_whisper import WhisperModel
    compute = "float16" if device == "cuda" else "int8"
    return WhisperModel(name, device=device, compute_type=compute, download_root=str(VOICE_ROOT / "asr"),
                        local_files_only=True)


def transcribe(model, audio_16k, words: bool = True) -> dict:
    segments, info = model.transcribe(audio_16k, language="en", beam_size=5, vad_filter=False,
                                      condition_on_previous_text=False, word_timestamps=words)
    rows = []
    for segment in segments:
        rows.append({"start": round(segment.start, 2), "end": round(segment.end, 2), "text": segment.text.strip(),
                     "avg_logprob": round(segment.avg_logprob, 3), "no_speech_prob": round(segment.no_speech_prob, 3),
                     "words": [{"word": w.word.strip(), "start": round(w.start, 2), "end": round(w.end, 2),
                                "probability": round(w.probability, 3)} for w in (segment.words or [])]})
    return {"text": " ".join(r["text"] for r in rows), "segments": rows}


def pitch(mono, rate: int, start: float, end: float) -> dict | None:
    import numpy as np
    import parselmouth
    part = mono[int(start * rate): int(end * rate)]
    if len(part) < rate * 0.3:
        return None
    values = parselmouth.Sound(part.astype("float64"), rate).to_pitch_ac(
        time_step=0.01, pitch_floor=50, pitch_ceiling=400).selected_array["frequency"]
    voiced = values[values > 0]
    if len(voiced) < 10:
        return None
    return {"median_hz": round(float(np.median(voiced)), 1),
            "p10_hz": round(float(np.percentile(voiced, 10)), 1), "p90_hz": round(float(np.percentile(voiced, 90)), 1)}


def analyze(args) -> int:
    import numpy as np
    import soxr
    out = args.out
    out.mkdir(parents=True, exist_ok=True)
    medium = whisper("medium.en", args.asr_device)
    small = whisper("small.en", args.asr_device)
    report = {"recordings_dir": str(RECORDINGS), "analysed_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
              "asr": {"primary": "faster-whisper medium.en", "cross_check": "faster-whisper small.en",
                      "device": args.asr_device, "local_files_only": True}, "recordings": []}
    for name in SOURCES:
        path = RECORDINGS / name
        info = probe(path)
        stereo = decode(path, ANALYSIS_RATE, 2)
        left, right = stereo[:, 0], stereo[:, 1]
        mono = stereo.mean(axis=1)
        channel_corr = float(np.corrcoef(left, right)[0, 1]) if np.std(left) > 0 and np.std(right) > 0 else None
        audio_16k = soxr.resample(mono, ANALYSIS_RATE, 16000).astype("float32")
        primary = transcribe(medium, audio_16k)
        check = transcribe(small, audio_16k, words=False)
        for segment in primary["segments"]:
            segment["pitch"] = pitch(mono, ANALYSIS_RATE, segment["start"], segment["end"])
            speech = sum(w["end"] - w["start"] for w in segment["words"])
            segment["words_per_second_in_words"] = round(len(segment["words"]) / speech, 2) if speech else None
        report["recordings"].append({
            "file": name, "path": str(path), "sha256": sha256(path), "bytes": path.stat().st_size, **info,
            "channel_correlation": channel_corr, "levels": levels(mono, ANALYSIS_RATE),
            "transcript_medium": primary, "transcript_small": check})
        print(json.dumps({"file": name, "duration": info["duration"], "text": primary["text"]}), flush=True)
    target = out / "recordings-analysis.json"
    target.write_text(json.dumps(report, indent=1) + "\n", encoding="utf-8")
    print(json.dumps({"written": str(target)}))
    return 0


def speech_ranges(signal, rate: int, span, max_pause: float, margin: float) -> list[list[float]]:
    """Speech inside span, split wherever a pause is longer than max_pause.

    A frame is silent when its 30 ms RMS is below whichever is higher: 15 dB over the span's
    10th-percentile level, or 30 dB under its 90th percentile. Each kept range keeps `margin`
    of the real pause on both sides, so consonant onsets and tails are never cut."""
    import numpy as np
    hop, window = int(0.01 * rate), int(0.03 * rate)
    first, last = int(span[0] * rate), int(span[1] * rate)
    part = signal[first:last]
    count = max(1, (len(part) - window) // hop + 1)
    db = np.array([20 * np.log10(np.sqrt(np.mean(part[i * hop:i * hop + window] ** 2)) + 1e-12) for i in range(count)])
    threshold = max(np.percentile(db, 10) + 15, np.percentile(db, 90) - 30)
    speech = db > threshold
    ranges, start = [], None
    for index, active in enumerate(np.append(speech, False)):
        if active and start is None:
            start = index
        elif not active and start is not None:
            ranges.append([start, index])
            start = None
    merged = []
    for left, right in ranges:
        if merged and (left - merged[-1][1]) * hop / rate <= max_pause:
            merged[-1][1] = right
        else:
            merged.append([left, right])
    merged = [r for r in merged if (r[1] - r[0]) * hop / rate >= 0.08]
    return [[round(max(span[0], span[0] + l * hop / rate - margin), 3),
             round(min(span[1], span[0] + (r * hop + window) / rate + margin), 3)] for l, r in merged]


def build(args) -> int:
    import numpy as np
    import soundfile as sf
    import soxr
    from scipy.signal import butter, sosfiltfilt
    plan = json.loads(args.plan.read_text(encoding="utf-8"))
    out = args.out
    out.mkdir(parents=True, exist_ok=True)
    source = RECORDINGS / plan["source"]
    source_sha = sha256(source)
    if plan.get("source_sha256") and plan["source_sha256"] != source_sha:
        raise SystemExit(f"{source} changed: {source_sha} != plan {plan['source_sha256']}")
    stereo = decode(source, ANALYSIS_RATE, 2)
    mono = stereo.mean(axis=1).astype("float64")
    sos = butter(2, 60, btype="highpass", fs=ANALYSIS_RATE, output="sos")
    filtered = sosfiltfilt(sos, mono)
    if "keep" not in plan:
        plan["keep"] = speech_ranges(filtered, ANALYSIS_RATE, plan["span"], plan.get("max_pause_seconds", 0.4),
                                     plan.get("margin_seconds", 0.12))
    gap = np.zeros(int(round(plan.get("gap_seconds", 0.3) * ANALYSIS_RATE)))
    fade = int(0.01 * ANALYSIS_RATE)
    pieces = []
    for index, (start, end) in enumerate(plan["keep"]):
        part = filtered[int(round(start * ANALYSIS_RATE)): int(round(end * ANALYSIS_RATE))].copy()
        ramp = np.linspace(0.0, 1.0, fade)
        part[:fade] *= ramp
        part[-fade:] *= ramp[::-1]
        if index:
            pieces.append(gap)
        pieces.append(part)
    joined = np.concatenate(pieces)
    reference = soxr.resample(joined, ANALYSIS_RATE, MODEL_RATE)
    peak = float(np.max(np.abs(reference)))
    gain = plan.get("target_peak", 0.8) / peak
    reference = (reference * gain).astype("float32")
    wav = out / "reference.wav"
    sf.write(str(wav), reference, MODEL_RATE, subtype="PCM_16")
    text = " ".join(plan["text"].split())
    (out / "reference.txt").write_text(text + "\n", encoding="utf-8")
    medium = whisper("medium.en", args.asr_device)
    heard = transcribe(medium, soxr.resample(reference, MODEL_RATE, 16000).astype("float32"), words=False)["text"]
    provenance = {
        "purpose": "Local voice-cloning reference for Chrono Trigger audio descriptions in the user's own voice.",
        "consent": "User-authorised cloning and editing of their own recordings (Codex delegation 2026-09-27).",
        "source": str(source), "source_sha256": source_sha,
        "edit_list": {"keep_seconds": plan["keep"], "gap_seconds_between_keeps": plan.get("gap_seconds", 0.3),
                      "fade_ms": 10, "highpass_hz": 60, "downmix": "mean of L/R", "resample": "soxr 48000->24000",
                      "gain": round(gain, 4), "target_peak": plan.get("target_peak", 0.8)},
        "not_applied": ["time stretching", "pitch shifting or contour transplant", "denoising", "compression",
                        "any third-party or synthetic audio"],
        "reference_wav": str(wav), "reference_sha256": sha256(wav),
        "duration_seconds": round(len(reference) / MODEL_RATE, 3), "sample_rate": MODEL_RATE, "subtype": "PCM_16",
        "reference_text": text, "reference_text_sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
        "reference_text_basis": plan.get("text_basis"),
        "asr_of_reference_medium_en": heard,
        "built_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    (out / "reference-provenance.json").write_text(json.dumps(provenance, indent=1) + "\n", encoding="utf-8")
    print(json.dumps({"reference": str(wav), "duration": provenance["duration_seconds"], "heard": heard,
                      "text": text}))
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["analyze", "build"])
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--plan", type=Path, help="build: JSON with source, source_sha256, keep, text")
    parser.add_argument("--asr-device", choices=["cuda", "cpu"], default="cuda")
    args = parser.parse_args(argv)
    if args.command == "build" and not args.plan:
        parser.error("build needs --plan")
    return analyze(args) if args.command == "analyze" else build(args)


if __name__ == "__main__":
    sys.exit(main())
