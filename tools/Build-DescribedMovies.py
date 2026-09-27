"""Build and reversibly install narrated Chrono Trigger movies from reviewed local cues.

No uploads, paid jobs, voice generation, or changes to the game executable. Build needs
ffmpeg/ffprobe, numpy and soundfile. Install/restore need only Python's standard library.
The .dat transform is audited at libcocos2d.dll RVA 0x287FAA, VideoPlayer::setFileName.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid

NAMES = {f"{n:03}.dat" for n in range(1, 9)} | {"007-en.dat"}
RATE = 48000
CREDIT = ("Descriptions generated with ViddyScribe, reviewed and edited for the Chrono Trigger "
          "accessibility mod. Narration synthesized locally from the user's own recordings.")


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def transform(source, destination, chunk_size=2 ** 20):
    """The game's symmetric byte XOR, retaining key position across chunks."""
    if Path(source).resolve() == Path(destination).resolve():
        raise ValueError("transform requires a separate output file")
    offset = 0
    with Path(source).open("rb") as src, Path(destination).open("wb") as dest:
        while block := src.read(chunk_size):
            data = bytearray(block)
            # Native chunks are 2 MiB (a multiple of 256); support arbitrary boundaries too.
            for k in range(256):
                key = 255 - ((offset + k) & 255)
                data[k::256] = data[k::256].translate(bytes(i ^ key for i in range(256)))
            dest.write(data)
            offset += len(block)


def run(args, **kwargs):
    return subprocess.run([str(a) for a in args], check=True, capture_output=True, **kwargs)


def probe(path):
    return json.loads(run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", path]).stdout)


def video_hash(path):
    return run(["ffmpeg", "-v", "error", "-i", path, "-map", "0:v:0", "-c:v", "copy",
                "-f", "hash", "-hash", "sha256", "-"], text=True).stdout.strip()


def video_packet_times(path):
    return json.loads(run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_packets",
                           "-show_entries", "packet=pts,dts,duration", "-of", "json", path]).stdout)["packets"]


def validate_cues(cues, duration):
    if not cues or not math.isfinite(duration) or duration <= 0:
        raise ValueError("empty script or invalid movie duration")
    previous = 0
    ids = set()
    for cue in cues:
        start, end = cue["start"], cue["end"]
        if (not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,63}", cue["id"]) or cue["id"] in ids or
                not isinstance(cue["text"], str) or not cue["text"].strip()):
            raise ValueError("invalid or duplicate cue id/text")
        if not all(isinstance(t, (int, float)) and math.isfinite(t) for t in (start, end)):
            raise ValueError("invalid cue time")
        if start < previous or end <= start or end > duration:
            raise ValueError(f"{cue['id']}: overlapping cue or time outside movie")
        previous = end
        ids.add(cue["id"])


def mix_film(film, source, voices, destination, approvals):
    import numpy as np
    import soundfile as sf
    validate_cues(film["cues"], film["duration_seconds"])
    before = probe(source)
    duration = float(before["format"]["duration"])
    if abs(duration - film["duration_seconds"]) > .025 or sha(source) != film["decoded_sha256"]:
        raise ValueError(f"{source.name}: source movie does not match reviewed script")
    raw = run(["ffmpeg", "-v", "error", "-i", source, "-map", "0:a:0", "-f", "f32le",
               "-acodec", "pcm_f32le", "-ar", RATE, "-ac", 2, "-"]).stdout
    frames = round(duration * RATE)
    original = np.frombuffer(raw, dtype="<f4").reshape(-1, 2)
    music = np.zeros((frames, 2), dtype=np.float32)
    music[:min(frames, len(original))] = original[:frames]
    duck = np.ones(frames, dtype=np.float32)
    speech = np.zeros(frames, dtype=np.float32)
    report = []
    for cue in film["cues"]:
        wave = voices / (cue["id"] + ".wav")
        meta = read_json(voices / (cue["id"] + ".json"))
        wave_hash = sha(wave)
        text = " ".join(cue["text"].split())
        if meta.get("text") != text or meta.get("wave_sha256") != wave_hash:
            raise ValueError(f"{cue['id']}: changed voice waveform or script")
        failures = meta.get("checks", {}).get("failures", ["missing checks"])
        approval = approvals.get(cue["id"], {})
        if failures and not (approval.get("wave_sha256") == wave_hash and approval.get("reason")):
            raise ValueError(f"{cue['id']}: unreviewed voice check failures: {failures}")
        raw = run(["ffmpeg", "-v", "error", "-i", wave, "-f", "f32le", "-acodec", "pcm_f32le",
                   "-ar", RATE, "-ac", 1, "-"]).stdout
        audio = np.frombuffer(raw, dtype="<f4").copy()
        start = round(cue["start"] * RATE)
        stop = start + len(audio)
        if len(audio) / RATE > cue["end"] - cue["start"] + .005 or stop > frames:
            raise ValueError(f"{cue['id']}: voice does not fit; shorten the text or choose another take")
        if not len(audio) or not np.all(np.isfinite(audio)) or np.max(np.abs(audio)) < .0001:
            raise ValueError(f"{cue['id']}: empty, silent or invalid voice")
        # Level only: no time stretching, pitch shifting or shortening of the voice.
        active = audio[np.abs(audio) > max(.002, float(np.max(np.abs(audio))) * .025)]
        rms = float(np.sqrt(np.mean(active ** 2)))
        gain = min(10 ** (-18 / 20) / rms, 10 ** (-3 / 20) / float(np.max(np.abs(audio))))
        speech[start:stop] += audio * gain
        # Preserve music/effects but leave at least a 12 dB RMS narration margin.
        # A fixed reduction was insufficient under the movies' loud action effects.
        edge = round(.10 * RATE)
        lo, hi = max(0, start - edge), min(frames, stop + 2 * edge)
        voice_rms = float(np.sqrt(np.mean((audio * gain) ** 2)))
        bed_rms = float(np.sqrt(np.mean(music[start:stop] ** 2)))
        floor = min(10 ** (-12 / 20), voice_rms / max(bed_rms, .000001) * 10 ** (-12 / 20))
        envelope = np.full(hi - lo, floor, dtype=np.float32)
        if start > lo:
            envelope[:start-lo] = np.linspace(1, floor, start-lo)
        if hi > stop:
            envelope[stop-lo:] = np.linspace(floor, 1, hi-stop)
        duck[lo:hi] = np.minimum(duck[lo:hi], envelope)
        report.append({"id": cue["id"], "waveSha256": wave_hash, "start": cue["start"],
                       "duration": len(audio) / RATE, "gainDb": round(20 * math.log10(gain), 3),
                       "soundtrackDuckDb": round(20 * math.log10(floor), 3),
                       "voiceChecks": failures, "review": approval or None})
    mixed = music * duck[:, None] + speech[:, None]
    peak = float(np.max(np.abs(mixed)))
    # Global gain only if required for 1 dB headroom; no clipping or pumping limiter.
    master = min(1, 10 ** (-1 / 20) / max(peak, .000001))
    mixed *= master
    wav_path = destination.with_suffix(".wav")
    sf.write(str(wav_path), mixed, RATE, subtype="PCM_24")
    # Keep original stream order (AAC then H.264), video packets and video timescale.
    video = next(s for s in before["streams"] if s["codec_type"] == "video")
    timescale = video["time_base"].split("/")[-1]
    run(["ffmpeg", "-v", "error", "-y", "-i", source, "-i", wav_path,
         "-map", "1:a:0", "-map", "0:v:0", "-map_metadata", "0", "-c:v", "copy",
         "-video_track_timescale", timescale, "-movie_timescale", "10000", "-c:a", "aac", "-b:a", "256k",
         "-t", f"{duration:.9f}", "-movflags", "+faststart", destination])
    after = probe(destination)
    after_video = next(s for s in after["streams"] if s["codec_type"] == "video")
    unchanged = video_hash(source) == video_hash(destination)
    stable = all(video.get(k) == after_video.get(k) for k in
                 ("codec_name", "width", "height", "nb_frames", "r_frame_rate", "time_base", "start_time"))
    stable = stable and video_packet_times(source) == video_packet_times(destination)
    # MP4 track summaries round edited durations differently. Compare every actual
    # video packet's PTS, DTS and duration, and bound container rounding to 1 ms.
    duration_delta = float(after["format"]["duration"]) - duration
    if not unchanged or not stable or abs(duration_delta) > .001:
        raise ValueError(f"{source.name}: output changed video or movie duration")
    return {"videoPacketsUnchanged": unchanged, "videoTimingUnchanged": stable, "duration": duration,
            "containerDurationDeltaSeconds": round(duration_delta, 9),
            "masterGainDb": round(20 * math.log10(master), 3), "preEncodePeak": float(np.max(np.abs(mixed))),
            "minimumNarrationMarginDb": 12, "cues": report}


def build(script, sources, voices, output, approvals_path=None):
    if (output / "Chrono Trigger.exe").exists() or output.resolve() == sources.resolve():
        raise ValueError("build output must be a staging directory, separate from the game and source movies")
    document = read_json(script)
    approvals = read_json(approvals_path) if approvals_path else {}
    output.mkdir(parents=True, exist_ok=True)
    pack = {"version": 1, "credit": CREDIT, "scriptSha256": sha(script), "movies": []}
    for film in document["films"]:
        name = film["movie"] + ".dat"
        if name not in NAMES:
            raise ValueError("unknown movie name")
        source = sources / (film["movie"] + ".mp4")
        described = output / (film["movie"] + ".mp4")
        report = mix_film(film, source, voices, described, approvals)
        encoded = output / name
        transform(described, encoded)
        roundtrip = output / (film["movie"] + ".roundtrip.mp4")
        transform(encoded, roundtrip)
        if sha(described) != sha(roundtrip):
            raise ValueError("native movie transform roundtrip failed")
        # No recursive cleanup; this known temporary file is in the output directory.
        roundtrip.unlink()
        write_json(output / (film["movie"] + "-verification.json"), report)
        pack["movies"].append({"fileName": name, "sourceSha256": film["source_sha256"],
                               "sha256": sha(encoded), "narration": True, "jobId": film["source_job"],
                               "cueCount": len(film["cues"]), "videoVerified": True})
        print(json.dumps({"movie": name, "cues": len(film["cues"]), "verified": True}), flush=True)
    shutil.copyfile(script, output / "reviewed-script.json")
    (output / "ATTRIBUTION.txt").write_text(CREDIT + "\nhttps://viddyscribe.com\n", encoding="utf-8")
    write_json(output / "pack.json", pack)
    return pack


def checked_rows(manifest):
    if manifest.get("version") != 1 or not manifest.get("movies"):
        raise ValueError("invalid pack manifest")
    names = set()
    for row in manifest["movies"]:
        name = row.get("fileName")
        if name not in NAMES or name in names or row.get("narration") is not True:
            raise ValueError("invalid pack movie")
        names.add(name)
        if any(not re.fullmatch(r"[a-fA-F0-9]{64}", row.get(key, "")) for key in ("sha256", "sourceSha256")):
            raise ValueError("invalid pack hash")
    return manifest["movies"]


def child(root, relative):
    result = (root / relative).resolve()
    if not result.is_relative_to(root.resolve()):
        raise ValueError("path escapes intended game or pack directory")
    return result


def replace_from(source, destination):
    temporary = destination.with_name(destination.name + ".ct-ad-" + uuid.uuid4().hex)
    try:
        shutil.copyfile(source, temporary)
        if sha(temporary) != sha(source):
            raise OSError("copied movie hash mismatch")
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)


def install(pack_dir, game):
    pack_dir, game = pack_dir.resolve(), game.resolve()
    manifest = read_json(pack_dir / "pack.json")
    rows = checked_rows(manifest)
    state = child(game, "Accessibility/AudioDescriptions")
    installed = state / "installed-movies.json"
    if installed.exists():
        raise ValueError("a narration pack is already installed; restore it before replacing it")
    # Check every file before the first mutation; never overwrite unknown source movies.
    for row in rows:
        if sha(child(pack_dir, row["fileName"])) != row["sha256"]:
            raise ValueError(f"{row['fileName']}: pack hash mismatch")
        if sha(child(game, row["fileName"])) != row["sourceSha256"]:
            raise ValueError(f"{row['fileName']}: source hash mismatch")
    backup = child(state, "backups/" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:8])
    backup.mkdir(parents=True)
    for row in rows:
        shutil.copyfile(game / row["fileName"], backup / row["fileName"])
        if sha(backup / row["fileName"]) != row["sourceSha256"]:
            raise OSError("backup verification failed")
    manifest["backupDirectory"] = str(backup)
    write_json(backup / "install-journal.json", manifest)
    changed = []
    try:
        for row in rows:
            replace_from(pack_dir / row["fileName"], game / row["fileName"])
            changed.append(row)
        write_json(installed.with_suffix(".pending"), manifest)
        os.replace(installed.with_suffix(".pending"), installed)
    except BaseException:
        for row in reversed(changed):
            replace_from(backup / row["fileName"], game / row["fileName"])
        raise
    for name in ("ATTRIBUTION.txt", "reviewed-script.json"):
        if (pack_dir / name).is_file():
            shutil.copyfile(pack_dir / name, state / name)
    return manifest


def restore(game):
    game = game.resolve()
    state = child(game, "Accessibility/AudioDescriptions")
    manifest = read_json(state / "installed-movies.json")
    rows = checked_rows(manifest)
    backup = Path(manifest["backupDirectory"]).resolve()
    if not backup.is_relative_to(child(state, "backups")):
        raise ValueError("backup is outside intended backup directory")
    for row in rows:
        if sha(child(game, row["fileName"])) not in (row["sha256"], row["sourceSha256"]):
            raise ValueError("installed movie changed; refusing to overwrite it")
        if sha(child(backup, row["fileName"])) != row["sourceSha256"]:
            raise ValueError("original backup changed")
    narrated = backup / ("narrated-" + uuid.uuid4().hex[:8])
    narrated.mkdir()
    for row in rows:
        shutil.copyfile(game / row["fileName"], narrated / row["fileName"])
    changed = []
    try:
        for row in rows:
            # Steam verification may already have restored some or all originals.
            if sha(game / row["fileName"]) == row["sourceSha256"]:
                continue
            replace_from(backup / row["fileName"], game / row["fileName"])
            changed.append(row)
        os.replace(state / "installed-movies.json", narrated / "installed-movies.json")
    except BaseException:
        for row in reversed(changed):
            replace_from(narrated / row["fileName"], game / row["fileName"])
        raise


def require_closed_game():
    if os.name == "nt":
        result = run(["powershell", "-NoProfile", "-Command",
                      "@(Get-Process -Name 'Chrono Trigger' -ErrorAction SilentlyContinue).Count"], text=True)
        if result.stdout.strip() != "0":
            raise ValueError("close Chrono Trigger normally before changing its movie files")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    command = commands.add_parser("build")
    for arg in ("script", "sources", "voices", "output"):
        command.add_argument("--" + arg, type=Path, required=True)
    command.add_argument("--approvals", type=Path)
    command = commands.add_parser("install")
    command.add_argument("--pack", type=Path, required=True)
    command.add_argument("--game", type=Path, required=True)
    command = commands.add_parser("restore")
    command.add_argument("--game", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "build":
        build(args.script, args.sources, args.voices, args.output, args.approvals)
    else:
        require_closed_game()
        if args.command == "install":
            print(json.dumps(install(args.pack, args.game), indent=2))
        else:
            restore(args.game)
            print("Original movie files restored; backups retained.")


if __name__ == "__main__":
    main()
