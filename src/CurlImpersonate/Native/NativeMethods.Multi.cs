using System.Runtime.InteropServices;
using CurlImpersonate.Enums;

namespace CurlImpersonate.Native;

internal static partial class NativeMethods
{
    // Multi handle functions

    /// <summary>
    /// Create a new curl multi handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_init")]
    internal static partial nint MultiInit();

    /// <summary>
    /// Clean up and free a curl multi handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_cleanup")]
    internal static partial CurlMultiCode MultiCleanup(nint curlm);

    /// <summary>
    /// Add an easy handle to a multi handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_add_handle")]
    internal static partial CurlMultiCode MultiAddHandle(nint curlm, nint curl);

    /// <summary>
    /// Remove an easy handle from a multi handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_remove_handle")]
    internal static partial CurlMultiCode MultiRemoveHandle(nint curlm, nint curl);

    /// <summary>
    /// Perform transfers on all added handles.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_perform")]
    internal static partial CurlMultiCode MultiPerform(nint curlm, out int runningHandles);

    /// <summary>
    /// Perform socket action on a multi handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_socket_action")]
    internal static partial CurlMultiCode MultiSocketAction(nint curlm, nint sockfd, int evBitmask, out int runningHandles);

    /// <summary>
    /// Set an option on a curl multi handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_setopt")]
    internal static partial CurlMultiCode MultiSetOpt(nint curlm, CurlMultiOption option, nint param);

    /// <summary>
    /// Associate user data with a socket.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_assign")]
    internal static partial CurlMultiCode MultiAssign(nint curlm, nint sockfd, nint sockptr);

    /// <summary>
    /// Get the timeout value for the next action.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_timeout")]
    internal static partial CurlMultiCode MultiTimeout(nint curlm, out long timeoutMs);

    /// <summary>
    /// Wait for activity on any file descriptor.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_wait")]
    internal static partial CurlMultiCode MultiWait(nint curlm, nint extraFds, uint extraNfds, int timeoutMs, out int numfds);

    /// <summary>
    /// Poll for activity on any file descriptor.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_poll")]
    internal static partial CurlMultiCode MultiPoll(nint curlm, nint extraFds, uint extraNfds, int timeoutMs, out int numfds);

    /// <summary>
    /// Wake up a waiting multi handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_wakeup")]
    internal static partial CurlMultiCode MultiWakeup(nint curlm);

    /// <summary>
    /// Get the error message for a multi error code.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_strerror")]
    internal static partial nint MultiStrError(CurlMultiCode code);

    /// <summary>
    /// Read a message from the multi handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_multi_info_read")]
    internal static partial nint MultiInfoRead(nint curlm, out int msgsInQueue);
}
