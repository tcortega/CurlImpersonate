#!/usr/bin/env python3
"""
Fetch curl-impersonate dependencies for building the native shim.

- Linux/macOS: Downloads from lexiforest/curl-impersonate GitHub releases
- Windows: Downloads curl_cffi wheel from PyPI and extracts binaries
"""

import os
import sys
import platform
import urllib.request
import zipfile
import tarfile
import shutil
import json
import tempfile
from pathlib import Path

# Configuration
CURL_IMPERSONATE_VERSION = "v1.3.0"
CURL_IMPERSONATE_REPO = "lexiforest/curl-impersonate"
CURL_CFFI_PACKAGE = "curl_cffi"

# Output directories
SCRIPT_DIR = Path(__file__).parent.resolve()
NATIVE_DIR = SCRIPT_DIR.parent
VENDOR_DIR = NATIVE_DIR / "vendor"
VENDOR_INCLUDE = VENDOR_DIR / "include"
VENDOR_LIB = VENDOR_DIR / "lib"


def get_system_info():
    """Detect current system platform and architecture."""
    system = platform.system().lower()
    machine = platform.machine().lower()

    # Normalize architecture names
    arch_map = {
        "x86_64": "x86_64",
        "amd64": "x86_64",
        "aarch64": "aarch64",
        "arm64": "aarch64",
        "armv7l": "arm",
        "i686": "i386",
        "i386": "i386",
    }
    arch = arch_map.get(machine, machine)

    return system, arch


def download_file(url: str, dest: Path) -> Path:
    """Download a file from URL to destination."""
    print(f"Downloading: {url}")
    print(f"        To: {dest}")

    dest.parent.mkdir(parents=True, exist_ok=True)

    req = urllib.request.Request(url, headers={"User-Agent": "curl-impersonate-net/1.0"})
    with urllib.request.urlopen(req) as response:
        with open(dest, "wb") as f:
            shutil.copyfileobj(response, f)

    return dest


def get_github_release_assets(repo: str, version: str) -> list:
    """Fetch release assets from GitHub API."""
    api_url = f"https://api.github.com/repos/{repo}/releases/tags/{version}"

    req = urllib.request.Request(api_url, headers={
        "User-Agent": "curl-impersonate-net/1.0",
        "Accept": "application/vnd.github.v3+json"
    })

    with urllib.request.urlopen(req) as response:
        data = json.loads(response.read().decode())

    return data.get("assets", [])


def find_matching_asset(assets: list, system: str, arch: str) -> dict:
    """Find the appropriate asset for the current platform."""

    # Build search patterns based on platform
    if system == "darwin":
        # macOS patterns (lexiforest uses arm64-macos, x86_64-macos format)
        if arch == "aarch64":
            patterns = ["arm64-macos", "macos-arm64", "darwin-arm64", "aarch64-macos"]
        else:
            patterns = ["x86_64-macos", "macos-x86_64", "darwin-x86_64", "amd64-macos"]
    elif system == "linux":
        # Linux patterns - prefer gnu over musl
        if arch == "aarch64":
            patterns = ["aarch64-linux-gnu", "linux-aarch64", "aarch64-linux"]
        elif arch == "arm":
            patterns = ["arm-linux-gnueabihf", "armv7-linux", "arm-linux"]
        else:
            patterns = ["x86_64-linux-gnu", "linux-x86_64", "x86_64-linux"]
    else:
        return None

    # Search for libcurl-impersonate tarball
    for asset in assets:
        name = asset["name"].lower()
        if "libcurl-impersonate" in name and name.endswith((".tar.gz", ".tar.xz")):
            for pattern in patterns:
                if pattern.lower() in name:
                    return asset

    # Fallback: any matching tarball
    for asset in assets:
        name = asset["name"].lower()
        if name.endswith((".tar.gz", ".tar.xz")):
            for pattern in patterns:
                if pattern.lower() in name:
                    return asset

    return None


def extract_tarball(tarball: Path, dest: Path):
    """Extract tarball to destination."""
    print(f"Extracting: {tarball}")

    dest.mkdir(parents=True, exist_ok=True)

    mode = "r:gz" if str(tarball).endswith(".gz") else "r:xz"
    with tarfile.open(tarball, mode) as tar:
        tar.extractall(dest)


def fetch_curl_impersonate_unix(system: str, arch: str):
    """Fetch curl-impersonate for Linux/macOS from GitHub releases."""
    print(f"\nFetching curl-impersonate for {system}/{arch}...")

    # Get release assets
    assets = get_github_release_assets(CURL_IMPERSONATE_REPO, CURL_IMPERSONATE_VERSION)

    if not assets:
        raise RuntimeError(f"No assets found for release {CURL_IMPERSONATE_VERSION}")

    # Find matching asset
    asset = find_matching_asset(assets, system, arch)

    if not asset:
        available = [a["name"] for a in assets]
        raise RuntimeError(
            f"No matching asset for {system}/{arch}.\n"
            f"Available assets: {available}"
        )

    print(f"Found asset: {asset['name']}")

    # Download
    with tempfile.TemporaryDirectory() as tmpdir:
        tmpdir = Path(tmpdir)
        tarball = tmpdir / asset["name"]
        download_file(asset["browser_download_url"], tarball)

        # Extract
        extract_dir = tmpdir / "extracted"
        extract_tarball(tarball, extract_dir)

        # Find and copy files
        setup_vendor_from_extracted(extract_dir, system)


def setup_vendor_from_extracted(extract_dir: Path, system: str):
    """Set up vendor directory from extracted files."""

    # Clear existing vendor directory
    if VENDOR_DIR.exists():
        shutil.rmtree(VENDOR_DIR)

    VENDOR_INCLUDE.mkdir(parents=True, exist_ok=True)
    VENDOR_LIB.mkdir(parents=True, exist_ok=True)

    # Find include directory in the tarball
    headers_found = False
    for inc_dir in extract_dir.rglob("include"):
        if inc_dir.is_dir() and (inc_dir / "curl").exists():
            print(f"Copying headers from: {inc_dir}")
            shutil.copytree(inc_dir / "curl", VENDOR_INCLUDE / "curl")
            headers_found = True
            break

    if not headers_found:
        # Try to find curl directory directly
        for curl_dir in extract_dir.rglob("curl"):
            if curl_dir.is_dir() and (curl_dir / "curl.h").exists():
                print(f"Copying headers from: {curl_dir}")
                shutil.copytree(curl_dir, VENDOR_INCLUDE / "curl")
                headers_found = True
                break

    # If no headers found in tarball, download from curl.se
    if not headers_found:
        print("No headers in release tarball, downloading from curl.se...")
        download_curl_headers()

    # Add curl_easy_impersonate declaration to easy.h
    patch_easy_h_for_impersonate()

    # Find and copy libraries
    if system == "darwin":
        lib_patterns = ["*.dylib"]
    else:
        lib_patterns = ["*.so", "*.so.*"]

    for pattern in lib_patterns:
        for lib_file in extract_dir.rglob(pattern):
            if lib_file.is_file() and "curl" in lib_file.name.lower():
                dest = VENDOR_LIB / lib_file.name
                print(f"Copying library: {lib_file.name}")
                shutil.copy2(lib_file, dest)

                # Create symlinks for versioned libraries
                if ".so." in lib_file.name:
                    base_name = lib_file.name.split(".so.")[0] + ".so"
                    symlink = VENDOR_LIB / base_name
                    if not symlink.exists():
                        symlink.symlink_to(lib_file.name)

    print(f"\nVendor directory set up at: {VENDOR_DIR}")


def get_pypi_wheel_url(package: str, system: str, arch: str) -> tuple:
    """Get the wheel URL from PyPI for the given platform."""

    # Query PyPI API
    api_url = f"https://pypi.org/pypi/{package}/json"

    req = urllib.request.Request(api_url, headers={"User-Agent": "curl-impersonate-net/1.0"})
    with urllib.request.urlopen(req) as response:
        data = json.loads(response.read().decode())

    version = data["info"]["version"]
    urls = data["urls"]

    # Build platform tag patterns
    if system == "windows":
        if arch == "aarch64":
            patterns = ["win_arm64"]
        else:
            patterns = ["win_amd64", "win32"]
    else:
        return None, None

    # Find matching wheel
    for url_info in urls:
        filename = url_info["filename"]
        if filename.endswith(".whl"):
            for pattern in patterns:
                if pattern in filename:
                    return url_info["url"], filename

    return None, None


def fetch_curl_cffi_windows(arch: str):
    """Fetch curl_cffi wheel from PyPI for Windows."""
    print(f"\nFetching curl_cffi for Windows/{arch}...")

    wheel_url, wheel_name = get_pypi_wheel_url(CURL_CFFI_PACKAGE, "windows", arch)

    if not wheel_url:
        raise RuntimeError(f"No Windows wheel found for curl_cffi ({arch})")

    print(f"Found wheel: {wheel_name}")

    with tempfile.TemporaryDirectory() as tmpdir:
        tmpdir = Path(tmpdir)
        wheel_path = tmpdir / wheel_name
        download_file(wheel_url, wheel_path)

        # Extract wheel (it's just a zip file)
        extract_dir = tmpdir / "extracted"
        print(f"Extracting wheel...")
        with zipfile.ZipFile(wheel_path, "r") as zf:
            zf.extractall(extract_dir)

        # Set up vendor directory
        setup_vendor_from_wheel(extract_dir)


def setup_vendor_from_wheel(extract_dir: Path):
    """Set up vendor directory from extracted wheel."""

    # Clear existing vendor directory
    if VENDOR_DIR.exists():
        shutil.rmtree(VENDOR_DIR)

    VENDOR_INCLUDE.mkdir(parents=True, exist_ok=True)
    VENDOR_LIB.mkdir(parents=True, exist_ok=True)

    # Find DLLs in wheel
    dll_found = False
    for dll_file in extract_dir.rglob("*.dll"):
        if "curl" in dll_file.name.lower() or "libcurl" in dll_file.name.lower():
            dest = VENDOR_LIB / dll_file.name
            print(f"Copying DLL: {dll_file.name}")
            shutil.copy2(dll_file, dest)
            dll_found = True

    # Also copy any .lib files (import libraries)
    for lib_file in extract_dir.rglob("*.lib"):
        dest = VENDOR_LIB / lib_file.name
        print(f"Copying import lib: {lib_file.name}")
        shutil.copy2(lib_file, dest)

    # Find and copy headers
    header_found = False
    for inc_dir in extract_dir.rglob("include"):
        if inc_dir.is_dir():
            curl_dir = inc_dir / "curl"
            if curl_dir.exists():
                print(f"Copying headers from: {inc_dir}")
                shutil.copytree(curl_dir, VENDOR_INCLUDE / "curl")
                header_found = True
                break

    # If no headers in wheel, we need to download them separately
    if not header_found:
        print("No headers found in wheel, downloading from curl releases...")
        download_curl_headers()

    # Add curl_easy_impersonate declaration to easy.h
    patch_easy_h_for_impersonate()

    if not dll_found:
        raise RuntimeError("No curl DLL found in wheel!")

    print(f"\nVendor directory set up at: {VENDOR_DIR}")


def download_curl_headers():
    """Download curl headers from official curl releases."""
    # Use a recent curl version for headers
    curl_version = "8.11.1"
    url = f"https://curl.se/download/curl-{curl_version}.tar.gz"

    with tempfile.TemporaryDirectory() as tmpdir:
        tmpdir = Path(tmpdir)
        tarball = tmpdir / f"curl-{curl_version}.tar.gz"
        download_file(url, tarball)

        extract_dir = tmpdir / "extracted"
        extract_tarball(tarball, extract_dir)

        # Find include/curl directory
        for curl_dir in extract_dir.rglob("curl"):
            if curl_dir.is_dir() and (curl_dir / "curl.h").exists():
                # Copy only header files
                for header in curl_dir.glob("*.h"):
                    dest = VENDOR_INCLUDE / "curl" / header.name
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(header, dest)
                print(f"Copied curl headers from version {curl_version}")
                return

        raise RuntimeError("Could not find curl headers in downloaded archive")


def patch_easy_h_for_impersonate():
    """Add curl_easy_impersonate declaration to easy.h if not present."""
    easy_h = VENDOR_INCLUDE / "curl" / "easy.h"

    if not easy_h.exists():
        print("Warning: easy.h not found, cannot patch")
        return

    content = easy_h.read_text()

    # Check if already patched
    if "curl_easy_impersonate" in content:
        print("easy.h already contains curl_easy_impersonate declaration")
        return

    # The declaration to add (matches curl-impersonate patch)
    impersonate_decl = '''
/*
 * curl_easy_impersonate - Impersonate a browser's TLS/HTTP fingerprint
 *
 * This function is provided by curl-impersonate. It configures the CURL
 * handle to mimic a specific browser's TLS fingerprint, HTTP/2 settings,
 * and optionally HTTP headers.
 *
 * @param curl           The CURL easy handle to configure
 * @param target         Browser target (e.g., "chrome", "chrome124", "safari17_0")
 * @param default_headers Non-zero to apply browser's default HTTP headers
 * @return CURLcode      CURLE_OK on success
 */
CURL_EXTERN CURLcode curl_easy_impersonate(CURL *curl, const char *target,
                                           int default_headers);
'''

    # Find the right place to insert (before the closing extern "C" or at end)
    # Look for the pattern before #ifdef __cplusplus at the end
    insert_marker = '#ifdef __cplusplus'
    if insert_marker in content:
        # Insert before the closing #ifdef __cplusplus
        parts = content.rsplit(insert_marker, 1)
        new_content = parts[0] + impersonate_decl + "\n" + insert_marker + parts[1]
    else:
        # Just append before #endif at the very end
        if content.rstrip().endswith("#endif"):
            new_content = content.rstrip()[:-6] + impersonate_decl + "\n#endif\n"
        else:
            new_content = content + impersonate_decl

    easy_h.write_text(new_content)
    print("Patched easy.h with curl_easy_impersonate declaration")


def main():
    """Main entry point."""
    system, arch = get_system_info()

    print("=" * 60)
    print("curl-impersonate Dependency Fetcher")
    print("=" * 60)
    print(f"System:       {system}")
    print(f"Architecture: {arch}")
    print(f"Vendor dir:   {VENDOR_DIR}")
    print("=" * 60)

    try:
        if system == "windows":
            fetch_curl_cffi_windows(arch)
        elif system in ("linux", "darwin"):
            fetch_curl_impersonate_unix(system, arch)
        else:
            raise RuntimeError(f"Unsupported platform: {system}")

        print("\n" + "=" * 60)
        print("SUCCESS! Dependencies fetched successfully.")
        print("=" * 60)

        # List what was installed
        print("\nInstalled files:")
        if VENDOR_INCLUDE.exists():
            headers = list(VENDOR_INCLUDE.rglob("*.h"))
            print(f"  Headers: {len(headers)} files in {VENDOR_INCLUDE}")

        if VENDOR_LIB.exists():
            libs = list(VENDOR_LIB.iterdir())
            for lib in libs:
                print(f"  Library: {lib.name}")

        return 0

    except Exception as e:
        print(f"\nERROR: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
