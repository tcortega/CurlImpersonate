using System.Net;
using System.Net.Http.Headers;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// Builds HttpResponseMessage from curl response data.
/// </summary>
internal static class ResponseBuilder
{
    /// <summary>
    /// Builds an HttpResponseMessage from curl response data.
    /// </summary>
    public static HttpResponseMessage Build(
        CurlResponse response,
        HttpRequestMessage request,
        bool automaticDecompression = false,
        CookieContainer? cookieContainer = null,
        object? cookieLock = null)
    {
        var httpResponse = new HttpResponseMessage((HttpStatusCode)response.StatusCode)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(response.Body),
            Version = response.Version ?? HttpVersion.Version11
        };

        ParseHeaders(
            response.Headers,
            httpResponse.Headers,
            httpResponse.Content.Headers,
            ShouldStripDecodedContentHeaders(response.Headers, automaticDecompression));

        StoreCookies(response.Headers, request.RequestUri, cookieContainer, cookieLock);
        SetCurlOptions(httpResponse, response);

        return httpResponse;
    }

    /// <summary>
    /// Builds an HttpResponseMessage from curl response headers and a streaming body.
    /// </summary>
    public static HttpResponseMessage Build(
        CurlResponseHeaders response,
        Stream body,
        HttpRequestMessage request,
        bool automaticDecompression = false,
        CookieContainer? cookieContainer = null,
        object? cookieLock = null)
    {
        var httpResponse = new HttpResponseMessage((HttpStatusCode)response.StatusCode)
        {
            RequestMessage = request,
            Content = new StreamContent(body),
            Version = response.Version ?? HttpVersion.Version11
        };

        ParseHeaders(
            response.Headers,
            httpResponse.Headers,
            httpResponse.Content.Headers,
            ShouldStripDecodedContentHeaders(response.Headers, automaticDecompression));

        StoreCookies(response.Headers, request.RequestUri, cookieContainer, cookieLock);
        SetCurlOptions(httpResponse, request.RequestUri, redirectCount: 0, metrics: null);

        return httpResponse;
    }

    private static void ParseHeaders(
        string[] rawHeaders,
        HttpResponseHeaders responseHeaders,
        HttpContentHeaders contentHeaders,
        bool stripDecodedContentHeaders)
    {
        foreach (var headerLine in rawHeaders)
        {
            // Skip status line (e.g., "HTTP/2 200")
            if (headerLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
                continue;

            var colonIndex = headerLine.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var name = headerLine[..colonIndex].Trim();
            var value = headerLine[(colonIndex + 1)..].Trim();

            if (stripDecodedContentHeaders && IsDecodedContentHeader(name))
                continue;

            // Try to add to response headers first, then content headers
            if (!TryAddHeader(responseHeaders, name, value))
            {
                TryAddHeader(contentHeaders, name, value);
            }
        }
    }

    private static bool TryAddHeader(HttpHeaders headers, string name, string value)
    {
        try
        {
            // TryAddWithoutValidation handles invalid values gracefully
            return headers.TryAddWithoutValidation(name, value);
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldStripDecodedContentHeaders(string[] rawHeaders, bool automaticDecompression)
    {
        if (!automaticDecompression)
            return false;

        foreach (var headerLine in rawHeaders)
        {
            var colonIndex = headerLine.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var name = headerLine[..colonIndex].Trim();
            if (!string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = headerLine[(colonIndex + 1)..].Trim();
            if (ContainsNonIdentityContentCoding(value))
                return true;
        }

        return false;
    }

    private static bool ContainsNonIdentityContentCoding(string value)
    {
        foreach (var coding in value.Split(','))
        {
            var normalized = coding.Trim();
            if (normalized.Length > 0 &&
                !string.Equals(normalized, "identity", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDecodedContentHeader(string name)
    {
        return string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase);
    }

    private static void StoreCookies(
        string[] rawHeaders,
        Uri? requestUri,
        CookieContainer? cookieContainer,
        object? cookieLock)
    {
        if (requestUri is null || cookieContainer is null)
            return;

        foreach (var headerLine in rawHeaders)
        {
            var colonIndex = headerLine.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var name = headerLine[..colonIndex].Trim();
            if (!string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = headerLine[(colonIndex + 1)..].Trim();
            if (value.Length == 0)
                continue;

            try
            {
                if (cookieLock is null)
                {
                    cookieContainer.SetCookies(requestUri, value);
                }
                else
                {
                    lock (cookieLock)
                    {
                        cookieContainer.SetCookies(requestUri, value);
                    }
                }
            }
            catch (CookieException)
            {
                // Match HttpClient's tolerant header behavior: invalid Set-Cookie
                // values should not fail an otherwise usable response.
            }
        }
    }

    private static void SetCurlOptions(HttpResponseMessage httpResponse, CurlResponse response)
    {
        SetCurlOptions(httpResponse, response.EffectiveUri, response.RedirectCount, response.Metrics);
    }

    private static void SetCurlOptions(
        HttpResponseMessage httpResponse,
        Uri? effectiveUri,
        int redirectCount,
        CurlTransferMetrics? metrics)
    {
        CurlResponseMetadata.Set(httpResponse, effectiveUri, redirectCount, metrics);
    }
}
