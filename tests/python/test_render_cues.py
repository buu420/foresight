"""render_cues.py: --x-vector-only and asr_words, without loading any model or touching the GPU."""
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import MagicMock, patch

MODULE = Path(__file__).resolve().parents[2] / "tools" / "audio_description" / "render_cues.py"
spec = importlib.util.spec_from_file_location("render_cues", MODULE)
rc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rc)
DEFAULT_PARAMS = copy.deepcopy(rc.PARAMS)


class RenderCuesTests(unittest.TestCase):
    def setUp(self):
        rc.PARAMS.clear()
        rc.PARAMS.update(copy.deepcopy(DEFAULT_PARAMS))
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / "reference.wav").write_bytes(b"not decoded by a dry run")
        self.profile_path = self.root / "profile.json"
        self.profile_path.write_text(json.dumps({"reference_wav": "reference.wav",
                                                 "reference_text": "Hello there."}), encoding="utf-8")
        self.script = self.root / "cues.json"
        self.script.write_text(json.dumps({"cues": [{"id": "c1", "text": "A beam shoots skyward.",
                                                     "start": 1.0, "end": 4.0}]}), encoding="utf-8")
        self.out = self.root / "out"

    def tearDown(self):
        self.temp.cleanup()
        rc.PARAMS.clear()
        rc.PARAMS.update(copy.deepcopy(DEFAULT_PARAMS))

    def dry_run(self, *extra):
        stdout = io.StringIO()
        with patch.object(rc, "gpu_preflight", return_value={"ok": True}), contextlib.redirect_stdout(stdout):
            code = rc.main(["--script", str(self.script), "--profile", str(self.profile_path),
                            "--out", str(self.out), "--dry-run", *extra])
        self.assertEqual(code, 0)
        return json.loads(stdout.getvalue().splitlines()[0])

    def test_default_fingerprint_is_unchanged_and_x_vector_changes_it(self):
        profile = rc.load_profile(self.profile_path)
        legacy = rc.sha_text(json.dumps({
            "model": rc.MODEL_SNAPSHOT.name, "reference_sha256": profile["reference_sha256"],
            "reference_text_sha256": rc.sha_text(profile["reference_text"]), "params": rc.PARAMS,
            "sample_rate": rc.SAMPLE_RATE, "subtype": rc.SUBTYPE, "script_version": rc.SCRIPT_VERSION,
            "x_vector_only_mode": False, "dtype": "bfloat16", "attn": "sdpa"}, sort_keys=True))
        self.assertEqual(rc.fingerprint(profile), legacy)
        self.assertEqual(rc.fingerprint(profile, False), legacy)
        self.assertNotEqual(rc.fingerprint(profile, True), legacy)

    def test_dry_run_plan_records_mode_and_fingerprint(self):
        profile = rc.load_profile(self.profile_path)
        default = self.dry_run()
        self.assertFalse(default["x_vector_only_mode"])
        self.assertEqual(default["fingerprint"], rc.fingerprint(profile))
        xvec = self.dry_run("--x-vector-only")
        self.assertTrue(xvec["x_vector_only_mode"])
        self.assertEqual(xvec["fingerprint"], rc.fingerprint(profile, True))

    def write_checkpoint(self, x_vector_only):
        profile = rc.load_profile(self.profile_path)
        cue = rc.load_script(self.script, "")[0]
        self.out.mkdir(exist_ok=True)
        wave = self.out / "c1.wav"
        wave.write_bytes(b"take")
        record = {"id": "c1", "fingerprint": rc.fingerprint(profile, x_vector_only),
                  "text_sha256": cue["text_sha256"], "render_text": cue["text"], "level": None,
                  "settled_endings": False, "ending_fallback": "none", "wave_sha256": rc.sha_bytes(wave)}
        if x_vector_only is not None:
            record["x_vector_only_mode"] = x_vector_only
        (self.out / "c1.json").write_text(json.dumps(record), encoding="utf-8")

    def test_icl_checkpoint_is_reused_by_default_but_not_for_x_vector(self):
        self.write_checkpoint(False)
        self.assertEqual(self.dry_run()["pending"], [])
        self.assertEqual(self.dry_run("--x-vector-only")["pending"], ["c1"])

    def test_checkpoint_from_before_the_flag_existed_is_still_reused(self):
        self.write_checkpoint(None)  # older records carry no x_vector_only_mode key
        self.assertEqual(self.dry_run()["pending"], [])

    def test_x_vector_checkpoint_is_reused_only_with_the_flag(self):
        self.write_checkpoint(True)
        self.assertEqual(self.dry_run("--x-vector-only")["pending"], [])
        self.assertEqual(self.dry_run()["pending"], ["c1"])

    def test_renderer_passes_x_vector_mode_to_the_prompt(self):
        model = MagicMock()
        fake_qwen = types.SimpleNamespace(Qwen3TTSModel=MagicMock(from_pretrained=MagicMock(return_value=model)))
        fake_torch = types.SimpleNamespace(set_num_threads=lambda n: None, bfloat16="bf16")
        profile = {"reference_wav": "ref.wav", "reference_text": "Hello there.", "identity_anchors": []}
        with patch.dict(sys.modules, {"qwen_tts": fake_qwen, "torch": fake_torch}), \
                patch.object(rc.Renderer, "embedding_of_file", return_value=None), \
                patch.object(rc, "Syllables", return_value=None):
            for flag in (False, True):
                model.create_voice_clone_prompt.reset_mock()
                renderer = rc.Renderer(profile, "none", "cpu", None, x_vector_only=flag)
                self.assertEqual(renderer.x_vector_only, flag)
                kwargs = model.create_voice_clone_prompt.call_args.kwargs
                self.assertIs(kwargs["x_vector_only_mode"], flag)
                self.assertEqual(kwargs["ref_text"], "Hello there.")
            model.create_voice_clone_prompt.reset_mock()
            rc.Renderer(profile, "none", "cpu", None)
            self.assertIs(model.create_voice_clone_prompt.call_args.kwargs["x_vector_only_mode"], False)

    def test_check_exposes_the_asr_word_timings_it_used(self):
        import numpy as np
        word = lambda w, s, e: types.SimpleNamespace(word=w, start=s, end=e)
        segment = types.SimpleNamespace(text=" A beam shoots skyward.",
                                        words=[word(" A", 0.1, 0.2), word(" beam", 0.2, 0.51234),
                                               word(" shoots", 0.52, 0.9), word(" skyward.", 0.95, 1.6)])
        renderer = rc.Renderer.__new__(rc.Renderer)
        renderer.syllables = rc.Syllables()
        renderer.glossary = None
        renderer.asr = MagicMock()
        renderer.asr.transcribe.return_value = ([segment], None)
        renderer.reference_embedding = None
        renderer.anchor_embeddings = []
        audio = (0.1 * np.sin(2 * np.pi * 120 * np.arange(rc.SAMPLE_RATE * 2) / rc.SAMPLE_RATE)).astype("float32")
        with patch.object(rc.Renderer, "embedding", return_value=None), \
                patch.object(rc.Renderer, "cosine", return_value=0.98), \
                patch.object(rc, "terminal_contour", return_value=None):
            checks = renderer.check(audio, "A beam shoots skyward.", 3.0, None)
        self.assertTrue(checks["exact"])
        self.assertEqual(checks["asr_words"], [
            {"word": "A", "start": 0.1, "end": 0.2}, {"word": "beam", "start": 0.2, "end": 0.512},
            {"word": "shoots", "start": 0.52, "end": 0.9}, {"word": "skyward.", "start": 0.95, "end": 1.6}])
        json.dumps(checks)  # the record must stay JSON-serialisable
        self.assertEqual(renderer.asr.transcribe.call_count, 1)


if __name__ == "__main__":
    unittest.main()
