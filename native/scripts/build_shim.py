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
from pathlib import Path

SCRIPT_DIR = Path(__file__).parent.resolve()
NATIVE_DIR = SCRIPT_DIR.parent
REPO_ROOT = NATIVE_DIR.parent
BUILD_DIR = NATIVE_DIR / "build"
VENDOR_DIR = NATIVE_DIR / "vendor"


def get_platform_info():
    """Return (system, arch, rid, lib_name)."""
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
        return system, arch, f"linux-{arch}", "libcurl_shim.so"
    elif system == "windows":
        return system, arch, f"win-{arch}", "curl_shim.dll"
    else:
        raise RuntimeError(f"Unsupported platform: {system}")


def ensure_vendor_deps(system: str):
    """Check for vendor libraries, run fetch_dependencies.py if missing."""
    if system == "windows":
        check_file = VENDOR_DIR / "lib" / "libcurl.lib"
    else:
        check_file = VENDOR_DIR / "lib" / "libcurl-impersonate.a"

    if check_file.exists():
        print(f"Vendor deps found: {check_file}")
        return

    print("Vendor dependencies not found, fetching...")
    fetch_script = SCRIPT_DIR / "fetch_dependencies.py"
    subprocess.run([sys.executable, str(fetch_script)], check=True)

    if not check_file.exists():
        raise RuntimeError(f"fetch_dependencies.py did not produce {check_file}")


def globalize_archive_symbols(system: str):
    """On macOS, globalize private-external symbols in the static archive.

    The curl-impersonate archive is built with -fvisibility=hidden, so all
    symbols are 'private external'. macOS's -exported_symbols_list can't
    promote these. We patch the Mach-O nlist entries to clear N_PEXT.
    """
    if system != "darwin":
        return

    archive = VENDOR_DIR / "lib" / "libcurl-impersonate.a"
    symbols_file = NATIVE_DIR / "shim.exp"
    output = VENDOR_DIR / "lib" / "libcurl-impersonate-global.a"

    if output.exists():
        print(f"Globalized archive already exists: {output}")
        return

    print("\nGlobalizing private-external symbols in static archive...")
    globalize_script = SCRIPT_DIR / "globalize_symbols.py"
    subprocess.run(
        [sys.executable, str(globalize_script),
         str(archive), str(symbols_file), str(output)],
        check=True,
    )


def cmake_build():
    """Run cmake configure + build."""
    BUILD_DIR.mkdir(parents=True, exist_ok=True)

    print(f"\nConfiguring cmake in {BUILD_DIR}...")
    subprocess.run(
        ["cmake", "-S", str(NATIVE_DIR), "-B", str(BUILD_DIR),
         "-DCMAKE_BUILD_TYPE=Release"],
        check=True,
    )

    print("\nBuilding...")
    subprocess.run(
        ["cmake", "--build", str(BUILD_DIR), "--config", "Release"],
        check=True,
    )


def copy_to_runtimes(lib_name: str, rid: str):
    """Copy built library to runtimes/{rid}/native/ at repo root."""
    src = BUILD_DIR / lib_name
    if not src.exists():
        # On multi-config generators the output may be in a Release subfolder
        src = BUILD_DIR / "Release" / lib_name
    if not src.exists():
        raise RuntimeError(f"Built library not found at {BUILD_DIR / lib_name}")

    dest_dir = REPO_ROOT / "runtimes" / rid / "native"
    dest_dir.mkdir(parents=True, exist_ok=True)
    dest = dest_dir / lib_name

    print(f"\nCopying {src} -> {dest}")
    shutil.copy2(src, dest)


def print_verification(lib_name: str, system: str):
    """Print library dependency info for verification."""
    lib_path = BUILD_DIR / lib_name
    if not lib_path.exists():
        lib_path = BUILD_DIR / "Release" / lib_name

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
        print("\n--- dumpbin /DEPENDENTS ---")
        subprocess.run(["dumpbin", "/DEPENDENTS", str(lib_path)],
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

    ensure_vendor_deps(system)
    globalize_archive_symbols(system)
    cmake_build()
    copy_to_runtimes(lib_name, rid)
    print_verification(lib_name, system)

    print(f"\nSUCCESS! Built {lib_name} -> runtimes/{rid}/native/{lib_name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
