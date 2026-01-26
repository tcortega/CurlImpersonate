using System.Runtime.InteropServices;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;
using Xunit;

namespace CurlImpersonate.Tests;

public class CurlEasyTests
{
    [Fact]
    public void EasyInit_ShouldReturnValidHandle()
    {
        var curl = NativeMethods.EasyInit();
        try
        {
            Assert.NotEqual(0, curl);
        }
        finally
        {
            if (curl != 0)
                NativeMethods.EasyCleanup(curl);
        }
    }

    [Fact]
    public void EasySetOpt_Url_ShouldSucceed()
    {
        var curl = NativeMethods.EasyInit();
        var url = "https://httpbin.org/get";
        var urlPtr = Marshal.StringToHGlobalAnsi(url);
        try
        {
            var result = NativeMethods.EasySetOpt(curl, CurlOption.Url, urlPtr);
            Assert.Equal(CurlCode.Ok, result);
        }
        finally
        {
            Marshal.FreeHGlobal(urlPtr);
            if (curl != 0)
                NativeMethods.EasyCleanup(curl);
        }
    }

    [Fact]
    public void EasyImpersonate_Chrome_ShouldSucceed()
    {
        var curl = NativeMethods.EasyInit();
        try
        {
            var result = NativeMethods.EasyImpersonate(curl, BrowserProfile.Chrome142.ToTargetString(), 1);
            Assert.Equal(CurlCode.Ok, result);
        }
        finally
        {
            if (curl != 0)
                NativeMethods.EasyCleanup(curl);
        }
    }

    [Fact]
    public void EasyImpersonate_Firefox_ShouldSucceed()
    {
        var curl = NativeMethods.EasyInit();
        try
        {
            var result = NativeMethods.EasyImpersonate(curl, BrowserProfile.Firefox144.ToTargetString(), 1);
            Assert.Equal(CurlCode.Ok, result);
        }
        finally
        {
            if (curl != 0)
                NativeMethods.EasyCleanup(curl);
        }
    }

    [Fact]
    public void EasyImpersonate_Safari_ShouldSucceed()
    {
        var curl = NativeMethods.EasyInit();
        try
        {
            var result = NativeMethods.EasyImpersonate(curl, BrowserProfile.Safari2601.ToTargetString(), 1);
            Assert.Equal(CurlCode.Ok, result);
        }
        finally
        {
            if (curl != 0)
                NativeMethods.EasyCleanup(curl);
        }
    }
}
