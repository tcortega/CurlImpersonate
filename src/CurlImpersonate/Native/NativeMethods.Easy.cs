using System.Runtime.InteropServices;
using CurlImpersonate.Enums;

namespace CurlImpersonate.Native;

internal static partial class NativeMethods
{
    // Easy handle functions

    /// <summary>
    /// Raw P/Invoke for curl_easy_init.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_easy_init")]
    private static partial nint EasyInitNative();

    /// <summary>
    /// Create a new curl easy handle. Automatically initializes curl globally on first use.
    /// </summary>
    internal static nint EasyInit()
    {
        CurlGlobal.Initialize();
        return EasyInitNative();
    }

    /// <summary>
    /// Set an option on a curl easy handle (via shim for variadic handling).
    /// </summary>
    [LibraryImport(ShimLibrary, EntryPoint = "shim_easy_setopt")]
    internal static partial CurlCode EasySetOpt(nint curl, CurlOption option, nint param);

    /// <summary>
    /// Get information from a curl easy handle (pointer output, via shim for variadic handling).
    /// </summary>
    [LibraryImport(ShimLibrary, EntryPoint = "shim_easy_getinfo")]
    internal static partial CurlCode EasyGetInfo(nint curl, CurlInfo info, out nint value);

    /// <summary>
    /// Get information from a curl easy handle (long output, via shim for variadic handling).
    /// </summary>
    [LibraryImport(ShimLibrary, EntryPoint = "shim_easy_getinfo")]
    internal static partial CurlCode EasyGetInfoLong(nint curl, CurlInfo info, out long value);

    /// <summary>
    /// Get information from a curl easy handle (double output, via shim for variadic handling).
    /// </summary>
    [LibraryImport(ShimLibrary, EntryPoint = "shim_easy_getinfo")]
    internal static partial CurlCode EasyGetInfoDouble(nint curl, CurlInfo info, out double value);

    /// <summary>
    /// Perform a blocking transfer using the curl easy handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_easy_perform")]
    internal static partial CurlCode EasyPerform(nint curl);

    /// <summary>
    /// Clean up and free a curl easy handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_easy_cleanup")]
    internal static partial void EasyCleanup(nint curl);

    /// <summary>
    /// Reset a curl easy handle to its initial state.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_easy_reset")]
    internal static partial void EasyReset(nint curl);

    /// <summary>
    /// Configure browser impersonation on a curl easy handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_easy_impersonate", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial CurlCode EasyImpersonate(nint curl, string target, int defaultHeaders);

    /// <summary>
    /// Duplicate a curl easy handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_easy_duphandle")]
    internal static partial nint EasyDupHandle(nint curl);

    /// <summary>
    /// Perform connection upkeep on a curl easy handle.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_easy_upkeep")]
    internal static partial CurlCode EasyUpkeep(nint curl);

    /// <summary>
    /// Get the error message for a curl error code.
    /// </summary>
    [LibraryImport(CurlLibrary, EntryPoint = "curl_easy_strerror")]
    internal static partial nint EasyStrError(CurlCode code);
}
