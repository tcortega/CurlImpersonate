using System.Runtime.InteropServices;

namespace CurlImpersonate.Native;

internal static partial class NativeMethods
{
    // String list functions

    /// <summary>
    /// Append a string to a curl slist.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_slist_append", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint SlistAppend(nint list, string data);

    /// <summary>
    /// Free all memory used by a curl slist.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_slist_free_all")]
    internal static partial void SlistFreeAll(nint list);
}
