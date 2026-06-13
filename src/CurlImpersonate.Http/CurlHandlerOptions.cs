using CurlImpersonate.Enums;
using System.Net;

namespace CurlImpersonate.Http;

/// <summary>
/// Configuration options for <see cref="CurlHandler"/>.
/// </summary>
public sealed class CurlHandlerOptions
{
    private const long DefaultMaxBufferedBodyBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Browser profile to impersonate. Default is the current Chrome profile.
    /// </summary>
    public BrowserProfile BrowserProfile { get; set; } = BrowserProfileExtensions.DefaultChrome;

    /// <summary>
    /// Maximum number of handles in the pool. Default is 20.
    /// </summary>
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>
    /// Total request timeout. Default is 100 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Connection timeout. Default is 30 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum request body size in bytes. Set to null to disable the limit.
    /// </summary>
    public long? MaxRequestBodyBytes { get; set; } = DefaultMaxBufferedBodyBytes;

    /// <summary>
    /// When true, request bodies are read by curl through a stream callback instead of pre-buffering.
    /// Requires <see cref="FollowRedirects"/> to be false for requests with content.
    /// </summary>
    public bool StreamRequestBodies { get; set; }

    /// <summary>
    /// Maximum buffered response body size in bytes. Set to null to disable the limit.
    /// </summary>
    public long? MaxResponseBodyBytes { get; set; } = DefaultMaxBufferedBodyBytes;

    /// <summary>
    /// When true, response bodies are exposed as they arrive instead of being buffered before SendAsync completes.
    /// Requires <see cref="FollowRedirects"/> to be false.
    /// </summary>
    public bool StreamResponseBodies { get; set; }

    /// <summary>
    /// Maximum number of open connections across the handler's curl multi handle.
    /// </summary>
    public int? MaxTotalConnections { get; set; }

    /// <summary>
    /// Maximum number of open connections to a single host.
    /// </summary>
    public int? MaxConnectionsPerHost { get; set; }

    /// <summary>
    /// Mirror of <see cref="MaxConnectionsPerHost"/> matching
    /// <c>HttpClientHandler.MaxConnectionsPerServer</c>. Reads as
    /// <see cref="int.MaxValue"/> when no limit is configured.
    /// </summary>
    public int MaxConnectionsPerServer
    {
        get => MaxConnectionsPerHost ?? int.MaxValue;
        set => MaxConnectionsPerHost = value == int.MaxValue ? null : value;
    }

    /// <summary>
    /// Maximum number of idle connections kept in curl's connection cache.
    /// </summary>
    public int? MaxConnects { get; set; }

    /// <summary>
    /// Maximum age of a connection before it is no longer reused.
    /// </summary>
    public TimeSpan? PooledConnectionLifetime { get; set; }

    /// <summary>
    /// Default HTTP version policy used for requests that leave the framework default policy.
    /// </summary>
    public HttpVersionPolicy VersionPolicy { get; set; } = HttpVersionPolicy.RequestVersionOrHigher;

    /// <summary>
    /// Whether explicit HTTP/3 requests are allowed. Default is false.
    /// </summary>
    public bool EnableHttp3 { get; set; }

    /// <summary>
    /// Whether to follow redirects. Default is true.
    /// </summary>
    public bool FollowRedirects { get; set; } = true;

    /// <summary>
    /// Mirror of <see cref="FollowRedirects"/> matching <c>HttpClientHandler.AllowAutoRedirect</c>.
    /// </summary>
    public bool AllowAutoRedirect
    {
        get => FollowRedirects;
        set => FollowRedirects = value;
    }

    /// <summary>
    /// Maximum number of redirects to follow. Default is 50.
    /// </summary>
    public int MaxRedirects { get; set; } = 50;

    /// <summary>
    /// Mirror of <see cref="MaxRedirects"/> matching <c>HttpClientHandler.MaxAutomaticRedirections</c>.
    /// </summary>
    public int MaxAutomaticRedirections
    {
        get => MaxRedirects;
        set => MaxRedirects = value;
    }

    /// <summary>
    /// Whether to automatically decompress response content. Default is true.
    /// </summary>
    public bool AutomaticDecompression { get; set; } = true;

    /// <summary>
    /// Whether curl-impersonate should apply browser default headers. Default is true.
    /// </summary>
    public bool UseBrowserHeaders { get; set; } = true;

    /// <summary>
    /// Policy for user-supplied headers that overlap browser-managed headers.
    /// </summary>
    public BrowserHeaderPolicy HeaderPolicy { get; set; } = BrowserHeaderPolicy.PreserveImpersonatedDefaults;

    /// <summary>
    /// Comma-separated normal HTTP header order passed to curl-impersonate.
    /// </summary>
    public IReadOnlyList<string>? HeaderOrder { get; set; }

    /// <summary>
    /// Advanced fingerprint overrides applied after the selected browser profile.
    /// </summary>
    public CurlFingerprintOptions Fingerprint { get; } = new();

    /// <summary>
    /// Enables libcurl verbose/debug callbacks for diagnostics. Default is false.
    /// </summary>
    public bool EnableCurlDebug { get; set; }

    /// <summary>
    /// Receives libcurl verbose/debug events when <see cref="EnableCurlDebug"/> is true.
    /// </summary>
    public Action<CurlDebugEvent>? DebugCallback { get; set; }

    /// <summary>
    /// Whether to send and store cookies using <see cref="CookieContainer"/>. Default is true.
    /// </summary>
    public bool UseCookies { get; set; } = true;

    /// <summary>
    /// Cookie container used when <see cref="UseCookies"/> is true.
    /// </summary>
    public CookieContainer CookieContainer { get; set; } = new();

    internal object CookieLock { get; } = new();

    /// <summary>
    /// Web proxy used for requests, mirroring <c>HttpClientHandler.Proxy</c>.
    /// Bypass rules and credentials attached to the proxy are honored.
    /// </summary>
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Whether configured proxies, including environment proxies, are used.
    /// Default is true. Mirrors <c>HttpClientHandler.UseProxy</c>.
    /// </summary>
    public bool UseProxy { get; set; } = true;

    /// <summary>
    /// Proxy URI (e.g., "http://proxy:8080" or "socks5://proxy:1080"). Default is null (no proxy).
    /// </summary>
    public Uri? ProxyUri { get; set; }

    /// <summary>
    /// Proxy credentials used when <see cref="Proxy"/> or <see cref="ProxyUri"/> is set.
    /// </summary>
    public NetworkCredential? ProxyCredentials { get; set; }

    /// <summary>
    /// Proxy authentication schemes libcurl may use with <see cref="ProxyCredentials"/>.
    /// </summary>
    public CurlProxyAuth ProxyAuth { get; set; } = CurlProxyAuth.Any;

    /// <summary>
    /// Comma-separated hosts that should bypass the proxy.
    /// </summary>
    public string? NoProxy { get; set; }

    /// <summary>
    /// Skip SSL certificate verification. Use only for testing/development. Default is false.
    /// </summary>
    public bool InsecureSkipVerify { get; set; }

    /// <summary>
    /// Path to a PEM CA bundle used to verify HTTPS origins. Default is null (libcurl platform default).
    /// </summary>
    public string? CaInfo { get; set; }

    /// <summary>
    /// Path to a directory of PEM CA certificates used to verify HTTPS origins. Default is null.
    /// </summary>
    public string? CaPath { get; set; }

    /// <summary>
    /// Path to a PEM CA bundle used to verify HTTPS proxies. Default is null (libcurl platform default).
    /// </summary>
    public string? ProxyCaInfo { get; set; }

    /// <summary>
    /// Path to a directory of PEM CA certificates used to verify HTTPS proxies. Default is null.
    /// </summary>
    public string? ProxyCaPath { get; set; }

    /// <summary>
    /// When true, all CurlHandler instances with this option share a single background event loop thread.
    /// Reduces thread count when using many handlers. Default is true.
    /// </summary>
    public bool UseSharedEventLoop { get; set; } = true;

    internal CurlHandlerOptions Snapshot()
    {
        var snapshot = new CurlHandlerOptions
        {
            BrowserProfile = BrowserProfile,
            MaxPoolSize = MaxPoolSize,
            Timeout = Timeout,
            ConnectTimeout = ConnectTimeout,
            MaxRequestBodyBytes = MaxRequestBodyBytes,
            StreamRequestBodies = StreamRequestBodies,
            MaxResponseBodyBytes = MaxResponseBodyBytes,
            StreamResponseBodies = StreamResponseBodies,
            MaxTotalConnections = MaxTotalConnections,
            MaxConnectionsPerHost = MaxConnectionsPerHost,
            MaxConnects = MaxConnects,
            PooledConnectionLifetime = PooledConnectionLifetime,
            VersionPolicy = VersionPolicy,
            EnableHttp3 = EnableHttp3,
            FollowRedirects = FollowRedirects,
            MaxRedirects = MaxRedirects,
            AutomaticDecompression = AutomaticDecompression,
            UseBrowserHeaders = UseBrowserHeaders,
            HeaderPolicy = HeaderPolicy,
            HeaderOrder = HeaderOrder?.ToArray(),
            EnableCurlDebug = EnableCurlDebug,
            DebugCallback = DebugCallback,
            UseCookies = UseCookies,
            CookieContainer = CookieContainer,
            Proxy = Proxy,
            UseProxy = UseProxy,
            ProxyUri = ProxyUri,
            ProxyCredentials = CloneCredential(ProxyCredentials),
            ProxyAuth = ProxyAuth,
            NoProxy = NoProxy,
            InsecureSkipVerify = InsecureSkipVerify,
            CaInfo = CaInfo,
            CaPath = CaPath,
            ProxyCaInfo = ProxyCaInfo,
            ProxyCaPath = ProxyCaPath,
            UseSharedEventLoop = UseSharedEventLoop
        };
        snapshot.Fingerprint.CopyFrom(Fingerprint);
        return snapshot;
    }

    private static NetworkCredential? CloneCredential(NetworkCredential? credential)
    {
        return credential is null
            ? null
            : new NetworkCredential(credential.UserName, credential.Password, credential.Domain);
    }
}
