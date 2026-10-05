#!/usr/bin/env python3
"""Build only the pinned, conditional OpenCL Int64 atomic validator."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command, directory, log):
    with log.open("w", encoding="utf-8") as stream:
        subprocess.run(command, cwd=directory, stdout=stream, stderr=subprocess.STDOUT,
                       check=True, timeout=1800)


def extract(archive, destination):
    destination.mkdir()
    with tarfile.open(archive, "r:gz") as source:
        source.extractall(destination, filter="data")
    roots = list(destination.iterdir())
    if len(roots) != 1 or not roots[0].is_dir():
        raise RuntimeError("The locked archive must have one source root.")
    return roots[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--cmake", default="cmake")
    parser.add_argument("--archives-directory")
    args = parser.parse_args()
    recipe = Path(__file__).resolve().parent
    lock = json.loads((recipe / "validator.lock.json").read_text(encoding="utf-8"))
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    archives = output / "archives"
    archives.mkdir()
    for entry in lock["archives"]:
        archive = archives / entry["name"]
        if args.archives_directory:
            shutil.copyfile(Path(args.archives_directory) / entry["name"], archive)
        else:
            with urllib.request.urlopen(entry["url"], timeout=60) as response, archive.open("wb") as target:
                shutil.copyfileobj(response, target)
        if digest(archive) != entry["sha256"]:
            raise RuntimeError("Locked archive SHA256 mismatch: " + entry["name"])
    source = extract(archives / lock["archives"][0]["name"], output / "tools-extracted")
    headers = extract(archives / lock["archives"][1]["name"], output / "headers-extracted")
    shutil.move(str(headers), source / "external" / "spirv-headers")
    if digest(source / "DEPS") != lock["depsSha256"]:
        raise RuntimeError("Pinned upstream DEPS mismatch.")
    patch = recipe / "conditional-int64-atomic-profile.patch"
    if digest(patch) != lock["patchSha256"]:
        raise RuntimeError("Conditional validator patch identity mismatch.")
    for entry in lock["files"]:
        if digest(source / entry["path"]) != entry["baselineSha256"]:
            raise RuntimeError("Unexpected upstream source: " + entry["path"])
    run(["patch", "--batch", "--forward", "-p1", "--input", str(patch)], source, output / "patch.log")
    for entry in lock["files"]:
        if digest(source / entry["path"]) != entry["sha256"]:
            raise RuntimeError("Patched source mismatch: " + entry["path"])
    build = output / "build"
    run([args.cmake, "-S", str(source), "-B", str(build), "-DCMAKE_BUILD_TYPE=Release",
         "-DSPIRV_SKIP_TESTS=ON", "-DSPIRV_WERROR=ON"], output, output / "configure.log")
    run([args.cmake, "--build", str(build), "--target", "spirv-val", "--parallel", "1"],
        output, output / "build.log")
    name = "spirv-val.exe" if (build / "tools" / "spirv-val.exe").exists() else "spirv-val"
    target_name = lock["executable"] + (".exe" if name.endswith(".exe") else "")
    binary = output / target_name
    shutil.copy2(build / "tools" / name, binary)
    version = subprocess.run([str(binary), "--version"], capture_output=True, text=True, check=True, timeout=20)
    if lock["identity"] + ";" not in version.stdout:
        raise RuntimeError("The compiled validator lost its required version identity.")
    (output / "artifact.json").write_text(json.dumps({"identity": lock["identity"], "executable": str(binary),
        "binarySha256": digest(binary), "lockSha256": digest(recipe / "validator.lock.json"),
        "patchSha256": digest(patch), "version": version.stdout,
        "sourceFiles": [{"path": str(path.relative_to(source)), "sha256": digest(path)}
                        for path in sorted(source.rglob("*")) if path.is_file()]}, indent=2) + "\n", encoding="utf-8")
    print(str(binary))


if __name__ == "__main__":
    main()
