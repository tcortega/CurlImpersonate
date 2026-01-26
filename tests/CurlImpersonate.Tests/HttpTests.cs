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
            var result = NativeMethods.EasySetOpt(curl, CurlOption.Url, urlPtr);
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
            var result = NativeMethods.EasySetOpt(curl, CurlOption.Url, urlPtr);
            Assert.Equal(CurlCode.Ok, result);

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
