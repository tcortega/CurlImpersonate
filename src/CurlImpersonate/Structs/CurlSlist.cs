using System.Runtime.InteropServices;

namespace CurlImpersonate.Structs;

/// <summary>
/// Represents a curl_slist linked list node.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CurlSlist
{
    /// <summary>
    /// Pointer to the string data.
    /// </summary>
    public nint Data;

    /// <summary>
    /// Pointer to the next node.
    /// </summary>
    public nint Next;
}

/// <summary>
/// Represents a CURLMsg message from curl_multi_info_read.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CurlMsg
{
    /// <summary>
    /// Message type (CURLMSG_DONE = 1).
    /// </summary>
    public int Msg;

    /// <summary>
    /// The easy handle this message is about.
    /// </summary>
    public nint EasyHandle;

    /// <summary>
    /// Union data - use Result for CURLMSG_DONE.
    /// </summary>
    public CurlMsgData Data;
}

/// <summary>
/// Union data for CURLMsg.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct CurlMsgData
{
    /// <summary>
    /// Generic data pointer.
    /// </summary>
    [FieldOffset(0)]
    public nint Whatever;

    /// <summary>
    /// CURLcode result for completed transfers.
    /// </summary>
    [FieldOffset(0)]
    public int Result;
}

/// <summary>
/// CURLMSG values.
/// </summary>
public static class CurlMsgType
{
    /// <summary>No message.</summary>
    public const int None = 0;
    /// <summary>Transfer completed.</summary>
    public const int Done = 1;
}
