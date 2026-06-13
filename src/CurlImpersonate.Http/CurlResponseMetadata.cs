using System.Runtime.CompilerServices;

namespace CurlImpersonate.Http;

/// <summary>
/// Metadata helpers for responses produced by <see cref="CurlHandler"/>.
/// </summary>
public static class CurlResponseMetadata
{
    private static readonly ConditionalWeakTable<HttpResponseMessage, Metadata> Responses = new();

    /// <summary>
    /// Tries to get the final effective URI after redirects.
    /// </summary>
    public static bool TryGetEffectiveUri(this HttpResponseMessage response, out Uri? effectiveUri)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (Responses.TryGetValue(response, out var metadata))
        {
            effectiveUri = metadata.EffectiveUri;
            return effectiveUri is not null;
        }

        effectiveUri = null;
        return false;
    }

    /// <summary>
    /// Tries to get the redirect count reported by the handler.
    /// </summary>
    public static bool TryGetRedirectCount(this HttpResponseMessage response, out int redirectCount)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (Responses.TryGetValue(response, out var metadata))
        {
            redirectCount = metadata.RedirectCount;
            return true;
        }

        redirectCount = 0;
        return false;
    }

    /// <summary>
    /// Tries to get transfer metrics reported by libcurl.
    /// </summary>
    public static bool TryGetTransferMetrics(
        this HttpResponseMessage response,
        out CurlTransferMetrics? metrics)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (Responses.TryGetValue(response, out var metadata) && metadata.Metrics is not null)
        {
            metrics = metadata.Metrics;
            return true;
        }

        metrics = null;
        return false;
    }

    internal static void Set(
        HttpResponseMessage response,
        Uri? effectiveUri,
        int redirectCount,
        CurlTransferMetrics? metrics)
    {
        Responses.Remove(response);
        Responses.Add(response, new Metadata(effectiveUri, redirectCount, metrics));
    }

    private sealed record Metadata(Uri? EffectiveUri, int RedirectCount, CurlTransferMetrics? Metrics);
}
