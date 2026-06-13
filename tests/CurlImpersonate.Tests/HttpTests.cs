using System.Runtime.InteropServices;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;
using Xunit;

namespace CurlImpersonate.Tests;

public class HttpTests
{
    [Fact]
    public void SetUrl_ShouldSucceed()
    {
        var curl = NativeMethods.EasyInit();
        Assert.NotEqual(0, curl);

        const string url = "https://httpbin.org/get";
        var urlPtr = Marshal.StringToHGlobalAnsi(url);
        try
        {
            var result = NativeMethods.EasySetOptPointer(curl, CurlOption.Url, urlPtr);
            Assert.Equal(CurlCode.Ok, result);
        }
        finally
        {
            Marshal.FreeHGlobal(urlPtr);
            NativeMethods.EasyCleanup(curl);
        }
    }

    [Fact]
    public void HttpPerform_ShouldSucceed()
    {
        var curl = NativeMethods.EasyInit();
        Assert.NotEqual(0, curl);

        const string url = "https://httpbin.org/get";
        var urlPtr = Marshal.StringToHGlobalAnsi(url);
        try
        {
            var result = NativeMethods.EasySetOptPointer(curl, CurlOption.Url, urlPtr);
            Assert.Equal(CurlCode.Ok, result);

            if (OperatingSystem.IsWindows())
            {
                // The bundled BoringSSL build has no default CA bundle on Windows;
                // raw handles must opt into the OS certificate store.
                result = NativeMethods.EasySetOptLong(curl, CurlOption.SslOptions, (long)CurlSslOption.NativeCa);
                Assert.Equal(CurlCode.Ok, result);
            }

            result = NativeMethods.EasyImpersonate(curl, BrowserProfile.Chrome142.ToTargetString(), 1);
            Assert.Equal(CurlCode.Ok, result);

            result = NativeMethods.EasyPerform(curl);
            Assert.Equal(CurlCode.Ok, result);
        }
        finally
        {
            Marshal.FreeHGlobal(urlPtr);
            NativeMethods.EasyCleanup(curl);
        }
    }
}
