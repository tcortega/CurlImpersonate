using CurlImpersonate.Enums;

namespace CurlImpersonate.Http;

/// <summary>
/// Configuration options for <see cref="CurlHandler"/>.
/// </summary>
public sealed class CurlHandlerOptions
{
    /// <summary>
    /// Browser profile to impersonate. Default is Chrome142.
    /// </summary>
    public BrowserProfile BrowserProfile { get; set; } = BrowserProfile.Chrome142;

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
    /// Whether to follow redirects. Default is true.
    /// </summary>
    public bool FollowRedirects { get; set; } = true;

    /// <summary>
    /// Maximum number of redirects to follow. Default is 50.
    /// </summary>
    public int MaxRedirects { get; set; } = 50;

    /// <summary>
    /// Whether to automatically decompress response content. Default is true.
    /// </summary>
    public bool AutomaticDecompression { get; set; } = true;

    /// <summary>
    /// Proxy URL (e.g., "http://proxy:8080" or "socks5://proxy:1080"). Default is null (no proxy).
    /// </summary>
    public string? Proxy { get; set; }

    /// <summary>
    /// Skip SSL certificate verification. Use only for testing/development. Default is false.
    /// </summary>
    public bool InsecureSkipVerify { get; set; }

    /// <summary>
    /// When true, all CurlHandler instances with this option share a single background event loop thread.
    /// Reduces thread count when using many handlers. Default is false (each handler gets its own loop).
    /// </summary>
    public bool UseSharedEventLoop { get; set; }
}
