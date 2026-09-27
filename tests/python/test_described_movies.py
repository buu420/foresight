"""Movie pack integrity and recovery must not depend on having the game installed."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

MODULE = Path(__file__).resolve().parents[2] / "tools" / "Build-DescribedMovies.py"
spec = importlib.util.spec_from_file_location("described_movies", MODULE)
movies = importlib.util.module_from_spec(spec)
spec.loader.exec_module(movies)


class MoviePackTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.game = self.root / "game"
        self.pack = self.root / "pack"
        self.game.mkdir()
        self.pack.mkdir()
        self.rows = []
        for name in ("001.dat", "002.dat"):
            (self.game / name).write_bytes(b"original " + name.encode())
            (self.pack / name).write_bytes(b"narrated " + name.encode())
            self.rows.append({"fileName": name, "sourceSha256": movies.sha(self.game / name),
                              "sha256": movies.sha(self.pack / name), "narration": True})
        (self.pack / "pack.json").write_text(json.dumps({"version": 1, "movies": self.rows}))

    def tearDown(self):
        self.temp.cleanup()

    def test_xor_roundtrip_across_non_aligned_chunks(self):
        source = self.root / "input.mp4"
        source.write_bytes(bytes(range(256)) * 1031)
        encoded, decoded = self.root / "encoded.dat", self.root / "decoded.mp4"
        movies.transform(source, encoded, chunk_size=257)
        self.assertEqual(encoded.read_bytes()[:4], bytes([255, 255, 255, 255]))
        movies.transform(encoded, decoded, chunk_size=333)
        self.assertEqual(source.read_bytes(), decoded.read_bytes())

    def test_unknown_original_prevents_any_install(self):
        before = (self.game / "001.dat").read_bytes()
        (self.game / "002.dat").write_bytes(b"unexpected edition")
        with self.assertRaisesRegex(ValueError, "source"):
            movies.install(self.pack, self.game)
        self.assertEqual(before, (self.game / "001.dat").read_bytes())
        self.assertFalse((self.game / "Accessibility/AudioDescriptions/installed-movies.json").exists())

    def test_modified_pack_cannot_be_installed(self):
        (self.pack / "002.dat").write_bytes(b"corrupted")
        with self.assertRaisesRegex(ValueError, "pack"):
            movies.install(self.pack, self.game)
        self.assertEqual(movies.sha(self.game / "001.dat"), self.rows[0]["sourceSha256"])

    def test_failed_second_replacement_restores_first_and_keeps_backups(self):
        original = movies.replace_from
        failed = False
        def fail_once(source, destination):
            nonlocal failed
            if source.parent == self.pack and destination.name == "002.dat" and not failed:
                failed = True
                raise OSError("simulated locked movie")
            return original(source, destination)
        with patch.object(movies, "replace_from", side_effect=fail_once):
            with self.assertRaisesRegex(OSError, "locked"):
                movies.install(self.pack, self.game)
        for row in self.rows:
            self.assertEqual(movies.sha(self.game / row["fileName"]), row["sourceSha256"])
        self.assertTrue(list((self.game / "Accessibility/AudioDescriptions/backups").glob("*/001.dat")))

    def test_install_and_restore_are_hash_guarded(self):
        manifest = movies.install(self.pack, self.game)
        self.assertTrue(all(movies.sha(self.game / r["fileName"]) == r["sha256"] for r in self.rows))
        (self.game / "002.dat").write_bytes(b"user edit")
        with self.assertRaisesRegex(ValueError, "changed"):
            movies.restore(self.game)
        self.assertEqual(movies.sha(self.game / "001.dat"), self.rows[0]["sha256"])
        (self.game / "002.dat").write_bytes((self.pack / "002.dat").read_bytes())
        movies.restore(self.game)
        self.assertTrue(all(movies.sha(self.game / r["fileName"]) == r["sourceSha256"] for r in self.rows))
        self.assertTrue(Path(manifest["backupDirectory"]).is_dir())

    def test_invalid_or_overlapping_cue_is_rejected(self):
        good = [{"id": "a", "text": "Hello.", "start": 0, "end": 1},
                {"id": "b", "text": "Bye.", "start": 2, "end": 3}]
        movies.validate_cues(good, 4)
        for change in ({"end": 5}, {"start": .5}, {"start": float('nan')}, {"id": "../outside"}):
            cues = [good[0], good[1] | change]
            with self.assertRaises(ValueError):
                movies.validate_cues(cues, 4)

    def test_separate_pack_requires_both_original_streams_verified_and_decoded_hash(self):
        manifest = {"version": 2, "playback": "simultaneous-separate-tracks", "movies": [
            self.rows[0] | {"decodedSha256": "a" * 64, "originalAudioVerified": True,
                            "videoVerified": True, "audioTrackCount": 2}]}
        self.assertEqual(len(movies.checked_rows(manifest)), 1)
        for change in ({"originalAudioVerified": False}, {"decodedSha256": "bad"},
                       {"audioTrackCount": 1}, {"videoVerified": False}):
            with self.assertRaises(ValueError):
                movies.checked_rows(manifest | {"movies": [manifest["movies"][0] | change]})

    def test_render_preserves_original_audio_packets_and_adds_a_separate_voice_track(self):
        import numpy as np
        import soundfile as sf
        source, output = self.root / "source.mp4", self.root / "output.mp4"
        movies.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=32x32:r=24:d=4",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                    "-map", "1:a", "-map", "0:v", "-c:v", "libx264", "-c:a", "aac", "-movie_timescale", "10000", source])
        wave = self.pack / "test-voice.wav"
        sf.write(wave, .1 * np.sin(np.arange(12000) * 2 * np.pi * 660 / 24000), 24000, subtype="PCM_24")
        movies.write_json(wave.with_suffix(".json"), {"text": "Test narration.", "wave_sha256": movies.sha(wave),
                                                    "checks": {"failures": []}})
        film = {"duration_seconds": 4, "decoded_sha256": movies.sha(source),
                "cues": [{"id": "test-voice", "text": "Test narration.", "start": 1, "end": 2}]}
        movies.mix_film(film, source, self.pack, output, {})
        streams = movies.probe(output)["streams"]
        self.assertEqual(sum(s["codec_type"] == "audio" for s in streams), 2)
        def packet_hash(path):
            return movies.run(["ffmpeg", "-v", "error", "-i", path, "-map", "0:a:0", "-c", "copy",
                               "-f", "hash", "-hash", "sha256", "-"], text=True).stdout
        self.assertEqual(packet_hash(source), packet_hash(output))
        voice, rate = sf.read(output.with_suffix(".wav"))
        self.assertEqual(rate, 48000)
        self.assertTrue(np.all(voice[:48000] == 0))
        self.assertGreater(np.max(np.abs(voice[48000:72000])), .01)
        self.assertTrue(np.all(voice[72000:] == 0))

    def test_separate_pack_requires_compatible_installed_mod_before_any_movie_replacement(self):
        for row in self.rows:
            row.update(decodedSha256="a" * 64, originalAudioVerified=True, videoVerified=True, audioTrackCount=2)
        movies.write_json(self.pack / "pack.json", {"version": 2, "playback": "simultaneous-separate-tracks", "movies": self.rows})
        with self.assertRaisesRegex(ValueError, "0.3.37"):
            movies.install(self.pack, self.game)
        manifest = self.game / "Reloaded-II/Mods/chrono.trigger.accessibility/ModConfig.json"
        manifest.parent.mkdir(parents=True)
        movies.write_json(manifest, {"ModVersion": "0.3.36"})
        with self.assertRaisesRegex(ValueError, "0.3.37"):
            movies.install(self.pack, self.game)
        self.assertEqual(movies.sha(self.game / "001.dat"), self.rows[0]["sourceSha256"])
        movies.write_json(manifest, {"ModVersion": "0.3.37"})
        movies.install(self.pack, self.game)
        movies.restore(self.game)
        self.assertEqual(movies.sha(self.game / "001.dat"), self.rows[0]["sourceSha256"])

    def test_steam_restored_originals_can_be_restored_and_reinstalled(self):
        for restored_count in (1, 2):
            manifest = movies.install(self.pack, self.game)
            backup = Path(manifest["backupDirectory"])
            for row in self.rows[:restored_count]:
                (self.game / row["fileName"]).write_bytes((backup / row["fileName"]).read_bytes())
            movies.restore(self.game)
            for row in self.rows:
                self.assertEqual(movies.sha(self.game / row["fileName"]), row["sourceSha256"])
            self.assertFalse((self.game / "Accessibility/AudioDescriptions/installed-movies.json").exists())


if __name__ == "__main__":
    unittest.main()
