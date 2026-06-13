# Migrating From HttpClientHandler

`CurlHandler` is a drop-in `HttpMessageHandler`. The one-line migration is:

```csharp
// before
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });

// after
using var client = new HttpClient(new CurlHandler(new() { AllowAutoRedirect = true }));
```

Construction differs in one way: `CurlHandler` takes a `CurlHandlerOptions`
object up front and snapshots it, where `HttpClientHandler` exposes mutable
properties. Set everything in the options initializer.

## Property mapping

| `HttpClientHandler` | `CurlHandlerOptions` | Notes |
| --- | --- | --- |
| `AllowAutoRedirect` | `AllowAutoRedirect` | Mirror of `FollowRedirects`. |
| `MaxAutomaticRedirections` | `MaxAutomaticRedirections` | Mirror of `MaxRedirects` (default 50). |
| `AutomaticDecompression` | `AutomaticDecompression` (bool) | Deliberately not `DecompressionMethods`: the impersonated browser profile owns the `Accept-Encoding` header, so per-method selection would change the fingerprint. `true` decodes everything the profile advertises. |
| `CookieContainer` | `CookieContainer` | Same type and semantics. |
| `UseCookies` | `UseCookies` | Same semantics. |
| `Proxy` | `Proxy` | Same `IWebProxy` type; bypass rules and attached `Credentials` are honored per request. curl-specific `ProxyUri`, `NoProxy`, `ProxyCredentials`, and `ProxyAuth` remain available. |
| `UseProxy` | `UseProxy` | `false` disables configured and environment proxies. |
| `DefaultProxyCredentials` | `ProxyCredentials` | Applies to whichever proxy is selected. |
| `MaxConnectionsPerServer` | `MaxConnectionsPerServer` | Mirror of `MaxConnectionsPerHost`; `int.MaxValue` means unlimited. |
| `SslProtocols` | intentionally unsupported | The impersonated profile owns TLS version negotiation; overriding it would break the fingerprint. |
| `ServerCertificateCustomValidationCallback` | intentionally unsupported | Verification happens inside BoringSSL. Use `CaInfo`/`CaPath` for custom trust, `InsecureSkipVerify` only in tests. |
| `ClientCertificates` | not supported today | Open an issue if you need mutual TLS. |
| `Credentials` / `PreAuthenticate` / `UseDefaultCredentials` | not supported | Server authentication is typically a per-request `Authorization` header or a `DelegatingHandler`; curl-managed server auth is not wired up. |
| `MaxResponseHeadersLength` | `MaxResponseBodyBytes` (related) | Header length is bounded by curl internally; body buffering is bounded by `MaxResponseBodyBytes`. |
| (no equivalent) | `MaxRequestBodyBytes` / `MaxResponseBodyBytes` | Both default to 64 MiB, lower than `HttpClientHandler`'s effective limits. An oversized request body throws `InvalidOperationException` before the request is sent; an oversized response body fails the transfer with `HttpRequestException`. Raise the limit, set it to `null` for unlimited, or use `StreamRequestBodies`/`StreamResponseBodies`. |
| `CheckCertificateRevocationList` | libcurl default | BoringSSL builds do not fetch CRLs; revocation checking follows the curl-impersonate build. |

`SocketsHttpHandler` extras map similarly where they exist:
`PooledConnectionLifetime` and `ConnectTimeout` carry the same names and
meanings on `CurlHandlerOptions`; `MaxConnectionsPerServer` as above.
`HttpClient.Timeout` keeps working, and `CurlHandlerOptions.Timeout` bounds the
whole transfer inside curl as well. The exception is `StreamResponseBodies`,
where `Timeout` bounds the wait for the response headers and body read time is
left to the caller, matching `SocketsHttpHandler` with `ResponseHeadersRead`.
Curl's transfer timeout keeps counting while a streaming transfer is paused on
consumer backpressure, so applying it to the body would abort a slow but
healthy download.

## Exceptions

Transport failures (DNS, connect, TLS, timeouts, transfer errors) surface as
`HttpRequestException` with `HttpRequestError` set, matching
`SocketsHttpHandler`, with the originating `CurlException` (carrying the
`CurlCode`) as `InnerException`. With `StreamResponseBodies`, a transport
failure after the headers have been returned surfaces from body stream reads
as `HttpIOException` (an `IOException`), again with the `CurlException` as
`InnerException`, matching `SocketsHttpHandler`. Cancellation surfaces as
`OperationCanceledException`. Invalid options and arguments throw their usual
`ArgumentException` family at configuration time.

## Things CurlHandler adds

- `BrowserProfile` per handler, or per request via
  `request.Options.Set(CurlRequestOptions.BrowserProfile, ...)`.
- Fingerprint overrides (`Fingerprint`), header ordering (`HeaderOrder`),
  browser header policy (`HeaderPolicy`).
- Streaming request/response bodies with backpressure
  (`StreamRequestBodies`, `StreamResponseBodies`).
- Response metadata: `response.TryGetEffectiveUri(...)`,
  `response.TryGetRedirectCount(...)`.
