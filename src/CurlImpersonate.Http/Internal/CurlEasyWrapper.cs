using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Net;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// Owns a raw curl easy handle with callbacks, buffers, and request state
/// management. The handle lifetime is bounded by this type: created in the
/// constructor and freed in Dispose.
/// </summary>
internal sealed unsafe class CurlEasyWrapper : IDisposable
{
    private const int CurlOptionTypeOffT = 30000;
    private const int CurlOptionTypeBlob = 40000;
    private const nuint CurlReadFuncAbort = 0x10000000;
    private const nuint CurlWriteFuncPause = 0x10000001;
    private static readonly byte[] EmptyRequestBody = [0];

    private nint _handle;
    private GCHandle _gcHandle;
    private bool _disposed;

    // Error buffer (256 bytes as per curl documentation)
    private readonly byte[] _errorBuffer = new byte[256];
    private GCHandle _errorBufferHandle;

    private byte[]? _requestBody;
    private GCHandle _requestBodyHandle;
    private Stream? _requestBodyStream;
    private long? _requestBodyLength;
    private long? _maxRequestBodyBytes;
    private long _requestBodyBytesRead;

    // Header list (must be freed after transfer)
    private nint _headerList;

    // String allocations (must remain valid until reset/dispose)
    private readonly List<nint> _stringAllocations = new();

    private readonly ResponseBuffer _responseBuffer = new();
    private readonly List<string> _responseHeaders = new();
    private CurlStreamingResponseState? _streamingResponse;

    private volatile bool _isAborted;
    private CancellationToken _transferCancellationToken;
    private Exception? _callbackException;
    private Action<CurlDebugEvent>? _debugCallback;

    /// <summary>
    /// The raw curl easy handle pointer.
    /// </summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>
    /// Whether this transfer has been aborted.
    /// </summary>
    public bool IsAborted => _isAborted;

    /// <summary>
    /// Whether this wrapper can be returned to the pool.
    /// </summary>
    public bool CanReuse => !_disposed && _handle != 0 && !_isAborted && _callbackException is null;

    /// <summary>
    /// Any exception captured by a managed callback.
    /// </summary>
    public Exception? CallbackException => _callbackException;

    /// <summary>
    /// Creates a new CurlEasyWrapper.
    /// </summary>
    public CurlEasyWrapper()
    {
        _handle = NativeMethods.EasyInit();
        if (_handle == 0)
            throw new InvalidOperationException("Failed to initialize curl easy handle");
        
        _gcHandle = GCHandle.Alloc(this);
        _errorBufferHandle = GCHandle.Alloc(_errorBuffer, GCHandleType.Pinned);
    }

    /// <summary>
    /// Sets up callbacks for response handling. Call this AFTER curl_easy_impersonate
    /// since impersonation may reset callback options.
    /// </summary>
    public void SetupCallbacks()
    {
        SetPointerOption(CurlOption.ErrorBuffer, _errorBufferHandle.AddrOfPinnedObject());

        SetPointerOption(
            CurlOption.WriteFunction,
            (nint)(delegate* unmanaged[Cdecl]<byte*, nuint, nuint, nint, nuint>)&OnWriteCallback);
        SetPointerOption(CurlOption.WriteData, GCHandle.ToIntPtr(_gcHandle));

        SetPointerOption(
            CurlOption.HeaderFunction,
            (nint)(delegate* unmanaged[Cdecl]<byte*, nuint, nuint, nint, nuint>)&OnHeaderCallback);
        SetPointerOption(CurlOption.HeaderData, GCHandle.ToIntPtr(_gcHandle));

        SetPointerOption(
            CurlOption.XferInfoFunction,
            (nint)(delegate* unmanaged[Cdecl]<nint, long, long, long, long, int>)&OnProgressCallback);
        SetPointerOption(CurlOption.XferInfoData, GCHandle.ToIntPtr(_gcHandle));
        SetLongOption(CurlOption.NoProgress, 0);
    }

    /// <summary>
    /// Configures optional libcurl verbose/debug callback diagnostics.
    /// </summary>
    public void SetupDebug(bool enable, Action<CurlDebugEvent>? callback)
    {
        _debugCallback = enable ? callback : null;
        if (!enable)
            return;

        SetPointerOption(
            CurlOption.DebugFunction,
            (nint)(delegate* unmanaged[Cdecl]<nint, int, byte*, nuint, nint, int>)&OnDebugCallback);
        SetPointerOption(CurlOption.DebugData, GCHandle.ToIntPtr(_gcHandle));
        SetLongOption(CurlOption.Verbose, 1);
    }

    /// <summary>
    /// Marks the beginning of one transfer lifetime.
    /// </summary>
    public void BeginTransfer(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _transferCancellationToken = cancellationToken;
        _callbackException = null;
        _isAborted = cancellationToken.IsCancellationRequested;
    }

    /// <summary>
    /// Clears transfer-scoped state after the event loop has removed the handle.
    /// </summary>
    public void EndTransfer()
    {
        _transferCancellationToken = CancellationToken.None;
    }

    /// <summary>
    /// Marks the current transfer as cancelled.
    /// </summary>
    public void MarkCancellationRequested()
    {
        _isAborted = true;
    }

    /// <summary>
    /// Sets the maximum buffered response size for the active request.
    /// </summary>
    public void SetMaxResponseBodyBytes(long? maxResponseBodyBytes)
    {
        _responseBuffer.MaxLength = maxResponseBodyBytes;
    }

    /// <summary>
    /// Enables streaming response body delivery for the active transfer.
    /// </summary>
    public void SetStreamingResponse(CurlStreamingResponseState streamingResponse)
    {
        _streamingResponse = streamingResponse;
    }

    /// <summary>
    /// Sets the request body data.
    /// </summary>
    public void SetRequestBody(byte[] body)
    {
        FreeRequestBody();

        _requestBody = body.Length == 0 ? EmptyRequestBody : body;
        _requestBodyHandle = GCHandle.Alloc(_requestBody, GCHandleType.Pinned);

        SetPointerOption(CurlOption.PostFields, _requestBodyHandle.AddrOfPinnedObject());
        // PostFieldSizeLarge is an OFF_T option - shim expects pointer to value
        SetLongOption(CurlOption.PostFieldSizeLarge, body.Length);
    }

    /// <summary>
    /// Sets the request body stream used by CURLOPT_READFUNCTION.
    /// </summary>
    public void SetStreamingRequestBody(
        Stream stream,
        long? contentLength,
        long? maxRequestBodyBytes,
        bool isPost)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new InvalidOperationException("Request content stream must be readable.");

        if (contentLength is < 0)
            throw new InvalidOperationException("Request content length cannot be negative.");

        FreeRequestBody();

        _requestBodyStream = stream;
        _requestBodyLength = contentLength;
        _maxRequestBodyBytes = maxRequestBodyBytes;
        _requestBodyBytesRead = 0;

        SetPointerOption(
            CurlOption.ReadFunction,
            (nint)(delegate* unmanaged[Cdecl]<byte*, nuint, nuint, nint, nuint>)&OnReadCallback);
        SetPointerOption(CurlOption.ReadData, GCHandle.ToIntPtr(_gcHandle));

        if (isPost)
        {
            SetPointerOption(CurlOption.PostFields, 0);
            SetLongOption(CurlOption.PostFieldSizeLarge, contentLength ?? -1);
        }
        else
        {
            SetLongOption(CurlOption.Upload, 1);
            SetLongOption(CurlOption.InFileSizeLarge, contentLength ?? -1);
        }
    }

    /// <summary>
    /// Sets the header list for the request.
    /// </summary>
    public void SetHeaderList(nint headerList)
    {
        FreeHeaderList();
        _headerList = headerList;
        if (_headerList != 0)
        {
            SetPointerOption(CurlOption.HttpHeader, _headerList);
        }
    }

    /// <summary>
    /// Sets a string option on the curl handle, keeping memory alive until reset/dispose.
    /// </summary>
    public void SetStringOption(CurlOption option, string value)
    {
        if (value.AsSpan().IndexOf('\0') >= 0)
            throw new ArgumentException($"{option} cannot contain embedded NUL characters.", nameof(value));

        var ptr = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            var code = NativeMethods.EasySetOptPointer(_handle, option, ptr);
            CurlNativeErrors.ThrowIfError(code, SetOptOperation(option), GetErrorMessage());
            _stringAllocations.Add(ptr);
        }
        catch
        {
            Marshal.FreeCoTaskMem(ptr);
            throw;
        }
    }

    /// <summary>
    /// Sets a numeric (long) option on the curl handle.
    /// The shim expects a pointer to the value for numeric options.
    /// </summary>
    public void SetLongOption(CurlOption option, long value)
    {
        var code = IsOffTOption(option)
            ? NativeMethods.EasySetOptOffT(_handle, option, value)
            : NativeMethods.EasySetOptLong(_handle, option, CheckedCLongValue(option, value));

        CurlNativeErrors.ThrowIfError(code, SetOptOperation(option), GetErrorMessage());
    }

    /// <summary>
    /// Gets the error message from curl's error buffer.
    /// </summary>
    public string? GetErrorMessage()
    {
        var nullIndex = Array.IndexOf(_errorBuffer, (byte)0);
        if (nullIndex <= 0)
            return null;

        return Encoding.UTF8.GetString(_errorBuffer, 0, nullIndex);
    }

    /// <summary>
    /// Builds the response from accumulated data.
    /// </summary>
    public CurlResponse BuildResponse()
    {
        var code = NativeMethods.EasyGetInfoLong(_handle, CurlInfo.ResponseCode, out var statusCode);
        CurlNativeErrors.ThrowIfError(
            code,
            $"curl_easy_getinfo({CurlInfo.ResponseCode})",
            GetErrorMessage());

        code = NativeMethods.EasyGetInfoLong(_handle, CurlInfo.HttpVersion, out var httpVersion);
        CurlNativeErrors.ThrowIfError(
            code,
            $"curl_easy_getinfo({CurlInfo.HttpVersion})",
            GetErrorMessage());

        code = NativeMethods.EasyGetInfoLong(_handle, CurlInfo.RedirectCount, out var redirectCount);
        CurlNativeErrors.ThrowIfError(
            code,
            $"curl_easy_getinfo({CurlInfo.RedirectCount})",
            GetErrorMessage());

        code = NativeMethods.EasyGetInfo(_handle, CurlInfo.EffectiveUrl, out var effectiveUrlPtr);
        CurlNativeErrors.ThrowIfError(
            code,
            $"curl_easy_getinfo({CurlInfo.EffectiveUrl})",
            GetErrorMessage());

        return new(
            (int)statusCode,
            _responseBuffer.ToArray(),
            _responseHeaders.ToArray(),
            MapHttpVersion(httpVersion),
            ParseUri(effectiveUrlPtr),
            checked((int)redirectCount),
            ReadTransferMetrics());
    }

    /// <summary>
    /// Resets the wrapper for reuse (curl_easy_reset + clear managed state).
    /// </summary>
    public void Reset()
    {
        NativeMethods.EasyReset(_handle);
        FreeRequestBody();
        FreeHeaderList();
        FreeStringAllocations();

        _responseBuffer.Reset();
        _responseBuffer.MaxLength = null;
        _responseHeaders.Clear();
        _streamingResponse = null;
        _isAborted = false;
        _transferCancellationToken = CancellationToken.None;
        _callbackException = null;
        _debugCallback = null;
        Array.Clear(_errorBuffer);

        // Defensive: re-set callbacks so the handle is never in a bare state.
        // RequestMapper.Configure calls SetupCallbacks() again after
        // EasyImpersonate, which may override these.
        SetupCallbacks();
    }

    private void SetPointerOption(CurlOption option, nint value)
    {
        var code = NativeMethods.EasySetOptPointer(_handle, option, value);
        CurlNativeErrors.ThrowIfError(code, SetOptOperation(option), GetErrorMessage());
    }

    private void FreeRequestBody()
    {
        if (_requestBodyHandle.IsAllocated)
        {
            _requestBodyHandle.Free();
        }

        _requestBody = null;
        _requestBodyStream = null;
        _requestBodyLength = null;
        _maxRequestBodyBytes = null;
        _requestBodyBytesRead = 0;
    }

    private void FreeHeaderList()
    {
        if (_headerList != 0)
        {
            NativeMethods.SlistFreeAll(_headerList);
            _headerList = 0;
        }
    }

    private void FreeStringAllocations()
    {
        foreach (var ptr in _stringAllocations)
            Marshal.FreeCoTaskMem(ptr);
        _stringAllocations.Clear();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint OnWriteCallback(byte* ptr, nuint size, nuint nmemb, nint userdata)
    {
        var handle = GCHandle.FromIntPtr(userdata);
        if (!handle.IsAllocated || handle.Target is not CurlEasyWrapper wrapper)
            return 0;

        try
        {
            var totalSize = checked((int)(size * nmemb));
            var span = new ReadOnlySpan<byte>(ptr, totalSize);
            if (wrapper._streamingResponse is { } streamingResponse)
            {
                if (!streamingResponse.WriteBody(span))
                    return CurlWriteFuncPause;
            }
            else
            {
                wrapper._responseBuffer.Write(span);
            }

            return size * nmemb;
        }
        catch (Exception ex)
        {
            wrapper._callbackException = ex;
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint OnHeaderCallback(byte* ptr, nuint size, nuint nmemb, nint userdata)
    {
        var handle = GCHandle.FromIntPtr(userdata);
        if (!handle.IsAllocated || handle.Target is not CurlEasyWrapper wrapper)
            return 0;

        try
        {
            var totalSize = checked((int)(size * nmemb));

            // Trim trailing CRLF from raw bytes (avoids TrimEnd allocation)
            var len = totalSize;
            while (len > 0 && (ptr[len - 1] == '\r' || ptr[len - 1] == '\n'))
                len--;

            if (len <= 0)
            {
                wrapper._streamingResponse?.CompleteHeaderBlock();
                return size * nmemb;
            }

            var headerLine = Encoding.UTF8.GetString(ptr, len);

            // HTTP status line marks a new response (redirect hop or final).
            // Clear previous headers so only the final response's headers remain.
            if (headerLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            {
                wrapper._responseHeaders.Clear();
            }
            else
            {
                wrapper._responseHeaders.Add(headerLine);
            }

            wrapper._streamingResponse?.AddHeaderLine(headerLine);

            return size * nmemb;
        }
        catch (Exception ex)
        {
            wrapper._callbackException = ex;
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint OnReadCallback(byte* ptr, nuint size, nuint nmemb, nint userdata)
    {
        var handle = GCHandle.FromIntPtr(userdata);
        if (!handle.IsAllocated || handle.Target is not CurlEasyWrapper wrapper)
            return CurlReadFuncAbort;

        try
        {
            var totalSize = checked((int)(size * nmemb));
            if (totalSize == 0)
                return 0;

            var read = wrapper.ReadRequestBody(new Span<byte>(ptr, totalSize));
            return checked((nuint)read);
        }
        catch (Exception ex)
        {
            wrapper._callbackException = ex;
            return CurlReadFuncAbort;
        }
    }

    private int ReadRequestBody(Span<byte> buffer)
    {
        if (_requestBodyStream is null)
            return 0;

        var readSize = buffer.Length;
        if (_requestBodyLength is { } contentLength)
        {
            var remainingBody = contentLength - _requestBodyBytesRead;
            if (remainingBody <= 0)
                return 0;

            readSize = checked((int)Math.Min(readSize, remainingBody));
        }

        if (_maxRequestBodyBytes is { } maxBytes)
        {
            var remainingLimit = maxBytes - _requestBodyBytesRead;
            if (remainingLimit < readSize)
            {
                if (remainingLimit < 0)
                    ThrowRequestBodyLimitExceeded(maxBytes);

                readSize = checked((int)Math.Min(readSize, remainingLimit + 1));
            }
        }

        if (readSize <= 0)
            return 0;

        var read = _requestBodyStream.Read(buffer[..readSize]);
        if (_maxRequestBodyBytes is { } max && _requestBodyBytesRead + read > max)
            ThrowRequestBodyLimitExceeded(max);

        _requestBodyBytesRead += read;
        return read;
    }

    private static void ThrowRequestBodyLimitExceeded(long maxBytes)
    {
        throw new InvalidOperationException(
            $"Request body exceeded the configured limit of {maxBytes} bytes.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnProgressCallback(nint clientp, long dltotal, long dlnow, long ultotal, long ulnow)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(clientp);
            if (!handle.IsAllocated || handle.Target is not CurlEasyWrapper wrapper)
                return 1;

            if (!wrapper._isAborted && wrapper._transferCancellationToken.IsCancellationRequested)
                wrapper._isAborted = true;

            return wrapper._isAborted ? 1 : 0;
        }
        catch
        {
            return 1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnDebugCallback(nint curl, int type, byte* data, nuint size, nint clientp)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(clientp);
            if (!handle.IsAllocated || handle.Target is not CurlEasyWrapper wrapper)
                return 0;

            var callback = wrapper._debugCallback;
            if (callback is null)
                return 0;

            var length = checked((int)size);
            var bytes = new byte[length];
            if (length > 0)
                new ReadOnlySpan<byte>(data, length).CopyTo(bytes);

            callback(new CurlDebugEvent((CurlDebugInfoType)type, bytes));
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string SetOptOperation(CurlOption option) => $"curl_easy_setopt({option})";

    private static Version MapHttpVersion(long httpVersion)
    {
        return httpVersion switch
        {
            1 => HttpVersion.Version10,
            2 => HttpVersion.Version11,
            3 or 4 or 5 => HttpVersion.Version20,
            30 or 31 => HttpVersion.Version30,
            _ => HttpVersion.Version11
        };
    }

    private static Uri? ParseUri(nint uriPtr)
    {
        if (uriPtr == 0)
            return null;

        var value = Marshal.PtrToStringUTF8(uriPtr);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private CurlTransferMetrics ReadTransferMetrics()
    {
        return new CurlTransferMetrics(
            SecondsToTimeSpan(GetInfoDouble(CurlInfo.TotalTime)),
            SecondsToTimeSpan(GetInfoDouble(CurlInfo.NameLookupTime)),
            SecondsToTimeSpan(GetInfoDouble(CurlInfo.ConnectTime)),
            SecondsToTimeSpan(GetInfoDouble(CurlInfo.AppConnectTime)),
            SecondsToTimeSpan(GetInfoDouble(CurlInfo.PreTransferTime)),
            SecondsToTimeSpan(GetInfoDouble(CurlInfo.StartTransferTime)),
            SecondsToTimeSpan(GetInfoDouble(CurlInfo.RedirectTime)),
            checked((int)GetInfoLong(CurlInfo.NumConnects)),
            GetInfoLong(CurlInfo.RequestSize),
            GetInfoLong(CurlInfo.HeaderSize),
            GetInfoString(CurlInfo.PrimaryIp),
            PositivePortOrNull(GetInfoLong(CurlInfo.PrimaryPort)),
            GetInfoString(CurlInfo.LocalIp),
            PositivePortOrNull(GetInfoLong(CurlInfo.LocalPort)))
        {
            DownloadedBytes = GetInfoOffT(CurlInfo.SizeDownloadT),
            UploadedBytes = GetInfoOffT(CurlInfo.SizeUploadT),
            DownloadSpeedBytesPerSecond = GetInfoOffT(CurlInfo.SpeedDownloadT),
            UploadSpeedBytesPerSecond = GetInfoOffT(CurlInfo.SpeedUploadT)
        };
    }

    private double GetInfoDouble(CurlInfo info)
    {
        var code = NativeMethods.EasyGetInfoDouble(_handle, info, out var value);
        CurlNativeErrors.ThrowIfError(code, $"curl_easy_getinfo({info})", GetErrorMessage());
        return value;
    }

    private long GetInfoLong(CurlInfo info)
    {
        var code = NativeMethods.EasyGetInfoLong(_handle, info, out var value);
        CurlNativeErrors.ThrowIfError(code, $"curl_easy_getinfo({info})", GetErrorMessage());
        return value;
    }

    private long GetInfoOffT(CurlInfo info)
    {
        var code = NativeMethods.EasyGetInfoOffT(_handle, info, out var value);
        CurlNativeErrors.ThrowIfError(code, $"curl_easy_getinfo({info})", GetErrorMessage());
        return value;
    }

    private string? GetInfoString(CurlInfo info)
    {
        var code = NativeMethods.EasyGetInfo(_handle, info, out var value);
        CurlNativeErrors.ThrowIfError(code, $"curl_easy_getinfo({info})", GetErrorMessage());
        return value == 0 ? null : Marshal.PtrToStringUTF8(value);
    }

    private static TimeSpan SecondsToTimeSpan(double seconds)
    {
        return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
    }

    private static int? PositivePortOrNull(long port)
    {
        return port > 0 ? checked((int)port) : null;
    }

    private static bool IsOffTOption(CurlOption option)
    {
        var value = (int)option;
        return value >= CurlOptionTypeOffT && value < CurlOptionTypeBlob;
    }

    private static long CheckedCLongValue(CurlOption option, long value)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            (value < int.MinValue || value > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"{option} exceeds the 32-bit C long range used by libcurl on Windows.");
        }

        return value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        FreeRequestBody();
        FreeHeaderList();
        FreeStringAllocations();

        // Clean up the curl handle before freeing the GCHandle: curl can invoke
        // callbacks during cleanup, and those still need a valid GCHandle.
        if (_handle != 0)
        {
            NativeMethods.EasyCleanup(_handle);
            _handle = 0;
        }

        // Now safe to free pinned memory - curl no longer holds references
        if (_errorBufferHandle.IsAllocated)
            _errorBufferHandle.Free();

        if (_gcHandle.IsAllocated)
            _gcHandle.Free();

        _responseBuffer.Dispose();
    }
}

/// <summary>
/// Response data from a completed curl transfer.
/// </summary>
internal readonly record struct CurlResponse(
    int StatusCode,
    byte[] Body,
    string[] Headers,
    Version? Version = null,
    Uri? EffectiveUri = null,
    int RedirectCount = 0,
    CurlTransferMetrics? Metrics = null);
