using System.Runtime.InteropServices;
using CurlImpersonate.Native;
using Xunit;

namespace CurlImpersonate.Tests;

public class CurlGlobalTests
{
    [Fact]
    public void Initialize_ShouldSucceed()
    {
        try
        {
            // Throws if the native libraries cannot be loaded or init fails.
            CurlGlobal.Initialize();

            Assert.True(CurlGlobal.IsInitialized);
        }
        finally
        {
            CurlGlobal.Cleanup();
        }
    }

    [Fact]
    public void Version_ShouldReturnNonEmptyString()
    {
        try
        {
            CurlGlobal.Initialize();

            var version = CurlGlobal.Version;

            Assert.False(string.IsNullOrEmpty(version));
            Assert.Contains("libcurl", version);
            Assert.Contains("IMPERSONATE", version, StringComparison.Ordinal);
        }
        finally
        {
            CurlGlobal.Cleanup();
        }
    }

    [Fact]
    public void Initialize_ShouldTrackPublicReferenceCount()
    {
        var baselineReferences = CurlGlobal.ReferenceCount;
        try
        {
            CurlGlobal.Initialize();
            CurlGlobal.Initialize();

            Assert.Equal(baselineReferences + 2, CurlGlobal.ReferenceCount);

            CurlGlobal.Cleanup();
            CurlGlobal.Cleanup();

            Assert.Equal(baselineReferences, CurlGlobal.ReferenceCount);
        }
        finally
        {
            while (CurlGlobal.ReferenceCount > baselineReferences)
                CurlGlobal.Cleanup();
        }
    }

    [Fact]
    public void EasyInit_ShouldNotAddPublicGlobalReference()
    {
        var initialReferences = CurlGlobal.ReferenceCount;
        var curl = NativeMethods.EasyInit();

        try
        {
            Assert.NotEqual(0, curl);
            Assert.True(CurlGlobal.IsInitialized);
            Assert.Equal(initialReferences, CurlGlobal.ReferenceCount);
        }
        finally
        {
            if (curl != 0)
                NativeMethods.EasyCleanup(curl);
        }
    }

    [Fact]
    public void Cleanup_WithoutPublicReference_DoesNotCleanupInternalInitialization()
    {
        if (CurlGlobal.ReferenceCount != 0)
            return;

        var curl = NativeMethods.EasyInit();
        try
        {
            Assert.True(CurlGlobal.IsInitialized);
            CurlGlobal.Cleanup();
            Assert.True(CurlGlobal.IsInitialized);
            Assert.Equal(0, CurlGlobal.ReferenceCount);
        }
        finally
        {
            if (curl != 0)
                NativeMethods.EasyCleanup(curl);
        }
    }

    [Fact]
    public void Cleanup_WithPublicReference_DoesNotCleanupInternalInitialization()
    {
        var baselineReferences = CurlGlobal.ReferenceCount;
        var curl = NativeMethods.EasyInit();

        try
        {
            Assert.True(CurlGlobal.HasProcessLifetimeInitialization);
            CurlGlobal.Initialize();
            Assert.Equal(baselineReferences + 1, CurlGlobal.ReferenceCount);

            CurlGlobal.Cleanup();

            Assert.True(CurlGlobal.IsInitialized);
            Assert.Equal(baselineReferences, CurlGlobal.ReferenceCount);
        }
        finally
        {
            while (CurlGlobal.ReferenceCount > baselineReferences)
                CurlGlobal.Cleanup();

            if (curl != 0)
                NativeMethods.EasyCleanup(curl);
        }
    }

    [Fact]
    public void NativeRuntimeIdentifier_ShouldMatchCurrentPlatform()
    {
        var rid = CurlGlobal.NativeRuntimeIdentifier;

        if (OperatingSystem.IsWindows())
            Assert.StartsWith("win-", rid, StringComparison.Ordinal);
        else if (OperatingSystem.IsMacOS())
            Assert.StartsWith("osx-", rid, StringComparison.Ordinal);
        else
            Assert.StartsWith("linux-", rid, StringComparison.Ordinal);

        Assert.Contains(
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            rid,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Architecture.X64, false, false, "linux-x64", false, "linux-x64")]
    [InlineData(Architecture.Arm64, false, false, "linux-arm64", false, "linux-arm64")]
    [InlineData(Architecture.X64, false, false, "linux-musl-x64", false, "linux-musl-x64")]
    [InlineData(Architecture.Arm64, false, false, "linux-arm64", true, "linux-musl-arm64")]
    [InlineData(Architecture.X64, true, false, "win-x64", false, "win-x64")]
    [InlineData(Architecture.Arm64, false, true, "osx-arm64", false, "osx-arm64")]
    public void NativeRuntimeIdentifier_ShouldMapSupportedRuntimeIdentifiers(
        Architecture architecture,
        bool isWindows,
        bool isMacOS,
        string runtimeIdentifier,
        bool isMuslLinux,
        string expected)
    {
        var rid = NativeLibraryResolver.GetRid(
            architecture,
            isWindows,
            isMacOS,
            runtimeIdentifier,
            isMuslLinux);

        Assert.Equal(expected, rid);
    }

    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    public void NativeRuntimeIdentifier_ShouldRejectUnsupportedArchitectures(Architecture architecture)
    {
        Assert.Throws<PlatformNotSupportedException>(
            () => NativeLibraryResolver.GetRid(
                architecture,
                isWindows: false,
                isMacOS: false,
                runtimeIdentifier: "linux-x64",
                isMuslLinux: false));
    }

    [Fact]
    public void NativeRuntimeIdentifier_ShouldRejectUnsupportedOperatingSystems()
    {
        Assert.Throws<PlatformNotSupportedException>(
            () => NativeLibraryResolver.GetRid(
                Architecture.X64,
                isWindows: false,
                isMacOS: false,
                runtimeIdentifier: "freebsd-x64",
                isMuslLinux: false));
    }

    [Theory]
    [InlineData(NativeMethods.CurlLibrary, true, false, "libcurl-impersonate.dll")]
    [InlineData(NativeMethods.ShimLibrary, true, false, "curl_shim.dll")]
    [InlineData(NativeMethods.CurlLibrary, false, true, "libcurl_shim.dylib")]
    [InlineData(NativeMethods.ShimLibrary, false, true, "libcurl_shim.dylib")]
    [InlineData(NativeMethods.CurlLibrary, false, false, "libcurl_shim.so")]
    [InlineData(NativeMethods.ShimLibrary, false, false, "libcurl_shim.so")]
    public void NativeLibraryResolver_ShouldMapPackagedLibraryNames(
        string libraryName,
        bool isWindows,
        bool isMacOS,
        string expected)
    {
        var platformName = NativeLibraryResolver.GetPlatformLibraryName(
            libraryName,
            isWindows,
            isMacOS);

        Assert.Equal(expected, platformName);
    }
}
