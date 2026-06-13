#!/usr/bin/env python3
"""Validate NuGet artifacts before public release."""

import argparse
import hashlib
import json
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path


PACKAGE_IDS = (
    "CurlImpersonate",
    "CurlImpersonate.Http",
    "CurlImpersonate.Http.DependencyInjection",
)
EXPECTED_PACKAGE_LICENSE = "MIT"
LICENSE_FILES = ("LICENSE", "LICENSE.md", "LICENSE.txt", "COPYING", "COPYING.md")
NOTICE_PATH = "THIRD-PARTY-NOTICES.md"
NATIVE_ASSETS_PATH = "build/CurlImpersonate.native-assets.json"
NATIVE_MANIFEST_PATH = "runtimes/native-files.json"

BLOCKING_NOTICE_MARKERS = (
    "known upstream components to review",
    "release owners must verify",
    "intentionally conservative",
    "before public release",
    "todo",
    "tbd",
)


def read_package_id(package_path: Path) -> str:
    root = read_nuspec(package_path)
    package_id = find_metadata_text(root, "id")
    if not package_id:
        raise ValueError(f"Could not read package ID from {package_path}")
    return package_id


def read_nuspec(package_path: Path) -> ET.Element:
    with zipfile.ZipFile(package_path) as package:
        nuspec_name = next(name for name in package.namelist() if name.endswith(".nuspec"))
        return ET.fromstring(package.read(nuspec_name))


def metadata(root: ET.Element) -> ET.Element:
    namespace = namespace_for(root)
    node = root.find(f"{namespace}metadata")
    if node is None:
        raise ValueError("nuspec metadata element is missing")
    return node


def namespace_for(root: ET.Element) -> str:
    if root.tag.startswith("{"):
        return root.tag.partition("}")[0] + "}"
    return ""


def find_metadata_text(root: ET.Element, name: str) -> str | None:
    node = metadata(root).find(f"{namespace_for(root)}{name}")
    if node is None or node.text is None:
        return None
    value = node.text.strip()
    return value or None


def find_package(package_dir: Path, package_id: str) -> Path | None:
    matches = sorted(package_dir.glob(f"{package_id}.*.nupkg"))
    for package_path in reversed(matches):
        try:
            if read_package_id(package_path) == package_id:
                return package_path
        except Exception:
            continue
    return None


def library_name_for_rid(rid: str) -> str:
    if rid.startswith("win-"):
        return "curl_shim.dll"
    if rid.startswith("osx-"):
        return "libcurl_shim.dylib"
    if rid.startswith("linux-"):
        return "libcurl_shim.so"
    raise ValueError(f"Unsupported RID: {rid}")


def validate_root_license(repo_root: Path, errors: list[str]) -> None:
    if not any((repo_root / name).is_file() for name in LICENSE_FILES):
        errors.append(
            "Repository license file is missing. Add LICENSE or equivalent before public release."
        )


def validate_notice_file(repo_root: Path, errors: list[str]) -> None:
    notice_file = repo_root / NOTICE_PATH
    if not notice_file.is_file():
        errors.append(f"{NOTICE_PATH} is missing.")
        return

    notice_text = notice_file.read_text(encoding="utf-8").lower()
    for marker in BLOCKING_NOTICE_MARKERS:
        if marker in notice_text:
            errors.append(
                f"{NOTICE_PATH} still contains release-blocking placeholder text: {marker!r}."
            )


def validate_package_metadata(
    package_path: Path,
    package: zipfile.ZipFile,
    errors: list[str],
) -> str | None:
    root = read_nuspec(package_path)
    package_id = find_metadata_text(root, "id")
    version = find_metadata_text(root, "version")
    package_metadata = metadata(root)
    namespace = namespace_for(root)

    for field in ("id", "version", "description", "readme"):
        if not find_metadata_text(root, field):
            errors.append(f"{package_path.name} nuspec is missing {field}.")

    repository = package_metadata.find(f"{namespace}repository")
    if repository is None or not repository.attrib.get("url"):
        errors.append(f"{package_path.name} nuspec is missing repository URL metadata.")

    license_node = package_metadata.find(f"{namespace}license")
    if license_node is None or not (license_node.text or "").strip():
        errors.append(f"{package_path.name} nuspec is missing package license metadata.")
    else:
        license_type = license_node.attrib.get("type")
        license_value = license_node.text.strip()
        if license_type not in {"expression", "file"}:
            errors.append(
                f"{package_path.name} package license type must be expression or file."
            )
        if license_type != "expression" or license_value != EXPECTED_PACKAGE_LICENSE:
            errors.append(
                f"{package_path.name} package license must be the "
                f"{EXPECTED_PACKAGE_LICENSE} expression; found type={license_type!r} "
                f"value={license_value!r}."
            )
        if license_type == "file" and license_value not in package.namelist():
            errors.append(
                f"{package_path.name} package license file {license_value!r} is not packed."
            )

    readme = find_metadata_text(root, "readme")
    if readme and readme not in package.namelist():
        errors.append(f"{package_path.name} package readme {readme!r} is not packed.")

    if NOTICE_PATH not in package.namelist():
        errors.append(f"{package_path.name} does not pack {NOTICE_PATH}.")

    if not package_id:
        errors.append(f"{package_path.name} package ID could not be read.")

    return version


def validate_symbol_package(
    package_dir: Path,
    package_id: str,
    version: str | None,
    errors: list[str],
) -> None:
    if version is None:
        return

    symbols = package_dir / f"{package_id}.{version}.snupkg"
    if not symbols.is_file():
        errors.append(f"Symbol package is missing: {symbols.name}.")


def package_dependencies(root: ET.Element) -> dict[str, str]:
    namespace = namespace_for(root)
    package_metadata = metadata(root)
    dependencies: dict[str, str] = {}

    for dependency in package_metadata.findall(f".//{namespace}dependency"):
        package_id = dependency.attrib.get("id")
        version = dependency.attrib.get("version")
        if package_id and version:
            dependencies[package_id] = version

    return dependencies


def validate_native_assets(package: zipfile.ZipFile, errors: list[str]) -> None:
    names = set(package.namelist())
    if NATIVE_ASSETS_PATH not in names:
        errors.append(f"CurlImpersonate package is missing {NATIVE_ASSETS_PATH}.")
        return
    if NATIVE_MANIFEST_PATH not in names:
        errors.append(f"CurlImpersonate package is missing {NATIVE_MANIFEST_PATH}.")
        return

    assets = json.loads(package.read(NATIVE_ASSETS_PATH))
    manifest = json.loads(package.read(NATIVE_MANIFEST_PATH))

    rid_assets = assets.get("assets")
    if not isinstance(rid_assets, dict) or not rid_assets:
        errors.append(f"{NATIVE_ASSETS_PATH} must contain a non-empty assets object.")
        return

    manifest_entries = manifest.get("files")
    if not isinstance(manifest_entries, list):
        errors.append(f"{NATIVE_MANIFEST_PATH} must contain a files array.")
        return

    manifest_by_path: dict[str, dict] = {}
    for entry in manifest_entries:
        if not isinstance(entry, dict) or not isinstance(entry.get("path"), str):
            errors.append(f"{NATIVE_MANIFEST_PATH} contains an invalid file entry.")
            continue

        path = entry["path"]
        if path in manifest_by_path:
            errors.append(f"{NATIVE_MANIFEST_PATH} contains duplicate entry {path}.")
        manifest_by_path[path] = entry

    for path, entry in sorted(manifest_by_path.items()):
        if path not in names:
            errors.append(f"{NATIVE_MANIFEST_PATH} includes missing package file {path}.")
            continue

        data = package.read(path)
        actual_size = len(data)
        actual_sha256 = hashlib.sha256(data).hexdigest()

        if entry.get("size") != actual_size:
            errors.append(
                f"{path} size mismatch: manifest={entry.get('size')} package={actual_size}."
            )
        if entry.get("sha256") != actual_sha256:
            errors.append(f"{path} SHA-256 does not match {NATIVE_MANIFEST_PATH}.")

    packaged_native_files = sorted(
        name
        for name in names
        if name.startswith("runtimes/")
        and "/native/" in name
        and not name.endswith("/")
    )
    for path in packaged_native_files:
        if path not in manifest_by_path:
            errors.append(f"{path} is missing from {NATIVE_MANIFEST_PATH}.")

    for rid in sorted(rid_assets):
        library_name = library_name_for_rid(rid)
        native_path = f"runtimes/{rid}/native/{library_name}"
        if native_path not in names:
            errors.append(f"CurlImpersonate package is missing {native_path}.")
            continue

        manifest_entry = manifest_by_path.get(native_path)
        if manifest_entry is None:
            errors.append(f"{NATIVE_MANIFEST_PATH} does not include {native_path}.")


def validate_packages(package_dir: Path, errors: list[str]) -> None:
    versions: dict[str, str] = {}
    dependencies_by_package: dict[str, dict[str, str]] = {}

    for package_id in PACKAGE_IDS:
        package_path = find_package(package_dir, package_id)
        if package_path is None:
            errors.append(f"No {package_id} package found in {package_dir}.")
            continue

        root = read_nuspec(package_path)
        package_version = find_metadata_text(root, "version")
        if package_version:
            versions[package_id] = package_version
        dependencies_by_package[package_id] = package_dependencies(root)

        with zipfile.ZipFile(package_path) as package:
            version = validate_package_metadata(package_path, package, errors)
            validate_symbol_package(package_dir, package_id, version, errors)
            if package_id == "CurlImpersonate":
                validate_native_assets(package, errors)

    core_version = versions.get("CurlImpersonate")
    for package_id in PACKAGE_IDS[1:]:
        package_version = versions.get(package_id)
        if core_version and package_version and package_version != core_version:
            errors.append(
                f"CurlImpersonate and {package_id} package versions must match. "
                f"Found CurlImpersonate={core_version}, {package_id}={package_version}."
            )

    http_dependencies = dependencies_by_package.get("CurlImpersonate.Http", {})
    http_core_dependency = http_dependencies.get("CurlImpersonate")
    if core_version and http_core_dependency != core_version:
        errors.append(
            "CurlImpersonate.Http must depend on the exact CurlImpersonate package "
            f"version {core_version}; found {http_core_dependency!r}."
        )

    di_dependencies = dependencies_by_package.get(
        "CurlImpersonate.Http.DependencyInjection", {}
    )
    di_http_dependency = di_dependencies.get("CurlImpersonate.Http")
    http_version = versions.get("CurlImpersonate.Http")
    if http_version and di_http_dependency != http_version:
        errors.append(
            "CurlImpersonate.Http.DependencyInjection must depend on the exact "
            f"CurlImpersonate.Http package version {http_version}; "
            f"found {di_http_dependency!r}."
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package-dir", default="artifacts/package")
    args = parser.parse_args()

    package_dir = Path(args.package_dir).resolve()
    if not package_dir.is_dir():
        raise FileNotFoundError(f"Package directory does not exist: {package_dir}")

    repo_root = Path(__file__).resolve().parents[1]
    errors: list[str] = []

    validate_root_license(repo_root, errors)
    validate_notice_file(repo_root, errors)
    validate_packages(package_dir, errors)

    if errors:
        print("Release readiness validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Release readiness validation passed.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"Release readiness validation failed: {exc}", file=sys.stderr)
        sys.exit(1)
