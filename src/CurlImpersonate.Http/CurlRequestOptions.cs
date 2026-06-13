using CurlImpersonate.Enums;

namespace CurlImpersonate.Http;

/// <summary>
/// Typed <see cref="HttpRequestMessage.Options"/> keys understood by
/// <see cref="CurlHandler"/>.
/// </summary>
public static class CurlRequestOptions
{
    /// <summary>
    /// Overrides <see cref="CurlHandlerOptions.BrowserProfile"/> for a single
    /// request, so one handler can serve multiple impersonation profiles.
    /// </summary>
    public static readonly HttpRequestOptionsKey<BrowserProfile> BrowserProfile =
        new("CurlImpersonate.BrowserProfile");
}
