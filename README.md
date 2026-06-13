# CurlImpersonate

CurlImpersonate is a .NET wrapper for the maintained `lexiforest/curl-impersonate`
fork. It provides low-level native bindings plus an `HttpMessageHandler` that can
send requests through curl-impersonate while preserving browser TLS and HTTP
fingerprint behavior.

## Packages

Use `CurlImpersonate.Http` for normal HTTP client usage. It depends on
`CurlImpersonate`, which carries the native runtime assets.

```bash
dotnet add package CurlImpersonate.Http
```

```csharp
using CurlImpersonate.Http;

using var client = new HttpClient(new CurlHandler());
using var response = await client.GetAsync("https://example.com");
response.EnsureSuccessStatusCode();
```

## Migrating From HttpClientHandler

`CurlHandlerOptions` mirrors the familiar `HttpClientHandler` surface:
`AllowAutoRedirect`, `MaxAutomaticRedirections`, `CookieContainer`,
`UseCookies`, `Proxy` (`IWebProxy` with bypass and credentials), `UseProxy`,
and `MaxConnectionsPerServer` keep their names and semantics. The full
property mapping table, including what is intentionally unsupported and why,
is in [the migration guide](docs/httpclienthandler-migration.md).

## IHttpClientFactory And Dependency Injection

Install `CurlImpersonate.Http.DependencyInjection` to wire `CurlHandler` into
`IHttpClientFactory` pipelines:

```csharp
services.AddCurlImpersonateClient("scraper", options =>
{
    options.BrowserProfile = BrowserProfile.Chrome142;
});
```

Or attach it to any client builder, composing with delegating handlers such as
Polly resilience or auth middleware:

```csharp
services.AddHttpClient<MyApiClient>()
    .AddStandardResilienceHandler()
    .AddCurlImpersonate(options => options.BrowserProfile = BrowserProfile.Firefox144);
```

Delegating handlers run before the curl transport. The factory rotates primary
handlers per its handler lifetime; curl re-resolves DNS per connection and
honors `PooledConnectionLifetime`, so longer handler lifetimes via
`SetHandlerLifetime` are appropriate for busy clients.

One handler can serve multiple impersonation profiles. Override the browser
profile per request through `HttpRequestMessage.Options`:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, url);
request.Options.Set(CurlRequestOptions.BrowserProfile, BrowserProfile.Safari2601);
using var response = await client.SendAsync(request);
```

## Native Assets

The package uses the .NET `runtimes/{rid}/native/` convention. Consumers should
not need to run native fetch or build scripts after installing a complete NuGet
package.

Supported release RID matrix:

- `linux-x64`
- `linux-arm64`
- `linux-musl-x64`
- `linux-musl-arm64`
- `osx-x64`
- `osx-arm64`
- `win-x64`
- `win-arm64`

The native shim targets `lexiforest/curl-impersonate` `v1.5.6`.

The `linux-x64` and `linux-arm64` release binaries are linked in a
manylinux2014 environment and require glibc 2.17 or newer, the same floor as
the upstream curl-impersonate release binaries. CI enforces the floor on every
build. The `linux-musl-*` binaries target musl distributions such as Alpine.

For local development, run:

```bash
python3 native/scripts/build_shim.py
```

The script fetches pinned native dependencies, builds `curl_shim`, and copies the
current platform binary into `runtimes/{rid}/native/`.

## Package Verification

After packing, run the package smoke test from the repository root:

```bash
dotnet pack CurlImpersonate.slnx -c Release -o artifacts/package
python3 tools/package_smoke_test.py \
  --package-dir artifacts/package \
  --publish-aot-rid <current-rid>
```

The smoke test creates a clean temporary console app, installs
`CurlImpersonate.Http` from the local package output, and verifies that the
packaged native library can execute a loopback `CurlHandler` request and report
`CurlGlobal.Version`. Publish checks also verify that every native file listed
for the selected RID is copied to the publish output and matches the packaged
native manifest hash. The optional AOT publish flag validates a native-compiled
consumer for the current host RID.

Use `CurlGlobal.Version` and `CurlGlobal.NativeRuntimeIdentifier` in diagnostics
to report the loaded curl-impersonate build and the packaged native RID selected
by the resolver.

Release gates and versioning policy are documented in `RELEASING.md`.

## Certificate Trust

Certificate verification is enabled by default. Do not use
`InsecureSkipVerify` in production. On Linux and macOS the bundled
curl-impersonate build uses the system CA bundle; on Windows the handler uses
the operating system certificate store by default. If a platform or deployment
needs an explicit trust store, configure `CurlHandlerOptions.CaInfo` with a PEM
CA bundle or `CurlHandlerOptions.CaPath` with a directory of PEM CA
certificates. HTTPS proxy verification can be configured separately with
`ProxyCaInfo` and `ProxyCaPath`.

## Proxies

Set `CurlHandlerOptions.ProxyUri` or `CurlHandlerOptions.Proxy` to an HTTP,
HTTPS, SOCKS4, or SOCKS5 proxy URL. Set only one proxy option; configuring both
is rejected. `ProxyCredentials` maps to separate `CURLOPT_PROXYUSERNAME` and
`CURLOPT_PROXYPASSWORD` values; `ProxyAuth` defaults to `CurlProxyAuth.Any` so
libcurl can select from schemes advertised by the proxy. Set
`ProxyAuth = CurlProxyAuth.Basic` when a proxy requires preemptive Basic
credentials. `NoProxy` maps to libcurl's no-proxy host list.

## HTTP Versions

`CurlHandlerOptions.VersionPolicy` defaults to
`HttpVersionPolicy.RequestVersionOrHigher` for requests that keep .NET's default
policy. Set `HttpRequestMessage.Version` and `VersionPolicy` for per-request
control.

HTTP/3 must be requested explicitly by setting `CurlHandlerOptions.EnableHttp3`
to `true` and using `HttpVersion.Version30`. `RequestVersionExact` maps to
HTTP/3-only; `RequestVersionOrHigher` maps to HTTP/3 with libcurl fallback.
HTTP/3 requires native QUIC support and UDP reachability, so proxy setups must
support UDP/HTTP/3 traffic.

## Event Loop Model

Each handler drives its transfers through a curl multi event loop. By default
(`UseSharedEventLoop = true`) all handlers in the process share one loop
thread; setting it to false gives a handler its own thread. Benchmarked at
concurrency 10 the two are statistically identical (477 us vs 482 us per
10-request batch, equal allocations), so the shared loop stays the default
because it holds the thread count constant no matter how many handlers exist.
Use a dedicated loop only to isolate a latency-critical handler from other
handlers' callback work.

## Connection Limits

`CurlHandlerOptions.MaxTotalConnections`, `MaxConnectionsPerHost`, and
`MaxConnects` map to libcurl multi-handle limits. Setting any of these uses an
owned event loop for that handler because multi limits are scoped to a single
multi handle. `PooledConnectionLifetime` maps to `CURLOPT_MAXLIFETIME_CONN` so
old connections are not reused indefinitely.

## Request And Response Buffering

`CurlHandler` buffers request and response bodies by default. The default
`MaxRequestBodyBytes` and `MaxResponseBodyBytes` limit is 64 MiB; set either
option to `null` to remove the corresponding limit. `GET`, `POST`, `PUT`,
`PATCH`, `DELETE`, `OPTIONS`, and custom methods support buffered bodies. Set
`StreamRequestBodies = true` with `FollowRedirects = false` to stream request
content through curl's upload callback instead of buffering it first. Set
`StreamResponseBodies = true` with `FollowRedirects = false` and call
`HttpClient.SendAsync` with `HttpCompletionOption.ResponseHeadersRead` to read
response bodies as curl receives them. Explicit empty `POST` content sends
`Content-Length: 0`. `HEAD` requests reject request content and return an empty
response body.

## Redirects

Redirect following is handled by `CurlHandler` when `FollowRedirects` is
enabled. The handler resolves relative `Location` values, limits redirects to
`http` and `https`, enforces `MaxRedirects`, stores cookies after each hop, and
strips `Authorization`, `Proxy-Authorization`, and explicit `Cookie` headers
when the redirect crosses origins. `POST` redirects with `301`, `302`, or `303`
are rewritten to `GET`; `307` and `308` preserve method and buffered body. The
final effective URI and redirect count are available via `TryGetEffectiveUri`
and `TryGetRedirectCount`.

## Response Metadata

Responses expose libcurl metadata through extension methods:
`TryGetEffectiveUri`, `TryGetRedirectCount`, and `TryGetTransferMetrics`.
Transfer metrics include timing phases, connection counts, request/response
header byte counts, and local/remote endpoint details reported by libcurl.

For low-level troubleshooting, set `CurlHandlerOptions.EnableCurlDebug = true`
and provide `DebugCallback` to receive libcurl verbose events such as text,
incoming/outgoing headers, body bytes, and TLS bytes. Debug callbacks are
disabled by default and should avoid recording sensitive data in production.

## Fingerprint Overrides

Named browser profiles should be preferred for normal use. Advanced callers can
override selected fingerprint pieces through `CurlHandlerOptions.Fingerprint`,
including JA3 TLS strings, HTTP/2 settings, HTTP/3 settings, TLS extension
controls, Akamai HTTP/2 strings, and PERK-style HTTP/3 strings. JA3 mapping is
limited to TLS 1.2-compatible strings and rejects unsupported cipher, curve, and
extension-toggle requests before sending. Apply custom fingerprints with care:
they intentionally override parts of the selected browser profile.

## Fingerprint Validation

External fingerprint services can rate-limit or change independently of this
library, so those tests are opt-in locally and run from the scheduled/manual CI
validation job. To run them yourself:

```bash
CURLIMPERSONATE_RUN_FINGERPRINT_TESTS=1 \
  dotnet test tests/CurlImpersonate.Tests/CurlImpersonate.Tests.csproj \
  --filter "FullyQualifiedName~FingerprintTests"
```

## License

CurlImpersonate is licensed under the MIT License. Native curl-impersonate
assets and their dependencies remain under their upstream licenses; third-party
native dependency notices are tracked in `THIRD-PARTY-NOTICES.md`.
