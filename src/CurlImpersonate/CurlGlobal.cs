using System.Runtime.InteropServices;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;

namespace CurlImpersonate;

/// <summary>
/// Global curl library initialization and cleanup.
/// </summary>
public static class CurlGlobal
{
    private static int _initialized;

    /// <summary>
    /// Initialize the curl library globally. This is called automatically via module initializer.
    /// </summary>
    /// <param name="flags">Initialization flags (default: CURL_GLOBAL_ALL).</param>
    /// <exception cref="CurlException">Thrown if initialization fails.</exception>
    public static void Initialize(long flags = NativeMethods.CURL_GLOBAL_ALL)
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0) return;
        
        var result = NativeMethods.GlobalInit(flags);
        if (result == CurlCode.Ok) return;
        
        Interlocked.Exchange(ref _initialized, 0);
        throw new CurlException(result, "Failed to initialize curl library");
    }

    /// <summary>
    /// Clean up the curl library globally. Usually not needed as cleanup happens at process exit.
    /// </summary>
    public static void Cleanup()
    {
        if (Interlocked.CompareExchange(ref _initialized, 0, 1) == 1)
        {
            NativeMethods.GlobalCleanup();
        }
    }

    /// <summary>
    /// Gets whether the curl library has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized == 1;

    /// <summary>
    /// Gets the curl library version string.
    /// </summary>
    public static string Version
    {
        get
        {
            var ptr = NativeMethods.Version();
            return ptr != 0 ? Marshal.PtrToStringAnsi(ptr) ?? "" : "";
        }
    }
}
