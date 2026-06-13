#!/usr/bin/env python3
"""Smoke test CurlImpersonate packages from a local package directory."""

import argparse
import hashlib
import json
import os
import platform
import shutil
import subprocess
import sys
import tempfile
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path


def run(command: list[str], cwd: Path, env: dict[str, str]) -> None:
    print(f"+ {' '.join(command)}", flush=True)
    subprocess.run(command, cwd=cwd, env=env, check=True)


def current_rid() -> tuple[str, str]:
    system = platform.system().lower()
    machine = platform.machine().lower()
    arch = {
        "amd64": "x64",
        "x86_64": "x64",
        "aarch64": "arm64",
        "arm64": "arm64",
    }.get(machine, machine)

    if system == "darwin":
        return f"osx-{arch}", "libcurl_shim.dylib"
    if system == "linux":
        prefix = "linux-musl" if is_musl_linux() else "linux"
        return f"{prefix}-{arch}", "libcurl_shim.so"
    if system == "windows":
        return f"win-{arch}", "curl_shim.dll"

    raise RuntimeError(f"Unsupported smoke-test platform: {system}/{machine}")


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


def library_name_for_rid(rid: str) -> str:
    if rid.startswith("win-"):
        return "curl_shim.dll"
    if rid.startswith("osx-"):
        return "libcurl_shim.dylib"
    if rid.startswith("linux-"):
        return "libcurl_shim.so"
    raise RuntimeError(f"Unsupported RID: {rid}")


def find_package(package_dir: Path, package_id: str) -> Path:
    matches = sorted(package_dir.glob(f"{package_id}.*.nupkg"))
    exact_matches = [package for package in matches if read_package_id(package) == package_id]
    if not exact_matches:
        raise FileNotFoundError(f"No {package_id} package found in {package_dir}")
    return exact_matches[-1]


def read_package_id(package_path: Path) -> str:
    with zipfile.ZipFile(package_path) as package:
        nuspec_name = next(name for name in package.namelist() if name.endswith(".nuspec"))
        root = ET.fromstring(package.read(nuspec_name))

    namespace = {"nuget": root.tag.partition("}")[0].removeprefix("{")}
    package_id = root.findtext("nuget:metadata/nuget:id", namespaces=namespace)
    if not package_id:
        raise ValueError(f"Could not read package ID from {package_path}")
    return package_id


def infer_version(package_path: Path, package_id: str) -> str:
    prefix = f"{package_id}."
    suffix = ".nupkg"
    name = package_path.name
    if not name.startswith(prefix) or not name.endswith(suffix):
        raise ValueError(f"Unexpected package name: {name}")
    return name[len(prefix):-len(suffix)]


def ensure_current_native_asset(package_dir: Path) -> None:
    rid, _ = current_rid()
    ensure_native_asset(package_dir, rid)


def executable_name(app_dir: Path, rid: str) -> str:
    return f"{app_dir.name}.exe" if rid.startswith("win-") else app_dir.name


def ensure_native_asset(package_dir: Path, rid: str) -> tuple[str, int, str]:
    library_name = library_name_for_rid(rid)

    expected = f"runtimes/{rid}/native/{library_name}"
    entries = native_manifest_entries_for_rid(package_dir, rid)
    matching_entries = [entry for entry in entries if entry[0] == expected]
    if not matching_entries:
        raise RuntimeError(f"CurlImpersonate package does not contain {expected}")

    return matching_entries[0]


def native_manifest_entries_for_rid(
    package_dir: Path,
    rid: str,
) -> list[tuple[str, int, str]]:
    core_package = find_package(package_dir, "CurlImpersonate")
    prefix = f"runtimes/{rid}/native/"

    with zipfile.ZipFile(core_package) as package:
        manifest = read_native_file_manifest(package)
        entries: list[tuple[str, int, str]] = []

        for entry in manifest:
            path = entry.get("path")
            if isinstance(path, str) and path.startswith(prefix):
                entries.append(verify_native_file_manifest_entry(package, entry))

        if not entries:
            raise RuntimeError(
                f"{core_package.name} does not contain native files for {rid}"
            )

        return entries


def read_native_file_manifest(package: zipfile.ZipFile) -> list[dict]:
    manifest_path = "runtimes/native-files.json"
    if manifest_path not in package.namelist():
        raise RuntimeError(f"{package.filename} does not contain {manifest_path}")

    manifest = json.loads(package.read(manifest_path))
    entries = manifest.get("files")
    if not isinstance(entries, list):
        raise RuntimeError(f"{manifest_path} must contain a files array")

    return entries


def verify_native_file_manifest_entry(
    package: zipfile.ZipFile,
    entry: dict,
) -> tuple[str, int, str]:
    expected_path = entry.get("path")
    if not isinstance(expected_path, str):
        raise RuntimeError("Native file manifest entry is missing a path")
    if expected_path not in package.namelist():
        raise RuntimeError(f"{package.filename} does not contain {expected_path}")

    data = package.read(expected_path)
    actual_size = len(data)
    actual_sha256 = hashlib.sha256(data).hexdigest()

    if entry.get("size") != actual_size:
        raise RuntimeError(
            f"{expected_path} size mismatch. "
            f"Manifest: {entry.get('size')}, package: {actual_size}"
        )

    if entry.get("sha256") != actual_sha256:
        raise RuntimeError(
            f"{expected_path} SHA-256 mismatch. "
            f"Manifest: {entry.get('sha256')}, package: {actual_sha256}"
        )

    return expected_path, actual_size, actual_sha256


def ensure_published_native_assets(
    package_dir: Path,
    output_dir: Path,
    rid: str,
) -> None:
    entries = native_manifest_entries_for_rid(package_dir, rid)

    for path, expected_size, expected_sha256 in entries:
        published = output_dir / Path(path).name

        if not published.is_file():
            raise RuntimeError(
                f"{output_dir} does not contain published native asset {published.name}"
            )

        data = published.read_bytes()
        actual_size = len(data)
        actual_sha256 = hashlib.sha256(data).hexdigest()

        if actual_size != expected_size:
            raise RuntimeError(
                f"{published} size mismatch. "
                f"Package: {expected_size}, publish output: {actual_size}"
            )

        if actual_sha256 != expected_sha256:
            raise RuntimeError(f"{published} SHA-256 does not match packaged native asset")


def write_nuget_config(package_dir: Path, app_dir: Path) -> None:
    nuget_config = f"""<?xml version=\"1.0\" encoding=\"utf-8\"?>
<configuration>
  <packageSources>
    <clear />
    <add key=\"local\" value=\"{package_dir}\" />
    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />
  </packageSources>
</configuration>
"""
    (app_dir / "NuGet.Config").write_text(nuget_config)


def write_program(app_dir: Path) -> None:
    source = """using System.Net;
using System.Net.Sockets;
using System.Text;
using CurlImpersonate;
using CurlImpersonate.Http;

using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var endpoint = (IPEndPoint)listener.LocalEndpoint;
var uri = new Uri($"http://127.0.0.1:{endpoint.Port}/");

var serverTask = Task.Run(async () =>
{
    using var connection = await listener.AcceptTcpClientAsync();
    await using var stream = connection.GetStream();
    await ReadRequestHeadersAsync(stream);
    var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\\r\\nContent-Length: 0\\r\\nConnection: close\\r\\n\\r\\n");
    await stream.WriteAsync(response);
});

using var handler = new CurlHandler();
using var client = new HttpClient(handler);
using var response = await client.GetAsync(uri);
await serverTask;

response.EnsureSuccessStatusCode();
Console.WriteLine(CurlGlobal.Version);

static async Task ReadRequestHeadersAsync(Stream stream)
{
    var buffer = new byte[4096];
    var received = new StringBuilder();

    while (true)
    {
        var read = await stream.ReadAsync(buffer);
        if (read == 0)
            break;

        received.Append(Encoding.ASCII.GetString(buffer, 0, read));
        if (received.ToString().Contains("\\r\\n\\r\\n", StringComparison.Ordinal))
            return;
    }
}
"""
    (app_dir / "Program.cs").write_text(source)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package-dir", default="artifacts/package")
    parser.add_argument("--framework", default="net10.0")
    parser.add_argument("--publish-rid", action="append", default=[])
    parser.add_argument("--publish-aot-rid", action="append", default=[])
    parser.add_argument("--keep-temp", action="store_true")
    args = parser.parse_args()

    package_dir = Path(args.package_dir).resolve()
    if not package_dir.is_dir():
        raise FileNotFoundError(f"Package directory does not exist: {package_dir}")

    ensure_current_native_asset(package_dir)
    for rid in args.publish_rid + args.publish_aot_rid:
        ensure_native_asset(package_dir, rid)

    http_package = find_package(package_dir, "CurlImpersonate.Http")
    version = infer_version(http_package, "CurlImpersonate.Http")

    temp_dir = Path(tempfile.mkdtemp(prefix="curlimpersonate-package-smoke-"))
    app_dir = temp_dir / "app"
    env = os.environ.copy()
    env["NUGET_PACKAGES"] = str(temp_dir / "nuget-packages")
    env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"

    try:
        run(["dotnet", "new", "console", "--framework", args.framework, "--no-restore", "-o", str(app_dir)], temp_dir, env)
        write_nuget_config(package_dir, app_dir)
        run(["dotnet", "add", "package", "CurlImpersonate.Http", "--version", version], app_dir, env)
        write_program(app_dir)
        run(["dotnet", "run", "--no-restore"], app_dir, env)

        for rid in args.publish_rid:
            output_dir = app_dir / "publish" / rid
            run(
                [
                    "dotnet", "publish",
                    "--configuration", "Release",
                    "--runtime", rid,
                    "--self-contained", "false",
                    "--output", str(output_dir),
                ],
                app_dir,
                env,
            )
            ensure_published_native_assets(package_dir, output_dir, rid)

        host_rid, _ = current_rid()
        for rid in args.publish_aot_rid:
            output_dir = app_dir / "publish-aot" / rid
            run(
                [
                    "dotnet", "publish",
                    "--configuration", "Release",
                    "--runtime", rid,
                    "--self-contained", "true",
                    "--output", str(output_dir),
                    "/p:PublishAot=true",
                ],
                app_dir,
                env,
            )
            ensure_published_native_assets(package_dir, output_dir, rid)

            if rid == host_rid:
                run([str(output_dir / executable_name(app_dir, rid))], output_dir, env)
    finally:
        if args.keep_temp:
            print(f"Temporary app kept at {app_dir}")
        else:
            shutil.rmtree(temp_dir, ignore_errors=True)

    print("Package smoke test passed.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"Package smoke test failed: {exc}", file=sys.stderr)
        sys.exit(1)
