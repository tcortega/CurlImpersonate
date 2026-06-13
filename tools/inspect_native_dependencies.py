#!/usr/bin/env python3
"""Fail release builds that leak vendored native dependencies dynamically."""

import argparse
import os
import shutil
import struct
import subprocess
import sys
from pathlib import Path


FORBIDDEN_SUBSTRINGS = (
    "curl-impersonate",
    "libcurl",
    "libssl",
    "libcrypto",
    "libnghttp2",
    "libnghttp3",
    "libngtcp2",
    "libbrotli",
    "libzstd",
    "libcares",
    "nghttp2",
    "nghttp3",
    "ngtcp2",
    "brotli",
    "zstd",
    "cares",
)

OWN_LIBRARIES = {
    "curl_shim.dll",
    "libcurl_shim.dylib",
    "libcurl_shim.so",
}

WINDOWS_SYSTEM_DLLS = {
    "advapi32.dll",
    "bcrypt.dll",
    "crypt32.dll",
    "gdi32.dll",
    "iphlpapi.dll",
    "kernel32.dll",
    "msvcp140.dll",
    "msvcrt.dll",
    "normaliz.dll",
    "ntdll.dll",
    "ole32.dll",
    "secur32.dll",
    "shell32.dll",
    "ucrtbase.dll",
    "user32.dll",
    "vcruntime140.dll",
    "vcruntime140_1.dll",
    "wldap32.dll",
    "ws2_32.dll",
}


def library_name_for_rid(rid: str) -> str:
    if rid.startswith("win-"):
        return "curl_shim.dll"
    if rid.startswith("osx-"):
        return "libcurl_shim.dylib"
    if rid.startswith("linux-"):
        return "libcurl_shim.so"
    raise ValueError(f"Unsupported RID: {rid}")


def run(command: list[str]) -> str:
    completed = subprocess.run(
        command,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    return completed.stdout


def inspect_linux(library: Path) -> list[str]:
    output = run(["ldd", str(library)])
    print(output, end="")
    dependencies: list[str] = []

    for line in output.splitlines():
        value = line.strip()
        if not value:
            continue
        # musl ldd reports unresolved symbols without a failing exit code
        if "Error relocating" in value or "symbol not found" in value:
            raise RuntimeError(f"Unresolved symbol in {library.name}: {value}")
        if "=>" in value:
            dependencies.append(value.split("=>", 1)[0].strip())
        else:
            dependencies.append(value.split(None, 1)[0])

    return dependencies


def inspect_macos(library: Path) -> list[str]:
    output = run(["otool", "-L", str(library)])
    print(output, end="")
    dependencies: list[str] = []

    for line in output.splitlines()[1:]:
        value = line.strip()
        if value:
            dependencies.append(value.split(None, 1)[0])

    return dependencies


def inspect_windows(library: Path, dumpbin: str | None) -> list[str]:
    dumpbin_path = dumpbin or os.environ.get("DUMPBIN") or shutil.which("dumpbin")
    if not dumpbin_path:
        raise RuntimeError("dumpbin.exe was not found. Pass --dumpbin or set DUMPBIN.")

    output = run([dumpbin_path, "/DEPENDENTS", str(library)])
    print(output, end="")
    dependencies: list[str] = []

    for line in output.splitlines():
        value = line.strip()
        if value.lower().endswith(".dll"):
            dependencies.append(value)

    return dependencies


def dependency_name(value: str) -> str:
    return Path(value).name.lower()


def is_forbidden_dependency(value: str, rid: str) -> bool:
    name = dependency_name(value)
    if name in OWN_LIBRARIES:
        return False

    if rid.startswith("win-"):
        return False

    if name.startswith("libz.") or name.startswith("zlib"):
        return True

    if rid.startswith("linux-") and (
        name.startswith("libstdc++") or name.startswith("libgcc_s")
    ):
        return True

    return any(token in name for token in FORBIDDEN_SUBSTRINGS)


def is_windows_system_dependency(value: str) -> bool:
    name = dependency_name(value)
    return (
        name in WINDOWS_SYSTEM_DLLS
        or name.startswith("api-ms-win-")
        or name.startswith("ext-ms-win-")
    )


def validate_windows_bundled_dependencies(
    library: Path,
    dependencies: list[str],
) -> list[str]:
    missing: list[str] = []

    for dependency in dependencies:
        name = dependency_name(dependency)
        if name in OWN_LIBRARIES or is_windows_system_dependency(name):
            continue
        if not (library.parent / name).is_file():
            missing.append(dependency)

    return missing


def inspect(library: Path, rid: str, dumpbin: str | None) -> list[str]:
    if rid.startswith("linux-"):
        return inspect_linux(library)
    if rid.startswith("osx-"):
        return inspect_macos(library)
    if rid.startswith("win-"):
        return inspect_windows(library, dumpbin)
    raise ValueError(f"Unsupported RID: {rid}")


def read_glibc_version_needs(library: Path) -> set[str]:
    """Parse GLIBC_* entries from the ELF64 .gnu.version_r section."""
    data = library.read_bytes()
    if data[:4] != b"\x7fELF" or data[4] != 2:
        raise ValueError(f"{library} is not an ELF64 object")

    (e_shoff,) = struct.unpack_from("<Q", data, 0x28)
    e_shentsize, e_shnum = struct.unpack_from("<HH", data, 0x3A)
    sections = []
    for index in range(e_shnum):
        offset = e_shoff + index * e_shentsize
        _, sh_type = struct.unpack_from("<II", data, offset)
        _, _, sh_offset, _ = struct.unpack_from("<QQQQ", data, offset + 8)
        sh_link, _ = struct.unpack_from("<II", data, offset + 40)
        sections.append((sh_type, sh_offset, sh_link))

    versions: set[str] = set()
    for sh_type, sh_offset, sh_link in sections:
        if sh_type != 0x6FFFFFFE:  # SHT_GNU_verneed
            continue
        strtab_offset = sections[sh_link][1]
        position = sh_offset
        while True:
            _, vn_cnt, _, vn_aux, vn_next = struct.unpack_from("<HHIII", data, position)
            aux_position = position + vn_aux
            for _ in range(vn_cnt):
                _, _, _, vna_name, vna_next = struct.unpack_from("<IHHII", data, aux_position)
                name_offset = strtab_offset + vna_name
                name = data[name_offset : data.index(b"\0", name_offset)].decode()
                if name.startswith("GLIBC_"):
                    versions.add(name)
                if vna_next == 0:
                    break
                aux_position += vna_next
            if vn_next == 0:
                break
            position += vn_next

    return versions


def validate_glibc_floor(library: Path, max_glibc: str) -> list[str]:
    limit = tuple(int(part) for part in max_glibc.split("."))
    return sorted(
        version
        for version in read_glibc_version_needs(library)
        if tuple(int(part) for part in version.split("_")[1].split(".")) > limit
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True)
    parser.add_argument("--library")
    parser.add_argument("--dumpbin")
    parser.add_argument(
        "--max-glibc",
        help="Fail if the library requires a GLIBC symbol version above this floor.",
    )
    args = parser.parse_args()

    library = Path(
        args.library
        or f"runtimes/{args.rid}/native/{library_name_for_rid(args.rid)}"
    )
    if not library.is_file():
        raise FileNotFoundError(f"Native library does not exist: {library}")

    dependencies = inspect(library, args.rid, args.dumpbin)

    if args.rid.startswith("win-"):
        missing = validate_windows_bundled_dependencies(library, dependencies)
        if missing:
            print("Unbundled Windows native dependencies detected:", file=sys.stderr)
            for dependency in missing:
                print(f"- {dependency}", file=sys.stderr)
            print(
                "Non-system Windows DLL dependencies must be packaged beside curl_shim.dll.",
                file=sys.stderr,
            )
            return 1

        print("Native dependency inspection passed.")
        return 0

    forbidden = [
        dependency
        for dependency in dependencies
        if is_forbidden_dependency(dependency, args.rid)
    ]

    if forbidden:
        print("Forbidden dynamic native dependencies detected:", file=sys.stderr)
        for dependency in forbidden:
            print(f"- {dependency}", file=sys.stderr)
        print(
            "curl-impersonate vendored dependencies must be statically linked into curl_shim.",
            file=sys.stderr,
        )
        return 1

    if args.max_glibc and args.rid.startswith("linux-"):
        excessive = validate_glibc_floor(library, args.max_glibc)
        if excessive:
            print(
                f"GLIBC symbol versions above the {args.max_glibc} floor detected:",
                file=sys.stderr,
            )
            for version in excessive:
                print(f"- {version}", file=sys.stderr)
            return 1
        print(f"GLIBC floor check passed (<= {args.max_glibc}).")

    print("Native dependency inspection passed.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"Native dependency inspection failed: {exc}", file=sys.stderr)
        sys.exit(1)
