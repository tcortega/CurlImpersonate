using CurlImpersonate.Enums;
using CurlImpersonate.Native;
using System.Globalization;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// Maps HttpRequestMessage to curl easy handle options.
/// </summary>
internal static class RequestMapper
{
    private const long CurlHttpVersionNone = 0;
    private const long CurlHttpVersion10 = 1;
    private const long CurlHttpVersion11 = 2;
    private const long CurlHttpVersion20 = 3;
    private const long CurlHttpVersion2Tls = 4;
    private const long CurlHttpVersion2PriorKnowledge = 5;
    private const long CurlHttpVersion3 = 30;
    private const long CurlHttpVersion3Only = 31;
    private const long CurlSslVersionTls12 = 6;
    private const long CurlSslVersionMaxDefault = 1 << 16;

    private static readonly IReadOnlyDictionary<int, string> TlsCipherNames = new Dictionary<int, string>
    {
        [0x000A] = "TLS_RSA_WITH_3DES_EDE_CBC_SHA",
        [0x002F] = "TLS_RSA_WITH_AES_128_CBC_SHA",
        [0x0033] = "TLS_DHE_RSA_WITH_AES_128_CBC_SHA",
        [0x0035] = "TLS_RSA_WITH_AES_256_CBC_SHA",
        [0x0039] = "TLS_DHE_RSA_WITH_AES_256_CBC_SHA",
        [0x003C] = "TLS_RSA_WITH_AES_128_CBC_SHA256",
        [0x003D] = "TLS_RSA_WITH_AES_256_CBC_SHA256",
        [0x0067] = "TLS_DHE_RSA_WITH_AES_128_CBC_SHA256",
        [0x006B] = "TLS_DHE_RSA_WITH_AES_256_CBC_SHA256",
        [0x008C] = "TLS_PSK_WITH_AES_128_CBC_SHA",
        [0x008D] = "TLS_PSK_WITH_AES_256_CBC_SHA",
        [0x009C] = "TLS_RSA_WITH_AES_128_GCM_SHA256",
        [0x009D] = "TLS_RSA_WITH_AES_256_GCM_SHA384",
        [0x009E] = "TLS_DHE_RSA_WITH_AES_128_GCM_SHA256",
        [0x009F] = "TLS_DHE_RSA_WITH_AES_256_GCM_SHA384",
        [0x1301] = "TLS_AES_128_GCM_SHA256",
        [0x1302] = "TLS_AES_256_GCM_SHA384",
        [0x1303] = "TLS_CHACHA20_POLY1305_SHA256",
        [0xC008] = "TLS_ECDHE_ECDSA_WITH_3DES_EDE_CBC_SHA",
        [0xC009] = "TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA",
        [0xC00A] = "TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA",
        [0xC012] = "TLS_ECDHE_RSA_WITH_3DES_EDE_CBC_SHA",
        [0xC013] = "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA",
        [0xC014] = "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA",
        [0xC023] = "TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256",
        [0xC024] = "TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA384",
        [0xC027] = "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256",
        [0xC028] = "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384",
        [0xC02B] = "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256",
        [0xC02C] = "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384",
        [0xC02F] = "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256",
        [0xC030] = "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
        [0xC035] = "TLS_ECDHE_PSK_WITH_AES_128_CBC_SHA",
        [0xC036] = "TLS_ECDHE_PSK_WITH_AES_256_CBC_SHA",
        [0xCCA8] = "TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256",
        [0xCCA9] = "TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256",
        [0xCCAC] = "TLS_ECDHE_PSK_WITH_CHACHA20_POLY1305_SHA256",
    };

    private static readonly IReadOnlyDictionary<int, string> TlsCurveNames = new Dictionary<int, string>
    {
        [19] = "P-192",
        [21] = "P-224",
        [23] = "P-256",
        [24] = "P-384",
        [25] = "P-521",
        [29] = "X25519",
        [256] = "ffdhe2048",
        [257] = "ffdhe3072",
        [4588] = "X25519MLKEM768",
        [25497] = "X25519Kyber768Draft00",
    };

    private static readonly HashSet<int> DefaultEnabledTlsExtensions =
    [
        0,
        10,
        11,
        13,
        16,
        23,
        35,
        43,
        45,
        51,
        65281,
    ];

    private static readonly HashSet<string> RestrictedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",              // curl sets from URL
        "Content-Length",    // curl sets from body
        "Transfer-Encoding", // curl handles chunked
        "Connection",        // curl manages connections
        "Upgrade"            // protocol upgrades
    };

    private static readonly HashSet<string> BrowserManagedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "User-Agent",
        "Accept",
        "Accept-Encoding",
        "Accept-Language",
        "Connection",
        "Host",
        "Priority",
        "Referer",
        "Origin",
        "Sec-Ch-Ua",
        "Upgrade-Insecure-Requests"
    };

    internal static void Configure(
        CurlEasyWrapper wrapper,
        HttpRequestMessage request,
        CurlHandlerOptions options,
        byte[]? body,
        bool followRedirects)
    {
        Configure(
            wrapper,
            request,
            options,
            body is null ? null : CurlRequestBody.Buffered(body),
            followRedirects);
    }

    internal static void Configure(
        CurlEasyWrapper wrapper,
        HttpRequestMessage request,
        CurlHandlerOptions options,
        CurlRequestBody? body,
        bool followRedirects)
    {
        var handle = wrapper.Handle;
        var httpVersion = MapRequestVersion(request, options);

        // Browser impersonation (must be done first, sets many options)
        var profile = options.BrowserProfile;
        if (request.Options.TryGetValue(CurlRequestOptions.BrowserProfile, out var requestProfile))
        {
            if (!Enum.IsDefined(requestProfile))
                throw new ArgumentException(
                    $"Request option CurlRequestOptions.BrowserProfile has undefined value {requestProfile}.",
                    nameof(request));
            profile = requestProfile;
        }

        var target = profile.ToTargetString();
        var impersonateResult = NativeMethods.EasyImpersonate(
            handle,
            target,
            ShouldUseBrowserHeaders(options) ? 1 : 0);
        CurlNativeErrors.ThrowIfError(
            impersonateResult,
            $"curl_easy_impersonate({target})",
            wrapper.GetErrorMessage());

        // Set up callbacks AFTER impersonation (impersonation may reset options)
        wrapper.SetupCallbacks();
        wrapper.SetupDebug(options.EnableCurlDebug, options.DebugCallback);

        wrapper.SetStringOption(CurlOption.Url, request.RequestUri!.AbsoluteUri);
        if (httpVersion != CurlHttpVersionNone)
            wrapper.SetLongOption(CurlOption.HttpVersion, httpVersion);

        ValidateRequestBodySize(body, options.MaxRequestBodyBytes);
        wrapper.SetMaxResponseBodyBytes(options.MaxResponseBodyBytes);

        ConfigureMethod(wrapper, request.Method, body is not null);

        // Headers (wrapper owns the slist and frees it on reset/dispose)
        var headerList = BuildHeaderList(request, options);
        wrapper.SetHeaderList(headerList);

        if (options.HeaderOrder is { Count: > 0 })
            wrapper.SetStringOption(CurlOption.HttpHeaderOrder, string.Join(',', options.HeaderOrder));

        ApplyFingerprintOptions(wrapper, options.Fingerprint);

        // Request body, including explicit empty content.
        if (body?.Bytes is not null)
        {
            wrapper.SetRequestBody(body.Bytes);
        }
        else if (body?.Stream is not null)
        {
            wrapper.SetStreamingRequestBody(
                body.Stream,
                body.Length,
                options.MaxRequestBodyBytes,
                request.Method == HttpMethod.Post);
        }

        // Timeouts. Curl's transfer timeout keeps counting while a streaming
        // transfer sits paused on consumer backpressure, which would abort
        // healthy slow-consumer downloads, so in streaming response mode the
        // handler bounds the wait for headers instead and leaves body read
        // time to the caller.
        if (!options.StreamResponseBodies)
            wrapper.SetLongOption(CurlOption.TimeoutMs, ToCurlMilliseconds(options.Timeout));
        wrapper.SetLongOption(CurlOption.ConnectTimeoutMs, ToCurlMilliseconds(options.ConnectTimeout));
        if (options.PooledConnectionLifetime.HasValue)
            wrapper.SetLongOption(
                CurlOption.MaxLifetimeConn,
                ToCurlSeconds(options.PooledConnectionLifetime.Value));

        // NoSignal keeps curl off its signal-based timeout path, which is not
        // thread-safe and would break concurrent transfers.
        wrapper.SetLongOption(CurlOption.NoSignal, 1);

        wrapper.SetStringOption(CurlOption.ProtocolsStr, "http,https");
        wrapper.SetLongOption(CurlOption.UnrestrictedAuth, 0);
        wrapper.SetLongOption(CurlOption.FollowLocation, followRedirects ? 1 : 0);
        if (followRedirects)
        {
            wrapper.SetLongOption(CurlOption.MaxRedirs, options.MaxRedirects);
            wrapper.SetStringOption(CurlOption.RedirProtocolsStr, "http,https");
        }

        if (options.AutomaticDecompression)
        {
            wrapper.SetStringOption(CurlOption.AcceptEncoding, "");  // empty string accepts all supported encodings
        }

        if (!options.UseProxy)
        {
            // An empty proxy string disables proxies, including environment ones.
            wrapper.SetStringOption(CurlOption.Proxy, "");
        }
        else
        {
            var proxyCredentials = options.ProxyCredentials;
            var proxy = options.ProxyUri?.AbsoluteUri;
            if (proxy is null && options.Proxy is { } webProxy
                && !webProxy.IsBypassed(request.RequestUri!)
                && webProxy.GetProxy(request.RequestUri!) is { } webProxyUri)
            {
                proxy = webProxyUri.AbsoluteUri;
                if (proxyCredentials is null
                    && webProxy.Credentials?.GetCredential(webProxyUri, "Basic") is { } resolved)
                {
                    proxyCredentials = resolved;
                }
            }

            if (!string.IsNullOrEmpty(proxy))
            {
                wrapper.SetStringOption(CurlOption.Proxy, proxy);
            }

            if (!string.IsNullOrEmpty(options.NoProxy))
            {
                wrapper.SetStringOption(CurlOption.NoProxy, options.NoProxy);
            }

            if (proxyCredentials is not null)
            {
                wrapper.SetStringOption(CurlOption.ProxyUsername, FormatCredentialUserName(proxyCredentials));
                wrapper.SetStringOption(CurlOption.ProxyPassword, proxyCredentials.Password);
                wrapper.SetLongOption(CurlOption.ProxyAuth, (long)options.ProxyAuth);
            }
        }

        if (!string.IsNullOrEmpty(options.CaInfo))
            wrapper.SetStringOption(CurlOption.CaInfo, options.CaInfo);

        if (!string.IsNullOrEmpty(options.CaPath))
            wrapper.SetStringOption(CurlOption.CaPath, options.CaPath);

        // The bundled BoringSSL build has no default CA bundle on Windows, so
        // default verification there must come from the OS certificate store.
        if (OperatingSystem.IsWindows()
            && string.IsNullOrEmpty(options.CaInfo)
            && string.IsNullOrEmpty(options.CaPath))
        {
            wrapper.SetLongOption(CurlOption.SslOptions, (long)CurlSslOption.NativeCa);
        }

        if (!string.IsNullOrEmpty(options.ProxyCaInfo))
            wrapper.SetStringOption(CurlOption.ProxyCaInfo, options.ProxyCaInfo);

        if (!string.IsNullOrEmpty(options.ProxyCaPath))
            wrapper.SetStringOption(CurlOption.ProxyCaPath, options.ProxyCaPath);

        if (OperatingSystem.IsWindows()
            && string.IsNullOrEmpty(options.ProxyCaInfo)
            && string.IsNullOrEmpty(options.ProxyCaPath))
        {
            wrapper.SetLongOption(CurlOption.ProxySslOptions, (long)CurlSslOption.NativeCa);
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
        if (method == HttpMethod.Head && hasContent)
            throw new NotSupportedException("HEAD requests with content are not supported.");

        if (method == HttpMethod.Get && !hasContent)
        {
            // Explicitly set for safety after pool reuse (clears residual POST state)
            wrapper.SetLongOption(CurlOption.HttpGet, 1);
        }
        else if (method == HttpMethod.Post)
        {
            wrapper.SetLongOption(CurlOption.Post, 1);
        }
        else if (method == HttpMethod.Head)
        {
            wrapper.SetLongOption(CurlOption.Nobody, 1);
        }
        else
        {
            if (!hasContent)
                wrapper.SetLongOption(CurlOption.HttpGet, 1);

            // GET-with-body lands here too: the body is set via POSTFIELDS and
            // CUSTOMREQUEST keeps the method line correct.
            wrapper.SetStringOption(CurlOption.CustomRequest, method.Method);
        }
    }

    internal static long MapRequestVersion(HttpRequestMessage request, CurlHandlerOptions options)
    {
        var versionPolicy = ResolveVersionPolicy(request, options);

        if (request.Version.Major >= 3)
        {
            if (!options.EnableHttp3)
            {
                throw new NotSupportedException(
                    "HTTP/3 requests require CurlHandlerOptions.EnableHttp3 = true because HTTP/3 depends on native QUIC support and network/proxy UDP reachability.");
            }

            return versionPolicy == HttpVersionPolicy.RequestVersionExact
                ? CurlHttpVersion3Only
                : CurlHttpVersion3;
        }

        if (request.Version.Major == 2)
        {
            return versionPolicy switch
            {
                HttpVersionPolicy.RequestVersionExact => CurlHttpVersion2PriorKnowledge,
                HttpVersionPolicy.RequestVersionOrHigher => CurlHttpVersion2Tls,
                _ => CurlHttpVersion20
            };
        }

        if (request.Version.Major == 1 && request.Version.Minor == 0)
            return CurlHttpVersion10;

        if (request.Version.Major == 1 &&
            request.Version.Minor == 1 &&
            versionPolicy == HttpVersionPolicy.RequestVersionExact)
        {
            return CurlHttpVersion11;
        }

        if (request.Version.Major == 1 &&
            request.Version.Minor == 1 &&
            versionPolicy == HttpVersionPolicy.RequestVersionOrHigher)
        {
            return CurlHttpVersion2Tls;
        }

        return CurlHttpVersionNone;
    }

    private static HttpVersionPolicy ResolveVersionPolicy(
        HttpRequestMessage request,
        CurlHandlerOptions options)
    {
        ValidateVersionPolicy(request.VersionPolicy, nameof(HttpRequestMessage.VersionPolicy));

        if (request.VersionPolicy == HttpVersionPolicy.RequestVersionOrLower)
        {
            ValidateVersionPolicy(options.VersionPolicy, nameof(CurlHandlerOptions.VersionPolicy));
            return options.VersionPolicy;
        }

        return request.VersionPolicy;
    }

    private static void ValidateVersionPolicy(HttpVersionPolicy value, string name)
    {
        if (Enum.IsDefined(value))
            return;

        throw new ArgumentOutOfRangeException(
            name,
            value,
            $"{name} must be a defined {nameof(HttpVersionPolicy)} value.");
    }

    private static nint BuildHeaderList(HttpRequestMessage request, CurlHandlerOptions options)
    {
        nint slist = 0;
        var hasExplicitCookieHeader = false;

        try
        {
            foreach (var header in request.Headers)
            {
                if (ShouldSkipHeader(header.Key, options))
                    continue;

                if (string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase))
                    hasExplicitCookieHeader = true;

                foreach (var value in header.Value)
                {
                    slist = AppendHeader(slist, header.Key, value);
                }
            }

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    if (ShouldSkipHeader(header.Key, options))
                        continue;

                    if (string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase))
                        hasExplicitCookieHeader = true;

                    foreach (var value in header.Value)
                    {
                        slist = AppendHeader(slist, header.Key, value);
                    }
                }
            }

            if (options.UseCookies && !hasExplicitCookieHeader && request.RequestUri is not null)
            {
                string cookieHeader;
                lock (options.CookieLock)
                {
                    cookieHeader = options.CookieContainer.GetCookieHeader(request.RequestUri);
                }

                if (!string.IsNullOrWhiteSpace(cookieHeader))
                    slist = AppendHeader(slist, "Cookie", cookieHeader);
            }

            return slist;
        }
        catch
        {
            if (slist != 0)
                NativeMethods.SlistFreeAll(slist);

            throw;
        }
    }

    private static nint AppendHeader(nint slist, string name, string value)
    {
        ValidateHeaderLine(name, value);

        var next = NativeMethods.SlistAppend(slist, $"{name}: {value}");
        if (next == 0)
            throw new OutOfMemoryException($"curl_slist_append failed while adding header '{name}'.");

        return next;
    }

    private static void ValidateHeaderLine(string name, string value)
    {
        if (!HeaderValidation.IsValidHeaderName(name))
        {
            throw new FormatException($"Header name '{name}' is not valid.");
        }

        if (value.AsSpan().IndexOfAny('\0', '\r', '\n') >= 0)
            throw new FormatException($"Header '{name}' contains prohibited NUL, CR, or LF characters.");
    }

    private static bool ShouldSkipHeader(string name, CurlHandlerOptions options)
    {
        if (RestrictedHeaders.Contains(name))
            return true;

        return ShouldUseBrowserHeaders(options) &&
               options.HeaderPolicy == BrowserHeaderPolicy.PreserveImpersonatedDefaults &&
               IsBrowserManagedHeader(name);
    }

    private static bool IsBrowserManagedHeader(string name)
    {
        return BrowserManagedHeaders.Contains(name) ||
               name.StartsWith("Sec-Ch-Ua-", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseBrowserHeaders(CurlHandlerOptions options)
    {
        return options.UseBrowserHeaders &&
               options.HeaderPolicy != BrowserHeaderPolicy.DisableBrowserHeaders;
    }

    private static string FormatCredentialUserName(System.Net.NetworkCredential credential)
    {
        return string.IsNullOrEmpty(credential.Domain)
            ? credential.UserName
            : $"{credential.Domain}\\{credential.UserName}";
    }

    private static void ApplyFingerprintOptions(CurlEasyWrapper wrapper, CurlFingerprintOptions options)
    {
        SetStringIfPresent(wrapper, CurlOption.SslSigHashAlgs, options.SslSignatureAlgorithms);
        SetStringIfPresent(wrapper, CurlOption.SslCertCompression, options.SslCertCompression);
        SetBoolIfPresent(wrapper, CurlOption.SslEnableAlps, options.SslEnableAlps);
        SetBoolIfPresent(wrapper, CurlOption.SslEnableTicket, options.SslEnableTicket);
        SetStringIfPresent(wrapper, CurlOption.Http2PseudoHeadersOrder, options.Http2PseudoHeaderOrder);
        SetStringIfPresent(wrapper, CurlOption.Http2Settings, options.Http2Settings);
        SetBoolIfPresent(wrapper, CurlOption.SslPermuteExtensions, options.SslPermuteExtensions);
        SetNonNegativeLongIfPresent(
            wrapper,
            CurlOption.Http2WindowUpdate,
            options.Http2WindowUpdate,
            nameof(CurlFingerprintOptions.Http2WindowUpdate));
        SetStringIfPresent(wrapper, CurlOption.Http2Streams, options.Http2Streams);
        SetBoolIfPresent(wrapper, CurlOption.TlsGrease, options.TlsGrease);
        SetStringIfPresent(wrapper, CurlOption.TlsExtensionOrder, options.TlsExtensionOrder);
        SetBoolIfPresent(wrapper, CurlOption.StreamExclusive, options.StreamExclusive);
        SetHttp2StreamWeightIfPresent(wrapper, options.Http2StreamWeight);
        SetBoolIfPresent(wrapper, CurlOption.TlsKeyUsageNoCheck, options.TlsKeyUsageNoCheck);
        SetBoolIfPresent(wrapper, CurlOption.TlsSignedCertTimestamps, options.TlsSignedCertificateTimestamps);
        SetBoolIfPresent(wrapper, CurlOption.TlsStatusRequest, options.TlsStatusRequest);
        SetStringIfPresent(wrapper, CurlOption.TlsDelegatedCredentials, options.TlsDelegatedCredentials);
        SetNonNegativeLongIfPresent(
            wrapper,
            CurlOption.TlsRecordSizeLimit,
            options.TlsRecordSizeLimit,
            nameof(CurlFingerprintOptions.TlsRecordSizeLimit));
        SetNonNegativeLongIfPresent(
            wrapper,
            CurlOption.TlsKeySharesLimit,
            options.TlsKeySharesLimit,
            nameof(CurlFingerprintOptions.TlsKeySharesLimit));
        SetBoolIfPresent(wrapper, CurlOption.TlsUseNewAlpsCodepoint, options.TlsUseNewAlpsCodepoint);
        SetBoolIfPresent(wrapper, CurlOption.Http2NoPriority, options.Http2NoPriority);
        SetBoolIfPresent(wrapper, CurlOption.SplitCookies, options.SplitCookies);
        SetStringIfPresent(wrapper, CurlOption.FormBoundary, options.FormBoundary);
        SetStringIfPresent(wrapper, CurlOption.Http3PseudoHeadersOrder, options.Http3PseudoHeaderOrder);
        SetStringIfPresent(wrapper, CurlOption.Http3Settings, options.Http3Settings);
        SetStringIfPresent(wrapper, CurlOption.QuicTransportParameters, options.QuicTransportParameters);
        SetStringIfPresent(wrapper, CurlOption.Http3SigHashAlgs, options.Http3SignatureHashAlgorithms);
        SetStringIfPresent(wrapper, CurlOption.Http3TlsExtensionOrder, options.Http3TlsExtensionOrder);

        if (!string.IsNullOrEmpty(options.Akamai))
            ApplyAkamaiFingerprint(wrapper, ParseAkamaiFingerprint(options.Akamai));

        if (!string.IsNullOrEmpty(options.Http3Fingerprint))
            ApplyHttp3Fingerprint(wrapper, ParseHttp3Fingerprint(options.Http3Fingerprint));

        if (!string.IsNullOrEmpty(options.Ja3))
            ApplyJa3Fingerprint(wrapper, ParseJa3Fingerprint(options.Ja3, options.Ja3PermuteExtensions));
    }

    internal static Ja3Fingerprint ParseJa3Fingerprint(string value, bool permuteExtensions = false)
    {
        var parts = value.Split(',');
        if (parts.Length != 5)
            throw new ArgumentException(
                "JA3 fingerprint must have format 'version,ciphers,extensions,curves,curve_formats'.",
                nameof(value));

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tlsVersion) ||
            tlsVersion != 0x0303)
        {
            throw new ArgumentException("Only TLS 1.2 JA3 fingerprints are supported.", nameof(value));
        }

        if (!int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var curveFormat) ||
            curveFormat != 0)
        {
            throw new ArgumentException("Only JA3 curve format 0 is supported.", nameof(value));
        }

        var cipherNames = ParseMappedDashList(parts[1], TlsCipherNames, "cipher");
        var extensionOrder = StripTrailingPaddingExtension(parts[2]);
        var extensionIds = ParseIntDashList(extensionOrder, "extension").ToHashSet();
        ValidateTlsExtensionToggles(extensionIds);
        var curveNames = ParseMappedDashList(parts[3], TlsCurveNames, "curve");

        return new(
            CurlSslVersionTls12 | CurlSslVersionMaxDefault,
            string.Join(':', cipherNames),
            permuteExtensions ? null : extensionOrder,
            extensionIds,
            string.Join(':', curveNames));
    }

    private static void ApplyJa3Fingerprint(CurlEasyWrapper wrapper, Ja3Fingerprint fingerprint)
    {
        wrapper.SetLongOption(CurlOption.SslVersion, fingerprint.SslVersion);
        wrapper.SetStringOption(CurlOption.SslCipherList, fingerprint.CipherList);
        ToggleTlsExtensions(wrapper, fingerprint.ExtensionIds);
        if (!string.IsNullOrEmpty(fingerprint.ExtensionOrder))
            wrapper.SetStringOption(CurlOption.TlsExtensionOrder, fingerprint.ExtensionOrder);
        wrapper.SetStringOption(CurlOption.SslEcCurves, fingerprint.CurveList);
    }

    private static void ToggleTlsExtensions(CurlEasyWrapper wrapper, IReadOnlySet<int> extensionIds)
    {
        ValidateTlsExtensionToggles(extensionIds);

        foreach (var extensionId in extensionIds.Except(DefaultEnabledTlsExtensions))
            ToggleTlsExtension(wrapper, extensionId, true);

        foreach (var extensionId in DefaultEnabledTlsExtensions.Except(extensionIds))
            ToggleTlsExtension(wrapper, extensionId, false);
    }

    private static void ValidateTlsExtensionToggles(IReadOnlySet<int> extensionIds)
    {
        foreach (var extensionId in extensionIds.Except(DefaultEnabledTlsExtensions))
            ValidateTlsExtensionToggle(extensionId, true);

        foreach (var extensionId in DefaultEnabledTlsExtensions.Except(extensionIds))
            ValidateTlsExtensionToggle(extensionId, false);
    }

    private static void ValidateTlsExtensionToggle(int extensionId, bool enable)
    {
        switch (extensionId)
        {
            case 65037:
            case 27:
            case 17513:
            case 17613:
            case 16:
            case 35:
            case 21:
            case 28:
            case 34:
                return;
            case 5:
            case 18:
                if (enable)
                    return;
                break;
        }

        var action = enable ? "enabled" : "disabled";
        throw new ArgumentException($"TLS extension {extensionId} cannot be {action} by JA3 mapping.");
    }

    private static void ToggleTlsExtension(CurlEasyWrapper wrapper, int extensionId, bool enable)
    {
        switch (extensionId)
        {
            case 65037:
                wrapper.SetStringOption(CurlOption.Ech, enable ? "grease" : "");
                break;
            case 27:
                wrapper.SetStringOption(CurlOption.SslCertCompression, enable ? "brotli" : "");
                break;
            case 17513:
                wrapper.SetLongOption(CurlOption.SslEnableAlps, enable ? 1 : 0);
                break;
            case 17613:
                wrapper.SetLongOption(CurlOption.SslEnableAlps, enable ? 1 : 0);
                wrapper.SetLongOption(CurlOption.TlsUseNewAlpsCodepoint, enable ? 1 : 0);
                break;
            case 16:
                wrapper.SetLongOption(CurlOption.SslEnableAlpn, enable ? 1 : 0);
                break;
            case 5:
                if (enable)
                    wrapper.SetLongOption(CurlOption.TlsStatusRequest, 1);
                break;
            case 18:
                if (enable)
                    wrapper.SetLongOption(CurlOption.TlsSignedCertTimestamps, 1);
                break;
            case 35:
                wrapper.SetLongOption(CurlOption.SslEnableTicket, enable ? 1 : 0);
                break;
            case 21:
            case 28:
            case 34:
                break;
            default:
                throw new ArgumentException($"TLS extension {extensionId} cannot be toggled by JA3 mapping.");
        }
    }

    private static string StripTrailingPaddingExtension(string extensions)
    {
        return extensions.EndsWith("-21", StringComparison.Ordinal)
            ? extensions[..^3]
            : extensions;
    }

    private static IReadOnlyList<string> ParseMappedDashList(
        string value,
        IReadOnlyDictionary<int, string> map,
        string name)
    {
        return ParseIntDashList(value, name)
            .Select(id => map.TryGetValue(id, out var mapped)
                ? mapped
                : throw new ArgumentException($"Unsupported JA3 {name} ID {id}."))
            .ToArray();
    }

    private static IReadOnlyList<int> ParseIntDashList(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"JA3 {name} list cannot be empty.");

        var parts = value.Split('-');
        var ids = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                throw new ArgumentException($"Invalid JA3 {name} ID '{parts[i]}'.");

            ids[i] = id;
        }

        return ids;
    }

    internal static AkamaiFingerprint ParseAkamaiFingerprint(string value)
    {
        var parts = value.Split('|');
        if (parts.Length != 4)
            throw new ArgumentException(
                "Akamai fingerprint must have format 'settings|window_update|streams|pseudo_header_order'.",
                nameof(value));

        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var windowUpdate) ||
            windowUpdate < 0)
        {
            throw new ArgumentException(
                "Akamai fingerprint window_update must be a non-negative integer.",
                nameof(value));
        }

        return new(
            parts[0].Replace(',', ';'),
            windowUpdate,
            parts[2],
            parts[3].Replace(",", ""));
    }

    internal static Http3Fingerprint ParseHttp3Fingerprint(string value)
    {
        var parts = value.Split('|');
        if (parts.Length != 3)
            throw new ArgumentException(
                "HTTP/3 fingerprint must have format 'settings|pseudo_header_order|quic_transport_parameters'.",
                nameof(value));

        return new(parts[0], parts[1].Replace(",", ""), parts[2]);
    }

    private static void ApplyAkamaiFingerprint(CurlEasyWrapper wrapper, AkamaiFingerprint fingerprint)
    {
        wrapper.SetLongOption(CurlOption.HttpVersion, CurlHttpVersion20);
        wrapper.SetStringOption(CurlOption.Http2Settings, fingerprint.Settings);
        wrapper.SetLongOption(CurlOption.Http2WindowUpdate, fingerprint.WindowUpdate);
        if (fingerprint.Streams != "0")
            wrapper.SetStringOption(CurlOption.Http2Streams, fingerprint.Streams);
        wrapper.SetStringOption(CurlOption.Http2PseudoHeadersOrder, fingerprint.PseudoHeaderOrder);
    }

    private static void ApplyHttp3Fingerprint(CurlEasyWrapper wrapper, Http3Fingerprint fingerprint)
    {
        wrapper.SetStringOption(CurlOption.Http3Settings, fingerprint.Settings);
        wrapper.SetStringOption(CurlOption.Http3PseudoHeadersOrder, fingerprint.PseudoHeaderOrder);
        wrapper.SetStringOption(CurlOption.QuicTransportParameters, fingerprint.QuicTransportParameters);
    }

    private static void SetStringIfPresent(CurlEasyWrapper wrapper, CurlOption option, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            wrapper.SetStringOption(option, value);
    }

    private static void SetBoolIfPresent(CurlEasyWrapper wrapper, CurlOption option, bool? value)
    {
        if (value.HasValue)
            wrapper.SetLongOption(option, value.Value ? 1 : 0);
    }

    private static void SetNonNegativeLongIfPresent(
        CurlEasyWrapper wrapper,
        CurlOption option,
        long? value,
        string name)
    {
        if (!value.HasValue)
            return;

        if (value.Value < 0)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value.Value,
                $"{name} must be greater than or equal to zero.");
        }

        wrapper.SetLongOption(option, value.Value);
    }

    private static void SetHttp2StreamWeightIfPresent(CurlEasyWrapper wrapper, long? value)
    {
        if (!value.HasValue)
            return;

        if (value.Value is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CurlFingerprintOptions.Http2StreamWeight),
                value,
                $"{nameof(CurlFingerprintOptions.Http2StreamWeight)} must be between 1 and 256.");
        }

        wrapper.SetLongOption(CurlOption.StreamWeight, value.Value);
    }

    private static void ValidateRequestBodySize(CurlRequestBody? body, long? maxBytes)
    {
        if (body is null || !maxBytes.HasValue)
            return;

        var knownLength = body.Bytes?.LongLength ?? body.Length;
        if (!knownLength.HasValue || knownLength.Value <= maxBytes.Value)
            return;

        throw new InvalidOperationException(
            $"Request body exceeded the configured limit of {maxBytes.Value} bytes.");
    }

    private static long ToCurlSeconds(TimeSpan value)
    {
        return Math.Max(1, checked((long)Math.Ceiling(value.TotalSeconds)));
    }

    private static long ToCurlMilliseconds(TimeSpan value)
    {
        return value == Timeout.InfiniteTimeSpan
            ? 0
            : Math.Max(1, checked((long)Math.Ceiling(value.TotalMilliseconds)));
    }
}

internal readonly record struct AkamaiFingerprint(
    string Settings,
    long WindowUpdate,
    string Streams,
    string PseudoHeaderOrder);

internal readonly record struct Http3Fingerprint(
    string Settings,
    string PseudoHeaderOrder,
    string QuicTransportParameters);

internal readonly record struct Ja3Fingerprint(
    long SslVersion,
    string CipherList,
    string? ExtensionOrder,
    IReadOnlySet<int> ExtensionIds,
    string CurveList);
