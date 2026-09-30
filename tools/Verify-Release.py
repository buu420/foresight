"""Check a Foresight ZIP using a temporary game copy, without registering it."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

spec = importlib.util.spec_from_file_location("release_package", Path(__file__).with_name("Package-Release.py"))
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)

def verify(zip_path: Path, game_exe: Path) -> dict:
    report = {}
    with zipfile.ZipFile(zip_path) as archive:
        names = archive.namelist()
        assert len(set(names)) == len(names), "Duplicate archive entries"
        report["archive_files"] = len(names)
        sums = archive.read("Foresight-SHA256SUMS.txt").decode().splitlines()
        declared = set()
        for line in sums:
            hash_value, name = line.split("  ", 1)
            assert hashlib.sha256(archive.read(name)).hexdigest() == hash_value.lower(), name
            assert name not in declared, name
            declared.add(name)
        assert declared | {"Foresight-SHA256SUMS.txt"} == set(names)
        assert not any(n.endswith((".dat", ".mp4", ".mp3", ".log", ".pdb")) for n in names)
        for name in ("Accessibility/Launcher/ChronoTriggerAccessibility.Launcher.exe", "Accessibility/Launcher/ChronoTriggerAccessibility.Installer.exe"):
            data = archive.read(name)
            offset = int.from_bytes(data[60:64], "little")
            assert int.from_bytes(data[offset+4:offset+6], "little") == 0x14c
            assert b"requestedExecutionLevel" in data
            if "Installer.exe" in name:
                assert b"requireAdministrator" in data
        report["all_payload_hashes_match"] = True
        report["native_x86_and_embedded_uac_manifests"] = True
        with tempfile.TemporaryDirectory(prefix="foresight-release-check-") as directory:
            root = Path(directory)
            for name in names:
                target = package.safe_member(root, name)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(archive.read(name))
            script = root / "Accessibility/Foresight/Setup-Foresight.ps1"
            command = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script), "-Mode", "Verify"]
            result = subprocess.run(command, capture_output=True, text=True, timeout=60)
            assert result.returncode != 0 and "Chrono Trigger.exe" in result.stderr, result
            shutil.copy2(game_exe, root / "Chrono Trigger.exe")
            result = subprocess.run(command, capture_output=True, text=True, timeout=60)
            assert result.returncode == 0, (result.returncode, result.stdout, result.stderr)
            report["clean_payload_verify"] = result.stdout.strip()
            runtime = root / "Accessibility/Runtime/dotnet/x86/dotnet.exe"
            result = subprocess.run([str(runtime), "--list-runtimes"], capture_output=True, text=True, timeout=20)
            assert result.returncode == 0 and "Microsoft.NETCore.App 9.0.20" in result.stdout, result
            report["private_x86_runtime_launches"] = True
            (root / "Reloaded-II/Mods/chrono.trigger.accessibility/ChronoTriggerAccessibility.Mod.dll").write_bytes(b"changed")
            result = subprocess.run(command, capture_output=True, text=True, timeout=60)
            assert result.returncode != 0 and "changed" in result.stderr, result
            report["changed_payload_rejected_before_install"] = True
    report["zip_sha256"] = package.digest(zip_path)
    return report

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", required=True, type=Path)
    parser.add_argument("--game-exe", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.zip, args.game_exe), indent=2))
