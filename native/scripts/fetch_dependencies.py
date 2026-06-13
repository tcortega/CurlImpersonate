#!/usr/bin/env python3
"""
Fetch curl-impersonate dependencies for building the native shim.

- Downloads exact pinned lexiforest/curl-impersonate GitHub release assets
"""

import os
import sys
import platform
import urllib.request
import tarfile
import shutil
import json
import tempfile
import hashlib
from pathlib import Path, PurePosixPath

# Output directories
SCRIPT_DIR = Path(__file__).parent.resolve()
NATIVE_DIR = SCRIPT_DIR.parent
VENDOR_DIR = NATIVE_DIR / "vendor"
VENDOR_INCLUDE = VENDOR_DIR / "include"
VENDOR_LIB = VENDOR_DIR / "lib"
VENDOR_VERSION_FILE = VENDOR_DIR / "curl-impersonate.version"
VENDOR_RID_FILE = VENDOR_DIR / "curl-impersonate.rid"
ASSET_MANIFEST_FILE = NATIVE_DIR / "native-assets.json"


def load_asset_manifest() -> dict:
    """Load pinned native release assets from native-assets.json."""
    with open(ASSET_MANIFEST_FILE, "r", encoding="utf-8") as manifest_file:
        return json.load(manifest_file)


ASSET_MANIFEST = load_asset_manifest()
CURL_IMPERSONATE_VERSION = ASSET_MANIFEST["version"]
CURL_IMPERSONATE_REPO = ASSET_MANIFEST["repository"]
CURL_HEADERS_VERSION = "8.11.1"
CURL_HEADERS_SHA256 = "a889ac9dbba3644271bd9d1302b5c22a088893719b72be3487bc3d401e5c4e80"


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


def get_target_rid(system: str, arch: str) -> str:
    """Return the target RID, allowing CI to override host detection."""
    override = os.environ.get("CURL_IMPERSONATE_RID")
    if override:
        if override not in ASSET_MANIFEST["assets"]:
            supported = ", ".join(sorted(ASSET_MANIFEST["assets"]))
            raise RuntimeError(f"Unsupported CURL_IMPERSONATE_RID '{override}'. Supported: {supported}")
        return override

    if system == "darwin":
        if arch == "aarch64":
            return "osx-arm64"
        if arch == "x86_64":
            return "osx-x64"

    if system == "linux":
        linux_prefix = "linux-musl" if is_musl_linux() else "linux"
        if arch == "aarch64":
            return f"{linux_prefix}-arm64"
        if arch == "x86_64":
            return f"{linux_prefix}-x64"

    if system == "windows":
        if arch == "aarch64":
            return "win-arm64"
        if arch == "x86_64":
            return "win-x64"

    raise RuntimeError(f"Unsupported target RID for {system}/{arch}")


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


def get_manifest_asset(rid: str) -> dict:
    """Return the pinned release asset metadata for a RID."""
    try:
        return ASSET_MANIFEST["assets"][rid]
    except KeyError as exc:
        supported = ", ".join(sorted(ASSET_MANIFEST["assets"]))
        raise RuntimeError(f"No native asset manifest entry for {rid}. Supported: {supported}") from exc


def get_system_for_rid(rid: str) -> str:
    """Return the archive layout system for a target RID."""
    if rid.startswith("osx-"):
        return "darwin"
    if rid.startswith("linux-"):
        return "linux"
    if rid.startswith("win-"):
        return "windows"
    raise RuntimeError(f"Unsupported target RID: {rid}")


def get_arch_for_rid(rid: str) -> str:
    """Return the normalized architecture segment for a target RID."""
    return rid.split("-")[-1]


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


def calculate_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            digest.update(chunk)

    return digest.hexdigest()


def verify_expected_sha256(path: Path, artifact_name: str, expected: str):
    """Verify a downloaded artifact against a pinned SHA-256."""
    actual = calculate_sha256(path)
    if actual != expected:
        raise RuntimeError(
            f"SHA-256 mismatch for {artifact_name}.\n"
            f"Expected: {expected}\n"
            f"Actual:   {actual}"
        )

    print(f"Verified SHA-256: {actual}")


def verify_sha256(path: Path, asset_name: str):
    """Verify a downloaded release asset against the pinned SHA-256."""
    expected = None
    for asset in ASSET_MANIFEST["assets"].values():
        if asset["file"] == asset_name:
            expected = asset["sha256"]
            break

    if expected is None:
        raise RuntimeError(f"No pinned SHA-256 for release asset {asset_name}")

    verify_expected_sha256(path, asset_name, expected)


def get_github_release_assets(repo: str, version: str) -> list:
    """Fetch release assets from GitHub API."""
    api_url = f"https://api.github.com/repos/{repo}/releases/tags/{version}"

    headers = {
        "User-Agent": "curl-impersonate-net/1.0",
        "Accept": "application/vnd.github.v3+json",
    }
    # Authenticated requests avoid the strict anonymous API rate limit in CI
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    req = urllib.request.Request(api_url, headers=headers)

    with urllib.request.urlopen(req) as response:
        data = json.loads(response.read().decode())

    return data.get("assets", [])


def find_release_asset(assets: list, expected_name: str) -> dict:
    """Find the exact pinned release asset from the GitHub release."""
    for asset in assets:
        if asset["name"] == expected_name:
            return asset

    available = [asset["name"] for asset in assets]
    raise RuntimeError(
        f"No release asset named {expected_name} found in {CURL_IMPERSONATE_REPO} "
        f"{CURL_IMPERSONATE_VERSION}.\nAvailable assets: {available}"
    )


def get_safe_tar_member_path(dest: Path, member_name: str) -> Path:
    """Return the safe extraction path for a tar member."""
    member_path = PurePosixPath(member_name)
    if member_path.is_absolute() or not member_path.parts:
        raise RuntimeError(f"Unsafe tar member path: {member_name}")

    for part in member_path.parts:
        if part == ".." or "\\" in part or (os.name == "nt" and ":" in part):
            raise RuntimeError(f"Unsafe tar member path: {member_name}")

    dest_root = dest.resolve()
    target = dest.joinpath(*member_path.parts).resolve()
    if not target.is_relative_to(dest_root):
        raise RuntimeError(f"Unsafe tar member path: {member_name}")

    return target


def extract_tarball(tarball: Path, dest: Path):
    """Extract tarball to destination."""
    print(f"Extracting: {tarball}")

    dest.mkdir(parents=True, exist_ok=True)

    mode = "r:gz" if str(tarball).endswith(".gz") else "r:xz"
    with tarfile.open(tarball, mode) as tar:
        for member in tar.getmembers():
            # Some tarballs carry a "." entry for the archive root
            if member.isdir() and not PurePosixPath(member.name).parts:
                continue

            target = get_safe_tar_member_path(dest, member.name)

            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
                continue

            if not member.isfile():
                print(f"Skipping non-regular tar member: {member.name}")
                continue

            target.parent.mkdir(parents=True, exist_ok=True)
            source = tar.extractfile(member)
            if source is None:
                raise RuntimeError(f"Could not read tar member: {member.name}")

            with source, open(target, "wb") as output:
                shutil.copyfileobj(source, output)


def fetch_curl_impersonate_release(system: str, arch: str, rid: str):
    """Fetch curl-impersonate for the target RID from GitHub releases."""
    print(f"\nFetching curl-impersonate for {system}/{arch} ({rid})...")
    manifest_asset = get_manifest_asset(rid)
    expected_name = manifest_asset["file"]

    # Get release assets
    assets = get_github_release_assets(CURL_IMPERSONATE_REPO, CURL_IMPERSONATE_VERSION)

    if not assets:
        raise RuntimeError(f"No assets found for release {CURL_IMPERSONATE_VERSION}")

    # Find pinned asset
    asset = find_release_asset(assets, expected_name)

    print(f"Found asset: {asset['name']}")

    # Download
    with tempfile.TemporaryDirectory() as tmpdir:
        tmpdir = Path(tmpdir)
        tarball = tmpdir / asset["name"]
        download_file(asset["browser_download_url"], tarball)
        verify_sha256(tarball, asset["name"])

        # Extract
        extract_dir = tmpdir / "extracted"
        extract_tarball(tarball, extract_dir)

        # Find and copy files
        setup_vendor_from_extracted(extract_dir, system, rid)


def setup_vendor_from_extracted(extract_dir: Path, system: str, rid: str):
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

    # Find and copy native libraries.
    if system == "darwin" or system == "linux":
        lib_patterns = ["*.a"]
    elif system == "windows":
        lib_patterns = ["*.lib", "*.dll"]
    else:
        lib_patterns = ["*.so", "*.so.*"]

    for pattern in lib_patterns:
        for lib_file in extract_dir.rglob(pattern):
            if lib_file.is_file():
                dest = VENDOR_LIB / lib_file.name
                print(f"Copying library: {lib_file.name}")
                shutil.copy2(lib_file, dest)

    if system == "windows":
        required = [
            VENDOR_LIB / "libcurl-impersonate_imp.lib",
            VENDOR_LIB / "libcurl-impersonate.dll",
            VENDOR_LIB / "zlib.dll",
        ]
    else:
        required = [VENDOR_LIB / "libcurl-impersonate.a"]

    missing = [str(path) for path in required if not path.exists()]
    if missing:
        raise RuntimeError(f"Release asset is missing required native files: {missing}")

    print(f"\nVendor directory set up at: {VENDOR_DIR}")
    VENDOR_VERSION_FILE.write_text(CURL_IMPERSONATE_VERSION + "\n")
    VENDOR_RID_FILE.write_text(rid + "\n")


def download_curl_headers():
    """Download curl headers from official curl releases."""
    url = f"https://curl.se/download/curl-{CURL_HEADERS_VERSION}.tar.gz"

    with tempfile.TemporaryDirectory() as tmpdir:
        tmpdir = Path(tmpdir)
        tarball = tmpdir / f"curl-{CURL_HEADERS_VERSION}.tar.gz"
        download_file(url, tarball)
        verify_expected_sha256(tarball, tarball.name, CURL_HEADERS_SHA256)

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
                print(f"Copied curl headers from version {CURL_HEADERS_VERSION}")
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
    host_system, host_arch = get_system_info()
    rid = get_target_rid(host_system, host_arch)
    target_system = get_system_for_rid(rid)
    target_arch = get_arch_for_rid(rid)

    print("=" * 60)
    print("curl-impersonate Dependency Fetcher")
    print("=" * 60)
    print(f"Host system:         {host_system}")
    print(f"Host architecture:   {host_arch}")
    print(f"Target RID:          {rid}")
    print(f"Vendor dir:          {VENDOR_DIR}")
    print("=" * 60)

    try:
        if target_system in ("linux", "darwin", "windows"):
            fetch_curl_impersonate_release(target_system, target_arch, rid)
        else:
            raise RuntimeError(f"Unsupported platform: {target_system}")

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
