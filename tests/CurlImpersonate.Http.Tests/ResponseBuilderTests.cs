using System.Net;
using CurlImpersonate.Http.Internal;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class ResponseBuilderTests
{
    [Fact]
    public void Build_WithAutomaticDecompression_StripsCompressedContentHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = ResponseBuilder.Build(
            new CurlResponse(
                200,
                [1, 2, 3],
                [
                    "Content-Encoding: gzip",
                    "Content-Length: 42",
                    "Content-Type: text/plain"
                ]),
            request,
            automaticDecompression: true);

        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal(3, response.Content.Headers.ContentLength);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Build_WithAutomaticDecompression_StripsWhenAnyEncodingIsCompressed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = ResponseBuilder.Build(
            new CurlResponse(
                200,
                [1, 2, 3],
                [
                    "Content-Encoding: identity",
                    "Content-Encoding: gzip",
                    "Content-Length: 42"
                ]),
            request,
            automaticDecompression: true);

        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal(3, response.Content.Headers.ContentLength);
    }

    [Fact]
    public void Build_WithoutAutomaticDecompression_PreservesCompressedContentHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = ResponseBuilder.Build(
            new CurlResponse(
                200,
                [1, 2, 3],
                [
                    "Content-Encoding: gzip",
                    "Content-Length: 42"
                ]),
            request,
            automaticDecompression: false);

        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        Assert.Equal(42, response.Content.Headers.ContentLength);
    }

    [Fact]
    public void Build_SetsResponseVersion()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = ResponseBuilder.Build(
            new CurlResponse(200, [], [], HttpVersion.Version20),
            request);

        Assert.Equal(HttpVersion.Version20, response.Version);
    }

    [Fact]
    public void Build_SetsCurlResponseOptions()
    {
        var effectiveUri = new Uri("https://example.test/final");
        var metrics = new CurlTransferMetrics(
            TotalTime: TimeSpan.FromMilliseconds(10),
            NameLookupTime: TimeSpan.FromMilliseconds(1),
            ConnectTime: TimeSpan.FromMilliseconds(2),
            TlsHandshakeTime: TimeSpan.FromMilliseconds(3),
            PreTransferTime: TimeSpan.FromMilliseconds(4),
            StartTransferTime: TimeSpan.FromMilliseconds(5),
            RedirectTime: TimeSpan.FromMilliseconds(6),
            NewConnectionCount: 1,
            RequestHeaderBytes: 123,
            ResponseHeaderBytes: 456,
            PrimaryIp: "203.0.113.10",
            PrimaryPort: 443,
            LocalIp: "192.0.2.20",
            LocalPort: 54321);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/start");
        using var response = ResponseBuilder.Build(
            new CurlResponse(200, [], [], HttpVersion.Version20, effectiveUri, RedirectCount: 2, metrics),
            request);

        Assert.True(response.TryGetEffectiveUri(out var actualEffectiveUri));
        Assert.Equal(effectiveUri, actualEffectiveUri);
        Assert.True(response.TryGetRedirectCount(out var redirectCount));
        Assert.Equal(2, redirectCount);
        Assert.True(response.TryGetTransferMetrics(out var actualMetrics));
        Assert.Equal(metrics, actualMetrics);
    }
}
