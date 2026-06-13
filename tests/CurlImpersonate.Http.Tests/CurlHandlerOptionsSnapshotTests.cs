using System.Net;
using System.Reflection;
using CurlImpersonate.Enums;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class CurlHandlerOptionsSnapshotTests
{
    [Fact]
    public void Constructor_SnapshotsMutableOptions()
    {
        var cookieContainer = new CookieContainer();
        var headerOrder = new List<string> { "X-A", "X-B" };
        var credentials = new NetworkCredential("user", "pass", "domain");
        var options = new CurlHandlerOptions
        {
            BrowserProfile = BrowserProfile.Firefox147,
            HeaderPolicy = BrowserHeaderPolicy.DisableBrowserHeaders,
            HeaderOrder = headerOrder,
            CookieContainer = cookieContainer,
            ProxyCredentials = credentials
        };
        options.Fingerprint.Http2StreamWeight = 32;

        using var handler = new CurlHandler(options);
        var snapshot = GetOptions(handler);

        options.BrowserProfile = (BrowserProfile)999;
        options.HeaderPolicy = BrowserHeaderPolicy.AllowUserOverride;
        options.CookieContainer = null!;
        options.Fingerprint.Http2StreamWeight = 0;
        headerOrder[0] = "X-Changed";
        credentials.UserName = "changed";

        Assert.NotSame(options, snapshot);
        Assert.Equal(BrowserProfile.Firefox147, snapshot.BrowserProfile);
        Assert.Equal(BrowserHeaderPolicy.DisableBrowserHeaders, snapshot.HeaderPolicy);
        Assert.Equal(["X-A", "X-B"], snapshot.HeaderOrder);
        Assert.Same(cookieContainer, snapshot.CookieContainer);
        Assert.Equal(32, snapshot.Fingerprint.Http2StreamWeight);
        Assert.NotSame(credentials, snapshot.ProxyCredentials);
        Assert.Equal("user", snapshot.ProxyCredentials?.UserName);
    }

    private static CurlHandlerOptions GetOptions(CurlHandler handler)
    {
        var field = typeof(CurlHandler).GetField(
            "_options",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return Assert.IsType<CurlHandlerOptions>(field.GetValue(handler));
    }
}
