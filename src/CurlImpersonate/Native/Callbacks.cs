using System.Runtime.InteropServices;

namespace CurlImpersonate.Native;

/// <summary>
/// Callback delegate for curl write operations (CURLOPT_WRITEFUNCTION).
/// </summary>
/// <param name="ptr">Pointer to the data.</param>
/// <param name="size">Size of each data element.</param>
/// <param name="nmemb">Number of data elements.</param>
/// <param name="userdata">User-provided data pointer.</param>
/// <returns>Number of bytes processed.</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate nuint WriteCallback(byte* ptr, nuint size, nuint nmemb, nint userdata);

/// <summary>
/// Callback delegate for curl read operations (CURLOPT_READFUNCTION).
/// </summary>
/// <param name="ptr">Pointer to the buffer to fill.</param>
/// <param name="size">Size of each data element.</param>
/// <param name="nmemb">Number of data elements.</param>
/// <param name="userdata">User-provided data pointer.</param>
/// <returns>Number of bytes provided.</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate nuint ReadCallback(byte* ptr, nuint size, nuint nmemb, nint userdata);

/// <summary>
/// Callback delegate for curl header operations (CURLOPT_HEADERFUNCTION).
/// </summary>
/// <param name="ptr">Pointer to the header data.</param>
/// <param name="size">Size of each data element.</param>
/// <param name="nmemb">Number of data elements.</param>
/// <param name="userdata">User-provided data pointer.</param>
/// <returns>Number of bytes processed.</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate nuint HeaderCallback(byte* ptr, nuint size, nuint nmemb, nint userdata);

/// <summary>
/// Callback delegate for curl progress operations (CURLOPT_XFERINFOFUNCTION).
/// </summary>
/// <param name="clientp">User-provided data pointer.</param>
/// <param name="dltotal">Total bytes to download.</param>
/// <param name="dlnow">Bytes downloaded so far.</param>
/// <param name="ultotal">Total bytes to upload.</param>
/// <param name="ulnow">Bytes uploaded so far.</param>
/// <returns>0 to continue, non-zero to abort.</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int ProgressCallback(nint clientp, long dltotal, long dlnow, long ultotal, long ulnow);

/// <summary>
/// Callback delegate for curl debug operations (CURLOPT_DEBUGFUNCTION).
/// </summary>
/// <param name="curl">The curl handle.</param>
/// <param name="type">Debug info type.</param>
/// <param name="data">Pointer to the debug data.</param>
/// <param name="size">Size of the debug data.</param>
/// <param name="clientp">User-provided data pointer.</param>
/// <returns>0 (return value is ignored).</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int DebugCallback(nint curl, int type, byte* data, nuint size, nint clientp);

/// <summary>
/// Callback delegate for curl multi socket operations (CURLMOPT_SOCKETFUNCTION).
/// </summary>
/// <param name="curl">The easy handle.</param>
/// <param name="sockfd">The socket file descriptor.</param>
/// <param name="what">What to watch for (CURL_POLL_* values).</param>
/// <param name="clientp">User-provided data pointer.</param>
/// <param name="socketp">Socket-specific user data pointer.</param>
/// <returns>0 for success.</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int SocketCallback(nint curl, nint sockfd, int what, nint clientp, nint socketp);

/// <summary>
/// Callback delegate for curl multi timer operations (CURLMOPT_TIMERFUNCTION).
/// </summary>
/// <param name="curlm">The multi handle.</param>
/// <param name="timeoutMs">Timeout in milliseconds (-1 to cancel timer).</param>
/// <param name="clientp">User-provided data pointer.</param>
/// <returns>0 for success.</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int TimerCallback(nint curlm, long timeoutMs, nint clientp);

/// <summary>
/// Debug info types for CURLOPT_DEBUGFUNCTION.
/// </summary>
public static class CurlInfoType
{
    /// <summary>Text information.</summary>
    public const int Text = 0;
    /// <summary>Incoming header data.</summary>
    public const int HeaderIn = 1;
    /// <summary>Outgoing header data.</summary>
    public const int HeaderOut = 2;
    /// <summary>Incoming data.</summary>
    public const int DataIn = 3;
    /// <summary>Outgoing data.</summary>
    public const int DataOut = 4;
    /// <summary>Incoming SSL data.</summary>
    public const int SslDataIn = 5;
    /// <summary>Outgoing SSL data.</summary>
    public const int SslDataOut = 6;
}

/// <summary>
/// Socket poll action values for CURLMOPT_SOCKETFUNCTION.
/// </summary>
public static class CurlPoll
{
    /// <summary>No action.</summary>
    public const int None = 0;
    /// <summary>Wait for incoming data.</summary>
    public const int In = 1;
    /// <summary>Wait for outgoing data.</summary>
    public const int Out = 2;
    /// <summary>Wait for incoming or outgoing data.</summary>
    public const int InOut = 3;
    /// <summary>Remove the socket from polling.</summary>
    public const int Remove = 4;
}

/// <summary>
/// Socket select action values for curl_multi_socket_action.
/// </summary>
public static class CurlCSelect
{
    /// <summary>Socket is readable.</summary>
    public const int In = 1;
    /// <summary>Socket is writable.</summary>
    public const int Out = 2;
    /// <summary>Socket has error.</summary>
    public const int Err = 4;
}
