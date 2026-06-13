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
    private static bool _initialized;
    private static bool _processLifetimeInitialized;
    private static int _refCount;

    /// <summary>
    /// Initialize the curl library globally.
    /// </summary>
    /// <param name="flags">Initialization flags (default: CURL_GLOBAL_ALL).</param>
    /// <exception cref="CurlException">Thrown if initialization fails.</exception>
    public static void Initialize(long flags = NativeMethods.CurlGlobalAll)
    {
        lock (Lock)
        {
            EnsureInitializedCore(flags);
            _refCount++;
        }
    }

    /// <summary>
    /// Ensures curl is globally initialized without taking a public cleanup reference.
    /// </summary>
    internal static void EnsureInitialized(long flags = NativeMethods.CurlGlobalAll)
    {
        lock (Lock)
        {
            EnsureInitializedCore(flags);
            _processLifetimeInitialized = true;
        }
    }

    /// <summary>
    /// Clean up the curl library globally. Usually not needed as cleanup happens at process exit.
    /// </summary>
    public static void Cleanup()
    {
        lock (Lock)
        {
            if (!_initialized)
                return;

            if (_refCount <= 0)
                return;

            if (--_refCount > 0)
                return;

            if (_processLifetimeInitialized)
                return;

            NativeMethods.GlobalCleanup();
            _initialized = false;
            _refCount = 0;
        }
    }

    /// <summary>
    /// Gets whether the curl library has been initialized.
    /// </summary>
    public static bool IsInitialized
    {
        get { lock (Lock) { return _initialized; } }
    }

    internal static int ReferenceCount
    {
        get { lock (Lock) { return _refCount; } }
    }

    internal static bool HasProcessLifetimeInitialization
    {
        get { lock (Lock) { return _processLifetimeInitialized; } }
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

    /// <summary>
    /// Gets the runtime identifier used when resolving packaged native assets.
    /// </summary>
    public static string NativeRuntimeIdentifier => NativeLibraryResolver.GetRid();

    private static void EnsureInitializedCore(long flags)
    {
        if (_initialized)
            return;

        var result = NativeMethods.GlobalInit(flags);
        if (result != CurlCode.Ok)
            throw new CurlException(result, "Failed to initialize curl library");

        _initialized = true;
    }
}
