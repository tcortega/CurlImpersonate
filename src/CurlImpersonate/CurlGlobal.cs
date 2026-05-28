using System.Runtime.InteropServices;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;

namespace CurlImpersonate;

/// <summary>
/// Global curl library initialization and cleanup.
/// </summary>
public static class CurlGlobal
{
    private static readonly Lock Lock = new();
    private static int _refCount;

    /// <summary>
    /// Initialize the curl library globally. This is called automatically via module initializer.
    /// </summary>
    /// <param name="flags">Initialization flags (default: CURL_GLOBAL_ALL).</param>
    /// <exception cref="CurlException">Thrown if initialization fails.</exception>
    public static void Initialize(long flags = NativeMethods.CurlGlobalAll)
    {
        lock (Lock)
        {
            if (_refCount == 0)
            {
                var result = NativeMethods.GlobalInit(flags);
                if (result != CurlCode.Ok)
                    throw new CurlException(result, "Failed to initialize curl library");
            }
            _refCount++;
        }
    }

    /// <summary>
    /// Clean up the curl library globally. Usually not needed as cleanup happens at process exit.
    /// </summary>
    public static void Cleanup()
    {
        lock (Lock)
        {
            if (_refCount <= 0) return;
            if (--_refCount == 0)
                NativeMethods.GlobalCleanup();
        }
    }

    /// <summary>
    /// Gets whether the curl library has been initialized.
    /// </summary>
    public static bool IsInitialized
    {
        get { lock (Lock) { return _refCount > 0; } }
    }

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
