#!/usr/bin/env python3
"""Validate BenchmarkDotNet CSV output against release budgets."""

import argparse
import csv
import json
import sys
from pathlib import Path


SMALL_GET_METHOD = "Curl_Get100Bytes"
SMALL_GET_ALLOCATED_LIMIT_BYTES = 32 * 1024
MAX_BASELINE_REGRESSION = 1.15

# Parity versus the SocketsHttpHandler-backed baseline, when present in the
# same results. Curl allocates a stable ~11.3 KB per small GET while the
# native baseline varies by runner (observed 2.8 KB to 3.8 KB), so the
# allocated limit carries headroom for baseline variance, not curl growth;
# the absolute curl budget above is the regression backstop.
PARITY_PAIRS = (("Curl_Get100Bytes", "Native_Get100Bytes"),)
PARITY_MAX_MEAN_RATIO = 2.0
PARITY_MAX_ALLOCATED_RATIO = 6.0


def parse_bytes(value: str) -> float:
    parts = value.replace(",", "").strip().split()
    if not parts:
        raise ValueError("empty byte value")

    number = float(parts[0])
    unit = parts[1].lower() if len(parts) > 1 else "b"
    multipliers = {
        "b": 1,
        "byte": 1,
        "bytes": 1,
        "kb": 1024,
        "kib": 1024,
        "mb": 1024 * 1024,
        "mib": 1024 * 1024,
        "gb": 1024 * 1024 * 1024,
        "gib": 1024 * 1024 * 1024,
    }
    try:
        return number * multipliers[unit]
    except KeyError as exc:
        raise ValueError(f"unsupported byte unit: {unit}") from exc


def parse_duration_microseconds(value: str) -> float:
    parts = value.replace(",", "").strip().split()
    if not parts:
        raise ValueError("empty duration value")

    number = float(parts[0])
    unit = parts[1].lower() if len(parts) > 1 else "ns"
    multipliers = {
        "ns": 0.001,
        "us": 1,
        "\u00b5s": 1,
        "\u03bcs": 1,
        "ms": 1000,
        "s": 1_000_000,
    }
    try:
        return number * multipliers[unit]
    except KeyError as exc:
        raise ValueError(f"unsupported duration unit: {unit}") from exc


def benchmark_key(csv_path: Path, row: dict[str, str]) -> str:
    return f"{csv_path.stem}:{row.get('Method', '').strip()}"


def load_rows(results_dir: Path) -> list[tuple[Path, dict[str, str]]]:
    rows: list[tuple[Path, dict[str, str]]] = []

    for csv_path in sorted(results_dir.glob("*-report.csv")):
        with csv_path.open(newline="", encoding="utf-8-sig") as csv_file:
            for row in csv.DictReader(csv_file):
                rows.append((csv_path, row))

    return rows


def row_metrics(row: dict[str, str]) -> dict[str, float]:
    metrics: dict[str, float] = {}
    allocated = _measured(row.get("Allocated"))
    mean = _measured(row.get("Mean"))

    if allocated:
        metrics["allocated_bytes"] = parse_bytes(allocated)
    if mean:
        metrics["mean_us"] = parse_duration_microseconds(mean)

    return metrics


def _measured(value: str | None) -> str | None:
    # BenchmarkDotNet writes NA or - when a measurement is unavailable.
    if value is None or value.strip() in ("", "NA", "-", "?"):
        return None
    return value


def load_baseline(path: Path | None) -> dict[str, dict[str, float]]:
    if path is None:
        return {}

    with path.open(encoding="utf-8") as baseline_file:
        data = json.load(baseline_file)

    if not isinstance(data, dict):
        raise ValueError("benchmark baseline must be a JSON object")

    baseline: dict[str, dict[str, float]] = {}
    for key, metrics in data.items():
        if not isinstance(key, str) or not isinstance(metrics, dict):
            raise ValueError("benchmark baseline entries must be keyed metric objects")
        baseline[key] = {
            metric: float(value)
            for metric, value in metrics.items()
            if isinstance(metric, str)
        }

    return baseline


def write_baseline(
    path: Path,
    rows: list[tuple[Path, dict[str, str]]],
) -> None:
    baseline = {
        benchmark_key(csv_path, row): row_metrics(row)
        for csv_path, row in rows
        if row.get("Method")
    }

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(baseline, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def validate(
    rows: list[tuple[Path, dict[str, str]]],
    baseline: dict[str, dict[str, float]],
) -> list[str]:
    errors: list[str] = []
    found_small_get = False

    for csv_path, row in rows:
        method = row.get("Method", "").strip()
        if not method:
            continue

        key = benchmark_key(csv_path, row)
        metrics = row_metrics(row)
        allocated = metrics.get("allocated_bytes")

        if method == SMALL_GET_METHOD:
            found_small_get = True
            if allocated is None:
                errors.append(f"{key} is missing Allocated column data.")
            elif allocated > SMALL_GET_ALLOCATED_LIMIT_BYTES:
                errors.append(
                    f"{key} allocated {allocated:.0f} bytes; "
                    f"limit is {SMALL_GET_ALLOCATED_LIMIT_BYTES} bytes."
                )

        baseline_metrics = baseline.get(key)
        if baseline_metrics is None:
            continue

        baseline_allocated = baseline_metrics.get("allocated_bytes")
        if allocated is not None and baseline_allocated is not None:
            limit = baseline_allocated * MAX_BASELINE_REGRESSION
            if allocated > limit:
                errors.append(
                    f"{key} allocated {allocated:.0f} bytes; "
                    f"baseline regression limit is {limit:.0f} bytes."
                )

        mean_us = metrics.get("mean_us")
        baseline_mean_us = baseline_metrics.get("mean_us")
        if mean_us is not None and baseline_mean_us is not None:
            limit = baseline_mean_us * MAX_BASELINE_REGRESSION
            if mean_us > limit:
                errors.append(
                    f"{key} mean {mean_us:.1f} us; "
                    f"baseline regression limit is {limit:.1f} us."
                )

    if not found_small_get:
        errors.append(
            f"No BenchmarkDotNet row found for required method {SMALL_GET_METHOD}."
        )

    validate_parity(rows, errors)

    return errors


def validate_parity(
    rows: list[tuple[Path, dict[str, str]]],
    errors: list[str],
) -> None:
    metrics_by_method: dict[str, dict[str, float]] = {}
    for _, row in rows:
        method = row.get("Method", "").strip()
        if method:
            metrics_by_method[method] = row_metrics(row)

    for curl_method, native_method in PARITY_PAIRS:
        curl = metrics_by_method.get(curl_method)
        native = metrics_by_method.get(native_method)
        if curl is None or native is None:
            print(
                f"Parity check skipped for {curl_method}: "
                f"baseline {native_method} not in this run."
            )
            continue

        checks = (
            ("mean_us", PARITY_MAX_MEAN_RATIO, "mean"),
            ("allocated_bytes", PARITY_MAX_ALLOCATED_RATIO, "allocated"),
        )
        for metric, max_ratio, label in checks:
            curl_value = curl.get(metric)
            native_value = native.get(metric)
            if curl_value is None or native_value is None or native_value == 0:
                errors.append(
                    f"Parity check for {curl_method} is missing {label} data."
                )
                continue
            ratio = curl_value / native_value
            if ratio > max_ratio:
                errors.append(
                    f"{curl_method} {label} is {ratio:.2f}x {native_method}; "
                    f"parity limit is {max_ratio:.2f}x."
                )
            else:
                print(
                    f"Parity {label} for {curl_method}: {ratio:.2f}x "
                    f"{native_method} (limit {max_ratio:.2f}x)."
                )


def print_summary(rows: list[tuple[Path, dict[str, str]]]) -> None:
    for csv_path, row in rows:
        method = row.get("Method", "").strip()
        if not method:
            continue
        metrics = row_metrics(row)
        details = []
        if "mean_us" in metrics:
            details.append(f"mean={metrics['mean_us']:.1f} us")
        if "allocated_bytes" in metrics:
            details.append(f"allocated={metrics['allocated_bytes']:.0f} bytes")
        print(f"{benchmark_key(csv_path, row)}: {', '.join(details)}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--results-dir", default="BenchmarkDotNet.Artifacts/results")
    parser.add_argument("--baseline")
    parser.add_argument("--write-baseline")
    args = parser.parse_args()

    results_dir = Path(args.results_dir)
    if not results_dir.is_dir():
        raise FileNotFoundError(
            f"Benchmark results directory does not exist: {results_dir}"
        )

    rows = load_rows(results_dir)
    if not rows:
        raise RuntimeError(f"No BenchmarkDotNet CSV results found in {results_dir}")

    if args.write_baseline:
        write_baseline(Path(args.write_baseline), rows)

    errors = validate(rows, load_baseline(Path(args.baseline) if args.baseline else None))
    print_summary(rows)

    if errors:
        print("Benchmark validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Benchmark validation passed.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"Benchmark validation failed: {exc}", file=sys.stderr)
        sys.exit(1)
