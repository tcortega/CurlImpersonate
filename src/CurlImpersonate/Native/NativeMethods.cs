using System.Runtime.InteropServices;
using CurlImpersonate.Enums;

namespace CurlImpersonate.Native;

/// <summary>
/// P/Invoke declarations for curl-impersonate native library.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// Library name for the curl shim (variadic wrapper).
    /// </summary>
    public const string ShimLibrary = "curl_shim";

    /// <summary>
    /// Library name for the main curl-impersonate library.
    /// </summary>
    public const string CurlLibrary = "curl";

    /// <summary>
    /// CURL_GLOBAL_ALL flag for curl_global_init.
    /// </summary>
    public const long CurlGlobalAll = 3;

    /// <summary>
    /// CURL_GLOBAL_SSL flag.
    /// </summary>
    public const long CurlGlobalSsl = 1;

    /// <summary>
    /// CURL_GLOBAL_WIN32 flag.
    /// </summary>
    public const long CurlGlobalWin32 = 2;

    /// <summary>
    /// CURL_GLOBAL_NOTHING flag.
    /// </summary>
    public const long CurlGlobalNothing = 0;

    // Global functions

    /// <summary>
    /// Initialize the curl library globally.
    /// </summary>
    [LibraryImport(ShimLibrary, EntryPoint = "shim_global_init")]
    internal static partial CurlCode GlobalInit(long flags);

    /// <summary>
    /// Clean up the curl library globally.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_global_cleanup")]
    internal static partial void GlobalCleanup();

    /// <summary>
    /// Get the curl library version string.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_version")]
    internal static partial nint Version();
}
