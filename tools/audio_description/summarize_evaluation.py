"""Compare voice profiles rendered by render_cues.py on the same cues (local files only).

    %VPY% tools\\audio_description\\summarize_evaluation.py <evaluation dir> [profile dir names...]

Reads <evaluation dir>/<profile>/<cue>.json and writes <evaluation dir>/comparison.json plus a
Markdown table (comparison.md). "Prior band" is the approved FFVII natural-cadence v5 range:
3.52-5.26 mean syllables per second within speech, internal two-second rate ratio 1.09-1.60.
Speaker cosines come from the Qwen3-TTS speaker encoder, the same encoder that conditions
generation; they rank candidates against the user's real recordings and are not an independent
speaker-verification result.
"""

from __future__ import annotations

import json
import statistics
import sys
from pathlib import Path

PRIOR_RATE = (3.52, 5.26)
PRIOR_RATIO = 1.60


def mean(values):
    values = [v for v in values if v is not None]
    return round(statistics.mean(values), 3) if values else None


def main(argv: list[str]) -> int:
    root = Path(argv[0])
    names = argv[1:] or sorted(p.name for p in root.iterdir() if p.is_dir())
    rows = []
    for name in names:
        records = [json.loads(p.read_text(encoding="utf-8")) for p in sorted((root / name).glob("*.json"))
                   if p.name not in ("manifest.json", "check-report.json")]
        if not records:
            continue
        chosen = [r["checks"] for r in records]
        takes = [t for r in records for t in r["takes"]]
        first = [r["takes"][0]["checks"] for r in records]
        repeats = [r.get("repeat_check") for r in records if r.get("repeat_check")]
        rows.append({
            "profile": name,
            "cues": len(records),
            "first_take_exact": sum(1 for c in first if c.get("exact")),
            "promoted_exact": sum(1 for c in chosen if c.get("exact")),
            "takes_rendered": len(takes),
            "all_takes_exact_rate": round(sum(1 for t in takes if t["checks"].get("exact")) / len(takes), 3),
            "mean_syllables_per_second_windows": mean([c.get("rate_windows", {}).get("mean_syllables_per_second")
                                                       for c in chosen]),
            "mean_internal_rate_ratio": mean([c.get("rate_windows", {}).get("internal_rate_ratio") for c in chosen]),
            "within_prior_rate_band": sum(1 for c in chosen if c.get("rate_windows") and
                                          PRIOR_RATE[0] <= c["rate_windows"]["mean_syllables_per_second"] <= PRIOR_RATE[1]),
            "syllables_per_audible_second": mean([c["syllables_per_audible_second"] for c in chosen]),
            "pitch_median_hz": mean([c.get("pitch_median_hz") for c in chosen]),
            "pitch_span_semitones": mean([c.get("pitch_span_semitones_p10_p90") for c in chosen]),
            "lead_silence_seconds": mean([c["lead_silence_seconds"] for c in chosen]),
            "tail_silence_seconds": mean([c["tail_silence_seconds"] for c in chosen]),
            "speaker_cosine_to_reference": mean([c["speaker_cosine_to_reference"] for c in chosen]),
            "speaker_cosine_to_real_recordings": mean([mean(c.get("speaker_cosine_to_anchors", [])) for c in chosen]),
            "repeat_identical": sum(1 for r in repeats if r["same_length"] and r["max_abs_difference"] == 0),
            "repeat_checked": len(repeats),
            "repeat_max_abs_difference": max((r["max_abs_difference"] or 0) for r in repeats) if repeats else None,
            "mean_generation_seconds": mean([t["generation_seconds"] for t in takes]),
        })
    (root / "comparison.json").write_text(json.dumps(rows, indent=1) + "\n", encoding="utf-8")
    columns = ["profile", "first_take_exact", "promoted_exact", "all_takes_exact_rate",
               "mean_syllables_per_second_windows", "mean_internal_rate_ratio", "within_prior_rate_band",
               "pitch_median_hz", "pitch_span_semitones", "speaker_cosine_to_real_recordings",
               "speaker_cosine_to_reference", "repeat_identical", "mean_generation_seconds"]
    lines = ["| " + " | ".join(columns) + " |", "|" + "---|" * len(columns)]
    for row in rows:
        lines.append("| " + " | ".join(str(row[c]) for c in columns) + " |")
    (root / "comparison.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
