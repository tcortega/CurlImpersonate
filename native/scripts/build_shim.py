#!/usr/bin/env python3
"""
Build the curl_shim native library.

1. Ensures vendor dependencies exist (runs fetch_dependencies.py if not)
2. Runs cmake configure + build
3. Copies the built library to runtimes/{rid}/native/
4. Prints library dependency info for verification
"""

import os
import sys
import platform
import subprocess
import shutil
import json
from pathlib import Path

SCRIPT_DIR = Path(__file__).parent.resolve()
NATIVE_DIR = SCRIPT_DIR.parent
REPO_ROOT = NATIVE_DIR.parent
BUILD_ROOT = NATIVE_DIR / "build"
VENDOR_DIR = NATIVE_DIR / "vendor"
VENDOR_VERSION_FILE = VENDOR_DIR / "curl-impersonate.version"
VENDOR_RID_FILE = VENDOR_DIR / "curl-impersonate.rid"
ASSET_MANIFEST_FILE = NATIVE_DIR / "native-assets.json"


def load_asset_manifest() -> dict:
    with open(ASSET_MANIFEST_FILE, "r", encoding="utf-8") as manifest_file:
        return json.load(manifest_file)


ASSET_MANIFEST = load_asset_manifest()
CURL_IMPERSONATE_VERSION = ASSET_MANIFEST["version"]


def validate_supported_rid(rid: str):
    if rid not in ASSET_MANIFEST["assets"]:
        supported = ", ".join(sorted(ASSET_MANIFEST["assets"]))
        raise RuntimeError(f"Unsupported CURL_IMPERSONATE_RID '{rid}'. Supported: {supported}")


def get_platform_info():
    """Return (system, arch, rid, lib_name)."""
    rid_override = os.environ.get("CURL_IMPERSONATE_RID")
    if rid_override:
        rid_parts = rid_override.split("-")
        if len(rid_parts) < 2:
            raise RuntimeError(f"Invalid CURL_IMPERSONATE_RID: {rid_override}")

        validate_supported_rid(rid_override)

        system = rid_parts[0]
        arch = rid_parts[-1]
        if system == "osx":
            return "darwin", arch, rid_override, "libcurl_shim.dylib"
        if system == "linux":
            return "linux", arch, rid_override, "libcurl_shim.so"
        if system == "win":
            return "windows", arch, rid_override, "curl_shim.dll"

        raise RuntimeError(f"Unsupported CURL_IMPERSONATE_RID: {rid_override}")

    system = platform.system().lower()
    machine = platform.machine().lower()

    arch_map = {
        "x86_64": "x64",
        "amd64": "x64",
        "aarch64": "arm64",
        "arm64": "arm64",
        "i686": "x86",
        "i386": "x86",
        "armv7l": "arm",
    }
    arch = arch_map.get(machine, machine)

    if system == "darwin":
        return system, arch, f"osx-{arch}", "libcurl_shim.dylib"
    elif system == "linux":
        prefix = "linux-musl" if is_musl_linux() else "linux"
        return system, arch, f"{prefix}-{arch}", "libcurl_shim.so"
    elif system == "windows":
        return system, arch, f"win-{arch}", "curl_shim.dll"
    else:
        raise RuntimeError(f"Unsupported platform: {system}")


def is_musl_linux() -> bool:
    libc_name, _ = platform.libc_ver()
    if libc_name.lower() == "musl":
        return True

    for directory in (Path("/lib"), Path("/usr/lib")):
        try:
            if directory.is_dir() and any(directory.glob("ld-musl-*.so.1")):
                return True
        except OSError:
            continue

    return False


def ensure_vendor_deps(system: str, rid: str):
    """Check for vendor libraries, run fetch_dependencies.py if missing."""
    if system == "windows":
        check_file = VENDOR_DIR / "lib" / "libcurl-impersonate_imp.lib"
    else:
        check_file = VENDOR_DIR / "lib" / "libcurl-impersonate.a"

    if check_file.exists():
        if VENDOR_VERSION_FILE.exists() and VENDOR_RID_FILE.exists():
            vendor_version = VENDOR_VERSION_FILE.read_text().strip()
            vendor_rid = VENDOR_RID_FILE.read_text().strip()
            if vendor_version == CURL_IMPERSONATE_VERSION and vendor_rid == rid:
                print(f"Vendor deps found: {check_file}")
                return

            print(
                "Vendor dependencies are "
                f"{vendor_version}/{vendor_rid}, expected {CURL_IMPERSONATE_VERSION}/{rid}; "
                "refetching..."
            )
        else:
            print("Vendor dependencies have no complete version/RID marker; refetching...")
    else:
        print("Vendor dependencies not found, fetching...")

    fetch_script = SCRIPT_DIR / "fetch_dependencies.py"
    subprocess.run([sys.executable, str(fetch_script)], check=True)

    if not check_file.exists():
        raise RuntimeError(f"fetch_dependencies.py did not produce {check_file}")


def globalize_archive_symbols(system: str):
    """On macOS and Linux, globalize hidden symbols in the static archive.

    The curl-impersonate archive is built with -fvisibility=hidden: Mach-O
    symbols become 'private external' (N_PEXT), ELF symbols become
    STV_HIDDEN. Neither -exported_symbols_list nor a version script can
    promote them, so we patch the object symbol tables before linking.
    """
    if system not in ("darwin", "linux"):
        return

    archive = VENDOR_DIR / "lib" / "libcurl-impersonate.a"
    symbols_file = NATIVE_DIR / "shim.exp"
    output = VENDOR_DIR / "lib" / "libcurl-impersonate-global.a"

    if output.exists():
        print(f"Globalized archive already exists: {output}")
        return

    print("\nGlobalizing hidden symbols in static archive...")
    globalize_script = SCRIPT_DIR / "globalize_symbols.py"
    subprocess.run(
        [sys.executable, str(globalize_script),
         str(archive), str(symbols_file), str(output)],
        check=True,
    )


def build_dir_for_rid(rid: str) -> Path:
    return BUILD_ROOT / rid


def cmake_build(build_dir: Path):
    """Run cmake configure + build."""
    build_dir.mkdir(parents=True, exist_ok=True)

    configure_command = [
        "cmake", "-S", str(NATIVE_DIR), "-B", str(build_dir),
        "-DCMAKE_BUILD_TYPE=Release",
    ]

    generator_platform = os.environ.get("CMAKE_GENERATOR_PLATFORM")
    if generator_platform:
        configure_command.extend(["-A", generator_platform])

    osx_architectures = os.environ.get("CMAKE_OSX_ARCHITECTURES")
    if osx_architectures:
        configure_command.append(f"-DCMAKE_OSX_ARCHITECTURES={osx_architectures}")

    toolchain_file = os.environ.get("CMAKE_TOOLCHAIN_FILE")
    if toolchain_file:
        configure_command.append(f"-DCMAKE_TOOLCHAIN_FILE={toolchain_file}")

    print(f"\nConfiguring cmake in {build_dir}...")
    subprocess.run(configure_command, check=True)

    print("\nBuilding...")
    subprocess.run(
        ["cmake", "--build", str(build_dir), "--config", "Release"],
        check=True,
    )


def copy_to_runtimes(lib_name: str, rid: str, system: str, build_dir: Path):
    """Copy built library to runtimes/{rid}/native/ at repo root."""
    src = build_dir / lib_name
    if not src.exists():
        # On multi-config generators the output may be in a Release subfolder
        src = build_dir / "Release" / lib_name
    if not src.exists():
        raise RuntimeError(f"Built library not found at {build_dir / lib_name}")

    dest_dir = REPO_ROOT / "runtimes" / rid / "native"
    dest_dir.mkdir(parents=True, exist_ok=True)
    dest = dest_dir / lib_name

    print(f"\nCopying {src} -> {dest}")
    shutil.copy2(src, dest)

    if system == "linux":
        strip_linux_shim(dest)

    if system == "windows":
        for dll in (VENDOR_DIR / "lib").glob("*.dll"):
            dep_dest = dest_dir / dll.name
            print(f"Copying runtime dependency {dll} -> {dep_dest}")
            shutil.copy2(dll, dep_dest)


def strip_linux_shim(path: Path):
    """Strip .symtab and debug sections; the dynamic symbol table stays intact."""
    before = path.stat().st_size
    subprocess.run(["strip", "--strip-unneeded", str(path)], check=True)
    after = path.stat().st_size
    print(f"Stripped {path.name}: {before / 1024 / 1024:.1f} MiB -> {after / 1024 / 1024:.1f} MiB")


def write_native_file_manifest():
    """Write size/hash metadata for staged native runtime files."""
    manifest_script = REPO_ROOT / "tools" / "write_native_file_manifest.py"
    subprocess.run(
        [
            sys.executable,
            str(manifest_script),
            "--runtimes-root",
            str(REPO_ROOT / "runtimes"),
            "--output",
            str(REPO_ROOT / "runtimes" / "native-files.json"),
        ],
        check=True,
    )


def print_verification(lib_name: str, system: str, build_dir: Path):
    """Print library dependency info for verification."""
    lib_path = build_dir / lib_name
    if not lib_path.exists():
        lib_path = build_dir / "Release" / lib_name

    if not lib_path.exists():
        print("Warning: could not find built library for verification")
        return

    print(f"\n{'=' * 60}")
    print(f"Verification: {lib_path}")
    print(f"{'=' * 60}")

    if system == "darwin":
        print("\n--- otool -L ---")
        subprocess.run(["otool", "-L", str(lib_path)])
        print("\n--- Exported symbols (T) ---")
        subprocess.run(
            ["bash", "-c", f"nm -gU '{lib_path}' | grep ' T ' | head -40"])
    elif system == "linux":
        print("\n--- ldd ---")
        subprocess.run(["ldd", str(lib_path)])
        print("\n--- Exported symbols (T) ---")
        subprocess.run(
            ["bash", "-c", f"nm -D '{lib_path}' | grep ' T ' | head -40"])
    elif system == "windows":
        dumpbin = os.environ.get("DUMPBIN") or shutil.which("dumpbin")
        if not dumpbin:
            print(
                "dumpbin not on PATH; skipping dependency printout "
                "(tools/inspect_native_dependencies.py is the gating check)"
            )
            return
        print("\n--- dumpbin /DEPENDENTS ---")
        subprocess.run([dumpbin, "/DEPENDENTS", str(lib_path)],
                        capture_output=False)


def main():
    system, arch, rid, lib_name = get_platform_info()

    print("=" * 60)
    print("curl_shim Native Build")
    print("=" * 60)
    print(f"System:  {system}")
    print(f"Arch:    {arch}")
    print(f"RID:     {rid}")
    print(f"Library: {lib_name}")
    print("=" * 60)

    build_dir = build_dir_for_rid(rid)
    ensure_vendor_deps(system, rid)
    globalize_archive_symbols(system)
    cmake_build(build_dir)
    copy_to_runtimes(lib_name, rid, system, build_dir)
    write_native_file_manifest()
    print_verification(lib_name, system, build_dir)

    print(f"\nSUCCESS! Built {lib_name} -> runtimes/{rid}/native/{lib_name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
