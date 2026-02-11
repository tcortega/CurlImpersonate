using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CurlImpersonate.Native;

/// <summary>
/// Handles native library resolution for curl-impersonate libraries.
/// </summary>
internal static class NativeLibraryResolver
{
    private static bool _initialized;
    private static readonly Lock Lock = new();

    /// <summary>
    /// Initialize the native library resolver.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        lock (Lock)
        {
            if (_initialized) return;

            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, ResolveLibrary);
            _initialized = true;
        }
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Map library names to platform-specific names
        var platformLibraryName = GetPlatformLibraryName(libraryName);

        // Try loading from various locations
        if (TryLoadFromRuntimesFolder(platformLibraryName, out var handle))
            return handle;

        if (TryLoadFromAssemblyDirectory(platformLibraryName, out handle) || TryLoadFromSystemPaths(platformLibraryName, out handle))
            return handle;

        // Let the default resolver try
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out handle))
            return handle;

        // Provide helpful error message
        throw new DllNotFoundException(
            $"Unable to load native library '{libraryName}'. " +
            $"Ensure the curl-impersonate native libraries are installed in one of these locations:\n" +
            $"  - runtimes/{GetRid()}/native/\n" +
            $"  - The application directory\n" +
            $"  - System library paths\n" +
            $"Run 'python native/scripts/fetch_dependencies.py' to download the required libraries.");
    }

    private static string GetPlatformLibraryName(string libraryName)
    {
        if (OperatingSystem.IsWindows())
        {
            return libraryName switch
            {
                NativeMethods.ShimLibrary => "curl_shim.dll",
                NativeMethods.CurlLibrary => "libcurl.dll",
                _ => $"{libraryName}.dll"
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return libraryName switch
            {
                NativeMethods.ShimLibrary => "libcurl_shim.dylib",
                NativeMethods.CurlLibrary => "libcurl_shim.dylib",
                _ => $"lib{libraryName}.dylib"
            };
        }

        // Linux and others
        return libraryName switch
        {
            NativeMethods.ShimLibrary => "libcurl_shim.so",
            NativeMethods.CurlLibrary => "libcurl_shim.so",
            _ => $"lib{libraryName}.so"
        };
    }

    private static string GetRid()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        if (OperatingSystem.IsWindows())
            return $"win-{arch}";
        
        if (OperatingSystem.IsMacOS())
            return $"osx-{arch}";
        
        return $"linux-{arch}";
    }

    [SuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file")]
    private static string? GetBaseDirectory()
    {
        var assemblyLocation = typeof(NativeLibraryResolver).Assembly.Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            return Path.GetDirectoryName(assemblyLocation);
        }
        
        return AppContext.BaseDirectory;
    }

    private static bool TryLoadFromRuntimesFolder(string libraryName, out nint handle)
    {
        handle = 0;

        var baseDir = GetBaseDirectory();
        if (string.IsNullOrEmpty(baseDir))
            return false;

        var rid = GetRid();
        var paths = new[]
        {
            Path.Combine(baseDir, "runtimes", rid, "native", libraryName),
            Path.Combine(baseDir, "..", "..", "runtimes", rid, "native", libraryName),
        };

        foreach (var path in paths)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
                return true;
        }

        return false;
    }

    private static bool TryLoadFromAssemblyDirectory(string libraryName, out nint handle)
    {
        handle = 0;

        var baseDir = GetBaseDirectory();
        if (string.IsNullOrEmpty(baseDir))
            return false;

        var path = Path.Combine(baseDir, libraryName);
        if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
            return true;

        return false;
    }

    private static bool TryLoadFromSystemPaths(string libraryName, out nint handle)
    {
        return NativeLibrary.TryLoad(libraryName, out handle);
    }
}
