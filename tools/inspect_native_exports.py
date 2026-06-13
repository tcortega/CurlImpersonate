#!/usr/bin/env python3
"""Validate exported native symbols against the intended shim ABI."""

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path


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


def normalize_symbol(value: str) -> str:
    symbol = value.removeprefix("_")
    symbol = symbol.split("@@", 1)[0].split("@", 1)[0]
    return symbol


def expected_exports(rid: str) -> set[str]:
    symbols_file = Path("native/shim.exp")
    symbols: set[str] = set()

    for line in symbols_file.read_text(encoding="utf-8").splitlines():
        value = line.strip()
        if not value or value.startswith("#"):
            continue
        symbol = normalize_symbol(value)
        if rid.startswith("win-") and not symbol.startswith("shim_"):
            continue
        symbols.add(symbol)

    return symbols


def inspect_unix(command: list[str]) -> set[str]:
    output = run(command)
    print(output, end="")
    symbols: set[str] = set()

    for line in output.splitlines():
        parts = line.split()
        if not parts:
            continue
        symbols.add(normalize_symbol(parts[-1]))

    return symbols


def inspect_windows(library: Path, dumpbin: str | None) -> set[str]:
    dumpbin_path = dumpbin or os.environ.get("DUMPBIN") or shutil.which("dumpbin")
    if not dumpbin_path:
        raise RuntimeError("dumpbin.exe was not found. Pass --dumpbin or set DUMPBIN.")

    output = run([dumpbin_path, "/EXPORTS", str(library)])
    print(output, end="")
    symbols: set[str] = set()

    for line in output.splitlines():
        parts = line.split()
        if len(parts) < 4 or not parts[0].isdigit():
            continue
        # Skip "N number of functions" / "N number of names" header lines
        if parts[1] == "number":
            continue
        symbols.add(normalize_symbol(parts[3]))

    return symbols


def inspect_exports(library: Path, rid: str, dumpbin: str | None) -> set[str]:
    if rid.startswith("linux-"):
        return inspect_unix(["nm", "-D", "--defined-only", str(library)])
    if rid.startswith("osx-"):
        return inspect_unix(["nm", "-gU", str(library)])
    if rid.startswith("win-"):
        return inspect_windows(library, dumpbin)
    raise ValueError(f"Unsupported RID: {rid}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True)
    parser.add_argument("--library")
    parser.add_argument("--dumpbin")
    args = parser.parse_args()

    library = Path(
        args.library
        or f"runtimes/{args.rid}/native/{library_name_for_rid(args.rid)}"
    )
    if not library.is_file():
        raise FileNotFoundError(f"Native library does not exist: {library}")

    expected = expected_exports(args.rid)
    actual = inspect_exports(library, args.rid, args.dumpbin)

    missing = sorted(expected - actual)
    unexpected = sorted(actual - expected)

    if missing or unexpected:
        print("Native export inspection failed:", file=sys.stderr)
        if missing:
            print("Missing expected exports:", file=sys.stderr)
            for symbol in missing:
                print(f"- {symbol}", file=sys.stderr)
        if unexpected:
            print("Unexpected exports:", file=sys.stderr)
            for symbol in unexpected:
                print(f"- {symbol}", file=sys.stderr)
        return 1

    print("Native export inspection passed.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"Native export inspection failed: {exc}", file=sys.stderr)
        sys.exit(1)
