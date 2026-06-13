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
        var platformLibraryName = GetPlatformLibraryName(libraryName);

        if (TryLoadFromRuntimesFolder(platformLibraryName, out var handle))
            return handle;

        if (TryLoadFromAssemblyDirectory(platformLibraryName, out handle) || TryLoadFromSystemPaths(platformLibraryName, out handle))
            return handle;

        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out handle))
            return handle;

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
        return GetPlatformLibraryName(
            libraryName,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS());
    }

    internal static string GetPlatformLibraryName(
        string libraryName,
        bool isWindows,
        bool isMacOS)
    {
        if (isWindows)
        {
            return libraryName switch
            {
                NativeMethods.ShimLibrary => "curl_shim.dll",
                NativeMethods.CurlLibrary => "libcurl-impersonate.dll",
                _ => $"{libraryName}.dll"
            };
        }

        if (isMacOS)
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

    internal static string GetRid()
    {
        return GetRid(
            RuntimeInformation.OSArchitecture,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            RuntimeInformation.RuntimeIdentifier,
            IsMuslLinux());
    }

    internal static string GetRid(
        Architecture architecture,
        bool isWindows,
        bool isMacOS,
        string runtimeIdentifier,
        bool isMuslLinux)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"CurlImpersonate native assets support only x64 and arm64; current architecture is {architecture}.")
        };

        if (isWindows)
            return $"win-{arch}";

        if (isMacOS)
            return $"osx-{arch}";

        if (!runtimeIdentifier.StartsWith("linux", StringComparison.Ordinal))
        {
            throw new PlatformNotSupportedException(
                $"CurlImpersonate native assets support Windows, macOS, and Linux; current runtime identifier is '{runtimeIdentifier}'.");
        }

        var linuxPrefix = isMuslLinux
            || runtimeIdentifier.StartsWith("linux-musl-", StringComparison.Ordinal)
            ? "linux-musl"
            : "linux";

        return $"{linuxPrefix}-{arch}";
    }

    private static bool IsMuslLinux()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        if (RuntimeInformation.RuntimeIdentifier.StartsWith(
            "linux-musl-",
            StringComparison.Ordinal))
        {
            return true;
        }

        return HasMuslLoader("/lib") || HasMuslLoader("/usr/lib");
    }

    private static bool HasMuslLoader(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                && Directory.EnumerateFiles(directory, "ld-musl-*.so.1").Any();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string GetBaseDirectory() => AppContext.BaseDirectory;

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
