using System.Runtime.InteropServices;
using CurlImpersonate.Native;
using Xunit;

namespace CurlImpersonate.Tests;

public class MinimalTest
{
    [Fact]
    public void LoadLibrary_ShouldSucceed()
    {
        var libraryName = GetPlatformLibraryName();
        var loaded = NativeLibrary.TryLoad(libraryName, out var handle);
        Assert.True(loaded, $"Failed to load {libraryName}");
        if (handle != 0)
            NativeLibrary.Free(handle);
    }

    [Fact]
    public void CurlVersion_ShouldWork()
    {
        NativeLibraryResolver.Initialize();
        var versionPtr = NativeMethods.Version();
        Assert.NotEqual(nint.Zero, versionPtr);
        var version = Marshal.PtrToStringAnsi(versionPtr);
        Assert.NotNull(version);
        Assert.Contains("libcurl", version);
    }

    private static string GetPlatformLibraryName()
    {
        if (OperatingSystem.IsWindows())
            return "curl_shim.dll";
        if (OperatingSystem.IsMacOS())
            return "libcurl_shim.dylib";
        return "libcurl_shim.so";
    }
}
