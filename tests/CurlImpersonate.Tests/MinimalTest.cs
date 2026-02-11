using System.Runtime.InteropServices;
using Xunit;

namespace CurlImpersonate.Tests;

public partial class MinimalTest
{
    [Fact]
    public void LoadLibrary_ShouldSucceed()
    {
        var loaded = NativeLibrary.TryLoad("libcurl_shim.dylib", out var handle);
        Assert.True(loaded, "Failed to load libcurl_shim.dylib");
        if (handle != 0)
            NativeLibrary.Free(handle);
    }

    [Fact]
    public void CurlVersion_ShouldWork()
    {
        // Test calling curl_version directly
        var versionPtr = curl_version();
        Assert.NotEqual(nint.Zero, versionPtr);
        var version = Marshal.PtrToStringAnsi(versionPtr);
        Assert.NotNull(version);
        Assert.Contains("libcurl", version);
    }

    [LibraryImport("libcurl_shim", EntryPoint = "curl_version")]
    private static partial nint curl_version();
}
