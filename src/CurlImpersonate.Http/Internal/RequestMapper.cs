using CurlImpersonate.Enums;
using CurlImpersonate.Native;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// Maps HttpRequestMessage to curl easy handle options.
/// </summary>
internal static class RequestMapper
{
    private static readonly HashSet<string> RestrictedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",              // curl sets from URL
        "Content-Length",    // curl sets from body
        "Transfer-Encoding", // curl handles chunked
        "Connection",        // curl manages connections
        "Upgrade"            // protocol upgrades
    };

    /// <summary>
    /// Configures a curl easy handle for the given request.
    /// </summary>
    public static async Task ConfigureAsync(
        CurlEasyWrapper wrapper,
        HttpRequestMessage request,
        CurlHandlerOptions options,
        CancellationToken cancellationToken)
    {
        var handle = wrapper.Handle;

        // Browser impersonation (must be done first, sets many options)
        var target = options.BrowserProfile.ToTargetString();
        NativeMethods.EasyImpersonate(handle, target, 1);

        // Set up callbacks AFTER impersonation (impersonation may reset options)
        wrapper.SetupCallbacks();

        // URL
        wrapper.SetStringOption(CurlOption.Url, request.RequestUri!.AbsoluteUri);

        // HTTP method
        ConfigureMethod(wrapper, request.Method, request.Content != null);

        // Headers (wrapper owns the slist and frees it on reset/dispose)
        var headerList = BuildHeaderList(request);
        wrapper.SetHeaderList(headerList);

        // Request body
        if (request.Content != null)
        {
            var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            if (body.Length > 0)
            {
                wrapper.SetRequestBody(body);
            }
        }

        // Timeouts
        wrapper.SetLongOption(CurlOption.TimeoutMs, (long)options.Timeout.TotalMilliseconds);
        wrapper.SetLongOption(CurlOption.ConnectTimeoutMs, (long)options.ConnectTimeout.TotalMilliseconds);

        // Thread safety - REQUIRED for multi-threaded usage
        wrapper.SetLongOption(CurlOption.NoSignal, 1);

        // Redirects
        wrapper.SetLongOption(CurlOption.FollowLocation, options.FollowRedirects ? 1 : 0);
        if (options.FollowRedirects)
        {
            wrapper.SetLongOption(CurlOption.MaxRedirs, options.MaxRedirects);
        }

        // Automatic decompression
        if (options.AutomaticDecompression)
        {
            wrapper.SetStringOption(CurlOption.AcceptEncoding, "");  // Empty string = accept all encodings
        }

        // Proxy
        if (!string.IsNullOrEmpty(options.Proxy))
        {
            wrapper.SetStringOption(CurlOption.Proxy, options.Proxy);
        }

        // SSL verification skip (for testing only)
        if (options.InsecureSkipVerify)
        {
            wrapper.SetLongOption(CurlOption.SslVerifyPeer, 0);
            wrapper.SetLongOption(CurlOption.SslVerifyHost, 0);
        }
    }

    private static void ConfigureMethod(CurlEasyWrapper wrapper, HttpMethod method, bool hasContent)
    {
        if (method == HttpMethod.Get)
        {
            // Skip for GET - it's the default anyway, and impersonation sets it up
        }
        else if (method == HttpMethod.Post)
        {
            wrapper.SetLongOption(CurlOption.Post, 1);
        }
        else if (method == HttpMethod.Put)
        {
            wrapper.SetLongOption(CurlOption.Upload, 1);
        }
        else if (method == HttpMethod.Head)
        {
            wrapper.SetLongOption(CurlOption.Nobody, 1);
        }
        else
        {
            // Custom method
            wrapper.SetStringOption(CurlOption.CustomRequest, method.Method);
        }
    }

    private static nint BuildHeaderList(HttpRequestMessage request)
    {
        nint slist = 0;

        // Request headers (skip restricted)
        foreach (var header in request.Headers)
        {
            if (IsRestrictedHeader(header.Key))
                continue;

            foreach (var value in header.Value)
            {
                slist = NativeMethods.SlistAppend(slist, $"{header.Key}: {value}");
            }
        }

        // Content headers
        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                if (IsRestrictedHeader(header.Key))
                    continue;

                foreach (var value in header.Value)
                {
                    slist = NativeMethods.SlistAppend(slist, $"{header.Key}: {value}");
                }
            }
        }

        return slist;
    }

    private static bool IsRestrictedHeader(string name) => RestrictedHeaders.Contains(name);
}
