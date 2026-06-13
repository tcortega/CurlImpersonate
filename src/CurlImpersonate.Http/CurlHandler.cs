using System.Net;
using CurlImpersonate.Http.Internal;
using CurlImpersonate.Enums;

namespace CurlImpersonate.Http;

/// <summary>
/// HttpMessageHandler implementation using curl-impersonate for browser TLS fingerprint impersonation.
/// </summary>
public sealed class CurlHandler : HttpMessageHandler
{
    private const long SupportedProxyAuthMask = (long)(
        CurlProxyAuth.Basic |
        CurlProxyAuth.Digest |
        CurlProxyAuth.Negotiate |
        CurlProxyAuth.Ntlm |
        CurlProxyAuth.DigestIe |
        CurlProxyAuth.Bearer);
    private const int RequestBodyCopyBufferSize = 81920;

    private static readonly Lazy<CurlEventLoop> SharedEventLoop = new(
        () => new CurlEventLoop(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CurlEventLoop? _ownedEventLoop;
    private readonly CurlEventLoop _eventLoop;
    private readonly CurlHandlePool _pool;
    private readonly CurlHandlerOptions _options;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _disposed;

    /// <summary>
    /// Creates a new CurlHandler with default options.
    /// </summary>
    public CurlHandler() : this(new())
    {
    }

    /// <summary>
    /// Creates a new CurlHandler with the specified options.
    /// </summary>
    public CurlHandler(CurlHandlerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = options.Snapshot();
        ValidateOptions(snapshot);

        _options = snapshot;

        if (snapshot.UseSharedEventLoop && !RequiresOwnedEventLoop(snapshot))
        {
            _eventLoop = SharedEventLoop.Value;
        }
        else
        {
            _ownedEventLoop = new CurlEventLoop(snapshot);
            _eventLoop = _ownedEventLoop;
        }

        _pool = new(snapshot);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        ValidateRequestUri(request.RequestUri);
        cancellationToken.ThrowIfCancellationRequested();
        var disposeCancellationToken = _disposeCancellation.Token;
        disposeCancellationToken.ThrowIfCancellationRequested();

        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            disposeCancellationToken);
        var effectiveCancellationToken = linkedCancellation.Token;
        var cancellationOwnedByStreamingResponse = false;

        try
        {
            var body = await CreateRequestBodyAsync(request, effectiveCancellationToken).ConfigureAwait(false);

            if (_options.StreamResponseBodies)
            {
                cancellationOwnedByStreamingResponse = true;
                return await SendStreamingSingleAsync(request, body, linkedCancellation, effectiveCancellationToken)
                    .ConfigureAwait(false);
            }

            if (body?.IsStreaming == true)
            {
                return await SendSingleAsync(request, body, followRedirects: false, effectiveCancellationToken)
                    .ConfigureAwait(false);
            }

            return _options.FollowRedirects
                ? await SendWithRedirectsAsync(request, body?.Bytes, effectiveCancellationToken)
                    .ConfigureAwait(false)
                : await SendSingleAsync(request, body, followRedirects: false, effectiveCancellationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (
            cancellationToken.IsCancellationRequested && ex.CancellationToken != cancellationToken)
        {
            // Rebind from the internal linked token so callers can filter on
            // the token they passed in.
            throw new TaskCanceledException(ex.Message, ex, cancellationToken);
        }
        finally
        {
            if (!cancellationOwnedByStreamingResponse)
                linkedCancellation.Dispose();
        }
    }

    private async Task<CurlRequestBody?> CreateRequestBodyAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
            return null;

        if (!_options.StreamRequestBodies)
            return CurlRequestBody.Buffered(await ReadBufferedRequestBodyAsync(
                request.Content,
                _options.MaxRequestBodyBytes,
                cancellationToken).ConfigureAwait(false));

        if (_options.FollowRedirects)
        {
            throw new NotSupportedException(
                $"{nameof(CurlHandlerOptions.StreamRequestBodies)} requires {nameof(CurlHandlerOptions.FollowRedirects)} to be false for requests with content.");
        }

        var stream = await request.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return CurlRequestBody.Streaming(stream, request.Content.Headers.ContentLength);
    }

    private static async Task<byte[]> ReadBufferedRequestBodyAsync(
        HttpContent content,
        long? maxBytes,
        CancellationToken cancellationToken)
    {
        if (maxBytes is { } declaredLimit &&
            content.Headers.ContentLength is { } contentLength &&
            contentLength > declaredLimit)
        {
            ThrowRequestBodyLimitExceeded(declaredLimit);
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var initialCapacity = GetInitialRequestBodyCapacity(content.Headers.ContentLength, maxBytes);
        using var body = initialCapacity > 0 ? new MemoryStream(initialCapacity) : new MemoryStream();
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(RequestBodyCopyBufferSize);
        var totalBytes = 0L;

        try
        {
            while (true)
            {
                var readLength = buffer.Length;
                if (maxBytes is { } readLimit)
                {
                    var remaining = readLimit - totalBytes;
                    if (remaining < buffer.Length)
                        readLength = (int)(remaining + 1);
                }

                var read = await stream.ReadAsync(buffer.AsMemory(0, readLength), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                if (totalBytes > long.MaxValue - read || totalBytes + read > Array.MaxLength)
                    throw new InvalidOperationException("Request body is too large to buffer.");

                totalBytes += read;
                if (maxBytes is { } limit && totalBytes > limit)
                    ThrowRequestBodyLimitExceeded(limit);

                body.Write(buffer, 0, read);
            }

            return body.ToArray();
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int GetInitialRequestBodyCapacity(long? contentLength, long? maxBytes)
    {
        if (contentLength is not > 0)
            return 0;

        var boundedLength = maxBytes.HasValue
            ? Math.Min(contentLength.Value, maxBytes.Value)
            : Math.Min(contentLength.Value, RequestBodyCopyBufferSize);

        return boundedLength <= int.MaxValue ? (int)boundedLength : RequestBodyCopyBufferSize;
    }

    private static void ThrowRequestBodyLimitExceeded(long maxBytes)
    {
        throw new InvalidOperationException(
            $"Request body exceeded the configured limit of {maxBytes} bytes.");
    }

    private async Task<HttpResponseMessage> SendWithRedirectsAsync(
        HttpRequestMessage request,
        byte[]? body,
        CancellationToken cancellationToken)
    {
        var currentRequest = request;
        var currentBody = body;
        var currentRequestIsClone = false;
        var redirectCount = 0;

        try
        {
            while (true)
            {
                var response = await SendSingleAsync(
                    currentRequest,
                    currentBody is null ? null : CurlRequestBody.Buffered(currentBody),
                    followRedirects: false,
                    cancellationToken).ConfigureAwait(false);

                HttpRequestMessage nextRequest;
                byte[]? nextBody;
                try
                {
                    if (!TryCreateRedirectRequest(
                            currentRequest,
                            response,
                            currentBody,
                            redirectCount,
                            out nextRequest,
                            out nextBody))
                    {
                        response.TryGetTransferMetrics(out var metrics);
                        CurlResponseMetadata.Set(response, currentRequest.RequestUri, redirectCount, metrics);

                        if (currentRequestIsClone)
                        {
                            response.RequestMessage = request;
                            currentRequest.Dispose();
                        }

                        return response;
                    }
                }
                catch
                {
                    response.Dispose();
                    throw;
                }

                response.Dispose();

                if (currentRequestIsClone)
                    currentRequest.Dispose();

                currentRequest = nextRequest;
                currentBody = nextBody;
                currentRequestIsClone = true;
                redirectCount++;
            }
        }
        catch
        {
            if (currentRequestIsClone)
                currentRequest.Dispose();

            throw;
        }
    }

    private async Task<HttpResponseMessage> SendSingleAsync(
        HttpRequestMessage request,
        CurlRequestBody? body,
        bool followRedirects,
        CancellationToken cancellationToken)
    {
        var wrapper = _pool.Rent();
        var success = false;
        try
        {
            RequestMapper.Configure(wrapper, request, _options, body, followRedirects);

            CurlResponse result;
            try
            {
                result = await _eventLoop.ExecuteAsync(wrapper, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is CurlException or CurlMultiException)
            {
                throw CurlNativeErrors.WrapTransportException(ex);
            }

            var response = ResponseBuilder.Build(
                result,
                request,
                _options.AutomaticDecompression,
                _options.UseCookies ? _options.CookieContainer : null,
                _options.UseCookies ? _options.CookieLock : null);
            success = true;
            return response;
        }
        finally
        {
            if (success)
                _pool.Return(wrapper);
            else
                _pool.Discard(wrapper);
        }
    }

    private bool TryCreateRedirectRequest(
        HttpRequestMessage request,
        HttpResponseMessage response,
        byte[]? body,
        int redirectCount,
        out HttpRequestMessage nextRequest,
        out byte[]? nextBody)
    {
        nextRequest = null!;
        nextBody = null;

        if (!IsRedirectStatus(response.StatusCode) ||
            response.Headers.Location is not { } location ||
            request.RequestUri is not { } requestUri)
        {
            return false;
        }

        // HttpClientHandler parity: stop following and hand the caller the
        // last 3xx response instead of throwing.
        if (redirectCount >= _options.MaxRedirects)
            return false;

        // HttpClientHandler parity: a Location that does not resolve or is not
        // http(s) is not followed; the caller gets the 3xx response.
        if (!TryResolveRedirectUri(requestUri, location, out var redirectUri) ||
            !IsHttpUri(redirectUri))
        {
            return false;
        }

        var redirectMethod = GetRedirectMethod(request.Method, response.StatusCode);
        nextBody = ShouldDropRedirectBody(redirectMethod) ? null : body;
        nextRequest = CreateRedirectRequest(request, redirectUri, redirectMethod, nextBody);
        return true;
    }

    private static HttpRequestMessage CreateRedirectRequest(
        HttpRequestMessage source,
        Uri redirectUri,
        HttpMethod method,
        byte[]? body)
    {
        var request = new HttpRequestMessage(method, redirectUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };

        // Per-request options (such as the browser profile override) must
        // survive redirect hops, or the fingerprint changes mid-chain.
        foreach (var option in source.Options)
            ((IDictionary<string, object?>)request.Options).Add(option.Key, option.Value);

        var sameOrigin = source.RequestUri is not null && IsSameOrigin(source.RequestUri, redirectUri);
        foreach (var header in source.Headers)
        {
            if (!sameOrigin && IsSensitiveRedirectHeader(header.Key))
                continue;

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            if (source.Content is not null)
            {
                foreach (var header in source.Content.Headers)
                {
                    if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                        continue;

                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendStreamingSingleAsync(
        HttpRequestMessage request,
        CurlRequestBody? body,
        CancellationTokenSource cancellationSource,
        CancellationToken cancellationToken)
    {
        var wrapper = _pool.Rent();
        var streamingResponse = new CurlStreamingResponseState(
            wrapper,
            _pool,
            _options.MaxResponseBodyBytes,
            cancellationSource);
        var responseOwnsCancellation = false;
        var started = false;

        try
        {
            RequestMapper.Configure(wrapper, request, _options, body, followRedirects: false);
            wrapper.SetStreamingResponse(streamingResponse);

            var headersTask = _eventLoop.ExecuteStreamingAsync(
                wrapper,
                streamingResponse,
                cancellationToken);
            started = true;

            CurlResponseHeaders headers;
            try
            {
                // Streaming transfers do not set the curl transfer timeout,
                // which counts paused time; Timeout bounds the headers wait.
                // Passing the token reports a caller cancel as cancellation
                // rather than racing it against the timeout.
                headers = await headersTask.WaitAsync(_options.Timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new HttpRequestException(
                    $"The response headers did not arrive within the configured {nameof(CurlHandlerOptions.Timeout)} of {_options.Timeout}.",
                    ex);
            }
            catch (Exception ex) when (ex is CurlException or CurlMultiException)
            {
                throw CurlNativeErrors.WrapTransportException(ex);
            }

            var response = ResponseBuilder.Build(
                headers,
                streamingResponse.OpenBodyStream(),
                request,
                _options.AutomaticDecompression,
                _options.UseCookies ? _options.CookieContainer : null,
                _options.UseCookies ? _options.CookieLock : null);
            streamingResponse.SetResponse(response);
            responseOwnsCancellation = true;
            return response;
        }
        catch
        {
            if (!started)
                streamingResponse.DiscardBeforeStart();
            else if (!responseOwnsCancellation)
                streamingResponse.CompleteBodyStream(cancelTransfer: true);

            throw;
        }
        finally
        {
            if (!responseOwnsCancellation)
                cancellationSource.Dispose();
        }
    }

    private static bool IsRedirectStatus(HttpStatusCode statusCode)
    {
        return (int)statusCode is 301 or 302 or 303 or 307 or 308;
    }

    private static bool TryResolveRedirectUri(Uri requestUri, Uri location, out Uri redirectUri)
    {
        if (location.IsAbsoluteUri)
        {
            redirectUri = location;
            return true;
        }

        return Uri.TryCreate(requestUri, location, out redirectUri!);
    }

    private static void ValidateRequestUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            throw new InvalidOperationException("Request URI must be absolute.");

        if (!IsHttpUri(uri))
            throw new NotSupportedException("Only HTTP and HTTPS request URIs are supported.");
    }

    private static bool IsHttpUri(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpMethod GetRedirectMethod(HttpMethod method, HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        if (status == 303 && method != HttpMethod.Head)
            return HttpMethod.Get;

        if ((status == 301 || status == 302) && method == HttpMethod.Post)
            return HttpMethod.Get;

        return method;
    }

    private static bool ShouldDropRedirectBody(HttpMethod method)
    {
        return method == HttpMethod.Get || method == HttpMethod.Head;
    }

    private static bool IsSensitiveRedirectHeader(string name)
    {
        return string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Cookie", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrigin(Uri left, Uri right)
    {
        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
               left.Port == right.Port;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && disposing)
        {
            _disposeCancellation.Cancel();
            _ownedEventLoop?.Dispose();
            _pool.Dispose();
            _disposeCancellation.Dispose();
        }
        base.Dispose(disposing);
    }

    private static bool RequiresOwnedEventLoop(CurlHandlerOptions options)
    {
        return options.MaxTotalConnections.HasValue ||
               options.MaxConnectionsPerHost.HasValue ||
               options.MaxConnects.HasValue ||
               options.StreamResponseBodies;
    }

    private static void ValidateOptions(CurlHandlerOptions options)
    {
        ValidateDefinedEnumOption(options.BrowserProfile, nameof(options.BrowserProfile));
        ValidateDefinedEnumOption(options.HeaderPolicy, nameof(options.HeaderPolicy));
        ValidateDefinedEnumOption(options.VersionPolicy, nameof(options.VersionPolicy));
        ValidateProxyAuthOption(options.ProxyAuth, nameof(options.ProxyAuth));
        ValidateHeaderOrder(options.HeaderOrder);
        ValidatePositiveOption(options.MaxPoolSize, nameof(options.MaxPoolSize));
        ValidatePositiveOption(options.MaxTotalConnections, nameof(options.MaxTotalConnections));
        ValidatePositiveOption(options.MaxConnectionsPerHost, nameof(options.MaxConnectionsPerHost));
        ValidatePositiveOption(options.MaxConnects, nameof(options.MaxConnects));
        ValidatePositiveOption(options.MaxRequestBodyBytes, nameof(options.MaxRequestBodyBytes));
        ValidatePositiveOption(options.MaxResponseBodyBytes, nameof(options.MaxResponseBodyBytes));
        ValidateTimeoutOption(options.Timeout, nameof(options.Timeout));
        ValidateTimeoutOption(options.ConnectTimeout, nameof(options.ConnectTimeout));
        ArgumentNullException.ThrowIfNull(options.CookieContainer, nameof(options.CookieContainer));

        if (options.StreamResponseBodies && options.FollowRedirects)
        {
            throw new ArgumentException(
                $"{nameof(options.StreamResponseBodies)} requires {nameof(options.FollowRedirects)} to be false.",
                nameof(options));
        }

        if (options.Proxy is not null && options.ProxyUri is not null)
        {
            throw new ArgumentException(
                $"{nameof(options.Proxy)} and {nameof(options.ProxyUri)} cannot both be set.",
                nameof(options));
        }

        if (options.ProxyUri is not null && !options.ProxyUri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                $"{nameof(options.ProxyUri)} must be an absolute URI.",
                nameof(options));
        }

        if (options.MaxRedirects < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CurlHandlerOptions.MaxRedirects),
                options.MaxRedirects,
                $"{nameof(options.MaxRedirects)} must be greater than or equal to zero.");
        }

        if (options.PooledConnectionLifetime is { } lifetime && lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CurlHandlerOptions.PooledConnectionLifetime),
                lifetime,
                $"{nameof(options.PooledConnectionLifetime)} must be greater than zero.");
        }
    }

    private static void ValidateDefinedEnumOption<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (Enum.IsDefined(value))
            return;

        throw new ArgumentOutOfRangeException(
            name,
            value,
            $"{name} must be a defined {typeof(TEnum).Name} value.");
    }

    private static void ValidateProxyAuthOption(CurlProxyAuth value, string name)
    {
        if (value is CurlProxyAuth.Any or CurlProxyAuth.AnySafe)
            return;

        var rawValue = (long)value;
        if (rawValue >= 0 && (rawValue & ~SupportedProxyAuthMask) == 0)
            return;

        throw new ArgumentOutOfRangeException(
            name,
            value,
            $"{name} must be {nameof(CurlProxyAuth.Any)}, {nameof(CurlProxyAuth.AnySafe)}, or a combination of supported {nameof(CurlProxyAuth)} flags.");
    }

    private static void ValidateHeaderOrder(IReadOnlyList<string>? headerOrder)
    {
        if (headerOrder is null)
            return;

        foreach (var headerName in headerOrder)
        {
            if (!HeaderValidation.IsValidHeaderName(headerName))
            {
                throw new ArgumentException(
                    $"{nameof(CurlHandlerOptions.HeaderOrder)} contains invalid header name '{headerName}'.",
                    nameof(CurlHandlerOptions.HeaderOrder));
            }
        }
    }

    private static void ValidatePositiveOption(int? value, string name)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"{name} must be greater than zero.");
        }
    }

    private static void ValidatePositiveOption(long? value, string name)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"{name} must be greater than zero.");
        }
    }

    private static void ValidateTimeoutOption(TimeSpan value, string name)
    {
        if (value == Timeout.InfiniteTimeSpan || value > TimeSpan.Zero)
            return;

        throw new ArgumentOutOfRangeException(
            name,
            value,
            $"{name} must be greater than zero or Timeout.InfiniteTimeSpan.");
    }
}
