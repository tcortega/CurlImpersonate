using CurlImpersonate.Enums;
using CurlImpersonate.Http.Internal;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class CurlHandlerOptionsValidationTests
{
    [Fact]
    public void Constructor_InvalidBrowserProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions
            {
                BrowserProfile = (BrowserProfile)999
            }));
    }

    [Fact]
    public void Constructor_InvalidHeaderPolicy_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions
            {
                HeaderPolicy = (BrowserHeaderPolicy)999
            }));
    }

    [Fact]
    public void Constructor_InvalidVersionPolicy_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions
            {
                VersionPolicy = (HttpVersionPolicy)999
            }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad Name")]
    [InlineData("Bad:Name")]
    [InlineData("Bad\r\nName")]
    [InlineData("Name,Other")]
    public void Constructor_InvalidHeaderOrder_Throws(string headerName)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new CurlHandler(new CurlHandlerOptions
            {
                HeaderOrder = [headerName]
            }));

        Assert.Equal(nameof(CurlHandlerOptions.HeaderOrder), exception.ParamName);
    }

    [Fact]
    public void Constructor_ValidHeaderOrder_DoesNotThrow()
    {
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            HeaderOrder = ["X-Test", "Sec-Ch-Ua", "X_Trace.Id"]
        });
    }

    [Theory]
    [InlineData((CurlProxyAuth)(-1))]
    [InlineData((CurlProxyAuth)32)]
    [InlineData((CurlProxyAuth)128)]
    public void Constructor_InvalidProxyAuth_Throws(CurlProxyAuth proxyAuth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions
            {
                ProxyAuth = proxyAuth
            }));
    }

    [Theory]
    [InlineData(CurlProxyAuth.None)]
    [InlineData(CurlProxyAuth.Basic | CurlProxyAuth.Digest)]
    [InlineData(CurlProxyAuth.Any)]
    [InlineData(CurlProxyAuth.AnySafe)]
    public void Constructor_ValidProxyAuth_DoesNotThrow(CurlProxyAuth proxyAuth)
    {
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            ProxyAuth = proxyAuth
        });
    }

    [Fact]
    public void MapRequestVersion_InvalidRequestVersionPolicy_Throws()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/")
        {
            VersionPolicy = (HttpVersionPolicy)999
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RequestMapper.MapRequestVersion(request, new CurlHandlerOptions()));
    }

    [Fact]
    public void MapRequestVersion_InvalidOptionsVersionPolicy_Throws()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        var options = new CurlHandlerOptions
        {
            VersionPolicy = (HttpVersionPolicy)999
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RequestMapper.MapRequestVersion(request, options));
    }
}
