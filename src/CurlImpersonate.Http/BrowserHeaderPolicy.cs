namespace CurlImpersonate.Http;

/// <summary>
/// Controls how user headers interact with curl-impersonate's browser header set.
/// </summary>
public enum BrowserHeaderPolicy
{
    /// <summary>
    /// Preserve curl-impersonate's browser-managed default headers.
    /// </summary>
    PreserveImpersonatedDefaults,

    /// <summary>
    /// Allow request headers to override browser-managed headers.
    /// </summary>
    AllowUserOverride,

    /// <summary>
    /// Disable curl-impersonate's default browser headers for the request.
    /// </summary>
    DisableBrowserHeaders
}
