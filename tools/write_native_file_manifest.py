#!/usr/bin/env python3
"""Write a manifest for runtime native files staged for packaging."""

import argparse
import hashlib
import json
from pathlib import Path


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtimes-root", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    root = Path(args.runtimes_root).resolve()
    if not root.is_dir():
        raise FileNotFoundError(f"Runtimes root does not exist: {root}")

    files = []
    for path in sorted(root.glob("*/native/*")):
        if not path.is_file():
            continue
        rid = path.relative_to(root).parts[0]
        files.append({
            "rid": rid,
            "path": str(path.relative_to(root.parent)),
            "size": path.stat().st_size,
            "sha256": sha256(path),
        })

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"files": files}, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {output} with {len(files)} native file(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
