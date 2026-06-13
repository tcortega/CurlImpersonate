# Releasing CurlImpersonate

This project is MIT licensed. Public package publication still requires every
release gate below to pass for the exact package artifacts being published.

## Versioning

- Use SemVer for both `CurlImpersonate` and `CurlImpersonate.Http`.
- Keep both packages on the same version.
- Do not publish a stable `1.0.0` package until the production-readiness audit
  definition of done is satisfied.
- Use prerelease versions for hardening builds, such as `1.0.0-rc.1`.
- The repository default package version is `1.0.0-rc.1`; release builds may
  override it with `/p:Version=...` after the release gates pass.
- CI tag builds derive the package version from the tag by removing the leading
  `v`, so `v1.0.0-rc.1` builds `1.0.0-rc.1`.
- Each package version is coupled to the pinned `lexiforest/curl-impersonate`
  release recorded in `native/native-assets.json`. Bumping the pinned native
  release is at least a minor version bump; browser profile additions or
  fingerprint behavior changes must be called out in the release notes.

## Release Gates

Before creating a release tag:

```bash
dotnet build CurlImpersonate.slnx
dotnet test CurlImpersonate.slnx
python3 native/scripts/build_shim.py
dotnet pack src/CurlImpersonate/CurlImpersonate.csproj -c Release -o artifacts/package
dotnet pack src/CurlImpersonate.Http/CurlImpersonate.Http.csproj -c Release -o artifacts/package
python3 tools/package_smoke_test.py \
  --package-dir artifacts/package \
  --publish-aot-rid <current-rid>
python3 tools/inspect_native_dependencies.py --rid <current-rid>
python3 tools/inspect_native_exports.py --rid <current-rid>
python3 tools/validate_benchmark_results.py
python3 tools/validate_release_readiness.py --package-dir artifacts/package
```

Native release CI must also pass for every supported RID:

- `linux-x64`
- `linux-arm64`
- `linux-musl-x64`
- `linux-musl-arm64`
- `osx-x64`
- `osx-arm64`
- `win-x64`
- `win-arm64`

The package job must produce:

- `CurlImpersonate.*.nupkg`
- `CurlImpersonate.Http.*.nupkg`
- `CurlImpersonate.Http.DependencyInjection.*.nupkg`
- matching `.snupkg` symbol packages
- `runtimes/{rid}/native/` assets for every supported RID
- `runtimes/native-files.json` with native file sizes and SHA-256 hashes
- passing native dependency inspection for every supported RID
- passing native export allowlist inspection for every supported RID
- passing benchmark budget validation for BenchmarkDotNet CSV results

## Tagged Publish

Pushing a `v*` tag builds and validates the release artifacts but does not
publish them. Tag builds run `tools/validate_release_readiness.py`, which
fails closed when package license metadata is not the MIT expression, the
root license file, third-party notices, symbol packages, or native RID assets
are missing.

Publication is a separate human-triggered step: dispatch the CI workflow on
the release tag ref with the `publish` input set to true. The publish job
also waits for the tag's external fingerprint validation and benchmark
validation jobs before pushing artifacts to NuGet.org.

```bash
gh workflow run ci.yml --ref vMAJOR.MINOR.PATCH -f publish=true
```

Publishing uses nuget.org Trusted Publishing (OIDC), not a long-lived API
key. One-time setup:

1. On nuget.org: account menu > Trusted Publishing > add a policy with
   Repository Owner `tcortega`, Repository `CurlImpersonate`, Workflow File
   `ci.yml`, Environment empty.
2. In the GitHub repository, set the `NUGET_USER` secret to the nuget.org
   profile name that owns the policy (not an email address).

The publish job exchanges the workflow's OIDC token for a one-hour API key
via `NuGet/login`.

Release tag format:

```text
vMAJOR.MINOR.PATCH
vMAJOR.MINOR.PATCH-prerelease.N
```

## Native Asset Updates

When updating curl-impersonate:

1. Verify the latest stable `lexiforest/curl-impersonate` release.
2. Update `native/native-assets.json`.
3. Update `CURL_IMPERSONATE_VERSION` consumers through the manifest, not a
   second hard-coded constant.
4. Run `python3 native/scripts/fetch_dependencies.py` and verify the pinned
   SHA-256 fails closed on mismatch.
5. Run native dependency inspection in CI (`ldd`, `readelf`, `otool`, or
   `dumpbin`) before publishing.

Pre-release upstream tags, including `v2.0.0a*`, are intentionally outside the
stable package line until upstream publishes a stable v2 release and the native
asset layout, exported shim ABI, browser target list, and package smoke tests are
revalidated for every supported RID.

## Non-Negotiable Blockers

Do not publish when any of these are true:

- root project license is missing
- package license metadata is not the MIT expression
- third-party native notices are missing
- any supported RID is missing from the package
- package smoke or publish validation fails
- fingerprint validation has not been run or explicitly documented as blocked
- full build/test verification has not been attempted
