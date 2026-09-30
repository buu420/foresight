"""Build the standalone Foresight beta ZIP from audited, explicit inputs."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import zipfile

RUNTIME_URL = "https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.20/dotnet-runtime-9.0.20-win-x86.zip"
RUNTIME_SHA512 = "c9679d5606604ff2970064d6ab9470b3b50e14ceb0a5efa67b3ce9b9e3b24d9edda2af102ac99623a8a0c3b83b645608027ec36483d928c917e83d2d00a60811"

def digest(path: Path, algorithm: str = "sha256") -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, algorithm).hexdigest()

def safe_member(root: Path, name: str) -> Path:
    normalized = name.replace("\\", "/")
    parts = PurePosixPath(normalized)
    if not normalized or parts.is_absolute() or ".." in parts.parts or ":" in normalized:
        raise ValueError(f"Unsafe package path: {name}")
    target = root.joinpath(*parts.parts).resolve()
    if not target.is_relative_to(root.resolve()):
        raise ValueError(f"Package path leaves its root: {name}")
    return target

def verify_mod_package(package: Path) -> None:
    expected = set()
    for line in (package / "SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines():
        hash_value, name = line.split("  ", 1)
        path = safe_member(package, name)
        if path in expected or digest(path) != hash_value.lower():
            raise ValueError(f"Mod package checksum mismatch or duplicate: {name}")
        expected.add(path)
    actual = {p.resolve() for p in package.rglob("*") if p.is_file() and p.name != "SHA256SUMS.txt"}
    if actual != expected:
        raise ValueError("The mod package contains files outside its checksum manifest")
    if not actual:
        raise ValueError("Empty mod package")

def stage(repo: Path, runtime_zip: Path, output: Path) -> Path:
    if output.exists():
        raise ValueError("Release output already exists; use a fresh directory")
    if digest(runtime_zip, "sha512") != RUNTIME_SHA512:
        raise ValueError("Official x86 runtime SHA-512 does not match")
    package = repo / "artifacts/package/chrono.trigger.accessibility"
    verify_mod_package(package)
    config = json.loads((package / "ModConfig.json").read_text(encoding="utf-8"))
    if config["ModId"] != "chrono.trigger.accessibility" or config["ModName"] != "Foresight (Beta)":
        raise ValueError("Unexpected public mod identity")
    version = config["ModVersion"]
    payload = output / "payload"
    payload.mkdir(parents=True)
    upstream = repo / "native/reloaded-ii/v1.30.3"
    shutil.copytree(upstream / "Loader/X86", payload / "Reloaded-II/Loader/X86")
    shutil.copytree(upstream / "mods/reloaded.sharedlib.hooks", payload / "Reloaded-II/Mods/reloaded.sharedlib.hooks")
    shutil.copytree(package, payload / "Reloaded-II/Mods/chrono.trigger.accessibility")
    launcher = payload / "Accessibility/Bootstrap"
    launcher.mkdir(parents=True)
    for name in ("Foresight.Bootstrap.exe",):
        shutil.copy2(repo / ".build/native" / name, launcher / name)
    shutil.copy2(repo / ".build/native/winmm.dll", payload / "winmm.dll")
    runtime = payload / "Accessibility/Runtime/dotnet/x86"
    with zipfile.ZipFile(runtime_zip) as archive:
        for entry in archive.infolist():
            target = safe_member(runtime, entry.filename)
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry) as source, target.open("wb") as destination:
                    shutil.copyfileobj(source, destination)
    for required in ("dotnet.exe", "host/fxr/9.0.20/hostfxr.dll", "shared/Microsoft.NETCore.App/9.0.20/coreclr.dll", "LICENSE.txt", "ThirdPartyNotices.txt"):
        if not (runtime / required).is_file():
            raise ValueError(f"Runtime dependency is missing: {required}")
    support = payload / "Accessibility/Foresight"
    support.mkdir(parents=True)
    shutil.copy2(repo / "tools/release/Setup-Foresight.ps1", support)
    shutil.copy2(repo / "tools/release/Remove-LegacyRegistration.ps1", support)
    for name in ("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md"):
        shutil.copy2(repo / name, support / name)
    shutil.copy2(upstream / "SOURCE.md", support / "Reloaded-SOURCE.md")
    (support / "Runtime-SOURCE.json").write_text(json.dumps({"url": RUNTIME_URL, "sha512": RUNTIME_SHA512}, indent=2) + "\n", encoding="utf-8")
    # Keep package documentation links useful without copying private research.
    readme = (repo / "README.md").read_text(encoding="utf-8").replace("](docs/", "](https://github.com/buu420/foresight/blob/main/docs/")
    (payload / "Foresight-README.md").write_text(readme, encoding="utf-8", newline="\n")
    for mode in ("Install", "Uninstall"):
        command = ('@echo off\r\n'
                   'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Accessibility\\Foresight\\Setup-Foresight.ps1" -Mode ' + mode + '\r\n'
                   'set "FORESIGHT_RESULT=%ERRORLEVEL%"\r\n'
                   'pause\r\n'
                   'exit /b %FORESIGHT_RESULT%\r\n')
        (payload / f"{mode} Foresight.cmd").write_bytes(command.encode("ascii"))
    forbidden = {".dat", ".mp4", ".mp3", ".pdb", ".log"}
    for path in payload.rglob("*"):
        if path.is_file() and (path.suffix.lower() in forbidden or path.name.lower() == "chrono trigger.exe"):
            raise ValueError(f"Private/game/development file in release: {path.relative_to(payload)}")
    lines = [f"{digest(p).upper()}  {p.relative_to(payload).as_posix()}" for p in sorted(payload.rglob("*")) if p.is_file()]
    (payload / "Foresight-SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    zip_path = output / f"Foresight-v{version}-beta-win-x86.zip"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(payload.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(payload).as_posix())
    (output / "SHA256SUMS.txt").write_text(f"{digest(zip_path).upper()}  {zip_path.name}\n", encoding="ascii")
    return zip_path

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--runtime-zip", type=Path, required=True, help=f"Official verified download: {RUNTIME_URL}")
    parser.add_argument("--output", type=Path, required=True, help="Fresh staging directory; existing directories are never removed")
    args = parser.parse_args()
    print(stage(args.repo.resolve(), args.runtime_zip.resolve(), args.output.resolve()))
