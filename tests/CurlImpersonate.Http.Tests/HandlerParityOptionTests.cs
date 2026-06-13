using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class HandlerParityOptionTests
{
    [Fact]
    public void AllowAutoRedirect_MirrorsFollowRedirects()
    {
        var options = new CurlHandlerOptions { AllowAutoRedirect = false };
        Assert.False(options.FollowRedirects);

        options.FollowRedirects = true;
        Assert.True(options.AllowAutoRedirect);
    }

    [Fact]
    public void MaxAutomaticRedirections_MirrorsMaxRedirects()
    {
        var options = new CurlHandlerOptions { MaxAutomaticRedirections = 7 };
        Assert.Equal(7, options.MaxRedirects);

        options.MaxRedirects = 3;
        Assert.Equal(3, options.MaxAutomaticRedirections);
    }

    [Fact]
    public void MaxConnectionsPerServer_MirrorsMaxConnectionsPerHost()
    {
        var options = new CurlHandlerOptions();
        Assert.Equal(int.MaxValue, options.MaxConnectionsPerServer);

        options.MaxConnectionsPerServer = 8;
        Assert.Equal(8, options.MaxConnectionsPerHost);

        options.MaxConnectionsPerServer = int.MaxValue;
        Assert.Null(options.MaxConnectionsPerHost);
    }
}
