using System.Runtime.InteropServices;
using System.Text;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// Wraps a SafeCurlEasyHandle with callbacks, buffers, and request state management.
/// </summary>
internal sealed unsafe class CurlEasyWrapper : IDisposable
{
    private nint _handle;
    private GCHandle _gcHandle;
    private bool _disposed;

    // Callback delegates - stored as fields to prevent GC
    private readonly WriteCallback _writeCallback;
    private readonly HeaderCallback _headerCallback;

    // Error buffer (256 bytes as per curl documentation)
    private readonly byte[] _errorBuffer = new byte[256];
    private GCHandle _errorBufferHandle;

    // Request body
    private byte[]? _requestBody;
    private GCHandle _requestBodyHandle;

    // Header list (must be freed after transfer)
    private nint _headerList;

    // String allocations (must remain valid until reset/dispose)
    private readonly List<nint> _stringAllocations = new();

    // Response accumulation
    private readonly ResponseBuffer _responseBuffer = new();
    private readonly List<string> _responseHeaders = new();

    // Transfer state
    private TaskCompletionSource<CurlResponse>? _completionSource;
    private volatile bool _isAborted;
    private CurlEventLoop? _eventLoop;

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
    /// Task completion source for async operation.
    /// </summary>
    public TaskCompletionSource<CurlResponse>? CompletionSource
    {
        get => _completionSource;
        set => _completionSource = value;
    }

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
        
        _writeCallback = OnWriteCallback;
        _headerCallback = OnHeaderCallback;
    }

    /// <summary>
    /// Sets up callbacks for response handling. Call this AFTER curl_easy_impersonate
    /// since impersonation may reset callback options.
    /// </summary>
    public void SetupCallbacks()
    {
        NativeMethods.EasySetOpt(_handle, CurlOption.ErrorBuffer, _errorBufferHandle.AddrOfPinnedObject());

        NativeMethods.EasySetOpt(_handle, CurlOption.WriteFunction,
            Marshal.GetFunctionPointerForDelegate(_writeCallback));
        NativeMethods.EasySetOpt(_handle, CurlOption.WriteData, GCHandle.ToIntPtr(_gcHandle));

        NativeMethods.EasySetOpt(_handle, CurlOption.HeaderFunction,
            Marshal.GetFunctionPointerForDelegate(_headerCallback));
        NativeMethods.EasySetOpt(_handle, CurlOption.HeaderData, GCHandle.ToIntPtr(_gcHandle));
    }

    /// <summary>
    /// Sets the event loop for abort handling.
    /// </summary>
    public void SetEventLoop(CurlEventLoop eventLoop)
    {
        _eventLoop = eventLoop;
    }

    /// <summary>
    /// Aborts the current transfer.
    /// </summary>
    public void Abort()
    {
        _isAborted = true;
        _eventLoop?.QueueRemoval(this);
    }

    /// <summary>
    /// Sets the request body data.
    /// </summary>
    public void SetRequestBody(byte[] body)
    {
        FreeRequestBody();

        _requestBody = body;
        _requestBodyHandle = GCHandle.Alloc(body, GCHandleType.Pinned);

        NativeMethods.EasySetOpt(_handle, CurlOption.PostFields, _requestBodyHandle.AddrOfPinnedObject());
        // PostFieldSizeLarge is an OFF_T option - shim expects pointer to value
        SetLongOption(CurlOption.PostFieldSizeLarge, body.Length);
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
            NativeMethods.EasySetOpt(_handle, CurlOption.HttpHeader, _headerList);
        }
    }

    /// <summary>
    /// Sets a string option on the curl handle, keeping memory alive until reset/dispose.
    /// </summary>
    public void SetStringOption(CurlOption option, string value)
    {
        var ptr = Marshal.StringToCoTaskMemUTF8(value);
        _stringAllocations.Add(ptr);
        NativeMethods.EasySetOpt(_handle, option, ptr);
    }

    /// <summary>
    /// Sets a numeric (long) option on the curl handle.
    /// The shim expects a pointer to the value for numeric options.
    /// </summary>
    public CurlCode SetLongOption(CurlOption option, long value)
    {
        // The shim dereferences the pointer immediately, so a stack pointer is valid.
        return NativeMethods.EasySetOpt(_handle, option, (nint)(&value));
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
        // Get status code
        NativeMethods.EasyGetInfoLong(_handle, CurlInfo.ResponseCode, out var statusCode);

        return new(
            (int)statusCode,
            _responseBuffer.ToArray(),
            _responseHeaders.ToArray());
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
        _responseHeaders.Clear();
        _isAborted = false;
        _completionSource = null;
        _eventLoop = null;
        Array.Clear(_errorBuffer);

        // Defensive: re-set callbacks so the handle is never in a bare state.
        // ConfigureAsync calls SetupCallbacks() again after EasyImpersonate,
        // which may override these.
        SetupCallbacks();
    }

    private void FreeRequestBody()
    {
        if (_requestBodyHandle.IsAllocated)
        {
            _requestBodyHandle.Free();
        }
        _requestBody = null;
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

    private static nuint OnWriteCallback(byte* ptr, nuint size, nuint nmemb, nint userdata)
    {
        var handle = GCHandle.FromIntPtr(userdata);
        if (!handle.IsAllocated || handle.Target is not CurlEasyWrapper wrapper)
            return 0;

        var totalSize = (int)(size * nmemb);
        var span = new ReadOnlySpan<byte>(ptr, totalSize);
        wrapper._responseBuffer.Write(span);

        return size * nmemb;
    }

    private static nuint OnHeaderCallback(byte* ptr, nuint size, nuint nmemb, nint userdata)
    {
        var handle = GCHandle.FromIntPtr(userdata);
        if (!handle.IsAllocated || handle.Target is not CurlEasyWrapper wrapper)
            return 0;

        var totalSize = (int)(size * nmemb);

        // Trim trailing CRLF from raw bytes (avoids TrimEnd allocation)
        var len = totalSize;
        while (len > 0 && (ptr[len - 1] == '\r' || ptr[len - 1] == '\n'))
            len--;

        if (len <= 0) return size * nmemb;
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

        return size * nmemb;
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

        // CRITICAL: Clean up curl handle FIRST, before freeing GCHandle
        // Curl may invoke callbacks during cleanup, which need valid GCHandle
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
    string[] Headers);
