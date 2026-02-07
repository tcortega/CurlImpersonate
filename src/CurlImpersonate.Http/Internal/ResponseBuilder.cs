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
    public static HttpResponseMessage Build(CurlResponse response, HttpRequestMessage request)
    {
        var httpResponse = new HttpResponseMessage((HttpStatusCode)response.StatusCode)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(response.Body)
        };

        // Parse headers
        ParseHeaders(response.Headers, httpResponse.Headers, httpResponse.Content.Headers);

        return httpResponse;
    }

    private static void ParseHeaders(
        string[] rawHeaders,
        HttpResponseHeaders responseHeaders,
        HttpContentHeaders contentHeaders)
    {
        foreach (var headerLine in rawHeaders)
        {
            // Skip status line (e.g., "HTTP/2 200")
            if (headerLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
                continue;

            // Find the colon separator
            var colonIndex = headerLine.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var name = headerLine[..colonIndex].Trim();
            var value = headerLine[(colonIndex + 1)..].Trim();

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
}
