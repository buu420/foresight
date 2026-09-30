import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("release_package", REPO / "tools/Package-Release.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)

class ReleasePackageTests(unittest.TestCase):
    def test_paths_cannot_escape_package_on_windows(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ("../secret", "a/../../secret", "C:/secret", "//server/share", "a\\..\\..\\secret", "a:stream"):
                with self.subTest(name=name), self.assertRaises(ValueError):
                    release.safe_member(root, name)
            self.assertEqual(root / "nested/file.txt", release.safe_member(root, "nested/file.txt"))

    def test_unlisted_private_files_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data = b"reviewed mod"
            (root / "mod.dll").write_bytes(data)
            (root / "SHA256SUMS.txt").write_text(hashlib.sha256(data).hexdigest() + "  mod.dll\n")
            release.verify_mod_package(root)
            (root / "private-recording.wav").write_bytes(b"must not publish")
            with self.assertRaisesRegex(ValueError, "outside"):
                release.verify_mod_package(root)

    def test_modified_and_duplicate_payloads_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "mod.dll").write_bytes(b"reviewed")
            line = hashlib.sha256(b"reviewed").hexdigest() + "  mod.dll\n"
            (root / "SHA256SUMS.txt").write_text(line + line)
            with self.assertRaisesRegex(ValueError, "duplicate"):
                release.verify_mod_package(root)
            (root / "SHA256SUMS.txt").write_text(line)
            (root / "mod.dll").write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "mismatch"):
                release.verify_mod_package(root)

    def test_runtime_mismatch_is_rejected_before_staging(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "runtime.zip"
            runtime.write_bytes(b"wrong runtime")
            output = root / "release"
            with self.assertRaisesRegex(ValueError, "SHA-512"):
                release.stage(REPO, runtime, output)
            self.assertFalse(output.exists())

if __name__ == "__main__":
    unittest.main()
